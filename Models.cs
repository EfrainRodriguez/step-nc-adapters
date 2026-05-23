using System;
using System.Collections.Generic;

namespace StepNc.Adapters;

public sealed class StepNcResult
{
    public bool Success { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class SimpleProgramOptions
{
    public string PartName { get; init; } = "test program";
    public string WorkingstepName { get; init; } = "WS-1";
    public double Feedrate { get; init; } = 2000.0;
    public double SpindleSpeed { get; init; } = 3000.0;
    public IReadOnlyList<ToolpathPoint> ToolpathPoints { get; init; }
}

public sealed record ToolpathPoint(string Name, double X, double Y, double Z);

public sealed class AdditiveLayer
{
    public int LayerNo { get; init; }
    public double Height { get; init; }
    public IReadOnlyList<AdditivePolyline> Polylines { get; init; } = Array.Empty<AdditivePolyline>();
}

public sealed class AdditivePolyline
{
    public int PolylineId { get; init; }
    public string PolylineType { get; init; } = string.Empty;
    public IReadOnlyList<ToolpathPoint> Points { get; init; } = Array.Empty<ToolpathPoint>();
}

public sealed class AdditiveConversionOptions
{
    public string ProjectName { get; init; } = "Additive Manufacturing STEP-NC";
    public int ContextId { get; init; } = 1;
    public string MainWorkplanName { get; init; } = "Main Additive Workplan";
    public double Feedrate { get; init; } = 1600.0;
    public double SpindleSpeed { get; init; } = 220.0;
    public bool SaveAsP21 { get; init; } = true;
}

public sealed class AdditiveParseResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<AdditiveLayer> Layers { get; init; } = Array.Empty<AdditiveLayer>();
    public int LayerCount { get; init; }
    public int PolylineCount { get; init; }
    public int PointCount { get; init; }
}

public sealed class AptConversionOptions
{
    public bool SaveAsP21 { get; init; } = true;
    public string MainWorkplanName { get; init; } = "Main Workplan";
    public string RawpieceName { get; init; } = "mastercam block";
    public double RawpieceX { get; init; } = 0.0;
    public double RawpieceY { get; init; } = 0.0;
    public double RawpieceZ { get; init; } = 0.0;
    public double RawpieceLength { get; init; } = 70.0;
    public double RawpieceWidth { get; init; } = 100.0;
    public double RawpieceHeight { get; init; } = 50.0;
}

public sealed class AptParseResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public AptProgram Program { get; init; }
    public int MachineGroupCount { get; init; }
    public int OperationCount { get; init; }
    public int ToolCount { get; init; }
    public int CommandCount { get; init; }
}

public sealed class AptProgram
{
    public string PartName { get; init; } = "APT Program";
    public string Units { get; init; } = "MM";
    public bool MultaxOn { get; init; }
    public IReadOnlyDictionary<int, AptToolDefinition> Tools { get; init; } = new Dictionary<int, AptToolDefinition>();
    public IReadOnlyList<AptMachineGroup> MachineGroups { get; init; } = Array.Empty<AptMachineGroup>();
}

public sealed class AptToolDefinition
{
    public int ToolId { get; init; }
    public double Diameter { get; init; }
    public string Label { get; init; } = string.Empty;
}

public sealed class AptMachineGroup
{
    public int GroupId { get; init; }
    public IReadOnlyList<AptOperation> Operations { get; init; } = Array.Empty<AptOperation>();
}

public sealed class AptOperation
{
    public int OperationId { get; init; }
    public int ToolId { get; init; }
    public IReadOnlyList<AptCommand> Commands { get; init; } = Array.Empty<AptCommand>();
}

public abstract class AptCommand { }

public sealed class AptRapidCommand : AptCommand { }

public sealed class AptGoToCommand : AptCommand
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
}

public sealed class AptCircleCommand : AptCommand
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Cx { get; init; }
    public double Cy { get; init; }
    public double Cz { get; init; }
    public double Radius { get; init; }
    public bool Clockwise { get; init; }
}

public sealed class AptIndirvCommand : AptCommand
{
    public double I { get; init; }
    public double J { get; init; }
    public double K { get; init; }
}

public sealed class AptFeedrateCommand : AptCommand
{
    public double Feedrate { get; init; }
    public string Unit { get; init; } = "MPM";
}

public sealed class AptSpindleSpeedCommand : AptCommand
{
    public double Speed { get; init; }
    public string Direction { get; init; } = "CLW";
    public string Unit { get; init; } = "RPM";
}

public sealed class AptCoolantCommand : AptCommand
{
    public bool Enabled { get; init; } = true;
}
