using System.Runtime.Versioning;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Web.Interop.Analysis;

[SupportedOSPlatform("browser")]
internal static class BrowserCloneCandidateWireProjection
{
    internal static BrowserCloneCandidateResult Project(
        BrowserCloneCandidateRequest request,
        CloneCandidatePresentationResult result) =>
        result switch
        {
            CloneCandidatePresentationResult.Available available =>
                new(
                    1,
                    request,
                    BrowserCloneCandidateResultKind.Available,
                    Project(available.Document),
                    SeedLibrary: null,
                    OpenFailureKind: null,
                    Failure: null,
                    PresentationRejectionKind: null,
                    Subject: null,
                    Detail: null,
                    MetadataRootReason: null),
            CloneCandidatePresentationResult.Rejected rejected =>
                new(
                    1,
                    request,
                    BrowserCloneCandidateResultKind.Rejected,
                    Document: null,
                    Project(rejected.SeedLibrary),
                    Project(rejected.Kind),
                    Failure: null,
                    PresentationRejectionKind: null,
                    Subject: null,
                    rejected.Detail.ToString(),
                    rejected.MetadataRootReason is { } reason
                        ? Project(reason)
                        : null),
            CloneCandidatePresentationResult.Failed failed =>
                new(
                    1,
                    request,
                    BrowserCloneCandidateResultKind.Failed,
                    Document: null,
                    Project(failed.SeedLibrary),
                    OpenFailureKind: null,
                    Project(failed.Failure),
                    PresentationRejectionKind: null,
                    Subject: null,
                    Detail: null,
                    MetadataRootReason: null),
            CloneCandidatePresentationResult.Unrepresentable unrepresentable =>
                new(
                    1,
                    request,
                    BrowserCloneCandidateResultKind.Unrepresentable,
                    Document: null,
                    SeedLibrary: null,
                    OpenFailureKind: null,
                    Failure: null,
                    Project(unrepresentable.Kind),
                    Project(unrepresentable.Subject),
                    unrepresentable.Detail.ToString(),
                    MetadataRootReason: null),
            _ => throw new InvalidOperationException(
                "Unknown Clone Candidates presentation result."),
        };

    static BrowserCloneCandidateDocument Project(
        CloneCandidateDocument document) =>
        new(
            document.SchemaVersion,
            Project(document.Seed),
            Project(document.Breadth),
            Project(document.Discovery),
            document.NameSimilarityThreshold,
            Project(document.Limits),
            document.ScopeChangedDuringSearch,
            document.CoverageIsComplete,
            [.. document.Rows.Select(Project)],
            [.. document.Seeds.Select(Project)],
            [.. document.Libraries.Select(Project)],
            Project(document.Receipt),
            document.ResultLimitReached,
            document.ResultLimitOmittedPairs);

    static BrowserCloneCandidateSeed Project(CloneCandidateSeed seed) =>
        new(
            Project(seed.Kind),
            seed.Type is null ? null : Project(seed.Type),
            seed.Member is null ? null : Project(seed.Member));

    static BrowserMetadataTypeDefinitionName Project(
        MetadataTypeDefinitionName type) =>
        new(type.Namespace, [.. type.Segments]);

    static BrowserCloneMemberAnchor Project(MemberAnchor member) =>
        new(
            member.StableSelector,
            member.CanonicalSignature,
            member.Fingerprint,
            member.TypeFullName,
            member.MemberName);

    static BrowserCloneCandidateRow Project(CloneCandidateRow row) =>
        new(
            row.Rank,
            Project(row.Left),
            Project(row.Right),
            Project(row.Similarity),
            row.NameQualification is null
                ? null
                : new BrowserCloneCandidateNameQualification(
                    row.NameQualification.DeclaringTypeSimilarity,
                    row.NameQualification.MemberSimilarity));

    static BrowserCloneCandidateMethod Project(
        CloneCandidateMethodIdentity method) =>
        new(
            Project(method.Participant),
            method.ModuleVersionId.ToString("D"),
            method.MethodDefinitionToken,
            method.AddressDisplay.ToString());

    static BrowserCloneCandidateParticipant Project(
        CloneCandidateParticipantIdentity participant) =>
        new(
            participant.Ordinal,
            Project(participant.Assembly),
            Project(participant.Provenance),
            participant.ModuleVersionId?.ToString("D"));

    static BrowserCloneCandidateSimilarity Project(
        CloneCandidateSimilarity similarity) =>
        new(
            similarity.Score,
            similarity.OperationScore,
            similarity.PositionScore,
            similarity.BlockScore,
            similarity.EdgeScore,
            similarity.LocalScore,
            similarity.SeedInstructions,
            similarity.CandidateInstructions,
            similarity.SeedBlocks,
            similarity.CandidateBlocks,
            similarity.SeedEdges,
            similarity.CandidateEdges,
            similarity.SeedLocals,
            similarity.CandidateLocals);

