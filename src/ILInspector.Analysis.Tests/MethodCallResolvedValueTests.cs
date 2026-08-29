using System.Collections.Immutable;

using ILInspector.ControlFlow;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Gates the resolved-value union, block reachability, field-store facts, and
/// the two recognized span lowerings. Every body here is hand-assembled IL so
/// the boundary between "proved" and "fails closed" is stated exactly.
/// </summary>
public sealed class MethodCallResolvedValueTests
{
    const int SinkToken = 0x0A000001;
    const int WidgetCtorToken = 0x0A000003;
    const int ProducerToken = 0x0A000004;
    const int ElementRefToken = 0x0A000005;
    const int FirstFactoryToken = 0x0A000006;
    const int SecondFactoryToken = 0x0A000007;
    const int AsSpanToken = 0x0A000008;
    const int BindToken = 0x0A000009;
    const int SpanConstructorToken = 0x0A00000A;
    const int StaticFieldToken = 0x04000001;
    const int InstanceFieldToken = 0x04000002;
    const int OpaqueFieldToken = 0x04000003;
    const int AliasedStaticFieldToken = 0x0A00000B;
    const int WidgetTypeToken = 0x01000001;
    const int AlphaStringToken = 0x70000001;

    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef s_int32 = TypeRef.CoreLib("System", "Int32");

    static readonly TypeRef s_widget =
        TypeRef.Definition("Fixture", "Fixtures", "Widget");

    static readonly TypeRef s_marshaler =
        TypeRef.Definition("Fixture", "Fixtures", "Marshaler");

    static readonly TypeRef s_span = TypeRef.GenericInstance(
        TypeRef.CoreLib("System", "ReadOnlySpan`1"),
        [s_marshaler]);

    [Fact]
    public void ResolvedValueSet_RejectsInconsistentResolutionState()
    {
        var source = new ResolvedValueSource(
            ResolvedValueSourceKind.NullReference,
            ILOffset: 0);

        Assert.Throws<ArgumentException>(
            () => new ResolvedValueSet([], isResolved: true));
        Assert.Throws<ArgumentException>(
            () => new ResolvedValueSet([source], isResolved: false));
    }

    [Fact]
    public void ResolvesArgumentValueSourceKinds()
    {
        byte[] il =
        [
            0x1F, 0x07,                         // IL_0000 ldc.i4.s 7
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0002 call Sink
            0x72, 0x01, 0x00, 0x00, 0x70,       // IL_0007 ldstr "alpha"
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_000C call Sink
            0x14,                               // IL_0011 ldnull
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0012 call Sink
            0x02,                               // IL_0017 ldarg.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0018 call Sink
            0x7E, 0x01, 0x00, 0x00, 0x04,       // IL_001D ldsfld Static
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0022 call Sink
            0x02,                               // IL_0027 ldarg.0
            0x7B, 0x02, 0x00, 0x00, 0x04,       // IL_0028 ldfld Instance
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_002D call Sink
            0xD0, 0x01, 0x00, 0x00, 0x01,       // IL_0032 ldtoken Widget
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0037 call Sink
            0x73, 0x03, 0x00, 0x00, 0x0A,       // IL_003C newobj Widget..ctor
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0041 call Sink
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0046 call Producer
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_004B call Sink
            0x2A,                               // IL_0050 ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(il, ObjectParameter());

        ResolvedValueSource literal = SingleArgument(calls, 0x0002);
        Assert.Equal(ResolvedValueSourceKind.Int32Literal, literal.Kind);
        Assert.Equal(7, literal.Int32Value);

        ResolvedValueSource text = SingleArgument(calls, 0x000C);
        Assert.Equal(ResolvedValueSourceKind.StringLiteral, text.Kind);
        Assert.Equal("alpha", text.StringValue);

        Assert.Equal(
            ResolvedValueSourceKind.NullReference,
            SingleArgument(calls, 0x0012).Kind);

        ResolvedValueSource argument = SingleArgument(calls, 0x0018);
        Assert.Equal(ResolvedValueSourceKind.Argument, argument.Kind);
        Assert.Equal(0, argument.ArgumentIndex);

        ResolvedValueSource staticField = SingleArgument(calls, 0x0022);
        Assert.Equal(
            ResolvedValueSourceKind.StaticFieldLoad,
            staticField.Kind);
        Assert.Equal("Static", staticField.Name);
        Assert.Equal(s_widget, staticField.Type);

        ResolvedValueSource instanceField = SingleArgument(calls, 0x002D);
        Assert.Equal(
            ResolvedValueSourceKind.InstanceFieldLoad,
            instanceField.Kind);
        Assert.Equal("Instance", instanceField.Name);
        Assert.Equal(0, instanceField.ArgumentIndex);

        ResolvedValueSource handle = SingleArgument(calls, 0x0037);
        Assert.Equal(ResolvedValueSourceKind.TypeHandle, handle.Kind);
        Assert.Equal(s_widget, handle.Type);

        ResolvedValueSource created = SingleArgument(calls, 0x0041);
        Assert.Equal(
            ResolvedValueSourceKind.NewObjectResult,
            created.Kind);
        Assert.Equal(0x003C, created.ILOffset);

        ResolvedValueSource result = SingleArgument(calls, 0x004B);
        Assert.Equal(ResolvedValueSourceKind.CallResult, result.Kind);
        Assert.Equal(0x0046, result.ILOffset);
        Assert.Equal("Producer", result.Name);
    }

    [Fact]
    public void ResolvesValuesThroughTransparentOperations()
    {
        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x74, 0x01, 0x00, 0x00, 0x01,       // IL_0005 castclass Widget
            0x25,                               // IL_000A dup
            0x0A,                               // IL_000B stloc.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_000C call Sink
            0x06,                               // IL_0011 ldloc.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0012 call Sink
            0x2A,                               // IL_0017 ret
        ];

        ImmutableArray<DirectCall> calls =
            Analyze(il, ObjectParameter(), [s_widget]);

        foreach (int callOffset in new[] { 0x000C, 0x0012 })
        {
            ResolvedValueSource source = SingleArgument(calls, callOffset);
            Assert.Equal(ResolvedValueSourceKind.CallResult, source.Kind);
            Assert.Equal(0x0000, source.ILOffset);
        }
    }

