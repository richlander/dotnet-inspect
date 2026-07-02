using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

// Slice 3 of docs/design/value-typed-emission.md: the Coerce node, the
// insertion pass, and the invariant. The insertion is output-neutral by
// construction (sinks already render through CoerceText; the node renders
// through the same function with the same target) — the full suite is that
// proof. These tests pin the routing machinery itself.
public class CoercionInvariantTests
{
    static readonly TypeRef Enum32 = TypeRef.Definition("synthetic", "", "E32");
    static readonly TypeRef Int32Type = TypeRef.CoreLib("System", "Int32");

    static IrFunction Function(BlockContainer body, TypeRef returnType, params Parameter[] parameters)
        => new("M", TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0), [], body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [Enum32] = TypeShape.Enum },
            EnumMembers = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [Enum32] = new Dictionary<long, string> { [1] = "One" },
            },
        };

    static BlockContainer Returning(IrExpression value)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(value));
        container.Add(block);
        return container;
    }

    [Fact]
    public void UncoercedEnumSink_IsAViolation_AndInsertionClearsIt()
    {
        // A non-constant integer at an enum return: not provably at target, so
        // the invariant flags it; the insertion pass wraps it and the invariant
        // goes green — the burn-down loop in miniature.
        var function = Function(
            Returning(new LoadArgument(0, "raw", Int32Type)),
            Enum32,
            new Parameter("raw", Int32Type));

        var before = CoercionInvariant.Check(function);
        Assert.Single(before);
        Assert.Contains("E32", before[0]);

        new CoercionInsertionPass().Run(function, PassContext.None);

        Assert.Empty(CoercionInvariant.Check(function));
        var coerce = Assert.IsType<Coerce>(Assert.Single(function.Descendants.OfType<Return>()).Value);
        Assert.Equal(Enum32, coerce.Target);
        Assert.Contains("return (E32)raw;", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void AtTargetValue_IsExemptThroughTheOnePredicate()
    {
        // The exemption ("provably already at the target type") is
        // CoercionTyping.IsAtTarget — one predicate, no per-sink judgment.
        var function = Function(
            Returning(new LoadArgument(0, "e", Enum32)),
            Enum32,
            new Parameter("e", Enum32));

        Assert.Empty(CoercionInvariant.Check(function));
        new CoercionInsertionPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<Coerce>());
    }

    [Fact]
    public void OutOfDomainSink_IsNotChecked()
    {
        // Reference-typed sinks are outside the invariant's domain until an
        // inverse conversion-classifier exists.
        var stringType = TypeRef.CoreLib("System", "String");
        var function = Function(
            Returning(new LoadArgument(0, "o", TypeRef.CoreLib("System", "Object"))),
            stringType,
            new Parameter("o", TypeRef.CoreLib("System", "Object")));

        Assert.Empty(CoercionInvariant.Check(function));
        new CoercionInsertionPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<Coerce>());
    }

    [Fact]
    public void CoerceComposesOverConvert_KeepingIlHistory()
    {
        // Leak case #6's composition: Coerce(Convert(long, m1), UE) — the inner
        // Convert keeps the IL history when folding is not applicable (checked
        // conv), the outer Coerce owns the surface conversion.
        var convert = new Convert(TypeRef.CoreLib("System", "Int64"), isChecked: true, isUnsigned: false,
            new LoadArgument(0, "raw", Int32Type));
        var function = Function(
            Returning(convert),
            Enum32,
            new Parameter("raw", Int32Type));

        new CoercionInsertionPass().Run(function, PassContext.None);

        var coerce = Assert.IsType<Coerce>(Assert.Single(function.Descendants.OfType<Return>()).Value);
        Assert.IsType<Convert>(coerce.Operand);
        Assert.Empty(CoercionInvariant.Check(function));
    }

    [Fact]
    public void NestedSinks_WrapDeepestFirst_LiveTreeStaysCoerced()
    {
        // A conditional (enum-merged) whose arm needs coercion, itself sitting
        // in an int-typed... rather: enum return whose conditional arms mix — the
        // arm wrap must land in the LIVE tree even though the outer wrap clones.
        var conditional = new Conditional(
            new LoadArgument(0, "c", TypeRef.CoreLib("System", "Boolean")),
            new LoadArgument(1, "raw", Int32Type),
            new LoadArgument(2, "e", Enum32))
        {
            MergedType = Enum32,
        };
        var function = Function(
            Returning(conditional),
            Enum32,
            new Parameter("c", TypeRef.CoreLib("System", "Boolean")),
            new Parameter("raw", Int32Type),
            new Parameter("e", Enum32));

        new CoercionInsertionPass().Run(function, PassContext.None);

        // The conditional is at-target (MergedType == return type), so only the
        // raw arm wraps — and it must wrap in the live tree.
        var live = Assert.Single(function.Descendants.OfType<Conditional>());
        Assert.IsType<Coerce>(live.WhenTrue);
        Assert.IsType<LoadArgument>(live.WhenFalse);
        Assert.Empty(CoercionInvariant.Check(function));
        Assert.Contains("(E32)raw", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void FullPipeline_LeavesFixturesInvariantClean()
    {
        // The standing assertion: after the real pipeline (which ends in the
        // insertion pass), every method in the enum-heavy fixture set satisfies
        // the invariant. This is where a future pass reshaping sink values
        // without coercion fails a unit test instead of a recompile.
        using var source = MetadataSource.Open(typeof(EnumCastSamples).Assembly.Location);
        foreach (var method in new[]
        {
            nameof(EnumCastSamples.EnumConditional),
            nameof(EnumCastSamples.EnumFlagsCompound),
            nameof(EnumCastSamples.EnumCoalesce),
            nameof(EnumCastSamples.LongEnumArray),
            nameof(EnumCastSamples.LongEnumBoxed),
            nameof(EnumCastSamples.ULongEnumBoxedMax),
            nameof(EnumCastSamples.UnsignedEnumConditionalArm),
            nameof(EnumCastSamples.CrossAssemblyEnumArray),
        })
        {
            var function = IrImporter.Import(source, typeof(EnumCastSamples).FullName!, method);
            Assert.NotNull(function);
            IrPasses.Run(function!);
            var violations = CoercionInvariant.Check(function!);
            Assert.True(violations.Count == 0,
                $"{method}: " + string.Join("; ", violations));
        }
    }
}
