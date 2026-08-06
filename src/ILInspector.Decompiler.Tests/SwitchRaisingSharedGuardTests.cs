using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

enum SharedGuardAlgorithm
{
    Low6 = 6,
    Low7 = 7,
    Dense24 = 24,
    Dense25 = 25,
    Dense30 = 30,
    Dense31 = 31,
    Dense32 = 32,
    Dense33 = 33,
    Dense35 = 35,
    Dense36 = 36,
    Dense37 = 37,
    Dense40 = 40,
    Dense41 = 41,
}

static class SharedGuardSwitchFixture
{
    public static void Check(SharedGuardAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case SharedGuardAlgorithm.Low6:
            case SharedGuardAlgorithm.Low7:
            case SharedGuardAlgorithm.Dense24:
            case SharedGuardAlgorithm.Dense25:
            case SharedGuardAlgorithm.Dense30:
            case SharedGuardAlgorithm.Dense31:
            case SharedGuardAlgorithm.Dense32:
            case SharedGuardAlgorithm.Dense33:
            case SharedGuardAlgorithm.Dense35:
            case SharedGuardAlgorithm.Dense36:
            case SharedGuardAlgorithm.Dense37:
            case SharedGuardAlgorithm.Dense40:
            case SharedGuardAlgorithm.Dense41:
                break;
            default:
                throw new ArgumentException();
        }
    }

    public static int ExitLoopFromDefault(int value, bool skip)
    {
        int result = 0;
        do
        {
            if (skip)
                goto Continue;
            switch (value)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 6:
                case 7:
                    goto Continue;
                case 5:
                    throw new ArgumentException();
                default:
                    goto Done;
            }
        Continue:
            result++;
        }
        while (result < 3);
    Done:
        return result;
    }
}

