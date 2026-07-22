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
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");

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

        // Render the actual text: a technically-raised-but-garbled body (e.g.
        // a duplicated or dropped miss-handler assignment) would slip past the
        // node-shape assertions above but must be caught here.
        var output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").Trim();
        Assert.Equal(
            """
            int V_0;

            switch (n)
            {
                case 0:
                    if (k == 7) goto IL_0018;
                    break;
                    IL_0018:
                    V_0 = 100;
                    goto IL_0034;
                case 1:
                    if (k == 9) goto IL_0028;
                    break;
                    IL_0028:
                    V_0 = 200;
                    goto IL_0034;
            }
            V_0 = -1;
            IL_0034:
            return V_0;
            """.ReplaceLineEndings("\n"),
            output);
    }

    // The same shape, but reached through the string-equality-chain raiser
    // (RaiseStringEqualityChain -> FinishSwitchRaise) rather than the IL
    // `switch` opcode raiser (Raise). Both entry paths must preserve the shared
    // region helper's one-hop and multi-hop exit-chain behavior.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void StringEqualityChainWithSharedMissHandler_StillRaisesToSwitch(int chainLength)
    {
        var function = BuildStringEqualityChainWithSharedMissHandler(chainLength);

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Equal(2, node.Sections.Count);
        Assert.DoesNotContain(node.Sections, s => s.IsDefault);
        Assert.Single(function.Descendants.OfType<Return>());

        if (chainLength >= 2)
        {
            var output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
            Assert.Contains("V_0 = -1;\nV_1 = 0;\nIL_0070:\nreturn V_0;", output);
            Assert.Equal(2, output.Split("V_1 = 0;", StringSplitOptions.None).Length);
        }
    }

    [Fact]
    public void IlSwitch_MissHandlerChainCrossingCaseEntry_RemainsFlat()
        => AssertMissHandlerChainCrossingCaseEntryRemainsFlat(
            BuildTwoCaseSwitchWithSharedMissHandler(missCrossesCaseEntry: true));

    [Fact]
    public void StringEqualityChain_MissHandlerChainCrossingCaseEntry_RemainsFlat()
        => AssertMissHandlerChainCrossingCaseEntryRemainsFlat(
            BuildStringEqualityChainWithSharedMissHandler(missCrossesCaseEntry: true));

    static void AssertMissHandlerChainCrossingCaseEntryRemainsFlat(IrFunction function)
    {
        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
    }

    // A longer transparent chain between the miss-handler and the join (two
    // plain single-successor blocks instead of one), to confirm ChasesTo
    // walks a chain of length > 1, not just a single hop.
    [Fact]
    public void MissHandlerChainOfTwoBlocksBeforeJoin_StillRaisesToSwitch()
    {
        var function = BuildTwoCaseSwitchWithSharedMissHandler(chainLength: 2);

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Equal(2, node.Sections.Count);
        Assert.Single(function.Descendants.OfType<Return>());
    }

    static Comparison IntEqual(int argIndex, string argName, int value)
        => new(ComparisonKind.Equal, isUnsigned: false, new LoadArgument(argIndex, argName, s_int), new Constant(value, s_int));

    // switch (n) { case 0: if (k == 7) goto leaf0; break; case 1: if (k == 9)
    // goto leaf1; break; } goto sharedMiss; leaf0: v = 100; goto join; leaf1:
    // v = 200; goto join; sharedMiss: v = -1; join: return v; — laid out the
    // way csc places case bodies (and their "no match" arms) before a shared
    // tail, exactly like the real Humanizer method's two-tier dispatch.
    //
    // chainLength controls how many plain, single-successor blocks sit
    // between the shared miss-handler and the join: 1 (default) means the
    // miss-handler falls straight into the join; 2 inserts one extra
    // transparent pass-through block, exercising ChasesTo over a multi-hop
    // chain rather than a single hop.
    static IrFunction BuildTwoCaseSwitchWithSharedMissHandler(
        int chainLength = 1,
        bool missCrossesCaseEntry = false)
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

        var joinOffset = chainLength >= 2 ? 0x38 : 0x34;

        var case0Leaf = new Block(0x18);
        case0Leaf.Add(new StoreLocal(0, s_int, new Constant(100, s_int)));
        case0Leaf.Add(new Branch(joinOffset));   // straight to the join, skipping the miss-handler
        body.Add(case0Leaf);

        var case1 = new Block(0x20);
        if (missCrossesCaseEntry)
            case1.Add(new Branch(joinOffset));
        else
            case1.Add(new ConditionalBranch(IntEqual(1, "k", 9), targetOffset: 0x28));
        body.Add(case1);

        if (!missCrossesCaseEntry)
        {
            var case1Miss = new Block(0x24);
            case1Miss.Add(new Branch(0x30));
            body.Add(case1Miss);

            var case1Leaf = new Block(0x28);
            case1Leaf.Add(new StoreLocal(0, s_int, new Constant(200, s_int)));
            case1Leaf.Add(new Branch(joinOffset));
            body.Add(case1Leaf);
        }

        var sharedMiss = new Block(0x30);
        sharedMiss.Add(new StoreLocal(0, s_int, new Constant(-1, s_int)));   // falls through
        if (missCrossesCaseEntry)
            sharedMiss.Add(new Branch(0x20));
        body.Add(sharedMiss);

        if (chainLength >= 2)
        {
            // An extra plain pass-through hop between the miss-handler and
            // the join — still no branch, just falls through in turn.
            var passThrough = new Block(0x34);
            passThrough.Add(new StoreLocal(1, s_int, new Constant(0, s_int)));
            body.Add(passThrough);
        }

        var join = new Block(joinOffset);
        join.Add(new Return(new LoadLocal(0, s_int)));
        body.Add(join);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [new Parameter("n", s_int), new Parameter("k", s_int)], HasThis: false, GenericParameterCount: 0),
            [s_int, s_int],
            body);
    }

    // switch (s) { case "a": ...; case "b": ...; } — an equality-chain
    // dispatch (RaiseStringEqualityChain -> FinishSwitchRaise) whose two case
    // bodies each have a case-local "no match" arm chaining into one shared
    // miss-handler that falls through into the method's single return, and a
    // matching arm that jumps straight to that return, skipping the
    // miss-handler. This drives the same shared region logic through
    // FinishSwitchRaise rather than Raise.
    static IrFunction BuildStringEqualityChainWithSharedMissHandler(
        int chainLength = 1,
        bool missCrossesCaseEntry = false)
    {
        var body = new BlockContainer();
        var eq = new MethodRef(s_string, "op_Equality", s_bool, [s_string, s_string], HasThis: false);
        IrExpression StrEq(string literal) => new Call(eq, isVirtual: false,
            [new LoadArgument(0, "s", s_string), new Constant(literal, s_string)]);

        var chain0 = new Block(0);
        chain0.Add(new ConditionalBranch(StrEq("a"), targetOffset: 0x40));
        body.Add(chain0);

        var chain1 = new Block(8);
        chain1.Add(new ConditionalBranch(StrEq("b"), targetOffset: 0x50));
        body.Add(chain1);

        var afterChain = new Block(0x10);
        afterChain.Add(new Branch(0x60));   // bare jump into the shared miss-handler (the default)
        body.Add(afterChain);

        var caseA = new Block(0x40);
        caseA.Add(new ConditionalBranch(IntEqual(1, "k", 1), targetOffset: 0x48));
        body.Add(caseA);

        var caseAMiss = new Block(0x44);
        caseAMiss.Add(new Branch(0x60));
        body.Add(caseAMiss);

        var caseALeaf = new Block(0x48);
        caseALeaf.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        caseALeaf.Add(new Branch(0x70));   // straight to the join, skipping the miss-handler
        body.Add(caseALeaf);

        var caseB = new Block(0x50);
        if (missCrossesCaseEntry)
            caseB.Add(new Branch(0x70));
        else
            caseB.Add(new ConditionalBranch(IntEqual(1, "k", 2), targetOffset: 0x58));
        body.Add(caseB);

        if (!missCrossesCaseEntry)
        {
            var caseBMiss = new Block(0x54);
            caseBMiss.Add(new Branch(0x60));
            body.Add(caseBMiss);

            var caseBLeaf = new Block(0x58);
            caseBLeaf.Add(new StoreLocal(0, s_int, new Constant(2, s_int)));
            caseBLeaf.Add(new Branch(0x70));
            body.Add(caseBLeaf);
        }

        var sharedMiss = new Block(0x60);
        sharedMiss.Add(new StoreLocal(0, s_int, new Constant(-1, s_int)));   // falls through into the join
        if (missCrossesCaseEntry)
            sharedMiss.Add(new Branch(0x50));
        body.Add(sharedMiss);

        if (chainLength >= 2)
        {
            var passThrough = new Block(0x68);
            passThrough.Add(new StoreLocal(1, s_int, new Constant(0, s_int)));
            body.Add(passThrough);
        }

        var join = new Block(0x70);
        join.Add(new Return(new LoadLocal(0, s_int)));
        body.Add(join);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [new Parameter("s", s_string), new Parameter("k", s_int)], HasThis: false, GenericParameterCount: 0),
            chainLength >= 2 ? [s_int, s_int] : [s_int],
            body);
    }
}
