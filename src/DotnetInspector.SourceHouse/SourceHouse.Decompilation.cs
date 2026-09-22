using System.Collections.Immutable;

using DotnetInspector.Libraries;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse;

public static partial class SourceHouse
{
    public static ValueTask<SourceHouseDecompilationOutcome>
        ExecuteDecompilationAsync(
            SourceHouseDecompilationRequest request,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationLease);

        SourceHouseDecompilationOutcome outcome;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            outcome = ExecuteDecompilationCore(
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

    private static SourceHouseDecompilationOutcome
        ExecuteDecompilationCore(
            SourceHouseDecompilationRequest request,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken)
    {
        SourceHouseDecompilationRequestEvidence evidence =
            new(request);
        var settlement = new SourceHouseLibraryLeaseSettlement(
            SourceHouseLibraryLeaseConsumer.SourceHouse);
        SourceHouseDecompilationWorkCharge emptyWork =
            new(0, 0, 0);
        SourceHousePdbContribution noPdb = PdbUnavailable();

        SourceHouseDecompilationOutcome Rejected(
            SourceHouseRejectionKind kind,
            SourceHousePdbContribution pdb,
            SourceHouseDecompilationWorkCharge work) =>
            new SourceHouseDecompilationOutcome.Rejected(
                evidence,
                pdb,
                new(kind),
                work,
                settlement);
        SourceHouseDecompilationOutcome Failed(
            SourceHouseFailureStage stage,
            string code,
            SourceHousePdbContribution pdb,
            SourceHouseDecompilationWorkCharge work,
            string? detail = null) =>
            new SourceHouseDecompilationOutcome.Failed(
                evidence,
                pdb,
                new(stage, code, detail),
                work,
                settlement);
        SourceHouseDecompilationOutcome Incomplete(
            SourceHouseIncompleteBoundary boundary,
            SourceHousePdbContribution pdb,
            SourceHouseDecompilationWorkCharge work) =>
            new SourceHouseDecompilationOutcome.Incomplete(
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
            new SourceHouseDecompilationWorkCharge(
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
            ApiMember? Member,
            bool RequiresAccessorProjection)? target =
            ResolveDecompilationTarget(surface, request.Target);
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
            target.Value.Type.Members = [target.Value.Member!];
        }

        SourceHouseDecompilationContent content;
        int bodyProjectionsAttempted;
        try
        {
            switch (request.Product)
            {
                case SourceHouseDecompilationProduct
                    .StructuredTypeDocument:
                {
                    CSharpTypeDocumentOutcome document =
                        CSharpDecompilerService.ProduceTypeDocument(
                            target.Value.Type,
                            descriptor,
                            request.Plan.BindingPolicy,
                            pdbImage,
                            request.Plan.PrinterOptions,
                            request.Plan.MaximumBodyProjections,
                            cancellationToken);
                    content = new SourceHouseDecompilationContent
                        .StructuredTypeDocument(document);
                    bodyProjectionsAttempted =
                        document.BodyProjectionsAttempted;
                    break;
                }
                case SourceHouseDecompilationProduct.SourceText:
                {
                    CSharpDecompilationAttempt attempt =
                        target.Value.Member is { } member
                        ? CSharpDecompilerService.ProduceMember(
                            target.Value.Type,
                            member,
                            descriptor,
                            request.Plan.BindingPolicy,
                            pdbImage,
                            request.Plan.PrinterOptions,
                            request.Plan.MaximumBodyProjections,
                            cancellationToken)
                        : CSharpDecompilerService.ProduceType(
                            target.Value.Type,
                            descriptor,
                            request.Plan.BindingPolicy,
                            pdbImage,
                            request.Plan.PrinterOptions,
                            request.Plan.MaximumBodyProjections,
                            cancellationToken);
                    content = new SourceHouseDecompilationContent
                        .SourceText(attempt);
                    bodyProjectionsAttempted =
                        attempt.BodyProjectionsAttempted;
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        "Unknown SourceHouse decompilation product.");
            }
        }
        finally
        {
            if (originalMembers is not null)
                target.Value.Type.Members = originalMembers;
        }
        var completedWork = snapshotWork with
        {
            BodyProjectionsAttempted =
                bodyProjectionsAttempted,
        };
        return new SourceHouseDecompilationOutcome.Completed(
            evidence,
            pdbContribution,
            content,
            completedWork,
            settlement);
    }

    private static (
        ImmutableArray<byte>? PdbImage,
        SourceHousePdbContribution Contribution)
        EmbeddedPdb(
            ResolvedAssemblyReference descriptor,
            SourceHouseDecompilationLimits limits,
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

    private static (
        ApiType Type,
        ApiMember? Member,
        bool RequiresAccessorProjection)?
        ResolveDecompilationTarget(
            ApiSurface surface,
            SourceHouseTarget target)
    {
        if (target is SourceHouseTarget.MemberTarget member)
        {
            (ApiType Type, ApiMember Member, bool RequiresAccessorProjection)?
                resolved = ResolveMemberTarget(surface, member);
            return resolved is { } exact
                ? (exact.Type, exact.Member, exact.RequiresAccessorProjection)
                : null;
        }

        ApiType[] types =
        [
            .. surface.Types.Where(
                candidate => candidate.DefinitionName == target.Type),
        ];
        return types.Length == 1
            ? (types[0], null, false)
            : null;
    }
}
