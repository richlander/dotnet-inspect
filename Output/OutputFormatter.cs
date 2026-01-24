using System.Text.Json;
using MarkOut;

namespace DotnetInspector.Output;

/// <summary>
/// Handles output formatting for inspection results.
/// </summary>
public static class OutputFormatter
{
    public static void WriteResult(InspectionResult result, bool jsonOutput)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonContext.Default.InspectionResult));
        }
        else
        {
            Console.WriteLine(MarkOutSerializer.Serialize(result, new MarkOutContext { BoldFieldNames = true }));
        }
    }
}
