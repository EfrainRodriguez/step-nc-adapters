using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StepNc.Adapters;

internal static class AdditiveJsonParser
{
    public static AdditiveJsonParseResult ParseFile(string inputJsonPath)
    {
        if (string.IsNullOrWhiteSpace(inputJsonPath))
        {
            return new AdditiveJsonParseResult { Success = false, Message = "Input JSON path is required." };
        }

        if (!File.Exists(inputJsonPath))
        {
            return new AdditiveJsonParseResult
            {
                Success = false,
                Message = $"Input JSON file was not found: {inputJsonPath}"
            };
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(inputJsonPath));
            var root = document.RootElement;

            if (!root.TryGetProperty("layers", out var layersElement) || layersElement.ValueKind != JsonValueKind.Array)
            {
                return new AdditiveJsonParseResult { Success = false, Message = "JSON does not contain a layers array." };
            }

            var layers = new List<AdditiveJsonLayer>();
            var eventCount = 0;
            var extrusionEventCount = 0;
            var travelEventCount = 0;
            var pointCount = 0;

            foreach (var layerElement in layersElement.EnumerateArray())
            {
                var layer = ParseLayer(layerElement, layers.Count, ref eventCount, ref extrusionEventCount, ref travelEventCount, ref pointCount);
                layers.Add(layer);
            }

            if (layers.Count == 0)
            {
                return new AdditiveJsonParseResult { Success = false, Message = "No layers were found in JSON." };
            }

            var program = new AdditiveJsonProgram
            {
                SchemaVersion = GetString(root, "schema_version"),
                GeneratorName = root.TryGetProperty("generator", out var generator) ? GetString(generator, "name") : string.Empty,
                GeneratorVersion = root.TryGetProperty("generator", out generator) ? GetString(generator, "version") : string.Empty,
                PrintConfig = root.TryGetProperty("print_config", out var printConfig) ? ParseStringMap(printConfig) : new Dictionary<string, string>(),
                Layers = layers
                    .OrderBy(layer => layer.LayerSeqId)
                    .ThenBy(layer => layer.PrintZ)
                    .ToArray(),
                SkirtPaths = ParsePrintLevelPaths(root, "skirt"),
                BrimPaths = ParsePrintLevelPaths(root, "brim")
            };

            return new AdditiveJsonParseResult
            {
                Success = true,
                Message = "Additive JSON parsed successfully.",
                Program = program,
                LayerCount = layers.Count,
                EventCount = eventCount,
                ExtrusionEventCount = extrusionEventCount,
                TravelEventCount = travelEventCount,
                PointCount = pointCount
            };
        }
        catch (JsonException ex)
        {
            return new AdditiveJsonParseResult { Success = false, Message = $"Failed to parse additive JSON: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new AdditiveJsonParseResult { Success = false, Message = $"Failed to parse additive JSON: {ex.Message}" };
        }
    }

    private static AdditiveJsonLayer ParseLayer(
        JsonElement layerElement,
        int fallbackLayerIndex,
        ref int eventCount,
        ref int extrusionEventCount,
        ref int travelEventCount,
        ref int pointCount)
    {
        var printZ = GetDouble(layerElement, "print_z") ?? GetDouble(layerElement, "slice_z") ?? 0.0;
        var copyOffset = ParseCopyOffset(layerElement);
        var events = new List<AdditiveJsonEvent>();

        if (layerElement.TryGetProperty("events", out var eventsElement) && eventsElement.ValueKind == JsonValueKind.Array)
        {
            var eventIndex = 0;
            foreach (var eventElement in eventsElement.EnumerateArray())
            {
                var parsedEvent = ParseEvent(eventElement, eventIndex, printZ);
                events.Add(parsedEvent);
                eventCount++;
                pointCount += parsedEvent.Points.Count;

                if (parsedEvent.IsTravel)
                {
                    travelEventCount++;
                }
                else if (parsedEvent.IsExtrusion)
                {
                    extrusionEventCount++;
                }

                eventIndex++;
            }
        }

        return new AdditiveJsonLayer
        {
            LayerId = GetInt(layerElement, "layer_id") ?? fallbackLayerIndex,
            LayerSeqId = GetInt(layerElement, "layer_seq_id") ?? fallbackLayerIndex,
            ObjectIndex = GetInt(layerElement, "object_index") ?? 0,
            CopyIndex = GetInt(layerElement, "copy_index") ?? 0,
            CopyOffsetX = copyOffset.x,
            CopyOffsetY = copyOffset.y,
            PrintZ = printZ,
            Height = GetDouble(layerElement, "height") ?? 0.0,
            IsRaftLayer = GetBool(layerElement, "is_raft_layer"),
            IsSupportLayer = GetBool(layerElement, "is_support_layer"),
            Process = layerElement.TryGetProperty("process", out var process) ? ParseStringMap(process) : new Dictionary<string, string>(),
            RegionConfig = ParseFirstRegionConfig(layerElement),
            Events = events
        };
    }

