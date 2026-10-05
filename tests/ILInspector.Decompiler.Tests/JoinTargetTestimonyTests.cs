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

    [Fact]
    public void ResidualBindingRefreshesTestimonyForAnArmItRetypes()
    {
        // A slot read as a conditional arm has no sink to testify from, so
        // materialization declines it and residual storage binding turns the
        // load into an int local after the final join binding ran. The
        // conditional's arm facts must describe the rewritten arm.
        var slotArm = new LoadStackSlot(0, type: IntType);
        var conditional = Conditional(slotArm, Arg(1, "right", IntType), mergedType: ForeignEnum);
        var function = Function(
            new StoreStackSlot(0, Arg(2, "value", IntType)),
            new StoreStackSlot(0, Arg(3, "other", IntType)),
            new Return(conditional));
        new PrimitiveJoinBindingPass().Run(function, PassContext.None);
        // Model the pre-binding state the corpus never produced: the arm's
        // issued fact was taken while it was untyped.
        ((IPrimitiveJoin)conditional).PrimitiveTargets = Testimony(conditional) with { IntegerArms = false };

        new ResidualSlotBindingPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants, static node => node is LoadStackSlot or StoreStackSlot);
        Assert.True(Testimony(conditional).IntegerArms);
        Assert.True(conditional.CanRenderValueJoinAt(ForeignEnum, NoShapes));
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
