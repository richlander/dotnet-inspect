using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

enum SharedGuardAlgorithm
{
    Low6 = 6,
    Low7 = 7,
    Dense24 = 24,
    Dense24Alias = Dense24,
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

enum NegativeOffsetAlgorithm
{
    Minus3 = -3,
    Minus2 = -2,
    Minus1 = -1,
    Zero = 0,
    One = 1,
    Two = 2,
}

enum HighOffsetAlgorithm : uint
{
    First = 0x80000000u,
    Second = 0x80000001u,
    Third = 0x80000002u,
    Fourth = 0x80000003u,
    Fifth = 0x80000004u,
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

    public static int ExitEnclosingLoopFromDenseDefault(int value)
    {
        int result = 0;
        do
        {
            switch (value)
            {
                case 0:
                    result += 1;
                    break;
                case 1:
                    result += 2;
                    break;
                case 2:
                    result += 3;
                    break;
                case 3:
                    result += 4;
                    break;
                default:
                    result += 100;
                    goto Done;
            }
            result++;
        }
        while (result < 30);
    Done:
        return result;
    }

    public static int ExitEnclosingLoopFromStringDefault(string value)
    {
        int result = 0;
        do
        {
            switch (value)
            {
                case "a":
                    result += 1;
                    break;
                case "b":
                    result += 2;
                    break;
                default:
                    result += 100;
                    goto Done;
            }
            result++;
        }
        while (result < 30);
    Done:
        return result;
    }

    public static int ClassifyNegativeOffset(NegativeOffsetAlgorithm algorithm)
    {
        switch (algorithm)
        {
            case NegativeOffsetAlgorithm.Minus3: return 3;
            case NegativeOffsetAlgorithm.Minus2: return 2;
            case NegativeOffsetAlgorithm.Minus1: return 1;
            case NegativeOffsetAlgorithm.Zero: return 0;
            case NegativeOffsetAlgorithm.One: return 10;
            case NegativeOffsetAlgorithm.Two: return 20;
            default: return -1;
        }
    }

    public static int ClassifyHighOffset(HighOffsetAlgorithm algorithm) => algorithm switch
    {
        HighOffsetAlgorithm.First => 1,
        HighOffsetAlgorithm.Second => 2,
        HighOffsetAlgorithm.Third => 3,
        HighOffsetAlgorithm.Fourth => 4,
        HighOffsetAlgorithm.Fifth => 5,
        _ => -1,
    };
}

[Trait("Area", "Pass")]
public class SwitchRaisingSharedGuardTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
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
        Assert.Contains("switch (algorithm)", output);
        Assert.Contains("case SharedGuardAlgorithm.Dense24:", output);
        Assert.Contains("case SharedGuardAlgorithm.Dense41:", output);
        Assert.DoesNotContain("Dense24Alias", output);
        Assert.DoesNotContain("algorithm - 24", output);
        Assert.DoesNotContain("__switchValue", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void CompilerProducedNegativeEnumSwitch_RestoresAddedOffset()
    {
        var function = Import(nameof(SharedGuardSwitchFixture.ClassifyNegativeOffset));
        var lowered = Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.IsType<Binary>(lowered.Value);

        IrPasses.Run(function);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.IsType<LoadArgument>(node.Value);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("switch (algorithm)", output);
        Assert.Contains("case NegativeOffsetAlgorithm.Minus3:", output);
        Assert.Contains("case NegativeOffsetAlgorithm.Two:", output);
        Assert.DoesNotContain("algorithm +", output);
    }

    [Fact]
    public void CompilerProducedHighUIntEnumSwitch_RestoresWrappedLabels()
    {
        var function = Import(nameof(SharedGuardSwitchFixture.ClassifyHighOffset));
        Assert.Single(function.Descendants.OfType<SwitchBranch>());

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<SwitchExpression>());
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("algorithm switch", output);
        Assert.Contains("HighOffsetAlgorithm.First => 1", output);
        Assert.Contains("HighOffsetAlgorithm.Fifth => 5", output);
        Assert.DoesNotContain("algorithm -", output);
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
    public void CheckedEnumOffset_RemainsOnArithmeticSelector()
    {
        var enumType = TypeRef.Definition("Synthetic", "", "CheckedAlgorithm");
        var function = BuildSharedGuardSwitch(enumType, switchIsChecked: true);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum };

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.IsType<Binary>(node.Value);
        Assert.Equal([0, 1, 2, 3], node.Sections.SelectMany(section => section.Labels).Select(label => label.Value));
    }

