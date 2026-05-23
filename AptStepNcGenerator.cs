using System;
using System.Runtime.InteropServices;
using STEPNCLib;

namespace StepNc.Adapters;

internal static class AptStepNcGenerator
{
    public static StepNcResult GenerateFromProgram(AptProgram program, string outputPath, AptConversionOptions options = null)
    {
        if (program == null)
        {
            return new StepNcResult { Success = false, Message = "APT program data is required." };
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new StepNcResult { Success = false, Message = "Output path is required." };
        }

        var opt = options ?? new AptConversionOptions();
        AptStepMaker stnc = null;

        try
        {
            stnc = new AptStepMaker();
            var feature = new Feature();
            var process = new Process();

            stnc.NewProjectWithCCandWP(program.PartName, 1, opt.MainWorkplanName);
            process.BlockRawpiece(
                opt.RawpieceName,
                opt.RawpieceX,
                opt.RawpieceY,
                opt.RawpieceZ,
                opt.RawpieceLength,
                opt.RawpieceWidth,
                opt.RawpieceHeight);

            stnc.Millimeters();

            if (program.MultaxOn)
            {
                stnc.MultaxOn();
            }

            stnc.CamModeOn();
            stnc.SetModeMill();

            foreach (var tool in program.Tools.Values)
            {
                var diameter = tool.Diameter <= 0.0 ? 1.0 : tool.Diameter;
                stnc.DefineTool(diameter, diameter / 2.0, 10.0, 10.0, 1.0, 0.0, 45.0);
            }

            foreach (var machineGroup in program.MachineGroups)
            {
                stnc.NestWorkplan($"Machine Group-{machineGroup.GroupId}");

                foreach (var operation in machineGroup.Operations)
                {
                    if (operation.ToolId >= 0)
                    {
                        stnc.LoadTool(operation.ToolId);
                    }

                    stnc.Workingstep($"WS-{operation.OperationId}");
                    var rapidMode = false;

                    foreach (var command in operation.Commands)
                    {
                        switch (command)
                        {
                            case AptRapidCommand:
                                // In APT, RAPID is typically modal until a feed move is set.
                                rapidMode = true;
                                break;
                            case AptGoToCommand move:
                                if (rapidMode)
                                {
                                    stnc.Rapid();
                                }
                                stnc.GoToXYZ("point", move.X, move.Y, move.Z);
                                break;
                            case AptCircleCommand arc:
                                // Arc moves are machining moves, not rapid traverses.
                                rapidMode = false;
                                stnc.ArcXYPlane("arc", arc.X, arc.Y, arc.Z, arc.Cx, arc.Cy, arc.Cz, arc.Radius, arc.Clockwise);
                                break;
                            case AptFeedrateCommand feed:
                                // Feed command exits rapid traversal mode.
                                rapidMode = false;
                                stnc.Feedrate(feed.Feedrate);
                                break;
                            case AptSpindleSpeedCommand spindle:
                                stnc.SpindleSpeed(spindle.Speed);
                                break;
                            case AptCoolantCommand coolant:
                                if (coolant.Enabled)
                                {
                                    stnc.CoolantOn();
                                }
                                else
                                {
                                    stnc.CoolantOff();
                                }

                                break;
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
                Message = "APT STEP-NC file generated successfully."
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
                Message = $"Failed to generate STEP-NC from APT: {ex.Message}"
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
