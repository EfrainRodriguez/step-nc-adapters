# Step-NC Adapters

Small .NET 8 project that wraps STEP-NC Machine DLL usage into reusable C# adapter functions.

## Requirements

1. .NET SDK 8
2. STEP-NC Machine Personal Edition (STEP Tools)
3. `stepnc_x64.dll` available at:

`C:\Program Files (x86)\STEP Tools\STEP-NC Machine Personal Edition\stepnc_x64.dll`

## What is included

- `StepNcApi.GenerateSimpleTestProgram(...)`: generates a simple STEP-NC test program.
- `StepNcApi.ParseAdditiveLayersXml(...)`: parses additive XML and reports layer/polyline/point counts.
- `StepNcApi.ConvertAdditiveXmlToStepNc(...)`: parses additive XML layers and exports STEP-NC.
- `StepNcApi.ParseMastercamApt(...)`: parses Mastercam-style APT and reports structure counts.
- `StepNcApi.ConvertMastercamAptToStepNc(...)`: converts Mastercam-style APT to STEP-NC.
- `Program.cs`: example `Main` showing how to call the API function.
- `samples/additive/square.xml`: sample additive XML input for parser testing.
- `samples/apt/testPart-1.apt`: sample APT input for parser testing.

## Project organization

- `StepNcApi.cs`: public facade used by client applications.
- `SimpleStepNcGenerator.cs`: simple test-program generation logic.
- `AdditiveXmlParser.cs`: additive XML parsing and validation.
- `AdditiveStepNcGenerator.cs`: additive STEP-NC writing logic.
- `AptParser.cs`: Mastercam-style APT parser.
- `AptStepNcGenerator.cs`: APT-to-STEP-NC writing logic.
- `Models.cs`: shared options/results/domain models.

## Install prerequisites

### 1) Install .NET SDK 8

- Download from Microsoft official page:
  - https://dotnet.microsoft.com/download/dotnet/8.0
- Install the SDK (not only runtime).

Verify in PowerShell:

```powershell
dotnet --list-sdks
```

You should see an 8.x entry.

### 2) Install STEP-NC Machine Personal Edition

- Download and install STEP-NC Machine from STEP Tools.
- The DLL is usually installed at:

`C:\Program Files (x86)\STEP Tools\STEP-NC Machine Personal Edition\stepnc_x64.dll`

Verify in PowerShell:

```powershell
Test-Path "C:\Program Files (x86)\STEP Tools\STEP-NC Machine Personal Edition\stepnc_x64.dll"
```

Expected result: `True`.

## API usage

### Function: `StepNcApi.GenerateSimpleTestProgram`

Purpose:

- Generate a minimal STEP-NC test file (`.stpnc`) that can be used to validate environment and integration.

Parameters:

- `outputPath` (`string`): output `.stpnc` file path.
- `options` (`SimpleProgramOptions`, optional): program/machining values.

`SimpleProgramOptions` fields:

- `PartName`: STEP-NC project/part identifier.
- `WorkingstepName`: workingstep label.
- `Feedrate`: feed rate value.
- `SpindleSpeed`: spindle speed value.
- `ToolpathPoints`: optional custom trajectory points. If omitted, the API generates the default test rectangle.

Return type:

- `StepNcResult`
  - `Success` (`bool`): generation status.
  - `OutputPath` (`string`): output path used.
  - `Message` (`string`): success or error detail.

Example:

```csharp
using StepNc.Adapters;

var result = StepNcApi.GenerateSimpleTestProgram(
    outputPath: "output/test_program.stpnc",
    options: new SimpleProgramOptions
    {
        PartName = "demo part",
        WorkingstepName = "WS-1",
        Feedrate = 2000.0,
        SpindleSpeed = 3000.0
    });

if (!result.Success)
{
    Console.WriteLine($"Error: {result.Message}");
}
else
{
    Console.WriteLine($"OK: {result.OutputPath}");
}
```

Example with custom points:

```csharp
using StepNc.Adapters;

var result = StepNcApi.GenerateSimpleTestProgram(
    outputPath: "custom_path_program.stpnc",
    options: new SimpleProgramOptions
    {
        PartName = "custom path demo",
        WorkingstepName = "WS-CUSTOM",
        Feedrate = 1800.0,
        SpindleSpeed = 2800.0,
        ToolpathPoints = new[]
        {
            new ToolpathPoint("A1", 0.0, 0.0, 0.0),
            new ToolpathPoint("A2", 20.0, 0.0, 0.0),
            new ToolpathPoint("A3", 20.0, 15.0, 0.0),
            new ToolpathPoint("A4", 0.0, 15.0, 0.0),
            new ToolpathPoint("A5", 0.0, 0.0, 0.0)
        }
    });
```

