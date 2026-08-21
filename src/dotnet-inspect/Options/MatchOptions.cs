namespace DotnetInspector.Options;

/// <summary>
/// Options for the <c>match</c> command: pairwise structural-clone correspondence between two
/// methods in one retained assembly, projected through
/// <see cref="ILInspector.Research.ResearchMatch"/>.
/// </summary>
public record MatchOptions : ApiOptions
{
    /// <summary>The first method selector (<c>Type.Member</c>), positional.</summary>
    public string? LeftSelector { get; init; }

    /// <summary>The second method selector (<c>Type.Member</c>), positional.</summary>
    public string? RightSelector { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public override bool IsRawOutput => Bare || JsonOutput || Tabular || Jsonl || NoHeader || Count;
}
