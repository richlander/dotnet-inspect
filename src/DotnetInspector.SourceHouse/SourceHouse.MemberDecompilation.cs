using System.Collections.Immutable;

using DotnetInspector.Libraries;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse;

public static partial class SourceHouse
{
    public static ValueTask<SourceHouseMemberDecompilationOutcome>
        ExecuteMemberDecompilationAsync(
            SourceHouseMemberDecompilationRequest request,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationLease);

        SourceHouseMemberDecompilationOutcome outcome;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            outcome = ExecuteMemberDecompilationCore(
                request,
                operationLease,
                cancellationToken);
        }
        finally
        {
            operationLease.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(outcome);
    }

    private static SourceHouseMemberDecompilationOutcome
        ExecuteMemberDecompilationCore(
            SourceHouseMemberDecompilationRequest request,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken)
    {
        SourceHouseMemberDecompilationRequestEvidence evidence =
            new(request);
        var settlement = new SourceHouseLibraryLeaseSettlement(
            SourceHouseLibraryLeaseConsumer.SourceHouse);
        SourceHouseMemberDecompilationWorkCharge emptyWork =
            new(0, 0, 0);
        SourceHousePdbContribution noPdb = PdbUnavailable();

        SourceHouseMemberDecompilationOutcome Rejected(
            SourceHouseRejectionKind kind,
            SourceHousePdbContribution pdb,
            SourceHouseMemberDecompilationWorkCharge work) =>
            new SourceHouseMemberDecompilationOutcome.Rejected(
                evidence,
                pdb,
                new(kind),
                work,
                settlement);
        SourceHouseMemberDecompilationOutcome Failed(
            SourceHouseFailureStage stage,
            string code,
            SourceHousePdbContribution pdb,
            SourceHouseMemberDecompilationWorkCharge work,
            string? detail = null) =>
            new SourceHouseMemberDecompilationOutcome.Failed(
                evidence,
                pdb,
                new(stage, code, detail),
                work,
                settlement);
        SourceHouseMemberDecompilationOutcome Incomplete(
            SourceHouseIncompleteBoundary boundary,
            SourceHousePdbContribution pdb,
            SourceHouseMemberDecompilationWorkCharge work) =>
            new SourceHouseMemberDecompilationOutcome.Incomplete(
                evidence,
                pdb,
                boundary,
                work,
                settlement);

        if (!ReferenceEquals(
                request.SelectedAssembly.Library,
                request.Library))
        {
            return Rejected(
                SourceHouseRejectionKind.LibraryReferenceMismatch,
                noPdb,
                emptyWork);
        }
        if (!request.Library.Contents.Any(
                content => ReferenceEquals(
                    content,
                    request.SelectedAssembly)))
        {
            return Rejected(
                SourceHouseRejectionKind.SelectedContentMismatch,
                noPdb,
                emptyWork);
        }
        if (!request.SelectedAssembly.HasRole(
                LibraryContentRole.ApiAssembly)
            && !request.SelectedAssembly.HasRole(
                LibraryContentRole.ImplementationAssembly))
        {
            return Rejected(
                SourceHouseRejectionKind.SelectedContentRoleMismatch,
                noPdb,
                emptyWork);
        }
        if (!ReferenceEquals(
                operationLease.Reference,
                request.Library))
        {
            return Rejected(
                SourceHouseRejectionKind.LeaseReferenceMismatch,
                noPdb,
                emptyWork);
        }
        LibraryContentReference[] companions =
        [
            .. request.Library.Contents.Where(
                content =>
                    content.HasRole(LibraryContentRole.PortablePdb)
                    && ReferenceEquals(
                        content.AssociatedAssembly,
                        request.SelectedAssembly)),
        ];
        if (companions.Length > 1)
        {
            return Rejected(
                SourceHouseRejectionKind.PortablePdbCompanionAmbiguous,
                new(
                    SourceHousePdbContributionKind.Rejected,
                    content: null,
                    bytesObserved: 0,
                    sourceLinkMap: null,
                    observations: []),
                emptyWork);
        }

        LibraryContentReference? companion =
            companions.Length == 0 ? null : companions[0];
        DetachedContent assembly;
        DetachedContent? portablePdb = null;
        try
        {
            assembly = SnapshotContent(
                operationLease,
                request.SelectedAssembly,
                request.Plan.Limits.MaximumAssemblyBytes,
                SourceHouseFailureStage.AssemblySnapshot,
                SourceHouseNativeObservationStage.Assembly,
                cancellationToken);
            if (companion is not null)
            {
                portablePdb = SnapshotContent(
                    operationLease,
                    companion,
                    request.Plan.Limits.MaximumPortablePdbBytes,
                    SourceHouseFailureStage.PortablePdbSnapshot,
                    SourceHouseNativeObservationStage.PortablePdb,
                    cancellationToken);
            }
        }
        catch (SourceHouseSnapshotException exception)
        {
            string detail = ExceptionDetail(exception.InnerException!);
            return Failed(
                exception.Stage,
                "ContentAccessFailed",
                new(
                    SourceHousePdbContributionKind.Failed,
                    exception.Stage
                        == SourceHouseFailureStage.PortablePdbSnapshot
                            ? companion
                            : null,
                    bytesObserved: 0,
                    sourceLinkMap: null,
                    observations:
                    [
                        new(exception.ObservationStage, detail),
                    ]),
                emptyWork,
                detail);
        }

        var snapshotWork =
            new SourceHouseMemberDecompilationWorkCharge(
                assembly.Length,
                portablePdb?.Length ?? 0,
                0);
        if (assembly.LimitExceeded)
        {
            return Incomplete(
                SourceHouseIncompleteBoundary.AssemblyBytes,
                noPdb,
                snapshotWork);
        }
        ResolvedAssemblyReference? descriptor =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                request.SelectedAssembly.Registration,
                () => new MemoryStream(
                    assembly.Bytes!,
                    writable: false),
                AssemblyResolutionProvenance.Designated(
                    "SourceHouse selected Library content"));
        if (descriptor is null)
        {
            return Failed(
                SourceHouseFailureStage.AssemblyInspection,
                "NotManagedAssembly",
                noPdb,
                snapshotWork);
        }
        ManagedMetadataIdentity.Assembly? expectedIdentity =
            request.SelectedAssembly.AssemblyIdentity;
        if (expectedIdentity is null
            || !descriptor.Identity.IsEquivalentTo(
                expectedIdentity.Identity))
        {
            return Rejected(
                SourceHouseRejectionKind.AssemblyIdentityMismatch,
                noPdb,
                snapshotWork);
        }

        ApiSurface surface;
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(descriptor);
            ApiSurfaceExtractionResult extraction =
                session.BoundedApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll,
                    request.Plan.Limits.TargetBounds);
            if (extraction is ApiSurfaceExtractionResult.Exceeded)
            {
                return Incomplete(
                    SourceHouseIncompleteBoundary.TargetSurface,
                    noPdb,
                    snapshotWork);
            }
            surface =
                ((ApiSurfaceExtractionResult.Extracted)extraction)
                    .Surface;
            if (!TargetExists(surface, request.Target)
                && RequiresCompilerGeneratedSurface(request.Target))
            {
                extraction = session.BoundedApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll,
                    request.Plan.Limits.TargetBounds,
                    includeCompilerGenerated: true);
                if (extraction is ApiSurfaceExtractionResult.Exceeded)
                {
                    return Incomplete(
                        SourceHouseIncompleteBoundary.TargetSurface,
                        noPdb,
                        snapshotWork);
                }
                surface =
                    ((ApiSurfaceExtractionResult.Extracted)extraction)
                        .Surface;
            }
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            string detail = ExceptionDetail(exception);
            return Failed(
                SourceHouseFailureStage.AssemblyInspection,
                "InspectionFailed",
                noPdb,
                snapshotWork,
                detail);
        }

        (
            ApiType Type,
            ApiMember Member,
            bool RequiresAccessorProjection)? target =
            ResolveMemberTarget(surface, request.Target);
        if (target is null)
        {
            if (CreateTargetInspectionFailure(
                    surface,
                    request.Target)
                is { } targetFailure)
            {
                return Failed(
                    targetFailure.Failure.Stage,
                    targetFailure.Failure.Code,
                    targetFailure.PdbContribution,
                    snapshotWork,
                    targetFailure.Failure.Detail);
            }

            return Rejected(
                SourceHouseRejectionKind.TargetMismatch,
                noPdb,
                snapshotWork);
        }

        ImmutableArray<byte>? pdbImage =
            portablePdb is { LimitExceeded: false, Bytes: { } bytes }
            ? ImmutableArray.CreateRange(bytes)
            : null;
        SourceHousePdbContribution pdbContribution;
        if (companion is not null)
        {
            pdbContribution = new(
                portablePdb!.LimitExceeded
                    ? SourceHousePdbContributionKind.Incomplete
                    : SourceHousePdbContributionKind.SuppliedCompanion,
                companion,
                portablePdb!.Length,
                sourceLinkMap: null,
                observations: []);
        }
        else
        {
            (pdbImage, pdbContribution) = EmbeddedPdb(
                descriptor,
                request.Plan.Limits,
                cancellationToken);
            snapshotWork = snapshotWork with
            {
                PortablePdbBytesObserved =
                    pdbContribution.BytesObserved,
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        List<ApiMember>? originalMembers = null;
        if (target.Value.RequiresAccessorProjection)
        {
            originalMembers = target.Value.Type.Members;
            target.Value.Type.Members = [target.Value.Member];
        }

        CSharpDecompilationAttempt attempt;
        try
        {
            attempt =
                CSharpDecompilerService.ProduceMember(
                    target.Value.Type,
                    target.Value.Member,
                    descriptor,
                    request.Plan.BindingPolicy,
                    pdbImage,
                    request.Plan.PrinterOptions,
                    request.Plan.MaximumBodyProjections,
                    cancellationToken);
        }
        finally
        {
            if (originalMembers is not null)
                target.Value.Type.Members = originalMembers;
        }
        var completedWork = snapshotWork with
        {
            BodyProjectionsAttempted =
                attempt.BodyProjectionsAttempted,
        };
        return new SourceHouseMemberDecompilationOutcome.Completed(
            evidence,
            pdbContribution,
            attempt,
            completedWork,
            settlement);
    }

    private static (
        ImmutableArray<byte>? PdbImage,
        SourceHousePdbContribution Contribution)
        EmbeddedPdb(
            ResolvedAssemblyReference descriptor,
            SourceHouseMemberDecompilationLimits limits,
            CancellationToken cancellationToken)
    {
        SourceLinkService? source = null;
        (
            ImmutableArray<byte>? PdbImage,
            SourceHousePdbContribution Contribution) result;
        try
        {
            source = SourceLinkService.OpenEmbeddedPdbOnly(
                descriptor,
                limits.EmbeddedPdbReadLimits);
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<byte>? image =
                source.Context.GetPortablePdbImage();
            long bytes = source.Context.EmbeddedPdbSize;
            result = (
                image,
                new(
                    image.HasValue
                        ? SourceHousePdbContributionKind.Embedded
                        : SourceHousePdbContributionKind.Unavailable,
                    content: null,
                    bytes,
                    sourceLinkMap: null,
                    observations: []));
        }
        catch (PdbResourceLimitException exception)
        {
            result = (
                null,
                new(
                    SourceHousePdbContributionKind.Incomplete,
                    content: null,
                    exception.ActualBytes,
                    sourceLinkMap: null,
                    observations:
                    [
                        new(
                            SourceHouseNativeObservationStage.PortablePdb,
                            ExceptionDetail(exception)),
                    ]));
        }
        catch (OperationCanceledException)
        {
            source?.DisposeWithFailure();
            throw;
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            result = (
                null,
                new(
                    SourceHousePdbContributionKind.Failed,
                    content: null,
                    bytesObserved: 0,
                    sourceLinkMap: null,
                    observations:
                    [
                        new(
                            SourceHouseNativeObservationStage.PortablePdb,
                            ExceptionDetail(exception)),
                    ]));
        }
        catch
        {
            source?.DisposeWithFailure();
            throw;
        }

        Exception? cleanup = source?.DisposeWithFailure();
        if (cleanup is not null)
        {
            return (
                null,
                new(
                    SourceHousePdbContributionKind.Failed,
                    content: null,
                    result.Contribution.BytesObserved,
                    sourceLinkMap: null,
                    observations:
                    [
                        .. result.Contribution.Observations,
                        new(
                            SourceHouseNativeObservationStage.Disposal,
                            ExceptionDetail(cleanup)),
                    ]));
        }

        return result;
    }
}
