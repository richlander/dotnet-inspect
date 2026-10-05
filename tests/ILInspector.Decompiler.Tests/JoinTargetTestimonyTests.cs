using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates for value-typed-emission.md, "Join target testimony": the enum and
/// <c>char</c> routes of join target compatibility read the issued arm facts
/// (every rendered arm integer-typed; every rendered arm a <c>char</c>
/// constant) through the one relation the printer and the residual storage
/// policy share, instead of each walking the arms at its own decision point.
/// </summary>
[Trait("Area", "Pass")]
public class JoinTargetTestimonyTests
{
    static readonly TypeRef BoolType = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef IntType = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef CharType = TypeRef.CoreLib("System", "Char");
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    // A cross-assembly enum: no loaded definition, so the shape map says
    // Unknown and only the structural enum-like test recognizes it.
    static readonly TypeRef ForeignEnum = TypeRef.Definition("Other", "Samples", "Mode");
    static readonly ImmutableDictionary<TypeRef, TypeShape> NoShapes = ImmutableDictionary<TypeRef, TypeShape>.Empty;

    [Fact]
    public void IntegerArmsRenderAtAnEnumLikeTargetForEveryJoinKind()
    {
        var conditional = Conditional(Arg(1, "left", IntType), Arg(2, "right", IntType), mergedType: ForeignEnum);
        var switchExpression = new SwitchExpression(
            Arg(0, "value", IntType),
            [
                new SwitchExpressionArm([0], false, new Constant(1, IntType)),
                new SwitchExpressionArm([], true, Arg(1, "right", IntType)),
            ]);
        var nullableEnum = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Nullable`1"), [ForeignEnum]);
        var coalesce = new Coalesce(Arg(0, "value", nullableEnum), Arg(1, "fallback", IntType));
        Bind(conditional, switchExpression, coalesce);

        // The join type is the enum, not an integer: the integer-family
        // testimony is empty, but the arm facts are issued regardless.
        Assert.Empty(Testimony(conditional).WholeJoinTargets);
        Assert.True(Testimony(conditional).IntegerArms);
        Assert.True(conditional.CanRenderValueJoinAt(ForeignEnum, NoShapes));
        Assert.True(switchExpression.CanRenderValueJoinAt(ForeignEnum, NoShapes));
        Assert.True(coalesce.CanRenderValueJoinAt(ForeignEnum, NoShapes));
        // A coalesce renders as a value join at the enum only when its left
        // operand is that enum's Nullable<T>; a known struct is never enum-like.
        var otherNullable = new Coalesce(
            Arg(0, "value", TypeRef.GenericInstance(TypeRef.CoreLib("System", "Nullable`1"), [IntType])),
            Arg(1, "fallback", IntType));
        Bind(otherNullable);
        Assert.False(otherNullable.CanRenderValueJoinAt(ForeignEnum, NoShapes));
        var structShapes = NoShapes.Add(ForeignEnum, TypeShape.ValueType);
        Assert.False(conditional.CanRenderValueJoinAt(ForeignEnum, structShapes));
    }

    [Fact]
    public void CharConstantArmsRenderAtCharOnlyForAConditional()
    {
        var conditional = Conditional(new Constant(97, IntType), new Constant(98, IntType), mergedType: IntType);
        var notConstant = Conditional(new Constant(97, IntType), Arg(1, "other", IntType), mergedType: IntType);
        var switchExpression = new SwitchExpression(
            Arg(0, "value", IntType),
            [
                new SwitchExpressionArm([0], false, new Constant(97, IntType)),
                new SwitchExpressionArm([], true, new Constant(98, IntType)),
            ]);
        Bind(conditional, notConstant, switchExpression);

        Assert.True(Testimony(conditional).CharConstantArms);
        Assert.True(conditional.CanRenderValueJoinAt(CharType, NoShapes));
        Assert.False(Testimony(notConstant).CharConstantArms);
        // The switch-expression route never had a char clause; its char
        // admission is the integer-family testimony's alone.
        Assert.True(Testimony(switchExpression).CharConstantArms);
        Assert.Equal(
            Testimony(switchExpression).WholeJoinTargets.ContainsKey(CharType),
            switchExpression.CanRenderValueJoinAt(CharType, NoShapes));
    }

    [Fact]
    public void MissingOrNonIntegerArmsDeclineTheEnumRoute()
    {
        var untyped = Conditional(new LoadStackSlot(0, type: null), Arg(1, "right", IntType), mergedType: ForeignEnum);
        var reference = Conditional(Arg(1, "left", StringType), Arg(2, "right", StringType), mergedType: StringType);
        Bind(untyped, reference);

        Assert.False(Testimony(untyped).IntegerArms);
        Assert.False(untyped.CanRenderValueJoinAt(ForeignEnum, NoShapes));
        Assert.False(reference.CanRenderValueJoinAt(ForeignEnum, NoShapes));
        // Reference arms are the conditional's other relation, not a value join.
        Assert.True(reference.CanRenderConditionalAt(StringType, NoShapes));
        Assert.False(reference.CanRenderValueJoinAt(StringType, NoShapes));
    }

    [Fact]
    public void ArmFactsAreIssuedAtBindingAndRefreshedByTheNextBinding()
    {
        var conditional = Conditional(Arg(1, "left", IntType), Arg(2, "right", IntType), mergedType: ForeignEnum);
        var function = Function(new Return(conditional));
        new PrimitiveJoinBindingPass().Run(function, PassContext.None);
        var clone = Assert.IsType<Conditional>(conditional.Clone());

        conditional.WhenFalse.ReplaceWith(new LoadStackSlot(0, type: null));
        Assert.True(conditional.CanRenderValueJoinAt(ForeignEnum, NoShapes));

        new PrimitiveJoinBindingPass().Run(function, PassContext.None);

        Assert.False(conditional.CanRenderValueJoinAt(ForeignEnum, NoShapes));
        Assert.True(clone.CanRenderValueJoinAt(ForeignEnum, NoShapes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArmFactsDescribeTheArmsCoercionInsertionWrapped(bool withResidualSlot)
    {
        // An enum-merged conditional of integer arms (ConditionalStoreChainPass
        // and SlotDiamondPass build it): the final binding sees int arms, then
        // coercion insertion wraps each arm in Coerce(Local). The readers —
        // the residual policy and the printer — must see the wrapped arms, as
        // the deleted live walks did (#9393 round 1). With and without a
        // residual slot (residual binding returns early when none exists).
        var conditional = Conditional(Arg(1, "a", IntType), Arg(2, "b", IntType), mergedType: LocalEnum);
        var statements = new List<IrNode>();
        if (withResidualSlot)
        {
            statements.Add(new StoreStackSlot(7, Arg(1, "a", IntType)));
            statements.Add(new StoreStackSlot(7, Arg(2, "b", IntType)));
            statements.Add(new Call(new MethodRef(TypeRef.CoreLib("System", "Math"), "Abs", IntType, [IntType], HasThis: false), false, [new LoadStackSlot(7, IntType)]));
        }
        statements.Add(new Return(conditional));
        var function = LocalEnumFunction(ForeignEnum, [.. statements]);

        RunEmissionTail(function);

        Assert.IsType<Coerce>(conditional.WhenTrue);
        Assert.Equal(FreshIntegerArms(conditional), Testimony(conditional).IntegerArms);
        Assert.False(Testimony(conditional).IntegerArms);
    }

    [Fact]
    public void ArmFactsDescribeSlotArmsAfterTheResidualDischarge()
    {
        // Both arms are slot loads: residual binding retypes them to int
        // locals and its coercion discharge then wraps them in Coerce(Local).
        var conditional = Conditional(new LoadStackSlot(3, IntType), new LoadStackSlot(4, IntType), mergedType: LocalEnum);
        var function = LocalEnumFunction(LocalEnum,
            new StoreStackSlot(3, Arg(1, "a", IntType)),
            new StoreStackSlot(4, Arg(2, "b", IntType)),
            new Return(conditional));

        RunEmissionTail(function);

        Assert.DoesNotContain(function.Descendants, static node => node is LoadStackSlot or StoreStackSlot);
        Assert.Equal(FreshIntegerArms(conditional), Testimony(conditional).IntegerArms);
    }

    [Fact]
    public void ResidualPolicyReadsTheWrappedArmsWhenItChoosesStorage()
    {
        // The round-1 admission witness: a slot stores the enum-merged
        // conditional and is loaded as a cross-assembly enum. With the stale
        // pre-insertion fact the policy unified the web as `Mode`; reading the
        // wrapped arms it keeps the base decision and splits the web.
        var conditional = Conditional(Arg(1, "a", IntType), Arg(2, "b", IntType), mergedType: LocalEnum);
        var function = LocalEnumFunction(ForeignEnum,
            new StoreStackSlot(7, conditional),
            new Return(new LoadStackSlot(7, ForeignEnum)));

        RunEmissionTail(function);

        var pieces = function.ResidualSlotBindings.Values.Where(static binding => binding.Slot == 7).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, static binding => Assert.Equal(ResidualSlotBindingKind.Split, binding.Kind));
    }

    static readonly TypeRef LocalEnum = TypeRef.Definition("synthetic", "", "Local");

    static bool FreshIntegerArms(Conditional conditional)
        => conditional.WhenTrue.ResultType is { } t && TypeFamilies.IsIntegerLike(t)
            && conditional.WhenFalse.ResultType is { } f && TypeFamilies.IsIntegerLike(f);

    static void RunEmissionTail(IrFunction function)
    {
        new PrimitiveJoinBindingPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);
        new ResidualSlotBindingPass().Run(function, PassContext.None);
    }

    static IrFunction LocalEnumFunction(TypeRef returnType, params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(returnType, [new Parameter("c", BoolType), new Parameter("a", IntType), new Parameter("b", IntType)], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [LocalEnum] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [LocalEnum] = IntType },
        };
    }

    static PrimitiveJoinTargetCompatibility Testimony(IrExpression join)
        => ((IPrimitiveJoin)join).PrimitiveTargets;

    static void Bind(params IrExpression[] joins)
    {
        foreach (var join in joins)
            ((IPrimitiveJoin)join).BindPrimitiveTargets(NoShapes, ImmutableDictionary<TypeRef, TypeRef>.Empty);
    }

    static LoadArgument Arg(int index, string name, TypeRef type) => new(index, name, type);

    static Conditional Conditional(IrExpression whenTrue, IrExpression whenFalse, TypeRef mergedType)
        => new(Arg(0, "choose", BoolType), whenTrue, whenFalse) { MergedType = mergedType };

    static IrFunction Function(params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(ForeignEnum, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
