using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class CoercionRenderingTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Enum32 = TypeRef.Definition("synthetic", "", "E32");
    static readonly TypeRef OtherEnum32 = TypeRef.Definition("synthetic", "", "OtherE32");

    static readonly IReadOnlyDictionary<TypeRef, TypeShape> Shapes = new Dictionary<TypeRef, TypeShape>
    {
        [Enum32] = TypeShape.Enum,
        [OtherEnum32] = TypeShape.Enum,
    };

    static readonly IReadOnlyDictionary<TypeRef, TypeRef> Underlyings = new Dictionary<TypeRef, TypeRef>
    {
        [Enum32] = Int32,
        [OtherEnum32] = Int32,
    };

    [Fact]
    public void ValueCoercionDomain_PreservesLoadBearingAsymmetries()
    {
        Assert.True(CoercionRendering.CanSpellValueCoercion(Bool, Byte, Shapes, Underlyings));
        Assert.False(CoercionRendering.CanSpellValueCoercion(Int32, Bool, Shapes, Underlyings));
        Assert.True(CoercionRendering.CanSpellValueCoercion(Int32, Enum32, Shapes, Underlyings));
        Assert.True(CoercionRendering.CanSpellValueCoercion(Enum32, Int64, Shapes, Underlyings));
        Assert.True(CoercionRendering.CanSpellValueCoercion(Enum32, OtherEnum32, Shapes, Underlyings));
        Assert.False(CoercionRendering.CanSpellValueCoercion(Int32, Int64, Shapes, Underlyings));
    }

    [Fact]
    public void SlotCoercionAddsVerifierLiveRangeFamilyFilter()
    {
        Assert.True(CoercionRendering.CanSpellSlotCoercion(Bool, Byte, Shapes, Underlyings));
        Assert.True(CoercionRendering.CanSpellSlotCoercion(Enum32, Int64, Shapes, Underlyings));
        Assert.False(CoercionRendering.CanSpellSlotCoercion(Int32, Bool, Shapes, Underlyings));
        Assert.False(CoercionRendering.CanSpellSlotCoercion(Int32, Int64, Shapes, Underlyings));
        Assert.False(CoercionRendering.CanSpellSlotCoercion(null, Int32, Shapes, Underlyings));
    }
}
