using DotnetInspector.Libraries;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries;

public static partial class AssemblyContextSourceQuery
{
    internal static async Task<AssemblyTypeSourceEntry>
        InspectTypeWithPdbLatencyHedgeAsync(
            AssemblyContextGroup group,
            AssemblyContextSubject subject,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            ResolvedAssemblyReference retained,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            TypeSourcePdbLatencyHedge latencyHedge,
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
        Exception? primaryFailure = null;
        try
        {
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
                PreparedTypePdb prepared =
                    await preparation.ConfigureAwait(false);
                TypePdbInspection pdb =
                    await SettlePreparedPdbPathAsync(
                            prepared,
                            group,
                            participant,
                            request,
                            context,
                            retained,
                            bindingPolicyVersion,
                            hedgeCancellation.Token)
                        .ConfigureAwait(false);
                if (pdb.Inspection.IsComplete)
                {
                    return PdbSourceEntry(
                        subject,
                        request,
                        pdb,
                        new(
                            PdbReadyBeforeDecompilation:
                                prepared.IsAvailable,
                            DecompilationStarted: false,
                            DecompilationUsedPdb: false,
                            TypeSourceLatencyHedgeSelection
                                .AuthoredBeforeDecompilation));
                }

                DecompilationOperationResult decompilation =
                    await ExecuteDecompilationOperationAsync(
                            group,
                            participant,
                            new SourceHouseTarget.TypeTarget(
                                request.Type),
                            request.PrinterOptions,
                            context,
                            context.TypeDecompilationLimits,
                            prepared.PortablePdb,
                            bindingPolicyVersion,
                            "type-decompilation",
                            cancellationToken)
                        .ConfigureAwait(false);
                TypeSourceLatencyHedgeSelection selection =
                    prepared.IsAvailable
                        ? TypeSourceLatencyHedgeSelection
                            .DecompiledAfterAuthoredUnavailable
                        : TypeSourceLatencyHedgeSelection
                            .DecompiledAfterPdbUnavailable;
                return DecompiledSourceEntry(
                    subject,
                    request,
                    pdb,
                    decompilation,
                    HedgeEvidence(
                        prepared.IsAvailable,
                        decompilation,
                        pdb.LibraryFailure,
                        IsDecompilationAvailable(decompilation)
                            ? selection
                            : TypeSourceLatencyHedgeSelection
                                .Unavailable));
            }

            await initialWindow.ConfigureAwait(false);
            DecompilationOperationResult fallback =
                await ExecuteDecompilationOperationAsync(
                        group,
                        participant,
                        new SourceHouseTarget.TypeTarget(
                            request.Type),
                        request.PrinterOptions,
                        context,
                        context.TypeDecompilationLimits,
                        portablePdb: null,
                        bindingPolicyVersion,
                        "type-decompilation",
                        cancellationToken)
                    .ConfigureAwait(false);
            if (IsDecompilationAvailable(fallback))
            {
                TypePdbInspection elapsed =
                    PdbPreferenceWindowElapsed(request);
                return DecompiledSourceEntry(
                    subject,
                    request,
                    elapsed,
                    fallback,
                    HedgeEvidence(
                        portablePdbReadyBeforeDecompilation:
                            false,
                        fallback,
                        elapsed.LibraryFailure,
                        TypeSourceLatencyHedgeSelection
                            .DecompiledAfterPdbPreferenceWindow));
            }

            PreparedTypePdb latePrepared =
                await preparation.ConfigureAwait(false);
            TypePdbInspection latePdb =
                await SettlePreparedPdbPathAsync(
                        latePrepared,
                        group,
                        participant,
                        request,
                        context,
                        retained,
                        bindingPolicyVersion,
                        hedgeCancellation.Token)
                    .ConfigureAwait(false);
            if (latePdb.Inspection.IsComplete)
            {
                return PdbSourceEntry(
                    subject,
                    request,
                    latePdb,
                    HedgeEvidence(
                        portablePdbReadyBeforeDecompilation:
                            false,
                        fallback,
                        latePdb.LibraryFailure,
                        TypeSourceLatencyHedgeSelection
                            .AuthoredAfterDecompilation));
            }

            return DecompiledSourceEntry(
                subject,
                request,
                latePdb,
                fallback,
                HedgeEvidence(
                    portablePdbReadyBeforeDecompilation:
                        false,
                    fallback,
                    latePdb.LibraryFailure,
                    TypeSourceLatencyHedgeSelection
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

    static async Task<TypePdbInspection> SettlePreparedPdbPathAsync(
        PreparedTypePdb prepared,
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion bindingPolicyVersion,
        CancellationToken cancellationToken)
    {
        Task<TypePdbInspection>? authored =
            StartAuthoredInspection(
                prepared,
                group,
                participant,
                request,
                context,
                retained,
                bindingPolicyVersion,
                cancellationToken);
        return authored is null
            ? PreparedFailureInspection(
                prepared,
                request)
            : await authored.ConfigureAwait(false);
    }

    static bool IsDecompilationAvailable(
        DecompilationOperationResult decompilation) =>
        decompilation
            is DecompilationOperationResult.Settled
        {
            Attempt.IsAvailable: true,
            Attempt.Text: not null,
        };
}
