using System.Reflection;

namespace DotnetInspector.Commands;

/// <summary>
/// Prints the llms.txt file for LLM consumption.
/// </summary>
public class LlmsTxtCommand
{
    public const string Name = "llmstxt";
    public static int Execute()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("dotnet-inspect.llms.txt");

        if (stream == null)
        {
            Console.Error.WriteLine("Error: llms.txt resource not found.");
            return 1;
        }

        using var reader = new StreamReader(stream);
        Console.WriteLine(reader.ReadToEnd());

        return 0;
    }
}
