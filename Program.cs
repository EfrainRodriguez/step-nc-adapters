using System;
using System.Collections.Generic;
using System.IO;
using StepNc.Adapters;

var outputDirectory = Path.Combine("samples", "output");
Directory.CreateDirectory(outputDirectory);

if (args.Length == 0)
{
    args = new[] { "simple" };
}

var command = args[0].Trim().ToLowerInvariant();
if (command is "help" or "--help" or "-h")
{
    PrintUsage();
    return;
}

var options = ParseOptions(args);

switch (command)
{
    case "simple":
    case "test":
        var simpleOutput = GetOption(options, "output", "o", Path.Combine(outputDirectory, "test_program.stpnc"));
        EnsureOutputDirectory(simpleOutput);
        ExitWithResult(StepNcApi.GenerateSimpleTestProgram(
            outputPath: simpleOutput,
            options: new SimpleProgramOptions
            {
                PartName = "test program",
                WorkingstepName = "WS-1",
                Feedrate = 2000.0,
                SpindleSpeed = 3000.0
            }));
        return;

    case "additive":
    case "additive-xml":
        var additiveXmlOutput = GetOption(options, "output", "o", Path.Combine(outputDirectory, "square.stpnc"));
        EnsureOutputDirectory(additiveXmlOutput);
        ExitWithResult(StepNcApi.ConvertAdditiveXmlToStepNc(
            inputXmlPath: GetOption(options, "input", "i", "samples/additive/square.xml"),
            outputPath: additiveXmlOutput));
        return;

    case "additive-json":
        var additiveJsonOutput = GetOption(options, "output", "o", Path.Combine(outputDirectory, "cube_new.stpnc"));
        EnsureOutputDirectory(additiveJsonOutput);
        ExitWithResult(StepNcApi.ConvertAdditiveJsonToStepNc(
            inputJsonPath: GetOption(options, "input", "i", "samples/additive/cube_new.json"),
            outputPath: additiveJsonOutput));
        return;

    case "apt":
        var aptOutput = GetOption(options, "output", "o", Path.Combine(outputDirectory, "testPart-1.stpnc"));
        EnsureOutputDirectory(aptOutput);
        ExitWithResult(StepNcApi.ConvertMastercamAptToStepNc(
            inputAptPath: GetOption(options, "input", "i", "samples/apt/testPart-1.apt"),
            outputPath: aptOutput));
        return;

    case "parse-additive":
    case "parse-additive-xml":
        var parsedXml = StepNcApi.ParseAdditiveLayersXml(GetOption(options, "input", "i", "samples/additive/square.xml"));
        Console.WriteLine(parsedXml.Message);
        if (parsedXml.Success)
        {
            Console.WriteLine($"Layers: {parsedXml.LayerCount}, Polylines: {parsedXml.PolylineCount}, Points: {parsedXml.PointCount}");
        }

        Environment.Exit(parsedXml.Success ? 0 : 1);
        return;

    case "parse-additive-json":
        var parsedJson = StepNcApi.ParseAdditiveJson(GetOption(options, "input", "i", "samples/additive/cube_new.json"));
        Console.WriteLine(parsedJson.Message);
        if (parsedJson.Success)
        {
            Console.WriteLine($"Layers: {parsedJson.LayerCount}, Events: {parsedJson.EventCount}, Extrusions: {parsedJson.ExtrusionEventCount}, Travels: {parsedJson.TravelEventCount}, Points: {parsedJson.PointCount}");
        }

        Environment.Exit(parsedJson.Success ? 0 : 1);
        return;

    case "parse-apt":
        var parsedApt = StepNcApi.ParseMastercamApt(GetOption(options, "input", "i", "samples/apt/testPart-1.apt"));
        Console.WriteLine(parsedApt.Message);
        if (parsedApt.Success)
        {
            Console.WriteLine($"Machine groups: {parsedApt.MachineGroupCount}, Operations: {parsedApt.OperationCount}, Tools: {parsedApt.ToolCount}, Commands: {parsedApt.CommandCount}");
        }

        Environment.Exit(parsedApt.Success ? 0 : 1);
        return;

    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage();
        Environment.Exit(2);
        return;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var positionalIndex = 0;

    for (var i = 1; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            var key = arg[2..];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            options[key] = i + 1 < args.Length ? args[++i] : string.Empty;
            continue;
        }

        if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length == 2)
        {
            options[arg[1..]] = i + 1 < args.Length ? args[++i] : string.Empty;
            continue;
        }

        if (positionalIndex == 0)
        {
            options["input"] = arg;
            positionalIndex++;
            continue;
        }

        if (positionalIndex == 1)
        {
            options["output"] = arg;
            positionalIndex++;
        }
    }

    return options;
}

static string GetOption(IReadOnlyDictionary<string, string> options, string longName, string shortName, string defaultValue)
{
    if (options.TryGetValue(longName, out var longValue) && !string.IsNullOrWhiteSpace(longValue))
    {
        return longValue;
    }

    return options.TryGetValue(shortName, out var shortValue) && !string.IsNullOrWhiteSpace(shortValue)
        ? shortValue
        : defaultValue;
}

static void ExitWithResult(StepNcResult result)
{
    Console.WriteLine(result.Message);
    if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath))
    {
        Console.WriteLine($"Output: {result.OutputPath}");
    }

    Environment.Exit(result.Success ? 0 : 1);
}

static void EnsureOutputDirectory(string outputPath)
{
    var outputParent = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(outputParent))
    {
        Directory.CreateDirectory(outputParent);
    }
}

static void PrintUsage()
{
    Console.WriteLine("STEP-NC Adapters CLI");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  StepNc.Adapters <command> [--input <path>] [--output <path>]");
    Console.WriteLine("  StepNc.Adapters <command> [input] [output]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  simple                         Generate simple test STEP-NC");
    Console.WriteLine("  additive | additive-xml        Convert additive XML to STEP-NC");
    Console.WriteLine("  additive-json                  Convert additive JSON to STEP-NC");
    Console.WriteLine("  apt                            Convert Mastercam APT to STEP-NC");
    Console.WriteLine("  parse-additive | parse-additive-xml");
    Console.WriteLine("  parse-additive-json");
    Console.WriteLine("  parse-apt");
    Console.WriteLine();
    Console.WriteLine("Default outputs are written to samples/output.");
}
