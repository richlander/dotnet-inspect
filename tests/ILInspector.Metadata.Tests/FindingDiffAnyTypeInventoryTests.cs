using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using Inspector.Findings;

using TypeSpec = ILInspector.Metadata.Tests.FindingDiffAnyIdentityTests.TypeSpec;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The Type pairing (<c>docs/design/finding-diff-any.md#type</c>): one paired
/// Type's rows are the Library level's census facts present on one side only
/// and every paired method's Body state, their fold is the Library level's Type
/// state, and lockstep over two IL cursors through the body reader yields the
/// same rows as the walk.
/// </summary>
public sealed class FindingDiffAnyTypeInventoryTests
{
    static readonly byte[] ReturnOne = [0x17, 0x2A];
    static readonly byte[] ReturnTwo = [0x18, 0x2A];

    [Fact]
    public void FixturePair_FoldsEqualTheLibraryLevel_AndBothReadersAgree()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(new MemoryStream(File.ReadAllBytes(pair.OldAssemblyPath()), writable: false));
        using var after = new PEReader(new MemoryStream(File.ReadAllBytes(pair.NewAssemblyPath()), writable: false));
        FindingDiffAnyResult library = FindingDiffAny.Compare(before, after, cancellationToken: TestContext.Current.CancellationToken);
        var byName = library.Types.ToDictionary(state => state.FullName, StringComparer.Ordinal);

        var walk = new FindingDiffAnyTypePairing(before, after);
        var lockstep = new FindingDiffAnyTypePairing(before, after, FindingDiffAnyBodyReaderTests.Lockstep(before, after));
        Assert.NotEmpty(walk.Types);
        Assert.Equal(walk.Types, lockstep.Types);
        Assert.True(walk.Types.Count < library.Types.Length, "the fixture pair has Types present on one side only, which are not paired");

        int changedTypes = 0;
        int bodyRows = 0;
        foreach (string name in walk.Types)
        {
            FindingDiffAnyTypeInventory w = walk.Inventory(name);
            FindingDiffAnyTypeInventory l = lockstep.Inventory(name);
            Assert.True(byName.TryGetValue(name, out FindingDiffAnyTypeState? state), name);
            Assert.Equal((state!.Api, state.Body), (w.Api, w.Body));
            Assert.Equal((w.Api, w.Body), (l.Api, l.Body));
            Assert.Equal(w.Facts, l.Facts);
            Assert.Equal(w.Bodies, l.Bodies);
            Assert.Equal(w.Api == FindingDiffAnyState.Changed, w.Facts.Any(fact => fact.Axis == FindingDiffAnyAxis.Api));
            if (w.Body == FindingDiffAnyState.Changed)
                changedTypes++;
            bodyRows += w.Bodies.Length;
        }
        Assert.True(changedTypes >= 10, $"{changedTypes} Types changed on Body");
        Assert.True(bodyRows > 0);
        Assert.Equal(walk.BodiesCompared, lockstep.BodiesCompared);
        Assert.Equal(0, lockstep.IlBytesCompared);

        walk.Release();
        Assert.Throws<InvalidOperationException>(() => walk.Inventory(walk.Types[0]));
    }

    [Theory]
    [MemberData(nameof(IlExistsAgreementTests.IdentityImages), MemberType = typeof(IlExistsAgreementTests))]
    public void IdentityImages_TheBodyRowIsTheDecision_AndBothReadersAgree(
        string type,
        byte[] beforeImage,
        byte[] afterImage,
        FindingExistsDecision expected)
    {
        using var before = new PEReader(new MemoryStream(beforeImage, writable: false));
        using var after = new PEReader(new MemoryStream(afterImage, writable: false));
        var walk = new FindingDiffAnyTypePairing(before, after);
        var lockstep = new FindingDiffAnyTypePairing(before, after, FindingDiffAnyBodyReaderTests.Lockstep(before, after));
        FindingDiffAnyTypeInventory w = walk.Inventory(type);
        FindingDiffAnyTypeInventory l = lockstep.Inventory(type);
        Assert.Equal(expected.ToString(), Assert.Single(w.Bodies).Body.ToString());
        Assert.Equal(expected.ToString(), w.Body.ToString());
        Assert.Equal(w.Bodies, l.Bodies);
        Assert.Equal(w.Facts, l.Facts);
        Assert.Equal((w.Api, w.Body), (l.Api, l.Body));
    }

    [Fact]
    public void AddedPublicMethod_IsAnApiFactOnTheAfterSideAndABodyDifference()
    {
        FindingDiffAnyTypeInventory inventory = Inventory(
            [new("N", "C", null, ReturnOne)],
            [new("N", "C", null, ReturnOne, Methods: ["M", "Added"])]);
        Assert.Equal(FindingDiffAnyState.Changed, inventory.Api);
        // A method with a body present on one side only is a Body difference,
        // public or not, without a Body fact or a body read.
        Assert.Equal(FindingDiffAnyState.Changed, inventory.Body);
        Assert.NotEmpty(inventory.Facts);
        Assert.All(inventory.Facts, fact =>
        {
            Assert.Equal(FindingDiffAnyAxis.Api, fact.Axis);
            Assert.Equal(FindingDiffAnySide.After, fact.Side);
        });
        Assert.Contains(inventory.Facts, fact => fact.Key.Contains("Added", StringComparison.Ordinal));
        Assert.Equal(FindingDiffAnyState.Unchanged, Assert.Single(inventory.Bodies).Body);
        Assert.True(inventory.ApiFactsAfter > inventory.ApiFactsBefore);
        Assert.Equal(inventory.BodyFactsBefore, inventory.BodyFactsAfter);
    }

    [Fact]
    public void RemovedMethodOnAHiddenType_IsABodyFactOnTheBeforeSide()
    {
        FindingDiffAnyTypeInventory inventory = Inventory(
            [new("N", "C", null, ReturnOne, Methods: ["M", "Removed"], Public: false)],
            [new("N", "C", null, ReturnOne, Public: false)]);
        Assert.Equal(FindingDiffAnyState.Unchanged, inventory.Api);
        Assert.Equal(FindingDiffAnyState.Changed, inventory.Body);
        Assert.NotEmpty(inventory.Facts);
        Assert.All(inventory.Facts, fact =>
        {
            Assert.Equal(FindingDiffAnyAxis.Body, fact.Axis);
            Assert.Equal(FindingDiffAnySide.Before, fact.Side);
        });
        Assert.Equal(FindingDiffAnyState.Unchanged, Assert.Single(inventory.Bodies).Body);
    }

    [Fact]
    public void ChangedBody_IsABodyRow_AndNoFact()
    {
        FindingDiffAnyTypeInventory inventory = Inventory(
            [new("N", "C", null, ReturnOne)],
            [new("N", "C", null, ReturnTwo)]);
        Assert.Equal(FindingDiffAnyState.Unchanged, inventory.Api);
        Assert.Equal(FindingDiffAnyState.Changed, inventory.Body);
        Assert.Empty(inventory.Facts);
        Assert.Equal(FindingDiffAnyState.Changed, Assert.Single(inventory.Bodies).Body);
    }

    [Fact]
    public void UndecodableBody_IsAnIndeterminateRow()
    {
        byte[] undefined = [0x24, 0x2A];
        FindingDiffAnyTypeInventory inventory = Inventory(
            [new("N", "C", null, undefined)],
            [new("N", "C", null, undefined)]);
        Assert.Equal(FindingDiffAnyState.Unchanged, inventory.Api);
        Assert.Equal(FindingDiffAnyState.Indeterminate, inventory.Body);
        Assert.Equal(FindingDiffAnyState.Indeterminate, Assert.Single(inventory.Bodies).Body);
    }

    [Fact]
    public void DifferenceBeforeAFault_IsChanged()
    {
        FindingDiffAnyTypeInventory inventory = Inventory(
            [new("N", "C", null, [0x17, 0x24, 0x2A])],
            [new("N", "C", null, [0x18, 0x24, 0x2A])]);
        Assert.Equal(FindingDiffAnyState.Changed, inventory.Body);
        Assert.Equal(FindingDiffAnyState.Changed, Assert.Single(inventory.Bodies).Body);
    }

    [Fact]
    public void TheFoldIsTheFirstBodyNotUnchanged_InTheBodyCensusOrder()
    {
        // Two paired methods: M changed and N undecodable, then the reverse.
        // Every row keeps its own state; the fold is where the Library level
        // stops, so Changed then Indeterminate folds to Changed and
        // Indeterminate then Changed folds to Indeterminate.
        byte[] undefined = [0x24, 0x2A];
        FindingDiffAnyTypeInventory changedFirst = Inventory(
            [new("N", "C", null, ReturnOne, Methods: ["M", "N"], MethodBodies: [ReturnOne, undefined])],
            [new("N", "C", null, ReturnOne, Methods: ["M", "N"], MethodBodies: [ReturnTwo, undefined])]);
        Assert.Equal(FindingDiffAnyState.Changed, changedFirst.Body);
        Assert.Equal(
            [FindingDiffAnyState.Changed, FindingDiffAnyState.Indeterminate],
            changedFirst.Bodies.Select(row => row.Body));

        FindingDiffAnyTypeInventory indeterminateFirst = Inventory(
            [new("N", "C", null, ReturnOne, Methods: ["M", "N"], MethodBodies: [undefined, ReturnOne])],
            [new("N", "C", null, ReturnOne, Methods: ["M", "N"], MethodBodies: [undefined, ReturnTwo])]);
        Assert.Equal(FindingDiffAnyState.Indeterminate, indeterminateFirst.Body);
        Assert.Equal(
            [FindingDiffAnyState.Indeterminate, FindingDiffAnyState.Changed],
            indeterminateFirst.Bodies.Select(row => row.Body));
    }

    [Fact]
    public void OneSidedTypeIsNotPaired_AndAnUnknownNameThrows()
    {
        using var before = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([new("N", "C", null, ReturnOne)]), writable: false));
        using var after = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image([new("N", "C", null, ReturnOne), new("N", "D", null, ReturnOne)]), writable: false));
        var pairing = new FindingDiffAnyTypePairing(before, after);
        Assert.Equal(["N.C"], pairing.Types);
        Assert.Throws<ArgumentException>(() => pairing.Inventory("N.D"));
        Assert.Throws<ArgumentException>(() => pairing.Inventory("N.E"));
    }

    static FindingDiffAnyTypeInventory Inventory(TypeSpec[] before, TypeSpec[] after)
    {
        using var beforeReader = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image(before), writable: false));
        using var afterReader = new PEReader(new MemoryStream(FindingDiffAnyIdentityTests.Image(after), writable: false));
        var walk = new FindingDiffAnyTypePairing(beforeReader, afterReader);
        var lockstep = new FindingDiffAnyTypePairing(beforeReader, afterReader, FindingDiffAnyBodyReaderTests.Lockstep(beforeReader, afterReader));
        FindingDiffAnyTypeInventory w = walk.Inventory("N.C");
        FindingDiffAnyTypeInventory l = lockstep.Inventory("N.C");
        Assert.Equal((w.Api, w.Body), (l.Api, l.Body));
        Assert.Equal(w.Facts, l.Facts);
        Assert.Equal(w.Bodies, l.Bodies);
        FindingDiffAnyTypeState library = Assert.Single(FindingDiffAny.Compare(beforeReader, afterReader).Types, state => state.FullName == "N.C");
        Assert.Equal((library.Api, library.Body), (w.Api, w.Body));
        return w;
    }
}
