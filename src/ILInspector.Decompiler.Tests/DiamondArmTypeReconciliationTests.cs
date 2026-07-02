using System.Collections.Generic;
using System.Linq;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the arm-order-independent slot typing for slot-diamond folds. When the
/// importer cannot merge a reused stack slot (its load type is unknown because
/// the join carries conflicting types, e.g. an <c>int</c> constant on one path
/// and an enum on the other), the folded conditional's result type must resolve
/// to the enum regardless of which arm the (polarity-preserved) branch leaves
/// first — but only when the constant is representable by the enum's underlying
/// type, so the printer's <c>(EnumType)value</c> cast compiles. Otherwise the
/// printer collapses the slot to <c>int</c> and renders the enum use without a
/// cast — invalid C# (#1901 regression).
/// </summary>
public class DiamondArmTypeReconciliationTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef Kind = TypeRef.Definition("Synthetic", "Samples", "Kind");

    [Fact]
    public void ConflictingArmType_IntConstantAndIntBackedEnum_ResolvesToEnum()
    {
        var function = FunctionWithEnum(Int32);
        var intConstant = new Constant(4, Int32);
        var enumValue = new Call(new MethodRef(Kind, "Get", Kind, [], HasThis: false), isVirtual: false, []);

        Assert.Equal(Kind, DiamondArmTypes.ConflictingArmType(intConstant, enumValue, function));
        Assert.Equal(Kind, DiamondArmTypes.ConflictingArmType(enumValue, intConstant, function));
    }

    [Fact]
    public void ConflictingArmType_InRangeConstantAndByteBackedEnum_ResolvesToEnum()
    {
        var function = FunctionWithEnum(Byte);
        var enumValue = new Call(new MethodRef(Kind, "Get", Kind, [], HasThis: false), isVirtual: false, []);
        Assert.Equal(Kind, DiamondArmTypes.ConflictingArmType(new Constant(4, Int32), enumValue, function));
    }

    [Fact]
    public void ConflictingArmType_OutOfRangeConstantAndByteBackedEnum_ResolvesToEnum()
    {
        // #2076: still anchored even out of the underlying range. The printer wraps
        // the reinterpret in `unchecked((Kind)300)` (see EnumIntegerCast), which is
        // valid and faithful, so the fold no longer needs to decline.
        var function = FunctionWithEnum(Byte);
        var enumValue = new Call(new MethodRef(Kind, "Get", Kind, [], HasThis: false), isVirtual: false, []);
        Assert.Equal(Kind, DiamondArmTypes.ConflictingArmType(new Constant(300, Int32), enumValue, function));
    }

    [Fact]
    public void ConflictingArmType_NegativeConstantAndUnsignedEnum_ResolvesToEnum()
    {
        // #2076: an unsigned-backed enum whose high-bit value is emitted as a
        // negative `ldc.i4` (e.g. 0x80000000u -> -1). `ConstantFits(-1, uint)` is
        // false, but the printer renders `unchecked((Kind)(-1))`, so anchor it.
        var function = FunctionWithEnum(TypeRef.CoreLib("System", "UInt32"));
        var enumValue = new Call(new MethodRef(Kind, "Get", Kind, [], HasThis: false), isVirtual: false, []);
        Assert.Equal(Kind, DiamondArmTypes.ConflictingArmType(new Constant(-1, Int32), enumValue, function));
    }

    [Fact]
    public void ConflictingArmType_TwoIntegerConstants_IsUnresolved()
    {
        var function = FunctionWithEnum(Int32);
        Assert.Null(DiamondArmTypes.ConflictingArmType(new Constant(4, Int32), new Constant(8, Int32), function));
    }

    [Fact]
    public void ConflictingArmType_IntegerConstantAndPlainIntegerArm_IsUnresolved()
    {
        // A plain (non-enum) integer concrete arm is intentionally excluded: the
        // arm printer would omit the cast a narrower or out-of-range constant needs
        // (e.g. `byte b = c ? 300 : x`). Only enum targets, which render an explicit
        // `(EnumType)value` cast, are anchored.
        var function = FunctionWithEnum(Int32);
        var byteValue = new Call(new MethodRef(Kind, "GetByte", Byte, [], HasThis: false), isVirtual: false, []);
        Assert.Null(DiamondArmTypes.ConflictingArmType(new Constant(300, Int32), byteValue, function));
    }

    [Fact]
    public void SlotDiamond_UnknownSlotWithIntConstantTrueArm_TypesConditionalAsEnum()
    {
        // if (c) S = 4; else S = Kind.Get(); return S;  — the true (int constant)
        // arm renders first under preserved polarity, and the returned slot's type
        // is unknown. The fold must still type the conditional as the enum.
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var get = new MethodRef(Kind, "Get", Kind, [], HasThis: false);

        var body = new BlockContainer();
        var head = new Block(0);
        head.Add(new ConditionalBranch(new LoadArgument(0, "c", Bool), 8));
        var falseArm = new Block(4);
        falseArm.Add(new StoreStackSlot(0, new Call(get, isVirtual: false, [])));
        falseArm.Add(new Branch(12));
        var trueArm = new Block(8);
        trueArm.Add(new StoreStackSlot(0, new Constant(4, Int32)));
        var merge = new Block(12);
        merge.Add(new Return(new LoadStackSlot(0, null)));
        foreach (var block in (Block[])[head, falseArm, trueArm, merge])
            body.Add(block);

        var function = new IrFunction(
            "M",
            owner,
            new MethodSignature(Kind, [new Parameter("c", Bool)], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [Kind] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [Kind] = Int32 },
        };

        new SlotDiamondPass().Run(function, PassContext.None);

        var conditional = Assert.Single(function.Descendants.OfType<Conditional>());
        Assert.Equal(Kind, conditional.ResultType);
    }

    static IrFunction FunctionWithEnum(TypeRef underlying)
    {
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(null));
        body.Add(block);
        return new IrFunction(
            "M",
            owner,
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [Kind] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [Kind] = underlying },
        };
    }
}
