using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Renders the focused-skill registry as a trailing <c>## Skills</c> section for
/// the base skill output. Serialized with <see cref="MarkoutWriterOptions.HeadingLevelOffset"/>
/// set to 1 so the document title drops from H1 to H2 ("print a section, elide
/// the H1") and the block can be appended under the baseline skill text. The
/// intro description explains how to print a focused skill, which lets the table
/// stay a clean two columns.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public class SkillsView
{
    [MarkoutIgnore]
    public string Title => "Skills";

    [MarkoutIgnore]
    public string Description =>
        "Targeted skills are available for more advanced topics. Print one with "
        + "`dotnet-inspect skill <name>` when a task needs it, or run "
        + "`dotnet-inspect skill list` to see them all.";

    [MarkoutSection(Headless = true)]
    public List<SkillRow> Skills { get; set; } = [];
}

public class SkillRow
{
    [MarkoutPropertyName("Skill")]
    public string Skill { get; set; } = "";

    [MarkoutPropertyName("Use it for")]
    public string UseFor { get; set; } = "";
}

[MarkoutContext(typeof(SkillsView))]
public partial class SkillsViewContext : MarkoutSerializerContext
{
}
