using System.Collections.Immutable;
using System.Reflection;
using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class GenericSlotMaterializationTests
{
    [Theory]
    [InlineData(false, false, GenericParameterAttributes.None)]
    [InlineData(false, true, GenericParameterAttributes.ReferenceTypeConstraint)]
    [InlineData(false, false, GenericParameterAttributes.NotNullableValueTypeConstraint)]
    [InlineData(true, false, GenericParameterAttributes.None)]
    [InlineData(true, true, GenericParameterAttributes.ReferenceTypeConstraint)]
    [InlineData(true, false, GenericParameterAttributes.NotNullableValueTypeConstraint)]
    public void ExactGenericStoragePreservesProducerAndIdentity(
        bool methodParameter, bool untypedLoad, GenericParameterAttributes attributes)
    {
        var type = ParameterType(methodParameter);
        var producer = new DefaultValue(type);
        var function = GenericFunction(methodParameter, type, attributes,
            new StoreStackSlot(0, producer),
            new Return(new LoadStackSlot(0, untypedLoad ? null : type)));

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal(type, Assert.Single(function.Locals));
        Assert.Same(producer, Assert.Single(function.Descendants.OfType<StoreLocal>()).Value);
        Assert.Equal(type, Assert.Single(function.Descendants.OfType<LoadLocal>()).ResultType);
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false, "ref-like")]
    [InlineData(true, "ref-like")]
    [InlineData(false, "missing-facts")]
    [InlineData(true, "missing-facts")]
    [InlineData(false, "wrong-index")]
    [InlineData(true, "wrong-index")]
    [InlineData(false, "wrong-name")]
    [InlineData(true, "wrong-name")]
    [InlineData(false, "shadowed")]
    public void UnknownOrUnspellableGenericStorageRemainsDeferred(bool methodParameter, string boundary)
    {
        var type = boundary switch
        {
            "wrong-index" => methodParameter
                ? TypeRef.MethodGenericParameter(1, "T") : TypeRef.GenericParameter(1, "T"),
            "wrong-name" => methodParameter
                ? TypeRef.MethodGenericParameter(0, "U") : TypeRef.GenericParameter(0, "U"),
            _ => ParameterType(methodParameter),
        };
        var function = GenericFunction(methodParameter, type,
            boundary == "ref-like" ? GenericParameterAttributes.AllowByRefLike : GenericParameterAttributes.None,
            new StoreStackSlot(0, new DefaultValue(type)),
            new Return(new LoadStackSlot(0, type)),
            includeFacts: boundary != "missing-facts",
            shadowTypeParameter: boundary == "shadowed");

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Fact]
    public void EqualNamesDoNotMergeTypeAndMethodParameters()
    {
        var type = TypeRef.MethodGenericParameter(0, "T");
        var function = GenericFunction(true, type, GenericParameterAttributes.None,
            new StoreStackSlot(0, new DefaultValue(TypeRef.GenericParameter(0, "T"))),
            new Return(new LoadStackSlot(0, type)));
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes
            .HasFlag(SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetained(function);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericCopyComponentsRemainAtomic(bool incomplete)
    {
        var type = ParameterType(true);
        var function = GenericFunction(true, type, GenericParameterAttributes.None,
            new StoreStackSlot(0, new DefaultValue(type)),
            new StoreStackSlot(1, new LoadStackSlot(0, type)),
            incomplete ? new ExpressionStatement(new LoadStackSlot(1, null))
                : new Return(new LoadStackSlot(1, type)));
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision =>
                Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.IncompleteCopyComponent)));
            AssertRetained(function);
            return;
        }

        Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Equal([type, type], function.Locals);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void GenericBoxPreservesItsTypedOperand()
    {
        var type = ParameterType(true);
        var box = new Box(type, new LoadStackSlot(0, type));
        var function = GenericFunction(true, TypeRef.CoreLib("System", "Object"), GenericParameterAttributes.None,
            new StoreStackSlot(0, new DefaultValue(type)),
            new Return(box));
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.Same(box, Assert.Single(function.Descendants.OfType<Box>()));
        Assert.Equal(type, Assert.IsType<LoadLocal>(box.Operand).ResultType);
        Assert.Equal(type, box.Type);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(GenericSlotMaterializationSamples.ReadReference))]
    [InlineData(nameof(GenericSlotMaterializationSamples.ReadValue))]
    [InlineData(nameof(GenericSlotMaterializationSamples.BoxMethod))]
    [InlineData(nameof(GenericSlotMaterializationSamples.CopyThenReplace))]
    public void CompilerProducedMethodGenericStorageMaterializes(string method)
        => AssertCompiledStorage(typeof(GenericSlotMaterializationSamples), method, materializes: true);

    [Fact]
    public void CompilerProducedUnconstrainedMethodStorageMaterializes()
        => AssertCompiledStorage(typeof(NamedReferenceSlotMaterializationSamples),
            nameof(NamedReferenceSlotMaterializationSamples.ReadUnconstrained), materializes: true);

    [Fact]
    public void CompilerProducedTypeGenericStorageMaterializes()
        => AssertCompiledStorage(typeof(GenericTypeStorageSamples<>), "ReadType", materializes: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerProducedRefLikeGenericStorageRemainsDeferred(bool typeParameter)
        => AssertCompiledStorage(typeParameter ? typeof(RefLikeGenericTypeStorageSamples<>)
            : typeof(GenericSlotMaterializationSamples),
            typeParameter ? "CopyThenReplace" : nameof(GenericSlotMaterializationSamples.CopyAllowsRefStruct),
            materializes: false);

    [Fact]
    public void RealRoslynGenericStorageMaterializes()
    {
        string path = typeof(SyntaxTree).Assembly.Location;
        AssertCompiledStorage(path, "Microsoft.CodeAnalysis.PooledObjects.ArrayBuilder`1", "Pop", materializes: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task CompilerProducedGenericStorageRecompilesExactly()
    {
        string[] methods =
        [
            nameof(GenericSlotMaterializationSamples.ReadReference),
            nameof(GenericSlotMaterializationSamples.ReadValue),
            nameof(GenericSlotMaterializationSamples.BoxMethod),
            nameof(GenericSlotMaterializationSamples.CopyThenReplace),
        ];
        var targets = methods.Select(method => new ReturnToSender.RequestedTarget(
            typeof(GenericSlotMaterializationSamples).FullName!, method, 0))
            .Append(new(typeof(NamedReferenceSlotMaterializationSamples).FullName!,
                nameof(NamedReferenceSlotMaterializationSamples.ReadUnconstrained), 0))
            .Append(new(typeof(GenericTypeStorageSamples<>).FullName!, "ReadType", 0)).ToArray();
        var results = await ReturnToSender.CompileBackTargets(
            typeof(GenericSlotMaterializationSamples).Assembly.Location, targets,
            sourceIndex: null, applyCompileBackFloor: false);
        Assert.Equal(targets.Length, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.UsedCompileBackFloor);
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
        });
    }

    static void AssertCompiledStorage(Type owner, string method, bool materializes)
        => AssertCompiledStorage(owner.Assembly.Location, owner.FullName!, method, materializes);

    static void AssertCompiledStorage(string path, string owner, string method, bool materializes)
    {
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, owner, method);
        var decisions = SlotMaterializationPass.Analyze(function).Where(decision =>
            decision.Type?.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter).ToArray();
        Assert.NotEmpty(decisions);
        Assert.All(decisions, decision => Assert.Equal(materializes, decision.WillMaterialize));
        var boxes = function.Descendants.OfType<Box>().ToArray();
        var invariant = SlotMaterializationInvariant.Capture(function);
        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();
        Assert.All(decisions, decision =>
        {
            if (materializes)
                Assert.Contains(decision.Type!, function.Locals);
            else
                Assert.Contains(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == decision.Slot);
        });
        Assert.All(boxes, box => Assert.Equal(box.Type, box.Operand.ResultType));
        function.CheckInvariant(includeSemantics: true);
    }

    static TypeRef ParameterType(bool method)
        => method ? TypeRef.MethodGenericParameter(0, "T") : TypeRef.GenericParameter(0, "T");

    static IrFunction GenericFunction(
        bool methodParameter, TypeRef returnType, GenericParameterAttributes attributes,
        IrNode first, IrNode second, IrNode? third = null,
        bool includeFacts = true, bool shadowTypeParameter = false)
    {
        ImmutableArray<GenericParameterConstraintInfo> parameters = includeFacts
            ? [new(0, attributes, [])] : [];
        var signature = new MethodSignature(returnType, [], false, methodParameter || shadowTypeParameter ? 1 : 0)
        {
            GenericParameterNames = methodParameter || shadowTypeParameter ? ["T"] : [],
            GenericParameters = methodParameter ? parameters : [],
        };
        var block = new Block(0);
        block.Add(first);
        block.Add(second);
        if (third is not null)
            block.Add(third);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction("M", Owner, signature, [], body)
        {
            DeclaringTypeGenericParameterNames = methodParameter ? [] : ["T"],
            DeclaringTypeParameters = methodParameter ? [] : parameters,
        };
    }
}
