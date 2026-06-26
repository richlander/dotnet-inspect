using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Renders the focused-skill registry as a trailing <c>## Skills</c> section for
/// the base skill output. Kept as a section (not a document title) so it renders
/// at heading level 2 and can be appended after the baseline skill text.
/// </summary>
[MarkoutSerializable]
public class SkillsView
{
    [MarkoutSection(Name = "Skills")]
    public List<SkillRow> Skills { get; set; } = [];
}

public class SkillRow
{
    [MarkoutPropertyName("Skill")]
    public string Skill { get; set; } = "";

    [MarkoutPropertyName("Print it with")]
    public string Command { get; set; } = "";

    [MarkoutPropertyName("Use it for")]
    public string Description { get; set; } = "";
}

[MarkoutContext(typeof(SkillsView))]
public partial class SkillsViewContext : MarkoutSerializerContext
{
}
