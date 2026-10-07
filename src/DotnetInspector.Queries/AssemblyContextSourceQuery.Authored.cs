using System.Collections.Immutable;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Findings;
using Inspector.Text;
using AuthoredSourceHouse = DotnetInspector.SourceHouse.SourceHouse;

namespace DotnetInspector.Queries;

public sealed record AssemblySourcePdbProvenance(
    AssemblyAcquisitionRegistration SourceRegistration,
    CodeViewInfo? Identity,
    string? Location,
    string? Path,
    string? SymbolServer) : IArtifactProvenance;

public static partial class AssemblyContextSourceQuery
{
    internal static async Task<MemberBestAvailableInspection>
        InspectMemberBestAvailableAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyMemberSourceRequest request,
            AssemblyContextSourceQueryContext context,
            ResolvedAssemblyReference retained,
            AssemblyBindingPolicyVersion version,
            CancellationToken cancellationToken)
    {
        var findingSubject = new FindingSubject(
            "member",
            request.Member.Format(MemberAnchorFormat.Qualified));
        AuthoredPdbInspection settled = await InspectAuthoredPdbAsync(
                group,
                participant,
                context,
                retained,
                version,
                new SourceHouseTarget.MemberTarget(
                    request.Type,
                    request.Member,
                    request.MetadataToken,
                    request.IncludeAuthoredParts
                        ? SourceHouseMemberSourceForm
                            .DocumentParts
                        : SourceHouseMemberSourceForm
                            .DeclarationText),
                operationName: "member-best-available",
                limits: context.MemberSourceLimits,
                timeout: context.MemberSourceTimeout,
                cancellationToken: cancellationToken,
                retainLibrary: false,
                retainAuthoredSession: false,
                retainedOperationLimits:
                    context.MemberDecompilationLimits,
                pdbEvidence: null,
                bestAvailable: true,
                decompilationPrinterOptions:
                    request.PrinterOptions)
            .ConfigureAwait(false);
        PdbMemberSourceInspection authored = settled switch
        {
            {
                BestAvailableOutcome.AuthoredOutcome:
                SourceHouseOutcome.Available outcome,
            } =>
                ProjectMemberAuthored(
                    outcome,
                    findingSubject),
            { AcquisitionFailure: { } failure } =>
                PdbSourceInspectionProjection.MemberAcquisitionFailed(
                    findingSubject,
                    failure),
            { LibraryFailure: { } terminal } =>
                UnsuccessfulMemberInspection(
                    findingSubject,
                    terminal
                        is AssemblyContextLibraryAdapterResult
                            .Incomplete
                        ? PdbMemberSourceOutcome.SourceLimitExceeded
                        : PdbMemberSourceOutcome.InspectionFailed,
                    AdmissionDetail(terminal),
                    failed: true),
            {
                BestAvailableOutcome.AuthoredOutcome:
                { } outcome,
            } =>
                ProjectMemberAuthored(
                    outcome,
                    findingSubject),
            _ => throw new InvalidOperationException(
                "Best-available member source inspection did not settle."),
        };
        return new(
            authored,
            authored.IsComplete
                ? settled.Provenance
                : null,
            settled.BestAvailableOutcome,
            settled.LibraryFailure);
    }

    internal static async Task<MemberPdbInspection> InspectMemberPdbAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion version,
        SourceHouseLimits limits,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool retainLibrary = false,
        bool retainAuthoredSession = false,
        SourceHouseDecompilationLimits?
            retainedOperationLimits = null)
    {
        var findingSubject = new FindingSubject(
            "member", request.Member.Format(MemberAnchorFormat.Qualified));
        AuthoredPdbInspection authored = await InspectAuthoredPdbAsync(
            group, participant, context, retained, version,
            new SourceHouseTarget.MemberTarget(
                request.Type, request.Member, request.MetadataToken,
                request.IncludeAuthoredParts
                    ? SourceHouseMemberSourceForm.DocumentParts
                    : SourceHouseMemberSourceForm.DeclarationText),
            operationName: "member-source", limits, timeout, cancellationToken,
            retainLibrary,
            retainAuthoredSession,
            retainedOperationLimits,
            pdbEvidence: null)
            .ConfigureAwait(false);
        PdbMemberSourceInspection inspection = authored switch
        {
            { AcquisitionFailure: { } failure } =>
                PdbSourceInspectionProjection.MemberAcquisitionFailed(
                    findingSubject,
                    failure),
            { LibraryFailure: { } terminal } =>
                UnsuccessfulMemberInspection(
                    findingSubject, terminal is AssemblyContextLibraryAdapterResult.Incomplete
                        ? PdbMemberSourceOutcome.SourceLimitExceeded
                        : PdbMemberSourceOutcome.InspectionFailed,
                    AdmissionDetail(terminal), failed: true),
            { HouseOutcome: { } outcome } => ProjectMemberAuthored(outcome, findingSubject),
            _ => throw new InvalidOperationException("Authored source inspection did not settle."),
        };
        return new(
            inspection,
            inspection.IsComplete ? authored.Provenance : null)
        {
            HouseOutcome = authored.HouseOutcome,
            LibraryFailure = authored.LibraryFailure,
            RetainedLibrary = authored.RetainedLibrary,
            RetainedAuthoredSession =
                authored.RetainedAuthoredSession,
            AcquisitionFailure =
                authored.AcquisitionFailure,
        };
    }

    internal static async Task<TypePdbInspection> InspectTypePdbAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion version,
        SourceHouseLimits limits,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        PortablePdbAcquisitionEvidenceCollector? pdbEvidence = null)
    {
        var findingSubject = new FindingSubject(
            "type", request.Type.ToMetadataFullName());
        AuthoredPdbInspection authored = await InspectAuthoredPdbAsync(
            group, participant, context, retained, version,
            new SourceHouseTarget.TypeTarget(request.Type, request.OriginalDocumentPath),
            operationName: "type-source", limits, timeout, cancellationToken,
            retainLibrary: false,
            retainAuthoredSession: false,
            retainedOperationLimits: null,
            pdbEvidence).ConfigureAwait(false);
        PdbTypeSourceInspection inspection = authored switch
        {
            { AcquisitionFailure: { } failure } =>
                PdbSourceInspectionProjection.TypeAcquisitionFailed(
                    findingSubject,
                    failure),
            { LibraryFailure: { } terminal } =>
                UnsuccessfulTypeInspection(
                    findingSubject, terminal is AssemblyContextLibraryAdapterResult.Incomplete
                        ? PdbTypeSourceOutcome.SourceLimitExceeded
                        : PdbTypeSourceOutcome.InspectionFailed,
                    AdmissionDetail(terminal), failed: true),
            { HouseOutcome: { } outcome } =>
                ProjectTypeAuthored(outcome, findingSubject),
            _ => throw new InvalidOperationException(
                "Authored source inspection did not settle."),
        };
        inspection = inspection with
        {
            PortablePdbAvailable =
                authored.PortablePdbAvailable,
        };
        if (inspection.IsComplete
            && authored.Provenance is null
            && authored.ProvenanceFailure is { } provenanceFailure)
        {
            throw provenanceFailure;
        }
        return new(
            inspection,
            inspection.IsComplete ? authored.Provenance : null)
        {
            HouseOutcome = authored.HouseOutcome,
            LibraryFailure = authored.LibraryFailure,
        };
    }

    internal static async Task<TypeBestAvailableInspection>
        InspectTypeBestAvailableAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            ResolvedAssemblyReference retained,
            AssemblyBindingPolicyVersion version,
            PortablePdbAcquisitionEvidenceCollector? pdbEvidence,
            CancellationToken cancellationToken)
    {
        var findingSubject = new FindingSubject(
            "type",
            request.Type.ToMetadataFullName());
        AuthoredPdbInspection settled = await InspectAuthoredPdbAsync(
                group,
                participant,
                context,
                retained,
                version,
                new SourceHouseTarget.TypeTarget(request.Type),
                operationName: "type-best-available",
                limits: context.TypeSourceLimits,
                timeout: context.TypeSourceTimeout,
                cancellationToken: cancellationToken,
                retainLibrary: false,
                retainAuthoredSession: false,
                retainedOperationLimits:
                    context.TypeDecompilationLimits,
                pdbEvidence: pdbEvidence,
                bestAvailable: true,
                decompilationPrinterOptions:
                    request.PrinterOptions)
            .ConfigureAwait(false);
        PdbTypeSourceInspection authored = settled switch
        {
            { AcquisitionFailure: { } failure } =>
                PdbSourceInspectionProjection.TypeAcquisitionFailed(
                    findingSubject,
                    failure),
            { LibraryFailure: { } terminal } =>
                UnsuccessfulTypeInspection(
                    findingSubject,
                    terminal
                        is AssemblyContextLibraryAdapterResult
                            .Incomplete
                        ? PdbTypeSourceOutcome.SourceLimitExceeded
                        : PdbTypeSourceOutcome.InspectionFailed,
                    AdmissionDetail(terminal),
                    failed: true),
            {
                BestAvailableOutcome.AuthoredOutcome:
                { } outcome,
            } =>
                ProjectTypeAuthored(
                    outcome,
                    findingSubject),
            _ => throw new InvalidOperationException(
                "Best-available type source inspection did not settle."),
        };
        authored = authored with
        {
            PortablePdbAvailable =
                settled.PortablePdbAvailable,
        };
        if (authored.IsComplete
            && settled.Provenance is null
            && settled.ProvenanceFailure is { } provenanceFailure)
        {
            throw provenanceFailure;
        }
        return new(
            authored,
            authored.IsComplete
                ? settled.Provenance
                : null,
            settled.BestAvailableOutcome,
            settled.LibraryFailure);
    }

    static async Task<AuthoredPdbInspection> InspectAuthoredPdbAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextSourceQueryContext context,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion version,
        SourceHouseTarget target,
        string operationName,
        SourceHouseLimits limits,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool retainLibrary,
        bool retainAuthoredSession,
        SourceHouseDecompilationLimits?
            retainedOperationLimits,
        PortablePdbAcquisitionEvidenceCollector? pdbEvidence,
        bool bestAvailable = false,
        PrinterOptions? decompilationPrinterOptions = null)
    {
        if (retainAuthoredSession
            && (!retainLibrary || bestAvailable))
        {
            throw new ArgumentException(
                "A retained authored session requires authored-only retained-Library execution.",
                nameof(retainAuthoredSession));
        }

        var opened = await OpenSourceLinkAsync(
            retained,
            context,
            pdbEvidence,
            cancellationToken).ConfigureAwait(false);

        AssemblyContextLibraryPortablePdb? companion = null;
        ImmutableArray<byte>? pdbImage = null;
        AssemblyPdbSourceProvenance? provenance = null;
        Exception? provenanceFailure = null;
        Exception? acquisitionFailure = opened.Failure;
        Exception? primaryFailure = null;
        bool? portablePdbAvailable = null;
        bool authoredPortablePdbUnavailable = false;
        if (opened.Source is { } source)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(participant, version);
                portablePdbAvailable =
                    source.Context.HasPdb
                        ? true
                        : source.Context.PdbId is not null
                            ? false
                            : null;
                authoredPortablePdbUnavailable =
                    !source.Context.HasPdb;
                try
                {
                    provenance = new(
                        source.RepositoryUrl,
                        source.CommitHash);
                }
                catch (Exception failure) when (
                    target is SourceHouseTarget.TypeTarget
                    && IsInspectionFailure(failure))
                {
                    // Malformed type-map provenance must not suppress decompiler fallback.
                    // InspectTypePdbAsync rethrows this failure if authored source succeeds.
                    provenanceFailure = failure;
                }
                if (!source.Context.HasEmbeddedPdb)
                    pdbImage = source.Context.GetPortablePdbImage();
                if (!source.Context.HasEmbeddedPdb
                    && pdbImage is { } image)
                {
                    companion = new(
                        image,
                        new AssemblySourcePdbProvenance(
                            participant.Assembly.Registration,
                            source.Context.PdbId,
                            source.Context.PdbLocation,
                            source.Context.PortablePdbPath,
                            source.Context.SymbolServer));
                }
            }
            catch (Exception failure)
            {
                primaryFailure = failure;
                throw;
            }
            finally
            {
                Exception? cleanup = source.DisposeWithFailure();
                if (primaryFailure is not null && cleanup is not null)
                    ArtifactSetSession.AttachCleanupFailures(primaryFailure, [cleanup]);
                else if (primaryFailure is null)
                    ValidateAfterSourceDisposal(
                        participant, version, cancellationToken, cleanup);
            }
        }
        else
        {
            authoredPortablePdbUnavailable = true;
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(participant, version);
            if (!retainLibrary && !bestAvailable)
            {
                return new(
                    AcquisitionFailure: acquisitionFailure)
                {
                    PortablePdbAvailable =
                        acquisitionFailure is null
                            ? null
                            : false,
                };
            }
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        SourceHouseOperationPlanIdentity operationIdentity =
            SourceHouseOperationPlanIdentity.Create(operationName);
        SourceHousePolicyGeneration policyGeneration =
            SourceHousePolicyGeneration.Create($"{operationName}-v1");
        var plan = new SourceHouseOperationPlan(
            operationIdentity,
            policyGeneration,
            limits,
            DateTimeOffset.UtcNow.Add(timeout),
            AssemblyContextSourceCapabilities.Create(context));
        AssemblyContextLibraryAdapterResult admission =
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                new(
                    Math.Max(
                        1,
                        Math.Max(
                            Math.Max(
                                limits.MaximumAssemblyBytes,
                                limits.MaximumPortablePdbBytes),
                            Math.Max(
                                retainedOperationLimits
                                    ?.MaximumAssemblyBytes ?? 0,
                                retainedOperationLimits
                                    ?.MaximumPortablePdbBytes ?? 0))),
                    Math.Max(1, Math.Min(int.MaxValue,
                        Math.Max(
                            (long)limits.MaximumAssemblyBytes
                                + limits.MaximumPortablePdbBytes,
                            (long)(retainedOperationLimits
                                    ?.MaximumAssemblyBytes ?? 0)
                                + (retainedOperationLimits
                                    ?.MaximumPortablePdbBytes ?? 0))))),
                companion, cancellationToken).ConfigureAwait(false);
        if (admission is AssemblyContextLibraryAdapterResult.Terminal terminal)
        {
            ThrowCleanupFailures(terminal.CleanupFailures);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(participant, version);
            return new(
                Provenance: provenance,
                AcquisitionFailure: acquisitionFailure,
                ProvenanceFailure: provenanceFailure)
            {
                LibraryFailure = terminal,
                PortablePdbAvailable =
                    portablePdbAvailable,
            };
        }
        if (admission is not AssemblyContextLibraryAdapterResult.Completed completed)
            throw new InvalidOperationException("Unknown Library admission result.");

        bool keepLibrary =
            retainLibrary
            && (!retainAuthoredSession
                || acquisitionFailure is null);
        AuthoredSourceHouse.AuthoredSession?
            authoredSession = null;
        SourceHouseOutcome? outcome = null;
        SourceHouseBestAvailableOutcome? bestAvailableOutcome =
            null;
        CancellationObservingBindingPolicy? bindingPolicy =
            null;
        primaryFailure = null;
        try
        {
            if (retainAuthoredSession
                && acquisitionFailure is null)
            {
                authoredSession =
                    AuthoredSourceHouse
                        .OpenAuthoredSession(
                            completed.Reference,
                            completed.Reference
                                .ImplementationAssembly
                                ?? throw new InvalidOperationException(
                                    "The admitted source Library has no implementation assembly."),
                            limits);
            }
            EnsureBindingPolicyVersion(participant, version);
            if (acquisitionFailure is null || bestAvailable)
            {
                if (bestAvailable)
                {
                    if (retainedOperationLimits is null
                        || target is SourceHouseTarget.TypeTarget
                        {
                            OriginalDocumentPath: not null,
                        })
                    {
                        throw new InvalidOperationException(
                            "Best-available settlement requires an exact member or type target and decompilation limits.");
                    }
                    bindingPolicy =
                        new CancellationObservingBindingPolicy(
                            participant.BindingPolicy);
                    var decompilationPlan =
                        new SourceHouseDecompilationPlan(
                            operationIdentity,
                            policyGeneration,
                            retainedOperationLimits,
                            bindingPolicy,
                            decompilationPrinterOptions,
                            context
                                .MaxDecompilerBodyProjections);
                    var houseRequest =
                        new SourceHouseBestAvailableRequest(
                            SourceHouseRequestIdentity.Create(
                                operationName),
                            completed.Reference,
                            completed.Reference
                                .ImplementationAssembly!,
                            target,
                            plan,
                            decompilationPlan,
                            authoredPortablePdbUnavailable
                                ? SourceHouseBestAvailableAuthoredPrecondition
                                    .PortablePdbUnavailable
                                : SourceHouseBestAvailableAuthoredPrecondition
                                    .Attempt);
                    if (completed.Owner.IssueOperationLease(
                            completed.Reference)
                        is not LibraryOperationLeaseIssueOutcome
                            .Issued issued)
                    {
                        throw new InvalidOperationException(
                            "The admitted Library could not issue its source operation lease.");
                    }
                    bestAvailableOutcome =
                        await AuthoredSourceHouse
                            .ExecuteBestAvailableAsync(
                                houseRequest,
                                issued.Lease,
                                cancellationToken)
                            .ConfigureAwait(false);
                    outcome =
                        bestAvailableOutcome.AuthoredOutcome;
                }
                else
                {
                    var houseRequest =
                        new SourceHouseAuthoredRequest(
                            SourceHouseRequestIdentity.Create(
                                operationName),
                            completed.Reference,
                            completed.Reference
                                .ImplementationAssembly!,
                            target,
                            plan);
                    if (completed.Owner.IssueOperationLease(
                            completed.Reference)
                        is not LibraryOperationLeaseIssueOutcome
                            .Issued issued)
                    {
                        throw new InvalidOperationException(
                            "The admitted Library could not issue its source operation lease.");
                    }
                    outcome =
                        authoredSession is null
                            ? await AuthoredSourceHouse
                                .ExecuteAuthoredAsync(
                                    houseRequest,
                                    issued.Lease,
                                    cancellationToken)
                                .ConfigureAwait(false)
                            : await authoredSession
                                .ExecuteAsync(
                                houseRequest,
                                issued.Lease,
                                cancellationToken)
                            .ConfigureAwait(false);
                }
            }
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            if (!keepLibrary || primaryFailure is not null)
            {
                Exception? sessionDisposalFailure =
                    null;
                if (authoredSession is not null)
                {
                    try
                    {
                        await authoredSession
                            .DisposeAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        sessionDisposalFailure =
                            failure;
                        if (primaryFailure
                            is not null)
                        {
                            ArtifactSetSession
                                .AttachCleanupFailures(
                                    primaryFailure,
                                    [failure]);
                        }
                    }
                }
                await RetireSourceHouseLibraryAsync(
                        completed,
                        primaryFailure
                        ?? sessionDisposalFailure)
                    .ConfigureAwait(false);
                if (primaryFailure is null
                    && sessionDisposalFailure is not null)
                {
                    throw sessionDisposalFailure;
                }
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bindingPolicy?.ThrowIfObserved();
            EnsureBindingPolicyVersion(participant, version);
        }
        catch (Exception failure)
        {
            if (keepLibrary)
            {
                if (authoredSession is not null)
                {
                    try
                    {
                        await authoredSession
                            .DisposeAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanup)
                    {
                        ArtifactSetSession
                            .AttachCleanupFailures(
                                failure,
                                [cleanup]);
                    }
                }
                await RetireSourceHouseLibraryAsync(
                        completed,
                        failure)
                    .ConfigureAwait(false);
            }
            throw;
        }
        return new(
            Provenance: provenance,
            AcquisitionFailure: acquisitionFailure,
            ProvenanceFailure: provenanceFailure)
        {
            HouseOutcome = outcome,
            BestAvailableOutcome =
                bestAvailableOutcome,
            RetainedLibrary = keepLibrary ? completed : null,
            RetainedAuthoredSession =
                keepLibrary
                    ? authoredSession
                    : null,
            PortablePdbAvailable =
                portablePdbAvailable,
        };
    }

    internal static async ValueTask RetireSourceHouseLibraryAsync(
        AssemblyContextLibraryAdapterResult.Completed completed,
        Exception? primaryFailure)
    {
        List<Exception> failures = [];
        try
        {
            await completed.Owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        failures.AddRange(completed.Owner.CleanupFailures);
        try
        {
            await completed.Artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        failures.AddRange(completed.Artifacts.CleanupFailures);
        if (primaryFailure is not null)
            ArtifactSetSession.AttachCleanupFailures(primaryFailure, failures);
        else
            ThrowCleanupFailures(failures);
    }

    static void ThrowCleanupFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count > 0)
            throw new InvalidOperationException(
                "SourceHouse Library/Artifact retirement failed.",
                new AggregateException(failures));
    }

    static string AdmissionDetail(AssemblyContextLibraryAdapterResult.Terminal terminal) =>
        terminal switch
        {
            AssemblyContextLibraryAdapterResult.Incomplete incomplete =>
                $"Library {incomplete.ContentRole} capture exceeded {incomplete.MaxCapturedImageBytes} bytes.",
            AssemblyContextLibraryAdapterResult.PortablePdbRejected rejected =>
                $"Portable PDB companion rejected: {string.Join("; ", rejected.Observations)}",
            AssemblyContextLibraryAdapterResult.ArtifactNotPublished notPublished =>
                $"Library artifacts were not published: {string.Join("; ", notPublished.Publication.Failures.Select(
                    failure => failure.Diagnostic.Code))}",
            AssemblyContextLibraryAdapterResult.SnapshotRejected rejected =>
                $"Library snapshot rejected: {rejected.Failure}",
            AssemblyContextLibraryAdapterResult.MetadataNotProjected =>
                "Library assembly metadata was not projected.",
            _ => throw new InvalidOperationException("Unknown Library admission terminal."),
        };

    static PdbMemberSourceInspection ProjectMemberAuthored(
        SourceHouseOutcome outcome,
        FindingSubject subject)
    {
        var mapping = outcome.AuthoredAttempt.Mapping as SourceHouseAuthoredMapping.Member;
        SourceHouseSourceAttempt? last = outcome.AuthoredAttempt.SourceAttempts.LastOrDefault();
        SourceChecksumVerification? verification = last?.ChecksumVerification;
        if (outcome is SourceHouseOutcome.Available available)
        {
            return new(
                new FindingInspection<string>.Complete(
                    TextFindings.Inspect(available.Source.Text, subject).ToImmutableArray()),
                available.Source.Text, mapping?.Observation, mapping?.Document,
                available.Source.Selected.ChecksumVerification);
        }

        PdbMemberSourceOutcome kind;
        string detail;
        bool failed = true;
        if (outcome is SourceHouseOutcome.Incomplete incomplete)
        {
            kind = incomplete.Boundary == SourceHouseIncompleteBoundary.Deadline
                ? PdbMemberSourceOutcome.SourceDeadlineExceeded
                : PdbMemberSourceOutcome.SourceLimitExceeded;
            detail = $"Authored source stopped at its {incomplete.Boundary} bound.";
        }
        else if (outcome is SourceHouseOutcome.Rejected rejected)
        {
            kind = PdbMemberSourceOutcome.InspectionFailed;
            detail = $"Authored source input rejected: {rejected.Rejection.Kind}.";
        }
        else if (last?.Observation?.Code == "ChecksumMismatch"
            || verification == SourceChecksumVerification.Mismatch)
        {
            kind = PdbMemberSourceOutcome.ChecksumMismatch;
            verification = SourceChecksumVerification.Mismatch;
            detail = "Fetched PDB source does not match the portable-PDB checksum.";
        }
        else if (last?.Observation?.Code == nameof(SourceChecksumVerification.Unsupported)
            || verification == SourceChecksumVerification.Unsupported)
        {
            kind = PdbMemberSourceOutcome.ChecksumUnsupported;
            verification = SourceChecksumVerification.Unsupported;
            detail = "The portable-PDB source checksum algorithm is unsupported.";
        }
        else if (outcome is SourceHouseOutcome.Failed failure)
        {
            kind = (failure.Failure.Stage, failure.Failure.Code) switch
            {
                (SourceHouseFailureStage.SourceSlicing, "SourceTooComplex") =>
                    PdbMemberSourceOutcome.SourceTooComplex,
                (SourceHouseFailureStage.SourceSlicing, "InvalidSequencePointCoordinates") =>
                    PdbMemberSourceOutcome.InvalidSequencePointCoordinates,
                (SourceHouseFailureStage.SourceSlicing, "SourceExtractionFailed")
                    or (SourceHouseFailureStage.SourceVerification, "SourceDecodeFailed") =>
                    PdbMemberSourceOutcome.SourceExtractionFailed,
                (SourceHouseFailureStage.SourceCapability, _) =>
                    PdbMemberSourceOutcome.SourceAcquisitionFailed,
                _ => PdbMemberSourceOutcome.InspectionFailed,
            };
            detail = $"{failure.Failure.Code}: {failure.Failure.Detail}";
        }
        else if (outcome.PdbContribution.Kind == SourceHousePdbContributionKind.Unavailable)
        {
            kind = PdbMemberSourceOutcome.PortablePdbUnavailable;
            detail = "A matching portable PDB remains unresolved after acquisition.";
        }
        else
        {
            failed = false;
            kind = mapping is null
                ? PdbMemberSourceOutcome.SourceMappingUnavailable
                : last?.Observation?.Code == "NoVouchedMemberDeclaration"
                    ? PdbMemberSourceOutcome.NoVouchedDeclaration
                    : mapping.Document.Checksum is not { Length: > 0 }
                        || mapping.Document.ChecksumAlgorithm is not { Length: > 0 }
                        ? PdbMemberSourceOutcome.ChecksumUnavailable
                        : PdbMemberSourceOutcome.SourceAcquisitionUnavailable;
            detail = last?.Observation?.Detail ?? last?.Observation?.Code
                ?? "The selected member has no available authored source.";
        }
        return UnsuccessfulMemberInspection(subject, kind, detail, failed, mapping, verification);
    }

    static PdbMemberSourceInspection UnsuccessfulMemberInspection(
        FindingSubject subject,
        PdbMemberSourceOutcome outcome,
        string detail,
        bool failed,
        SourceHouseAuthoredMapping.Member? mapping = null,
        SourceChecksumVerification? verification = null) =>
        new(
            failed
                ? new FindingInspection<string>.Failed(
                    new InspectionError(subject, TextFindings.LineDescriptor, detail))
                : new FindingInspection<string>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput, detail),
            null, mapping?.Observation, mapping?.Document, verification)
        {
            Outcome = outcome,
        };

    static PdbTypeSourceInspection ProjectTypeAuthored(
        SourceHouseOutcome outcome,
        FindingSubject subject)
    {
        var mapping = outcome.AuthoredAttempt.Mapping as SourceHouseAuthoredMapping.Type;
        SourceHouseSourceAttempt? last = outcome.AuthoredAttempt.SourceAttempts.LastOrDefault();
        SourceChecksumVerification? verification = last?.ChecksumVerification;
        if (outcome is SourceHouseOutcome.Available available)
        {
            if (mapping is null)
                throw new InvalidOperationException("Available type source requires its mapping.");
            PdbTypeSourceInspection inspection =
                PdbSourceInspectionProjection.FromVerifiedTypeContent(
                    mapping.SourceMapping,
                    mapping.Document,
                    available.Source.Text,
                    available.Source.Selected.ChecksumVerification
                        ?? throw new InvalidOperationException(
                            "Available type source requires checksum verification."),
                    subject);
            return WithTypeEvidence(inspection, mapping);
        }

        PdbTypeSourceOutcome kind;
        string detail;
        bool failed = true;
        if (outcome is SourceHouseOutcome.Incomplete incomplete)
        {
            kind = incomplete.Boundary == SourceHouseIncompleteBoundary.Deadline
                ? PdbTypeSourceOutcome.SourceDeadlineExceeded
                : PdbTypeSourceOutcome.SourceLimitExceeded;
            detail = $"Authored source stopped at its {incomplete.Boundary} bound.";
        }
        else if (outcome is SourceHouseOutcome.Rejected rejected)
        {
            kind = PdbTypeSourceOutcome.InspectionFailed;
            detail = $"Authored source input rejected: {rejected.Rejection.Kind}.";
        }
        else if (last?.Observation?.Code == "ChecksumMismatch"
            || verification == SourceChecksumVerification.Mismatch)
        {
            kind = PdbTypeSourceOutcome.ChecksumMismatch;
            verification = SourceChecksumVerification.Mismatch;
            detail = "Fetched PDB source does not match the portable-PDB checksum.";
        }
        else if (last?.Observation?.Code == nameof(SourceChecksumVerification.Unsupported)
            || verification == SourceChecksumVerification.Unsupported)
        {
            kind = PdbTypeSourceOutcome.ChecksumUnsupported;
            verification = SourceChecksumVerification.Unsupported;
            detail = "The portable-PDB source checksum algorithm is unsupported.";
        }
        else if (outcome is SourceHouseOutcome.Failed failure)
        {
            kind = (failure.Failure.Stage, failure.Failure.Code) switch
            {
                (SourceHouseFailureStage.SourceVerification, "SourceDecodeFailed") =>
                    PdbTypeSourceOutcome.SourceExtractionFailed,
                (SourceHouseFailureStage.SourceCapability, _) =>
                    PdbTypeSourceOutcome.SourceAcquisitionFailed,
                _ => PdbTypeSourceOutcome.InspectionFailed,
            };
            detail = failure.Failure switch
            {
                { Stage: SourceHouseFailureStage.SourceCapability, Code: "StorageFailed" } =>
                    "The source-content store failed.",
                {
                    Stage: SourceHouseFailureStage.PortablePdbInspection
                    or SourceHouseFailureStage.SourceLinkInspection
                    or SourceHouseFailureStage.TargetMapping
                } =>
                    $"Portable PDB type source mapping failed: "
                    + $"{failure.Failure.Detail ?? failure.Failure.Code}",
                _ => $"{failure.Failure.Code}: {failure.Failure.Detail}",
            };
        }
        else if (outcome.PdbContribution.Kind == SourceHousePdbContributionKind.Unavailable)
        {
            kind = PdbTypeSourceOutcome.PortablePdbUnavailable;
            detail = "A matching portable PDB remains unresolved after acquisition.";
        }
        else
        {
            failed = false;
            kind = mapping is null
                ? PdbTypeSourceOutcome.SourceMappingUnavailable
                : mapping.Document.Checksum is not { Length: > 0 }
                    || mapping.Document.ChecksumAlgorithm is not { Length: > 0 }
                    ? PdbTypeSourceOutcome.ChecksumUnavailable
                    : PdbTypeSourceOutcome.SourceAcquisitionUnavailable;
            detail = mapping is null
                ? "The selected type has no portable-PDB source mapping."
                : last?.Observation?.Detail ?? last?.Observation?.Code
                    ?? "The selected type has no available authored source.";
        }
        return UnsuccessfulTypeInspection(
            subject, kind, detail, failed, mapping, verification);
    }

    static PdbTypeSourceInspection UnsuccessfulTypeInspection(
        FindingSubject subject,
        PdbTypeSourceOutcome outcome,
        string detail,
        bool failed,
        SourceHouseAuthoredMapping.Type? mapping = null,
        SourceChecksumVerification? verification = null) =>
        TypeInspection(
            failed
                ? new FindingInspection<string>.Failed(
                    new InspectionError(subject, TextFindings.LineDescriptor, detail))
                : new FindingInspection<string>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput, detail),
            text: null,
            mapping,
            verification,
            outcome);

    static PdbTypeSourceInspection TypeInspection(
        FindingInspection<string> lines,
        string? text,
        SourceHouseAuthoredMapping.Type? mapping,
        SourceChecksumVerification? verification,
        PdbTypeSourceOutcome outcome)
    {
        var inspection = new PdbTypeSourceInspection(
            lines,
            text,
            mapping?.SourceMapping,
            mapping?.Document,
            verification)
        {
            Outcome = outcome,
        };
        return WithTypeEvidence(inspection, mapping);
    }

    static PdbTypeSourceInspection WithTypeEvidence(
        PdbTypeSourceInspection inspection,
        SourceHouseAuthoredMapping.Type? mapping) =>
        inspection with
        {
            Scope = mapping?.Scope switch
            {
                SourceHouseSourceUnitScope.PrimaryTypeDocument =>
                    PdbTypeSourceUnitScope.PrimaryTypeDocument,
                SourceHouseSourceUnitScope.AdditionalTypeDocument =>
                    PdbTypeSourceUnitScope.AdditionalTypeDocument,
                null => null,
                _ => throw new InvalidOperationException("Unknown type source unit scope."),
            },
            Strength = mapping?.Strength switch
            {
                SourceHouseMappingStrength.CorrelatedTypeDocument =>
                    PdbTypeSourceMappingStrength.CorrelatedTypeDocument,
                SourceHouseMappingStrength.InferredTypeDocument =>
                    PdbTypeSourceMappingStrength.InferredTypeDocument,
                null => null,
                _ => throw new InvalidOperationException("Unknown type source mapping strength."),
            },
            IsPartial = mapping?.IsPartial ?? false,
            AdditionalDocuments = mapping is null
                ? Array.Empty<PdbTypeSourceAdditionalDocument>()
                : mapping.AdditionalDocuments
                    .Select(static document => new PdbTypeSourceAdditionalDocument(
                        document.OriginalPath, document.ResolvedUrl))
                    .ToArray(),
        };

    sealed record AuthoredPdbInspection(
        AssemblyPdbSourceProvenance? Provenance = null,
        Exception? AcquisitionFailure = null,
        Exception? ProvenanceFailure = null)
    {
        public SourceHouseOutcome? HouseOutcome { get; init; }
        public SourceHouseBestAvailableOutcome?
            BestAvailableOutcome
        { get; init; }
        public AssemblyContextLibraryAdapterResult.Terminal? LibraryFailure { get; init; }
        public AssemblyContextLibraryAdapterResult.Completed?
            RetainedLibrary
        { get; init; }
        public AuthoredSourceHouse.AuthoredSession?
            RetainedAuthoredSession
        { get; init; }
        public bool? PortablePdbAvailable { get; init; }
    }

    internal sealed record MemberBestAvailableInspection(
        PdbMemberSourceInspection Authored,
        AssemblyPdbSourceProvenance? Provenance,
        SourceHouseBestAvailableOutcome? HouseOutcome,
        AssemblyContextLibraryAdapterResult.Terminal?
            LibraryFailure);

    internal sealed record TypeBestAvailableInspection(
        PdbTypeSourceInspection Authored,
        AssemblyPdbSourceProvenance? Provenance,
        SourceHouseBestAvailableOutcome? HouseOutcome,
        AssemblyContextLibraryAdapterResult.Terminal?
            LibraryFailure);
}
