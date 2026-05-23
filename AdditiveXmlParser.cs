using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace StepNc.Adapters;

internal static class AdditiveXmlParser
{
    public static AdditiveParseResult ParseFile(string inputXmlPath)
    {
        if (string.IsNullOrWhiteSpace(inputXmlPath))
        {
            return new AdditiveParseResult { Success = false, Message = "Input XML path is required." };
        }

        if (!File.Exists(inputXmlPath))
        {
            return new AdditiveParseResult
            {
                Success = false,
                Message = $"Input XML file was not found: {inputXmlPath}"
            };
        }

        try
        {
            var doc = XDocument.Load(inputXmlPath, LoadOptions.None);
            var layersContainer = doc.Descendants("Layers").FirstOrDefault();
            if (layersContainer == null)
            {
                return new AdditiveParseResult
                {
                    Success = false,
                    Message = "XML does not contain a Layers element."
                };
            }

            var layers = new List<AdditiveLayer>();
            var layerIndex = 0;
            var polylineCount = 0;
            var pointCount = 0;

            foreach (var layerElement in layersContainer.Elements("Layer"))
            {
                var height = ParseRequiredDoubleAttribute(layerElement, "z");
                var polylines = new List<AdditivePolyline>();
                var polylineIndex = 0;

                foreach (var exposureElement in layerElement.Elements("Exposure"))
                {
                    var polylineType = ((string)exposureElement.Attribute("polylineType") ?? string.Empty).Trim();
                    var points = new List<ToolpathPoint>();
                    var pointIndex = 1;

                    // Support both formats:
                    // 1) Exposure > Point
                    // 2) Exposure > Segments > Segment > Point
                    foreach (var pointElement in exposureElement.Descendants("Point"))
                    {
                        var point = new ToolpathPoint(
                            $"P{pointIndex}",
                            ParseRequiredDoubleAttribute(pointElement, "x"),
                            ParseRequiredDoubleAttribute(pointElement, "y"),
                            ParseRequiredDoubleAttribute(pointElement, "z"));
                        points.Add(point);
                        pointIndex++;
                        pointCount++;
                    }

                    if (points.Count > 0)
                    {
                        polylines.Add(new AdditivePolyline
                        {
                            PolylineId = polylineIndex,
                            PolylineType = polylineType,
                            Points = points
                        });

                        polylineIndex++;
                        polylineCount++;
                    }
                }

                if (height > 0.0)
                {
                    layers.Add(new AdditiveLayer
                    {
                        LayerNo = layerIndex,
                        Height = height,
                        Polylines = polylines
                    });
                }

                layerIndex++;
            }

            return new AdditiveParseResult
            {
                Success = true,
                Message = "Additive XML parsed successfully.",
                Layers = layers,
                LayerCount = layers.Count,
                PolylineCount = polylineCount,
                PointCount = pointCount
            };
        }
        catch (Exception ex)
        {
            return new AdditiveParseResult
            {
                Success = false,
                Message = $"Failed to parse additive XML: {ex.Message}"
            };
        }
    }

    private static double ParseRequiredDoubleAttribute(XElement element, string attributeName)
    {
        var value = (string)element.Attribute(attributeName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"Element '{element.Name}' is missing required attribute '{attributeName}'.");
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException(
                $"Attribute '{attributeName}' on element '{element.Name}' is not a valid number: {value}");
        }

        return parsed;
    }
}
