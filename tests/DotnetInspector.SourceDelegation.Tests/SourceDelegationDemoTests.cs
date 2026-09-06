namespace DotnetInspector.SourceDelegation.Tests;

// A runnable walkthrough of the whole protocol on one Gallery-shaped scenario:
// two ordered members, a source that can only acquire cheaply for one of them,
// a decline that leaves the reference path available, and one accepted
// acquisition-only handoff whose usable rows flow into the caller's residual
// while the capped member's Count question stays unanswered.
public sealed class SourceDelegationDemoTests
{
    private static readonly ToyMember Aspire = ToyDelegation.Member("prefix:Aspire.");
    private static readonly ToyMember Toolkit = ToyDelegation.Member("prefix:CommunityToolkit.");

    [Fact]
    public async Task AcquisitionOnlyGalleryDiscoveryWalkthrough()
    {
        var log = new List<string>();
        var requirement = new ToyRequirement("gallery.discovery-rows");
        ToyGroup group = ToyDelegation.Group(Aspire, Toolkit);

        // The caller's complete plan per member, and the partition it proved.
        IReadOnlyList<ToyOperation> Residual(ToyMember member) => [ToyOperation.Exclude("Aspire.Internal")];

        // Preference order: a cheap exact-Count candidate first, then an
        // acquisition-only row handoff.
        var countFirst = ToyDelegation.CountCandidate(group, requirement, capability: ToyDelegation.Unsupported);
        var acquisitionOnly = ToyDelegation.RowHandoff(group, requirement);

        var gallery = new ToySource(
            candidate => candidate.Capability == ToyDelegation.RowPrefix,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                member => member == Aspire
                    // A complete catalog page: the feed proved exhaustion.
                    ? Offer.Rows(
                        ToyDelegation.Rows("Aspire.Hosting", "Aspire.Internal", "Aspire.Redis"),
                        ToyDisposition.Acquired,
                        Evidence.Exhaustion())
                    // A server-driven page boundary: an incomplete stop, even
                    // though the page looks full.
                    : Offer.Rows(
                        ToyDelegation.Rows("CommunityToolkit.Aspire.Hosting"),
                        ToyDisposition.ProviderCapped,
                        Evidence.Stop())));

        var outcome = await ToyDelegation.RunAsync(gallery, countFirst, acquisitionOnly);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);

        log.Add($"planned {gallery.Planned.Count} candidates in declaration order, executed {gallery.ExecuteCount}");
        foreach (ToyRowOutcome entry in handoff.Outcomes)
        {
            string shape = entry is ToyRowValues rows
                ? $"RowValues[{rows.Values.Count}]"
                : "Unavailable";
            log.Add($"  {entry.Member} -> {shape} ({entry.Disposition}, {entry.Evidence.Basis})");
        }

        var caller = new ToyCaller();
        Dictionary<ToyMember, IReadOnlyList<ToyRow>> admitted = caller.AdmitToResidual(handoff, Residual);
        foreach ((ToyMember member, IReadOnlyList<ToyRow> rows) in admitted)
            log.Add($"  residual({member}) -> {string.Join(", ", rows.Select(row => row.Id))}");

        // The same capped member cannot answer an exact Count.
        var countCandidate = ToyDelegation.CountCandidate(
            ToyDelegation.Group(Toolkit),
            requirement);
        var countSource = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                _ => Offer.Count(1, ToyDisposition.ProviderCapped, Evidence.Stop())));

        var countOutcome = await ToyDelegation.RunAsync(countSource, countCandidate);
        var notSatisfied = Assert.IsType<ToyNotSatisfied>(
            countOutcome.Result);
        log.Add($"  count({Toolkit}) -> NotSatisfied ({notSatisfied.Members[0].Evidence.Basis})");

        foreach (string line in log)
            Console.WriteLine(line);

        Assert.Equal(
            [
                "planned 2 candidates in declaration order, executed 1",
                "  prefix:Aspire. -> RowValues[3] (acquired, LogicalExhaustion)",
                "  prefix:CommunityToolkit. -> RowValues[1] (provider-capped, IncompleteStop)",
                "  residual(prefix:Aspire.) -> Aspire.Hosting, Aspire.Redis",
                "  residual(prefix:CommunityToolkit.) -> CommunityToolkit.Aspire.Hosting",
                "  count(prefix:CommunityToolkit.) -> NotSatisfied (IncompleteStop)",
            ],
            log);
    }
}
