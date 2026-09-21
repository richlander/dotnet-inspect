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

    [Fact]
    public void LocalDeclarationPlan_OwnsOnlyMaterializedLocals()
    {
        var nested = new Block();
        var localStore = new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32));
        nested.Add(localStore);
        nested.Add(Observe(0));
        var entry = new Block();
        var stackStore = new StoreStackSlot(
            0,
            new Constant(2, Int32));
        entry.Add(stackStore);
        entry.Add(new ExpressionStatement(
            new LoadStackSlot(0, Int32)));
        entry.Add(nested);
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32],
            body)
        {
            LocalDeclaredInNestedScope = [true],
        };

        var plan = LocalDeclarationPlan.Create(function, 1);

        Assert.Contains(localStore, plan.DeclaringNodes);
        Assert.DoesNotContain(stackStore, plan.DeclaringNodes);
        Assert.Same(nested, plan.DeclarationScopes[0]);
    }

    [Fact]
    public void LocalDeclarationPlan_PlansRaisedLambdaBody()
    {
        var lambdaBody = new BlockContainer();
        var lambdaEntry = new Block();
        lambdaEntry.Add(new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32)));
        lambdaEntry.Add(Observe(0));
        lambdaBody.Add(lambdaEntry);
        var lambda = new Lambda(
            TypeRef.CoreLib("System", "Action"),
            [],
            [Int32],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);

        var plan = LocalDeclarationPlan.Create(lambda, 1);

        Assert.Contains(
            plan.DeclaringNodes,
            node => node is StoreLocal { Index: 0 });
        Assert.All(
            plan.DeclaringNodes.OfType<StoreLocal>()
                .SelectMany(store =>
                    IrFunction.LocalSlotReferencesInScope(
                        store.Parent!,
                        store.Index)),
            reference => Assert.True(
                ExactLocalNameAllocation.Contains(
                    plan.DeclarationScopes[0],
                    reference)));
    }

    [Theory]
    [InlineData(nameof(PdbScopeFixtures.DisjointScopeLocals))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocals))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocalsWithGoto))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocalsWithInternalLabels))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocalsWithEntryLabels))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocalsWithEntryAndInternalLabels))]
    [InlineData(nameof(PdbScopeFixtures.SequentialScopeLocalsWithTrailingTransfer))]
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
        if (method is nameof(PdbScopeFixtures.SequentialScopeLocalsWithGoto)
            or nameof(PdbScopeFixtures.SequentialScopeLocalsWithInternalLabels)
            or nameof(PdbScopeFixtures.SequentialScopeLocalsWithEntryLabels)
            or nameof(PdbScopeFixtures.SequentialScopeLocalsWithEntryAndInternalLabels)
            or nameof(PdbScopeFixtures.SequentialScopeLocalsWithTrailingTransfer))
        {
            Assert.Contains(
                function.Descendants,
                node => node is Branch or ConditionalBranch or Leave);
        }
        if (method is nameof(PdbScopeFixtures.SequentialScopeLocalsWithInternalLabels)
            or nameof(PdbScopeFixtures.SequentialScopeLocalsWithEntryAndInternalLabels))
            Assert.NotEmpty(function.Descendants.OfType<LabelAnchor>());
        if (method == nameof(PdbScopeFixtures.SequentialScopeLocalsWithEntryLabels))
        {
            Assert.Contains("IL_0005:\n{\n    int same =", result.Output);
            Assert.Contains("IL_0016:\n{\n    string same =", result.Output);
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
    public void ScopeEntryPatternCarriers_PreserveBothExactNames()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.ScopeEntryPatternLocals))!;

        var result = CSharpPrinter.PrintRaised(
            function, member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.True(result.Fidelity == DecompilationFidelity.Full,
            $"{result.Output}\n{CSharpSpellability.InspectUnrepresentableMetadataName(function)}\n{IrPrinter.Dump(function)}");
        Assert.Contains("ScopeEntryLocal same", result.Output);
        Assert.Contains("ScopeEntryField same", result.Output);
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
    public void InScopeTrailingTransfer_ClosesCrossBlockExactLocalRange(
        string transferKind)
    {
        var function = ScopeTailTransfer(
            transferKind,
            transferOffset: 30,
            firstScope: new LocalSlotScope(10, 31));

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Contains(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.SourceOffset == 20 && anchor.RetainsPdbLocalScope);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("V_", result.Output);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void OmittedBlockStartLabels_CloseTransitivelyWithinExactPdbScope()
    {
        var declaration = new Block(10);
        var firstStore = new StoreLocal(0, Int32, new Constant(1, Int32));
        firstStore.SetSourceOffset(10);
        declaration.Add(firstStore);

        var use = new Block(20);
        use.Add(Marker(21, 0));

        var firstTail = new Block(30);
        IrNode firstTransfer = Transfer("conditional", 20);
        firstTransfer.SetSourceOffset(31);
        firstTail.Add(firstTransfer);

        var secondTail = new Block(40);
        IrNode secondTransfer = Transfer("branch", 30);
        secondTransfer.SetSourceOffset(41);
        secondTail.Add(secondTransfer);

        var second = new Block(50);
        var secondStore = new StoreLocal(1, Int32, new Constant(2, Int32));
        secondStore.SetSourceOffset(50);
        second.Add(secondStore);
        second.Add(Observe(1));

        var body = new BlockContainer();
        body.Add(declaration);
        body.Add(use);
        body.Add(firstTail);
        body.Add(secondTail);
        body.Add(second);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
            LocalDeclarationBindings =
            [
                new PdbLocalDeclaration(
                    1,
                    1,
                    0,
                    "same",
                    new LocalSlotScope(10, 42),
                    System.Reflection.Metadata.LocalVariableAttributes.None),
                new PdbLocalDeclaration(
                    2,
                    2,
                    1,
                    "same",
                    new LocalSlotScope(50, 60),
                    System.Reflection.Metadata.LocalVariableAttributes.None),
            ],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Contains(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.SourceOffset == 20 && anchor.RetainsPdbLocalScope);
        Assert.Contains(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.SourceOffset == 30 && anchor.RetainsPdbLocalScope);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("V_", result.Output);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);
    }

    [Theory]
    [InlineData(9, 10, 31)]
    [InlineData(30, 10, 30)]
    public void TransferOutsideExactPdbScope_DoesNotCloseCrossBlockRange(
        int transferOffset,
        int scopeStart,
        int scopeEnd)
    {
        var function = ScopeTailTransfer(
            "conditional",
            transferOffset,
            new LocalSlotScope(scopeStart, scopeEnd));

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.DoesNotContain(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.RetainsPdbLocalScope);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ConsecutiveBasicBlockRanges_PreserveExactNames(
        string transferKind)
    {
        var firstDeclaration = new Block(0);
        firstDeclaration.Add(new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32)));
        var firstUse = new Block(10);
        firstUse.Add(Transfer(transferKind, 30));
        firstUse.Add(Observe(0));
        var secondDeclaration = new Block(30);
        secondDeclaration.Add(Marker(30));
        secondDeclaration.Add(new StoreLocal(
            1,
            Int32,
            new Constant(2, Int32)));
        var secondUse = new Block(40);
        secondUse.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(firstDeclaration);
        body.Add(firstUse);
        body.Add(secondDeclaration);
        body.Add(secondUse);
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

        Assert.Equal(2, function.Descendants.OfType<Block>().Count(
            block => block.Parent is Block));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("V_", result.Output);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void SameNamedLocalInsideBasicBlockRange_LeavesCollisionVisible()
    {
        var first = new Block(0);
        var firstStore = new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32));
        first.Add(firstStore);
        var second = new Block(10);
        second.Add(new StoreLocal(
            1,
            Int32,
            new Constant(2, Int32)));
        var firstUse = new Block(20);
        firstUse.Add(Observe(0));
        firstUse.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        body.Add(firstUse);
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

        Assert.Same(first, firstStore.Parent);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
    }

    [Fact]
    public void SwitchSections_UseExplicitBlocksForRepeatedExactNames()
    {
        var firstBlock = new Block();
        firstBlock.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        firstBlock.Add(Observe(0));
        firstBlock.Add(new Break());
        var firstBody = new BlockContainer();
        firstBody.Add(firstBlock);
        var secondBlock = new Block();
        secondBlock.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        secondBlock.Add(Observe(1));
        secondBlock.Add(new Break());
        var secondBody = new BlockContainer();
        secondBody.Add(secondBlock);
        var selector = new Parameter("selector", Int32);
        var entry = new Block();
        entry.Add(new Switch(
            new LoadArgument(0, selector),
            [
                new SwitchSection([new Constant(0, Int32)], false, firstBody),
                new SwitchSection([], true, secondBody),
            ]));
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [selector], false, 0),
            [Int32, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
        };

        var initialScopes =
            LocalDeclarationPlan.Create(function, 2).DeclarationScopes;
        Assert.Same(initialScopes[0], initialScopes[1]);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(2, function.Descendants.OfType<Block>().Count(
            block => block.Parent is Block));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("V_", result.Output);
    }

    [Fact]
    public void CompilerSwitchSectionOutVariables_UseExplicitBlocksForRepeatedExactNames()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.SwitchSectionOutVariables))!;

        var result = CSharpPrinter.PrintRaised(
            function,
            member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(5, result.Output!.Split(
            "out int same", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("out int V_", result.Output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void InternalTransferToConsumedBasicBlockLabel_PreservesExactNames(
        string transferKind)
    {
        var declaration = new Block(0);
        var firstStore = new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32));
        declaration.Add(firstStore);
        declaration.Add(Transfer(transferKind, 20));
        var firstUse = new Block(20);
        firstUse.Add(Observe(0));
        var second = new Block(30);
        second.Add(new StoreLocal(
            1,
            Int32,
            new Constant(2, Int32)));
        second.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(declaration);
        body.Add(firstUse);
        body.Add(second);
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

        Assert.NotSame(declaration, firstStore.Parent);
        Assert.Contains(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.SourceOffset == 20 && anchor.RetainsPdbLocalScope);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.Contains("IL_0014:", result.Output);
        Assert.DoesNotContain("V_", result.Output);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ExistingLexicalBlock_InternalTransfer_PreservesExactNames(
        string transferKind)
    {
        var function = ExistingLabeledScopes(
            entryTransferKind: "branch",
            internalTransferKind: transferKind,
            entryTargetsInternalLabel: false);
        int blocks = function.Descendants.OfType<Block>().Count();

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(blocks, function.Descendants.OfType<Block>().Count());
        Assert.Contains(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.SourceOffset == 20 && anchor.RetainsPdbLocalScope);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.Contains("IL_000A:\n{\n    int same = 1;", result.Output);
        Assert.DoesNotContain("V_", result.Output);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ExistingLexicalBlock_ContainingEntry_PreservesExactNames(
        string transferKind)
    {
        var function = ExistingLabeledScopes(
            entryTransferKind: transferKind,
            internalTransferKind: "branch",
            entryTargetsInternalLabel: false);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.Contains("IL_000A:\n{\n    int same = 1;", result.Output);
        Assert.DoesNotContain("V_", result.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrecedingEntryAnchor_RequiresRetainedPdbScopeProof(bool retainsPdbLocalScope)
    {
        var first = new Block(10);
        var anchor = new LabelAnchor { RetainsPdbLocalScope = retainsPdbLocalScope };
        anchor.SetSourceOffset(20);
        first.Add(anchor);
        var firstStore = new StoreLocal(0, Int32, new Constant(1, Int32));
        firstStore.SetSourceOffset(20);
        first.Add(firstStore);
        first.Add(Observe(0));
        first.Add(Transfer("conditional", 20));

        var second = new Block(30);
        second.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        second.Add(Observe(1));

        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
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

        Assert.Equal(retainsPdbLocalScope, !ReferenceEquals(first, firstStore.Parent));
        Assert.Same(anchor, first.Children[0]);
        Assert.Contains("IL_0014:", result.Output);
        Assert.Equal(
            retainsPdbLocalScope ? DecompilationFidelity.Full : DecompilationFidelity.Partial,
            result.Fidelity);
        Assert.Equal(
            retainsPdbLocalScope ? 2 : 1,
            result.Output!.Split("int same =", StringSplitOptions.None).Length - 1);
        Assert.Equal(!retainsPdbLocalScope, result.Output.Contains("V_1"));

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void PrecedingProofAnchor_CrossingExactRangesCloseInsideOut()
    {
        var first = new Block(10);
        var anchor = new LabelAnchor { RetainsPdbLocalScope = true };
        anchor.SetSourceOffset(20);
        first.Add(anchor);
        var firstOuterStore = new StoreLocal(0, Int32, new Constant(1, Int32));
        firstOuterStore.SetSourceOffset(20);
        first.Add(firstOuterStore);
        first.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        var internalAnchor = new LabelAnchor { RetainsPdbLocalScope = true };
        internalAnchor.SetSourceOffset(25);
        first.Add(internalAnchor);
        first.Add(Observe(0));
        first.Add(Transfer("conditional", 25));
        first.Add(Transfer("conditional", 20));
        first.Add(Observe(1));

        var second = new Block(30);
        second.Add(new StoreLocal(2, Int32, new Constant(3, Int32)));
        second.Add(new StoreLocal(3, Int32, new Constant(4, Int32)));
        second.Add(Observe(2));
        second.Add(Observe(3));

        var body = new BlockContainer();
        body.Add(first);
        body.Add(second);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32, Int32, Int32],
            body)
        {
            LocalNames = ["outer", "inner", "outer", "inner"],
            LocalDeclaredInNestedScope = [true, true, true, true],
            LocalDeclarationBindings =
            [
                PdbDeclaration(1, 1, 0, "outer", 10, 30),
                PdbDeclaration(2, 1, 1, "inner", 10, 30),
                PdbDeclaration(3, 2, 2, "outer", 30, 40),
                PdbDeclaration(4, 2, 3, "inner", 30, 40),
            ],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.True(
            result.Fidelity == DecompilationFidelity.Full,
            result.Output + Environment.NewLine + IrPrinter.Dump(function));
        Assert.Equal(2, result.Output!.Split(
            "int outer =", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, result.Output.Split(
            "int inner =", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("V_", result.Output);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);

        static PdbLocalDeclaration PdbDeclaration(
            int variableRow,
            int scopeRow,
            int slot,
            string name,
            int start,
            int end)
            => new(
                variableRow,
                scopeRow,
                slot,
                name,
                new LocalSlotScope(start, end),
                System.Reflection.Metadata.LocalVariableAttributes.None);
    }

    [Fact]
    public void CoDeclaredOutLocals_ComposeLongerLifetimeFirst()
    {
        var callee = new MethodRef(
            Owner,
            "GrowRegion",
            Boolean,
            [TypeRef.ByRef(Int32), TypeRef.ByRef(Int32)],
            HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Out, ArgumentRefKind.Out],
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
        };
        var entry = new Block();
        AddScope(entry, callee, region: 0, exits: 1);
        AddScope(entry, callee, region: 2, exits: 3);
        var body = new BlockContainer();
        body.Add(entry);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32, Int32, Int32],
            body)
        {
            LocalNames = ["region", "exits", "region", "exits"],
            LocalDeclaredInNestedScope = [true, true, true, true],
            LocalDeclarationBindings =
            [
                PdbDeclaration(1, 1, 0, "region", 10, 30),
                PdbDeclaration(2, 1, 1, "exits", 10, 30),
                PdbDeclaration(3, 2, 2, "region", 30, 50),
                PdbDeclaration(4, 2, 3, "exits", 30, 50),
            ],
        };

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.True(
            result.Fidelity == DecompilationFidelity.Full,
            result.Output + Environment.NewLine + IrPrinter.Dump(function));
        Assert.Equal(2, result.Output!.Split(
            "out int region", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, result.Output.Split(
            "out int exits", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("V_", result.Output);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(result.Output, CSharpPrinter.Print(function).Output);

        static void AddScope(Block block, MethodRef callee, int region, int exits)
        {
            block.Add(new ExpressionStatement(new Call(
                callee,
                isVirtual: false,
                [
                    new LoadLocalAddress(region, Int32),
                    new LoadLocalAddress(exits, Int32),
                ])));
            block.Add(Observe(exits));
            block.Add(Observe(region));
        }

        static PdbLocalDeclaration PdbDeclaration(
            int variableRow,
            int scopeRow,
            int slot,
            string name,
            int start,
            int end)
            => new(
                variableRow,
                scopeRow,
                slot,
                name,
                new LocalSlotScope(start, end),
                System.Reflection.Metadata.LocalVariableAttributes.None);
    }

    [Fact]
    public void NestedCompilerScopesWithEntryLabels_PreserveEveryExactName()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.NestedScopeLocalsWithEntryLabels))!;

        var result = CSharpPrinter.PrintRaised(
            function,
            member => IrImporter.Import(source, member));
        function.CheckInvariant();

        Assert.True(
            result.Fidelity == DecompilationFidelity.Full,
            result.Output + Environment.NewLine + IrPrinter.Dump(function));
        foreach (string declaration in new[]
        {
            "int currentPos =",
            "int splitIdx =",
            "string currentSplit =",
            "int typeIndex =",
            "Type type =",
            "int splitPoint =",
        })
        {
            int count = result.Output!.Split(
                declaration, StringSplitOptions.None).Length - 1;
            Assert.True(count == 2, $"{declaration}: {count}\n{result.Output}");
        }
        Assert.DoesNotContain("V_", result.Output);
        Assert.Contains(
            function.Descendants,
            node => node is LabelAnchor { RetainsPdbLocalScope: true });
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ExistingLexicalBlock_ExternalInternalEntry_LeavesCollisionVisible(
        string transferKind)
    {
        var function = ExistingLabeledScopes(
            entryTransferKind: transferKind,
            internalTransferKind: "branch",
            entryTargetsInternalLabel: true);

        new PdbLocalScopePass().Run(function, PassContext.None);
        function.CheckInvariant();
        var result = CSharpPrinter.Print(function);

        Assert.DoesNotContain(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.RetainsPdbLocalScope);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
    }

    [Fact]
    public void OrdinaryLabelAnchor_DoesNotRelaxDeclarationPlacement()
    {
        var lexical = new Block();
        lexical.Add(new StoreLocal(0, Int32, new Constant(1, Int32)));
        lexical.Add(new Branch(20));
        var anchor = new LabelAnchor();
        anchor.SetSourceOffset(20);
        lexical.Add(anchor);
        lexical.Add(Observe(0));
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
            LocalNames = ["exact"],
            LocalDeclaredInNestedScope = [true],
        };

        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output!;

        Assert.StartsWith("int exact = default;\n\n{\n", output);
        Assert.DoesNotContain("{\n    int exact =", output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ExternalTransferToConsumedBasicBlockLabel_LeavesCollisionVisible(
        string transferKind)
    {
        var entry = new Block(0);
        entry.Add(Transfer(transferKind, 20));
        var declaration = new Block(10);
        var firstStore = new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32));
        declaration.Add(firstStore);
        var firstUse = new Block(20);
        firstUse.Add(Observe(0));
        var second = new Block(30);
        second.Add(new StoreLocal(
            1,
            Int32,
            new Constant(2, Int32)));
        second.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(entry);
        body.Add(declaration);
        body.Add(firstUse);
        body.Add(second);
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

        Assert.Same(declaration, firstStore.Parent);
        Assert.DoesNotContain(
            function.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.SourceOffset == 20);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ExternalTransferToDeclarationBlockEntry_PreservesExactNames(
        string transferKind)
    {
        var entry = new Block(0);
        entry.Add(Transfer(transferKind, 10));
        var first = new Block(10);
        var firstStore = new StoreLocal(0, Int32, new Constant(1, Int32));
        first.Add(firstStore);
        first.Add(Observe(0));
        var second = new Block(20);
        second.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        second.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(entry);
        body.Add(first);
        body.Add(second);
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

        Assert.NotSame(first, firstStore.Parent);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, result.Output!.Split(
            "int same =", StringSplitOptions.None).Length - 1);
        Assert.Contains("IL_000A:\n{", result.Output);
        Assert.DoesNotContain("V_", result.Output);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("conditional")]
    [InlineData("switch")]
    [InlineData("leave")]
    public void ExternalTransferIntoBasicBlockRange_LeavesCollisionVisible(
        string transferKind)
    {
        var entry = new Block(0);
        entry.Add(Transfer(transferKind, 20));
        var declaration = new Block(10);
        var firstStore = new StoreLocal(
            0,
            Int32,
            new Constant(1, Int32));
        declaration.Add(firstStore);
        declaration.Add(Marker(20));
        var firstUse = new Block(30);
        firstUse.Add(Observe(0));
        var second = new Block(40);
        second.Add(new StoreLocal(
            1,
            Int32,
            new Constant(2, Int32)));
        second.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(entry);
        body.Add(declaration);
        body.Add(firstUse);
        body.Add(second);
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

        Assert.Same(declaration, firstStore.Parent);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("V_1", result.Output);
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

    static IrFunction ExistingLabeledScopes(
        string entryTransferKind,
        string internalTransferKind,
        bool entryTargetsInternalLabel)
    {
        var entry = new Block(0);
        entry.Add(Transfer(entryTransferKind, entryTargetsInternalLabel ? 20 : 10));

        var firstStore = new StoreLocal(0, Int32, new Constant(1, Int32));
        firstStore.SetSourceOffset(10);
        var anchor = new LabelAnchor();
        anchor.SetSourceOffset(20);
        var firstLexical = new Block(10);
        firstLexical.Add(firstStore);
        firstLexical.Add(Transfer(internalTransferKind, 20));
        firstLexical.Add(anchor);
        firstLexical.Add(Observe(0));
        var first = new Block(10);
        first.Add(firstLexical);

        var secondLexical = new Block(30);
        secondLexical.Add(new StoreLocal(1, Int32, new Constant(2, Int32)));
        secondLexical.Add(Observe(1));
        var second = new Block(30);
        second.Add(secondLexical);

        var body = new BlockContainer();
        body.Add(entry);
        body.Add(first);
        body.Add(second);
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
    }

    static IrFunction ScopeTailTransfer(
        string transferKind,
        int transferOffset,
        LocalSlotScope firstScope)
    {
        var declaration = new Block(10);
        var firstStore = new StoreLocal(0, Int32, new Constant(1, Int32));
        firstStore.SetSourceOffset(10);
        declaration.Add(firstStore);
        var use = new Block(20);
        use.Add(Marker(20, 0));
        var tail = new Block(30);
        IrNode transfer = Transfer(transferKind, 20);
        transfer.SetSourceOffset(transferOffset);
        tail.Add(transfer);
        var second = new Block(40);
        var secondStore = new StoreLocal(1, Int32, new Constant(2, Int32));
        secondStore.SetSourceOffset(40);
        second.Add(secondStore);
        second.Add(Observe(1));
        var body = new BlockContainer();
        body.Add(declaration);
        body.Add(use);
        body.Add(tail);
        body.Add(second);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], false, 0),
            [Int32, Int32],
            body)
        {
            LocalNames = ["same", "same"],
            LocalDeclaredInNestedScope = [true, true],
            LocalDeclarationBindings =
            [
                new PdbLocalDeclaration(
                    1,
                    1,
                    0,
                    "same",
                    firstScope,
                    System.Reflection.Metadata.LocalVariableAttributes.None),
                new PdbLocalDeclaration(
                    2,
                    2,
                    1,
                    "same",
                    new LocalSlotScope(40, 50),
                    System.Reflection.Metadata.LocalVariableAttributes.None),
            ],
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
