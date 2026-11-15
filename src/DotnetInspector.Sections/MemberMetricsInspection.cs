using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

[Flags]
public enum MemberMetricEvidenceKind
{
    None = 0,
    BodySize = 1,
    SiblingCalls = 2,
}

public enum MemberMetricField
{
    LargestPhysicalIlBytes,
    IncomingSiblingCallers,
    OutgoingSiblingTargets,
    SiblingRelationships,
}

public enum MemberMetricEvidenceState
{
    NotRequested,
    Available,
    Bodyless,
    Unavailable,
    Incomplete,
    Failed,
}

public enum MemberPhysicalBodyRole
{
    Declared,
    StateMachineExecutionBody,
    LiftedExecutionBody,
}

public sealed record MemberPhysicalBodySizeEvidence(
    int PhysicalMethodToken,
    int DeclaredMemberToken,
    MemberPhysicalBodyRole Role,
    bool IsAsyncStateMachine,
    bool HasManagedBody,
    int? EncodedIlBytes,
    MemberMetricEvidenceState State,
    string? Detail);

public sealed record MemberBodySizeMetrics(
    int? LargestPhysicalIlBytes,
    ImmutableArray<MemberPhysicalBodySizeEvidence> PhysicalBodies);

public sealed record MemberSiblingCallMetrics(
    int? IncomingSiblingCallers,
    int? OutgoingSiblingTargets);

public sealed record MemberMetricRowIdentity(
    int MetadataToken,
    MemberAnchor Anchor,
    int BaselineOrdinal);

public sealed record MemberSiblingCallRelationship(
    MemberMetricRowIdentity Caller,
    MemberMetricRowIdentity Callee,
    int InvocationCount,
    ImmutableArray<int> PhysicalCallerMethodTokens);

public sealed record MemberSiblingRelationships(
    ImmutableArray<MemberSiblingCallRelationship> Incoming,
    ImmutableArray<MemberSiblingCallRelationship> Outgoing);

public sealed record MemberMetricsRow(
    MemberOverloadShape Member,
    MemberMetricEvidenceState BodySizeState,
    MemberMetricEvidenceState SiblingCallsState,
    MemberBodySizeMetrics? BodySize,
    MemberSiblingCallMetrics? SiblingCalls,
    MemberSiblingRelationships? Relationships);

public abstract record MemberMetricsPopulationResult
{
    private protected MemberMetricsPopulationResult()
    {
    }

    public sealed record Rows(
        ImmutableArray<MemberMetricsRow> Items)
        : MemberMetricsPopulationResult;

    public sealed record Count : MemberMetricsPopulationResult
    {
        public Count(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public int Value { get; }
    }
}

public sealed record MemberMetricEvidenceReceipt(
    MemberMetricEvidenceKind Authorized,
    MemberMetricEvidenceKind Requested,
    MemberMetricEvidenceKind Effective,
    ImmutableArray<MemberMetricField> ProjectedFields);

public sealed record MemberMetricEvidenceExecutionReceipt(
    MethodDefinitionSourceGroupReceipt Source,
    WorkReceipt? BodySize,
    WorkReceipt? SiblingCalls,
    ImmutableArray<ExactMethodSemanticLaneFailure> Failures);

public enum MemberMetricsCompletion
{
    Complete,
    Incomplete,
}

public sealed record MemberMetricsCompletedContent(
    MemberGroupSubject Subject,
    MemberOverloadPopulationBinding Population,
    MemberMetricEvidenceReceipt Evidence,
    MemberMetricEvidenceExecutionReceipt Execution,
    MemberMetricsPopulationResult Result,
    MemberMetricsCompletion Completion);

public enum MemberMetricsInspectionRejection
{
    LeaseReferenceMismatch,
    PopulationGenerationMismatch,
    AssemblyIdentityMismatch,
    ModuleVersionMismatch,
    AssemblyLengthMismatch,
    PopulationSubjectMismatch,
    PopulationRowsRequired,
    PopulationIncomplete,
    InvalidPopulation,
    QueryRejected,
    UnauthorizedEvidence,
    NoEffectiveEvidence,
}

public enum MemberMetricsInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    EmptyModuleVersionId,
    MalformedMetadata,
    AnalysisFailed,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(MemberMetricsInspectionContent.Completed),
    "completed")]
[JsonDerivedType(
    typeof(MemberMetricsInspectionContent.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(MemberMetricsInspectionContent.Failed),
    "failed")]
public abstract record MemberMetricsInspectionContent
{
    private protected MemberMetricsInspectionContent()
    {
    }

    public sealed record Completed(
        MemberMetricsCompletedContent Content)
        : MemberMetricsInspectionContent;

    public sealed record Rejected(
        MemberMetricsInspectionRejection Reason,
        string Detail)
        : MemberMetricsInspectionContent;

    public sealed record Failed(
        MemberMetricsInspectionFailure Reason,
        string Detail)
        : MemberMetricsInspectionContent;
}

public sealed record MemberMetricsInspectionRequest
{
    public MemberMetricsInspectionRequest(
        LibraryReference library,
        MemberOverloadPopulationContent population,
        MemberMetricEvidenceKind authorizedEvidence,
        QuerySpaceRequest query,
        MethodDefinitionGeneratedExpansionLimits generatedExpansionLimits,
        MethodDefinitionTerminalWorkLimits terminalWorkLimits)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
        if ((authorizedEvidence
            & ~(MemberMetricEvidenceKind.BodySize
                | MemberMetricEvidenceKind.SiblingCalls)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorizedEvidence),
                authorizedEvidence,
                "Unknown Member metric evidence authorization.");
        }

        AuthorizedEvidence = authorizedEvidence;
        Query = query
            ?? throw new ArgumentNullException(nameof(query));
        GeneratedExpansionLimits = generatedExpansionLimits
            ?? throw new ArgumentNullException(
                nameof(generatedExpansionLimits));
        TerminalWorkLimits = terminalWorkLimits
            ?? throw new ArgumentNullException(
                nameof(terminalWorkLimits));
    }

    public LibraryReference Library { get; }

    public MemberOverloadPopulationContent Population { get; }

    public MemberMetricEvidenceKind AuthorizedEvidence { get; }

    public QuerySpaceRequest Query { get; }

    public MethodDefinitionGeneratedExpansionLimits
        GeneratedExpansionLimits
    { get; }

    public MethodDefinitionTerminalWorkLimits TerminalWorkLimits
    { get; }
}