### Function: `StepNcApi.ConvertAdditiveXmlToStepNc`

Purpose:

- Parse additive XML layers/exposures/points and generate STEP-NC using the STEP-NC Machine DLL.

Parameters:

- `inputXmlPath` (`string`): input additive XML path.
- `outputPath` (`string`): output `.stpnc` path.
- `options` (`AdditiveConversionOptions`, optional): additive generation settings.

`AdditiveConversionOptions` fields:

- `ProjectName`: STEP-NC additive project name.
- `ContextId`: context id used in `NewProjectWithCCandWP`.
- `MainWorkplanName`: root additive workplan name.
- `Feedrate`: feedrate for deposition paths.
- `SpindleSpeed`: spindle speed for deposition paths.
- `SaveAsP21`: when `true` (default), outputs STEP Part 21 text format.

Return type:

- `StepNcResult`
  - `Success` (`bool`)
  - `OutputPath` (`string`)
  - `Message` (`string`)

Example:

```csharp
using StepNc.Adapters;

var result = StepNcApi.ConvertAdditiveXmlToStepNc(
    inputXmlPath: "samples/additive/square.xml",
    outputPath: "output/square.stpnc");

Console.WriteLine(result.Message);
```

### Function: `StepNcApi.ParseAdditiveLayersXml`

Purpose:

- Parse the additive XML and return diagnostic counts before generating STEP-NC.

Returns:

- `AdditiveParseResult`
  - `Success` (`bool`)
  - `Message` (`string`)
  - `LayerCount` (`int`)
  - `PolylineCount` (`int`)
  - `PointCount` (`int`)
  - `Layers` (`IReadOnlyList<AdditiveLayer>`)

Example:

```csharp
using StepNc.Adapters;

var parsed = StepNcApi.ParseAdditiveLayersXml("samples/additive/square.xml");
if (!parsed.Success)
{
    Console.WriteLine(parsed.Message);
    return;
}

Console.WriteLine($"Layers: {parsed.LayerCount}");
Console.WriteLine($"Polylines: {parsed.PolylineCount}");
Console.WriteLine($"Points: {parsed.PointCount}");
```

### Function: `StepNcApi.ParseMastercamApt`

Purpose:

- Parse a Mastercam-style APT file and return counts for machine groups, operations, tools, and commands.

Example:

```csharp
using StepNc.Adapters;

var parsed = StepNcApi.ParseMastercamApt("samples/apt/testPart-1.apt");
if (!parsed.Success)
{
    Console.WriteLine(parsed.Message);
    return;
}

Console.WriteLine($"Groups: {parsed.MachineGroupCount}");
Console.WriteLine($"Operations: {parsed.OperationCount}");
Console.WriteLine($"Tools: {parsed.ToolCount}");
Console.WriteLine($"Commands: {parsed.CommandCount}");
```

### Function: `StepNcApi.ConvertMastercamAptToStepNc`

Purpose:

- Convert Mastercam-style APT into STEP-NC with the STEP-NC Machine DLL.

Parameters:

- `inputAptPath` (`string`): input `.apt` file.
- `outputPath` (`string`): output `.stpnc` file.
- `options` (`AptConversionOptions`, optional): rawpiece/workplan and save format options.

Example:

```csharp
using StepNc.Adapters;

var result = StepNcApi.ConvertMastercamAptToStepNc(
    inputAptPath: "samples/apt/testPart-1.apt",
    outputPath: "output/testPart-1.stpnc");

Console.WriteLine(result.Message);
```

## Build and run

From this folder:

```powershell
dotnet build
dotnet run
dotnet run additive
dotnet run parse-additive
dotnet run apt
dotnet run parse-apt
```

This runs `Program.cs`, which calls `StepNcApi.GenerateSimpleTestProgram(...)` and creates `output/test_program.stpnc`.
`dotnet run additive` runs the additive parser flow and creates `output/square.stpnc`.
`dotnet run parse-additive` parses XML only and prints layer/polyline/point counts.
`dotnet run apt` converts `samples/apt/testPart-1.apt` to `output/testPart-1.stpnc`.
`dotnet run parse-apt` parses APT only and prints group/operation/tool/command counts.

## Reference path in project file

The project keeps a fixed reference in `StepNc.Adapters.csproj`:

```xml
<Reference Include="stepnc_x64">
  <HintPath>C:\Program Files (x86)\STEP Tools\STEP-NC Machine Personal Edition\stepnc_x64.dll</HintPath>
</Reference>
```