    static BrowserCloneCandidateSeedCoverage Project(
        CloneCandidateSeedCoverage coverage) =>
        new(
            Project(coverage.Seed),
            Project(coverage.Disposition),
            coverage.RankedPairs,
            coverage.SuppressedPairs,
            [.. coverage.Blockers.Select(Project)],
            [.. coverage.Failures.Select(Project)],
            coverage.IsComplete);

    static BrowserCloneCandidateLibraryCoverage Project(
        CloneCandidateLibraryCoverage coverage) =>
        new(
            Project(coverage.Participant),
            Project(coverage.Membership),
            coverage.Admitted,
            coverage.CandidateMethods,
            coverage.DiscoveredMethods,
            coverage.RetrievalPairs,
            coverage.NameComparisonWork,
            [.. coverage.Failures.Select(Project)],
            [.. coverage.AnalysisBlockers.Select(Project)],
            coverage.IsComplete);

    static BrowserCloneCandidateFailure Project(
        CloneCandidateFailure failure) =>
        new(
            Project(failure.Kind),
            failure.Subject is null ? null : Project(failure.Subject),
            failure.Detail.ToString());

    static BrowserCloneCandidateAnalysisBlocker Project(
        CloneCandidateAnalysisBlocker blocker) =>
        new(Project(blocker.Kind), blocker.Detail.ToString());

    static BrowserCloneCandidateLimits Project(
        WorkspaceStructuralCloneSearchLimits limits) =>
        new(
            limits.MaximumResults,
            limits.MaximumSeedMethods,
            limits.MaximumCandidateMethods,
            limits.MaximumParticipants,
            limits.MaximumRetrievalPairs,
            limits.MaximumRetrievalChunkMethods,
            limits.MaximumNameCharacters,
            limits.MaximumNameComparisonWork,
            limits.MaximumNameCacheCells,
            limits.ComparisonLimits is null
                ? null
                : new BrowserCloneComparisonLimits(
                    limits.ComparisonLimits.MaximumInstructions,
                    limits.ComparisonLimits.MaximumBlocks,
                    limits.ComparisonLimits.MaximumEdges,
                    limits.ComparisonLimits.MaximumLocals,
                    limits.ComparisonLimits.MaximumVerificationSteps,
                    limits.ComparisonLimits.MaximumBodyBytes,
                    limits.ComparisonLimits
                        .MaximumNearAlignmentIndexSteps,
                    limits.ComparisonLimits
                        .MaximumNearAlignmentCandidates,
                    limits.ComparisonLimits
                        .MaximumNearAlignmentVerificationSteps,
                    limits.ComparisonLimits
                        .MaximumNearAlignmentAlternatives,
                    limits.ComparisonLimits.MaximumNearBlockElements));

    static BrowserCloneCandidateReceipt Project(
        CloneCandidateReceipt receipt) =>
        new(
            receipt.SeedMethods,
            receipt.CandidateMethods,
            receipt.DiscoveredMethods,
            receipt.AdmittedLibraries,
            receipt.ExcludedLibraries,
            receipt.NameComparisonWork,
            receipt.RetrievalPairs,
            receipt.RetrievalCalls,
            receipt.RankedPairs,
            receipt.SuppressedPairs,
            receipt.ReturnedPairs,
            receipt.ResultLimitReached);

    static BrowserCloneAssemblyIdentity Project(
        AssemblyReferenceIdentity identity) =>
        new(
            identity.Name,
            identity.Version?.ToString(),
            identity.Culture,
            identity.PublicKeyToken);