    [Fact]
    public void ResolvesFieldAddressValueSources()
    {
        byte[] il =
        [
            0x7F, 0x01, 0x00, 0x00, 0x04,       // IL_0000 ldsflda Static
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0005 call Sink
            0x02,                               // IL_000A ldarg.0
            0x7C, 0x02, 0x00, 0x00, 0x04,       // IL_000B ldflda Instance
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0010 call Sink
            0x2A,                               // IL_0015 ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(il, ObjectParameter());

        ResolvedValueSource staticField = SingleArgument(calls, 0x0005);
        Assert.Equal(
            ResolvedValueSourceKind.StaticFieldAddress,
            staticField.Kind);
        Assert.Equal("Static", staticField.Name);

        ResolvedValueSource instanceField =
            SingleArgument(calls, 0x0010);
        Assert.Equal(
            ResolvedValueSourceKind.InstanceFieldAddress,
            instanceField.Kind);
        Assert.Equal("Instance", instanceField.Name);
        Assert.Equal(0, instanceField.ArgumentIndex);
    }

    [Fact]
    public void LeavesAddressedLocalValuesUnresolved()
    {
        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x0A,                               // IL_0005 stloc.0
            0x12, 0x00,                         // IL_0006 ldloca.s 0
            0x26,                               // IL_0008 pop
            0x06,                               // IL_0009 ldloc.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_000A call Sink
            0x2A,                               // IL_000F ret
        ];

        ImmutableArray<DirectCall> calls =
            Analyze(il, ObjectParameter(), [s_object]);

        DirectCall sink = CallAt(calls, 0x000A);
        Assert.False(sink.ResolvedArgumentValues[0].IsResolved);
        Assert.Empty(sink.ResolvedArgumentValues[0].Sources);
    }

