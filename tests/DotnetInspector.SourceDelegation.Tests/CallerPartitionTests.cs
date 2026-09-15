namespace DotnetInspector.SourceDelegation.Tests;

// The caller-side adoption gate, exercised by the harness caller: candidate
// construction binds the complete ordered member sequence, every row-handoff
// member delegates one contiguous reference-order prefix and retains the exact
// disjoint suffix, and the delegated result plus that residual reproduces the
// reference computation.
public sealed class CallerPartitionTests
{
    private static readonly ToyMember Packages = ToyDelegation.Member("packages");
    private static readonly ToyMember Versions = ToyDelegation.Member("versions");

    private static readonly Dictionary<ToyMember, IReadOnlyList<ToyOperation>> Plans = new()
    {
        [Packages] = [ToyOperation.Exclude("b"), ToyOperation.TakeFirst(2), ToyOperation.Exclude("d")],
        [Versions] = [ToyOperation.Exclude("x"), ToyOperation.TakeFirst(1)],
    };

    private static readonly Dictionary<ToyMember, IReadOnlyList<ToyRow>> Inventory = new()
    {
        [Packages] = ToyDelegation.Rows("a", "b", "c", "d"),
        [Versions] = ToyDelegation.Rows("x", "y", "z"),
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SourceDelegationPartitionMatchesReference(int split)
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages, Versions);
        IReadOnlyList<ToyOperation> Prefix(ToyMember member) => [.. Plans[member].Take(split)];
        IReadOnlyList<ToyOperation> Residual(ToyMember member) => [.. Plans[member].Skip(split)];

        var candidate = ToyDelegation.RowHandoff(group, requirement, prefix: Prefix);

        // The candidate binds the complete ordered member sequence once.
        Assert.Equal(group.Members, candidate.Prefixes.Select(prefix => prefix.Member));

        // Prefix and residual are disjoint, contiguous, and together cover the
        // complete plan in reference order.
        foreach (ToyMember member in group.Members)
        {
            Assert.Equal(
                Plans[member],
                [.. candidate.PrefixFor(member).Operations, .. Residual(member)]);
            Assert.Equal(split, candidate.PrefixFor(member).Operations.Count);
            Assert.Equal(split == 0, candidate.PrefixFor(member).IsEmpty);
        }

        Assert.Equal(split == 0, candidate.IsAcquisitionOnly);

        // The source executes the delegated prefix exactly.
        int acquisitions = 0;
        IReadOnlyList<ToyRow> Acquire(ToyMember member)
        {
            acquisitions++;
            return Inventory[member];
        }

        var source = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                member => Offer.Rows(
                    ToyDelegation.Execute(Acquire(member), accepted.PrefixFor(member).Operations),
                    ToyDisposition.Acquired,
                    Evidence.Exhaustion())));

        var outcome = await ToyDelegation.RunAsync(source, candidate);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);
        Dictionary<ToyMember, IReadOnlyList<ToyRow>> delegated =
            new ToyCaller().AdmitToResidual(handoff, Residual);

        // Delegated prefix plus retained residual equals the reference result.
        foreach (ToyMember member in group.Members)
        {
            Assert.Equal(
                ToyDelegation.Execute(Inventory[member], Plans[member]).Select(row => row.Id),
                delegated[member].Select(row => row.Id));
        }

        Assert.Equal(2, acquisitions);
    }
}
