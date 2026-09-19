using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using Inspector.Findings;

namespace ILInspector.Research;

public sealed record FactRow(
    string Member,
    int? ILOffset,
    int? CSharpLine,
    string Anchor,
    string Category,
    string Id,
    string? Detail,
    string Conditionality,
    FindingCensusReceipt? CensusReceipt = null,
    FindingInstanceKey? InstanceKey = null,
    ResearchFindingEvidence? Evidence = null);

public sealed record AnnotatedSourceFactIdentity
{
    public AnnotatedSourceFactIdentity(
        int factId,
        FindingCensusReceipt censusReceipt,
        FindingInstanceKey instanceKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(factId);
        if (censusReceipt.IsDefault)
        {
            throw new ArgumentException(
                "Census receipt must be producer-issued and non-default.",
                nameof(censusReceipt));
        }
        if (instanceKey.IsDefault)
        {
            throw new ArgumentException(
                "Instance key must be producer-issued and non-default.",
                nameof(instanceKey));
        }

        FactId = factId;
        CensusReceipt = censusReceipt;
        InstanceKey = instanceKey;
    }

    public int FactId { get; }
    public FindingCensusReceipt CensusReceipt { get; }
    public FindingInstanceKey InstanceKey { get; }
}

public enum AllocationExceptionPathKind
{
    ThrownValue,
    ExceptionHandler,
}

public sealed record AnnotatedSourceAllocationExceptionPath(
    int FactId,
    AllocationExceptionPathKind Kind);

public sealed record CostOverlayResult(
    DecompilerResult Body,
    IReadOnlyList<ResearchHeaderFact> HeaderFacts);

public sealed record MemberProjectionRequest(
    MetadataSource Source,
    string Type,
    string Method,
    int OverloadIndex = 0,
    bool PublicOnly = false,
    bool AnnotatedSource = false,
    bool CostOverlay = false,
    bool SemanticsOverlay = false,
    bool FactRows = false,
    AnnotationStage AnnotatedStage = AnnotationStage.Raised,
    ResearchFactRegistry? Registry = null,
    int? MethodToken = null,
    PrinterOptions? PrinterOptions = null,
    string? CaretFocus = null,
    bool SourceDocument = false,

    /// <summary>
    /// The focused Analysis results fact producers observe through. Supplied by
    /// the host that owns the Analysis execution. Null retains path-backed
    /// compatibility behavior, or a consistent absence for pathless content.
    /// </summary>
    MemberProjectionAnalysisInput? Analysis = null,
    IReadOnlyList<DirectCall>? CallSites = null);

public sealed record MemberProjectionResult(
    DecompilerResult? AnnotatedSource,
    CostOverlayResult? CostOverlay,
    DecompilerResult? SemanticsOverlay,
    IReadOnlyList<FactRow>? Facts,
    DecompilerTrace? Trace,
    /// <summary>
    /// Set when a caret focus was requested and promoted nothing: the fact
    /// families this member actually has, so the caller can tell a typo from
    /// an honest absence. Null when no focus was asked for, or when the focus
    /// matched.
    /// </summary>
    IReadOnlyList<string>? UnmatchedFocusAlternatives = null,

    /// <summary>
    /// Portable interleaved source, produced only when
    /// <see cref="MemberProjectionRequest.SourceDocument"/> is requested.
    /// </summary>
    AnnotatedSourceDocument? SourceDocument = null,

    /// <summary>
    /// Failure isolated to portable-document production. Sibling projections
    /// remain available when the document's C# printer cannot produce output.
    /// </summary>
    DecompilerResult? SourceDocumentFailure = null,

    /// <summary>The MethodDef token selected for this projection.</summary>
    int? SelectedMethodToken = null,

    /// <summary>
    /// Research-owned Finding identities for body facts in
    /// <see cref="SourceDocument"/>, joined by document-local fact id.
    /// </summary>
    IReadOnlyList<AnnotatedSourceFactIdentity>?
        SourceDocumentFactIdentities = null,

    /// <summary>
    /// Receipt for the one body Finding census successfully collected by this
    /// member operation, including a successful empty census.
    /// </summary>
    FindingCensusReceipt? FactCensusReceipt = null,

    /// <summary>
    /// Product-issued C# node ids for classic awaits whose inline and
    /// suspension/resume paths were proven by reconstruction.
    /// </summary>
    IReadOnlyList<int>? AwaitCompletionPathNodeIds = null,

    /// <summary>
    /// Exact allocation facts Analysis placed on a thrown-value or
    /// exception-handler path.
    /// </summary>
    IReadOnlyList<AnnotatedSourceAllocationExceptionPath>?
        AllocationExceptionPaths = null);