    [Fact]
    public void EnumWithUnknownBacking_RemainsOnArithmeticSelector()
    {
        var enumType = TypeRef.Definition("External", "Synthetic", "UnknownAlgorithm");
        var function = BuildSharedGuardSwitch(enumType);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum };

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.IsType<Binary>(node.Value);
        Assert.Equal([0, 1, 2, 3], node.Sections.SelectMany(section => section.Labels).Select(label => label.Value));
    }

    [Fact]
    public void LongBackedEnumOffset_RemainsOnArithmeticSelector()
    {
        var enumType = TypeRef.Definition("Synthetic", "", "LongAlgorithm");
        var function = BuildSharedGuardSwitch(enumType);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum };
        function.EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef>
        {
            [enumType] = TypeRef.CoreLib("System", "Int64"),
        };

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.IsType<Binary>(node.Value);
        Assert.Equal([0, 1, 2, 3], node.Sections.SelectMany(section => section.Labels).Select(label => label.Value));
    }

    [Fact]
    public void NarrowEnumWithRepresentableTranslatedLabels_RestoresEnumSelector()
    {
        var enumType = TypeRef.Definition("Synthetic", "", "ByteAlgorithm");
        var function = BuildSharedGuardSwitch(enumType, switchOffset: 252);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum };
        function.EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef>
        {
            [enumType] = TypeRef.CoreLib("System", "Byte"),
        };

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.IsType<LoadArgument>(node.Value);
        Assert.Equal([252, 253, 254, 255], node.Sections.SelectMany(section => section.Labels).Select(label => label.Value));
    }

    [Fact]
    public void NarrowEnumWithOutOfRangeTranslatedLabels_RemainsOnArithmeticSelector()
    {
        var enumType = TypeRef.Definition("Synthetic", "", "ByteAlgorithm");
        var function = BuildSharedGuardSwitch(enumType, switchOffset: 300);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum };
        function.EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef>
        {
            [enumType] = TypeRef.CoreLib("System", "Byte"),
        };

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var node = Assert.Single(function.Descendants.OfType<Switch>());
        Assert.IsType<Binary>(node.Value);
        Assert.Equal([0, 1, 2, 3], node.Sections.SelectMany(section => section.Labels).Select(label => label.Value));
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
    public void CompilerProducedDenseDefaultContainingEnclosingLoopBreak_RemainsFlat()
    {
        using var source = MetadataSource.Open(typeof(SharedGuardSwitchFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(SharedGuardSwitchFixture).FullName!,
            nameof(SharedGuardSwitchFixture.ExitEnclosingLoopFromDenseDefault));
        Assert.NotNull(function);
        Assert.Single(function.Descendants.OfType<SwitchBranch>());

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.Single(function.Descendants.OfType<DoWhileLoop>());
    }

    [Fact]
    public void CompilerProducedStringDefaultContainingEnclosingLoopBreak_RemainsFlat()
    {
        using var source = MetadataSource.Open(typeof(SharedGuardSwitchFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(SharedGuardSwitchFixture).FullName!,
            nameof(SharedGuardSwitchFixture.ExitEnclosingLoopFromStringDefault));
        Assert.NotNull(function);
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        Assert.NotEmpty(function.Descendants.OfType<ConditionalBranch>());

        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<Switch>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        Assert.NotEmpty(function.Descendants.OfType<ConditionalBranch>());
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

    [Fact]
    public void DenseDefaultContainingNestedLoopBreak_StillRaises()
    {
        var function = BuildDenseSwitchWithOwnedBreak(
            "DenseLoop",
            NestedBreakLoop());

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        var nestedLoop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(nestedLoop.Descendants.OfType<Break>());
    }

    [Fact]
    public void DenseDefaultContainingNestedSwitchBreak_StillRaises()
    {
        var function = BuildDenseSwitchWithOwnedBreak(
            "DenseSwitch",
            NestedBreakSwitch());

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        var switches = function.Descendants.OfType<Switch>().ToList();
        Assert.Equal(2, switches.Count);
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        var nestedSwitch = Assert.Single(
            switches,
            node => node.Value is Constant);
        Assert.Single(nestedSwitch.Descendants.OfType<Break>());
    }

    [Fact]
    public void StringDefaultContainingNestedLoopBreak_StillRaises()
    {
        var function = BuildStringEqualitySwitchWithNestedLoopBreak();

        new SwitchRaisingPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Single(function.Descendants.OfType<Switch>());
        Assert.Empty(function.Descendants.OfType<SwitchBranch>());
        var nestedLoop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(nestedLoop.Descendants.OfType<Break>());
    }

    static IrFunction BuildDenseSwitchWithOwnedBreak(
        string name,
        IrNode nestedOwner)
    {
        var body = new BlockContainer();

        var dispatch = new Block(0);
        dispatch.Add(new SwitchBranch(
            new LoadArgument(0, "value", s_int),
            [0x10, 0x20]));
        body.Add(dispatch);

        var defaultBlock = new Block(4);
        defaultBlock.Add(nestedOwner);
        defaultBlock.Add(new Branch(0x30));
        body.Add(defaultBlock);

        var case0 = new Block(0x10);
        case0.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        case0.Add(new Branch(0x30));
        body.Add(case0);

        var case1 = new Block(0x20);
        case1.Add(new StoreLocal(0, s_int, new Constant(2, s_int)));
        case1.Add(new Branch(0x30));
        body.Add(case1);

        var join = new Block(0x30);
        join.Add(new Return(new LoadLocal(0, s_int)));
        body.Add(join);

        return Function(
            name,
            [new Parameter("value", s_int)],
            body);
    }

    static IrFunction BuildStringEqualitySwitchWithNestedLoopBreak()
    {
        var body = new BlockContainer();
        var equals = new MethodRef(
            s_string,
            "op_Equality",
            s_bool,
            [s_string, s_string],
            HasThis: false);
        IrExpression Is(string value) => new Call(
            equals,
            isVirtual: false,
            [
                new LoadArgument(0, "value", s_string),
                new Constant(value, s_string),
            ]);

        var firstTest = new Block(0);
        firstTest.Add(new ConditionalBranch(Is("a"), 0x20));
        body.Add(firstTest);

        var secondTest = new Block(8);
        secondTest.Add(new ConditionalBranch(Is("b"), 0x30));
        body.Add(secondTest);

        var defaultBlock = new Block(0x10);
        defaultBlock.Add(NestedBreakLoop());
        defaultBlock.Add(new Branch(0x40));
        body.Add(defaultBlock);

        var caseA = new Block(0x20);
        caseA.Add(new StoreLocal(0, s_int, new Constant(1, s_int)));
        caseA.Add(new Branch(0x40));
        body.Add(caseA);

        var caseB = new Block(0x30);
        caseB.Add(new StoreLocal(0, s_int, new Constant(2, s_int)));
        caseB.Add(new Branch(0x40));
        body.Add(caseB);

        var join = new Block(0x40);
        join.Add(new Return(new LoadLocal(0, s_int)));
        body.Add(join);

        return Function(
            "String",
            [new Parameter("value", s_string)],
            body);
    }

    static WhileLoop NestedBreakLoop()
    {
        var body = new Block();
        body.Add(new Break());
        return new WhileLoop(new Constant(true, s_bool), body);
    }

    static Switch NestedBreakSwitch()
    {
        var block = new Block();
        block.Add(new Break());
        var body = new BlockContainer();
        body.Add(block);
        return new Switch(
            new Constant(0, s_int),
            [new SwitchSection([], isDefault: true, body)]);
    }

    static IrFunction Function(
        string name,
        Parameter[] parameters,
        BlockContainer body)
        => new(
            name,
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(
                s_int,
                [.. parameters],
                HasThis: false,
                GenericParameterCount: 0),
            [s_int],
            body);

    static IrFunction Import(string methodName)
    {
        using var source = MetadataSource.Open(typeof(SharedGuardSwitchFixture).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(SharedGuardSwitchFixture).FullName!,
            methodName);
        Assert.NotNull(function);
        return function;
    }

    static IrFunction BuildSharedGuardSwitch(
        TypeRef? governingType = null,
        bool switchIsChecked = false,
        int switchOffset = 24,
        bool guardTargetsCaseBody = false,
        bool defaultExitsToContinuation = false,
        bool defaultContainsNestedLoopBreak = false)
    {
        governingType ??= s_int;
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
                    new LoadArgument(0, "algorithm", governingType),
                    new Constant(6, s_int)),
                new Constant(1, s_int)),
            guardTargetsCaseBody ? 0x20 : 0x30));
        body.Add(guard);

        var dispatch = new Block(0x0D);
        dispatch.Add(new SwitchBranch(
            new Binary(
                BinaryKind.Subtract,
                isChecked: switchIsChecked,
                isUnsigned: false,
                new LoadArgument(0, "algorithm", governingType),
                new Constant(switchOffset, s_int)),
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
                [new Parameter("algorithm", governingType)],
                HasThis: false,
                GenericParameterCount: 0),
            defaultExitsToContinuation ? [s_int] : [],
            body);
    }
}
