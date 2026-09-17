namespace DotnetInspector.EcosystemLoading;

/// <summary>Executes one exact bound loader request at most once.</summary>
public static class EcosystemPopulationLoadOperation
{
    public static async ValueTask<EcosystemPopulationLoadOutcome> InvokeAsync<
        TInputs>(
        EcosystemPopulationLoadRequest<TInputs> request)
        where TInputs : class, IEcosystemPopulationLoadInputs
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.TryBeginInvocation())
        {
            throw new InvalidOperationException(
                "An Ecosystem population load request can be invoked only once.");
        }

        request.CancellationToken.ThrowIfCancellationRequested();
        EcosystemPopulationLoaderReply reply =
            await request.Binding.LoadAsync(request)
            ?? throw new InvalidOperationException(
                "The Ecosystem population loader returned no reply.");
        if (!ReferenceEquals(reply.Request, request.Identity))
        {
            await RetireReplyOwnersAsync(reply);
            throw new InvalidOperationException(
                "The Ecosystem population loader returned a reply for another request.");
        }

        EcosystemPopulationLoadSettlementKind settlement = reply switch
        {
            EcosystemPopulationLoaderReply.Completed =>
                EcosystemPopulationLoadSettlementKind.Completed,
            EcosystemPopulationLoaderReply.Unavailable =>
                EcosystemPopulationLoadSettlementKind.Unavailable,
            EcosystemPopulationLoaderReply.Incomplete =>
                EcosystemPopulationLoadSettlementKind.Incomplete,
            EcosystemPopulationLoaderReply.Rejected =>
                EcosystemPopulationLoadSettlementKind.Rejected,
            EcosystemPopulationLoaderReply.Failed =>
                EcosystemPopulationLoadSettlementKind.Failed,
            _ => throw new InvalidOperationException(
                "Unknown Ecosystem population loader reply."),
        };
        EcosystemPopulationCompletionWitness? completion =
            (reply as EcosystemPopulationLoaderReply.Completed)?.Completion;
        var receipt = new EcosystemPopulationLoadReceipt(
            request.Snapshot,
            settlement,
            completion,
            reply.Children,
            reply.Diagnostics);

        EcosystemPopulationOwnerBatch? owners =
            reply.Ownerships.Count == 0
                && reply is not EcosystemPopulationLoaderReply.Completed
                && reply is not EcosystemPopulationLoaderReply.Incomplete
            ? null
            : new EcosystemPopulationOwnerBatch(
                reply.Ownerships);

        if (request.CancellationToken.IsCancellationRequested)
        {
            if (owners is not null)
            {
                try
                {
                    await owners.DisposeAsync();
                }
                catch (Exception cleanupFailure)
                {
                    throw new OperationCanceledException(
                        "The Ecosystem population load was cancelled and returned owner cleanup failed.",
                        cleanupFailure,
                        request.CancellationToken);
                }
            }

            request.CancellationToken.ThrowIfCancellationRequested();
        }

        return reply switch
        {
            EcosystemPopulationLoaderReply.Completed =>
                new EcosystemPopulationLoadOutcome.Completed(
                    receipt,
                    owners!),
            EcosystemPopulationLoaderReply.Unavailable =>
                new EcosystemPopulationLoadOutcome.Unavailable(receipt),
            EcosystemPopulationLoaderReply.Incomplete =>
                new EcosystemPopulationLoadOutcome.Incomplete(
                    receipt,
                    owners!),
            EcosystemPopulationLoaderReply.Rejected =>
                new EcosystemPopulationLoadOutcome.Rejected(receipt),
            EcosystemPopulationLoaderReply.Failed =>
                new EcosystemPopulationLoadOutcome.Failed(receipt),
            _ => throw new InvalidOperationException(
                "Unknown Ecosystem population loader reply."),
        };
    }

    static async ValueTask RetireReplyOwnersAsync(
        EcosystemPopulationLoaderReply reply)
    {
        if (reply.Ownerships.Count == 0)
            return;

        var owners = new EcosystemPopulationOwnerBatch(reply.Ownerships);
        try
        {
            await owners.DisposeAsync();
        }
        catch (Exception cleanupFailure)
        {
            throw new InvalidOperationException(
                "A foreign Ecosystem population reply returned Library owners and their cleanup failed.",
                cleanupFailure);
        }
    }
}
