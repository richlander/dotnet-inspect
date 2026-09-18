using CSharpText.Tests;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class PdbLocalDeclarationScopeTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner = TypeRef.Definition("Tests", "Samples", "Scopes");
    static readonly MethodRef StringValue = new(
        Owner, "get_Value", String, [], HasThis: true);

    [Theory]
    [InlineData(nameof(PdbScopeFixtures.DisjointScopeLocals))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocals))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocalsWithGoto))]
    [InlineData(nameof(PdbScopeFixtures.LambdaScopes))]
    [InlineData(nameof(PdbScopeFixtures.LocalFunctionScopes))]
    public void DisjointCompilerScopes_PreserveBothExactNames(string method)
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            method)!;

        var result = CSharpPrinter.PrintRaised(function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.True(result.Fidelity == DecompilationFidelity.Full,
            $"{method}: {result.Output}\n{CSharpSpellability.InspectUnrepresentableMetadataName(function)}\n{IrPrinter.Dump(function)}");
        Assert.Contains("int same = value;", result.Output);
        Assert.Contains("string same = value.ToString();", result.Output);
        Assert.Contains("Increment(ref same);", result.Output);
        Assert.Contains("KeepAlive(ref same);", result.Output);
        Assert.DoesNotContain("V_", result.Output);
        if (method == nameof(PdbScopeFixtures.SequentialScopeLocalsWithGoto))
        {
            Assert.Contains(
                function.Descendants,
                node => node is Branch or ConditionalBranch or Leave);
        }
    }

    [Fact]
    public void NoPdb_DoesNotIntroduceLexicalBlocks()
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.DisjointScopeLocals))!;
        IrPasses.Run(function, [.. IrPasses.Default.Where(pass => pass is not PdbLocalScopePass)]);
        string before = CSharpPrinter.Print(function).Output!;

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(before, CSharpPrinter.Print(function).Output);
        Assert.DoesNotContain("same", before);
    }

    [Fact]
    public void SequentialValueTypeCompilerScopes_PreserveBothExactNames()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SequentialValueTypeScopeLocals))!;

        var result = CSharpPrinter.PrintRaised(function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "Guid same = default;", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, result.Output.Split(
            "KeepGuidAlive(ref same);", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Guid V_", result.Output);
    }

    [Fact]
    public void SiblingBlocks_PreserveNamesWithoutAdditionalWrapping()
    {
        var function = Siblings();
        int blocks = function.Descendants.OfType<Block>().Count();

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(blocks, function.Descendants.OfType<Block>().Count());
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("int same = 1;", result.Output);
        Assert.Contains("int same = 2;", result.Output);
    }

    [Fact]
    public void OverlappingUses_KeepCollisionVisible()
    {
        var function = Siblings();
        function.Body.Blocks[0].Add(Observe(0));

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Contains("V_1", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void ParameterReservation_IsNotBypassedByDisjointScopes()
    {
        var function = Siblings(new Parameter("same", Boolean));

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int V_0 = 1;", output);
        Assert.Contains("int V_1 = 2;", output);
    }

    [Fact]
    public void ScopedPass_IsIdempotent()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.DisjointScopeLocals))!;
        IrPasses.Run(function);
        string before = CSharpPrinter.Print(function).Output!;

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(before, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void InlinedStackCarry_DoesNotPretendScopesAreDisjoint()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SequentialStackCarry))!;
        IrPasses.Run(function, [.. IrPasses.Default.Where(pass => pass is not PdbLocalScopePass)]);
        string before = CSharpPrinter.Print(function).Output!;

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(before, CSharpPrinter.Print(function).Output);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void SequentialPatternScopes_PreserveBothExactNames()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SequentialPatterns))!;

        var result = CSharpPrinter.PrintRaised(function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("if (first is string value)", result.Output);
        Assert.Contains("if (second is string value)", result.Output);
        Assert.DoesNotContain(" V_", result.Output);
    }

    [Fact]
    public void SequentialOutVariableScopes_PreserveBothExactNames()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SequentialOutVariables))!;

        var result = CSharpPrinter.PrintRaised(function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "out int value", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(" V_", result.Output);
    }

    [Fact]
    public void SwitchExpressionOutVariableScopes_PreserveBothExactNames()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SwitchExpressionOutVariables))!;

        var result = CSharpPrinter.PrintRaised(function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "out int value", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(" V_", result.Output);
    }

    [Theory]
    [InlineData(ArgumentRefKind.Ref, ParameterRefKindFacts.Known)]
    [InlineData(ArgumentRefKind.Out, ParameterRefKindFacts.Unknown)]
    public void NonVerifiedOutArguments_LeaveCollisionVisible(
        ArgumentRefKind refKind,
        ParameterRefKindFacts facts)
    {
        var function = SequentialAddressCalls(refKind, facts);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
    }

    [Fact]
    public void OutArgumentAfterEarlierUse_LeavesCollisionVisible()
    {
        var function = SequentialAddressCalls(
            ArgumentRefKind.Out,
            ParameterRefKindFacts.Known,
            readSecondBeforeCall: true);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
    }

    [Fact]
    public void OutArgumentInWhileConditionWithLaterUse_LeavesCollisionVisible()
    {
        var callee = new MethodRef(
            Owner,
            "TryRead",
            Boolean,
            [TypeRef.ByRef(Int32)],
            HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Out],
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
        };
        var entry = new Block();
        entry.Add(LoopAndObserve(0));
        entry.Add(LoopAndObserve(1));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
        Assert.DoesNotContain("out int same", result.Output);

        Block LoopAndObserve(int index)
        {
            var lexical = new Block();
            lexical.Add(new WhileLoop(
                new Call(
                    callee,
                    isVirtual: false,
                    [new LoadLocalAddress(index, Int32)]),
                new Block()));
            lexical.Add(Observe(index));
            return lexical;
        }
    }

    [Theory]
    [InlineData("using")]
    [InlineData("foreach")]
    [InlineData("fixed")]
    [InlineData("catch-filter")]
    public void OutArgumentInStatementHeaderWithLaterUse_LeavesCollisionVisible(
        string header)
    {
        TypeRef returnType = header == "catch-filter" ? Boolean : Object;
        TypeRef headerLocalType = header == "fixed"
            ? TypeRef.Pinned(TypeRef.ByRef(Int32))
            : header == "using"
                ? Object
                : Int32;
        var callee = new MethodRef(
            Owner,
            "Open",
            returnType,
            [TypeRef.ByRef(Int32)],
            HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Out],
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
        };
        var entry = new Block();
        entry.Add(HeaderAndObserve(outLocal: 0, headerLocal: 2));
        entry.Add(HeaderAndObserve(outLocal: 1, headerLocal: 3));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32, headerLocalType, headerLocalType],
            body)
        {
            LocalNames = ["same", "same", "header1", "header2"],
            LocalDeclaredInNestedScope = [true, true, false, false],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
        Assert.DoesNotContain("out int same", result.Output);

        Block HeaderAndObserve(int outLocal, int headerLocal)
        {
            var call = new Call(
                callee,
                isVirtual: false,
                [new LoadLocalAddress(outLocal, Int32)]);
            IrNode statement = header switch
            {
                "using" => new UsingStatement(
                    headerLocal,
                    Object,
                    call,
                    Container()),
                "foreach" => new ForeachStatement(
                    headerLocal,
                    Int32,
                    call,
                    new Block()),
                "fixed" => new Fixed(
                    Int32,
                    headerLocal,
                    call,
                    Container(),
                    sourceIsAddress: false),
                "catch-filter" => new TryCatch(
                    Container(),
                    [new CatchClause(Object, Container(), call)]),
                _ => throw new ArgumentOutOfRangeException(nameof(header)),
            };
            var lexical = new Block();
            lexical.Add(statement);
            lexical.Add(Observe(outLocal));
            return lexical;
        }

        static BlockContainer Container()
        {
            var container = new BlockContainer();
            container.Add(new Block());
            return container;
        }
    }

    [Fact]
    public void UniqueOutArgument_RemainsAtFunctionScope()
    {
        var callee = new MethodRef(
            Owner,
            "TryRead",
            Boolean,
            [TypeRef.ByRef(Int32)],
            HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Out],
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
        };
        var then = new Block();
        then.Add(Observe(0));
        var lexical = new Block();
        lexical.Add(new IfStatement(
            new Call(callee, isVirtual: false, [new LoadLocalAddress(0, Int32)]),
            then,
            null));
        var entry = new Block();
        entry.Add(lexical);
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32],
            body)
        {
            LocalNames = ["value"],
            LocalDeclaredInNestedScope = [true],
        };

        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output!;

        Assert.StartsWith("int value;\n\n{\n", output);
        Assert.DoesNotContain("{\n    int value;", output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void TransfersOutOfCandidateRanges_PreserveExactNames(string transferKind)
    {
        var entry = new Block();
        entry.Add(new StoreLocal(0, Boolean, new Constant(true, Boolean)));
        entry.Add(Transfer(transferKind, 30));
        entry.Add(ObserveLocal(0, Boolean));
        entry.Add(Marker(30));
        entry.Add(new StoreLocal(1, Boolean, new Constant(false, Boolean)));
        entry.Add(Transfer(transferKind, 60));
        entry.Add(ObserveLocal(1, Boolean));
        entry.Add(Marker(60));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Boolean, Boolean],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "bool same =", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("V_", result.Output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ExternalTransferIntoCandidateRange_LeavesCollisionVisible(
        string transferKind)
    {
        var entry = new Block();
        entry.Add(Transfer(transferKind, 20));
        var firstDeclaration = new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32));
        entry.Add(firstDeclaration);
        entry.Add(Marker(20, 0));
        entry.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        entry.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Same(entry, firstDeclaration.Parent);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
    }

    [Fact]
    public void SequentialConstructorOutArguments_PreserveBothExactNames()
    {
        var constructor = new MethodRef(
            Owner,
            ".ctor",
            Void,
            [TypeRef.ByRef(Int32)],
            HasThis: true)
        {
            ParameterRefKinds = [ArgumentRefKind.Out],
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
        };
        var entry = new Block();
        entry.Add(Construction(0));
        entry.Add(Construction(1));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "out int same", StringSplitOptions.None).Length - 1);

        Block Construction(int index)
        {
            var block = new Block();
            block.Add(new ExpressionStatement(new NewObject(
                constructor,
                [new LoadLocalAddress(index, Int32)])));
            block.Add(Observe(index));
            return block;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingPatternScope_LeavesCollisionVisible(bool referenceOutsideFirstPattern)
    {
        var firstThen = new Block();
        firstThen.Add(ObserveLocal(0, String));
        var secondThen = new Block();
        secondThen.Add(ObserveLocal(1, Int32));
        var second = new IfStatement(
            new IsPattern(new LoadArgument(1, "second", Object), Int32, 1),
            secondThen,
            null);
        var first = new IfStatement(
            new IsPattern(new LoadArgument(0, "first", Object), String, 0),
            firstThen,
            null);
        var entry = new Block();
        if (referenceOutsideFirstPattern)
        {
            entry.Add(first);
            entry.Add(ObserveLocal(0, String));
            entry.Add(second);
        }
        else
        {
            firstThen.Add(second);
            entry.Add(first);
        }
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction("M", Owner,
            new MethodSignature(Void,
                [new Parameter("first", Object), new Parameter("second", Object)],
                false, 0),
            [String, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("is string same", result.Output);
        Assert.Contains("is int V_1", result.Output);
    }

    [Fact]
    public void SequentialPropertyPatternScopes_PreserveBothExactNames()
    {
        var firstThen = new Block();
        firstThen.Add(ObserveLocal(0, String));
        var secondThen = new Block();
        secondThen.Add(ObserveLocal(1, String));
        var entry = new Block();
        entry.Add(new IfStatement(
            new RecursivePropertyDeclarationPattern(
                new LoadArgument(0, "first", Object), StringValue, String, 0),
            firstThen,
            null));
        entry.Add(new IfStatement(
            new RecursivePropertyDeclarationPattern(
                new LoadArgument(1, "second", Object), StringValue, String, 1),
            secondThen,
            null));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction("M", Owner,
            new MethodSignature(Void,
                [new Parameter("first", Object), new Parameter("second", Object)],
                false, 0),
            [String, String],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "Value: string same", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(" V_", result.Output);
    }

    static IrFunction Siblings(Parameter? parameter = null)
    {
        parameter ??= new Parameter("condition", Boolean);
        var then = new Block();
        then.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        then.Add(Observe(0));
        var otherwise = new Block();
        otherwise.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        otherwise.Add(Observe(1));
        var entry = new Block();
        entry.Add(new IfStatement(new LoadArgument(0, parameter), then, otherwise));
        var body = new BlockContainer();
        body.Add(entry);
        return new IrFunction("M", Owner, new MethodSignature(Void, [parameter], false, 0), [Int32, Int32], body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };
    }

    static IrFunction SequentialAddressCalls(
        ArgumentRefKind refKind,
        ParameterRefKindFacts facts,
        bool readSecondBeforeCall = false)
    {
        var callee = new MethodRef(
            Owner,
            "TryRead",
            Boolean,
            [TypeRef.ByRef(Int32)],
            HasThis: false)
        {
            ParameterRefKinds = [refKind],
            ParameterRefKindsFacts = facts,
        };
        var entry = new Block();
        entry.Add(OutCall(0));
        if (readSecondBeforeCall)
            entry.Add(Observe(1));
        entry.Add(OutCall(1));
        var body = new BlockContainer();
        body.Add(entry);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        IfStatement OutCall(int index)
        {
            var then = new Block();
            then.Add(Observe(index));
            return new IfStatement(
                new Call(
                    callee,
                    isVirtual: false,
                    [new LoadLocalAddress(index, Int32)]),
                then,
                null);
        }
    }

    static IrNode Observe(int index)
        => new ExpressionStatement(new Call(
            new MethodRef(Owner, "Observe", Void, [Int32], false),
            false,
            [new LoadLocal(index, Int32)]));

    static IrNode ObserveLocal(int index, TypeRef type)
        => new ExpressionStatement(new Call(
            new MethodRef(Owner, "Observe", Void, [type], false),
            false,
            [new LoadLocal(index, type)]));

    static IrNode Transfer(string kind, int targetOffset)
        => kind switch
        {
            "branch" => new Branch(targetOffset),
            "conditional" => new ConditionalBranch(
                new Constant(true, Boolean),
                targetOffset),
            "switch" => new SwitchBranch(
                new Constant(0, Int32),
                [targetOffset]),
            "leave" => new Leave(targetOffset),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static IrNode Marker(int offset, int? local = null)
    {
        IrNode statement = local is { } index
            ? Observe(index)
            : new ExpressionStatement(new Call(
                new MethodRef(Owner, "Observe", Void, [], false),
                false,
                []));
        statement.SetSourceOffset(offset);
        return statement;
    }
}
