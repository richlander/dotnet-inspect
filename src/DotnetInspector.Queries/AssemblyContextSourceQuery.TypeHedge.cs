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
    internal static async Task<AssemblyTypeSourceEntry>
        InspectTypeWithLatencyHedgeAsync(
            AssemblyContextGroup group,
            AssemblyContextSubject subject,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            ResolvedAssemblyReference retained,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            TypeSourceLatencyHedge latencyHedge,
            CancellationToken cancellationToken)
    {
        using var hedgeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        Task<PreparedTypePdb> preparation =
            PrepareTypePdbAsync(
                participant,
                retained,
                context,
                bindingPolicyVersion,
                hedgeCancellation.Token);
        Task<TypePdbInspection>? authored = null;
        Exception? primaryFailure = null;
        try
        {
            PreparedTypePdb? prepared = null;
            Task initialWindow = Task.Delay(
                latencyHedge.PortablePdbPreferenceWindow,
                latencyHedge.TimeProvider,
                hedgeCancellation.Token);
            if (await Task.WhenAny(
                    preparation,
                    initialWindow)
                    .ConfigureAwait(false)
                == preparation)
            {
                prepared =
                    await preparation.ConfigureAwait(false);
                authored = StartAuthoredInspection(
                    prepared,
                    group,
                    participant,
                    request,
                    context,
                    retained,
                    bindingPolicyVersion,
                    hedgeCancellation.Token);
                if (authored is not null
                    && !authored.IsCompleted)
                {
                    // Let authored I/O start before synchronous Wasm decompilation.
                    await Task.Yield();
                }
                if (authored?.IsCompleted == true)
                {
                    TypePdbInspection immediate =
                        await authored.ConfigureAwait(false);
                    if (immediate.Inspection.IsComplete)
                    {
                        return PdbSourceEntry(
                            subject,
                            request,
                            immediate,
                            new(
                                PdbReadyBeforeDecompilation:
                                    prepared.IsAvailable,
                                DecompilationStarted: false,
                                DecompilationUsedPdb: false,
                                TypeSourceLatencyHedgeSelection
                                    .AuthoredBeforeDecompilation));
                    }
                }
            }
            else
            {
                await initialWindow.ConfigureAwait(false);
            }

            bool portablePdbReadyBeforeDecompilation =
                prepared?.IsAvailable == true;
            DecompilationOperationResult decompilation =
                await ExecuteDecompilationOperationAsync(
                        group,
                        participant,
                        new SourceHouseTarget.TypeTarget(
                            request.Type),
                        request.PrinterOptions,
                        context,
                        context.TypeDecompilationLimits,
                        prepared?.PortablePdb,
                        bindingPolicyVersion,
                        "type-decompilation",
                        cancellationToken)
                    .ConfigureAwait(false);

            bool decompilationAvailable =
                decompilation
                    is DecompilationOperationResult.Settled
                {
                    Attempt.IsAvailable: true,
                    Attempt.Text: not null,
                };
            TypePdbInspection pdb;
            if (authored is not null)
            {
                pdb = await SettleAuthoredAsync(
                        authored,
                        decompilationAvailable,
                        request,
                        latencyHedge,
                        hedgeCancellation.Token)
                    .ConfigureAwait(false);
            }
            else if (prepared is not null)
            {
                pdb = PreparedFailureInspection(
                    prepared,
                    request);
            }
            else
            {
                Task preferenceWindow = Task.Delay(
                    latencyHedge.AuthoredSourcePreferenceWindow,
                    latencyHedge.TimeProvider,
                    hedgeCancellation.Token);
                if (!decompilationAvailable
                    || await Task.WhenAny(
                            preparation,
                            preferenceWindow)
                            .ConfigureAwait(false)
                        == preparation)
                {
                    prepared =
                        await preparation.ConfigureAwait(false);
                    authored = StartAuthoredInspection(
                        prepared,
                        group,
                        participant,
                        request,
                        context,
                        retained,
                        bindingPolicyVersion,
                        hedgeCancellation.Token);
                    pdb = authored is null
                        ? PreparedFailureInspection(
                            prepared,
                            request)
                        : await SettleAuthoredAsync(
                                authored,
                                decompilationAvailable,
                                request,
                                latencyHedge,
                                hedgeCancellation.Token,
                                preferenceWindow)
                            .ConfigureAwait(false);
                }
                else
                {
                    await preferenceWindow.ConfigureAwait(false);
                    pdb = PreferenceWindowElapsed(
                        request);
                }
            }

            if (pdb.Inspection.IsComplete)
            {
                return PdbSourceEntry(
                    subject,
                    request,
                    pdb,
                    HedgeEvidence(
                        portablePdbReadyBeforeDecompilation,
                        decompilation,
                        pdb.LibraryFailure,
                        TypeSourceLatencyHedgeSelection
                            .AuthoredAfterDecompilation));
            }

            return DecompiledSourceEntry(
                subject,
                request,
                pdb,
                decompilation,
                HedgeEvidence(
                    portablePdbReadyBeforeDecompilation,
                    decompilation,
                    pdb.LibraryFailure,
                    pdb.Inspection.Outcome
                        == PdbTypeSourceOutcome
                            .AuthoredSourcePreferenceWindowElapsed
                        ? TypeSourceLatencyHedgeSelection
                            .DecompiledAfterPreferenceWindow
                        : decompilationAvailable
                            ? TypeSourceLatencyHedgeSelection
                                .DecompiledAfterAuthoredUnavailable
                            : TypeSourceLatencyHedgeSelection
                                .Unavailable));
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            hedgeCancellation.Cancel();
            List<Exception> failures = [];
            await SettleHedgeTaskAsync(
                    preparation,
                    hedgeCancellation.Token,
                    failures)
                .ConfigureAwait(false);
            if (authored is not null)
            {
                await SettleHedgeTaskAsync(
                        authored,
                        hedgeCancellation.Token,
                        failures)
                    .ConfigureAwait(false);
            }

            if (primaryFailure is not null)
            {
                ArtifactSetSession.AttachCleanupFailures(
                    primaryFailure,
                    failures);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    participant,
                    bindingPolicyVersion);
                ThrowCleanupFailures(failures);
            }
        }
    }

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

    static async Task<TypePdbInspection> SettleAuthoredAsync(
        Task<TypePdbInspection> authored,
        bool decompilationAvailable,
        AssemblyTypeSourceRequest request,
        TypeSourceLatencyHedge latencyHedge,
        CancellationToken cancellationToken,
        Task? preferenceWindow = null)
    {
        if (!decompilationAvailable)
        {
            return await authored.ConfigureAwait(false);
        }

        preferenceWindow ??= Task.Delay(
            latencyHedge.AuthoredSourcePreferenceWindow,
            latencyHedge.TimeProvider,
            cancellationToken);
        if (await Task.WhenAny(
                authored,
                preferenceWindow)
                .ConfigureAwait(false)
            == authored)
        {
            return await authored.ConfigureAwait(false);
        }

        await preferenceWindow.ConfigureAwait(false);
        return PreferenceWindowElapsed(request);
    }

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

    static TypePdbInspection PreferenceWindowElapsed(
        AssemblyTypeSourceRequest request) =>
        new(
            new PdbTypeSourceInspection(
                new FindingInspection<string>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    $"Authored source for {request.Type.ToMetadataFullName()} "
                    + "did not settle within the configured latency preference windows."),
                Text: null,
                Mapping: null,
                Document: null,
                ChecksumVerification: null)
            {
                Outcome =
                    PdbTypeSourceOutcome
                        .AuthoredSourcePreferenceWindowElapsed,
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
                ImmutableArray<byte> image =
                    source.Context.GetPortablePdbImage()
                    ?? throw new InvalidOperationException(
                        "An external Portable PDB source requires retained PDB content.");
                portablePdb = new(
                    image,
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
