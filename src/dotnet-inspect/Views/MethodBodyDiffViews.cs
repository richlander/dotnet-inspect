using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using InertText;
using Markout;

namespace DotnetInspector.Views;

public enum MethodBodyDiffStage
{
    Query,
    Research,
}

public enum MethodBodyEndpointState
{
    NotInspected,
    Complete,
    SubjectAbsent,
    NoApplicableInput,
    Failed,
}

public sealed record MethodBodyDiffDocument(
    string Before,
    string After,
    MethodBodyDiffStage Stage,
    string Outcome,
    MethodBodyDiffDiagnostic? Diagnostic,
    ImmutableArray<MethodBodyProducerDocument> Producers,
    ImmutableArray<MethodBodyCleanupDocument> Cleanup)
{
    public int? WorkItemCount { get; init; }
    public bool HasFailures { get; init; }

    [JsonIgnore]
    public ResearchProducerSessionOutcome? NativeOutcome { get; init; }
}

public sealed record MethodBodyDiffDiagnostic(
    string Kind,
    string? Side,
    string Detail,
    ResearchProducerKind? Producer = null)
{
    [JsonIgnore]
    public ResearchComparisonInputId? Input { get; init; }
}

public sealed record MethodBodyEndpointDocument(
    ResearchComparisonSide Side,
    MethodBodyEndpointState State)
{
    public ResearchTargetOutcomeKind? TargetState { get; init; }
    public MetadataMethodAddress? Address { get; init; }
    public MemberAnchor? Anchor { get; init; }
    public FindingSubject? CSharpSubject { get; init; }
    public IlMemberDiffSubject? IlSubject { get; init; }
    public string? Detail { get; init; }
    public InspectionError? Error { get; init; }
    public int? FindingCount { get; init; }

    [JsonIgnore]
    public ResearchTargetAttempt? Attempt { get; init; }
}

public sealed record MethodBodyFindingsDocument(
    string Outcome,
    bool? IsExact,
    FindingInspectionTransition? Transition,
    FindingMatch? Match,
    int? PairCount,
    string? Failure);

// Owner-defined absent arrays serialize as empty; the original payloads remain on the producer.
public sealed record MethodBodyCSharpEvidence(
    bool IsExact,
    ImmutableArray<CSharpDiffRow> Rows,
    ImmutableArray<CSharpDiffFailureRow> FailureRows,
    ImmutableArray<CSharpIdentityResolutionFailure> IdentityFailures);

public sealed record MethodBodyIlEvidence(
    IlMemberDiffSubject Old,
    IlMemberDiffSubject New,
    IlBodyDiffOutcome Outcome,
    bool IsExact,
    bool IsAvailable,
    string? Failure,
    ImmutableArray<IlDiffRow> Rows,
    ImmutableArray<IlDiffFailureRow> FailureRows,
    ImmutableArray<IlIdentityResolutionFailure> IdentityFailures);

public sealed record MethodBodyProducerDocument(
    ResearchProducerKind Producer,
    string Basis,
    string Outcome,
    string NativeVerdict,
    MethodBodyEndpointDocument Before,
    MethodBodyEndpointDocument After)
{
    public MethodBodyFindingsDocument? Findings { get; init; }
    public MethodBodyCSharpEvidence? CSharp { get; init; }
    public MethodBodyIlEvidence? Il { get; init; }
    public MethodBodyDiffDiagnostic? Diagnostic { get; init; }

    [JsonIgnore]
    public ResearchProducerWorkResult? NativeWork { get; init; }

    [JsonIgnore]
    public CSharpBodyDiffResult? NativeCSharp { get; init; }

    [JsonIgnore]
    public IlMemberDiffResult? NativeIl { get; init; }
}

public sealed record MethodBodyCleanupDocument(
    ResearchComparisonSide Side,
    string Outcome,
    MethodBodyDiffDiagnostic? Diagnostic)
{
    [JsonIgnore]
    public ResearchProducerCleanupOutcome? NativeCleanup { get; init; }
}

