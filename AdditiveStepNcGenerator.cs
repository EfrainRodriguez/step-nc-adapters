using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using STEPNCLib;

namespace StepNc.Adapters;

internal static class AdditiveStepNcGenerator
{
    public static StepNcResult GenerateFromLayers(
        IReadOnlyList<AdditiveLayer> layers,
        string outputPath,
        AdditiveConversionOptions options = null)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new StepNcResult { Success = false, Message = "Output path is required." };
        }

        if (layers == null || layers.Count == 0)
        {
            return new StepNcResult { Success = false, OutputPath = outputPath, Message = "No valid layers were found in XML." };
        }

        var totalPoints = layers.SelectMany(layer => layer.Polylines).Sum(polyline => polyline.Points.Count);
        if (totalPoints == 0)
        {
            return new StepNcResult
            {
                Success = false,
                OutputPath = outputPath,
                Message = "No toolpath points were found in parsed additive layers."
            };
        }

        var opt = options ?? new AdditiveConversionOptions();
        AptStepMaker stnc = null;

        try
        {
            stnc = new AptStepMaker();
            stnc.NewProjectWithCCandWP(opt.ProjectName, opt.ContextId, opt.MainWorkplanName);
            stnc.Millimeters();
            stnc.DefineTool(1.75, 1.75 / 2, 10.0, 10.0, 1.0, 0.0, 45.0);

            foreach (var layer in layers)
            {
                stnc.NestWorkplan($"Additive Layer-{layer.LayerNo}");

                foreach (var polyline in layer.Polylines)
                {
                    stnc.LoadTool(1);
                    stnc.Workingstep($"Additive WS({polyline.PolylineType})-{polyline.PolylineId}");

                    var travel = true;
                    foreach (var point in polyline.Points)
                    {
                        if (polyline.PolylineType == "contour-open")
                        {
                            if (travel)
                            {
                                stnc.Rapid();
                                stnc.GoToXYZ(polyline.PolylineType, point.X, point.Y, point.Z);
                                stnc.Feedrate(opt.Feedrate);
                                stnc.SpindleSpeed(opt.SpindleSpeed);
                                travel = false;
                            }
                            else
                            {
                                stnc.GoToXYZ(polyline.PolylineType, point.X, point.Y, point.Z);
                            }
                        }
                        else if (polyline.PolylineType == "hatch")
                        {
                            if (travel)
                            {
                                stnc.Rapid();
                                stnc.GoToXYZ(polyline.PolylineType, point.X, point.Y, point.Z);
                                stnc.Feedrate(opt.Feedrate);
                                stnc.SpindleSpeed(opt.SpindleSpeed);
                                travel = false;
                            }
                            else
                            {
                                stnc.GoToXYZ(polyline.PolylineType, point.X, point.Y, point.Z);
                                travel = true;
                            }
                        }
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
                Message = "Additive STEP-NC file generated successfully."
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
                Message = $"Failed to write additive STEP-NC file: {ex.Message}"
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
}
