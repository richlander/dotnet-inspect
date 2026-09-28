using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Direct coverage of the allocation owner: occurrence discovery, the path/
/// multiplicity reading of the shared control flow, and escape classification.
/// Each case asserts externally observable <see cref="AllocationOccurrence"/>
/// properties over hand-written IL, so the test says what the analysis claims
/// rather than how it is wired.
/// </summary>
public sealed class MethodAllocationFactsTests
{
    const int ConstructorToken = 0x06000002;
    const int TypeToken = 0x01000004;
    const int LoadTypeToken = 0x01000005;
    const int FieldToken = 0x04000001;

    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_char = TypeRef.CoreLib("System", "Char");
    static readonly TypeRef s_string =
        TypeRef.CoreLib("System", "String");
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
        Assert.Equal(
            new AllocationLifetimeUse(
                5,
                AllocationLifetimeUseKind.Return),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
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
        Assert.Equal(
            new AllocationLifetimeUse(
                5,
                AllocationLifetimeUseKind.Drop),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
    }

    [Fact]
    public void FactsBundlesBindContextOccurrencesAndQueries()
    {
        // newobj Widget::.ctor; pop; ldarg.0; brtrue.s IL_0000; ret
        byte[] il =
        [
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x26,
            0x02,
            0x2D, 0xF7,
            0x2A,
        ];
        var loopContext = Context(il, [(0, 7)]);
        var straightContext = Context(il);
        var loopFacts = MethodAllocationFacts.Create(loopContext);
        loopFacts.Collect(new Resolver(il));
        var straightFacts = MethodAllocationFacts.Create(straightContext);
        straightFacts.Collect(new Resolver(il));

        var discovered = Assert.Single(loopFacts.DiscoveredOccurrences);
        var classified = Assert.Single(loopFacts.ClassifiedOccurrences);
        Assert.Same(loopContext, loopFacts.Context);
        Assert.Same(straightContext, straightFacts.Context);
        Assert.Equal(AllocationKind.Object, discovered.Kind);
        Assert.Equal(AllocationKind.Object, classified.Kind);
        // The shared scan leaves escape unclassified; only the published
        // occurrences carry the refined verdict.
        Assert.Equal(AllocationEscape.Unknown, discovered.Escape);
        Assert.Equal(AllocationEscape.LocalOnly, classified.Escape);
        Assert.Equal(
            AllocationMultiplicity.Loop,
            loopFacts.MultiplicityAt(0));
        Assert.NotEqual(
            AllocationMultiplicity.Loop,
            straightFacts.MultiplicityAt(0));
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
        var result = MethodAllocationFacts.Create(context);
        result.Collect(new Resolver(il));

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
            result.MultiplicityAt(3));
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
        Assert.Equal(
            new AllocationLifetimeUse(
                5,
                AllocationLifetimeUseKind.Capture),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
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
        Assert.Equal(
            new AllocationLifetimeUse(
                5,
                AllocationLifetimeUseKind.FieldStore),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
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
    public void MultipleLocalUsesJoinAllTerminalEvidence()
    {
        // ldc.i4.1; newarr int; stloc.0;
        // ldloc.0; pop; ldloc.0; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x06,
            0x26,
            0x06,
            0x2A,
        ];

        var result = Collect(il);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.Return, occurrence.EscapeKind);
        Assert.Equal(
            [
                new(
                    8,
                    AllocationLifetimeUseKind.Drop),
                new(
                    10,
                    AllocationLifetimeUseKind.Return),
            ],
            occurrence.LifetimeEvidence.Uses);
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
    }

    [Fact]
    public void ConvergentAliasGraphRetainsDistinctEvidenceWithinBudget()
    {
        MemberRef sink = ByReferenceCaptureMember();
        _ = Collect(
            ConvergentAliasGraph(2),
            resolvedMember: sink);

        byte[] il = ConvergentAliasGraph(14);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = Collect(il, resolvedMember: sink);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.True(
            occurrence.Escape == AllocationEscape.Escapes,
            $"Expected escape; limitations: {string.Join(
                ", ",
                occurrence.LifetimeEvidence.Limitations)}");
        Assert.Equal(
            AllocationLifetimeUseKind.ByReferenceTransfer,
            Assert.Single(occurrence.LifetimeEvidence.Uses).Kind);
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
        Assert.True(
            allocated < 2 * 1024 * 1024,
            $"Convergent alias analysis allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CyclicAliasRetainsReachableSinkAndLimitation()
    {
        // ldc.i4.1; newarr int; stloc.0;
        // loop: ldloc.0; stloc.0; ldarg.0; brtrue.s loop;
        // ldloc.0; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x06,
            0x0A,
            0x02,
            0x2D, 0xFB,
            0x06,
            0x2A,
        ];

        var result = Collect(il);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(
            new AllocationLifetimeUse(
                13,
                AllocationLifetimeUseKind.Return),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
        Assert.Equal(
            AllocationLifetimeLimitationKind.AliasCycle,
            Assert.Single(
                occurrence.LifetimeEvidence.Limitations).Kind);
    }

    [Fact]
    public void ManagedReferenceReadContinuesToArrayConsumer()
    {
        // ldc.i4.1; newarr int; stloc.0; ldloca.s 0;
        // ldind.ref; ldlen; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x50,
            0x8E,
            0x26,
            0x2A,
        ];

        var result = Collect(il);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.LocalOnly, occurrence.Escape);
        Assert.Equal(
            new AllocationLifetimeUse(
                10,
                AllocationLifetimeUseKind.LengthRead),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
    }

    [Fact]
    public void ManagedObjectReadRequiresMatchingType()
    {
        // ldc.i4.1; newarr int; stloc.0; ldloca.s 0;
        // ldobj int[]; ldlen; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x71, 0x05, 0x00, 0x00, 0x01,
            0x8E,
            0x26,
            0x2A,
        ];

        var result = Collect(
            il,
            resolvedLoadType: TypeRef.SzArray(s_int));

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.LocalOnly, occurrence.Escape);
        Assert.Equal(
            new AllocationLifetimeUse(
                14,
                AllocationLifetimeUseKind.LengthRead),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedManagedObjectReadPublishesLimitation(
        bool throws)
    {
        // ldc.i4.1; newarr int; stloc.0; ldloca.s 0;
        // ldobj <unresolved>; ldlen; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x71, 0x05, 0x00, 0x00, 0x01,
            0x8E,
            0x26,
            0x2A,
        ];

        var result = Collect(
            il,
            throwOnLoadTypeResolution: throws);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Unknown, occurrence.Escape);
        Assert.Empty(occurrence.LifetimeEvidence.Uses);
        Assert.Equal(
            new AllocationLifetimeLimitation(
                AllocationLifetimeLimitationKind.MetadataResolution,
                9,
                ILOpCode.Ldobj),
            Assert.Single(occurrence.LifetimeEvidence.Limitations));
    }

    [Fact]
    public void IncompatibleManagedObjectReadPublishesLimitation()
    {
        // ldc.i4.1; newarr int; stloc.0; ldloca.s 0;
        // ldobj Widget; ldlen; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x71, 0x05, 0x00, 0x00, 0x01,
            0x8E,
            0x26,
            0x2A,
        ];

        var result = Collect(
            il,
            resolvedLoadType: s_widget);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Unknown, occurrence.Escape);
        Assert.Empty(occurrence.LifetimeEvidence.Uses);
        Assert.Equal(
            new AllocationLifetimeLimitation(
                AllocationLifetimeLimitationKind
                    .UnsupportedByReferenceFlow,
                9,
                ILOpCode.Ldobj),
            Assert.Single(occurrence.LifetimeEvidence.Limitations));
    }

    [Fact]
    public void NestedFunctionPointerMismatchPublishesLimitation()
    {
        // ldc.i4.1; newarr delegate*<int>; stloc.0; ldloca.s 0;
        // ldobj delegate*<string>[]; ldlen; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x71, 0x05, 0x00, 0x00, 0x01,
            0x8E,
            0x26,
            0x2A,
        ];

        var result = Collect(
            il,
            resolvedType: FunctionPointerReturning(s_int),
            resolvedLoadType: TypeRef.SzArray(
                FunctionPointerReturning(s_string)));

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Unknown, occurrence.Escape);
        Assert.Empty(occurrence.LifetimeEvidence.Uses);
        Assert.Equal(
            new AllocationLifetimeLimitation(
                AllocationLifetimeLimitationKind
                    .UnsupportedByReferenceFlow,
                9,
                ILOpCode.Ldobj),
            Assert.Single(occurrence.LifetimeEvidence.Limitations));
    }

    [Fact]
    public void MatchingNestedFunctionPointerResumesArrayConsumer()
    {
        // ldc.i4.1; newarr delegate*<int>; stloc.0; ldloca.s 0;
        // ldobj delegate*<int>[]; ldlen; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x71, 0x05, 0x00, 0x00, 0x01,
            0x8E,
            0x26,
            0x2A,
        ];
        TypeRef pointer = FunctionPointerReturning(s_int);

        var result = Collect(
            il,
            resolvedType: pointer,
            resolvedLoadType: TypeRef.SzArray(pointer));

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.LocalOnly, occurrence.Escape);
        Assert.Equal(
            new AllocationLifetimeUse(
                14,
                AllocationLifetimeUseKind.LengthRead),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
    }

    [Fact]
    public void ByReferenceCallUsesCallCoordinate()
    {
        // ldc.i4.1; newarr int; stloc.0; ldloca.s 0;
        // call Capture(ref int[]); ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x28, 0x02, 0x00, 0x00, 0x06,
            0x2A,
        ];

        var result = Collect(
            il,
            resolvedMember: ByReferenceCaptureMember());

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Escapes, occurrence.Escape);
        Assert.Equal(AllocationEscapeKind.None, occurrence.EscapeKind);
        Assert.Equal(
            new AllocationLifetimeUse(
                9,
                AllocationLifetimeUseKind.ByReferenceTransfer),
            Assert.Single(occurrence.LifetimeEvidence.Uses));
        Assert.Empty(occurrence.LifetimeEvidence.Limitations);
    }

    [Fact]
    public void UnsupportedManagedReferenceFlowPublishesLimitation()
    {
        // ldc.i4.1; newarr int; stloc.0; ldloca.s 0;
        // dup; pop; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x12, 0x00,
            0x25,
            0x26,
            0x26,
            0x2A,
        ];

        var result = Collect(il);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Unknown, occurrence.Escape);
        Assert.Empty(occurrence.LifetimeEvidence.Uses);
        Assert.Equal(
            new AllocationLifetimeLimitation(
                AllocationLifetimeLimitationKind
                    .UnsupportedByReferenceFlow,
                9,
                ILOpCode.Dup),
            Assert.Single(occurrence.LifetimeEvidence.Limitations));
    }

    [Theory]
    [InlineData(true, AllocationEscape.LocalOnly)]
    [InlineData(false, AllocationEscape.Unknown)]
    public void StringArrayConstructor_IsTrustedOnlyForCoreLibrary(
        bool coreLibrary,
        AllocationEscape expected)
    {
        // ldc.i4.1; newarr char; stloc.0; ldloc.0;
        // newobj String::.ctor(char[]); pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x06,
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x26,
            0x2A,
        ];
        TypeRef array = TypeRef.SzArray(s_char);
        var constructor = new MemberRef(
            coreLibrary
                ? TypeRef.CoreLib("System", "String")
                : TypeRef.Definition(
                    "Fixture",
                    "System",
                    "String"),
            ".ctor",
            [array],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Constructor);

        var result = Collect(
            il,
            resolvedType: s_char,
            resolvedMember: constructor);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(expected, occurrence.Escape);
        if (coreLibrary)
        {
            Assert.Equal(
                new AllocationLifetimeUse(
                    8,
                    AllocationLifetimeUseKind
                        .TrustedNonCapturingCall),
                Assert.Single(occurrence.LifetimeEvidence.Uses));
            Assert.Empty(occurrence.LifetimeEvidence.Limitations);
        }
        else
        {
            Assert.Equal(
                new AllocationLifetimeLimitation(
                    AllocationLifetimeLimitationKind
                        .UnsupportedCall,
                    8,
                    ILOpCode.Newobj),
                Assert.Single(
                    occurrence.LifetimeEvidence.Limitations));
        }
    }

    [Fact]
    public void StringArrayConstructor_WithRetainedAliasIsNotLocalOnly()
    {
        // ldc.i4.1; newarr char; dup;
        // newobj String::.ctor(char[]); pop; stsfld; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x25,
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x26,
            0x80, 0x03, 0x00, 0x00, 0x04,
            0x2A,
        ];
        TypeRef array = TypeRef.SzArray(s_char);
        var constructor = new MemberRef(
            TypeRef.CoreLib("System", "String"),
            ".ctor",
            [array],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Constructor);

        var result = Collect(
            il,
            resolvedType: s_char,
            resolvedMember: constructor);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Unknown, occurrence.Escape);
        Assert.Equal(
            new AllocationLifetimeLimitation(
                AllocationLifetimeLimitationKind.UnsupportedStackShape,
                7,
                ILOpCode.Newobj),
            Assert.Single(occurrence.LifetimeEvidence.Limitations));
    }

    [Fact]
    public void IncompleteReachingDefinitions_KeepStoredArrayUnknown()
    {
        // ldc.i4.1; newarr int; stloc.0; ldloc.0; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
            0x06,
            0x26,
            0x2A,
        ];

        var result = Collect(
            il,
            incompleteReachingDefinitions: true);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Unknown, occurrence.Escape);
        Assert.Equal(
            new AllocationLifetimeLimitation(
                AllocationLifetimeLimitationKind
                    .ReachingDefinitionsIncomplete,
                6,
                ILOpCode.Stloc_0),
            Assert.Single(occurrence.LifetimeEvidence.Limitations));
    }

    [Fact]
    public void FailedCallResolutionPublishesTypedLimitation()
    {
        // ldc.i4.1; newarr char; newobj <malformed>; pop; ret
        byte[] il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x73, 0x02, 0x00, 0x00, 0x06,
            0x26,
            0x2A,
        ];

        var result = Collect(
            il,
            resolvedType: s_char,
            throwOnMemberResolution: true);

        AllocationOccurrence occurrence = Assert.Single(
            result.ClassifiedOccurrences,
            occurrence => occurrence.Kind == AllocationKind.Array);
        Assert.Equal(AllocationEscape.Unknown, occurrence.Escape);
        Assert.Equal(
            new AllocationLifetimeLimitation(
                AllocationLifetimeLimitationKind.MetadataResolution,
                6,
                ILOpCode.Newobj),
            Assert.Single(occurrence.LifetimeEvidence.Limitations));
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
        var result = MethodAllocationFacts.Create(context);
        result.Collect(
            new Resolver(il) { ThrowOnMemberResolution = true });

        var raw = Assert.Single(result.DiscoveredOccurrences);
        var classified = Assert.Single(result.ClassifiedOccurrences);
        Assert.Equal(AllocationKind.Array, raw.Kind);
        Assert.Equal(raw.ILOffset, classified.ILOffset);
    }

    static MethodAllocationFacts Collect(
        byte[] il,
        IReadOnlyList<(int Start, int End)>? loopRegions = null,
        (TypeRef? DeclaringType, string? Name) fieldOwner = default,
        bool nonHeapConstruction = false,
        TypeRef? resolvedType = null,
        TypeRef? resolvedLoadType = null,
        MemberRef? resolvedMember = null,
        bool incompleteReachingDefinitions = false,
        bool throwOnLoadTypeResolution = false,
        bool throwOnMemberResolution = false)
    {
        var context = Context(il, loopRegions);
        var result = MethodAllocationFacts.Create(context);
        result.Collect(new Resolver(il)
            {
                FieldOwner = fieldOwner,
                NonHeapConstruction = nonHeapConstruction,
                ResolvedType = resolvedType,
                ResolvedLoadType = resolvedLoadType,
                ResolvedMember = resolvedMember,
                IncompleteReachingDefinitions =
                    incompleteReachingDefinitions,
                ThrowOnLoadTypeResolution =
                    throwOnLoadTypeResolution,
                ThrowOnMemberResolution =
                    throwOnMemberResolution,
            });
        return result;
    }

    static MemberRef ByReferenceCaptureMember() =>
        new(
            s_widget,
            "Capture",
            [TypeRef.ByRef(TypeRef.SzArray(s_int))],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

    static TypeRef FunctionPointerReturning(
        TypeRef returnType) =>
        TypeRef.UnsupportedFunctionPointer(
            new MethodSignature<TypeRef>(
                new SignatureHeader(
                    SignatureKind.Method,
                    SignatureCallingConvention.Default,
                    SignatureAttributes.None),
                returnType,
                requiredParameterCount: 0,
                genericParameterCount: 0,
                []));

    static byte[] ConvergentAliasGraph(int depth)
    {
        List<byte> il =
        [
            0x17,
            0x8D, 0x04, 0x00, 0x00, 0x01,
            0x0A,
        ];

        for (int slot = 1; slot <= depth; slot++)
        {
            il.Add(0x02);
            il.Add(0x2C);
            int falseBranchOperand = il.Count;
            il.Add(0);
            EmitLoad(il, slot - 1);
            EmitStore(il, slot);
            il.Add(0x2B);
            int joinBranchOperand = il.Count;
            il.Add(0);

            int falseOffset = il.Count;
            EmitLoad(il, slot - 1);
            EmitStore(il, slot);
            int joinOffset = il.Count;

            il[falseBranchOperand] = BranchDelta(
                falseOffset,
                falseBranchOperand);
            il[joinBranchOperand] = BranchDelta(
                joinOffset,
                joinBranchOperand);
        }

        il.Add(0x12);
        il.Add(checked((byte)depth));
        il.AddRange([0x28, 0x02, 0x00, 0x00, 0x06, 0x2A]);
        return [.. il];

        static byte BranchDelta(int target, int operandOffset) =>
            unchecked((byte)checked(
                (sbyte)(target - operandOffset - 1)));

        static void EmitLoad(List<byte> body, int slot)
        {
            if (slot <= 3)
            {
                body.Add(checked((byte)(0x06 + slot)));
                return;
            }
            body.Add(0x11);
            body.Add(checked((byte)slot));
        }

        static void EmitStore(List<byte> body, int slot)
        {
            if (slot <= 3)
            {
                body.Add(checked((byte)(0x0A + slot)));
                return;
            }
            body.Add(0x13);
            body.Add(checked((byte)slot));
        }
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

        public bool ThrowOnLoadTypeResolution { get; init; }

        public TypeRef? ResolvedType { get; init; }

        public TypeRef? ResolvedLoadType { get; init; }

        public MemberRef? ResolvedMember { get; init; }

        public bool IncompleteReachingDefinitions { get; init; }

        public TypeRef ResolveType(int token)
        {
            if (token == LoadTypeToken
                && ThrowOnLoadTypeResolution)
            {
                throw new BadImageFormatException(
                    "Malformed type token.");
            }

            return token switch
            {
                TypeToken => ResolvedType ?? s_int,
                LoadTypeToken => ResolvedLoadType
                    ?? TypeRef.Unsupported("type token"),
                _ => TypeRef.Unsupported("type token"),
            };
        }

        public bool ExactSignatureTypesMatch(
            TypeRef left,
            TypeRef right) =>
            TypeRef.ExactSignatureEquals(left, right);

        public MemberRef ResolveMember(int token)
            => ThrowOnMemberResolution
                ? throw new BadImageFormatException("Malformed member token.")
                : token == ConstructorToken
                ? ResolvedMember
                    ?? new MemberRef(
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
            => IncompleteReachingDefinitions
                ? new([], [], false, "Synthetic incomplete flow.")
                : ReachingDefinitions.Analyze(
                    il,
                    argumentSlotCount: 1);
    }
}