    static BrowserCloneCandidateProvenance Project(
        AssemblyResolutionProvenance provenance) =>
        provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset package =>
                new(
                    BrowserCloneCandidateProvenanceKind.Package,
                    package.PackageId,
                    package.PackageVersion,
                    package.Tfm,
                    package.Rid),
            AssemblyResolutionProvenance.PlatformAsset platform =>
                new(
                    BrowserCloneCandidateProvenanceKind.Platform,
                    Framework: platform.Framework,
                    FrameworkVersion: platform.FrameworkVersion,
                    ResolverSource: platform.ResolverSource),
            AssemblyResolutionProvenance.ProjectAsset project =>
                new(
                    BrowserCloneCandidateProvenanceKind.Project,
                    Tfm: project.Tfm,
                    Rid: project.Rid,
                    Project: project.Project),
            AssemblyResolutionProvenance.LocalAsset local =>
                new(
                    BrowserCloneCandidateProvenanceKind.Local,
                    ResolverSource: local.ResolverSource),
            AssemblyResolutionProvenance.DesignatedAsset designated =>
                new(
                    BrowserCloneCandidateProvenanceKind.Designated,
                    ResolverSource: designated.ResolverSource),
            AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                new(
                    BrowserCloneCandidateProvenanceKind.Embedded,
                    ContentRef: embedded.ContentRef,
                    Digest: embedded.Digest,
                    DeclaredName: embedded.DeclaredName),
            _ => throw new InvalidOperationException(
                "Unknown assembly-resolution provenance."),
        };

    static BrowserCloneCandidateSeedKind Project(
        CloneCandidateSeedKind kind) =>
        kind switch
        {
            CloneCandidateSeedKind.Library =>
                BrowserCloneCandidateSeedKind.Library,
            CloneCandidateSeedKind.Type =>
                BrowserCloneCandidateSeedKind.Type,
            CloneCandidateSeedKind.Member =>
                BrowserCloneCandidateSeedKind.Member,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    internal static BrowserCloneCandidateBreadth Project(
        StructuralCloneCandidateBreadth breadth) =>
        breadth switch
        {
            StructuralCloneCandidateBreadth.Self =>
                BrowserCloneCandidateBreadth.Self,
            StructuralCloneCandidateBreadth
                .SelfAndRegisteredEcosystems =>
                    BrowserCloneCandidateBreadth
                        .SelfAndRegisteredEcosystems,
            StructuralCloneCandidateBreadth.Everything =>
                BrowserCloneCandidateBreadth.Everything,
            _ => throw new ArgumentOutOfRangeException(nameof(breadth)),
        };

    internal static BrowserCloneCandidateDiscovery Project(
        StructuralCloneCandidateDiscovery discovery) =>
        discovery switch
        {
            StructuralCloneCandidateDiscovery.SimilarNames =>
                BrowserCloneCandidateDiscovery.SimilarNames,
            StructuralCloneCandidateDiscovery.All =>
                BrowserCloneCandidateDiscovery.All,
            _ => throw new ArgumentOutOfRangeException(nameof(discovery)),
        };

    static BrowserCloneCandidateParticipantMembership Project(
        StructuralCloneParticipantMembership membership) =>
        membership switch
        {
            StructuralCloneParticipantMembership.ContainingLibrary =>
                BrowserCloneCandidateParticipantMembership
                    .ContainingLibrary,
            StructuralCloneParticipantMembership.RegisteredEcosystem =>
                BrowserCloneCandidateParticipantMembership
                    .RegisteredEcosystem,
            StructuralCloneParticipantMembership.Available =>
                BrowserCloneCandidateParticipantMembership.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(membership)),
        };

    static BrowserCloneCandidateRetrievalDisposition Project(
        StructuralCloneRetrievalDisposition disposition) =>
        disposition switch
        {
            StructuralCloneRetrievalDisposition.Completed =>
                BrowserCloneCandidateRetrievalDisposition.Completed,
            StructuralCloneRetrievalDisposition.Unsupported =>
                BrowserCloneCandidateRetrievalDisposition.Unsupported,
            StructuralCloneRetrievalDisposition.LimitReached =>
                BrowserCloneCandidateRetrievalDisposition.LimitReached,
            StructuralCloneRetrievalDisposition.Failed =>
                BrowserCloneCandidateRetrievalDisposition.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    static BrowserCloneCandidateAnalysisBlockerKind Project(
        StructuralCloneRetrievalBlockerKind kind) =>
        kind switch
        {
            StructuralCloneRetrievalBlockerKind.MetadataReadFailure =>
                BrowserCloneCandidateAnalysisBlockerKind
                    .MetadataReadFailure,
            StructuralCloneRetrievalBlockerKind.MethodLimit =>
                BrowserCloneCandidateAnalysisBlockerKind.MethodLimit,
            StructuralCloneRetrievalBlockerKind.SeedUnsupported =>
                BrowserCloneCandidateAnalysisBlockerKind.SeedUnsupported,
            StructuralCloneRetrievalBlockerKind.SeedProductionLimit =>
                BrowserCloneCandidateAnalysisBlockerKind
                    .SeedProductionLimit,
            StructuralCloneRetrievalBlockerKind.SeedProductionFailure =>
                BrowserCloneCandidateAnalysisBlockerKind
                    .SeedProductionFailure,
            StructuralCloneRetrievalBlockerKind.CandidateProductionLimit =>
                BrowserCloneCandidateAnalysisBlockerKind
                    .CandidateProductionLimit,
            StructuralCloneRetrievalBlockerKind.CandidateProductionFailure =>
                BrowserCloneCandidateAnalysisBlockerKind
                    .CandidateProductionFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserCloneCandidateFailureKind Project(
        StructuralCloneSearchFailureKind kind) =>
        kind switch
        {
            StructuralCloneSearchFailureKind.SeedTypeNotFound =>
                BrowserCloneCandidateFailureKind.SeedTypeNotFound,
            StructuralCloneSearchFailureKind.SeedTypeAmbiguous =>
                BrowserCloneCandidateFailureKind.SeedTypeAmbiguous,
            StructuralCloneSearchFailureKind.SeedMemberNotFound =>
                BrowserCloneCandidateFailureKind.SeedMemberNotFound,
            StructuralCloneSearchFailureKind.SeedMemberAmbiguous =>
                BrowserCloneCandidateFailureKind.SeedMemberAmbiguous,
            StructuralCloneSearchFailureKind.SeedMemberHasNoMethodBody =>
                BrowserCloneCandidateFailureKind
                    .SeedMemberHasNoMethodBody,
            StructuralCloneSearchFailureKind.SeedPopulationLimitReached =>
                BrowserCloneCandidateFailureKind
                    .SeedPopulationLimitReached,
            StructuralCloneSearchFailureKind
                .CandidatePopulationLimitReached =>
                    BrowserCloneCandidateFailureKind
                        .CandidatePopulationLimitReached,
            StructuralCloneSearchFailureKind
                .ParticipantPopulationLimitReached =>
                    BrowserCloneCandidateFailureKind
                        .ParticipantPopulationLimitReached,
            StructuralCloneSearchFailureKind.RetrievalWorkLimitReached =>
                BrowserCloneCandidateFailureKind
                    .RetrievalWorkLimitReached,
            StructuralCloneSearchFailureKind.CandidateLibraryUnavailable =>
                BrowserCloneCandidateFailureKind
                    .CandidateLibraryUnavailable,
            StructuralCloneSearchFailureKind.SeedLibraryReleased =>
                BrowserCloneCandidateFailureKind.SeedLibraryReleased,
            StructuralCloneSearchFailureKind.CandidateLibraryReleased =>
                BrowserCloneCandidateFailureKind
                    .CandidateLibraryReleased,
            StructuralCloneSearchFailureKind.MetadataInspectionFailed =>
                BrowserCloneCandidateFailureKind.MetadataInspectionFailed,
            StructuralCloneSearchFailureKind.NameDecodeFailed =>
                BrowserCloneCandidateFailureKind.NameDecodeFailed,
            StructuralCloneSearchFailureKind.NameWorkLimitReached =>
                BrowserCloneCandidateFailureKind.NameWorkLimitReached,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserCloneCandidateOpenFailureKind Project(
        CandidateOpenFailureKind kind) =>
        kind switch
        {
            CandidateOpenFailureKind.Unreadable =>
                BrowserCloneCandidateOpenFailureKind.Unreadable,
            CandidateOpenFailureKind.InvalidImage =>
                BrowserCloneCandidateOpenFailureKind.InvalidImage,
            CandidateOpenFailureKind.ResourceBudget =>
                BrowserCloneCandidateOpenFailureKind.ResourceBudget,
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                BrowserCloneCandidateOpenFailureKind
                    .UnsupportedMetadataFormat,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static BrowserMetadataRootMalformedReason Project(
        MetadataRootMalformedReason reason) =>
        reason switch
        {
            MetadataRootMalformedReason.UnmappableMetadataDirectory =>
                BrowserMetadataRootMalformedReason
                    .UnmappableMetadataDirectory,
            MetadataRootMalformedReason.TruncatedFixedPrefix =>
                BrowserMetadataRootMalformedReason.TruncatedFixedPrefix,
            MetadataRootMalformedReason.InvalidSignature =>
                BrowserMetadataRootMalformedReason.InvalidSignature,
            MetadataRootMalformedReason.InvalidVersionLength =>
                BrowserMetadataRootMalformedReason.InvalidVersionLength,
            MetadataRootMalformedReason.TruncatedVersionField =>
                BrowserMetadataRootMalformedReason.TruncatedVersionField,
            MetadataRootMalformedReason.MissingVersionTerminator =>
                BrowserMetadataRootMalformedReason
                    .MissingVersionTerminator,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    static BrowserCloneCandidatePresentationRejectionKind Project(
        CloneCandidatePresentationRejectionKind kind) =>
        kind switch
        {
            CloneCandidatePresentationRejectionKind
                .ParticipantCoverageMissing =>
                    BrowserCloneCandidatePresentationRejectionKind
                        .ParticipantCoverageMissing,
            CloneCandidatePresentationRejectionKind
                .ParticipantModuleInconsistent =>
                    BrowserCloneCandidatePresentationRejectionKind
                        .ParticipantModuleInconsistent,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
