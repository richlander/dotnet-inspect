using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Inspector.Artifacts.Workspaces;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.PackageQueries;

public enum PackageAssemblyAssetSequence
{
    Compile,
    Implementation,
}

public readonly record struct PackageAssemblyAssetOccurrence(
    PackageAssemblyAssetSequence Sequence,
    int Ordinal);

public sealed record PackageAssemblyAssetEvidence(
    PackageCompileAssetKind Kind,
    InertString AssemblyName,
    InertString TargetFramework,
    InertString Path);

public sealed record PackageAssemblyEvaluationSubject(
    PackageRootReacquisitionRequest RootRequest,
    PackageContentGenerationIdentity ContentGeneration,
    PackageRootSelectionIdentity Selection,
    PackageAssemblyPatternRequest Pattern)
{
    public RealizedMemberCoordinate.Package Coordinate => RootRequest.Coordinate;
}

public sealed record PackageAssemblySelectedAssetContext(
    PackageAssemblyEvaluationSubject Subject,
    PackageAssemblyAssetOccurrence Occurrence,
    PackageAssemblyAssetEvidence Asset,
    int UnevaluatedSiblings);

public enum PackageAssemblyNotApplicableReason
{
    NoCompileAssets,
    NoMatchingTargetFramework,
    EmptyCompileGroup,
    NoImplementationCounterpart,
}

public enum PackageAssemblyNoMatchKind
{
    SemanticallyConfirmed,
}

public enum PackageAssemblyFailureStage
{
    InvalidAssetSelection,
    InvalidBinding,
    ProjectionContractViolation,
    SelectedEntryUnavailable,
    SelectedEntryByteLimit,
    ArtifactPublication,
    ImageAdmission,
    SemanticIncomplete,
    SemanticDecode,
    UnsupportedProducerInput,
    SemanticWorkLimit,
    SemanticProducerContractViolation,
    CandidateCleanup,
}

public enum PackageAssemblyImageAdmissionStage
{
    Projection,
    Query,
}

public sealed record PackageAssemblyArtifactFailure(
    ArtifactSetAdmissionFailureKind Kind,
    string DiagnosticCode);

public abstract record PackageAssemblyFailureReason
{
    private protected PackageAssemblyFailureReason(PackageAssemblyFailureStage stage) =>
        Stage = stage;

    public PackageAssemblyFailureStage Stage { get; }

    public sealed record InvalidSelection(PackageCompileAssetSelectionStatus Status)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.InvalidAssetSelection);

    public sealed record Contract : PackageAssemblyFailureReason
    {
        internal Contract(PackageAssemblyFailureStage stage) : base(stage) { }
    }

    public sealed record EntryUnavailable()
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.SelectedEntryUnavailable);

    public sealed record EntryByteLimit(long MaximumEntryBytes, long MaximumRetainedImageBytes)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.SelectedEntryByteLimit);

    public sealed record ArtifactPublication(ImmutableArray<PackageAssemblyArtifactFailure> Failures)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.ArtifactPublication);

    public sealed record NotAssembly(
        PackageAssemblyImageAdmissionStage AdmissionStage,
        ArtifactNonAssemblyKind Kind)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.ImageAdmission);

    public sealed record ProjectionRejected(ArtifactAssemblyProjectionFailure Failure)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.ImageAdmission);

    public sealed record QueryRejected(ArtifactAssemblyQueryFailure Failure)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.ImageAdmission);

    public sealed record SemanticRejection : PackageAssemblyFailureReason
    {
        internal SemanticRejection(
            StringLiteralUseRejection rejection,
            StringLiteralUsePatternReceipt receipt)
            : base(rejection.Kind switch
            {
                StringLiteralUseRejectionKind.Incomplete =>
                    PackageAssemblyFailureStage.SemanticIncomplete,
                StringLiteralUseRejectionKind.BoundedDecode =>
                    PackageAssemblyFailureStage.SemanticDecode,
                StringLiteralUseRejectionKind.UnsupportedInput =>
                    PackageAssemblyFailureStage.UnsupportedProducerInput,
                _ => throw new InvalidOperationException("Unknown literal producer rejection."),
            })
        {
            Rejection = rejection;
            Receipt = receipt;
        }

        public StringLiteralUseRejection Rejection { get; }
        public StringLiteralUsePatternReceipt Receipt { get; }
    }

    public sealed record SemanticWorkLimit(
        StringLiteralUseLimitKind Limit,
        StringLiteralUsePatternBudget Budget,
        StringLiteralUsePatternReceipt Receipt)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.SemanticWorkLimit);

    public sealed record CandidateCleanup(PackageAssemblyEvaluationCleanupEvidence Evidence)
        : PackageAssemblyFailureReason(PackageAssemblyFailureStage.CandidateCleanup);
}