[MarkoutSerializable(TitleProperty = nameof(Title))]
public sealed class MethodBodyDiffView(
    InertString title,
    InertString stage,
    InertString outcome,
    InertString summary)
{
    [MarkoutIgnore] public string Title => title.ToString();
    public string Stage => stage.ToString();
    public string Outcome => outcome.ToString();
    public string Summary => summary.ToString();

    [MarkoutSection(Name = "Producers")]
    public List<MethodBodyProducerRow>? Producers { get; init; }

    [MarkoutSection(Name = "Endpoints")]
    public List<MethodBodyEndpointRow>? Endpoints { get; init; }

    [MarkoutSection(Name = "Body Evidence")]
    public List<MethodBodyEvidenceRow>? Evidence { get; init; }

    [MarkoutSection(Name = "Diagnostics")]
    public List<MethodBodyDiagnosticRow>? Diagnostics { get; init; }

    [MarkoutSection(Name = "Cleanup")]
    public List<MethodBodyCleanupRow>? Cleanup { get; init; }
}

[MarkoutSerializable]
public sealed record MethodBodyProducerRow(
    [property: MarkoutIgnore, JsonIgnore] InertString ProducerText,
    [property: MarkoutIgnore, JsonIgnore] InertString WorkText,
    [property: MarkoutIgnore, JsonIgnore] InertString NativeVerdictText,
    [property: MarkoutIgnore, JsonIgnore] InertString BeforeText,
    [property: MarkoutIgnore, JsonIgnore] InertString AfterText,
    [property: MarkoutIgnore, JsonIgnore] InertString FindingsText)
{
    public string Producer => ProducerText.ToString();
    public string Work => WorkText.ToString();
    public string NativeVerdict => NativeVerdictText.ToString();
    public string Before => BeforeText.ToString();
    public string After => AfterText.ToString();
    public string Findings => FindingsText.ToString();
}

[MarkoutSerializable]
public sealed record MethodBodyEndpointRow(
    [property: MarkoutIgnore, JsonIgnore] InertString ProducerText,
    [property: MarkoutIgnore, JsonIgnore] InertString SideText,
    [property: MarkoutIgnore, JsonIgnore] InertString MemberText,
    [property: MarkoutIgnore, JsonIgnore] InertString StateText,
    [property: MarkoutIgnore, JsonIgnore] InertString AddressText,
    [property: MarkoutIgnore, JsonIgnore] InertString DetailText)
{
    public string Producer => ProducerText.ToString();
    public string Side => SideText.ToString();
    public string State => StateText.ToString();
    public string Address => AddressText.ToString();
    public string Member => MemberText.ToString();
    public string Detail => DetailText.ToString();
}

[MarkoutSerializable]
public sealed record MethodBodyEvidenceRow(
    [property: MarkoutIgnore, JsonIgnore] InertString ProducerText,
    [property: MarkoutIgnore, JsonIgnore] InertString EvidenceText)
{
    public string Producer => ProducerText.ToString();
    public string Evidence => EvidenceText.ToString();
}

[MarkoutSerializable]
public sealed record MethodBodyDiagnosticRow(
    [property: MarkoutIgnore, JsonIgnore] InertString ProducerText,
    [property: MarkoutIgnore, JsonIgnore] InertString SideText,
    [property: MarkoutIgnore, JsonIgnore] InertString KindText,
    [property: MarkoutIgnore, JsonIgnore] InertString DetailText)
{
    public string Producer => ProducerText.ToString();
    public string Side => SideText.ToString();
    public string Kind => KindText.ToString();
    public string Detail => DetailText.ToString();
}

[MarkoutSerializable]
public sealed record MethodBodyCleanupRow(
    [property: MarkoutIgnore, JsonIgnore] InertString SideText,
    [property: MarkoutIgnore, JsonIgnore] InertString OutcomeText)
{
    public string Side => SideText.ToString();
    public string Outcome => OutcomeText.ToString();
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(MethodBodyDiffView))]
[MarkoutContext(typeof(MethodBodyProducerRow))]
[MarkoutContext(typeof(MethodBodyEndpointRow))]
[MarkoutContext(typeof(MethodBodyEvidenceRow))]
[MarkoutContext(typeof(MethodBodyDiagnosticRow))]
[MarkoutContext(typeof(MethodBodyCleanupRow))]
public partial class MethodBodyDiffViewContext : MarkoutSerializerContext
{
}
