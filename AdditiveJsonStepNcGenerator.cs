using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using STEPNCLib;

namespace StepNc.Adapters;

internal static class AdditiveJsonStepNcGenerator
{
    private const string ExtrusionMappingProperty = "spindle_rpm_represents_mm3_per_min";

    public static StepNcResult GenerateFromProgram(
        AdditiveJsonProgram program,
        string outputPath,
        AdditiveJsonConversionOptions options = null)
    {
        if (program == null)
        {
            return new StepNcResult { Success = false, Message = "Additive JSON program data is required." };
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new StepNcResult { Success = false, Message = "Output path is required." };
        }

        if (program.Layers == null || program.Layers.Count == 0)
        {
            return new StepNcResult { Success = false, OutputPath = outputPath, Message = "No layers were found in additive JSON." };
        }

        var eventPointCount = program.Layers.SelectMany(layer => layer.Events).Sum(ev => ev.Points.Count);
        var printLevelPointCount = (program.SkirtPaths?.Sum(path => path.Points.Count) ?? 0)
            + (program.BrimPaths?.Sum(path => path.Points.Count) ?? 0);

        if (eventPointCount + printLevelPointCount == 0)
        {
            return new StepNcResult
            {
                Success = false,
                OutputPath = outputPath,
                Message = "No toolpath points were found in parsed additive JSON."
            };
        }

        var opt = options ?? new AdditiveJsonConversionOptions();
        AptStepMaker stnc = null;

        try
        {
            stnc = new AptStepMaker();
            stnc.NewProjectWithCCandWP(opt.ProjectName, opt.ContextId, opt.MainWorkplanName);
            stnc.Millimeters();
            TryCall(stnc, "FeedrateUnit", "mmpm");
            TryCall(stnc, "SpindleSpeedUnit", "rpm");
            stnc.CamModeOn();
            stnc.SetModeMill();

            var nozzleDiameter = ResolvePositiveDouble(program.PrintConfig, "nozzle_diameter") ?? opt.DefaultNozzleDiameter;
            stnc.DefineTool(nozzleDiameter, nozzleDiameter / 2.0, 10.0, 10.0, 1.0, 0.0, 45.0);
            stnc.LoadTool(1);

            EmitProgramMetadata(stnc, program);

            if (opt.EmitSkirtAndBrim)
            {
                EmitPrintLevelPaths(stnc, program.SkirtPaths, "skirt", opt, program.PrintConfig);
                EmitPrintLevelPaths(stnc, program.BrimPaths, "brim", opt, program.PrintConfig);
            }

            foreach (var layer in program.Layers.OrderBy(layer => layer.LayerSeqId).ThenBy(layer => layer.PrintZ))
            {
                if (!HasEmittableEvents(layer, opt))
                {
                    continue;
                }

                stnc.NestWorkplan($"Layer {layer.LayerSeqId:000} Z{layer.PrintZ.ToString("0.###", CultureInfo.InvariantCulture)}");

                foreach (var pathEvent in layer.Events)
                {
                    if (pathEvent.IsTravel)
                    {
                        if (opt.EmitTravels)
                        {
                            EmitTravelEvent(stnc, layer, pathEvent, opt);
                        }

                        continue;
                    }

                    if (pathEvent.IsExtrusion)
                    {
                        EmitExtrusionEvent(stnc, program, layer, pathEvent, opt);
                    }
                }

                stnc.EndWorkplan();
            }

            if (opt.SaveAsP21)
            {
                stnc.SaveAsP21(outputPath);
            }
            else
            {
                stnc.SaveFastAsModules(outputPath);
            }

            return new StepNcResult
            {
                Success = true,
                OutputPath = outputPath,
                Message = "Additive JSON STEP-NC file generated successfully."
            };
        }
        catch (SEHException ex)
        {
            return new StepNcResult
            {
                Success = false,
                OutputPath = outputPath,
                Message = $"Native STEP-NC DLL error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new StepNcResult
            {
                Success = false,
                OutputPath = outputPath,
                Message = $"Failed to write additive JSON STEP-NC file: {ex.Message}"
            };
        }
        finally
        {
            if (stnc != null)
            {
                try { Marshal.FinalReleaseComObject(stnc); } catch { }
            }
        }
    }

    private static bool HasEmittableEvents(AdditiveJsonLayer layer, AdditiveJsonConversionOptions options)
    {
        return layer.Events.Any(pathEvent => pathEvent.Points.Count >= 2
            && (pathEvent.IsExtrusion || (pathEvent.IsTravel && options.EmitTravels)));
    }

    private static void EmitProgramMetadata(AptStepMaker stnc, AdditiveJsonProgram program)
    {
        TryCall(stnc, "PPrint", $"Source: additive JSON; schema={program.SchemaVersion}; generator={program.GeneratorName} {program.GeneratorVersion}".Trim());

        if (TryGetString(program.PrintConfig, "temperature", out var temperature))
        {
            TryCall(stnc, "PPrint", $"Extruder temperature C: {temperature}");
        }

        if (TryGetString(program.PrintConfig, "bed_temperature", out var bedTemperature))
        {
            TryCall(stnc, "PPrint", $"Bed temperature C: {bedTemperature}");
        }

        TryCall(stnc, "PPrint", $"Extrusion mapping: {ExtrusionMappingProperty}");
    }

    private static void EmitPrintLevelPaths(
        AptStepMaker stnc,
        IReadOnlyList<AdditiveJsonPath> paths,
        string groupName,
        AdditiveJsonConversionOptions options,
        IReadOnlyDictionary<string, string> printConfig)
    {
        if (paths == null || paths.Count == 0)
        {
            return;
        }

        stnc.NestWorkplan(groupName);

        foreach (var path in paths)
        {
            if (path.Points.Count < 2)
            {
                continue;
            }

            stnc.Workingstep($"{groupName} {path.PathIndex:000}");
            TryCall(stnc, "SetPathTypeTrajectory");

            var feedrate = ResolveSpeedValue(printConfig, groupName + "_speed", options.DefaultFeedrate) ?? options.DefaultFeedrate;
            stnc.Feedrate(feedrate);
            stnc.SpindleSpeed(options.DefaultSpindleSpeed);

            EmitPoints(stnc, path.Points, groupName, 0.0, 0.0, applyOffset: false);
        }

        stnc.EndWorkplan();
    }

    private static void EmitTravelEvent(AptStepMaker stnc, AdditiveJsonLayer layer, AdditiveJsonEvent pathEvent, AdditiveJsonConversionOptions options)
    {
        if (pathEvent.Points.Count < 2)
        {
            return;
        }

        var name = FormatWorkingstepName(layer, pathEvent);
        stnc.Workingstep(name);
        TryCall(stnc, "SetPathTypeConnect");
        stnc.Rapid();
        AddWorkingstepProperties(stnc, layer, pathEvent, null, null, options);
        EmitPoints(stnc, pathEvent.Points, LabelFor(pathEvent), layer.CopyOffsetX, layer.CopyOffsetY, options.ApplyCopyOffset);
    }

    private static void EmitExtrusionEvent(
        AptStepMaker stnc,
        AdditiveJsonProgram program,
        AdditiveJsonLayer layer,
        AdditiveJsonEvent pathEvent,
        AdditiveJsonConversionOptions options)
    {
        if (pathEvent.Points.Count < 2)
        {
            return;
        }

        var name = FormatWorkingstepName(layer, pathEvent);
        var feedrate = ResolveEventFeedrate(program, layer, pathEvent, options);
        var spindleSpeed = ResolveSpindleSpeed(pathEvent, feedrate, options);

        stnc.Workingstep(name);
        TryCall(stnc, "SetPathTypeTrajectory");
        TryCall(stnc, "SetPathPriorityRequired");
        stnc.Feedrate(feedrate);
        stnc.SpindleSpeed(spindleSpeed);
        AddWorkingstepProperties(stnc, layer, pathEvent, feedrate, spindleSpeed, options);
        EmitPoints(stnc, pathEvent.Points, LabelFor(pathEvent), layer.CopyOffsetX, layer.CopyOffsetY, options.ApplyCopyOffset);
    }

    private static void EmitPoints(
        AptStepMaker stnc,
        IReadOnlyList<ToolpathPoint> points,
        string label,
        double offsetX,
        double offsetY,
        bool applyOffset)
    {
        foreach (var point in points)
        {
            var x = applyOffset ? point.X + offsetX : point.X;
            var y = applyOffset ? point.Y + offsetY : point.Y;
            stnc.GoToXYZ(label, x, y, point.Z);
        }
    }

    private static double ResolveEventFeedrate(
        AdditiveJsonProgram program,
        AdditiveJsonLayer layer,
        AdditiveJsonEvent pathEvent,
        AdditiveJsonConversionOptions options)
    {
        var speed = pathEvent.FeatureType switch
        {
            "perimeter_external" => ResolveFeatureSpeed(layer, program.PrintConfig, "external_perimeter_speed", "perimeter_speed", options.DefaultFeedrate),
            "perimeter_internal" => ResolveFeatureSpeed(layer, program.PrintConfig, "print_speed_perimeter", "perimeter_speed", options.DefaultFeedrate),
            "infill" => ResolveFeatureSpeed(layer, program.PrintConfig, "print_speed_infill", "infill_speed", options.DefaultFeedrate),
            "infill_solid" => ResolveFeatureSpeed(layer, program.PrintConfig, "print_speed_solid", "solid_infill_speed", options.DefaultFeedrate),
            "infill_top_solid" => ResolveFeatureSpeed(layer, program.PrintConfig, "print_speed_top_solid", "top_solid_infill_speed", options.DefaultFeedrate),
            "support" => ResolveFeatureSpeed(layer, program.PrintConfig, "support_speed", "support_speed", options.DefaultFeedrate),
            "support_interface" => ResolveFeatureSpeed(layer, program.PrintConfig, "support_interface_speed", "support_speed", options.DefaultFeedrate),
            "bridge" => ResolveFeatureSpeed(layer, program.PrintConfig, "bridge_speed", "bridge_speed", options.DefaultFeedrate),
            "gap_fill" => ResolveFeatureSpeed(layer, program.PrintConfig, "gap_fill_speed", "gap_fill_speed", options.DefaultFeedrate),
            _ => ResolveFeatureSpeed(layer, program.PrintConfig, "print_speed_infill", "infill_speed", options.DefaultFeedrate)
        };

        if (layer.LayerSeqId == 0 && TryGetString(program.PrintConfig, "first_layer_speed", out var firstLayerSpeed))
        {
            speed = ResolveSpeedLiteral(firstLayerSpeed, speed) ?? speed;
        }

        return speed <= 0.0 ? options.DefaultFeedrate : speed;
    }

    private static double ResolveFeatureSpeed(
        AdditiveJsonLayer layer,
        IReadOnlyDictionary<string, string> printConfig,
        string processKey,
        string regionKey,
        double fallback)
    {
        var baseSpeed = ResolveSpeedValue(layer.RegionConfig, regionKey, fallback)
            ?? ResolveSpeedValue(printConfig, regionKey, fallback)
            ?? ResolveSpeedValue(printConfig, "max_print_speed", fallback)
            ?? fallback;

        return ResolveSpeedValue(layer.Process, processKey, baseSpeed)
            ?? ResolveSpeedValue(layer.RegionConfig, regionKey, fallback)
            ?? ResolveSpeedValue(printConfig, regionKey, fallback)
            ?? ResolveSpeedValue(printConfig, "max_print_speed", fallback)
            ?? fallback;
    }

    private static double? ResolveSpeedValue(IReadOnlyDictionary<string, string> values, string key, double baseSpeed)
    {
        return TryGetString(values, key, out var raw) ? ResolveSpeedLiteral(raw, baseSpeed) : null;
    }

    private static double? ResolveSpeedLiteral(string raw, double baseSpeed)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim().Trim('"');
        if (raw.EndsWith("%", StringComparison.Ordinal))
        {
            var percentText = raw[..^1];
            if (double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                return baseSpeed * percent / 100.0;
            }

            return null;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value * 60.0;
        }

        return null;
    }

    private static double ResolveSpindleSpeed(AdditiveJsonEvent pathEvent, double feedrate, AdditiveJsonConversionOptions options)
    {
        if (!options.UseVolumetricFlowAsSpindle)
        {
            return options.DefaultSpindleSpeed;
        }

        if (pathEvent.Mm3PerMm is > 0.0)
        {
            return pathEvent.Mm3PerMm.Value * feedrate;
        }

        if (pathEvent.Width is > 0.0 && pathEvent.Height > 0.0)
        {
            return pathEvent.Width.Value * pathEvent.Height * feedrate;
        }

        return options.DefaultSpindleSpeed;
    }

    private static void AddWorkingstepProperties(
        AptStepMaker stnc,
        AdditiveJsonLayer layer,
        AdditiveJsonEvent pathEvent,
        double? feedrate,
        double? spindleSpeed,
        AdditiveJsonConversionOptions options)
    {
        if (!options.AddWorkingstepProperties)
        {
            return;
        }

        var workingstepId = TryCall(stnc, "GetCurrentWorkingstep");
        if (workingstepId == null)
        {
            return;
        }

        TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "feature_type", pathEvent.FeatureType);
        TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "role_name", pathEvent.RoleName);
        TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "source", pathEvent.Source);
        TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "path_type", pathEvent.Type);
        TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "extrusion_mapping", ExtrusionMappingProperty);
        TryCall(stnc, "WorkingstepAddPropertyCountMeasure", workingstepId, "layer_seq_id", layer.LayerSeqId);
        TryCall(stnc, "WorkingstepAddPropertyCountMeasure", workingstepId, "event_index", pathEvent.EventIndex);

        if (pathEvent.Width is > 0.0)
        {
            TryCall(stnc, "WorkingstepAddPropertyLengthMeasure", workingstepId, "path_width", pathEvent.Width.Value);
        }

        if (pathEvent.Height > 0.0)
        {
            TryCall(stnc, "WorkingstepAddPropertyLengthMeasure", workingstepId, "layer_height", pathEvent.Height);
        }

        if (pathEvent.Mm3PerMm is > 0.0)
        {
            TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "mm3_per_mm", pathEvent.Mm3PerMm.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (feedrate is > 0.0)
        {
            TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "feedrate_mm_per_min", feedrate.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (spindleSpeed is >= 0.0)
        {
            TryCall(stnc, "WorkingstepAddPropertyDescriptiveMeasure", workingstepId, "spindle_rpm", spindleSpeed.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string FormatWorkingstepName(AdditiveJsonLayer layer, AdditiveJsonEvent pathEvent)
    {
        return $"L{layer.LayerSeqId:000} E{pathEvent.EventIndex:0000} {LabelFor(pathEvent)}";
    }

    private static string LabelFor(AdditiveJsonEvent pathEvent)
    {
        if (!string.IsNullOrWhiteSpace(pathEvent.FeatureType))
        {
            return pathEvent.FeatureType;
        }

        if (!string.IsNullOrWhiteSpace(pathEvent.RoleName))
        {
            return pathEvent.RoleName;
        }

        return string.IsNullOrWhiteSpace(pathEvent.Type) ? "path" : pathEvent.Type;
    }

    private static bool TryGetString(IReadOnlyDictionary<string, string> values, string key, out string value)
    {
        value = null;
        return values != null && values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);
    }

    private static double? ResolvePositiveDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!TryGetString(values, key, out var raw))
        {
            return null;
        }

        raw = raw.Trim().Trim('"');
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0.0
            ? value
            : null;
    }

    private static object TryCall(object target, string methodName, params object[] args)
    {
        try
        {
            foreach (var method in target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                    && candidate.GetParameters().Length == args.Length))
            {
                if (TryConvertArguments(method.GetParameters(), args, out var convertedArgs))
                {
                    return method.Invoke(target, convertedArgs);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryConvertArguments(ParameterInfo[] parameters, object[] args, out object[] convertedArgs)
    {
        convertedArgs = new object[args.Length];

        for (var i = 0; i < args.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            var arg = args[i];

            if (arg == null)
            {
                convertedArgs[i] = null;
                continue;
            }

            if (parameterType.IsInstanceOfType(arg))
            {
                convertedArgs[i] = arg;
                continue;
            }

            try
            {
                convertedArgs[i] = Convert.ChangeType(arg, parameterType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }
}
