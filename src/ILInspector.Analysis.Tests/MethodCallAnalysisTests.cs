using System.Collections.Immutable;

using ILInspector.Instructions;

namespace ILInspector.Analysis.Tests;

public sealed class MethodCallAnalysisTests
{
    const int FirstToken = 0x0A000001;
    const int SecondToken = 0x0A000002;
    const int SignatureToken = 0x11000001;

    static readonly TypeRef s_void =
        TypeRef.CoreLib("System", "Void");

    [Fact]
    public void ProjectsCallKindsAndCoordinatesInInstructionOrder()
    {
        byte[] il =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x6F, 0x02, 0x00, 0x00, 0x0A,
            0x73, 0x01, 0x00, 0x00, 0x0A,
            0xFE, 0x06, 0x02, 0x00, 0x00, 0x0A,
            0xFE, 0x07, 0x01, 0x00, 0x00, 0x0A,
            0x29, 0x01, 0x00, 0x00, 0x11,
            0x2A,
        ];
        var context = Context(il, [(5, 21)]);
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();

        MethodCallAnalysis.Collect(
            context,
            new Resolver(),
            offset => offset switch
            {
                0 => AllocationMultiplicity.Once,
                5 => AllocationMultiplicity.Conditional,
                10 => AllocationMultiplicity.Loop,
                15 => AllocationMultiplicity.Unknown,
                21 => AllocationMultiplicity.Once,
                _ => AllocationMultiplicity.Conditional,
            },
            calls,
            evidence,
            includeIndirectOpcodes: false);

