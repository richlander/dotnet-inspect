using System.Reflection;

namespace DotnetInspector.Commands;

/// <summary>
/// Prints the SKILL.md file for LLM consumption.
/// </summary>
public class SkillCommand
{
    public const string Name = "skill";
    public static int Execute()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("dotnet-inspect.SKILL.md");

        if (stream == null)
        {
            Console.Error.WriteLine("Error: SKILL.md resource not found.");
            return 1;
        }

        using var reader = new StreamReader(stream);
        Console.WriteLine(reader.ReadToEnd());

        return 0;
    }
}
