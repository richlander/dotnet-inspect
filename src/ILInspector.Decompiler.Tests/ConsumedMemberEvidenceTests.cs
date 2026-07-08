using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ConsumedMemberEvidenceTests
{
    static readonly TypeRef Type = TypeRef.CoreLib("Sample", "Widget");
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    static MethodRef Method(string name, AccessorKind accessor = AccessorKind.Unknown)
        => new MethodRef(Type, name, Int, [], HasThis: true) { AccessorKind = accessor };

    [Fact]
    public void RegularMethod_SeedsTargetRoot()
        => Assert.True(new ConsumedMemberEvidence(Method: Method("GetValue")).EffectiveAllowTargetRoot);

    [Fact]
    public void PropertyAccessors_DoNotSeedTargetRoot()
    {
        Assert.False(new ConsumedMemberEvidence(Method: Method("get_Value", AccessorKind.PropertyGet)).EffectiveAllowTargetRoot);
        Assert.False(new ConsumedMemberEvidence(Method: Method("set_Value", AccessorKind.PropertySet)).EffectiveAllowTargetRoot);
    }

    [Fact]
    public void Constructors_DoNotSeedTargetRoot()
    {
        Assert.False(new ConsumedMemberEvidence(Method: Method(".ctor")).EffectiveAllowTargetRoot);
        Assert.False(new ConsumedMemberEvidence(Method: Method(".cctor")).EffectiveAllowTargetRoot);
    }

    [Fact]
    public void EventAccessors_SeedTargetRoot()
    {
        // Only property accessors are owned by a property; event add/remove are ordinary members here.
        Assert.True(new ConsumedMemberEvidence(Method: Method("add_Changed", AccessorKind.EventAdd)).EffectiveAllowTargetRoot);
        Assert.True(new ConsumedMemberEvidence(Method: Method("remove_Changed", AccessorKind.EventRemove)).EffectiveAllowTargetRoot);
    }

    [Fact]
    public void ConstructionContext_OverrideSeedsConstructorOnTargetRoot()
        // NewObject / initializer evidence sets AllowTargetRoot directly: a ctor/setter is legitimately re-seeded.
        => Assert.True(new ConsumedMemberEvidence(Method: Method(".ctor"), AllowTargetRoot: true).EffectiveAllowTargetRoot);

    [Fact]
    public void MemberKindIsTyped_NotNamePrefix()
        // A plain method named like an accessor is not one: kind comes from AccessorKind, not the name prefix.
        => Assert.True(new ConsumedMemberEvidence(Method: Method("get_NotAnAccessor", AccessorKind.None)).EffectiveAllowTargetRoot);

    [Fact]
    public void UnresolvedAccessor_FallsBackToNamePrefix()
    {
        // When metadata is unresolved (AccessorKind.Unknown) the member kind is not
        // known, so the reserved get_/set_ prefixes are honored — an unresolved
        // accessor is not re-seeded on the target root (matches the pre-refactor check).
        Assert.False(new ConsumedMemberEvidence(Method: Method("get_Value", AccessorKind.Unknown)).EffectiveAllowTargetRoot);
        Assert.False(new ConsumedMemberEvidence(Method: Method("set_Value", AccessorKind.Unknown)).EffectiveAllowTargetRoot);
    }

    [Fact]
    public void UnresolvedOrdinaryMethod_SeedsTargetRoot()
        // Unknown kind + a non-accessor name is still an ordinary member.
        => Assert.True(new ConsumedMemberEvidence(Method: Method("Compute", AccessorKind.Unknown)).EffectiveAllowTargetRoot);
}