    private static AdditiveJsonEvent ParseEvent(JsonElement eventElement, int eventIndex, double fallbackZ)
    {
        var featureType = GetString(eventElement, "feature_type");
        var eventType = GetString(eventElement, "type");
        var z = GetDouble(eventElement, "layer_print_z") ?? fallbackZ;
        var points = eventElement.TryGetProperty("points", out var pointsElement)
            ? ParsePoints(pointsElement, z, $"E{eventIndex}")
            : Array.Empty<ToolpathPoint>();

        return new AdditiveJsonEvent
        {
            EventIndex = eventIndex,
            Type = eventType,
            FeatureType = featureType,
            RoleName = GetString(eventElement, "role_name"),
            Source = GetString(eventElement, "source"),
            RoleId = GetInt(eventElement, "role_id"),
            RegionId = GetInt(eventElement, "region_id"),
            LayerPrintZ = z,
            Height = GetDouble(eventElement, "height") ?? 0.0,
            Width = GetDouble(eventElement, "width"),
            Mm3PerMm = GetDouble(eventElement, "mm3_per_mm"),
            IsBridge = GetBool(eventElement, "is_bridge"),
            IsSolidInfill = GetBool(eventElement, "is_solid_infill"),
            Points = points
        };
    }

    private static IReadOnlyList<AdditiveJsonPath> ParsePrintLevelPaths(JsonElement root, string pathGroupName)
    {
        if (!root.TryGetProperty("print_level_toolpaths", out var printLevel)
            || !printLevel.TryGetProperty(pathGroupName, out var groupElement)
            || groupElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AdditiveJsonPath>();
        }

        var paths = new List<AdditiveJsonPath>();

        foreach (var group in groupElement.EnumerateArray())
        {
            var z = GetDouble(group, "layer_print_z") ?? 0.0;
            var featureType = GetString(group, "feature_type");
            var roleName = GetString(group, "role_name");
            var type = GetString(group, "type");

            if (!group.TryGetProperty("paths", out var pathsElement) || pathsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var pathElement in pathsElement.EnumerateArray())
            {
                var pathZ = GetDouble(pathElement, "layer_print_z") ?? z;
                var points = pathElement.TryGetProperty("points", out var pointsElement)
                    ? ParsePoints(pointsElement, pathZ, $"{pathGroupName}{paths.Count}")
                    : ParsePoints(pathElement, pathZ, $"{pathGroupName}{paths.Count}");

                if (points.Count == 0)
                {
                    continue;
                }

                paths.Add(new AdditiveJsonPath
                {
                    PathIndex = paths.Count,
                    Type = string.IsNullOrWhiteSpace(type) ? "extrusion_path" : type,
                    FeatureType = string.IsNullOrWhiteSpace(featureType) ? pathGroupName : featureType,
                    RoleName = string.IsNullOrWhiteSpace(roleName) ? pathGroupName : roleName,
                    LayerPrintZ = pathZ,
                    Points = points
                });
            }
        }

        return paths;
    }

    private static IReadOnlyDictionary<string, string> ParseFirstRegionConfig(JsonElement layerElement)
    {
        if (!layerElement.TryGetProperty("regions", out var regions) || regions.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>();
        }

        foreach (var region in regions.EnumerateArray())
        {
            if (region.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
            {
                return ParseStringMap(config);
            }
        }

        return new Dictionary<string, string>();
    }

    private static IReadOnlyList<ToolpathPoint> ParsePoints(JsonElement pointsElement, double z, string prefix)
    {
        if (pointsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ToolpathPoint>();
        }

        var points = new List<ToolpathPoint>();
        var pointIndex = 0;

        foreach (var pointElement in pointsElement.EnumerateArray())
        {
            if (pointElement.ValueKind != JsonValueKind.Array || pointElement.GetArrayLength() < 2)
            {
                continue;
            }

            var x = GetArrayDouble(pointElement, 0);
            var y = GetArrayDouble(pointElement, 1);
            if (x == null || y == null)
            {
                continue;
            }

            points.Add(new ToolpathPoint($"{prefix}-P{pointIndex + 1}", x.Value, y.Value, z));
            pointIndex++;
        }

        return points;
    }

    private static IReadOnlyDictionary<string, string> ParseStringMap(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => property.Value.GetRawText()
            };
        }

        return values;
    }

    private static (double x, double y) ParseCopyOffset(JsonElement layerElement)
    {
        if (!layerElement.TryGetProperty("copy_offset", out var offset)
            || offset.ValueKind != JsonValueKind.Array
            || offset.GetArrayLength() < 2)
        {
            return (0.0, 0.0);
        }

        return (GetArrayDouble(offset, 0) ?? 0.0, GetArrayDouble(offset, 1) ?? 0.0);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return GetDouble(value);
    }

    private static double? GetArrayDouble(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() <= index)
        {
            return null;
        }

        return GetDouble(element[index]);
    }

    private static double? GetDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }
}
