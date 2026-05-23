using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace StepNc.Adapters;

internal static class AptParser
{
    public static AptParseResult ParseMastercamFile(string inputAptPath)
    {
        if (string.IsNullOrWhiteSpace(inputAptPath))
        {
            return new AptParseResult { Success = false, Message = "Input APT path is required." };
        }

        if (!File.Exists(inputAptPath))
        {
            return new AptParseResult { Success = false, Message = $"Input APT file was not found: {inputAptPath}" };
        }

        try
        {
            var lines = File.ReadAllLines(inputAptPath)
                .Select(static line => NormalizeLine(line.Trim()))
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            return ParseMastercamLines(lines);
        }
        catch (Exception ex)
        {
            return new AptParseResult { Success = false, Message = $"Failed to parse APT: {ex.Message}" };
        }
    }

    private static AptParseResult ParseMastercamLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return new AptParseResult { Success = false, Message = "APT file is empty." };
        }

        var partName = "APT Program";
        var units = "MM";
        var multaxOn = false;
        var tools = new Dictionary<int, AptToolDefinition>();
        var groups = new List<AptMachineGroup>();

        var lineIndex = 0;
        var sawMachineGroup = false;
        while (lineIndex < lines.Count)
        {
            var line = lines[lineIndex];

            if (line.StartsWith("PARTNO/", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("PARTNO ", StringComparison.OrdinalIgnoreCase))
            {
                partName = line.Contains('/', StringComparison.Ordinal)
                    ? line[(line.IndexOf('/', StringComparison.Ordinal) + 1)..].Trim()
                    : line["PARTNO".Length..].Trim();
                lineIndex++;
                continue;
            }

            if (line.StartsWith("UNITS/", StringComparison.OrdinalIgnoreCase))
            {
                units = line["UNITS/".Length..].Trim();
                lineIndex++;
                continue;
            }

            if (line.StartsWith("MULTAX/", StringComparison.OrdinalIgnoreCase))
            {
                multaxOn = line["MULTAX/".Length..].Trim().Equals("ON", StringComparison.OrdinalIgnoreCase);
                lineIndex++;
                continue;
            }

            if (line.StartsWith("$$Machine Group-", StringComparison.OrdinalIgnoreCase))
            {
                sawMachineGroup = true;
                var groupId = TryParseInt(line[("$$Machine Group-").Length..], -1);
                var nextBoundary = FindNextMachineGroupOrFini(lines, lineIndex + 1);
                var groupLines = lines.Skip(lineIndex + 1).Take(nextBoundary - (lineIndex + 1)).ToList();
                var operations = ParseOperations(groupLines, tools);

                groups.Add(new AptMachineGroup { GroupId = groupId, Operations = operations });
                lineIndex = nextBoundary;
                continue;
            }

            if (line.StartsWith("FINI", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            lineIndex++;
        }

        if (!sawMachineGroup)
        {
            var operations = ParseOperations(lines, tools);
            groups.Add(new AptMachineGroup { GroupId = 1, Operations = operations });
        }

        var commandCount = groups.SelectMany(static g => g.Operations).Sum(static op => op.Commands.Count);
        var operationCount = groups.Sum(static g => g.Operations.Count);

        return new AptParseResult
        {
            Success = true,
            Message = "APT parsed successfully.",
            Program = new AptProgram
            {
                PartName = string.IsNullOrWhiteSpace(partName) ? "APT Program" : partName,
                Units = units,
                MultaxOn = multaxOn,
                Tools = tools,
                MachineGroups = groups
            },
            MachineGroupCount = groups.Count,
            OperationCount = operationCount,
            ToolCount = tools.Count,
            CommandCount = commandCount
        };
    }

    private static int FindNextMachineGroupOrFini(IReadOnlyList<string> lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("$$Machine Group-", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith("FINI", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return lines.Count;
    }

    private static IReadOnlyList<AptOperation> ParseOperations(IReadOnlyList<string> lines, IDictionary<int, AptToolDefinition> tools)
    {
        var operations = new List<AptOperation>();
        var operationId = 0;
        var currentCommands = new List<AptCommand>();
        var currentToolId = -1;
        var lastCutterLine = string.Empty;
        var lastTprintLine = string.Empty;
        var currentIndirv = new AptIndirvCommand();
        var currentPosition = new AptGoToCommand();
        AptCircleDefinition pendingCircle = null;

        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];

            if (line.StartsWith("CUTTER/", StringComparison.OrdinalIgnoreCase))
            {
                lastCutterLine = line;
                i++;
                continue;
            }

            if (line.StartsWith("TPRINT/", StringComparison.OrdinalIgnoreCase))
            {
                lastTprintLine = line;
                i++;
                continue;
            }

            if (line.StartsWith("LOAD/", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("LOADTL/", StringComparison.OrdinalIgnoreCase))
            {
                var toolId = ParseLoadToolId(line);
                if (toolId >= 0)
                {
                    currentToolId = toolId;
                    if (!tools.ContainsKey(toolId))
                    {
                        tools[toolId] = new AptToolDefinition
                        {
                            ToolId = toolId,
                            Diameter = ParseToolDiameter(lastCutterLine),
                            Label = ParseToolLabel(lastTprintLine)
                        };
                    }
                }

                if (currentCommands.Count > 0)
                {
                    operations.Add(new AptOperation
                    {
                        OperationId = operationId++,
                        ToolId = currentToolId,
                        Commands = currentCommands
                    });
                    currentCommands = new List<AptCommand>();
                }

                i++;
                continue;
            }

            if (line.Equals("RAPID", StringComparison.OrdinalIgnoreCase))
            {
                currentCommands.Add(new AptRapidCommand());
                i++;
                continue;
            }

            if (line.StartsWith("GOTO/", StringComparison.OrdinalIgnoreCase))
            {
                currentPosition = ParseGoto(line);
                if (pendingCircle != null)
                {
                    currentCommands.Add(new AptCircleCommand
                    {
                        Cx = pendingCircle.Cx,
                        Cy = pendingCircle.Cy,
                        Cz = pendingCircle.Cz,
                        Radius = pendingCircle.Radius,
                        Clockwise = pendingCircle.Clockwise,
                        X = currentPosition.X,
                        Y = currentPosition.Y,
                        Z = currentPosition.Z
                    });
                    pendingCircle = null;
                    i++;
                    continue;
                }

                currentCommands.Add(currentPosition);
                i++;
                continue;
            }

            if (line.StartsWith("INDIRV/", StringComparison.OrdinalIgnoreCase))
            {
                currentIndirv = ParseIndirv(line);
                currentCommands.Add(currentIndirv);
                i++;
                continue;
            }

            if (line.StartsWith("CIRCLE/", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("$", StringComparison.Ordinal) && i + 2 < lines.Count)
                {
                    var circle = ParseCircle(currentPosition, currentIndirv, lines[i], lines[i + 1], lines[i + 2]);
                    currentCommands.Add(circle);
                    currentPosition = new AptGoToCommand { X = circle.X, Y = circle.Y, Z = circle.Z };
                    i += 3;
                    continue;
                }

                pendingCircle = ParseInlineCircleDefinition(currentPosition, currentIndirv, line);
                i++;
                continue;
            }

            if (line.StartsWith("FEDRAT/", StringComparison.OrdinalIgnoreCase))
            {
                currentCommands.Add(ParseFeedrate(line));
                i++;
                continue;
            }

            if (line.StartsWith("SPINDL/", StringComparison.OrdinalIgnoreCase))
            {
                currentCommands.Add(ParseSpindle(line));
                i++;
                continue;
            }

            if (line.StartsWith("COOLNT/", StringComparison.OrdinalIgnoreCase))
            {
                currentCommands.Add(ParseCoolant(line));
                i++;
                continue;
            }

            i++;
        }

        if (currentCommands.Count > 0)
        {
            operations.Add(new AptOperation
            {
                OperationId = operationId,
                ToolId = currentToolId,
                Commands = currentCommands
            });
        }

        if (operations.Count == 0)
        {
            operations.Add(new AptOperation
            {
                OperationId = 0,
                ToolId = currentToolId,
                Commands = Array.Empty<AptCommand>()
            });
        }

        return operations;
    }

    private static int ParseLoadToolId(string line)
    {
        var payload = line.StartsWith("LOADTL/", StringComparison.OrdinalIgnoreCase)
            ? line["LOADTL/".Length..]
            : line["LOAD/".Length..];

        if (line.StartsWith("LOADTL/", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseInt(payload, -1);
        }

        var parts = payload.Split(',');
        if (parts.Length < 2)
        {
            return -1;
        }

        var normalized = parts[1].Trim().Replace(".", string.Empty, StringComparison.Ordinal);
        return TryParseInt(normalized, -1);
    }

    private static double ParseToolDiameter(string cutterLine)
    {
        if (string.IsNullOrWhiteSpace(cutterLine) || !cutterLine.StartsWith("CUTTER/", StringComparison.OrdinalIgnoreCase))
        {
            return 0.0;
        }

        var payload = cutterLine["CUTTER/".Length..];
        var parts = payload.Split(',');
        return parts.Length > 0 ? ParseDouble(parts[0]) : 0.0;
    }

    private static string ParseToolLabel(string tprintLine)
    {
        if (string.IsNullOrWhiteSpace(tprintLine) || !tprintLine.StartsWith("TPRINT/", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return tprintLine["TPRINT/".Length..].Trim();
    }

    private static AptGoToCommand ParseGoto(string line)
    {
        var payload = line["GOTO/".Length..];
        var parts = payload.Split(',');
        return new AptGoToCommand
        {
            X = parts.Length > 0 ? ParseDouble(parts[0]) : 0.0,
            Y = parts.Length > 1 ? ParseDouble(parts[1]) : 0.0,
            Z = parts.Length > 2 ? ParseDouble(parts[2]) : 0.0
        };
    }

    private static AptIndirvCommand ParseIndirv(string line)
    {
        var payload = line["INDIRV/".Length..];
        var parts = payload.Split(',');
        return new AptIndirvCommand
        {
            I = parts.Length > 0 ? ParseDouble(parts[0]) : 0.0,
            J = parts.Length > 1 ? ParseDouble(parts[1]) : 0.0,
            K = parts.Length > 2 ? ParseDouble(parts[2]) : 0.0
        };
    }

    private static AptCircleCommand ParseCircle(
        AptGoToCommand startpoint,
        AptIndirvCommand indirv,
        string line1,
        string line2,
        string line3)
    {
        var cPayload = line1[(line1.IndexOf("CIRCLE/", StringComparison.OrdinalIgnoreCase) + "CIRCLE/".Length)..];
        var centerParts = cPayload.Split(',');

        var radiusPart = ExtractCircleRadiusToken(line2);

        var gotoPayload = line3;
        if (gotoPayload.StartsWith("GOTO/", StringComparison.OrdinalIgnoreCase))
        {
            gotoPayload = gotoPayload["GOTO/".Length..];
        }

        var endParts = gotoPayload.Replace(")", string.Empty, StringComparison.Ordinal).Split(',');

        var circle = new AptCircleCommand
        {
            Cx = centerParts.Length > 0 ? ParseDouble(centerParts[0]) : 0.0,
            Cy = centerParts.Length > 1 ? ParseDouble(centerParts[1]) : 0.0,
            Cz = centerParts.Length > 2 ? ParseDouble(centerParts[2]) : 0.0,
            Radius = ParseDouble(radiusPart),
            X = endParts.Length > 0 ? ParseDouble(endParts[0]) : 0.0,
            Y = endParts.Length > 1 ? ParseDouble(endParts[1]) : 0.0,
            Z = endParts.Length > 2 ? ParseDouble(endParts[2]) : 0.0,
            Clockwise = ResolveArcDirection(startpoint, indirv, centerParts.Length > 0 ? ParseDouble(centerParts[0]) : 0.0, centerParts.Length > 1 ? ParseDouble(centerParts[1]) : 0.0)
        };

        return circle;
    }

    private static string ExtractCircleRadiusToken(string line)
    {
        var cleaned = line.Replace(")", string.Empty, StringComparison.Ordinal).Trim();
        var parts = cleaned.Split(',');

        if (parts.Length == 1)
        {
            return parts[0];
        }

        return parts[^1];
    }

    private static bool ResolveArcDirection(AptGoToCommand startpoint, AptIndirvCommand indirv, double cx, double cy)
    {
        var cw = false;
        var ccw = true;

        if (indirv.I <= 0 && indirv.J < 0)
        {
            if (cx < startpoint.X && cy >= startpoint.Y) return cw;
            if (cx > startpoint.X && cy <= startpoint.Y) return ccw;
        }

        if (indirv.I < 0 && indirv.J >= 0)
        {
            if (cx >= startpoint.X && cy > startpoint.Y) return cw;
            if (cx <= startpoint.X && cy < startpoint.Y) return ccw;
        }

        if (indirv.I >= 0 && indirv.J > 0)
        {
            if (cx > startpoint.X && cy <= startpoint.Y) return cw;
            if (cx < startpoint.X && cy >= startpoint.Y) return ccw;
        }

        if (indirv.I > 0 && indirv.J <= 0)
        {
            if (cx <= startpoint.X && cy < startpoint.Y) return cw;
            if (cx >= startpoint.X && cy > startpoint.Y) return ccw;
        }

        return cw;
    }

    private static AptFeedrateCommand ParseFeedrate(string line)
    {
        var payload = line["FEDRAT/".Length..];
        var parts = payload.Split(',');
        var first = parts.Length > 0 ? parts[0].Trim() : "MPM";
        var second = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        var firstIsNumber = double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var firstValue);

        return new AptFeedrateCommand
        {
            Unit = firstIsNumber ? second : first,
            Feedrate = firstIsNumber ? firstValue : (parts.Length > 1 ? ParseDouble(parts[1]) : 0.0)
        };
    }

    private static AptSpindleSpeedCommand ParseSpindle(string line)
    {
        var payload = line["SPINDL/".Length..];
        var parts = payload.Split(',');
        return new AptSpindleSpeedCommand
        {
            Unit = parts.Length > 0 ? parts[0].Trim() : "RPM",
            Speed = parts.Length > 1 ? ParseDouble(parts[1]) : 0.0,
            Direction = parts.Length > 2 ? parts[2].Trim() : "CLW"
        };
    }

    private static AptCoolantCommand ParseCoolant(string line)
    {
        var value = line["COOLNT/".Length..].Trim();
        return new AptCoolantCommand { Enabled = !value.Equals("OFF", StringComparison.OrdinalIgnoreCase) };
    }

    private static AptCircleDefinition ParseInlineCircleDefinition(
        AptGoToCommand startpoint,
        AptIndirvCommand indirv,
        string line)
    {
        var payload = line["CIRCLE/".Length..].Replace("$", string.Empty, StringComparison.Ordinal);
        var parts = payload.Split(',').Select(static p => p.Trim()).ToArray();
        var cx = parts.Length > 0 ? ParseDouble(parts[0]) : 0.0;
        var cy = parts.Length > 1 ? ParseDouble(parts[1]) : 0.0;
        var cz = parts.Length > 2 ? ParseDouble(parts[2]) : 0.0;
        var k = parts.Length > 5 ? ParseDouble(parts[5]) : 0.0;
        var radius = parts.Length > 6 ? ParseDouble(parts[6]) : 0.0;

        return new AptCircleDefinition
        {
            Cx = cx,
            Cy = cy,
            Cz = cz,
            Radius = radius,
            Clockwise = k < 0 ? false : ResolveArcDirection(startpoint, indirv, cx, cy)
        };
    }

    private static string NormalizeLine(string line)
    {
        return line
            .Replace("PARTNO /", "PARTNO/", StringComparison.OrdinalIgnoreCase)
            .Replace("UNITS /", "UNITS/", StringComparison.OrdinalIgnoreCase)
            .Replace("LOADTL /", "LOADTL/", StringComparison.OrdinalIgnoreCase)
            .Replace("MACHIN /", "MACHIN/", StringComparison.OrdinalIgnoreCase)
            .Replace("SPINDL /", "SPINDL/", StringComparison.OrdinalIgnoreCase)
            .Replace("COOLNT /", "COOLNT/", StringComparison.OrdinalIgnoreCase)
            .Replace("GOTO /", "GOTO/", StringComparison.OrdinalIgnoreCase)
            .Replace("FEDRAT /", "FEDRAT/", StringComparison.OrdinalIgnoreCase)
            .Replace("CIRCLE /", "CIRCLE/", StringComparison.OrdinalIgnoreCase)
            .Replace("RAPID ", "RAPID", StringComparison.OrdinalIgnoreCase);
    }

    private static double ParseDouble(string value)
    {
        return double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static int TryParseInt(string value, int fallback)
    {
        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private sealed class AptCircleDefinition
    {
        public double Cx { get; init; }
        public double Cy { get; init; }
        public double Cz { get; init; }
        public double Radius { get; init; }
        public bool Clockwise { get; init; }
    }
}
