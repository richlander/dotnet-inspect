using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ReferenceCoalesceBindingTests
{
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");

    [Theory]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.SameType), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.SameType), true)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ObjectRight), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ObjectRight), true)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ObjectLeft), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ObjectLeft), true)]
    public void CompilerProducedCoalescesCarryAssignmentTestimony(string method, bool lowered)
    {
        var function = Raise(method, lowered);
        var coalesce = Assert.Single(function.Descendants.OfType<Coalesce>());
        Assert.Equal(method == nameof(ReferenceCoalesceBindingSamples.SameType) ? StringType : ObjectType,
            coalesce.AssignmentType);
        Assert.Contains("??", CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ObjectOverload), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ObjectOverload), true)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.SpilledObjectOverload), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.SpilledObjectOverload), true)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ConstructorOverload), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.ConstructorOverload), true)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.NestedObjectOverload), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.NestedObjectOverload), true)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.LocalFunctionObjectOverload), false)]
    [InlineData(nameof(ReferenceCoalesceBindingSamples.LocalFunctionObjectOverload), true)]
    public void OverloadedArgumentsKeepObjectBinding(string method, bool lowered)
    {
        var function = Raise(method, lowered);
        var witnesses = function.Descendants.OfType<Coerce>()
            .Where(coerce => coerce.Kind == CoercionKind.ReferenceWitness).ToArray();
        Assert.NotEmpty(witnesses);
        Assert.All(witnesses, witness =>
        {
            Assert.Equal(ObjectType, witness.Target);
            Assert.IsType<Coalesce>(witness.Operand);
        });
        Assert.Contains("(object)", CSharpPrinter.Print(function).Output);
        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);
        Assert.Equal(witnesses.Length, function.Descendants.OfType<Coerce>()
            .Count(coerce => coerce.Kind == CoercionKind.ReferenceWitness));
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullArmUsesTheOtherProvenReference(bool leftNull)
    {
        IrExpression text = new LoadArgument(0, "text", StringType);
        IrExpression nil = new Constant(null, ObjectType);
        var coalesce = leftNull ? new Coalesce(nil, text) : new Coalesce(text, nil);
        var function = Function(StringType, new Return(coalesce));

        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.Equal(StringType, coalesce.AssignmentType);
        Assert.Equal(StringType, Assert.IsType<Coalesce>(coalesce.Clone()).AssignmentType);
    }

    [Fact]
    public void PublishedAnnotationShapeDoesNotMaterializeItsSeparateUntypedNull()
    {
        var coalesce = new Coalesce(new LoadArgument(0, "format", StringType),
            new Constant("annotation", StringType));
        var function = Function(StringType,
            new StoreStackSlot(0, coalesce),
            new StoreStackSlot(0, new Constant(null, ObjectType)),
            new Return(new LoadStackSlot(0, StringType)));

        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.Equal(StringType, coalesce.AssignmentType);
        Assert.False(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        Assert.Contains("string S_0", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void ReferenceWideningDoesNotExpandExistingStorageAdmission()
    {
        var coalesce = new Coalesce(new LoadArgument(0, "text", StringType),
            new LoadArgument(1, "fallback", ObjectType));
        var function = Function(ObjectType,
            new StoreStackSlot(0, coalesce),
            new Return(new LoadStackSlot(0, ObjectType)));
        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.False(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal(ObjectType, coalesce.AssignmentType);
        Assert.Empty(function.Locals);
        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Contains("object S_0", CSharpPrinter.Print(function).Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingReferenceEvidenceDoesNotAuthorizeAssignment(bool differentTypes)
    {
        var left = TypeRef.Definition("Foreign", "Samples", "Left");
        var right = differentTypes ? TypeRef.Definition("Foreign", "Samples", "Right") : left;
        var coalesce = new Coalesce(new LoadArgument(0, "left", left),
            new LoadArgument(1, "right", right));
        var function = Function(left, new Return(coalesce));
        if (differentTypes)
            function.TypeShapes = new Dictionary<TypeRef, TypeShape>
            {
                [left] = TypeShape.Reference,
                [right] = TypeShape.Reference,
            };

        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.Null(coalesce.AssignmentType);
        Assert.Empty(function.Descendants.OfType<Coerce>());
    }

    [Fact]
    public void BothNullArmsRemainUndecided()
    {
        var coalesce = new Coalesce(new Constant(null, ObjectType), new Constant(null, ObjectType));
        Assert.Null(coalesce.AssignmentType);
    }

    [Fact]
    public void MetadataShapeBindingFlowsThroughNestedCoalesces()
    {
        var reference = TypeRef.Definition("Foreign", "Samples", "Reference");
        var inner = new Coalesce(new Constant(null, ObjectType),
            new LoadArgument(0, "first", reference));
        var outer = new Coalesce(inner, new LoadArgument(1, "second", reference));
        var function = Function(reference, new Return(outer));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [reference] = TypeShape.Reference };
        Assert.Null(outer.AssignmentType);

        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.Equal(reference, inner.AssignmentType);
        Assert.Equal(reference, outer.AssignmentType);
    }

    [Fact]
    public void BindingRefreshesAfterOperandReplacement()
    {
        var coalesce = new Coalesce(new LoadArgument(0, "text", StringType),
            new LoadArgument(1, "fallback", StringType));
        var function = Function(ObjectType, new Return(coalesce));
        Assert.Equal(StringType, coalesce.AssignmentType);
        coalesce.Right.ReplaceWith(new LoadArgument(1, "fallback", ObjectType));

        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.Equal(ObjectType, coalesce.AssignmentType);
    }

    [Fact]
    public void NullableValueBindingIsUnchanged()
    {
        var integer = TypeRef.CoreLib("System", "Int32");
        var nullable = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Nullable`1"), [integer]);
        var coalesce = new Coalesce(new LoadArgument(0, "value", nullable),
            new LoadArgument(1, "fallback", integer));
        var function = Function(integer, new Return(coalesce));

        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.Equal(integer, coalesce.AssignmentType);
        Assert.Equal(coalesce.ResultType, coalesce.AssignmentType);
        Assert.DoesNotContain(function.Descendants.OfType<Coerce>(),
            coerce => coerce.Kind == CoercionKind.ReferenceWitness);
    }

    [Fact]
    public void RealRoslynDisplayCarriesDecidedCoalesceAssignment()
    {
        string path = typeof(SyntaxTree).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source,
            "Microsoft.CodeAnalysis.Diagnostics.AnalyzerImageReference", "get_Display");
        var coalesces = function.Descendants.OfType<Coalesce>().ToArray();

        Assert.NotEmpty(coalesces);
        Assert.All(coalesces, coalesce => Assert.Equal(StringType, coalesce.AssignmentType));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void CompilerProducedComparerKeepsExistingStorageAdmission()
    {
        string path = typeof(ReferenceCoalesceBindingSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, typeof(ReferenceCoalesceBindingSamples).FullName!,
            nameof(ReferenceCoalesceBindingSamples.CacheComparer));
        var store = Assert.Single(function.Descendants.OfType<StoreStackSlot>(),
            store => store.Value is Coalesce);
        var coalesce = Assert.IsType<Coalesce>(store.Value);

        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);

        Assert.Null(coalesce.AssignmentType);
        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function),
            decision => decision.Slot == store.Slot && ReferenceEquals(decision.Scope, function)).WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.DoesNotContain(function.Descendants.OfType<StoreStackSlot>(),
            remaining => remaining.Slot == store.Slot);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task CompilerProducedReferenceBindingsKeepNativeContracts()
    {
        string[] methods =
        [
            nameof(ReferenceCoalesceBindingSamples.SameType),
            nameof(ReferenceCoalesceBindingSamples.ObjectRight),
            nameof(ReferenceCoalesceBindingSamples.ObjectLeft),
            nameof(ReferenceCoalesceBindingSamples.NullableValue),
            nameof(ReferenceCoalesceBindingSamples.ObjectOverload),
            nameof(ReferenceCoalesceBindingSamples.SpilledObjectOverload),
            nameof(ReferenceCoalesceBindingSamples.ConstructorOverload),
            nameof(ReferenceCoalesceBindingSamples.CacheComparer),
            nameof(ReferenceCoalesceBindingSamples.NestedObjectOverload),
            nameof(ReferenceCoalesceBindingSamples.LocalFunctionObjectOverload),
        ];
        var results = await ReturnToSender.CompileBackTargets(
            typeof(ReferenceCoalesceBindingSamples).Assembly.Location,
            methods.Select(method => new ReturnToSender.RequestedTarget(
                typeof(ReferenceCoalesceBindingSamples).FullName!, method, 0)).ToArray(),
            sourceIndex: null, applyCompileBackFloor: false);

        Assert.Equal(methods.Length, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.UsedCompileBackFloor);
            // These two nested shapes have the same generated-identity limits
            // at the measured base; neither is an Exact fidelity claim.
            var expected = result.Plan.TargetMethod.Method switch
            {
                nameof(ReferenceCoalesceBindingSamples.NestedObjectOverload)
                    => FidelityCheck.CompileBackStatus.OperandDiff,
                nameof(ReferenceCoalesceBindingSamples.LocalFunctionObjectOverload)
                    => FidelityCheck.CompileBackStatus.NotFull,
                _ => FidelityCheck.CompileBackStatus.Exact,
            };
            Assert.True(result.Status == expected,
                $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
            Assert.Equal(result.OriginalOpcodes, result.RecompiledOpcodes);
        });
    }

    static IrFunction Raise(string method, bool lowered)
    {
        using var source = MetadataSource.Open(typeof(ReferenceCoalesceBindingSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ReferenceCoalesceBindingSamples).FullName!, method);
        Assert.NotNull(function);
        var context = PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint);
        IrPasses.Run(function, lowered ? IrPasses.Lowered : IrPasses.Default, context);
        return function;
    }
}
