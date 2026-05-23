using System;
using System.IO;
using StepNc.Adapters;

StepNcResult result;
var outputDirectory = "output";
Directory.CreateDirectory(outputDirectory);

if (args.Length > 0 && string.Equals(args[0], "parse-additive", StringComparison.OrdinalIgnoreCase))
{
    var additiveInput = args.Length > 1 ? args[1] : "samples/additive/square.xml";
    var parsed = StepNcApi.ParseAdditiveLayersXml(additiveInput);
    Console.WriteLine(parsed.Message);
    if (parsed.Success)
    {
        Console.WriteLine($"Layers: {parsed.LayerCount}, Polylines: {parsed.PolylineCount}, Points: {parsed.PointCount}");
    }

    Environment.Exit(parsed.Success ? 0 : 1);
    return;
}

if (args.Length > 0 && string.Equals(args[0], "additive", StringComparison.OrdinalIgnoreCase))
{
    var additiveInput = args.Length > 1 ? args[1] : "samples/additive/square.xml";
    var additiveOutput = args.Length > 2 ? args[2] : Path.Combine(outputDirectory, "square.stpnc");

    result = StepNcApi.ConvertAdditiveXmlToStepNc(
        inputXmlPath: additiveInput,
        outputPath: additiveOutput);
}
else if (args.Length > 0 && string.Equals(args[0], "parse-apt", StringComparison.OrdinalIgnoreCase))
{
    var aptInput = args.Length > 1 ? args[1] : "samples/apt/testPart-1.apt";
    var parsed = StepNcApi.ParseMastercamApt(aptInput);
    Console.WriteLine(parsed.Message);
    if (parsed.Success)
    {
        Console.WriteLine($"Machine groups: {parsed.MachineGroupCount}, Operations: {parsed.OperationCount}, Tools: {parsed.ToolCount}, Commands: {parsed.CommandCount}");
    }

    Environment.Exit(parsed.Success ? 0 : 1);
    return;
}
else if (args.Length > 0 && string.Equals(args[0], "apt", StringComparison.OrdinalIgnoreCase))
{
    var aptInput = args.Length > 1 ? args[1] : "samples/apt/testPart-1.apt";
    var aptOutput = args.Length > 2 ? args[2] : Path.Combine(outputDirectory, "testPart-1.stpnc");
    result = StepNcApi.ConvertMastercamAptToStepNc(aptInput, aptOutput);
}
else
{
    result = StepNcApi.GenerateSimpleTestProgram(
        outputPath: Path.Combine(outputDirectory, "test_program.stpnc"),
        options: new SimpleProgramOptions
        {
            PartName = "test program",
            WorkingstepName = "WS-1",
            Feedrate = 2000.0,
            SpindleSpeed = 3000.0
        });
}

Console.WriteLine(result.Message);

Environment.Exit(result.Success ? 0 : 1);
