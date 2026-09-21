using System.Collections.Immutable;

using DotnetInspector.Libraries;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using Inspector.Artifacts.Workspaces;
using Inspector.Findings;

namespace DotnetInspector.Queries;

public static partial class AssemblyContextSourceQuery
{
    static Task<TypePdbInspection>? StartAuthoredInspection(
        PreparedTypePdb prepared,
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken) =>
        prepared.IsAvailable
            ? InspectTypePdbAsync(
                group,
                participant,
                request,
                context,
                retained,
                bindingPolicyVersion,
                context.TypeSourceLimits,
                context.TypeSourceTimeout,
                cancellationToken,
                retainLibrary: false)
            : null;

    static AssemblyTypeSourceEntry PdbSourceEntry(
        AssemblyContextSubject subject,
        AssemblyTypeSourceRequest request,
        TypePdbInspection pdb,
        TypeSourceLatencyHedgeEvidence evidence)
    {
        if (pdb.Inspection.Text is not { } text
            || pdb.Provenance is not { } provenance)
        {
            throw new InvalidOperationException(
                "Completed authored source requires text and provenance.");
        }

        return new AssemblyTypeSourceEntry.Available(
            subject,
            request,
            new AssemblyTypeSource.Pdb(
                text,
                pdb.Inspection,
                provenance))
        {
            HouseOutcome = pdb.HouseOutcome,
            LibraryFailure = pdb.LibraryFailure,
            LatencyHedgeEvidence = evidence,
        };
    }

    static AssemblyTypeSourceEntry DecompiledSourceEntry(
        AssemblyContextSubject subject,
        AssemblyTypeSourceRequest request,
        TypePdbInspection pdb,
        DecompilationOperationResult decompilation,
        TypeSourceLatencyHedgeEvidence evidence)
    {
        if (decompilation
            is DecompilationOperationResult.LibraryUnavailable
                libraryUnavailable)
        {
            return new AssemblyTypeSourceEntry.Unavailable(
                subject,
                request,
                LibraryAdmissionUnavailable(
                    libraryUnavailable.Terminal),
                pdb.Inspection)
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure =
                    libraryUnavailable.Terminal,
                LatencyHedgeEvidence = evidence,
            };
        }

        var settled =
            (DecompilationOperationResult.Settled)
                decompilation;
        if (settled.Attempt.IsAvailable
            && settled.Attempt.Text is { } text)
        {
            return new AssemblyTypeSourceEntry.Available(
                subject,
                request,
                new AssemblyTypeSource.Decompiled(
                    text,
                    settled.Attempt,
                    pdb.Inspection))
            {
                HouseOutcome = pdb.HouseOutcome,
                DecompilationHouseOutcome =
                    settled.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
                LatencyHedgeEvidence = evidence,
            };
        }