[Trait("Area", "Pass")]
public class SwitchRaisingSharedGuardTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef s_object = TypeRef.CoreLib("System", "Object");

    [Fact]
    public void CompilerProducedSparseEnumSwitch_RaisesSharedGuardContinuation()
    {
        using var source = MetadataSource.Open(typeof(SharedGuardSwitchFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(SharedGuardSwitchFixture).FullName!,
            nameof(SharedGuardSwitchFixture.Check));
        Assert.NotNull(function);
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Single(function.Descendants.OfType<ConditionalBranch>());

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("switch (algorithm - 24)", output);
        Assert.DoesNotContain("__switchValue", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void PrecedingGuardAndCasesSharingContinuation_RaisesToSwitch()
    {
        var function = BuildSharedGuardSwitch();

        IrPasses.Run(function);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Equal(2, node.Sections.Count);
        Assert.Single(node.Sections, section => section.IsDefault);
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("switch (algorithm - 24)", output);
        Assert.Contains("case 0:", output);
        Assert.Contains("case 1:", output);
        Assert.Contains("case 2:", output);
        Assert.Contains("case 3:", output);
        Assert.Contains("default:", output);
        Assert.Contains("throw null;", output);
        Assert.DoesNotContain("__switchValue", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void SharedDefaultCaseExitingToContinuation_RaisesWithBreak()
    {
        var function = BuildSharedGuardSwitch(defaultExitsToContinuation: true);

        IrPasses.Run(function);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        var sharedDefault = Assert.Single(node.Sections, section => section.IsDefault);
        Assert.Equal(2, sharedDefault.Labels.Length);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("case 2:", output);
        Assert.Contains("case 3:", output);
        Assert.Contains("default:", output);
        Assert.Contains("V_0 = 1;", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("__switchValue", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void PrecedingGuardEnteringCaseBody_RemainsFlat()
    {
        var function = BuildSharedGuardSwitch(guardTargetsCaseBody: true);

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
    }

    [Fact]
    public void SharedDefaultContainingEnclosingLoopBreak_RemainsFlat()
    {
        var loopBody = new BlockContainer();

        var guard = new Block(0);
        guard.Add(new ConditionalBranch(
            new LoadArgument(1, "skip", s_bool),
            0x30));
        loopBody.Add(guard);

        var dispatch = new Block(0x0D);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x30, 0x20, 0x20, 0x20]));
        loopBody.Add(dispatch);

        var sharedDefault = new Block(0x20);
        sharedDefault.Add(new Break());
        loopBody.Add(sharedDefault);

        var continuation = new Block(0x30);
        continuation.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        loopBody.Add(continuation);

        var outerBlock = new Block();
        outerBlock.Add(new DoWhileLoop(loopBody, new Constant(false, s_bool)));
        outerBlock.Add(new Return(new LoadLocal(0, s_int)));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_int,
                [
                    new Parameter("value", s_int),
                    new Parameter("skip", s_bool),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [s_int],
            outerBody);

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
    }

    [Fact]
    public void CompilerProducedSeparateDefaultContainingEnclosingLoopBreak_RemainsFlat()
    {
        using var source = MetadataSource.Open(typeof(SharedGuardSwitchFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(SharedGuardSwitchFixture).FullName!,
            nameof(SharedGuardSwitchFixture.ExitLoopFromDefault));
        Assert.NotNull(function);
        Assert.Single(function.Descendants.OfType<SwitchBranch>());

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
    }

    [Fact]
    public void SharedDefaultSiblingCaseContainingEnclosingLoopBreak_RemainsFlat()
    {
        var loopBody = new BlockContainer();

        var guard = new Block(0);
        guard.Add(new ConditionalBranch(
            new LoadArgument(1, "skip", s_bool),
            0x40));
        loopBody.Add(guard);

        var dispatch = new Block(0x0D);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x40, 0x20, 0x30]));
        loopBody.Add(dispatch);

        var sharedDefault = new Block(0x20);
        sharedDefault.Add(new Throw(new Constant(null, s_object)));
        loopBody.Add(sharedDefault);

        var breakArm = new Block();
        breakArm.Add(new Break());
        var caseWithLoopBreak = new Block(0x30);
        caseWithLoopBreak.Add(new IfStatement(
            new LoadArgument(1, "skip", s_bool),
            breakArm,
            elseArm: null));
        loopBody.Add(caseWithLoopBreak);

        var continuation = new Block(0x40);
        continuation.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        loopBody.Add(continuation);

        var outerBlock = new Block();
        outerBlock.Add(new DoWhileLoop(loopBody, new Constant(false, s_bool)));
        outerBlock.Add(new Return(new LoadLocal(0, s_int)));
        var outerBody = new BlockContainer();
        outerBody.Add(outerBlock);

        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_int,
                [
                    new Parameter("value", s_int),
                    new Parameter("skip", s_bool),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [s_int],
            outerBody);

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
    }

    [Fact]
    public void SharedDefaultContainingNestedLoopBreak_StillRaises()
    {
        var function = BuildSharedGuardSwitch(
            defaultExitsToContinuation: true,
            defaultContainsNestedLoopBreak: true);

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        var nestedLoop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(nestedLoop.Descendants.OfType<Break>());
    }

    static IrFunction BuildSharedGuardSwitch(
        bool guardTargetsCaseBody = false,
        bool defaultExitsToContinuation = false,
        bool defaultContainsNestedLoopBreak = false)
    {
        var body = new BlockContainer();

        var guard = new Block(0);
        guard.Add(new ConditionalBranch(
            new Comparison(
                ComparisonKind.LessThanOrEqual,
                isUnsigned: true,
                new Binary(
                    BinaryKind.Subtract,
                    isChecked: false,
                    isUnsigned: false,
                    new LoadArgument(0, "algorithm", s_int),
                    new Constant(6, s_int)),
                new Constant(1, s_int)),
            guardTargetsCaseBody ? 0x20 : 0x30));
        body.Add(guard);

        var dispatch = new Block(0x0D);
        dispatch.Add(new SwitchBranch(
            new Binary(
                BinaryKind.Subtract,
                isChecked: false,
                isUnsigned: false,
                new LoadArgument(0, "algorithm", s_int),
                new Constant(24, s_int)),
            [0x30, 0x30, 0x20, 0x20]));
        body.Add(dispatch);

        var rejected = new Block(0x20);
        if (defaultExitsToContinuation)
        {
            if (defaultContainsNestedLoopBreak)
            {
                var nestedBody = new Block();
                nestedBody.Add(new Break());
                rejected.Add(new WhileLoop(new Constant(true, s_bool), nestedBody));
            }
            rejected.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
            rejected.Add(new Branch(0x30));
        }
        else
        {
            rejected.Add(new Throw(new Constant(null, s_object)));
        }
        body.Add(rejected);

        var continuation = new Block(0x30);
        continuation.Add(new Return(null));
        body.Add(continuation);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_void,
                [new Parameter("algorithm", s_int)],
                HasThis: false,
                GenericParameterCount: 0),
            defaultExitsToContinuation ? [s_int] : [],
            body);
    }
}
