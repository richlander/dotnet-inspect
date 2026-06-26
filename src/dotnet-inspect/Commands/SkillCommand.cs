using System.Reflection;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Prints embedded skill documents for LLM consumption. The base skill acts as a
/// router that lists focused topical skills (source, performance, ...). Each
/// focused skill is an embedded resource printed on demand.
/// </summary>
public class SkillCommand
{
    public const string Name = "skill";

    /// <summary>
    /// A focused skill that can be printed with <c>dotnet-inspect skill &lt;name&gt;</c>.
    /// </summary>
    public sealed record SkillEntry(string Name, string Description, string ResourceName);

    /// <summary>
    /// The base/router skill, printed by bare <c>dotnet-inspect skill</c>.
    /// </summary>
    public const string RouterResourceName = "dotnet-inspect.SKILL.md";

    /// <summary>
    /// Registry of focused skills. Add an entry plus its embedded resource to
    /// expose a new <c>dotnet-inspect skill &lt;name&gt;</c> subcommand.
    /// </summary>
    public static readonly IReadOnlyList<SkillEntry> Skills =
    [
        new SkillEntry(
            "query",
            "Output formats, -D/-S discovery, value projection, @ categories, and output limits shared across commands.",
            "dotnet-inspect.skills.query.md"),
        new SkillEntry(
            "source",
            "Decompiled C#, IL, annotated source, SourceLink original source, and unsafe/IL audits.",
            "dotnet-inspect.skills.source.md"),
        new SkillEntry(
            "performance",
            "Whole-assembly call-graph leverage ranking and performance triage (experimental).",
            "dotnet-inspect.skills.performance.md"),
    ];

    /// <summary>
    /// Prints the base router skill: the baseline skill text followed by a
    /// generated <c>## Skills</c> section listing the focused skills. The skill
    /// list is appended last (after the baseline) so a <c>head -n</c> filter on
    /// the output keeps the high-value baseline content.
    /// </summary>
    public static int Execute()
    {
        var baseline = ReadResource(RouterResourceName);
        if (baseline is null)
        {
            Console.Error.WriteLine($"Error: skill resource '{RouterResourceName}' not found.");
            return 1;
        }

        Console.Write(baseline.TrimEnd('\n'));
        Console.WriteLine();
        Console.WriteLine();

        // HeadingLevelOffset = 1 renders the view's H1 title as an H2 section so
        // it appends cleanly under the baseline skill without a second H1.
        var options = new MarkoutWriterOptions { HeadingLevelOffset = 1 };
        MarkoutSerializer.Serialize(BuildSkillsView(), Console.Out, SkillsViewContext.Default, options);

        return 0;
    }

    /// <summary>
    /// Prints a focused skill by name.
    /// </summary>
    public static int ExecuteSkill(string name)
    {
        var entry = Skills.FirstOrDefault(
            s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            Console.Error.WriteLine($"Error: Unknown skill '{name}'.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Available skills:");
            foreach (var s in Skills)
            {
                Console.Error.WriteLine($"  {s.Name}");
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("Run 'dotnet-inspect skill list' for descriptions.");
            return 1;
        }

        return PrintResource(entry.ResourceName);
    }

    /// <summary>
    /// Lists the focused skills as a standalone Markdown document. Same content
    /// as the trailing Skills section of the base skill, but rendered at H1
    /// (default heading level) since it is the whole document here.
    /// </summary>
    public static int ExecuteList()
    {
        MarkoutSerializer.Serialize(BuildSkillsView(), Console.Out, SkillsViewContext.Default);
        return 0;
    }

    private static SkillsView BuildSkillsView() => new()
    {
        Skills = Skills
            .Select(s => new SkillRow
            {
                Skill = s.Name,
                Description = s.Description,
            })
            .ToList(),
    };

    private static int PrintResource(string resourceName)
    {
        var content = ReadResource(resourceName);
        if (content is null)
        {
            Console.Error.WriteLine($"Error: skill resource '{resourceName}' not found.");
            return 1;
        }

        Console.WriteLine(content);
        return 0;
    }

    private static string? ReadResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
