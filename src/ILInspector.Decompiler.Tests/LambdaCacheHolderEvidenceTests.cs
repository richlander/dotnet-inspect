using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Adversarial guard for LambdaCachePass declaring-type evidence (issue #1480).
// IsDelegateCacheField anchored the cache idiom on the `<>9__` field-name shape
// alone. A foreign or preinitialized field wearing that name on an ordinary type
// would be erased. Require the declaring type to also be a generated `<>c` closure
// holder. csc only ever emits `<>9__` caches on `<>c` (or generic `<>c__N`), so the
// positive canary still collapses; the negative (same name on an ordinary type)
// must stay the lazy-null guarded shape.
public class LambdaCacheHolderEvidenceTests
{
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static IrFunction Build(TypeRef cacheHolder)
    {
        var cacheField = new FieldRef(cacheHolder, "<>9__0_0", Action);
        var target = new MethodRef(Owner, "Target", Void, [], HasThis: false);
        var use = new MethodRef(Owner, "Use", Void, [Action], HasThis: false);

        // then: t = new Action(Target); cache = t; result = t;
        var then = new Block(0);
        then.Add(new StoreStackSlot(2, new DelegateCreation(Action, target, isVirtual: false, new Constant(null, Object))));
        then.Add(new StoreField(cacheField, null, new LoadStackSlot(2, Action)));
        then.Add(new StoreStackSlot(1, new LoadStackSlot(2, Action)));

        // s0 = cache; result = s0; if (s0 is null) { then } use(result);
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadField(cacheField, null)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, Action)));
        block.Add(new IfStatement(new LogicalNot(new LoadStackSlot(0, Action)), then, elseArm: null));
        block.Add(new ExpressionStatement(new Call(use, isVirtual: false, [new LoadStackSlot(1, Action)])));

        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction("M", Owner, new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0), [], container);
        new LambdaCachePass().Run(function, PassContext.None);
        function.CheckInvariant();
        return function;
    }

    [Fact]
    public void CacheFieldOnClosureHolder_Collapses()
    {
        // The csc shape: the cache field lives on the generated `<>c` closure holder.
        var function = Build(TypeRef.Definition("Synthetic", "Samples", "Owner+<>c"));

        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), l => l.Field.Name == "<>9__0_0");
    }

    [Fact]
    public void CacheNamedFieldOnOrdinaryType_StaysUnfolded()
    {
        // A `<>9__`-named field on an ordinary (non-`<>c`) type is not proof of a
        // compiler cache — leave the lazy-null guarded shape intact.
        var function = Build(Owner);

        Assert.Single(function.Descendants.OfType<IfStatement>());
        Assert.Contains(function.Descendants.OfType<StoreField>(), s => s.Field.Name == "<>9__0_0");
    }
}
