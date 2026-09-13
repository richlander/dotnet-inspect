using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RetainedMergeStructuringTests
{
    static readonly TypeRef I32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "RetainedMerge");

    [Fact]
    public void SequentialRetainedMergesStructureFurthestRangeAndTail()
    {
        var (function, diagnostics) = Structure(SequentialRetainedMerges());

        Assert.Equal(2, diagnostics.RetainedRegions);
        Assert.Equal(3, function.Body.Blocks.Count);
        Assert.Equal(4, function.Descendants.OfType<StoreLocal>().Count());
        Assert.Equal(2, function.Descendants.OfType<Branch>().Count());
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 40);
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 80);

        var firstRetained = Assert.Single(
            function.Descendants.OfType<IfStatement>(),
            statement => statement.Then.Children.OfType<Branch>().Any(branch => branch.TargetOffset == 40));
        Assert.IsType<Comparison>(firstRetained.Condition);

        Assert.Equal(40, function.Body.Blocks[1].Children[0].SourceOffset);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        Assert.Contains("if (", output);
        Assert.Contains("goto IL_0028;", output);
        Assert.Contains("IL_0028:", output);
        Assert.Contains("goto IL_0050;", output);
        Assert.Contains("IL_0050:", output);
    }

    [Fact]
    public void RetainedAuditRecordsOnlyInstalledReplacement()
    {
        var successStepper = new Stepper(enabled: true);
        var (_, successDiagnostics) = Structure(
            SequentialRetainedMerges(),
            stepper: successStepper);

        Assert.Equal(1, successDiagnostics.Structured);
        Assert.Equal(2, successDiagnostics.RetainedRegions);
        Assert.Empty(successDiagnostics.Stops);
        Assert.Empty(successDiagnostics.RetainedDeclines);
        Assert.Single(
            successStepper.Steps,
            step => step.Description.Contains("2 retained-merge region(s)", StringComparison.Ordinal));

        var declineStepper = new Stepper(enabled: true);
        var (declined, declineDiagnostics) = Structure(
            RetainedBodyMergeNestedBelowGotoBlocks(),
            stepper: declineStepper);

        Assert.Equal(0, declineDiagnostics.RetainedRegions);
        Assert.Equal(["cond-target-past-region"], declineDiagnostics.Stops);
        Assert.Equal(
            [
                "retained-dangling-merge-label",
                "retained-external-entry",
                "retained-back-edge-entangled",
                "retained-external-entry",
                "retained-external-entry",
                "retained-back-edge-region",
            ],
            declineDiagnostics.RetainedDeclines);
        Assert.Empty(declined.Descendants.OfType<WhileLoop>());
        Assert.Empty(declineStepper.Steps);
    }

    [Fact]
    public void RetainedAuditStepLimitStopsBeforeInstallationAndSuccessRecords()
    {
        var function = CreateFunction(
            SequentialRetainedMerges(),
            parameters: null,
            usesUpdatedMemorySafetyRules: false,
            out var originalBody);
        string originalIr = IrPrinter.Dump(function);
        var diagnostics = new StructuringDiagnostics();
        var stepper = new Stepper(enabled: true) { StepLimit = 0 };

        Assert.Throws<StepLimitReachedException>(
            () => new StructuringPass().Run(
                function,
                new PassContext(stepper, diagnostics)));

        Assert.Same(originalBody, function.Body);
        Assert.Equal(originalIr, IrPrinter.Dump(function));
        Assert.Equal(0, diagnostics.Structured);
        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(diagnostics.Stops);
        Assert.Empty(diagnostics.RetainedDeclines);
        Assert.Empty(stepper.Steps);
        function.CheckInvariant();
    }

    [Fact]
    public void CrossingForwardRegionsStayFlat()
    {
        var blocks = new[]
        {
            Term(0, Cond(5)),
            Term(1, new Branch(3)),
            Term(2, Cond(6)),
            Term(3, Cond(4)),
            Term(4, new Branch(5)),
            Term(5, Cond(6)),
            Term(6, new Return(null)),
        };

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-crossing", diagnostics.RetainedDeclines);
        Assert.Equal(blocks.Length, function.Body.Blocks.Count);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void CanonicalWhileWithRetainedBodyMergeRaises()
    {
        var blocks = RetainedLoopBlocks();

        var plan = StructuringJoinAnalysis.Analyze(blocks);
        Assert.Contains(plan.ForwardRegions, region =>
            region.Start == 1 && region.Merge == 6 && region.IsBackEdgeEntangled);
        Assert.Contains(plan.ForwardRegions, region =>
            region.Start == 6 && region.Merge == 11 && region.IsBackEdgeEntangled);
        Assert.NotEmpty(plan.BackEdgeRegions);

        var (function, diagnostics) = Structure(blocks);

        Assert.True(
            diagnostics.RetainedRegions == 1,
            string.Join(", ", diagnostics.RetainedDeclines));
        Assert.DoesNotContain("retained-back-edge-region", diagnostics.RetainedDeclines);
        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(loop.Body.Descendants.OfType<Break>());
        Assert.DoesNotContain(
            loop.Body.Descendants.OfType<Branch>(),
            branch => branch.TargetOffset == 1);
        Assert.Contains(
            loop.Body.Descendants.OfType<Branch>(),
            branch => branch.TargetOffset == 6);
        Assert.Contains(
            loop.Body.Descendants.OfType<Branch>(),
            branch => branch.TargetOffset == 11);
    }

    [Fact]
    public void RetainedLoopConditionalMergePreservesTransferKind()
    {
        var (function, diagnostics) = Structure(RetainedLoopBlocks());

        Assert.Equal(1, diagnostics.RetainedRegions);
        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Contains(
            loop.Body.Descendants.OfType<ConditionalBranch>(),
            branch => branch.TargetOffset == 6);
        Assert.Contains(
            loop.Body.Descendants.OfType<ConditionalBranch>(),
            branch => branch.TargetOffset == 11);
    }

    [Fact]
    public void RetainedLoopImportedConditionalTargetKeepsInstructionProvenanceAndBlockLabel()
    {
        var blocks = RetainedLoopBlocks();
        var retained = Cond(6);
        retained.SetSourceOffset(45);
        blocks[4] = Term(4, retained);

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(1, diagnostics.RetainedRegions);
        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Contains(
            loop.Body.Descendants.OfType<ConditionalBranch>(),
            branch => branch.SourceOffset == 45 && branch.TargetOffset == 6);
        Assert.Contains(
            loop.Body.Descendants.OfType<LabelAnchor>(),
            anchor => anchor.SourceOffset == 4);
    }

    [Fact]
    public void RetainedLoopSynthesizedConditionalMergeStaysFlat()
    {
        var blocks = RetainedLoopBlocks();
        blocks[2] = Term(
            2,
            Cond(6, ConditionalBranchOrigin.Synthesized));

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains(
            "retained-loop-synthesized-conditional-transfer",
            diagnostics.RetainedDeclines);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeWithoutRotatedEntryStaysFlat()
    {
        var blocks = RetainedLoopBlocks();
        blocks[0] = Term(0, Cond(12));

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeWithMultipleLatchesStaysFlat()
    {
        var blocks = RetainedLoopBlocks();
        blocks[3] = Term(3, new Branch(1));

        var plan = StructuringJoinAnalysis.Analyze(blocks);
        Assert.Contains(
            plan.BackEdgeRegions,
            region => region.Start == 1 && region.BackEdgeSources.Length == 2);

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeAtLatchStaysFlat()
    {
        var blocks = new[]
        {
            Term(0, new Branch(5)),
            Term(1, Cond(4)),
            Term(2, Cond(5)),
            Term(3, new Branch(4)),
            Term(4, new Branch(5)),
            Term(5, Cond(1)),
            Term(6, new Return(null)),
        };
        var plan = StructuringJoinAnalysis.Analyze(blocks);
        Assert.Contains(
            plan.ForwardRegions,
            region => region.Merge == 5 && region.IsBackEdgeEntangled);

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-back-edge-entangled", diagnostics.RetainedDeclines);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeWithNoncanonicalExitStaysFlat()
    {
        var blocks = RetainedLoopBlocks().ToList();
        blocks[11] = Block(
            11,
            new StoreLocal(0, I32, new Constant(4, I32)),
            Cond(14));
        blocks[13] = Term(13, new Branch(14));
        blocks.Add(Term(14, new Return(new LoadLocal(0, I32))));

        var plan = StructuringJoinAnalysis.Analyze(blocks);
        Assert.Contains(
            plan.BackEdgeRegions,
            region => region.Start == 1 && region.Merge != region.End);

        var (function, diagnostics) = Structure([.. blocks]);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeWithExternalInteriorEntryStaysFlat()
    {
        var blocks = new[]
        {
            Term(0, new Branch(4)),
            Term(1, new Branch(2)),
            Term(2, new Branch(14)),
            Term(3, Cond(6)),
            Term(4, Cond(8)),
            Block(
                5,
                new StoreLocal(0, I32, new Constant(2, I32)),
                new Branch(7)),
            Term(6, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(7, new Branch(8)),
            Term(8, Cond(11)),
            Term(9, Cond(13)),
            Block(
                10,
                new StoreLocal(0, I32, new Constant(5, I32)),
                new Branch(12)),
            Term(11, new StoreLocal(0, I32, new Constant(6, I32))),
            Term(12, new Branch(13)),
            Block(
                13,
                new StoreLocal(0, I32, new Constant(7, I32)),
                Cond(15)),
            Term(14, Cond(3)),
            Term(15, new Return(new LoadLocal(0, I32))),
        };
        var plan = StructuringJoinAnalysis.Analyze(blocks);
        Assert.Contains(
            plan.BackEdgeRegions,
            region => region.Start == 3
                && region.End == 15
                && region.BackEdgeSources.SequenceEqual([14]));

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeNestedBelowItsGotoStaysFlat()
    {
        var (function, diagnostics) = Structure(RetainedBodyMergeNestedBelowGotoBlocks());

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-dangling-merge-label", diagnostics.RetainedDeclines);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeNestedExpressionCannotVouchForHiddenLabel()
    {
        var blocks = RetainedBodyMergeNestedBelowGotoBlocks();
        var misleadingOwner = new Constant(0, I32);
        misleadingOwner.SetSourceOffset(8);
        blocks[1] = Block(
            1,
            new StoreLocal(0, I32, misleadingOwner),
            Cond(8));

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-dangling-merge-label", diagnostics.RetainedDeclines);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeLabelStaysOutsideSynthesizedUnsafeScope()
    {
        var blocks = RetainedLoopBlocks();
        var pointer = TypeRef.Pointer(I32);
        blocks[11] = Block(
            11,
            new StoreIndirect(
                I32,
                new LoadArgument(0, "p", pointer),
                new Constant(7, I32)),
            Cond(13));

        var (function, diagnostics) = Structure(
            blocks,
            parameters: [new Parameter("p", pointer)],
            usesUpdatedMemorySafetyRules: true);

        Assert.Equal(1, diagnostics.RetainedRegions);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        int label = output.IndexOf("IL_000B:", StringComparison.Ordinal);
        int unsafeBlock = output.IndexOf("unsafe\n", StringComparison.Ordinal);
        Assert.True(label >= 0, output);
        Assert.True(unsafeBlock > label, output);
        Assert.DoesNotContain("unsafe\n{\n    IL_000B:", output);
    }

    [Fact]
    public void RetainedBodyMergeLabelSurvivesDownstreamInlining()
    {
        var blocks = RetainedLoopBlocks();
        blocks[11] = Block(
            11,
            new StoreStackSlot(0, new Constant(7, I32)),
            new StoreLocal(0, I32, new LoadStackSlot(0, I32)),
            Cond(13));
        var (function, diagnostics) = Structure(blocks);

        new ExpressionInliningPass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("goto IL_000B;", output);
        Assert.Contains("IL_000B:", output);
    }

    [Fact]
    public void RetainedBodyMergeWithEmptyLandingPadStaysFlat()
    {
        var blocks = RetainedLoopBlocks();
        blocks[11] = new Block(11);

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-dangling-merge-label", diagnostics.RetainedDeclines);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeWithSwitchStaysFlat()
    {
        var blocks = RetainedLoopBlocks();
        blocks[8] = Term(
            8,
            new SwitchBranch(new Constant(0, I32), [10]));

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeWithStructuredSwitchStaysFlat()
    {
        var blocks = RetainedLoopBlocks();
        var switchBody = new BlockContainer();
        switchBody.Add(Term(200, new Return(new Constant(99, I32))));
        blocks[11] = Block(
            11,
            new Switch(
                new Constant(0, I32),
                [new SwitchSection([new Constant(0, I32)], false, switchBody)]),
            new StoreLocal(0, I32, new Constant(7, I32)),
            Cond(13));

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<Switch>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedBodyMergeWithStructuredMethodExitStaysFlat(bool useThrow)
    {
        var blocks = RetainedLoopBlocks();
        var exitArm = new Block(200);
        exitArm.Add(
            useThrow
                ? new Throw(new Constant(null, TypeRef.CoreLib("System", "Object")))
                : new Return(new Constant(99, I32)));
        blocks[11] = Block(
            11,
            new IfStatement(
                new Comparison(
                    ComparisonKind.Equal,
                    isUnsigned: false,
                    new Constant(0, I32),
                    new Constant(1, I32)),
                exitArm,
                null),
            new StoreLocal(0, I32, new Constant(7, I32)),
            Cond(13));

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
    }

    [Fact]
    public void RetainedBodyMergeWithCrossArmPredecessorPreservesJoin()
    {
        var blocks = CrossArmRetainedLoopBlocks();

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.Single(function.Descendants.OfType<WhileLoop>());
        var joinStore = Assert.Single(
            function.Descendants.OfType<StoreLocal>(),
            store => store.Value is Constant { Value: 4 });
        var joinBlock = Assert.IsType<Block>(joinStore.Parent);
        Assert.Equal(1, joinStore.ChildIndex);
        Assert.IsType<IfStatement>(joinBlock.Children[0]);
    }

    [Theory]
    [InlineData("ToUpperOrdinal")]
    [InlineData("ToLowerOrdinal")]
    public void CoreLibOrdinalCasingCrossArmPredecessorPreservesFallbackPath(string methodName)
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(
            source,
            "System.Globalization.OrdinalCasing",
            methodName);
        Assert.NotNull(function);
        var diagnostics = new StructuringDiagnostics();

        IrPasses.Run(
            function!,
            IrPasses.Default,
            new PassContext(new Stepper(enabled: false), diagnostics));
        function!.CheckInvariant();

        Assert.Equal(1, diagnostics.RetainedRegions);
        string fallbackName = methodName == "ToUpperOrdinal" ? "ToUpper" : "ToLower";
        var fallbackStore = Assert.Single(
            function.Descendants.OfType<StoreIndirect>(),
            store => store.Value is Call { Callee.Name: var name } && name == fallbackName);
        var fallbackBlock = Assert.IsType<Block>(fallbackStore.Parent);
        Assert.Equal(1, fallbackStore.ChildIndex);
        Assert.IsType<IfStatement>(fallbackBlock.Children[0]);
    }

    [Fact]
    public void CoreLibUrlDecodeWithRetainedBodyMergeRaises()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(
            source,
            "System.Net.WebUtility",
            "UrlDecodeInternal",
            overloadIndex: 0);
        Assert.NotNull(function);
        var diagnostics = new StructuringDiagnostics();

        IrPasses.Run(
            function!,
            IrPasses.Default,
            new PassContext(new Stepper(enabled: false), diagnostics));
        function!.CheckInvariant();

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.Contains(
            function.Descendants.OfType<Branch>(),
            branch => branch.TargetOffset == 0xB5);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("for (", output);
        Assert.Contains("goto IL_00B5;", output);
    }

    [Fact]
    public void UnrelatedTailBackEdgeDoesNotRejectForwardRetainedRegion()
    {
        var blocks = SequentialRetainedMerges().Take(6).ToList();
        blocks[5] = Term(40, new StoreLocal(0, I32, new Constant(5, I32)));
        blocks.Add(Term(48, Cond(64)));
        blocks.Add(Term(56, new Branch(48)));
        blocks.Add(Term(64, new Return(new LoadLocal(0, I32))));

        var (function, diagnostics) = Structure([.. blocks]);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.Contains("retained-back-edge-region", diagnostics.RetainedDeclines);
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 48);
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 40);
    }

    [Fact]
    public void TailEntryIntoRetainedInteriorDeclines()
    {
        var blocks = SequentialRetainedMerges().Take(6).ToList();
        blocks[5] = Term(40, Cond(16));
        blocks.Add(Term(48, new Return(new LoadLocal(0, I32))));

        var (function, diagnostics) = Structure([.. blocks]);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-external-entry", diagnostics.RetainedDeclines);
        Assert.Equal(blocks.Count, function.Body.Blocks.Count);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void RetainedGotoParticipatesInDefiniteAssignment()
    {
        var blocks = new[]
        {
            Term(0, Cond(24)),
            Block(
                8,
                new StoreLocal(0, I32, new Constant(1, I32)),
                Cond(40)),
            Block(16, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(32)),
            Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(32, new Branch(40)),
            Term(40, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.False(facts.Bailed);
        Assert.Empty(facts.ReadBeforeAssign);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int V_0;", output);
        Assert.DoesNotContain("int V_0 = default;", output);
    }

    [Fact]
    public void RetainedGotoWithFlatTailBailsDefiniteAssignmentConservatively()
    {
        var blocks = new[]
        {
            Term(0, Cond(24)),
            Term(8, Cond(40)),
            Block(16, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(32)),
            Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(32, new Branch(40)),
            Term(40, Cond(64)),
            Term(48, new Branch(40)),
            Term(64, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.True(facts.Bailed);
        Assert.Contains("int V_0 = default;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void RetainedLoopGotoBailsDefiniteAssignmentConservatively()
    {
        var (function, diagnostics) = Structure(RetainedLoopBlocks());
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.True(facts.Bailed);
        Assert.Contains("int V_0 = default;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void RetainedRegionStartKeepsOutsideTargetLabel()
    {
        var blocks = new[]
        {
            Term(0, new Branch(8)),
            Term(8, Cond(32)),
            Term(16, Cond(48)),
            Block(24, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(40)),
            Term(32, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(40, new Branch(48)),
            Term(48, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(1, diagnostics.RetainedRegions);
        Assert.Equal(8, function.Body.Blocks[1].Children[0].SourceOffset);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("goto IL_0008;", output);
        Assert.Contains("IL_0008:", output);
    }

    [Fact]
    public void OneJumpPlusFallthroughIsNotAProvenSharedMerge()
    {
        var blocks = new[]
        {
            Term(0, Cond(24)),
            Term(8, Cond(40)),
            Term(16, new Branch(32)),
            Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
            Term(32, new StoreLocal(0, I32, new Constant(4, I32))),
            Term(40, new Return(new LoadLocal(0, I32))),
        };

        var (function, diagnostics) = Structure(blocks);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-merge-not-shared", diagnostics.RetainedDeclines);
        Assert.Equal(blocks.Length, function.Body.Blocks.Count);
    }

    [Fact]
    public void DuplicateOffsetsDeclineWithoutCrashingDefiniteAssignment()
    {
        var blocks = SequentialRetainedMerges();
        blocks[1] = Block(
            0,
            new StoreLocal(0, I32, new Constant(1, I32)),
            new Branch(40));

        var (function, diagnostics) = Structure(blocks);
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal(0, diagnostics.RetainedRegions);
        Assert.Contains("retained-duplicate-offset", diagnostics.RetainedDeclines);
        Assert.NotNull(facts);
    }

    static Block[] SequentialRetainedMerges() =>
    [
        Term(0, Cond(24)),
        Term(8, Cond(40)),
        Block(16, new StoreLocal(0, I32, new Constant(2, I32)), new Branch(32)),
        Term(24, new StoreLocal(0, I32, new Constant(3, I32))),
        Term(32, new Branch(40)),
        Term(40, Cond(64)),
        Term(48, Cond(80)),
        Block(56, new StoreLocal(0, I32, new Constant(7, I32)), new Branch(72)),
        Term(64, new StoreLocal(0, I32, new Constant(8, I32))),
        Term(72, new Branch(80)),
        Term(80, new Return(new LoadLocal(0, I32))),
    ];

    static Block[] RetainedLoopBlocks() =>
    [
        Term(0, new Branch(12)),
        Term(1, Cond(4)),
        Term(2, Cond(6)),
        Block(
            3,
            new StoreLocal(0, I32, new Constant(2, I32)),
            new Branch(5)),
        Term(4, new StoreLocal(0, I32, new Constant(3, I32))),
        Term(5, new Branch(6)),
        Term(6, Cond(9)),
        Term(7, Cond(11)),
        Block(
            8,
            new StoreLocal(0, I32, new Constant(5, I32)),
            new Branch(10)),
        Term(9, new StoreLocal(0, I32, new Constant(6, I32))),
        Term(10, new Branch(11)),
        Block(
            11,
            new StoreLocal(0, I32, new Constant(7, I32)),
            Cond(13)),
        Term(12, Cond(1)),
        Term(13, new Return(new LoadLocal(0, I32))),
    ];

    static Block[] CrossArmRetainedLoopBlocks() =>
    [
        Term(0, new Branch(9)),
        Term(1, Cond(3)),
        Block(2, new StoreLocal(0, I32, new Constant(1, I32)), new Branch(8)),
        Term(3, Cond(7)),
        Term(4, Cond(7)),
        Block(5, new StoreLocal(0, I32, new Constant(2, I32)), Cond(7)),
        Block(6, new StoreLocal(0, I32, new Constant(3, I32)), new Branch(8)),
        Term(7, new StoreLocal(0, I32, new Constant(4, I32))),
        Term(8, new StoreLocal(0, I32, new Constant(5, I32))),
        Term(9, Cond(1)),
        Term(10, new Return(new LoadLocal(0, I32))),
    ];

    static Block[] RetainedBodyMergeNestedBelowGotoBlocks() =>
    [
        Term(0, new Branch(12)),
        Term(1, Cond(8)),
        Term(2, Cond(10)),
        Term(3, Cond(6)),
        Term(4, Cond(8)),
        Block(
            5,
            new StoreLocal(0, I32, new Constant(5, I32)),
            new Branch(7)),
        Term(6, new StoreLocal(0, I32, new Constant(6, I32))),
        Term(7, new Branch(8)),
        Term(8, new StoreLocal(0, I32, new Constant(8, I32))),
        Term(9, new Branch(10)),
        Term(10, new StoreLocal(0, I32, new Constant(10, I32))),
        Block(
            11,
            new StoreLocal(0, I32, new Constant(11, I32)),
            Cond(13)),
        Term(12, Cond(1)),
        Term(13, new Return(new LoadLocal(0, I32))),
    ];

    static (IrFunction Function, StructuringDiagnostics Diagnostics) Structure(
        Block[] blocks,
        Parameter[]? parameters = null,
        bool usesUpdatedMemorySafetyRules = false,
        Stepper? stepper = null)
    {
        var function = CreateFunction(
            blocks,
            parameters,
            usesUpdatedMemorySafetyRules,
            out _);
        var diagnostics = new StructuringDiagnostics();

        new StructuringPass().Run(
            function,
            new PassContext(stepper ?? new Stepper(enabled: false), diagnostics));
        function.CheckInvariant();
        return (function, diagnostics);
    }

    static IrFunction CreateFunction(
        Block[] blocks,
        Parameter[]? parameters,
        bool usesUpdatedMemorySafetyRules,
        out BlockContainer container)
    {
        container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(I32, parameters is null ? [] : [.. parameters], HasThis: false, GenericParameterCount: 0),
            [I32],
            container)
        {
            UsesUpdatedMemorySafetyRules = usesUpdatedMemorySafetyRules,
        };
        return function;
    }

    static Block Term(int offset, IrNode terminator) => Block(offset, terminator);

    static Block Block(int offset, params IrNode[] statements)
    {
        var block = new Block(offset);
        foreach (var statement in statements)
            block.Add(statement);
        return block;
    }

    static ConditionalBranch Cond(
        int targetOffset,
        ConditionalBranchOrigin origin = ConditionalBranchOrigin.Imported)
        => new(
            new Comparison(
                ComparisonKind.Equal,
                isUnsigned: false,
                new Constant(1, I32),
                new Constant(0, I32)),
            targetOffset,
            origin);
}
