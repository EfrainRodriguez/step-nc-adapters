# AGENTS.md

## Project Instructions

- Always respond in English, even when the user writes in another language.
- Always write code, comments, documentation, commit messages, CLI help text, and user-facing strings in English.
- Keep changes small, direct, and aligned with the existing C# style.
- Do not revert or overwrite unrelated user changes.
- Prefer explicit validation by building and running the relevant adapter command after code changes.

## Project Context

- Main CLI entry point: `Program.cs`.
- Public adapter facade: `StepNcApi.cs`.
- Shared models and options: `Models.cs`.
- STEP-NC DLL wrapper reference: `StepNc.Adapters.csproj`.
- General usage and examples: `README.md`.
- JSON additive adapter plan/context: `docs/json-additive-stepnc-adapter-plan.md`.
- STEP-NC Machine DLL reference notes: `docs/dll.html`.

## Adapter Files

- Simple STEP-NC generation: `SimpleStepNcGenerator.cs`.
- Additive XML parsing: `AdditiveXmlParser.cs`.
- Additive XML STEP-NC generation: `AdditiveStepNcGenerator.cs`.
- Additive JSON parsing: `AdditiveJsonParser.cs`.
- Additive JSON STEP-NC generation: `AdditiveJsonStepNcGenerator.cs`.
- Mastercam APT parsing: `AptParser.cs`.
- Mastercam APT STEP-NC generation: `AptStepNcGenerator.cs`.

## Sample Inputs And Outputs

- Additive JSON sample: `samples/additive/cube_new.json`.
- Additive XML sample: `samples/additive/square.xml`.
- APT sample: `samples/apt/testPart-1.apt`.
- Default generated STEP-NC outputs: `samples/output/`.

## CLI Commands

Run commands from the repository root so relative sample paths resolve correctly.

```powershell
dotnet build
DOTNET_ROLL_FORWARD=Major bin\Debug\net8.0-windows\StepNc.Adapters.exe --help
DOTNET_ROLL_FORWARD=Major bin\Debug\net8.0-windows\StepNc.Adapters.exe simple
DOTNET_ROLL_FORWARD=Major bin\Debug\net8.0-windows\StepNc.Adapters.exe additive-xml --input samples/additive/square.xml --output samples/output/square.stpnc
DOTNET_ROLL_FORWARD=Major bin\Debug\net8.0-windows\StepNc.Adapters.exe additive-json --input samples/additive/cube_new.json --output samples/output/cube_new.stpnc
DOTNET_ROLL_FORWARD=Major bin\Debug\net8.0-windows\StepNc.Adapters.exe apt --input samples/apt/testPart-1.apt --output samples/output/testPart-1.stpnc
```

## Environment Notes

- The project targets `net8.0-windows` and x64.
- If only a newer .NET runtime is installed, use `DOTNET_ROLL_FORWARD=Major` when running the compiled executable or `dotnet run`.
- STEP-NC generation requires STEP-NC Machine Personal Edition and the referenced `stepnc_x64.dll` path in `StepNc.Adapters.csproj`.

## Validation Checklist

- Run `dotnet build` after code changes.
- For CLI changes, run `StepNc.Adapters.exe --help`.
- For adapter changes, generate the matching sample output under `samples/output/`.
- For additive JSON changes, verify empty JSON layers are not emitted as empty STEP-NC workplans.
