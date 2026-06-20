using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class PlaceIdentityTests
{
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Obj = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Str = TypeRef.CoreLib("System", "String");

    [Fact]
    public void SameVariable_MatchesSameLocalAndArgument()
    {
        Assert.True(PlaceIdentity.SameVariable(new LoadLocal(2, Int), new LoadLocal(2, Int)));
        Assert.True(PlaceIdentity.SameVariable(new LoadArgument(0, "this", Obj), new LoadArgument(0, "this", Obj)));
    }

    [Fact]
    public void SameVariable_RejectsDifferentSlotKindOrIndex()
    {
        Assert.False(PlaceIdentity.SameVariable(new LoadLocal(1, Int), new LoadLocal(2, Int)));
        Assert.False(PlaceIdentity.SameVariable(new LoadArgument(0, "a", Int), new LoadArgument(1, "b", Int)));
        // A local and an argument at the same index are different places.
        Assert.False(PlaceIdentity.SameVariable(new LoadLocal(0, Int), new LoadArgument(0, "a", Int)));
        // A stack slot is not a variable.
        Assert.False(PlaceIdentity.SameVariable(new LoadStackSlot(0, Int), new LoadStackSlot(0, Int)));
        Assert.False(PlaceIdentity.SameVariable(null, null));
    }

    [Fact]
    public void SameStackSlot_MatchesOnlyMatchingSlots()
    {
        Assert.True(PlaceIdentity.SameStackSlot(new LoadStackSlot(256, Int), new LoadStackSlot(256, Int)));
        Assert.False(PlaceIdentity.SameStackSlot(new LoadStackSlot(256, Int), new LoadStackSlot(257, Int)));
        // A re-loaded variable is not the spilled slot — the discriminator that
        // separates a genuine compiler spill from hand-written source.
        Assert.False(PlaceIdentity.SameStackSlot(new LoadLocal(0, Int), new LoadLocal(0, Int)));
        Assert.False(PlaceIdentity.SameStackSlot(null, null));
    }

    [Fact]
    public void NeitherAtom_TreatsAPropertyGetterOrCallAsAPlace()
    {
        // A property getter / method call has side effects and is not a
        // re-evaluable place — both atoms must reject it even against itself.
        var getter = new MethodRef(Obj, "get_Value", Str, ImmutableArray<TypeRef>.Empty, HasThis: true);
        var property = new LoadProperty(getter, new LoadArgument(0, "this", Obj), []);
        var property2 = new LoadProperty(getter, new LoadArgument(0, "this", Obj), []);

        Assert.False(PlaceIdentity.SameVariable(property, property2));
        Assert.False(PlaceIdentity.SameStackSlot(property, property2));

        var call = new Call(getter, isVirtual: false, [new LoadArgument(0, "this", Obj)]);
        var call2 = new Call(getter, isVirtual: false, [new LoadArgument(0, "this", Obj)]);
        Assert.False(PlaceIdentity.SameVariable(call, call2));
        Assert.False(PlaceIdentity.SameStackSlot(call, call2));
    }
}
