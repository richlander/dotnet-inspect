using System.Collections.Immutable;
using System.Text;

using CSharpText;
using CSharpText.MemberSlicing;
using DotnetInspector.Libraries;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse;

/// <summary>
/// Settles source evidence for one exact Library target.
/// </summary>
public static partial class SourceHouse
{
    public static async ValueTask<SourceHouseOutcome> ExecuteAuthoredAsync(
        SourceHouseAuthoredRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationLease);

        ProvisionalOutcome provisional;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            provisional = await ExecuteCoreAsync(
                    request,
                    operationLease,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            operationLease.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        SourceHouseRequestEvidence evidence = Evidence(request);
        var settlement = new SourceHouseLibraryLeaseSettlement(
            SourceHouseLibraryLeaseConsumer.SourceHouse);
        return provisional.Complete(evidence, settlement);
    }

    private static async ValueTask<ProvisionalOutcome> ExecuteCoreAsync(
        SourceHouseAuthoredRequest request,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken)
    {
        SourceHouseWorkCharge emptyWork = EmptyWork();
        SourceHousePdbContribution noPdb = PdbUnavailable();
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
        if (DeadlineExpired(request.Plan))
        {
            return Incomplete(
                SourceHouseIncompleteBoundary.Deadline,
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
                new SourceHousePdbContribution(
                    SourceHousePdbContributionKind.Rejected,
                    content: null,
                    bytesObserved: 0,
                    sourceLinkMap: null,
                    observations: []),
                emptyWork);
        }

        LibraryContentReference? companion =
            companions.Length == 0 ? null : companions[0];
        DetachedInputs detached;
        try
        {
            detached = SnapshotInputs(
                request,
                operationLease,
                companion,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SourceHouseSnapshotException exception)
        {
            string detail = ExceptionDetail(exception.InnerException!);
            return Failed(
                exception.Stage,
                "ContentAccessFailed",
                new SourceHousePdbContribution(
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
                detail: detail);
        }

        SourceHouseWorkCharge snapshotWork = emptyWork with
        {
            AssemblyBytesObserved = detached.AssemblyLength,
            PortablePdbBytesObserved = detached.PortablePdbLength,
        };
        if (detached.IncompleteBoundary is { } snapshotBoundary)
        {
            return Incomplete(
                snapshotBoundary,
                new SourceHousePdbContribution(
                    SourceHousePdbContributionKind.Incomplete,
                    companion,
                    detached.PortablePdbLength,
                    sourceLinkMap: null,
                    observations: []),
                snapshotWork);
        }
        if (DeadlineExpired(request.Plan))
        {
            return Incomplete(
                SourceHouseIncompleteBoundary.Deadline,
                noPdb,
                snapshotWork);
        }

        cancellationToken.ThrowIfCancellationRequested();
        PreparedAuthoredSource prepared;
        try
        {
            prepared = PrepareAuthoredSource(
                request,
                detached.AssemblyBytes!,
                detached.PortablePdbBytes,
                companion,
                snapshotWork,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdbResourceLimitException exception)
        {
            string detail = ExceptionDetail(exception);
            SourceHouseWorkCharge limitedWork = snapshotWork with
            {
                PortablePdbBytesObserved = exception.ActualBytes,
            };
            return Incomplete(
                SourceHouseIncompleteBoundary.PortablePdbBytes,
                new SourceHousePdbContribution(
                    SourceHousePdbContributionKind.Incomplete,
                    companion,
                    exception.ActualBytes,
                    sourceLinkMap: null,
                    observations:
                    [
                        new(
                            SourceHouseNativeObservationStage.PortablePdb,
                            detail),
                    ]),
                limitedWork);
        }
        catch (SourceHouseDisposalException exception)
        {
            return Failed(
                SourceHouseFailureStage.ResourceDisposal,
                "SourceLinkDisposalFailed",
                exception.PdbContribution,
                exception.Work,
                exception.Mapping,
                detail: ExceptionDetail(exception.InnerException!));
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            string detail = ExceptionDetail(exception);
            return Failed(
                SourceHouseFailureStage.AssemblyInspection,
                "InspectionFailed",
                new SourceHousePdbContribution(
                    SourceHousePdbContributionKind.Failed,
                    companion,
                    detached.PortablePdbLength,
                    sourceLinkMap: null,
                    observations:
                    [
                        new(
                            SourceHouseNativeObservationStage.Assembly,
                            detail),
                    ]),
                snapshotWork,
                detail: detail);
        }

        if (prepared.Terminal is { } terminal)
            return terminal;

        return await SettleCandidatesAsync(
                request,
                prepared,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static DetachedInputs SnapshotInputs(
        SourceHouseAuthoredRequest request,
        LibraryOperationLease operationLease,
        LibraryContentReference? companion,
        CancellationToken cancellationToken)
    {
        SourceHouseLimits limits = request.Plan.Limits;
        DetachedContent assembly = SnapshotContent(
            operationLease,
            request.SelectedAssembly,
            limits.MaximumAssemblyBytes,
            SourceHouseFailureStage.AssemblySnapshot,
            SourceHouseNativeObservationStage.Assembly,
            cancellationToken);
        if (assembly.LimitExceeded)
        {
            return new DetachedInputs(
                AssemblyBytes: null,
                PortablePdbBytes: null,
                assembly.Length,
                PortablePdbLength: 0,
                SourceHouseIncompleteBoundary.AssemblyBytes);
        }
        if (companion is null)
        {
            return new DetachedInputs(
                assembly.Bytes,
                PortablePdbBytes: null,
                assembly.Length,
                PortablePdbLength: 0,
                IncompleteBoundary: null);
        }

        DetachedContent pdb = SnapshotContent(
            operationLease,
            companion,
            limits.MaximumPortablePdbBytes,
            SourceHouseFailureStage.PortablePdbSnapshot,
            SourceHouseNativeObservationStage.PortablePdb,
            cancellationToken);
        return new DetachedInputs(
            assembly.Bytes,
            pdb.LimitExceeded ? null : pdb.Bytes,
            assembly.Length,
            pdb.Length,
            pdb.LimitExceeded
                ? SourceHouseIncompleteBoundary.PortablePdbBytes
                : null);
    }

    private static DetachedContent SnapshotContent(
        LibraryOperationLease operationLease,
        LibraryContentReference content,
        int maximumBytes,
        SourceHouseFailureStage failureStage,
        SourceHouseNativeObservationStage observationStage,
        CancellationToken cancellationToken)
    {
        try
        {
            return operationLease.Snapshot(
                content,
                maximumBytes,
                static (view, limit, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    int length = view.Content.Length;
                    return length > limit
                        ? new DetachedContent(
                            Bytes: null,
                            length,
                            LimitExceeded: true)
                        : new DetachedContent(
                            view.Content.ToArray(),
                            length,
                            LimitExceeded: false);
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsSnapshotFailure(exception))
        {
            throw new SourceHouseSnapshotException(
                failureStage,
                observationStage,
                exception);
        }
    }

    private static PreparedAuthoredSource PrepareAuthoredSource(
        SourceHouseAuthoredRequest request,
        byte[] assemblyBytes,
        byte[]? portablePdbBytes,
        LibraryContentReference? companion,
        SourceHouseWorkCharge work,
        CancellationToken cancellationToken)
    {
        ResolvedAssemblyReference? descriptor =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                request.SelectedAssembly.Registration,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Designated(
                    "SourceHouse selected Library content"));
        if (descriptor is null)
        {
            return PreparedAuthoredSource.TerminalOutcome(
                Failed(
                    SourceHouseFailureStage.AssemblyInspection,
                    "NotManagedAssembly",
                    PdbUnavailable(),
                    work));
        }

        ManagedMetadataIdentity.Assembly? expectedIdentity =
            request.SelectedAssembly.AssemblyIdentity;
        if (expectedIdentity is null
            || !descriptor.Identity.IsEquivalentTo(
                expectedIdentity.Identity))
        {
            return PreparedAuthoredSource.TerminalOutcome(
                Rejected(
                    SourceHouseRejectionKind.AssemblyIdentityMismatch,
                    PdbUnavailable(),
                    work));
        }

        ApiSurface surface;
        using (AssemblyInspectionSession session =
               AssemblyInspectionSession.Open(descriptor))
        {
            ApiSurfaceExtractionResult extraction =
                session.BoundedApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll,
                    request.Plan.Limits.TargetBounds);
            if (extraction
                is ApiSurfaceExtractionResult.Exceeded)
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    Incomplete(
                        SourceHouseIncompleteBoundary.TargetSurface,
                        PdbUnavailable(),
                        work));
            }

            surface =
                ((ApiSurfaceExtractionResult.Extracted)extraction)
                    .Surface;

            if (!TargetExists(surface, request.Target)
                && RequiresCompilerGeneratedSurface(request.Target))
            {
                extraction =
                    session.BoundedApiSurface(
                        ApiSurfaceExtractionScope.IncludeAll,
                        request.Plan.Limits.TargetBounds,
                        includeCompilerGenerated: true);
                if (extraction
                    is ApiSurfaceExtractionResult.Exceeded)
                {
                    return PreparedAuthoredSource.TerminalOutcome(
                        Incomplete(
                            SourceHouseIncompleteBoundary.TargetSurface,
                            PdbUnavailable(),
                            work));
                }

                surface =
                    ((ApiSurfaceExtractionResult.Extracted)extraction)
                        .Surface;
            }
        }

        if (!TargetExists(surface, request.Target))
        {
            if (CreateTargetInspectionFailure(
                    surface,
                    request.Target)
                is { } targetFailure)
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    Failed(
                        targetFailure.Failure.Stage,
                        targetFailure.Failure.Code,
                        targetFailure.PdbContribution,
                        work,
                        detail: targetFailure.Failure.Detail));
            }

            return PreparedAuthoredSource.TerminalOutcome(
                Rejected(
                    SourceHouseRejectionKind.TargetMismatch,
                    PdbUnavailable(),
                    work));
        }

        var observations =
            new List<SourceHouseNativeObservation>();
        void Log(string detail) =>
            observations.Add(
                new(
                    SourceHouseNativeObservationStage.SourceLink,
                    detail));

        SourceLinkService? source = null;
        SourceHousePdbContributionKind contributionKind =
            SourceHousePdbContributionKind.Unavailable;
        SourceHouseFailureStage failureStage =
            SourceHouseFailureStage.PortablePdbInspection;
        SourceHouseNativeObservationStage observationStage =
            SourceHouseNativeObservationStage.PortablePdb;
        long pdbBytesObserved = portablePdbBytes?.Length ?? 0;
        SourceHouseWorkCharge observedWork = work;
        SourceHouseAuthoredMapping? observedMapping = null;
        try
        {
            if (portablePdbBytes is not null)
            {
                contributionKind =
                    SourceHousePdbContributionKind.SuppliedCompanion;
                source = SourceLinkService.OpenMetadataOnly(
                    descriptor,
                    Log,
                    cache: null,
                    request.Plan.Limits.EffectiveSourceLinkReadLimits);
                source.LoadPdbFromStream(
                    new MemoryStream(
                        portablePdbBytes,
                        writable: false),
                    pdbLocation: "Library companion",
                    throwOnReadFailure: true);
                if (!source.HasPdb)
                {
                    SourceHousePdbContribution rejectedPdb = Pdb(
                        SourceHousePdbContributionKind.Rejected,
                        companion,
                        portablePdbBytes.Length,
                        source,
                        observations);
                    return PreparedAuthoredSource.TerminalOutcome(
                        Rejected(
                            SourceHouseRejectionKind
                                .PortablePdbCorrespondenceMismatch,
                            rejectedPdb,
                            work));
                }
            }
            else
            {
                contributionKind =
                    SourceHousePdbContributionKind.Embedded;
                source = SourceLinkService.OpenEmbeddedPdbOnly(
                    descriptor,
                    request.Plan.Limits.EffectiveSourceLinkReadLimits,
                    Log);
                pdbBytesObserved = source.Context.EmbeddedPdbSize;
                observedWork = work with
                {
                    PortablePdbBytesObserved = pdbBytesObserved,
                };
                if (!source.HasPdb)
                {
                    SourceHousePdbContribution unavailablePdb = Pdb(
                        SourceHousePdbContributionKind.Unavailable,
                        content: null,
                        pdbBytesObserved,
                        source,
                        observations);
                    return PreparedAuthoredSource.TerminalOutcome(
                        SettleUnavailable(
                            request.Plan,
                            unavailablePdb,
                            observedWork));
                }
            }

            failureStage = SourceHouseFailureStage.SourceLinkInspection;
            observationStage =
                SourceHouseNativeObservationStage.SourceLink;
            cancellationToken.ThrowIfCancellationRequested();
            if (DeadlineExpired(request.Plan))
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    Incomplete(
                        SourceHouseIncompleteBoundary.Deadline,
                        Pdb(
                            SourceHousePdbContributionKind.Incomplete,
                            companion,
                            pdbBytesObserved,
                            source,
                            observations),
                        observedWork));
            }

            SourceLinkMapAudit map = source.InspectSourceLinkMap();
            if (map.LimitKind
                == SourceLinkMapLimitKind.EncodedBytes)
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    Incomplete(
                        SourceHouseIncompleteBoundary.SourceLinkMapBytes,
                        Pdb(
                            SourceHousePdbContributionKind.Incomplete,
                            companion,
                            pdbBytesObserved,
                            source,
                            observations),
                        observedWork));
            }
            if (map.LimitKind
                == SourceLinkMapLimitKind.Mappings)
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    Incomplete(
                        SourceHouseIncompleteBoundary.SourceLinkMappings,
                        Pdb(
                            SourceHousePdbContributionKind.Incomplete,
                            companion,
                            pdbBytesObserved,
                            source,
                            observations),
                        observedWork));
            }
            if (map.Map.Status == SourceLinkMapStatus.Unusable)
            {
                observations.Add(
                    new(
                        SourceHouseNativeObservationStage.SourceLink,
                        map.Map.Error
                        ?? "The SourceLink map is unusable."));
            }

            failureStage = SourceHouseFailureStage.TargetMapping;
            observationStage =
                SourceHouseNativeObservationStage.Mapping;
            MappingPreparation mapping = PrepareMapping(
                request.Target,
                source,
                request.Plan.Limits,
                cancellationToken);
            observedWork = observedWork with
            {
                DocumentsObserved = mapping.DocumentsObserved,
                TargetMappingsObserved = mapping.TargetMappingsObserved,
            };
            SourceHouseWorkCharge mappingWork = observedWork;
            observedMapping = mapping.Mapping;
            if (mapping.Failure?.Detail is { } mappingDetail)
            {
                observations.Add(
                    new(
                        SourceHouseNativeObservationStage.Mapping,
                        mappingDetail));
            }
            SourceHousePdbContribution pdb = Pdb(
                contributionKind,
                companion,
                pdbBytesObserved,
                source,
                observations);
            if (mapping.IncompleteBoundary is { } boundary)
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    Incomplete(
                        boundary,
                        pdb with
                        {
                            Kind =
                                SourceHousePdbContributionKind
                                    .Incomplete,
                        },
                        mappingWork));
            }
            if (mapping.Failure is { } failure)
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    Failed(
                        failure.Stage,
                        failure.Code,
                        pdb with
                        {
                            Kind =
                                SourceHousePdbContributionKind.Failed,
                        },
                        mappingWork,
                        detail: failure.Detail));
            }
            if (mapping.Mapping is null
                || mapping.Document is null)
            {
                return PreparedAuthoredSource.TerminalOutcome(
                    SettleUnavailable(request.Plan, pdb, mappingWork));
            }

            var candidate = new SourceHouseSourceCandidate(
                request.Target,
                mapping.Document,
                mapping.Mapping,
                source.RepositoryUrl,
                source.CommitHash);
            return new PreparedAuthoredSource(
                pdb,
                mapping.Mapping,
                candidate,
                mappingWork,
                Terminal: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdbResourceLimitException)
        {
            throw;
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            string detail = ExceptionDetail(exception);
            observations.Add(new(observationStage, detail));
            return PreparedAuthoredSource.TerminalOutcome(
                Failed(
                    failureStage,
                    "InspectionFailed",
                    new SourceHousePdbContribution(
                        SourceHousePdbContributionKind.Failed,
                        companion,
                        pdbBytesObserved,
                        sourceLinkMap: null,
                        observations),
                    observedWork,
                    detail: detail));
        }
        finally
        {
            if (source is not null
                && source.DisposeWithFailure() is { } failure)
            {
                observations.Add(
                    new(
                        SourceHouseNativeObservationStage.Disposal,
                        ExceptionDetail(failure)));
                if (!cancellationToken.IsCancellationRequested)
                {
                    throw new SourceHouseDisposalException(
                        new SourceHousePdbContribution(
                            SourceHousePdbContributionKind.Failed,
                            companion,
                            pdbBytesObserved,
                            sourceLinkMap: null,
                            observations),
                        observedWork,
                        observedMapping,
                        failure);
                }
            }
        }
    }

    private static MappingPreparation PrepareMapping(
        SourceHouseTarget target,
        SourceLinkService source,
        SourceHouseLimits limits,
        CancellationToken cancellationToken)
    {
        var findingSubject = new FindingSubject(
            target.Kind == SourceHouseTargetKind.Member
                ? "member"
                : "type",
            target.Type.ToMetadataFullName());
        FindingInspection<SourceDocumentObservation> documentInspection =
            SourceLinkFindings.InspectSourceDocuments(
                source,
                findingSubject);
        if (documentInspection.Value
            is FindingInspection<SourceDocumentObservation>.Failed
                documentFailure)
        {
            return MappingPreparation.Failed(
                SourceHouseFailureStage.SourceLinkInspection,
                "DocumentInspectionFailed",
                documentFailure.Error.Reason);
        }
        if (documentInspection.Value
            is FindingInspection<SourceDocumentObservation>.Absent)
        {
            return MappingPreparation.Unavailable();
        }

        SourceDocumentObservation[] documents =
        [
            .. ((FindingInspection<
                    SourceDocumentObservation>.Complete)
                documentInspection.Value)
                .Findings
                .Select(static finding => finding.Payload),
        ];
        int documentsObserved = documents.Length;
        if (documents.Length > limits.MaximumDocuments)
        {
            return MappingPreparation.Incomplete(
                SourceHouseIncompleteBoundary.Documents,
                documentsObserved,
                TargetMappingsObserved: 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (target is SourceHouseTarget.MemberTarget memberTarget)
        {
            FindingInspection<MemberSourceObservation> memberInspection =
                SourceLinkFindings.InspectMemberSources(
                    source,
                    findingSubject,
                    new MemberSourceQuery(
                        new HashSet<int>
                        {
                            memberTarget.MetadataToken,
                        }));
            if (memberInspection.Value
                is FindingInspection<MemberSourceObservation>.Failed
                    memberFailure)
            {
                return MappingPreparation.Failed(
                    SourceHouseFailureStage.TargetMapping,
                    "MemberMappingInspectionFailed",
                    memberFailure.Error.Reason,
                    documentsObserved);
            }
            if (memberInspection.Value
                is FindingInspection<MemberSourceObservation>.Absent)
            {
                return MappingPreparation.Unavailable(
                    documentsObserved);
            }

            MemberSourceObservation[] mappings =
            [
                .. ((FindingInspection<
                        MemberSourceObservation>.Complete)
                    memberInspection.Value)
                    .Findings
                    .Select(static finding => finding.Payload),
            ];
            if (mappings.Length > limits.MaximumTargetMappings)
            {
                return MappingPreparation.Incomplete(
                    SourceHouseIncompleteBoundary.TargetMappings,
                    documentsObserved,
                    mappings.Length);
            }

            MemberSourceObservation[] exact =
            [
                .. mappings.Where(
                    mapping =>
                        mapping.MetadataToken
                            == memberTarget.MetadataToken),
            ];
            if (exact.Length == 0)
            {
                return MappingPreparation.Unavailable(
                    documentsObserved,
                    mappings.Length);
            }
            MemberSourceObservation mapping =
                exact
                    .OrderByDescending(
                        static value => value.IsPrimaryDocument)
                    .ThenBy(
                        static value => value.DocumentRowId)
                    .First();
            SourceDocumentObservation? document =
                SelectDocument(
                    documents,
                    mapping.DocumentRowId,
                    mapping.OriginalPath);
            if (document is null)
            {
                return MappingPreparation.Unavailable(
                    documentsObserved,
                    mappings.Length);
            }

            return MappingPreparation.Available(
                new SourceHouseAuthoredMapping.Member(
                    mapping,
                    document),
                document,
                documentsObserved,
                mappings.Length);
        }

        SourceLinkResolver.TypeSourceInfo? typeMapping =
            source.ResolveTypeSource(target.Type);
        if (typeMapping is null
            || TypeSourceDocumentSelection.SelectDefault(typeMapping) is not { } primary)
        {
            return MappingPreparation.Unavailable(
                documentsObserved);
        }

        int typeMappingsObserved = typeMapping.Documents.Length;
        if (typeMappingsObserved > limits.MaximumTargetMappings)
        {
            return MappingPreparation.Incomplete(
                SourceHouseIncompleteBoundary.TargetMappings,
                documentsObserved,
                typeMappingsObserved);
        }
        SourceLinkResolver.TypeSourceDocument selected = primary;
        if (target is SourceHouseTarget.TypeTarget
            { OriginalDocumentPath: { } selectedPath })
        {
            var matching = typeMapping.Documents.FirstOrDefault(
                document => string.Equals(
                    document.FilePath, selectedPath, StringComparison.Ordinal));
            if (matching is null)
            {
                return MappingPreparation.Unavailable(
                    documentsObserved,
                    typeMappingsObserved);
            }
            selected = matching;
        }
        SourceDocumentObservation? typeDocument =
            SelectDocument(documents, documentRowId: null, selected.FilePath);
        if (typeDocument is null)
        {
            return MappingPreparation.Unavailable(
                documentsObserved,
                typeMappingsObserved);
        }

        SourceHouseMappingStrength strength =
            selected.ResolutionMethod
                == SourceLinkResolver.SourceResolutionMethod.Inferred
                ? SourceHouseMappingStrength.InferredTypeDocument
                : SourceHouseMappingStrength.CorrelatedTypeDocument;
        SourceLinkResolver.TypeSourceInfo detachedMapping =
            typeMapping with
            {
                Documents =
                [
                    .. typeMapping.Documents.Select(
                        static document => document with
                        {
                            Checksum = document.Checksum?.ToArray(),
                        }),
                ],
            };
        var typeEvidence = new SourceHouseAuthoredMapping.Type(
            detachedMapping,
            typeDocument,
            strength,
            typeMapping.Documents.Length > 1,
            typeMapping.Documents
                .Where(document => document.FilePath != primary.FilePath)
                .Select(
                    static additional =>
                        new SourceHouseAdditionalTypeDocument(
                            additional.FilePath,
                            additional.SourceUrl))
                .ToArray());
        return MappingPreparation.Available(
            typeEvidence,
            typeDocument,
            documentsObserved,
            typeMappingsObserved);
    }

    private static async ValueTask<ProvisionalOutcome>
        SettleCandidatesAsync(
            SourceHouseAuthoredRequest request,
            PreparedAuthoredSource prepared,
            CancellationToken cancellationToken)
    {
        var attempts = new List<SourceHouseSourceAttempt>();
        long sourceBytes = 0;
        long sourceCharacters = 0;
        SourceHouseFailure? observedFailure = null;
        IReadOnlyList<ISourceHouseSourceCapability> capabilities =
            request.Plan.Capabilities;

        await using DeadlineCancellation operationDeadline =
            DeadlineCancellation.Start(
                request.Plan.Deadline,
                cancellationToken);
        CancellationToken operationCancellation =
            operationDeadline.Token;

        foreach (ISourceHouseSourceCapability capability
            in capabilities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeadlineExpired(request.Plan))
            {
                return Incomplete(
                    SourceHouseIncompleteBoundary.Deadline,
                    prepared.PdbContribution,
                    Charge(),
                    prepared.Mapping,
                    attempts);
            }
            if (attempts.Count
                >= request.Plan.Limits.MaximumCandidateAttempts)
            {
                return Incomplete(
                    SourceHouseIncompleteBoundary.CandidateAttempts,
                    prepared.PdbContribution,
                    Charge(),
                    prepared.Mapping,
                    attempts);
            }

            int remainingBytes =
                request.Plan.Limits.MaximumSourceBytes
                - checked((int)sourceBytes);
            if (remainingBytes <= 0)
            {
                return Incomplete(
                    SourceHouseIncompleteBoundary.SourceBytes,
                    prepared.PdbContribution,
                    Charge(),
                    prepared.Mapping,
                    attempts);
            }

            SourceHouseCapabilityOutcome capabilityOutcome;
            try
            {
                capabilityOutcome =
                    await capability.ReadAsync(
                            prepared.Candidate,
                            remainingBytes,
                            operationCancellation)
                        .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "A source capability returned no outcome.");
            }
            catch (OperationCanceledException exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operationDeadline.IsDeadlineCancellationRequested)
                {
                    attempts.Add(
                        Attempt(
                            capability,
                            SourceHouseSourceAttemptKind.Incomplete,
                            BytesObserved: 0,
                            ChecksumVerification: null,
                            new(
                                "DeadlineExpiredDuringCapability",
                                ExceptionDetail(exception))));
                    return Incomplete(
                        SourceHouseIncompleteBoundary.Deadline,
                        prepared.PdbContribution,
                        Charge(),
                        prepared.Mapping,
                        attempts);
                }

                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException
                    or ArgumentException
                    or NotSupportedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string detail = ExceptionDetail(exception);
                if (operationDeadline.IsDeadlineCancellationRequested)
                {
                    attempts.Add(
                        Attempt(
                            capability,
                            SourceHouseSourceAttemptKind.Incomplete,
                            BytesObserved: 0,
                            ChecksumVerification: null,
                            new(
                                "DeadlineExpiredDuringCapability",
                                detail)));
                    return Incomplete(
                        SourceHouseIncompleteBoundary.Deadline,
                        prepared.PdbContribution,
                        Charge(),
                        prepared.Mapping,
                        attempts);
                }

                attempts.Add(
                    Attempt(
                        capability,
                        SourceHouseSourceAttemptKind.Failed,
                        BytesObserved: 0,
                        ChecksumVerification: null,
                        new(
                            "CapabilityThrew",
                            detail)));
                observedFailure ??= new(
                    SourceHouseFailureStage.SourceCapability,
                    "CapabilityThrew",
                    detail);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (DeadlineExpired(request.Plan))
            {
                RecordDeadlineAttempt(
                    capability,
                    capabilityOutcome);
                return Incomplete(
                    SourceHouseIncompleteBoundary.Deadline,
                    prepared.PdbContribution,
                    Charge(),
                    prepared.Mapping,
                    attempts);
            }

            switch (capabilityOutcome)
            {
                case SourceHouseCapabilityOutcome.Unavailable unavailable:
                    attempts.Add(
                        Attempt(
                            capability,
                            SourceHouseSourceAttemptKind.Unavailable,
                            BytesObserved: 0,
                            ChecksumVerification: null,
                            unavailable.Observation));
                    continue;
                case SourceHouseCapabilityOutcome.Rejected rejected:
                    attempts.Add(
                        Attempt(
                            capability,
                            SourceHouseSourceAttemptKind.Rejected,
                            BytesObserved: 0,
                            ChecksumVerification: null,
                            rejected.Observation));
                    continue;
                case SourceHouseCapabilityOutcome.Failed failed:
                    SourceHouseCapabilityObservation failureObservation =
                        failed.Observation!;
                    attempts.Add(
                        Attempt(
                            capability,
                            SourceHouseSourceAttemptKind.Failed,
                            BytesObserved: 0,
                            ChecksumVerification: null,
                            failureObservation));
                    observedFailure ??= new(
                        SourceHouseFailureStage.SourceCapability,
                        failureObservation.Code,
                        failureObservation.Detail);
                    continue;
                case SourceHouseCapabilityOutcome.Incomplete incomplete:
                    attempts.Add(
                        Attempt(
                            capability,
                            SourceHouseSourceAttemptKind.Incomplete,
                            BytesObserved: 0,
                            ChecksumVerification: null,
                            incomplete.Observation));
                    return Incomplete(
                        SourceHouseIncompleteBoundary.SourceBytes,
                        prepared.PdbContribution,
                        Charge(),
                        prepared.Mapping,
                        attempts);
                case SourceHouseCapabilityOutcome.Available available:
                    int candidateBytes = available.Bytes.Length;
                    sourceBytes = checked(sourceBytes + candidateBytes);
                    if (candidateBytes > remainingBytes)
                    {
                        attempts.Add(
                            Attempt(
                                capability,
                                SourceHouseSourceAttemptKind.Incomplete,
                                candidateBytes,
                                ChecksumVerification: null,
                                new(
                                    "SourceByteLimitExceeded",
                                    available.Observation?.Detail)));
                        return Incomplete(
                            SourceHouseIncompleteBoundary.SourceBytes,
                            prepared.PdbContribution,
                            Charge(),
                            prepared.Mapping,
                            attempts);
                    }

                    byte[] bytes = available.Bytes.ToArray();
                    SourceChecksumVerification verification =
                        SourceLinkService.VerifyChecksum(
                            prepared.Candidate.Document,
                            bytes);
                    if (verification is not (
                            SourceChecksumVerification.Exact
                            or SourceChecksumVerification
                                .LineEndingNormalized))
                    {
                        attempts.Add(
                            Attempt(
                                capability,
                                SourceHouseSourceAttemptKind.Rejected,
                                bytes.Length,
                                verification,
                                available.Observation
                                ?? new(
                                    verification.ToString())));
                        continue;
                    }

                    string text;
                    try
                    {
                        text = SourceLinkService.DecodeSourceText(bytes);
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException
                            or DecoderFallbackException)
                    {
                        string detail = ExceptionDetail(exception);
                        attempts.Add(
                            Attempt(
                                capability,
                                SourceHouseSourceAttemptKind.Failed,
                                bytes.Length,
                                verification,
                                new(
                                    "SourceDecodeFailed",
                                    detail)));
                        observedFailure ??= new(
                            SourceHouseFailureStage.SourceVerification,
                            "SourceDecodeFailed",
                            detail);
                        continue;
                    }

                    sourceCharacters =
                        checked(sourceCharacters + text.Length);
                    if (sourceCharacters
                        > request.Plan.Limits
                            .MaximumSourceTextCharacters)
                    {
                        attempts.Add(
                            Attempt(
                                capability,
                                SourceHouseSourceAttemptKind.Incomplete,
                                bytes.Length,
                                verification,
                                new("SourceTextLimitExceeded")));
                        return Incomplete(
                            SourceHouseIncompleteBoundary
                                .SourceTextCharacters,
                            prepared.PdbContribution,
                            Charge(),
                            prepared.Mapping,
                            attempts);
                    }

                    SourceHouseAuthoredMemberDocument? memberDocument = null;
                    if (prepared.Mapping
                        is SourceHouseAuthoredMapping.Member member)
                    {
                        try
                        {
                            if (request.Target is SourceHouseTarget.MemberTarget
                                { SourceForm: SourceHouseMemberSourceForm.DocumentParts })
                            {
                                MemberTextParts? parts = MemberTextSlicer.GetMemberTextParts(
                                    text,
                                    member.Observation.StartLine,
                                    member.Observation.EndLine,
                                    member.Observation.Anchor.MemberName,
                                    member.Observation.SequencePointStartLines);
                                if (parts is not null)
                                {
                                    memberDocument = new(text, parts);
                                    text = text.Substring(parts.Member.Start, parts.Member.Length);
                                }
                                else
                                {
                                    text = "";
                                }
                            }
                            else
                            {
                                text = MemberTextSlicer.ExtractMemberText(
                                    text,
                                    member.Observation.StartLine,
                                    member.Observation.EndLine,
                                    member.Observation.Anchor.MemberName,
                                    member.Observation.SequencePointStartLines) ?? "";
                            }
                        }
                        catch (Exception exception) when (
                            exception
                                is CSharpTextComplexityException
                                or InvalidMemberTextCoordinatesException
                                or ArgumentException
                                or IndexOutOfRangeException
                                or InvalidOperationException)
                        {
                            string detail = ExceptionDetail(exception);
                            string code = exception switch
                            {
                                CSharpTextComplexityException => "SourceTooComplex",
                                InvalidMemberTextCoordinatesException => "InvalidSequencePointCoordinates",
                                _ => "SourceExtractionFailed",
                            };
                            attempts.Add(
                                Attempt(
                                    capability,
                                    SourceHouseSourceAttemptKind.Failed,
                                    bytes.Length,
                                    verification,
                                    new(
                                        code,
                                        detail)));
                            observedFailure ??= new(
                                SourceHouseFailureStage.SourceSlicing,
                                code,
                                detail);
                            continue;
                        }
                        if (text.Length == 0)
                        {
                            attempts.Add(
                                Attempt(
                                    capability,
                                    SourceHouseSourceAttemptKind.Rejected,
                                    bytes.Length,
                                    verification,
                                    new("NoVouchedMemberDeclaration")));
                            continue;
                        }
                    }

                    SourceHouseSourceAttempt selected =
                        Attempt(
                            capability,
                            SourceHouseSourceAttemptKind.Available,
                            bytes.Length,
                            verification,
                            available.Observation);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DeadlineExpired(request.Plan))
                    {
                        attempts.Add(selected);
                        return Incomplete(
                            SourceHouseIncompleteBoundary.Deadline,
                            prepared.PdbContribution,
                            Charge(),
                            prepared.Mapping,
                            attempts);
                    }
                    attempts.Add(selected);
                    var authored =
                        new SourceHouseAuthoredAttempt.Available(
                            text,
                            prepared.Mapping,
                            selected,
                            attempts,
                            memberDocument);
                    return new AvailableOutcome(
                        prepared.PdbContribution,
                        authored,
                        Charge());
                default:
                    throw new InvalidOperationException(
                        "Unknown source capability outcome.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (DeadlineExpired(request.Plan))
        {
            return Incomplete(
                SourceHouseIncompleteBoundary.Deadline,
                prepared.PdbContribution,
                Charge(),
                prepared.Mapping,
                attempts);
        }

        if (observedFailure is not null)
        {
            return Failed(
                observedFailure.Stage,
                observedFailure.Code,
                prepared.PdbContribution,
                Charge(),
                prepared.Mapping,
                attempts,
                observedFailure.Detail);
        }

        SourceLinkMapInspection? map =
            prepared.PdbContribution.SourceLinkMap?.Map;
        bool mapUnusable = map?.Status == SourceLinkMapStatus.Unusable;
        bool documentRejected =
            prepared.Candidate.Document.ResolutionStatus
                == SourceDocumentResolutionStatus.Rejected;
        if ((mapUnusable || documentRejected)
            && prepared.Candidate.Document.ResolvedUrl is null
            && capabilities.Any(
                static capability =>
                    capability.Category
                        == SourceHouseCapabilityCategory.Remote))
        {
            return Failed(
                SourceHouseFailureStage.SourceLinkInspection,
                mapUnusable
                    ? "SourceLinkMapUnusable"
                    : "SourceLinkDocumentMappingRejected",
                prepared.PdbContribution,
                Charge(),
                prepared.Mapping,
                attempts,
                map?.Error);
        }

        return SettleUnavailable(
            request.Plan,
            prepared.PdbContribution,
            Charge(),
            prepared.Mapping,
            attempts);

        SourceHouseWorkCharge Charge() =>
            prepared.Work with
            {
                CandidateAttempts = attempts.Count,
                SourceBytesObserved = sourceBytes,
                SourceTextCharactersObserved = sourceCharacters,
            };

        void RecordDeadlineAttempt(
            ISourceHouseSourceCapability capability,
            SourceHouseCapabilityOutcome outcome)
        {
            if (outcome is SourceHouseCapabilityOutcome.Available available)
            {
                int bytesObserved = available.Bytes.Length;
                sourceBytes = checked(sourceBytes + bytesObserved);
                attempts.Add(
                    Attempt(
                        capability,
                        SourceHouseSourceAttemptKind.Incomplete,
                        bytesObserved,
                        ChecksumVerification: null,
                        available.Observation
                        ?? new("DeadlineExpiredAfterCapability")));
                return;
            }

            attempts.Add(
                Attempt(
                    capability,
                    outcome switch
                    {
                        SourceHouseCapabilityOutcome.Unavailable =>
                            SourceHouseSourceAttemptKind.Unavailable,
                        SourceHouseCapabilityOutcome.Rejected =>
                            SourceHouseSourceAttemptKind.Rejected,
                        SourceHouseCapabilityOutcome.Failed =>
                            SourceHouseSourceAttemptKind.Failed,
                        SourceHouseCapabilityOutcome.Incomplete =>
                            SourceHouseSourceAttemptKind.Incomplete,
                        _ => throw new InvalidOperationException(
                            "Unknown source capability outcome."),
                    },
                    BytesObserved: 0,
                    ChecksumVerification: null,
                    outcome.Observation));
        }
    }

    private static bool TargetExists(
        ApiSurface surface,
        SourceHouseTarget target)
    {
        ApiType[] types =
        [
            .. surface.Types.Where(
                candidate =>
                    candidate.DefinitionName == target.Type),
        ];
        if (types.Length != 1)
            return false;
        if (target is not SourceHouseTarget.MemberTarget memberTarget)
            return true;

        return ResolveMemberTarget(surface, memberTarget) is not null;
    }

    private static (
        ApiType Type,
        ApiMember Member,
        bool RequiresAccessorProjection)?
        ResolveMemberTarget(
            ApiSurface surface,
            SourceHouseTarget.MemberTarget target)
    {
        ApiType[] types =
        [
            .. surface.Types.Where(
                candidate =>
                    candidate.DefinitionName == target.Type),
        ];
        if (types.Length != 1)
            return null;

        ApiType type = types[0];
        // An explicit accessor can appear as both a physical method and an
        // accessor projection; the same token and anchor still name one target.
        ApiMember[] direct =
        [
            .. type.Members.Where(
                candidate =>
                    candidate.MetadataToken
                        == target.MetadataToken
                    && ApiMemberIdentity.GetMemberAnchor(
                        type,
                        candidate)
                        == target.Member),
        ];
        if (direct.Length == 1)
            return (type, direct[0], false);
        if (direct.Length > 1)
            return null;

        ApiMember[] accessors =
        [
            .. type.Members
                .SelectMany(
                    owner => ApiMemberAccessors.Create(owner, type))
                .Where(
                    candidate =>
                        candidate.MetadataToken
                            == target.MetadataToken
                        && ApiMemberIdentity.GetMemberAnchor(
                            type,
                            candidate)
                            == target.Member),
        ];
        if (accessors.Length != 1)
            return null;

        return (type, accessors[0], true);
    }

    private static bool RequiresCompilerGeneratedSurface(
        SourceHouseTarget target) =>
        TypeFilters.IsCompilerGeneratedNested(
            target.Type.ToNestedMetadataName())
        || target is SourceHouseTarget.MemberTarget member
            && MemberFilters.IsCompilerGenerated(
                member.Member.MemberName);

    private static ApiSurfaceInspectionFailure?
        FindPotentialTargetInspectionFailure(
            ApiSurface surface,
            SourceHouseTarget target) =>
        surface.InspectionFailures.FirstOrDefault(
            failure =>
                failure.Operation
                    != ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation
                && failure.Operation
                    != ApiSurfaceInspectionFailure
                        .EnumAttributeTypeIndexOperation
                && ((target.Kind == SourceHouseTargetKind.Member
                        && failure.OwningTypeDefinition is not null)
                    || MayAffectTargetType(failure, target.Type)));

    private static (
        SourceHouseFailure Failure,
        SourceHousePdbContribution PdbContribution)?
        CreateTargetInspectionFailure(
            ApiSurface surface,
            SourceHouseTarget target)
    {
        if (FindPotentialTargetInspectionFailure(surface, target)
            is not { } targetFailure)
        {
            return null;
        }

        string detail =
            "The API surface could not establish exact target "
            + $"absence because '{targetFailure.Operation}' failed "
            + $"for metadata subject 0x{targetFailure.SubjectToken:X8} "
            + $"({targetFailure.Kind}): {targetFailure.Detail}";
        return (
            new(
                SourceHouseFailureStage.AssemblyInspection,
                "TargetInspectionFailed",
                detail),
            new(
                SourceHousePdbContributionKind.Failed,
                content: null,
                bytesObserved: 0,
                sourceLinkMap: null,
                observations:
                [
                    new(
                        SourceHouseNativeObservationStage.Assembly,
                        detail),
                ]));
    }

    private static bool MayAffectTargetType(
        ApiSurfaceInspectionFailure failure,
        MetadataTypeDefinitionName targetType)
    {
        if (failure.OwningTypeDefinition is { } owner)
            return owner == targetType;
        if (!failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
        {
            return failure.AffectedTypeDefinitions.Contains(
                targetType);
        }

        return true;
    }

    private static SourceDocumentObservation? SelectDocument(
        IEnumerable<SourceDocumentObservation> documents,
        int? documentRowId,
        string originalPath)
    {
        SourceDocumentObservation? match = null;
        foreach (SourceDocumentObservation candidate in documents)
        {
            if ((documentRowId is { } row
                    && candidate.DocumentRowId != row)
                || !string.Equals(
                    candidate.OriginalPath,
                    originalPath,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (match is not null)
                return null;
            match = candidate;
        }

        return match;
    }

    private static SourceHousePdbContribution Pdb(
        SourceHousePdbContributionKind kind,
        LibraryContentReference? content,
        long bytesObserved,
        SourceLinkService source,
        IReadOnlyList<SourceHouseNativeObservation> observations) =>
        new(
            kind,
            content,
            bytesObserved,
            source.InspectSourceLinkMap(),
            observations);

    private static SourceHouseSourceAttempt Attempt(
        ISourceHouseSourceCapability capability,
        SourceHouseSourceAttemptKind kind,
        long BytesObserved,
        SourceChecksumVerification? ChecksumVerification,
        SourceHouseCapabilityObservation? Observation) =>
        new(
            capability.Identity,
            capability.Category,
            kind,
            BytesObserved,
            ChecksumVerification,
            Observation);

    private static SourceHouseRequestEvidence Evidence(
        SourceHouseAuthoredRequest request) =>
        new(
            request.Identity,
            request.Library,
            request.SelectedAssembly,
            request.Target,
            request.Plan.Identity,
            request.Plan.PolicyGeneration);

    private static bool DeadlineExpired(
        SourceHouseOperationPlan plan) =>
        DateTimeOffset.UtcNow >= plan.Deadline;

    private static bool IsSnapshotFailure(Exception exception) =>
        exception is ObjectDisposedException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException;

    private static bool IsInspectionFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException;

    private static string ExceptionDetail(Exception exception) =>
        exception.ToString();

    private static SourceHouseWorkCharge EmptyWork() =>
        new(
            AssemblyBytesObserved: 0,
            PortablePdbBytesObserved: 0,
            DocumentsObserved: 0,
            TargetMappingsObserved: 0,
            CandidateAttempts: 0,
            SourceBytesObserved: 0,
            SourceTextCharactersObserved: 0);

    private static SourceHousePdbContribution PdbUnavailable() =>
        new(
            SourceHousePdbContributionKind.Unavailable,
            content: null,
            bytesObserved: 0,
            sourceLinkMap: null,
            observations: []);

    private static ProvisionalOutcome Rejected(
        SourceHouseRejectionKind kind,
        SourceHousePdbContribution pdb,
        SourceHouseWorkCharge work) =>
        new RejectedOutcome(
            pdb,
            new SourceHouseAuthoredAttempt.Rejected(
                mapping: null,
                sourceAttempts: []),
            new(kind),
            work);

    private static ProvisionalOutcome Failed(
        SourceHouseFailureStage stage,
        string code,
        SourceHousePdbContribution pdb,
        SourceHouseWorkCharge work,
        SourceHouseAuthoredMapping? mapping = null,
        IReadOnlyList<SourceHouseSourceAttempt>? attempts = null,
        string? detail = null) =>
        new FailedOutcome(
            pdb,
            new SourceHouseAuthoredAttempt.Failed(
                mapping,
                attempts ?? []),
            new(stage, code, detail),
            work);

    private static ProvisionalOutcome Incomplete(
        SourceHouseIncompleteBoundary boundary,
        SourceHousePdbContribution pdb,
        SourceHouseWorkCharge work,
        SourceHouseAuthoredMapping? mapping = null,
        IReadOnlyList<SourceHouseSourceAttempt>? attempts = null) =>
        new IncompleteOutcome(
            pdb,
            new SourceHouseAuthoredAttempt.Incomplete(
                mapping,
                attempts ?? []),
            boundary,
            work);

    private static ProvisionalOutcome SettleUnavailable(
        SourceHouseOperationPlan plan,
        SourceHousePdbContribution pdb,
        SourceHouseWorkCharge work,
        SourceHouseAuthoredMapping? mapping = null,
        IReadOnlyList<SourceHouseSourceAttempt>? attempts = null)
    {
        if (DeadlineExpired(plan))
        {
            return Incomplete(
                SourceHouseIncompleteBoundary.Deadline,
                pdb,
                work,
                mapping,
                attempts);
        }

        return new UnavailableOutcome(
            pdb,
            new SourceHouseAuthoredAttempt.Unavailable(
                mapping,
                attempts ?? []),
            work);
    }

    private sealed record DetachedInputs(
        byte[]? AssemblyBytes,
        byte[]? PortablePdbBytes,
        int AssemblyLength,
        int PortablePdbLength,
        SourceHouseIncompleteBoundary? IncompleteBoundary);

    private sealed record DetachedContent(
        byte[]? Bytes,
        int Length,
        bool LimitExceeded);

    private sealed record PreparedAuthoredSource(
        SourceHousePdbContribution PdbContribution,
        SourceHouseAuthoredMapping Mapping,
        SourceHouseSourceCandidate Candidate,
        SourceHouseWorkCharge Work,
        ProvisionalOutcome? Terminal)
    {
        internal static PreparedAuthoredSource TerminalOutcome(
            ProvisionalOutcome terminal) =>
            new(
                PdbUnavailable(),
                Mapping: null!,
                Candidate: null!,
                EmptyWork(),
                terminal);
    }

    private sealed class SourceHouseDisposalException : Exception
    {
        internal SourceHouseDisposalException(
            SourceHousePdbContribution pdbContribution,
            SourceHouseWorkCharge work,
            SourceHouseAuthoredMapping? mapping,
            Exception innerException)
            : base(
                "SourceLink resource disposal failed.",
                innerException)
        {
            PdbContribution = pdbContribution;
            Work = work;
            Mapping = mapping;
        }

        internal SourceHousePdbContribution PdbContribution { get; }
        internal SourceHouseWorkCharge Work { get; }
        internal SourceHouseAuthoredMapping? Mapping { get; }
    }

    private sealed class SourceHouseSnapshotException : Exception
    {
        internal SourceHouseSnapshotException(
            SourceHouseFailureStage stage,
            SourceHouseNativeObservationStage observationStage,
            Exception innerException)
            : base("Library content snapshot failed.", innerException)
        {
            Stage = stage;
            ObservationStage = observationStage;
        }

        internal SourceHouseFailureStage Stage { get; }
        internal SourceHouseNativeObservationStage ObservationStage { get; }
    }

    private sealed class DeadlineCancellation : IAsyncDisposable
    {
        private static readonly TimeSpan s_maximumTimerDelay =
            TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

        private readonly DateTimeOffset _deadline;
        private readonly CancellationTokenSource _deadlineCancellation =
            new();
        private readonly CancellationTokenSource _schedulerStop =
            new();
        private readonly CancellationTokenSource _operationCancellation;
        private readonly Task _scheduler;

        private DeadlineCancellation(
            DateTimeOffset deadline,
            CancellationToken callerCancellation)
        {
            _deadline = deadline;
            _operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    callerCancellation,
                    _deadlineCancellation.Token);
            _scheduler = ScheduleAsync(
                deadline,
                _deadlineCancellation,
                _schedulerStop.Token);
        }

        internal CancellationToken Token =>
            _operationCancellation.Token;

        internal bool IsDeadlineCancellationRequested =>
            _deadlineCancellation.IsCancellationRequested
            || DateTimeOffset.UtcNow >= _deadline;

        internal static DeadlineCancellation Start(
            DateTimeOffset deadline,
            CancellationToken callerCancellation) =>
            new(deadline, callerCancellation);

        public async ValueTask DisposeAsync()
        {
            _schedulerStop.Cancel();
            try
            {
                await _scheduler.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (_schedulerStop.IsCancellationRequested)
            {
            }
            finally
            {
                _operationCancellation.Dispose();
                _deadlineCancellation.Dispose();
                _schedulerStop.Dispose();
            }
        }

        private static async Task ScheduleAsync(
            DateTimeOffset deadline,
            CancellationTokenSource deadlineCancellation,
            CancellationToken schedulerStop)
        {
            while (true)
            {
                TimeSpan remaining =
                    deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    deadlineCancellation.Cancel();
                    return;
                }

                await Task.Delay(
                        remaining <= s_maximumTimerDelay
                            ? remaining
                            : s_maximumTimerDelay,
                        schedulerStop)
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed record MappingPreparation(
        SourceHouseAuthoredMapping? Mapping,
        SourceDocumentObservation? Document,
        int DocumentsObserved,
        int TargetMappingsObserved,
        SourceHouseIncompleteBoundary? IncompleteBoundary,
        SourceHouseFailure? Failure)
    {
        internal static MappingPreparation Available(
            SourceHouseAuthoredMapping mapping,
            SourceDocumentObservation document,
            int documentsObserved,
            int targetMappingsObserved) =>
            new(
                mapping,
                document,
                documentsObserved,
                targetMappingsObserved,
                IncompleteBoundary: null,
                Failure: null);

        internal static MappingPreparation Unavailable(
            int documentsObserved = 0,
            int targetMappingsObserved = 0) =>
            new(
                Mapping: null,
                Document: null,
                documentsObserved,
                targetMappingsObserved,
                IncompleteBoundary: null,
                Failure: null);

        internal static MappingPreparation Incomplete(
            SourceHouseIncompleteBoundary boundary,
            int documentsObserved,
            int TargetMappingsObserved) =>
            new(
                Mapping: null,
                Document: null,
                documentsObserved,
                TargetMappingsObserved,
                boundary,
                Failure: null);

        internal static MappingPreparation Failed(
            SourceHouseFailureStage stage,
            string code,
            string? detail = null,
            int documentsObserved = 0) =>
            new(
                Mapping: null,
                Document: null,
                documentsObserved,
                TargetMappingsObserved: 0,
                IncompleteBoundary: null,
                new(stage, code, detail));
    }

    private abstract record ProvisionalOutcome(
        SourceHousePdbContribution PdbContribution,
        SourceHouseAuthoredAttempt AuthoredAttempt,
        SourceHouseWorkCharge Work)
    {
        internal abstract SourceHouseOutcome Complete(
            SourceHouseRequestEvidence request,
            SourceHouseLibraryLeaseSettlement settlement);
    }

    private sealed record AvailableOutcome(
        SourceHousePdbContribution PdbContribution,
        SourceHouseAuthoredAttempt.Available Available,
        SourceHouseWorkCharge Work)
        : ProvisionalOutcome(PdbContribution, Available, Work)
    {
        internal override SourceHouseOutcome Complete(
            SourceHouseRequestEvidence request,
            SourceHouseLibraryLeaseSettlement settlement) =>
            new SourceHouseOutcome.Available(
                request,
                PdbContribution,
                Available,
                Work,
                settlement);
    }

    private sealed record UnavailableOutcome(
        SourceHousePdbContribution PdbContribution,
        SourceHouseAuthoredAttempt.Unavailable Unavailable,
        SourceHouseWorkCharge Work)
        : ProvisionalOutcome(PdbContribution, Unavailable, Work)
    {
        internal override SourceHouseOutcome Complete(
            SourceHouseRequestEvidence request,
            SourceHouseLibraryLeaseSettlement settlement) =>
            new SourceHouseOutcome.Unavailable(
                request,
                PdbContribution,
                Unavailable,
                Work,
                settlement);
    }

    private sealed record RejectedOutcome(
        SourceHousePdbContribution PdbContribution,
        SourceHouseAuthoredAttempt.Rejected RejectedAttempt,
        SourceHouseRejection Rejection,
        SourceHouseWorkCharge Work)
        : ProvisionalOutcome(PdbContribution, RejectedAttempt, Work)
    {
        internal override SourceHouseOutcome Complete(
            SourceHouseRequestEvidence request,
            SourceHouseLibraryLeaseSettlement settlement) =>
            new SourceHouseOutcome.Rejected(
                request,
                PdbContribution,
                RejectedAttempt,
                Rejection,
                Work,
                settlement);
    }

    private sealed record FailedOutcome(
        SourceHousePdbContribution PdbContribution,
        SourceHouseAuthoredAttempt.Failed FailedAttempt,
        SourceHouseFailure Failure,
        SourceHouseWorkCharge Work)
        : ProvisionalOutcome(PdbContribution, FailedAttempt, Work)
    {
        internal override SourceHouseOutcome Complete(
            SourceHouseRequestEvidence request,
            SourceHouseLibraryLeaseSettlement settlement) =>
            new SourceHouseOutcome.Failed(
                request,
                PdbContribution,
                FailedAttempt,
                Failure,
                Work,
                settlement);
    }

    private sealed record IncompleteOutcome(
        SourceHousePdbContribution PdbContribution,
        SourceHouseAuthoredAttempt.Incomplete IncompleteAttempt,
        SourceHouseIncompleteBoundary Boundary,
        SourceHouseWorkCharge Work)
        : ProvisionalOutcome(PdbContribution, IncompleteAttempt, Work)
    {
        internal override SourceHouseOutcome Complete(
            SourceHouseRequestEvidence request,
            SourceHouseLibraryLeaseSettlement settlement) =>
            new SourceHouseOutcome.Incomplete(
                request,
                PdbContribution,
                IncompleteAttempt,
                Boundary,
                Work,
                settlement);
    }
}
