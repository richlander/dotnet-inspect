using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// Issue #2954: a real two-tier string-switch lowering (e.g.
// Humanizer.MalteseFormatter.GetResourceKey) has every case's "no match" arm
// jump into one shared miss-handler block that itself falls through into the
// method's single return, while each case's matching arm jumps straight to
// that same return, skipping the miss-handler. Raise's exit-unify previously
// required literal equality between a case's exits, so a case whose two exits
// were the miss-handler and the return it falls into (not the same block)
// failed to unify and the whole table was left flat as an if-ladder.
public class SwitchRaisingSharedMissHandlerTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void CaseWithMissArmChainingIntoTheReturnItFallsInto_StillRaisesToSwitch()
    {
        var function = BuildTwoCaseSwitchWithSharedMissHandler();

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        // Two case sections; the default is an empty jump straight to the
        // (shared) join, so C# omits it entirely.
        Assert.Equal(2, node.Sections.Count);
        Assert.DoesNotContain(node.Sections, s => s.IsDefault);

        // The shared miss-handler and the return it falls into print once,
        // as ordinary code after the switch — not duplicated per case.
        Assert.Single(function.Descendants.OfType<Return>());
    }

    static Comparison IntEqual(int argIndex, string argName, int value)
        => new(ComparisonKind.Equal, isUnsigned: false, new LoadArgument(argIndex, argName, s_int), new Constant(value, s_int));

    // switch (n) { case 0: if (k == 7) goto leaf0; break; case 1: if (k == 9)
    // goto leaf1; break; } goto sharedMiss; leaf0: v = 100; goto join; leaf1:
    // v = 200; goto join; sharedMiss: v = -1; join: return v; — laid out the
    // way csc places case bodies (and their "no match" arms) before a shared
    // tail, exactly like the real Humanizer method's two-tier dispatch.
    static IrFunction BuildTwoCaseSwitchWithSharedMissHandler()
    {
        var body = new BlockContainer();

        var entry = new Block(0);
        entry.Add(new SwitchBranch(new LoadArgument(0, "n", s_int), [0x10, 0x20]));
        body.Add(entry);

        var defaultBlock = new Block(4);
        defaultBlock.Add(new Branch(0x30));   // bare jump into the shared miss-handler
        body.Add(defaultBlock);

        var case0 = new Block(0x10);
        case0.Add(new ConditionalBranch(IntEqual(1, "k", 7), targetOffset: 0x18));
        body.Add(case0);

        var case0Miss = new Block(0x14);
        case0Miss.Add(new Branch(0x30));
        body.Add(case0Miss);

        var case0Leaf = new Block(0x18);
        case0Leaf.Add(new StoreLocal(0, s_int, new Constant(100, s_int)));
        case0Leaf.Add(new Branch(0x34));   // straight to the join, skipping the miss-handler
        body.Add(case0Leaf);

        var case1 = new Block(0x20);
        case1.Add(new ConditionalBranch(IntEqual(1, "k", 9), targetOffset: 0x28));
        body.Add(case1);

        var case1Miss = new Block(0x24);
        case1Miss.Add(new Branch(0x30));
        body.Add(case1Miss);

        var case1Leaf = new Block(0x28);
        case1Leaf.Add(new StoreLocal(0, s_int, new Constant(200, s_int)));
        case1Leaf.Add(new Branch(0x34));
        body.Add(case1Leaf);

        var sharedMiss = new Block(0x30);
        sharedMiss.Add(new StoreLocal(0, s_int, new Constant(-1, s_int)));   // falls through into the join
        body.Add(sharedMiss);

        var join = new Block(0x34);
        join.Add(new Return(new LoadLocal(0, s_int)));
        body.Add(join);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [new Parameter("n", s_int), new Parameter("k", s_int)], HasThis: false, GenericParameterCount: 0),
            [s_int],
            body);
    }
}
