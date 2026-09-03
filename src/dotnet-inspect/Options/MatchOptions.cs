namespace DotnetInspector.Options;

/// <summary>
/// Options for the <c>match</c> command. Pairwise mode reports structural-clone correspondence
/// between two methods in one retained assembly, projected through
/// <see cref="ILInspector.Research.ResearchMatch"/>. Discovery mode (<see cref="Similar"/>) ranks a
/// bounded candidate population against one seed (issue #4740).
/// </summary>
public record MatchOptions : ApiOptions
{
    /// <summary>The first method selector (<c>Type.Member</c>), positional.</summary>
    public string? LeftSelector { get; init; }

    /// <summary>
    /// The second positional argument. In pairwise mode it is the second method selector
    /// (<c>Type.Member</c>). In discovery mode (<see cref="Similar"/>) it is the candidate type
    /// scope, and defaults to the seed's declaring type when omitted.
    /// </summary>
    public string? RightSelector { get; init; }

    /// <summary>
    /// Switch <c>match</c> from pairwise comparison to seeded discovery: rank the candidate
    /// population by structural similarity to the seed named by <see cref="LeftSelector"/>
    /// (issue #4740). This is a thin consumer of
    /// <see cref="DotnetInspector.Queries.AssemblyContextStructuralCloneRetrievalQuery"/>.
    /// </summary>
    public bool Similar { get; init; }

    /// <summary>
    /// Discovery mode: rank every method in the candidate assembly rather than the methods of one
    /// type. Type-scoped retrieval is the normal bounded path, so whole-assembly search is opt-in.
    /// </summary>
    public bool AssemblyWide { get; init; }

    /// <summary>
    /// Discovery mode: the number of ranked rows rendered as text. This bounds presentation only;
    /// it never changes <see cref="MaximumResults"/> and never truncates JSON evidence.
    /// </summary>
    public int? Top { get; init; }

    /// <summary>
    /// Discovery mode: the product retrieval limit
    /// (<see cref="ILInspector.Analysis.StructuralCloneRetrievalLimits.MaximumResults"/>). This is
    /// orthogonal to <see cref="Top"/> and is always reported in the rendered output.
    /// </summary>
    public int? MaximumResults { get; init; }

    /// <summary>
    /// Discovery mode: the product method-scan limit
    /// (<see cref="ILInspector.Analysis.StructuralCloneRetrievalLimits.MaximumMethods"/>).
    /// </summary>
    public int? MaximumMethods { get; init; }

    /// <summary>
    /// Additionally decompile both members and render a Research-owned side-by-side C#/IL
    /// implementation-diff view alongside the verified structural clone relation (issue #4304
    /// Slice 4). Decompilation is CPU-expensive, so this stays out of the default view.
    /// </summary>
    public bool IncludeImplementation { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public override bool IsRawOutput => Bare || JsonOutput || Tabular || Jsonl || NoHeader || Count;
}
