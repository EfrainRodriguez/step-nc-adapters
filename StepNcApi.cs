namespace StepNc.Adapters;

/// <summary>
/// Public facade for reusable STEP-NC adapter functions.
/// </summary>
public static class StepNcApi
{
    public static StepNcResult GenerateSimpleTestProgram(string outputPath, SimpleProgramOptions options = null)
    {
        return SimpleStepNcGenerator.Generate(outputPath, options);
    }

    public static AdditiveParseResult ParseAdditiveLayersXml(string inputXmlPath)
    {
        return AdditiveXmlParser.ParseFile(inputXmlPath);
    }

    public static StepNcResult ConvertAdditiveXmlToStepNc(
        string inputXmlPath,
        string outputPath,
        AdditiveConversionOptions options = null)
    {
        var parsed = AdditiveXmlParser.ParseFile(inputXmlPath);
        if (!parsed.Success)
        {
            return new StepNcResult
            {
                Success = false,
                OutputPath = outputPath,
                Message = parsed.Message
            };
        }

        return AdditiveStepNcGenerator.GenerateFromLayers(parsed.Layers, outputPath, options);
    }

    public static AptParseResult ParseMastercamApt(string inputAptPath)
    {
        return AptParser.ParseMastercamFile(inputAptPath);
    }

    public static StepNcResult ConvertMastercamAptToStepNc(
        string inputAptPath,
        string outputPath,
        AptConversionOptions options = null)
    {
        var parsed = AptParser.ParseMastercamFile(inputAptPath);
        if (!parsed.Success)
        {
            return new StepNcResult
            {
                Success = false,
                OutputPath = outputPath,
                Message = parsed.Message
            };
        }

        return AptStepNcGenerator.GenerateFromProgram(parsed.Program, outputPath, options);
    }
}
