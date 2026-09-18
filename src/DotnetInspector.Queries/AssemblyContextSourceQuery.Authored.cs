using System.Collections.Immutable;
using DotnetInspector.Libraries;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Findings;
using Inspector.Text;
using AuthoredSourceHouse = DotnetInspector.SourceHouse.SourceHouse;

namespace DotnetInspector.Queries;

public sealed record AssemblyMemberSourcePdbProvenance(
    AssemblyAcquisitionRegistration SourceRegistration,
    CodeViewInfo? Identity,
    string? Location,
    string? Path,
    string? SymbolServer) : IArtifactProvenance;

public static partial class AssemblyContextSourceQuery
{
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
        bool retainSymbols = false)
    {
        var findingSubject = new FindingSubject(
            "member", request.Member.Format(MemberAnchorFormat.Qualified));
        var opened = await OpenSourceLinkAsync(
            retained, context, cancellationToken).ConfigureAwait(false);
        if (opened.Source is not { } source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(participant, version);
            return new(
                PdbSourceHouse.MemberPdbAcquisitionFailed(findingSubject, opened.Failure!),
                null, null);
        }

        AssemblyContextLibraryPortablePdb? companion = null;
        ImmutableArray<byte>? pdbImage = null;
        AssemblyPdbSourceProvenance provenance;
        Exception? primaryFailure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(participant, version);
            provenance = new(source.RepositoryUrl, source.CommitHash);
            if (retainSymbols || !source.Context.HasEmbeddedPdb)
                pdbImage = source.Context.GetPortablePdbImage();
            if (!source.Context.HasEmbeddedPdb
                && pdbImage is { } image)
            {
                companion = new(image, new AssemblyMemberSourcePdbProvenance(
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

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        var plan = new SourceHouseOperationPlan(
            SourceHouseOperationPlanIdentity.Create("member-source"),
            SourceHousePolicyGeneration.Create("member-source-v1"),
            limits,
            DateTimeOffset.UtcNow.Add(timeout),
            SourceCapabilities(context));
        AssemblyContextLibraryAdapterResult admission =
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                new(
                    Math.Max(1, Math.Max(limits.MaximumAssemblyBytes, limits.MaximumPortablePdbBytes)),
                    Math.Max(1, Math.Min(int.MaxValue,
                        (long)limits.MaximumAssemblyBytes + limits.MaximumPortablePdbBytes))),
                companion, cancellationToken).ConfigureAwait(false);
        if (admission is AssemblyContextLibraryAdapterResult.Terminal terminal)
        {
            ThrowCleanupFailures(terminal.CleanupFailures);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(participant, version);
            return new(
                UnsuccessfulInspection(
                    findingSubject, terminal is AssemblyContextLibraryAdapterResult.Incomplete
                        ? PdbMemberSourceOutcome.SourceLimitExceeded
                        : PdbMemberSourceOutcome.InspectionFailed,
                    AdmissionDetail(terminal), failed: true),
                null, retainSymbols ? pdbImage : null)
            {
                LibraryFailure = terminal,
            };
        }
        if (admission is not AssemblyContextLibraryAdapterResult.Completed completed)
            throw new InvalidOperationException("Unknown Library admission result.");

        SourceHouseOutcome outcome;
        primaryFailure = null;
        try
        {
            EnsureBindingPolicyVersion(participant, version);
            var houseRequest = new SourceHouseAuthoredRequest(
                SourceHouseRequestIdentity.Create("member-source"),
                completed.Reference,
                completed.Reference.ImplementationAssembly!,
                new SourceHouseTarget.MemberTarget(request.Type, request.Member, request.MetadataToken),
                plan);
            if (completed.Owner.IssueOperationLease(completed.Reference)
                is not LibraryOperationLeaseIssueOutcome.Issued issued)
            {
                throw new InvalidOperationException("The admitted Library could not issue its operation lease.");
            }
            outcome = await AuthoredSourceHouse.ExecuteAuthoredAsync(
                houseRequest, issued.Lease, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            await RetireAuthoredLibraryAsync(completed, primaryFailure).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(participant, version);
        PdbMemberSourceInspection inspection = ProjectAuthored(outcome, findingSubject);
        return new(inspection, inspection.IsComplete ? provenance : null,
            retainSymbols ? pdbImage : null)
        {
            HouseOutcome = outcome,
        };
    }

    static async ValueTask RetireAuthoredLibraryAsync(
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
                "Member source Library/Artifact retirement failed.", new AggregateException(failures));
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

    static PdbMemberSourceInspection ProjectAuthored(
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
        return UnsuccessfulInspection(subject, kind, detail, failed, mapping, verification);
    }

    static PdbMemberSourceInspection UnsuccessfulInspection(
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

    static IReadOnlyList<ISourceHouseSourceCapability> SourceCapabilities(
        AssemblyContextSourceQueryContext context)
    {
        List<ISourceHouseSourceCapability> capabilities = [];
        if (context.AllowLocalSourceReads)
            capabilities.Add(new LocalSourceCapability());
        if (context.RepositoryPaths is { Count: > 0 } paths)
            capabilities.Add(new RepositorySourceCapability(paths));
        capabilities.Add(new RemoteSourceCapability(context.SourceFetch));
        return capabilities;
    }

    sealed class LocalSourceCapability : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create("local-source");
        public SourceHouseCapabilityCategory Category => SourceHouseCapabilityCategory.Local;
        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate, int maximumBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? bytes = PdbSourceHouse.TryReadVerifiedLocalSource(candidate.Document);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CapturedSource(bytes, maximumBytes, "LocalSourceUnavailable"));
        }
    }

    sealed class RepositorySourceCapability(IReadOnlyList<string> paths) : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create("repository-source");
        public SourceHouseCapabilityCategory Category => SourceHouseCapabilityCategory.Repository;
        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate, int maximumBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? bytes = LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(candidate.Document, paths);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CapturedSource(bytes, maximumBytes, "RepositorySourceUnavailable"));
        }
    }

    sealed class RemoteSourceCapability(SourceFetch fetcher) : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create("remote-source");
        public SourceHouseCapabilityCategory Category => SourceHouseCapabilityCategory.Remote;
        public async ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate, int maximumBytes, CancellationToken cancellationToken)
        {
            if (candidate.Document.ResolvedUrl is not { Length: > 0 } url)
                return new SourceHouseCapabilityOutcome.Unavailable(new("SourceUrlUnavailable"));
            SourceChecksumVerification checksum = SourceLinkService.VerifyChecksum(candidate.Document, []);
            if (checksum is SourceChecksumVerification.Unavailable or SourceChecksumVerification.Unsupported)
                return new SourceHouseCapabilityOutcome.Rejected(new(checksum.ToString()));

            FetchSourceResult result = await fetcher.FetchVerifiedSourceBytesAsync(
                url,
                bytes => SourceLinkService.VerifyChecksum(candidate.Document, bytes.Span)
                    is SourceChecksumVerification.Exact or SourceChecksumVerification.LineEndingNormalized,
                cancellationToken).ConfigureAwait(false);
            return result switch
            {
                FetchSourceResult.Success success =>
                    CapturedSource(success.Content, maximumBytes, "SourceUnavailable"),
                FetchSourceResult.Failure { Error: SourceError.NotFound } =>
                    new SourceHouseCapabilityOutcome.Unavailable(new("SourceNotFound")),
                FetchSourceResult.Failure { Error: SourceError.ValidationFailed } =>
                    new SourceHouseCapabilityOutcome.Failed(new("ChecksumMismatch")),
                FetchSourceResult.Failure failure =>
                    new SourceHouseCapabilityOutcome.Failed(new(failure.Error.ToString())),
                _ => throw new InvalidOperationException("Unknown source fetch result."),
            };
        }
    }

    static SourceHouseCapabilityOutcome CapturedSource(byte[]? bytes, int maximumBytes, string unavailable) =>
        bytes is null
            ? new SourceHouseCapabilityOutcome.Unavailable(new(unavailable))
            : bytes.Length > maximumBytes
                ? new SourceHouseCapabilityOutcome.Incomplete(new("SourceBytesExceeded"))
                : new SourceHouseCapabilityOutcome.Available(bytes);
}
