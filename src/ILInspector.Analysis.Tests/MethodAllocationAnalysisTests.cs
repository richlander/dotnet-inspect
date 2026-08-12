using System.Collections.Immutable;

using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Direct coverage of the allocation owner: occurrence discovery, the path/
/// multiplicity reading of the shared control flow, and escape classification.
/// Each case asserts externally observable <see cref="AllocationOccurrence"/>
/// properties over hand-written IL, so the test says what the analysis claims
/// rather than how it is wired.
/// </summary>
public sealed class MethodAllocationAnalysisTests
{
    const int ConstructorToken = 0x06000002;
    const int TypeToken = 0x01000004;
    const int FieldToken = 0x04000001;

    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_widget =
        TypeRef.Definition("Fixture", "Fixtures", "Widget");

    [Fact]
    public void StraightLineAllocationRunsOncePerCallAndReturnEscapes()
    {
        // newobj Widget::.ctor; ret
        byte[] il = [0x73, 0x02, 0x00, 0x00, 0x06, 0x2A];
        var result = Collect(il);

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(0, occurrence.ILOffset);
        Assert.Equal(AllocationKind.Object, occurrence.Kind);
        Assert.Equal(s_widget, occurrence.AllocatedType);
        Assert.True(occurrence.CountsAsHeapAllocation);
        Assert.False(occurrence.InLoop);
        Assert.Equal(AllocationPathContext.StraightLine, occurrence.PathContext);
        Assert.Equal(
            AllocationPathConfidence.DominatesReturn,
            occurrence.PathConfidence);
        Assert.Equal(AllocationMultiplicity.Once, occurrence.Multiplicity);
        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.Return, occurrence.EscapeKind);
    }

    [Fact]
    public void DroppedAllocationStaysLocalWithNoEscapeKind()
    {
        // newobj Widget::.ctor; pop; ret — the close negative for the return escape.
        byte[] il = [0x73, 0x02, 0x00, 0x00, 0x06, 0x26, 0x2A];
        var result = Collect(il);

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationEscape.LocalOnly, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.None, occurrence.EscapeKind);
        Assert.Equal(AllocationMultiplicity.Once, occurrence.Multiplicity);
    }

    [Fact]
    public void OneScanFeedsBothDiscoveredAndClassifiedOccurrences()
    {
        // newobj Widget::.ctor; ret
        byte[] il = [0x73, 0x02, 0x00, 0x00, 0x06, 0x2A];
        var result = Collect(il);

        var discovered = Assert.Single(result.DiscoveredOccurrences);
        var classified = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(discovered.ILOffset, classified.ILOffset);
        Assert.Equal(discovered.Kind, classified.Kind);
        // The shared scan leaves escape unclassified; only the published
        // occurrences carry the refined verdict.
        Assert.Equal(AllocationEscape.Unknown, discovered.Escape);
        Assert.Equal(AllocationEscape.Escapes, classified.Escape);
    }

    [Fact]
    public void DiscoveryIdentifiesAllocationOnThrowPath()
    {
        // newobj Widget::.ctor; throw
        byte[] il = [0x73, 0x02, 0x00, 0x00, 0x06, 0x7A];
        var result = Collect(il);

        var discovered = Assert.Single(result.DiscoveredOccurrences);
        var classified = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationEscape.ThrowPath, discovered.Escape);
        Assert.Equal(AllocationEscape.ThrowPath, classified.Escape);
        Assert.Equal(
            AllocationPathContext.ErrorPath,
            discovered.PathContext);
    }

    [Fact]
    public void AllocationBehindAConditionalBranchIsConditional()
    {
        // ldarg.0; brfalse.s IL_0008; newobj Widget::.ctor; ret
        byte[] il =
        [
            0x02,
            0x2C, 0x05,
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x2A,
        ];
        var context = Context(il);
        var analysis = new MethodAllocationAnalysis(context);
        var result = analysis.Collect(new Resolver(il));

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(3, occurrence.ILOffset);
        Assert.Equal(AllocationPathContext.Branch, occurrence.PathContext);
        Assert.Equal(
            AllocationPathConfidence.BehindBranch,
            occurrence.PathConfidence);
        Assert.Equal(
            AllocationMultiplicity.Conditional,
            occurrence.Multiplicity);
        // Call-site acquisition and optimization-opportunity collection query the
        // same interpretation for offsets that are not allocations.
        Assert.Equal(
            AllocationMultiplicity.Conditional,
            analysis.MultiplicityAt(3));
    }

    [Fact]
    public void AllocationOnALoopBackedgeIteratesPerCall()
    {
        // IL_0000: newobj Widget::.ctor; pop; ldarg.0; brtrue.s IL_0000; ret
        byte[] il =
        [
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x26,
            0x02,
            0x2D, 0xF7,
            0x2A,
        ];
        var result = Collect(il, loopRegions: [(0, 7)]);

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.True(occurrence.InLoop);
        Assert.Equal(AllocationPathContext.LoopBody, occurrence.PathContext);
        Assert.Equal(AllocationMultiplicity.Loop, occurrence.Multiplicity);
    }

    [Fact]
    public void LoopRegionMembershipAloneDoesNotMakeAnAllocationIterate()
    {
        // The same body without the backedge: the offset is reported inside a loop
        // region, but control cannot cycle back, so it runs at most once.
        // newobj Widget::.ctor; pop; ldarg.0; pop; ret
        byte[] il =
        [
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x26,
            0x02,
            0x26,
            0x2A,
        ];
        var result = Collect(il, loopRegions: [(0, 7)]);

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.True(occurrence.InLoop);
        Assert.Equal(AllocationPathContext.LoopBody, occurrence.PathContext);
        Assert.NotEqual(AllocationMultiplicity.Loop, occurrence.Multiplicity);
        Assert.Equal(AllocationMultiplicity.Once, occurrence.Multiplicity);
    }

    [Fact]
    public void StoreIntoAClosureFieldEscapesAsCapture()
    {
        // newobj Widget::.ctor; stfld <>c__DisplayClass0_0::value; ret
        byte[] il =
        [
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x7D, 0x01, 0x00, 0x00, 0x04,
            0x2A,
        ];
        var result = Collect(
            il,
            fieldOwner: (
                TypeRef.Definition(
                    "Fixture",
                    "Fixtures",
                    "Holder+<>c__DisplayClass0_0"),
                "value"));

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.Capture, occurrence.EscapeKind);
    }

    [Fact]
    public void StoreIntoAnOrdinaryFieldEscapesAsField()
    {
        // The close negative for capture: an ordinary declaring type is a plain
        // field escape, not a hoisted capture.
        byte[] il =
        [
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x7D, 0x01, 0x00, 0x00, 0x04,
            0x2A,
        ];
        var result = Collect(
            il,
            fieldOwner: (
                TypeRef.Definition("Fixture", "Fixtures", "Holder"),
                "value"));

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.Field, occurrence.EscapeKind);
    }

    [Fact]
    public void ConstantLengthArraySizeIsEstimatedExactly()
    {
        // ldc.i4.4; newarr System.Int32; pop; ret
        byte[] il =
        [
            0x1A,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x26,
            0x2A,
        ];
        var result = Collect(il);

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationKind.Array, occurrence.Kind);
        Assert.Equal(AllocationFactSource.Newarr, occurrence.Source);
        // 24-byte x64 sz-array header + 4 * 4-byte elements.
        Assert.Equal(40, occurrence.EstimatedSizeBytes);
        Assert.Equal(AllocationSizeTier.Exact, occurrence.SizeTier);
    }

    [Fact]
    public void NonConstantArrayLengthLeavesTheSizeUnknown()
    {
        // ldarg.0; newarr System.Int32; pop; ret — the close negative for the
        // constant-length estimate.
        byte[] il =
        [
            0x02,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x26,
            0x2A,
        ];
        var result = Collect(il);

        var occurrence = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationKind.Array, occurrence.Kind);
        Assert.Null(occurrence.EstimatedSizeBytes);
        Assert.Equal(AllocationSizeTier.Unknown, occurrence.SizeTier);
    }

    [Fact]
    public void NonHeapConstructionIsNotReported()
    {
        // A value-type newobj neither allocates nor annotates when the operand
        // resolves in this assembly.
        byte[] il = [0x73, 0x02, 0x00, 0x00, 0x06, 0x26, 0x2A];
        var result = Collect(il, nonHeapConstruction: true);

        Assert.Empty(result.DiscoveredOccurrences);
        Assert.Empty(result.ClassifiedOccurrences);
    }

    [Fact]
    public void OccurrenceBeforeLaterResolutionFailureIsPreserved()
    {
        // ldc.i4.1; newarr int; pop; newobj <malformed>; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x26,
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x2A,
        ];
        var context = Context(il);
        var result = new MethodAllocationAnalysis(context).Collect(
            new Resolver(il) { ThrowOnMemberResolution = true });

        var raw = Assert.Single(result.DiscoveredOccurrences);
        var classified = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationKind.Array, raw.Kind);
        Assert.Equal(raw.ILOffset, classified.ILOffset);
    }

    static MethodAllocationResult Collect(
        byte[] il,
        IReadOnlyList<(int Start, int End)>? loopRegions = null,
        (TypeRef? DeclaringType, string? Name) fieldOwner = default,
        bool nonHeapConstruction = false)
    {
        var context = Context(il, loopRegions);
        var analysis = new MethodAllocationAnalysis(context);
        return analysis.Collect(
            new Resolver(il)
            {
                FieldOwner = fieldOwner,
                NonHeapConstruction = nonHeapConstruction,
            });
    }

    static MethodBodyAnalysisContext Context(
        byte[] il,
        IReadOnlyList<(int Start, int End)>? loopRegions = null)
    {
        var instructions = MethodInstructions.Decode(il, il.Length, []);
        Assert.True(instructions.IsComplete);
        return new MethodBodyAnalysisContext(
            Method(),
            instructions,
            [],
            loopRegions ?? [],
            []);
    }

    static MethodIdentity Method()
        => new(
            "Fixture",
            Guid.Empty,
            TypeRef.Definition("Fixture", "Fixtures", "Holder"),
            "M",
            [s_int],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);

    /// <summary>
    /// The metadata/IL answers the assembly reader would supply, stubbed to the
    /// fixture's single constructor, type, and field token.
    /// </summary>
    sealed class Resolver(byte[] il) : IMethodAllocationResolver
    {
        public (TypeRef? DeclaringType, string? Name) FieldOwner { get; init; }

        public bool NonHeapConstruction { get; init; }

        public bool ThrowOnMemberResolution { get; init; }

        public TypeRef ResolveType(int token)
            => token == TypeToken ? s_int : TypeRef.Unsupported("type token");

        public MemberRef ResolveMember(int token)
            => ThrowOnMemberResolution
                ? throw new BadImageFormatException("Malformed member token.")
                : token == ConstructorToken
                ? new MemberRef(
                    s_widget,
                    ".ctor",
                    [],
                    TypeRef.CoreLib("System", "Void"),
                    MemberKind.Constructor)
                : MemberRef.Unsupported("member token");

        public NewObjectConstructionKind ClassifyConstruction(
            int operandToken,
            TypeRef declaringType)
            => NonHeapConstruction
                ? NewObjectConstructionKind.NonHeap
                : NewObjectConstructionKind.Heap;

        public bool IsDelegateConstructor(int operandToken, MemberRef constructor)
            => false;

        public bool IsAllocatingValueTypeBox(int operandToken, TypeRef boxed)
            => true;

        public bool IsInAssemblyReferenceType(int typeToken) => false;

        public (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(
            int fieldToken)
            => fieldToken == FieldToken ? FieldOwner : (null, null);

        public ReachingDefinitionsResult AnalyzeReachingDefinitions()
            => ReachingDefinitions.Analyze(il, argumentSlotCount: 1);
    }
}
