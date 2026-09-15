using System.Collections.Immutable;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

public class UnsafetyFindingDiffTests
{
    [Fact]
    public void Compare_SurfacesAddedUnsafeOperation()
    {
        var oldIndex = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.OldAssemblyPath());
        var newIndex = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.NewAssemblyPath());

        var comparison = Compare(oldIndex, newIndex);

        Assert.Contains(comparison.Changes, change =>
            change.Kind == ResearchChangeKind.Added
            && change.Signal == "stackalloc"
            && change.Subject.Display.Contains("AddsUnsafe", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_SelfDiffHasNoChanges()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.NewAssemblyPath());

        var comparison = Compare(index, index);

        Assert.Empty(comparison.Changes);
    }

    [Fact]
    public void Compare_MethodKeyDistinguishesSameNameDifferentArityDeclaringTypes()
    {
        var method1 = DiffMethod(TypeRef.Definition("Asm", "Ns", "Box`1"), "Use");
        var method2 = DiffMethod(TypeRef.Definition("Asm", "Ns", "Box`2"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method1],
            [new UnsafeEvidence(method1, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method2],
            [new UnsafeEvidence(method2, "Unsafe operation", "stackalloc", "opcode", 0, null)]);

        var comparison = Compare(oldIndex, newIndex);

        Assert.Contains(comparison.Changes, change =>
            change.Kind == ResearchChangeKind.Removed);
        Assert.Contains(comparison.Changes, change =>
            change.Kind == ResearchChangeKind.Added);
    }

    [Fact]
    public void Compare_MethodKeyDistinguishesSameNameDifferentGenericArity()
    {
        var method1 = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var method2 = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use")
            with { GenericArity = 1, GenericParameterNames = ["T"] };
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method1],
            [new UnsafeEvidence(method1, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method2],
            [new UnsafeEvidence(method2, "Unsafe operation", "stackalloc", "opcode", 0, null)]);

        var comparison = Compare(oldIndex, newIndex);

        Assert.Contains(comparison.Changes, change =>
            change.Kind == ResearchChangeKind.Removed);
        Assert.Contains(comparison.Changes, change =>
            change.Kind == ResearchChangeKind.Added);
    }

    [Fact]
    public void Compare_PreservesCountOfRepeatedUnsafeOperations()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 0, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 4, null),
            ]);

        var comparison = Compare(oldIndex, newIndex);

        var added = Assert.Single(comparison.Changes);
        Assert.Equal(ResearchChangeKind.Added, added.Kind);
        Assert.StartsWith("stackalloc:", added.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_AttributesPrependedOffsetAdditionToNewOffset()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 10, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 20, null),
            ]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 5, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 10, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 20, null),
            ]);

        var comparison = Compare(oldIndex, newIndex);

        var added = Assert.Single(comparison.Changes);
        Assert.Equal(ResearchChangeKind.Added, added.Kind);
        Assert.Equal(5, added.NewIlOffset);
    }

    [Fact]
    public void Compare_OffsetShiftWithAddedOperation_DoesNotEmitRemoveAddFlood()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 10, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 20, null),
            ]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 5, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 15, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 25, null),
            ]);

        var comparison = Compare(oldIndex, newIndex);

        var added = Assert.Single(comparison.Changes);
        Assert.Equal(ResearchChangeKind.Added, added.Kind);
        Assert.Null(added.NewIlOffset);
    }

    [Fact]
    public void Compare_DoesNotMatchMemberEvidenceToBodyEvidence()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", null, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 4, null)]);

        var comparison = Compare(oldIndex, newIndex);

        Assert.Contains(comparison.Changes, change =>
            change.Kind == ResearchChangeKind.Removed
            && change.OldIlOffset is null);
        Assert.Contains(comparison.Changes, change =>
            change.Kind == ResearchChangeKind.Added
            && change.NewIlOffset == 4);
    }

    [Fact]
    public void Compare_ReorderedOperationsDoNotBecomeRemoveAddChanges()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [],
            unsafetyOccurrences: new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>
            {
                [method.MetadataToken] =
                [
                    new(method, 0, UnsafetyKind.StackAlloc, "byte*"),
                    new(method, 4, UnsafetyKind.Deref, "int"),
                ],
            });
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [],
            unsafetyOccurrences: new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>
            {
                [method.MetadataToken] =
                [
                    new(method, 0, UnsafetyKind.Deref, "int"),
                    new(method, 4, UnsafetyKind.StackAlloc, "byte*"),
                ],
            });

        var comparison = Compare(oldIndex, newIndex);

        Assert.Empty(comparison.Changes);
    }

    [Fact]
    public void Compare_DefiniteOperationSuppressesBroaderEvidenceAtSameOffset()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence([method], []);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 4, null)],
            unsafetyOccurrences: new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>
            {
                [method.MetadataToken] =
                [
                    new(method, 4, UnsafetyKind.StackAlloc, "byte*"),
                ],
            });

        var comparison = Compare(oldIndex, newIndex);

        var added = Assert.Single(comparison.Changes, change =>
            change.Descriptor.Id.StartsWith("unsafe.", StringComparison.Ordinal));
        Assert.Equal("stackalloc", added.Signal);
        Assert.Equal(4, added.NewIlOffset);
    }

    static ResearchComparison Compare(
        LibraryBodyIndex oldIndex,
        LibraryBodyIndex newIndex)
        => ResearchDiff.Compare(
            new ResearchDiffInput([], BodyIndexes: [oldIndex]),
            new ResearchDiffInput([], BodyIndexes: [newIndex]),
            new ResearchDiffOptions(ResearchChangeMechanism.BodySignals));

    static MethodIdentity DiffMethod(TypeRef declaring, string name)
        => new(
            "Asm",
            Guid.Empty,
            declaring,
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
}