        return new AssemblyTypeSourceEntry.Unavailable(
            subject,
            request,
            BothUnavailable(),
            pdb.Inspection,
            settled.Attempt)
        {
            HouseOutcome = pdb.HouseOutcome,
            DecompilationHouseOutcome =
                settled.HouseOutcome,
            LibraryFailure = pdb.LibraryFailure,
            LatencyHedgeEvidence = evidence,
        };
    }

    static TypeSourceLatencyHedgeEvidence HedgeEvidence(
        bool portablePdbReadyBeforeDecompilation,
        DecompilationOperationResult decompilation,
        AssemblyContextLibraryAdapterResult.Terminal?
            authoredLibraryFailure,
        TypeSourceLatencyHedgeSelection selection)
    {
        bool usedPdb =
            decompilation
                is DecompilationOperationResult.Settled
            {
                HouseOutcome.PdbContribution.Kind:
                        not SourceHousePdbContributionKind
                            .Unavailable,
            };
        return new(
            PdbReadyBeforeDecompilation:
                portablePdbReadyBeforeDecompilation,
            DecompilationStarted: true,
            DecompilationUsedPdb:
                usedPdb,
            selection)
        {
            AuthoredLibraryFailure =
                authoredLibraryFailure,
            DecompilationLibraryFailure =
                decompilation
                    is DecompilationOperationResult
                        .LibraryUnavailable unavailable
                    ? unavailable.Terminal
                    : null,
        };
    }

    static TypePdbInspection PreparedFailureInspection(
        PreparedTypePdb prepared,
        AssemblyTypeSourceRequest request)
    {
        if (prepared.IsAvailable)
        {
            throw new InvalidOperationException(
                "An available prepared PDB cannot project unavailable evidence.");
        }

        PdbTypeSourceInspection inspection =
            prepared.Failure is { } failure
                ? PdbSourceHouse.TypePdbAcquisitionFailed(
                    new FindingSubject(
                        "type",
                        request.Type.ToMetadataFullName()),
                    failure)
                : new(
                    new FindingInspection<string>.Absent(
                        FindingInspectionAbsenceKind.NoApplicableInput,
                        "A matching portable PDB is unavailable."),
                    Text: null,
                    Mapping: null,
                    Document: null,
                    ChecksumVerification: null)
                {
                    Outcome =
                        PdbTypeSourceOutcome
                            .PortablePdbUnavailable,
                };
        return new(
            inspection,
            Provenance: null);
    }

    static TypePdbInspection PdbPreferenceWindowElapsed(
        AssemblyTypeSourceRequest request) =>
        new(
            new PdbTypeSourceInspection(
                new FindingInspection<string>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    $"The Portable PDB for {request.Type.ToMetadataFullName()} "
                    + "did not settle within the configured preference window."),
                Text: null,
                Mapping: null,
                Document: null,
                ChecksumVerification: null)
            {
                Outcome =
                    PdbTypeSourceOutcome
                        .PortablePdbPreferenceWindowElapsed,
            },
            Provenance: null);

    static async Task<PreparedTypePdb> PrepareTypePdbAsync(
        AssemblyContextParticipant participant,
        ResolvedAssemblyReference retained,
        AssemblyContextSourceQueryContext context,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken)
    {
        SourceLinkOpenResult opened =
            await OpenSourceLinkAsync(
                    retained,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        if (opened.Source is not { } source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
            return new(
                IsAvailable: false,
                PortablePdb: null,
                opened.Failure);
        }

        Exception? primaryFailure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureBindingPolicyVersion(
                participant,
                bindingPolicyVersion);
            AssemblyContextLibraryPortablePdb? portablePdb =
                null;
            if (!source.Context.HasEmbeddedPdb)
            {
                ImmutableArray<byte>? image =
                    source.Context.GetPortablePdbImage();
                if (image is null)
                {
                    return new(
                        IsAvailable: false,
                        PortablePdb: null,
                        Failure: opened.Failure);
                }
                portablePdb = new(
                    image.Value,
                    new AssemblySourcePdbProvenance(
                        participant.Assembly.Registration,
                        source.Context.PdbId,
                        source.Context.PdbLocation,
                        source.Context.PortablePdbPath,
                        source.Context.SymbolServer));
            }

            return new(
                IsAvailable: true,
                portablePdb,
                Failure: null);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            Exception? cleanup =
                source.DisposeWithFailure();
            if (primaryFailure is not null
                && cleanup is not null)
            {
                ArtifactSetSession.AttachCleanupFailures(
                    primaryFailure,
                    [cleanup]);
            }
            else if (primaryFailure is null)
            {
                ValidateAfterSourceDisposal(
                    participant,
                    bindingPolicyVersion,
                    cancellationToken,
                    cleanup);
            }
        }
    }

    static async Task SettleHedgeTaskAsync(
        Task task,
        CancellationToken hedgeCancellation,
        List<Exception> failures)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (hedgeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    sealed record PreparedTypePdb(
        bool IsAvailable,
        AssemblyContextLibraryPortablePdb? PortablePdb,
        Exception? Failure);
}