public enum PackageAssemblyCandidateCleanupStage
{
    GroupRelease,
    ArtifactSessionRelease,
    CloseReportContract,
    CloseOrchestration,
}

public readonly record struct PackageAssemblyCandidateCleanupFailure(
    PackageAssemblyCandidateCleanupStage Stage,
    int Count);

public sealed record PackageAssemblyEvaluationCleanupEvidence
{
    internal PackageAssemblyEvaluationCleanupEvidence(
        SparsePackageProjectionCleanupReceipt? projectionCleanup,
        ImmutableArray<PackageAssemblyCandidateCleanupFailure> candidateFailures)
    {
        ProjectionCleanup = projectionCleanup;
        CandidateFailures = candidateFailures;
    }

    public SparsePackageProjectionCleanupReceipt? ProjectionCleanup { get; }
    public ImmutableArray<PackageAssemblyCandidateCleanupFailure> CandidateFailures { get; }
    public bool IsEmpty => ProjectionCleanup is null && CandidateFailures.IsEmpty;
}

public static class PackageAssemblyEvaluationExceptionEvidence
{
    static readonly object CleanupKey = new();

    public static bool TryGetCleanup(
        Exception primary,
        [NotNullWhen(true)] out PackageAssemblyEvaluationCleanupEvidence? cleanup)
    {
        ArgumentNullException.ThrowIfNull(primary);
        cleanup = primary.Data[CleanupKey] as PackageAssemblyEvaluationCleanupEvidence;
        return cleanup is not null;
    }

    internal static void Attach(
        Exception primary,
        PackageAssemblyEvaluationCleanupEvidence cleanup)
    {
        if (!cleanup.IsEmpty)
            primary.Data[CleanupKey] = cleanup;
    }
}

public abstract record PackageAssemblyEvaluationOutcome
{
    private protected PackageAssemblyEvaluationOutcome(
        PackageAssemblyEvaluationSubject subject,
        PackageAssemblySelectedAssetContext? selectedAsset)
    {
        Subject = subject;
        SelectedAsset = selectedAsset;
    }

    public PackageAssemblyEvaluationSubject Subject { get; }
    public PackageAssemblySelectedAssetContext? SelectedAsset { get; }

    public sealed record Matched : PackageAssemblyEvaluationOutcome
    {
        internal Matched(
            PackageAssemblySelectedAssetContext context,
            StringLiteralUsePatternResult.Match evidence)
            : base(context.Subject, context) => Evidence = evidence;

        public StringLiteralUsePatternResult.Match Evidence { get; }
    }

    public sealed record NoMatch : PackageAssemblyEvaluationOutcome
    {
        internal NoMatch(
            PackageAssemblySelectedAssetContext context,
            StringLiteralUsePatternReceipt receipt)
            : base(context.Subject, context) => Receipt = receipt;

        public StringLiteralUsePatternReceipt Receipt { get; }
        public PackageAssemblyNoMatchKind Kind => PackageAssemblyNoMatchKind.SemanticallyConfirmed;
    }

    public sealed record NotApplicable : PackageAssemblyEvaluationOutcome
    {
        internal NotApplicable(
            PackageAssemblyEvaluationSubject subject,
            PackageAssemblyNotApplicableReason reason,
            PackageAssemblySelectedAssetContext? primaryCompileAsset = null)
            : base(subject, primaryCompileAsset) => Reason = reason;

        public PackageAssemblyNotApplicableReason Reason { get; }
    }

    public sealed record Failure : PackageAssemblyEvaluationOutcome
    {
        internal Failure(
            PackageAssemblyEvaluationSubject subject,
            PackageAssemblySelectedAssetContext? selectedAsset,
            PackageAssemblyFailureReason reason,
            PackageAssemblyEvaluationCleanupEvidence? cleanup = null)
            : base(subject, selectedAsset)
        {
            Reason = reason;
            Cleanup = cleanup;
        }

        public PackageAssemblyFailureReason Reason { get; }
        public PackageAssemblyEvaluationCleanupEvidence? Cleanup { get; }
    }
}
