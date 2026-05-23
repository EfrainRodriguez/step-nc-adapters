using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using STEPNCLib;

namespace StepNc.Adapters;

internal static class SimpleStepNcGenerator
{
    public static StepNcResult Generate(string outputPath, SimpleProgramOptions options = null)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new StepNcResult { Success = false, Message = "Output path is required." };
        }

        var opt = options ?? new SimpleProgramOptions();
        AptStepMaker apt = null;

        try
        {
            apt = new AptStepMaker();
            apt.PartNo(opt.PartName);
            apt.DefineTool(10.0, 2.5, 1.0, 2.0, 3.0, 4.0, 5.0);
            apt.LoadTool(1);
            apt.Feedrate(opt.Feedrate);
            apt.SpindleSpeed(opt.SpindleSpeed);
            apt.CoolantOn();
            apt.Workingstep(opt.WorkingstepName);

            foreach (var point in GetToolpathPoints(opt.ToolpathPoints))
            {
                apt.GoToXYZ(point.Name, point.X, point.Y, point.Z);
            }

            apt.SaveAsP21(outputPath);

            return new StepNcResult
            {
                Success = true,
                OutputPath = outputPath,
                Message = "STEP-NC file generated successfully."
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
                Message = $"Failed to generate STEP-NC file: {ex.Message}"
            };
        }
        finally
        {
            if (apt != null)
            {
                try { Marshal.FinalReleaseComObject(apt); } catch { }
            }
        }
    }

    private static IReadOnlyList<ToolpathPoint> GetToolpathPoints(IReadOnlyList<ToolpathPoint> customPoints)
    {
        if (customPoints != null && customPoints.Count > 0)
        {
            return customPoints;
        }

        return new[]
        {
            new ToolpathPoint("P1", 10.0, 10.0, 0.0),
            new ToolpathPoint("P2", 30.0, 10.0, 0.0),
            new ToolpathPoint("P3", 30.0, 30.0, 0.0),
            new ToolpathPoint("P4", 10.0, 30.0, 0.0),
            new ToolpathPoint("P5", 10.0, 10.0, 0.0)
        };
    }
}