        Assert.Collection(
            calls,
            call => AssertCall(
                call,
                0,
                5,
                FirstToken,
                0x06000001,
                CallKind.Call,
                "call"),
            call => AssertCall(
                call,
                5,
                10,
                SecondToken,
                0x06000002,
                CallKind.CallVirtual,
                "callvirt"),
            call => AssertCall(
                call,
                10,
                15,
                FirstToken,
                0x06000001,
                CallKind.NewObject,
                "newobj"),
            call => AssertCall(
                call,
                15,
                21,
                SecondToken,
                0x06000002,
                CallKind.LoadFunction,
                "ldftn"),
            call => AssertCall(
                call,
                21,
                27,
                FirstToken,
                0x06000001,
                CallKind.LoadVirtualFunction,
                "ldvirtftn"),
            call => AssertCall(
                call,
                27,
                32,
                SignatureToken,
                SignatureToken,
                CallKind.CallIndirect,
                "calli"));
        Assert.Collection(
            calls,
            call => AssertCallContext(
                call,
                inLoop: false,
                AllocationMultiplicity.Once),
            call => AssertCallContext(
                call,
                inLoop: true,
                AllocationMultiplicity.Conditional),
            call => AssertCallContext(
                call,
                inLoop: true,
                AllocationMultiplicity.Loop),
            call => AssertCallContext(
                call,
                inLoop: true,
                AllocationMultiplicity.Unknown),
            call => AssertCallContext(
                call,
                inLoop: true,
                AllocationMultiplicity.Once),
            call => AssertCallContext(
                call,
                inLoop: false,
                AllocationMultiplicity.Conditional));
        Assert.Single(
            evidence,
            item => item.Kind == "calli"
                && item.ILOffset == 27);
    }

    [Fact]
    public void DelegatesUnsafeCallAndOperationEvidenceInBodyOrder()
    {
        byte[] il =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x4A,
            0x29, 0x01, 0x00, 0x00, 0x11,
            0x2A,
        ];
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();

        MethodCallAnalysis.Collect(
            Context(il),
            new Resolver(unsafeMember: true),
            _ => AllocationMultiplicity.Once,
            calls,
            evidence,
            includeIndirectOpcodes: true);

        Assert.Collection(
            evidence,
            item =>
            {
                Assert.Equal("Unsafe call", item.Reason);
                Assert.Equal(0, item.ILOffset);
            },
            item =>
            {
                Assert.Equal("Unsafe operation", item.Reason);
                Assert.Equal(5, item.ILOffset);
            },
            item =>
            {
                Assert.Equal("calli", item.Kind);
                Assert.Equal(6, item.ILOffset);
            });
    }

    [Fact]
    public void SuppressesIndirectOpcodesWithoutSuppressingOtherUnsafeOperations()
    {
        byte[] il =
        [
            0xFE, 0x0F,
            0x4A,
            0x29, 0x01, 0x00, 0x00, 0x11,
            0x2A,
        ];
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();

        MethodCallAnalysis.Collect(
            Context(il),
            new Resolver(),
            _ => AllocationMultiplicity.Once,
            calls,
            evidence,
            includeIndirectOpcodes: false);

        Assert.Collection(
            evidence,
            item =>
            {
                Assert.Equal("localloc", item.Detail);
                Assert.Equal(0, item.ILOffset);
            },
            item =>
            {
                Assert.Equal("calli", item.Kind);
                Assert.Equal(3, item.ILOffset);
            });
    }

    [Fact]
    public void DirectCallDependenciesCompleteBeforeResultsAreAppended()
    {
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var events = new List<string>();

        Assert.Throws<BadImageFormatException>(() =>
            MethodCallAnalysis.Collect(
                Context([0x28, 0x01, 0x00, 0x00, 0x0A, 0x2A]),
                new Resolver(
                    unsafeMember: true,
                    throwOnDefinition: true,
                    events: events),
                _ =>
                {
                    events.Add("multiplicity");
                    return AllocationMultiplicity.Once;
                },
                calls,
                evidence,
                includeIndirectOpcodes: false));

        Assert.Equal(["member", "definition"], events);
        Assert.Empty(calls);
        Assert.Empty(evidence);

        events.Clear();
        Assert.Throws<InvalidOperationException>(() =>
            MethodCallAnalysis.Collect(
                Context([0x28, 0x01, 0x00, 0x00, 0x0A, 0x2A]),
                new Resolver(unsafeMember: true, events: events),
                _ =>
                {
                    events.Add("multiplicity");
                    throw new InvalidOperationException(
                        "Multiplicity unavailable.");
                },
                calls,
                evidence,
                includeIndirectOpcodes: false));

        Assert.Equal(
            ["member", "definition", "multiplicity"],
            events);
        Assert.Empty(calls);
        Assert.Empty(evidence);
    }

    [Fact]
    public void IndirectCallResolutionCompletesBeforeResultsAreAppended()
    {
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        bool multiplicityRequested = false;

        Assert.Throws<BadImageFormatException>(() =>
            MethodCallAnalysis.Collect(
                Context([0x29, 0x01, 0x00, 0x00, 0x11, 0x2A]),
                new Resolver(throwOnIndirectCall: true),
                _ =>
                {
                    multiplicityRequested = true;
                    return AllocationMultiplicity.Once;
                },
                calls,
                evidence,
                includeIndirectOpcodes: false));

        Assert.False(multiplicityRequested);
        Assert.Empty(calls);
        Assert.Empty(evidence);
    }

    [Fact]
    public void ResultsBeforeLaterResolutionFailureArePreserved()
    {
        byte[] il =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x2A,
        ];
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();

        Assert.Throws<BadImageFormatException>(() =>
            MethodCallAnalysis.Collect(
                Context(il),
                new Resolver(
                    unsafeMember: true,
                    throwOnSecondMember: true),
                _ => AllocationMultiplicity.Once,
                calls,
                evidence,
                includeIndirectOpcodes: false));

        var call = Assert.Single(calls);
        Assert.Equal(FirstToken, call.OperandToken);
        var item = Assert.Single(evidence);
        Assert.Equal("Unsafe call", item.Reason);
        Assert.Equal(0, item.ILOffset);
    }

    [Fact]
    public void ClassifiesReturnedAndDiscardedCallResults()
    {
        DirectCall returned = CollectSingle(
            [
                0x28, 0x01, 0x00, 0x00, 0x0A,
                0x2A,
            ]);
        DirectCall discarded = CollectSingle(
            [
                0x28, 0x01, 0x00, 0x00, 0x0A,
                0x26,
                0x2A,
            ]);

        Assert.Equal(
            DirectCallResultUse.MethodReturn,
            returned.ResultUse);
        Assert.Equal(5, returned.ResultConsumerOffset);
        Assert.Equal(
            DirectCallResultUse.Discarded,
            discarded.ResultUse);
        Assert.Equal(5, discarded.ResultConsumerOffset);
    }

    [Fact]
    public void ClassifiesSingleLocalUseAsCallArgument()
    {
        byte[] il =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x0A,
            0x06,
            0x28, 0x02, 0x00, 0x00, 0x0A,
            0x2A,
        ];
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();

        MethodCallAnalysis.Collect(
            Context(il),
            new Resolver(returningFirst: true),
            _ => AllocationMultiplicity.Once,
            calls,
            evidence,
            includeIndirectOpcodes: false);

        DirectCall producer = calls[0];
        Assert.Equal(
            DirectCallResultUse.CallArgument,
            producer.ResultUse);
        Assert.Equal(7, producer.ResultConsumerOffset);
    }

    [Fact]
    public void ClassifiesReturnSinkSourcesAndIncompleteCoverage()
    {
        byte[] sharedLocalReturn =
        [
        0x16,
        0x2C, 0x08,
        0x28, 0x01, 0x00, 0x00, 0x0A,
        0x0A,
        0x2B, 0x06,
        0x72, 0x02, 0x00, 0x00, 0x70,
        0x0A,
        0x06,
        0x2A,
        ];
        byte[] serializedBranches = (byte[])sharedLocalReturn.Clone();
        serializedBranches[11] = 0x28;
        serializedBranches[12] = 0x01;
        serializedBranches[13] = 0x00;
        serializedBranches[14] = 0x00;
        serializedBranches[15] = 0x0A;

        MethodResultSink incomplete = Assert.Single(
        CollectResultSinks(sharedLocalReturn));
        MethodResultSink complete = Assert.Single(
        CollectResultSinks(serializedBranches));

        Assert.Equal(MethodResultSinkKind.MethodReturn, incomplete.Kind);
        Assert.Equal(18, incomplete.ILOffset);
        Assert.Empty(incomplete.SourceCallOffsets);
        Assert.False(incomplete.IsComplete);
        Assert.Equal([3, 11], complete.SourceCallOffsets);
        Assert.True(complete.IsComplete);
    }

    [Fact]
    public void RejectsMergedEvaluationStackResultSources()
    {
        byte[] directReturn =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x2A,
        ];
        byte[] localReturn =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x0A,
            0x06,
            0x2A,
        ];
        byte[] rawStackMerge =
        [
            0x02,
            0x25,
            0x2D, 0x06,
            0x26,
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x2A,
        ];
        byte[] rawLocalStore =
        [
            0x02,
            0x25,
            0x2D, 0x06,
            0x26,
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x0A,
            0x06,
            0x2A,
        ];
        byte[] addressTakenLocal =
        [
            0x28, 0x01, 0x00, 0x00, 0x0A,
            0x0A,
            0x12, 0x00,
            0x26,
            0x06,
            0x2A,
        ];

        MethodResultSink direct = Assert.Single(
            CollectResultSinks(directReturn));
        MethodResultSink local = Assert.Single(
            CollectResultSinks(localReturn));
        MethodResultSink rawMerged = Assert.Single(
            CollectResultSinks(rawStackMerge));
        MethodResultSink rawStored = Assert.Single(
            CollectResultSinks(rawLocalStore));
        MethodResultSink addressTaken = Assert.Single(
            CollectResultSinks(addressTakenLocal));

        Assert.Equal([0], direct.SourceCallOffsets);
        Assert.True(direct.IsComplete);
        Assert.Equal([0], local.SourceCallOffsets);
        Assert.True(local.IsComplete);
        Assert.Empty(rawMerged.SourceCallOffsets);
        Assert.False(rawMerged.IsComplete);
        Assert.Empty(rawStored.SourceCallOffsets);
        Assert.False(rawStored.IsComplete);
        Assert.Empty(addressTaken.SourceCallOffsets);
        Assert.False(addressTaken.IsComplete);
    }

    static DirectCall CollectSingle(byte[] il)
    {
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        MethodCallAnalysis.Collect(
            Context(il),
            new Resolver(returningFirst: true),
            _ => AllocationMultiplicity.Once,
            calls,
            evidence,
            includeIndirectOpcodes: false);
        return Assert.Single(calls);
    }

    static ImmutableArray<MethodResultSink> CollectResultSinks(byte[] il)
    {
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var resultSinks = ImmutableArray.CreateBuilder<MethodResultSink>();
        MethodCallAnalysis.Collect(
            Context(il),
            new Resolver(returningFirst: true),
            _ => AllocationMultiplicity.Once,
            calls,
            evidence,
            includeIndirectOpcodes: false,
            resultSinks: resultSinks);
        return resultSinks.ToImmutable();
    }

    static void AssertCall(
        DirectCall call,
        int offset,
        int returnAddress,
        int operandToken,
        int definitionToken,
        CallKind kind,
        string opcode)
    {
        Assert.Equal(offset, call.ILOffset);
        Assert.Equal(returnAddress, call.ReturnAddress);
        Assert.Equal(operandToken, call.OperandToken);
        Assert.Equal(definitionToken, call.CalleeDefinitionToken);
        Assert.Equal(kind, call.Kind);
        Assert.Equal(opcode, call.Opcode);
    }

    static void AssertCallContext(
        DirectCall call,
        bool inLoop,
        AllocationMultiplicity multiplicity)
    {
        Assert.Equal(inLoop, call.InLoop);
        Assert.Equal(multiplicity, call.Multiplicity);
    }

    static MethodBodyAnalysisContext Context(
        byte[] il,
        IReadOnlyList<(int Start, int End)>? loopRegions = null)
    {
        var instructions = MethodInstructions.Decode(
            il,
            il.Length,
            []);
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
            TypeRef.Definition(
                "Fixture",
                "Fixtures",
                "Caller"),
            "M",
            [],
            s_void,
            MetadataToken: 0x06000001,
            IsStatic: true);

    sealed class Resolver(
        bool unsafeMember = false,
        bool throwOnSecondMember = false,
        bool throwOnDefinition = false,
        bool throwOnIndirectCall = false,
        bool returningFirst = false,
        List<string>? events = null)
        : IMethodCallResolver
    {
        public MemberRef ResolveMember(int token)
        {
            events?.Add("member");
            if (throwOnSecondMember && token == SecondToken)
                throw new BadImageFormatException(
                    "Malformed member token.");
            return new MemberRef(
                TypeRef.Definition(
                    "Fixture",
                    "Fixtures",
                    "Target"),
                "Target",
                unsafeMember
                    ? [TypeRef.Pointer(
                        TypeRef.CoreLib(
                            "System",
                            "Int32"))]
                    : [],
                returningFirst && token == FirstToken
                    ? TypeRef.CoreLib("System", "String")
                    : s_void,
                MemberKind.Method);
        }

        public MemberRef ResolveIndirectCall(int signatureToken)
        {
            if (throwOnIndirectCall)
                throw new BadImageFormatException(
                    "Malformed standalone signature.");
            return new(
                    TypeRef.Unsupported("function pointer"),
                    "calli",
                    [],
                    s_void,
                    MemberKind.FunctionPointer);
        }

        public int DefinitionToken(int operandToken)
        {
            events?.Add("definition");
            if (throwOnDefinition)
                throw new BadImageFormatException(
                    "Malformed definition token.");
            return operandToken switch
            {
                FirstToken => 0x06000001,
                SecondToken => 0x06000002,
                _ => operandToken,
            };
        }
    }
}