    [Fact]
    public void MarksCallsAfterUnconditionalBranchUnreachable()
    {
        byte[] il =
        [
            0x14,                               // IL_0000 ldnull
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0001 call Sink
            0x2B, 0x06,                         // IL_0006 br.s IL_000E
            0x14,                               // IL_0008 ldnull
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0009 call Sink
            0x2A,                               // IL_000E ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(il, ObjectParameter());

        Assert.True(CallAt(calls, 0x0001).IsReachable);
        Assert.False(CallAt(calls, 0x0009).IsReachable);
    }

    [Fact]
    public void LeavesReachabilityUnknownWithoutValueFlow()
    {
        byte[] il =
        [
            0x14,                               // IL_0000 ldnull
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0001 call Sink
            0x2A,                               // IL_0006 ret
        ];

        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        MethodCallAnalysis.Collect(
            Context(il, ObjectParameter(), []),
            new ValueResolver(),
            static _ => AllocationMultiplicity.Once,
            calls,
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: false);

        Assert.Null(CallAt(calls.ToImmutable(), 0x0001).IsReachable);
    }

    /// <summary>
    /// Gates the "original argument" half of argument provenance: a load is the
    /// caller's value only when the entry definition is the single definition
    /// reaching that exact use.
    /// </summary>
    [Fact]
    public void LeavesReassignedArgumentValuesUnresolved()
    {
        byte[] il =
        [
            0x02,                               // IL_0000 ldarg.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0001 call Sink
            0x14,                               // IL_0006 ldnull
            0x10, 0x00,                         // IL_0007 starg.s 0
            0x02,                               // IL_0009 ldarg.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_000A call Sink
            0x2A,                               // IL_000F ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(il, ObjectParameter());

        ResolvedValueSource original = SingleArgument(calls, 0x0001);
        Assert.Equal(ResolvedValueSourceKind.Argument, original.Kind);
        Assert.Equal(0, original.ArgumentIndex);
        Assert.False(
            CallAt(calls, 0x000A).ResolvedArgumentValues[0].IsResolved);
    }

    /// <summary>
    /// Gates the merged case: a load that two definitions reach is not the
    /// caller's value on every path, so it fails closed rather than picking one.
    /// </summary>
    [Fact]
    public void LeavesMergedArgumentValuesUnresolved()
    {
        byte[] il =
        [
            0x02,                               // IL_0000 ldarg.0
            0x2C, 0x03,                         // IL_0001 brfalse.s IL_0006
            0x14,                               // IL_0003 ldnull
            0x10, 0x00,                         // IL_0004 starg.s 0
            0x02,                               // IL_0006 ldarg.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0007 call Sink
            0x2A,                               // IL_000C ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(il, ObjectParameter());

        Assert.False(
            CallAt(calls, 0x0007).ResolvedArgumentValues[0].IsResolved);
    }

    /// <summary>
    /// Gates the addressed case: once the slot has been handed out by
    /// reference, an unrelated byref write cannot be ruled out without alias
    /// analysis, so neither the address nor any load of the slot resolves.
    /// </summary>
    [Fact]
    public void LeavesAddressedArgumentValuesUnresolved()
    {
        byte[] il =
        [
            0x0F, 0x00,                         // IL_0000 ldarga.s 0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0002 call Sink
            0x02,                               // IL_0007 ldarg.0
            0x28, 0x01, 0x00, 0x00, 0x0A,       // IL_0008 call Sink
            0x2A,                               // IL_000D ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(il, ObjectParameter());

        Assert.False(
            CallAt(calls, 0x0002).ResolvedArgumentValues[0].IsResolved);
        Assert.False(
            CallAt(calls, 0x0008).ResolvedArgumentValues[0].IsResolved);
    }

    /// <summary>
    /// Gates <see cref="FieldIdentity"/>: a <c>MemberRef</c> alias and the
    /// <c>FieldDef</c> it names carry different tokens for the same runtime
    /// field, so a consumer counting writes by token would see one where there
    /// are two.
    /// </summary>
    [Fact]
    public void LinksAliasedFieldAccessesByIdentity()
    {
        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x80, 0x01, 0x00, 0x00, 0x04,       // IL_0005 stsfld Static
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_000A call Producer
            0x80, 0x0B, 0x00, 0x00, 0x0A,       // IL_000F stsfld Static (alias)
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0014 call Producer
            0x80, 0x03, 0x00, 0x00, 0x04,       // IL_0019 stsfld Opaque
            0x2A,                               // IL_001E ret
        ];

        ImmutableArray<FieldStoreFact> stores =
            AnalyzeFieldStores(il, ObjectParameter());

        FieldStoreFact declared = Assert.Single(
            stores,
            store => store.ILOffset == 0x0005);
        FieldStoreFact aliased = Assert.Single(
            stores,
            store => store.ILOffset == 0x000F);
        FieldStoreFact other = Assert.Single(
            stores,
            store => store.ILOffset == 0x0019);

        Assert.NotEqual(declared.FieldToken, aliased.FieldToken);
        Assert.NotNull(declared.Identity);
        Assert.Equal(declared.Identity, aliased.Identity);
        Assert.NotEqual(declared.Identity, other.Identity);
    }

    [Fact]
    public void FieldIdentity_DistinguishesDeclaringTypeOrigins()
    {
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Fixtures",
                    ["Context"]))
            .Name;
        var assembly = new AssemblyReferenceIdentity(
            "Fixture",
            new Version(1, 0),
            null,
            null);
        TypeRef local = TypeRef.Definition(
            "Fixture",
            "Fixtures",
            "Context",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(assembly),
                typeName));
        TypeRef localAlias = TypeRef.Definition(
            "Fixture",
            "Fixtures",
            "Context",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(assembly),
                typeName));
        TypeRef externalCollision = TypeRef.Definition(
            "Fixture",
            "Fixtures",
            "Context",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(assembly),
                typeName));

        FieldIdentity expected =
            FieldIdentity.TryCreate(local, "Default")!;
        Assert.Equal(
            expected,
            FieldIdentity.TryCreate(localAlias, "Default"));
        Assert.NotEqual(
            expected,
            FieldIdentity.TryCreate(externalCollision, "Default"));
    }

    /// <summary>
    /// Gates the fail-closed half of <see cref="FieldIdentity"/>: an operand
    /// whose declaring type or name could not be resolved yields no identity at
    /// all, so a consumer cannot mistake it for a distinct field.
    /// </summary>
    [Fact]
    public void LeavesUnresolvableFieldAccessesWithoutIdentity()
    {
        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x80, 0x99, 0x00, 0x00, 0x04,       // IL_0005 stsfld <unknown>
            0x2A,                               // IL_000A ret
        ];

        Assert.Null(
            Assert.Single(AnalyzeFieldStores(il, ObjectParameter()))
                .Identity);
    }

    /// <summary>
    /// Gates the whole-body return fact on the cache shape a source generator
    /// emits: two paths merging at one <c>ret</c>, where the evaluation-stack
    /// join alone knows nothing.
    /// </summary>
    [Fact]
    public void ResolvesReturnAlternativesAcrossControlFlowMerge()
    {
        byte[] il =
        [
            0x7E, 0x01, 0x00, 0x00, 0x04,       // IL_0000 ldsfld Static
            0x25,                               // IL_0005 dup
            0x2D, 0x06,                         // IL_0006 brtrue.s IL_000E
            0x26,                               // IL_0008 pop
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0009 call Producer
            0x2A,                               // IL_000E ret
        ];

        MethodReturnFlow flow =
            AnalyzeReturnFlow(il, ObjectParameter());

        Assert.Equal([0x000E], flow.ReturnOffsets);
        Assert.True(flow.Value.IsResolved);
        Assert.Collection(
            flow.Value.Sources,
            cached =>
            {
                Assert.Equal(
                    ResolvedValueSourceKind.StaticFieldLoad,
                    cached.Kind);
                Assert.Equal(0x0000, cached.ILOffset);
            },
            fresh =>
            {
                Assert.Equal(
                    ResolvedValueSourceKind.CallResult,
                    fresh.Kind);
                Assert.Equal(0x0009, fresh.ILOffset);
            });
    }

    /// <summary>
    /// Gates failing closed: one unproven alternative leaves the whole fact
    /// unresolved rather than reporting the alternatives it did prove.
    /// </summary>
    [Fact]
    public void LeavesUnprovenReturnAlternativeUnresolved()
    {
        byte[] il =
        [
            0x7E, 0x01, 0x00, 0x00, 0x04,       // IL_0000 ldsfld Static
            0x25,                               // IL_0005 dup
            0x2D, 0x02,                         // IL_0006 brtrue.s IL_000A
            0x26,                               // IL_0008 pop
            0x06,                               // IL_0009 ldloc.0
            0x2A,                               // IL_000A ret
        ];

        MethodReturnFlow flow =
            AnalyzeReturnFlow(il, ObjectParameter(), [s_object]);

        Assert.False(flow.Value.IsResolved);
        Assert.Empty(flow.Value.Sources);
    }

    [Fact]
    public void LeavesValueChangingUnaryReturnUnresolved()
    {
        byte[] il =
        [
            0x17,                               // IL_0000 ldc.i4.1
            0x65,                               // IL_0001 neg
            0x2A,                               // IL_0002 ret
        ];

        Assert.False(AnalyzeReturnFlow(il, []).Value.IsResolved);
    }

    [Fact]
    public void LeavesReachableJumpCompletionUnresolved()
    {
        byte[] il =
        [
            0x02,                               // IL_0000 ldarg.0
            0x2D, 0x05,                         // IL_0001 brtrue.s IL_0008
            0x27, 0x04, 0x00, 0x00, 0x0A,       // IL_0003 jmp Producer
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0008 call Producer
            0x2A,                               // IL_000D ret
        ];

        MethodReturnFlow flow =
            AnalyzeReturnFlow(il, ObjectParameter());

        Assert.Equal([0x000D], flow.ReturnOffsets);
        Assert.False(flow.Value.IsResolved);
    }

    /// <summary>
    /// Gates the reachability filter: a <c>ret</c> no execution can arrive at
    /// contributes nothing and does not spoil the proven alternatives.
    /// </summary>
    [Fact]
    public void SkipsUnreachableReturnAlternatives()
    {
        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x2A,                               // IL_0005 ret
            0x14,                               // IL_0006 ldnull
            0x2A,                               // IL_0007 ret
        ];

        MethodReturnFlow flow =
            AnalyzeReturnFlow(il, ObjectParameter());

        Assert.Equal([0x0005], flow.ReturnOffsets);
        Assert.True(flow.Value.IsResolved);
        ResolvedValueSource only = Assert.Single(flow.Value.Sources);
        Assert.Equal(ResolvedValueSourceKind.CallResult, only.Kind);
    }

    /// <summary>
    /// Gates the EH boundary: catch/filter entry stacks are injected by the
    /// runtime and do not inherit values from protected-block exits.
    /// </summary>
    [Fact]
    public void LeavesExceptionHandlerEntryValueUnresolved()
    {
        byte[] il =
        [
            0x14,                               // IL_0000 ldnull
            0x7A,                               // IL_0001 throw
            0x2A,                               // IL_0002 ret (catch entry)
        ];
        ImmutableArray<DecodedInstruction> decoded =
            InstructionDecoder.Decode(il);
        var instructions = new MethodInstructions(
            decoded,
            new BlockGraph(
                [
                    new InstructionBlock(
                        0,
                        0,
                        1,
                        new BlockEdges(
                            [1, 2],
                            [],
                            ExitsMethod: false,
                            LeavesRegion: false)),
                    new InstructionBlock(
                        1,
                        1,
                        2,
                        new BlockEdges(
                            [],
                            [],
                            ExitsMethod: true,
                            LeavesRegion: false)),
                    new InstructionBlock(
                        2,
                        2,
                        3,
                        new BlockEdges(
                            [],
                            [],
                            ExitsMethod: true,
                            LeavesRegion: false)),
                ],
                [
                    new ExceptionRegionModel(
                        HandlerKind.Catch,
                        TryStart: 0,
                        TryEnd: 1,
                        HandlerStart: 2,
                        HandlerEnd: 3,
                        FilterStart: -1,
                        FilterEnd: -1),
                ],
                IsComplete: true,
                IncompleteReason: null));
        var returnFlows =
            ImmutableArray.CreateBuilder<MethodReturnFlow>();

        MethodCallAnalysis.Collect(
            Context(instructions, ObjectParameter(), [], s_object),
            new ValueResolver(),
            static _ => AllocationMultiplicity.Once,
            ImmutableArray.CreateBuilder<DirectCall>(),
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: true,
            resultSinks: ImmutableArray.CreateBuilder<MethodResultSink>(),
            fieldStores: ImmutableArray.CreateBuilder<FieldStoreFact>(),
            fieldLoads: ImmutableArray.CreateBuilder<FieldLoadFact>(),
            returnFlows: returnFlows);

        MethodReturnFlow flow = Assert.Single(returnFlows);
        Assert.Equal([0x0002], flow.ReturnOffsets);
        Assert.False(flow.Value.IsResolved);
        Assert.Empty(flow.Value.Sources);
    }

    /// <summary>
    /// Gates the void non-action boundary: a void body has no returned value to
    /// describe, so no fact is emitted at all.
    /// </summary>
    [Fact]
    public void OmitsReturnFlowForVoidBodies()
    {
        byte[] il =
        [
            0x2A,                               // IL_0000 ret
        ];

        var returnFlows =
            ImmutableArray.CreateBuilder<MethodReturnFlow>();
        MethodCallAnalysis.Collect(
            Context(il, ObjectParameter(), []),
            new ValueResolver(),
            static _ => AllocationMultiplicity.Once,
            ImmutableArray.CreateBuilder<DirectCall>(),
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: true,
            returnFlows: returnFlows);

        Assert.Empty(returnFlows);
    }

    [Fact]
    public void CollectsReturnFlowWithoutResultSinkBuilder()
    {
        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x2A,                               // IL_0005 ret
        ];
        var returnFlows =
            ImmutableArray.CreateBuilder<MethodReturnFlow>();

        MethodCallAnalysis.Collect(
            Context(il, [], [], returnType: s_object),
            new ValueResolver(),
            static _ => AllocationMultiplicity.Once,
            ImmutableArray.CreateBuilder<DirectCall>(),
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: true,
            returnFlows: returnFlows);

        Assert.True(Assert.Single(returnFlows).Value.IsResolved);
    }

    [Fact]
    public void DoesNotCreditCallThatCanBeSkippedByNormalReturn()
    {
        byte[] il =
        [
            0x02,                               // IL_0000 ldarg.0
            0x2D, 0x01,                         // IL_0001 brtrue.s IL_0004
            0x2A,                               // IL_0003 ret
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0004 call Producer
            0x26,                               // IL_0009 pop
            0x2A,                               // IL_000A ret
        ];

        DirectCall call = Assert.Single(
            Analyze(il, ObjectParameter()));

        Assert.True(call.IsReachable);
        Assert.False(call.DominatesEveryNormalReturn);
    }

    [Fact]
    public void CreditsCallThatDominatesEveryNormalReturn()
    {
        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x26,                               // IL_0005 pop
            0x2A,                               // IL_0006 ret
        ];

        Assert.True(
            Assert.Single(Analyze(il, []))
                .DominatesEveryNormalReturn);
    }

    [Fact]
    public void CollectsFieldStoreFacts()
    {        byte[] il =
        [
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0000 call Producer
            0x80, 0x01, 0x00, 0x00, 0x04,       // IL_0005 stsfld Static
            0x02,                               // IL_000A ldarg.0
            0x02,                               // IL_000B ldarg.0
            0x7D, 0x02, 0x00, 0x00, 0x04,       // IL_000C stfld Instance
            0xFE, 0x1C, 0x01, 0x00, 0x00, 0x01, // IL_0011 sizeof Widget
            0x80, 0x03, 0x00, 0x00, 0x04,       // IL_0017 stsfld Opaque
            0x2A,                               // IL_001C ret
        ];

        ImmutableArray<FieldStoreFact> stores =
            AnalyzeFieldStores(il, ObjectParameter());

        Assert.Collection(
            stores,
            store =>
            {
                Assert.True(store.IsStatic);
                Assert.Equal("Static", store.FieldName);
                Assert.Equal(s_widget, store.DeclaringType);
                Assert.Equal(-1, store.ReceiverArgumentIndex);
                Assert.True(store.IsReachable);
                Assert.Equal(
                    ResolvedValueSourceKind.CallResult,
                    Assert.Single(store.Value.Sources).Kind);
            },
            store =>
            {
                Assert.False(store.IsStatic);
                Assert.Equal("Instance", store.FieldName);
                Assert.Equal(0, store.ReceiverArgumentIndex);
                Assert.Equal(
                    ResolvedValueSourceKind.Argument,
                    Assert.Single(store.Value.Sources).Kind);
            },
            store =>
            {
                Assert.Equal("Opaque", store.FieldName);
                Assert.False(store.Value.IsResolved);
                Assert.Empty(store.Value.Sources);
            });
    }

    [Fact]
    public void CollectsFieldLoadFacts()
    {
        byte[] il =
        [
            0x7E, 0x01, 0x00, 0x00, 0x04,       // IL_0000 ldsfld Static
            0x26,                               // IL_0005 pop
            0x02,                               // IL_0006 ldarg.0
            0x7B, 0x02, 0x00, 0x00, 0x04,       // IL_0007 ldfld Instance
            0x26,                               // IL_000C pop
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_000D call Producer
            0x7B, 0x02, 0x00, 0x00, 0x04,       // IL_0012 ldfld Instance
            0x26,                               // IL_0017 pop
            0x7F, 0x01, 0x00, 0x00, 0x04,       // IL_0018 ldsflda Static
            0x26,                               // IL_001D pop
            0x02,                               // IL_001E ldarg.0
            0x7C, 0x02, 0x00, 0x00, 0x04,       // IL_001F ldflda Instance
            0x26,                               // IL_0024 pop
            0x2A,                               // IL_0025 ret
        ];

        ImmutableArray<FieldLoadFact> loads =
            AnalyzeFieldLoads(il, ObjectParameter());

        Assert.Collection(
            loads,
            load =>
            {
                Assert.True(load.IsStatic);
                Assert.False(load.IsAddress);
                Assert.Equal("Static", load.FieldName);
                Assert.Equal(s_widget, load.DeclaringType);
                Assert.Equal(-1, load.ReceiverArgumentIndex);
                Assert.True(load.IsReachable);
            },
            load =>
            {
                Assert.False(load.IsStatic);
                Assert.False(load.IsAddress);
                Assert.Equal("Instance", load.FieldName);
                Assert.Equal(0, load.ReceiverArgumentIndex);
            },
            load =>
            {
                // A call-result receiver is not an argument slot, so the
                // receiver stays unattributed rather than being guessed.
                Assert.Equal("Instance", load.FieldName);
                Assert.Equal(-1, load.ReceiverArgumentIndex);
                Assert.False(load.IsAddress);
            },
            load =>
            {
                Assert.True(load.IsStatic);
                Assert.True(load.IsAddress);
                Assert.Equal("Static", load.FieldName);
            },
            load =>
            {
                Assert.False(load.IsStatic);
                Assert.True(load.IsAddress);
                Assert.Equal("Instance", load.FieldName);
                Assert.Equal(0, load.ReceiverArgumentIndex);
            });
    }

    [Fact]
    public void ResolvesResultSinkValues()
    {
        byte[] resolved =
        [
            0x7E, 0x01, 0x00, 0x00, 0x04,       // IL_0000 ldsfld Static
            0x2A,                               // IL_0005 ret
        ];

        MethodResultSink sink = Assert.Single(
            AnalyzeResultSinks(resolved, []),
            candidate => candidate.Kind
                == MethodResultSinkKind.MethodReturn);
        Assert.Equal(
            ResolvedValueSourceKind.StaticFieldLoad,
            Assert.Single(sink.ResolvedValue!.Sources).Kind);

        // The call-only completeness pair keeps its existing meaning: a field
        // load is not a direct call result, so it stays incomplete even though
        // the new union proves it.
        Assert.False(sink.IsComplete);
        Assert.Empty(sink.SourceCallOffsets);
    }

    [Fact]
    public void LeavesMergedResultSinkValuesUnresolved()
    {
        byte[] il =
        [
            0x02,                               // IL_0000 ldarg.0
            0x25,                               // IL_0001 dup
            0x2D, 0x06,                         // IL_0002 brtrue.s IL_000A
            0x26,                               // IL_0004 pop
            0x28, 0x04, 0x00, 0x00, 0x0A,       // IL_0005 call Producer
            0x2A,                               // IL_000A ret
        ];

        MethodResultSink sink = Assert.Single(
            AnalyzeResultSinks(il, ObjectParameter()),
            candidate => candidate.Kind
                == MethodResultSinkKind.MethodReturn);
        Assert.False(sink.ResolvedValue!.IsResolved);
    }

    [Fact]
    public void ResolvesInlineArraySpanArgumentElements()
    {
        ImmutableArray<DirectCall> calls = Analyze(
            InlineArraySpanIl(),
            [],
            [TrustedInlineArrayBuffer()]);

        SpanArgumentElements span = Assert.Single(
            CallAt(calls, 0x002C).SpanArgumentSources);
        Assert.True(span.IsResolved);
        Assert.Equal(0, span.ArgumentIndex);
        Assert.Equal(2, span.Elements.Count);
        Assert.Equal(
            "FirstFactory",
            Assert.Single(span.Elements[0].Sources).Name);
        Assert.Equal(
            "SecondFactory",
            Assert.Single(span.Elements[1].Sources).Name);
    }

    [Fact]
    public void RejectsInlineArraySpanWithUntrustedBufferType()
    {
        ImmutableArray<DirectCall> calls = Analyze(
            InlineArraySpanIl(),
            [],
            [
                TypeRef.GenericInstance(
                    TypeRef.Definition(
                        "Fixture",
                        "System.Runtime.CompilerServices",
                        "InlineArray2`1"),
                    [s_marshaler]),
            ]);

        Assert.False(
            Assert.Single(CallAt(calls, 0x002C).SpanArgumentSources)
                .IsResolved);
    }

    [Fact]
    public void RejectsInlineArraySpanWithExtraBufferAddressUse()
    {
        byte[] il = [.. InlineArraySpanIl()];
        // IL_0000 ldloca.s 0 / initobj becomes ldloca.s 0 / pop, which leaves
        // the buffer uninitialized and adds an address use the lowering never
        // emits. The trailing bytes stay decodable as nop.
        il[2] = 0x26;
        il[3] = 0x00;
        il[4] = 0x00;
        il[5] = 0x00;
        il[6] = 0x00;
        il[7] = 0x00;

        ImmutableArray<DirectCall> calls =
            Analyze(il, [], [TrustedInlineArrayBuffer()]);

        Assert.False(
            Assert.Single(CallAt(calls, 0x002C).SpanArgumentSources)
                .IsResolved);
    }

    [Fact]
    public void ResolvesSingleElementSpanArgumentElements()
    {
        byte[] il =
        [
            0x28, 0x06, 0x00, 0x00, 0x0A,       // IL_0000 call FirstFactory
            0x13, 0x00,                         // IL_0005 stloc.s 0
            0x12, 0x00,                         // IL_0007 ldloca.s 0
            0x73, 0x0A, 0x00, 0x00, 0x0A,       // IL_0009 newobj Span..ctor
            0x28, 0x09, 0x00, 0x00, 0x0A,       // IL_000E call Bind
            0x2A,                               // IL_0013 ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(il, [], [s_marshaler]);

        SpanArgumentElements span = Assert.Single(
            CallAt(calls, 0x000E).SpanArgumentSources);
        Assert.True(span.IsResolved);
        Assert.Equal(
            "FirstFactory",
            Assert.Single(Assert.Single(span.Elements).Sources).Name);
    }

    [Fact]
    public void RecordsNewObjectArgumentProvenance()
    {
        byte[] il =
        [
            0x1F, 0x2A,                         // IL_0000 ldc.i4.s 42
            0x73, 0x03, 0x00, 0x00, 0x0A,       // IL_0002 newobj Widget..ctor
            0x26,                               // IL_0007 pop
            0x2A,                               // IL_0008 ret
        ];

        ImmutableArray<DirectCall> calls = Analyze(
            il,
            [],
            [],
            widgetConstructorParameters: [s_int32]);

        DirectCall created = CallAt(calls, 0x0002);
        Assert.Equal(CallKind.NewObject, created.Kind);
        ResolvedValueSource source =
            Assert.Single(created.ResolvedArgumentValues[0].Sources);
        Assert.Equal(ResolvedValueSourceKind.Int32Literal, source.Kind);
        Assert.Equal(42, source.Int32Value);
        Assert.Null(created.ResolvedReceiverValue);
        Assert.Equal(
            0,
            Assert.Single(created.ArgumentSources).ArgumentIndex);
    }

    // The IL the C# compiler emits for a two-element collection-expression
    // span argument: a zero-initialized corelib inline-array buffer, one
    // element reference and reference store per index, then AsReadOnlySpan.
    static byte[] InlineArraySpanIl() =>
    [
        0x12, 0x00,                         // IL_0000 ldloca.s 0
        0xFE, 0x15, 0x01, 0x00, 0x00, 0x01, // IL_0002 initobj Buffer
        0x12, 0x00,                         // IL_0008 ldloca.s 0
        0x16,                               // IL_000A ldc.i4.0
        0x28, 0x05, 0x00, 0x00, 0x0A,       // IL_000B call ElementRef
        0x28, 0x06, 0x00, 0x00, 0x0A,       // IL_0010 call FirstFactory
        0x51,                               // IL_0015 stind.ref
        0x12, 0x00,                         // IL_0016 ldloca.s 0
        0x17,                               // IL_0018 ldc.i4.1
        0x28, 0x05, 0x00, 0x00, 0x0A,       // IL_0019 call ElementRef
        0x28, 0x07, 0x00, 0x00, 0x0A,       // IL_001E call SecondFactory
        0x51,                               // IL_0023 stind.ref
        0x12, 0x00,                         // IL_0024 ldloca.s 0
        0x18,                               // IL_0026 ldc.i4.2
        0x28, 0x08, 0x00, 0x00, 0x0A,       // IL_0027 call AsReadOnlySpan
        0x28, 0x09, 0x00, 0x00, 0x0A,       // IL_002C call Bind
        0x2A,                               // IL_0031 ret
    ];

    static TypeRef TrustedInlineArrayBuffer()
        => TypeRef.GenericInstance(
            TypeRef.CoreLib(
                "System.Runtime.CompilerServices",
                "InlineArray2`1"),
            [s_marshaler]);

    static ImmutableArray<TypeRef> ObjectParameter() => [s_object];

    static ResolvedValueSource SingleArgument(
        ImmutableArray<DirectCall> calls,
        int callOffset)
    {
        ResolvedValueSet value =
            CallAt(calls, callOffset).ResolvedArgumentValues[0];
        Assert.True(value.IsResolved);
        return Assert.Single(value.Sources);
    }

    static DirectCall CallAt(
        ImmutableArray<DirectCall> calls,
        int offset)
        => Assert.Single(calls.Where(call => call.ILOffset == offset));

    static ImmutableArray<DirectCall> Analyze(
        byte[] il,
        ImmutableArray<TypeRef> parameters,
        ImmutableArray<TypeRef> locals = default,
        ImmutableArray<TypeRef> widgetConstructorParameters = default)
    {
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        Collect(
            il,
            parameters,
            locals.IsDefault ? [] : locals,
            widgetConstructorParameters.IsDefault
                ? []
                : widgetConstructorParameters,
            calls,
            ImmutableArray.CreateBuilder<FieldStoreFact>(),
            ImmutableArray.CreateBuilder<FieldLoadFact>(),
            ImmutableArray.CreateBuilder<MethodResultSink>());
        return calls.ToImmutable();
    }

    static ImmutableArray<FieldStoreFact> AnalyzeFieldStores(
        byte[] il,
        ImmutableArray<TypeRef> parameters)
    {
        var fieldStores = ImmutableArray.CreateBuilder<FieldStoreFact>();
        Collect(
            il,
            parameters,
            [],
            [],
            ImmutableArray.CreateBuilder<DirectCall>(),
            fieldStores,
            ImmutableArray.CreateBuilder<FieldLoadFact>(),
            ImmutableArray.CreateBuilder<MethodResultSink>());
        return fieldStores.ToImmutable();
    }

    static ImmutableArray<FieldLoadFact> AnalyzeFieldLoads(
        byte[] il,
        ImmutableArray<TypeRef> parameters)
    {
        var fieldLoads = ImmutableArray.CreateBuilder<FieldLoadFact>();
        Collect(
            il,
            parameters,
            [],
            [],
            ImmutableArray.CreateBuilder<DirectCall>(),
            ImmutableArray.CreateBuilder<FieldStoreFact>(),
            fieldLoads,
            ImmutableArray.CreateBuilder<MethodResultSink>());
        return fieldLoads.ToImmutable();
    }

    static MethodReturnFlow AnalyzeReturnFlow(
        byte[] il,
        ImmutableArray<TypeRef> parameters,
        ImmutableArray<TypeRef> locals = default)
    {
        var returnFlows =
            ImmutableArray.CreateBuilder<MethodReturnFlow>();
        MethodCallAnalysis.Collect(
            Context(
                il,
                parameters,
                locals.IsDefault ? [] : locals,
                returnType: s_object),
            new ValueResolver(),
            static _ => AllocationMultiplicity.Once,
            ImmutableArray.CreateBuilder<DirectCall>(),
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: true,
            resultSinks: ImmutableArray.CreateBuilder<MethodResultSink>(),
            fieldStores: ImmutableArray.CreateBuilder<FieldStoreFact>(),
            fieldLoads: ImmutableArray.CreateBuilder<FieldLoadFact>(),
            returnFlows: returnFlows);
        return Assert.Single(returnFlows.ToImmutable());
    }

    static ImmutableArray<MethodResultSink> AnalyzeResultSinks(
        byte[] il,
        ImmutableArray<TypeRef> parameters,
        ImmutableArray<TypeRef> locals = default)
    {
        var sinks = ImmutableArray.CreateBuilder<MethodResultSink>();
        Collect(
            il,
            parameters,
            locals.IsDefault ? [] : locals,
            [],
            ImmutableArray.CreateBuilder<DirectCall>(),
            ImmutableArray.CreateBuilder<FieldStoreFact>(),
            ImmutableArray.CreateBuilder<FieldLoadFact>(),
            sinks);
        return sinks.ToImmutable();
    }

    static void Collect(
        byte[] il,
        ImmutableArray<TypeRef> parameters,
        ImmutableArray<TypeRef> locals,
        ImmutableArray<TypeRef> widgetConstructorParameters,
        ImmutableArray<DirectCall>.Builder calls,
        ImmutableArray<FieldStoreFact>.Builder fieldStores,
        ImmutableArray<FieldLoadFact>.Builder fieldLoads,
        ImmutableArray<MethodResultSink>.Builder resultSinks)
        => MethodCallAnalysis.Collect(
            Context(il, parameters, locals),
            new ValueResolver(widgetConstructorParameters),
            static _ => AllocationMultiplicity.Once,
            calls,
            ImmutableArray.CreateBuilder<UnsafeEvidence>(),
            includeIndirectOpcodes: false,
            includeCallValueFlow: true,
            resultSinks: resultSinks,
            fieldStores: fieldStores,
            fieldLoads: fieldLoads);

    static MethodBodyAnalysisContext Context(
        byte[] il,
        ImmutableArray<TypeRef> parameters,
        ImmutableArray<TypeRef> locals,
        TypeRef? returnType = null)
    {
        MethodInstructions instructions =
            MethodInstructions.Decode(il, il.Length, []);
        Assert.True(instructions.IsComplete);
        return Context(instructions, parameters, locals, returnType);
    }

    static MethodBodyAnalysisContext Context(
        MethodInstructions instructions,
        ImmutableArray<TypeRef> parameters,
        ImmutableArray<TypeRef> locals,
        TypeRef? returnType = null)
    {
        return new MethodBodyAnalysisContext(
            new MethodIdentity(
                "Fixture",
                Guid.Empty,
                TypeRef.Definition("Fixture", "Fixtures", "Caller"),
                "M",
                parameters,
                returnType ?? s_void,
                MetadataToken: 0x06000001,
                IsStatic: true),
            instructions,
            [],
            [],
            locals);
    }

    sealed class ValueResolver(
        ImmutableArray<TypeRef> widgetConstructorParameters = default)
        : IMethodCallResolver
    {
        readonly ImmutableArray<TypeRef> _widgetConstructorParameters =
            widgetConstructorParameters.IsDefault
                ? []
                : widgetConstructorParameters;

        public MemberRef ResolveMember(int token) => token switch
        {
            SinkToken => Static("Sink", [s_object], s_void),
            WidgetCtorToken => new MemberRef(
                s_widget,
                ".ctor",
                _widgetConstructorParameters,
                s_void,
                MemberKind.Method)
            {
                HasThis = true,
            },
            ProducerToken => Static("Producer", [], s_object),
            ElementRefToken => new MemberRef(
                TypeRef.Definition(
                    "Fixture",
                    "",
                    "<PrivateImplementationDetails>"),
                "InlineArrayElementRef",
                [TypeRef.ByRef(s_object), s_int32],
                TypeRef.ByRef(s_marshaler),
                MemberKind.Method),
            FirstFactoryToken => Static("FirstFactory", [], s_marshaler),
            SecondFactoryToken => Static("SecondFactory", [], s_marshaler),
            AsSpanToken => new MemberRef(
                TypeRef.Definition(
                    "Fixture",
                    "",
                    "<PrivateImplementationDetails>"),
                "InlineArrayAsReadOnlySpan",
                [TypeRef.ByRef(s_object), s_int32],
                s_span,
                MemberKind.Method),
            BindToken => Static("Bind", [s_span], s_void),
            SpanConstructorToken => new MemberRef(
                s_span,
                ".ctor",
                [TypeRef.ByRef(s_marshaler)],
                s_void,
                MemberKind.Method)
            {
                HasThis = true,
            },
            _ => Static("Unknown", [], s_void),
        };

        static MemberRef Static(
            string name,
            ImmutableArray<TypeRef> parameters,
            TypeRef returnType)
            => new(
                TypeRef.Definition("Fixture", "Fixtures", "Target"),
                name,
                parameters,
                returnType,
                MemberKind.Method);

        public MemberRef ResolveIndirectCall(int signatureToken)
            => new(
                TypeRef.Unsupported("function pointer"),
                "calli",
                [],
                s_void,
                MemberKind.FunctionPointer);

        public int DefinitionToken(int operandToken) => operandToken;

        public string? ResolveUserString(int token)
            => token == AlphaStringToken ? "alpha" : null;

        public TypeRef ResolveType(int token)
            => token == WidgetTypeToken
                ? s_widget
                : TypeRef.Unsupported("type token");

        public (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(
            int fieldToken)
            => fieldToken switch
            {
                StaticFieldToken => (s_widget, "Static"),
                AliasedStaticFieldToken => (s_widget, "Static"),
                InstanceFieldToken => (s_widget, "Instance"),
                OpaqueFieldToken => (s_widget, "Opaque"),
                _ => (null, null),
            };
    }
}
