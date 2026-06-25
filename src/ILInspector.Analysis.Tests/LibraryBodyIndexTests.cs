using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public class LibraryBodyIndexTests
{
    [Fact]
    public void FindCalls_FindsConsoleWriteLine()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method("System.Console", "WriteLine"));

        var call = Assert.Single(calls.Where(c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));
        Assert.Equal(CallKind.Call, call.Kind);
        Assert.Equal(TypeRef.CoreLib("System", "String"), Assert.Single(call.Callee.ParameterTypes));
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public void DirectCalls_RecordVirtualCallEvidenceWithoutInferringTargets()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsVirtualToString)
            && c.Callee.DeclaringType.Equals(TypeRef.CoreLib("System", "Object"))
            && c.Callee.Name == "ToString"));

        Assert.Equal(CallKind.CallVirtual, call.Kind);
    }

    [Fact]
    public void DirectCalls_MarksCallsInsideLoopRegions()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLineInLoop)
            && c.Callee.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));

        Assert.True(call.InLoop);
    }

    [Fact]
    public void DirectCalls_MarksLoopCall_WhenForwardBranchPrecedesLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.GuardThenCallsInLoop)
            && c.Callee.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));

        Assert.True(call.InLoop);
    }

    [Fact]
    public void BuildCallerTree_RendersReverseEdgesForSelectedRoot()
    {
        var index = LibraryBodyIndex.Open(typeof(CallerTreeFixtures).Assembly.Location);
        var root = Assert.Single(index.Methods.Where(method => method.Name == nameof(CallerTreeFixtures.Inner)));

        var tree = index.BuildCallerTree(root.MetadataToken, maxDepth: 2, maxNodes: 10);

        Assert.Equal(nameof(CallerTreeFixtures.Inner), tree.Member.Name);
        Assert.Contains(tree.Children, child => child.Member.Name == nameof(CallerTreeFixtures.Mid));
        Assert.Contains(tree.Children.SelectMany(child => child.Children), child => child.Member.Name == nameof(CallerTreeFixtures.RootCall));
        Assert.Equal("target", tree.Perf?.RootKind);
    }

    [Fact]
    public void BuildCallerTree_MarksCallerNodeInLoop_WhenCallerInvokesTargetInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);
        var root = Assert.Single(index.Methods.Where(method => method.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));

        var tree = index.BuildCallerTree(root.MetadataToken, maxDepth: 2, maxNodes: 25);

        var loopingCaller = Assert.Single(tree.Children.Where(child =>
            child.Member.Name == nameof(CallSiteFixtures.CallsConsoleWriteLineInLoop)));
        Assert.True(loopingCaller.Perf?.InLoop);
    }

    [Fact]
    public void BuildCallerTree_ResolvesCallers_WhenSelectedRootIsBodilessInterfaceMethod()
    {
        var index = LibraryBodyIndex.Open(typeof(BodilessRootFixtures).Assembly.Location);
        // Interface methods have no body and so are absent from index.Methods; the caller
        // references the method by its interface-method token.
        int targetToken = typeof(ICallerGraphTarget)
            .GetMethod(nameof(ICallerGraphTarget.Target))!.MetadataToken;

        var tree = index.BuildCallerTree(targetToken, maxDepth: 2, maxNodes: 25);

        Assert.Equal(nameof(ICallerGraphTarget.Target), tree.Member.Name);
        Assert.Equal("target", tree.Perf?.RootKind);
        Assert.Contains(tree.Children, child =>
            child.Member.Name == nameof(BodilessRootFixtures.InvokesThroughInterface));
    }

    [Fact]
    public void BuildCallerTree_WithScope_IncorporatesAndTagsExternalCallers()
    {
        var analysisIndex = LibraryBodyIndex.Open(typeof(LibraryBodyIndex).Assembly.Location);
        var testIndex = LibraryBodyIndex.Open(typeof(LibraryBodyIndexTests).Assembly.Location);
        var testAssemblyName = testIndex.Methods.First().AssemblyName;

        // LibraryBodyIndex.Open is a static method in the analysis assembly that this test
        // assembly calls; scoping the test assembly must pull those external callers into the
        // reverse graph and tag them with their source assembly.
        var open = analysisIndex.Methods.First(method =>
            method.DeclaringType.Name == nameof(LibraryBodyIndex) && method.Name == nameof(LibraryBodyIndex.Open));

        var scoped = analysisIndex.BuildCallerTree(open.MetadataToken, new[] { testIndex }, maxDepth: 2, maxNodes: 200);
        var unscoped = analysisIndex.BuildCallerTree(open.MetadataToken, maxDepth: 2, maxNodes: 200);

        Assert.Equal("target", scoped.Perf?.RootKind);
        // The target itself is not external.
        Assert.Null(scoped.Perf?.Source);
        Assert.True(HasSource(scoped, testAssemblyName), "expected an external caller tagged with the test assembly");
        // The single-assembly graph never incorporates the other assembly's callers.
        Assert.False(HasSource(unscoped, testAssemblyName), "single-assembly graph must not contain external callers");

        static bool HasSource(CallTreeNode node, string source)
            => node.Perf?.Source == source || node.Children.Any(child => HasSource(child, source));
    }

    [Fact]
    public void TopLeverage_RanksMostCalledMethodFirst()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 5, scope: InLeverageFixtures);

        var top = ranked[0];
        Assert.Equal(nameof(LeverageFixtures.Hot), top.Method.Name);
        // Called directly by A, B, C, and Fanned (the in-loop call site).
        Assert.Equal(4, top.DirectCallerCount);
    }

    [Fact]
    public void TopLeverage_CountsFanoutAndLoopCalls()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 25, scope: InLeverageFixtures);

        var fanned = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(LeverageFixtures.Fanned)));
        // Calls A, B, C, and Hot — at least four outbound call sites, one in a loop.
        Assert.True(fanned.Fanout >= 4, $"expected fanout >= 4, got {fanned.Fanout}");
        Assert.True(fanned.LoopCallCount >= 1, $"expected loop calls >= 1, got {fanned.LoopCallCount}");
        Assert.True(fanned.MaxDepth >= 2, $"expected depth >= 2, got {fanned.MaxDepth}");
    }

    [Fact]
    public void TopLeverage_ScopeRestrictsRankedMethods()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: InLeverageFixtures);

        Assert.NotEmpty(ranked);
        Assert.All(ranked, entry => Assert.Equal(nameof(LeverageFixtures), entry.Method.DeclaringType.Name));
    }

    static bool InLeverageFixtures(MethodIdentity method)
        => method.DeclaringType.Name == nameof(LeverageFixtures);

    [Fact]
    public void TopLeverage_ReportsTrueChainDepth_StableAcrossMethodOrder()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageDepthFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageDepthFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name, entry => entry.MaxDepth);

        // ChainTop -> ChainMid -> ChainLeaf is a three-method chain.
        Assert.Equal(3, byName[nameof(LeverageDepthFixtures.ChainTop)]);
        Assert.Equal(2, byName[nameof(LeverageDepthFixtures.ChainMid)]);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.ChainLeaf)]);

        // Pong -> ChainTop -> ChainMid -> ChainLeaf is the longest acyclic path from
        // Pong (the Pong <-> Ping back-edge is cut); Ping prepends one more hop.
        Assert.Equal(4, byName[nameof(LeverageDepthFixtures.Pong)]);
        Assert.Equal(5, byName[nameof(LeverageDepthFixtures.Ping)]);
    }

    [Fact]
    public void TopLeverage_CountsDistinctRootReach()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageRootReachFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageRootReachFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name);

        // Root1 -> Funnel -> Single and Root2 -> Funnel -> Single. Root1/Root2 have no
        // in-assembly caller, so each is a root reaching only itself.
        Assert.Equal(1, byName[nameof(LeverageRootReachFixtures.Root1)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageRootReachFixtures.Root2)].RootReach);

        // Funnel is reached from both roots: RootReach 2 matches its direct fanin of 2.
        Assert.Equal(2, byName[nameof(LeverageRootReachFixtures.Funnel)].DirectCallerCount);
        Assert.Equal(2, byName[nameof(LeverageRootReachFixtures.Funnel)].RootReach);

        // Single has a single direct caller (Funnel) yet is reached from two roots, so
        // RootReach (2) exceeds direct fanin (1): the inbound-scale signal Root Reach adds.
        Assert.Equal(1, byName[nameof(LeverageRootReachFixtures.Single)].DirectCallerCount);
        Assert.Equal(2, byName[nameof(LeverageRootReachFixtures.Single)].RootReach);
    }

    [Fact]
    public void TopLeverage_TreatsSelfRecursiveEntryAsRoot()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageSelfRootFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageSelfRootFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name);

        // SelfRoot recurses, so it carries a self-edge in the reverse graph, but it has no
        // other in-assembly caller: it must still count as a root (ignoring the self-edge).
        Assert.Equal(1, byName[nameof(LeverageSelfRootFixtures.SelfRoot)].RootReach);
        // Helper is reached only from SelfRoot; it would score zero if SelfRoot were
        // misclassified as a non-root.
        Assert.Equal(1, byName[nameof(LeverageSelfRootFixtures.Helper)].RootReach);
    }

    [Fact]
    public void TopLeverage_CountsCallerOfIntraAssemblyGenericMethod()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 200,
            scope: method => method.DeclaringType.Name == nameof(CallSiteFixtures));

        // GenericEcho is invoked once, via a MethodSpec operand, by CallsGenericEcho.
        var echo = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(CallSiteFixtures.GenericEcho)));
        Assert.Equal(1, echo.DirectCallerCount);
    }

    [Fact]
    public void MemberReferences_InstantiateGenericDeclaringTypeArguments()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var call = Assert.Single(index.DirectCalls.Where(c =>
            c.Caller.Name == nameof(CallSiteFixtures.CallsListAdd)
            && c.Callee.Name == "Add"));

        Assert.Equal("System.Collections.Generic.List<int>", call.Callee.DeclaringType.ToQualifiedDisplayString());
        Assert.Equal(TypeRef.CoreLib("System", "Int32"), Assert.Single(call.Callee.ParameterTypes));
    }

    [Fact]
    public void TypeIdentity_CanonicalizesCoreLibraryFacadeAssemblies()
    {
        Assert.Equal(
            TypeRef.CoreLib("System", "String"),
            TypeRef.Definition("System.Runtime", "System", "String"));
    }

    [Fact]
    public void DisplayStrings_RenderDecimalKeyword()
    {
        Assert.Equal("decimal", TypeRef.CoreLib("System", "Decimal").ToDisplayString());
    }

    [Fact]
    public void FindCalls_CanMatchFullParameterShape()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);

        var calls = index.FindCalls(MemberPattern.Method(
            TypeRef.Definition("System.Console", "System", "Console"),
            "WriteLine",
            ImmutableArray.Create(TypeRef.CoreLib("System", "String"))));

        Assert.Contains(calls, c => c.Caller.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine));
    }

    [Fact]
    public void Open_DoesNotKeepAssemblyFileLocked()
    {
        string path = Path.Combine(Path.GetTempPath(), $"analysis-lock-{Guid.NewGuid():N}.dll");
        File.Copy(typeof(CallSiteFixtures).Assembly.Location, path);
        try
        {
            var index = LibraryBodyIndex.Open(path);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.NotEmpty(index.Methods);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnsafeEvidence_FindsSignatureOperationsAndUnsafeCalls()
    {
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence.Reason == "Unsafe signature"
            && evidence.Detail.Contains("int*", StringComparison.Ordinal));
        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && evidence is { Reason: "Unsafe operation", Detail: "ldind.i4", Kind: "opcode", ILOffset: not null });
        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)
            && evidence.Reason == "Unsafe call"
            && evidence.Detail.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", StringComparison.Ordinal)
            && evidence.OperandToken is not null);
        Assert.DoesNotContain(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.PInvokeOnly));
    }

    [Fact]
    public void UnsafeEvidence_ClassifiesMembersDeclaredOnUnsafeApi()
    {
        var index = LibraryBodyIndex.Open(typeof(Unsafe).Assembly.Location);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.DeclaringType is { Namespace: "System.Runtime.CompilerServices", Name: "Unsafe" }
            && evidence.Member.Name == "Add"
            && evidence is { Reason: "Unsafe API member", Kind: "api" });
    }

    [Fact]
    public void CallerUnsafeMode_PointerSignatureIsImplicitWhenModuleNotOptedIn()
    {
        // This test assembly carries no MemorySafetyRulesAttribute, so a pointer
        // signature lands in the legacy Implicit bucket (Roslyn's CallerUnsafeMode).
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        Assert.False(index.MemorySafetyRulesEnabled);
        Assert.Equal(0, index.UnsafeModes.Explicit);

        var pointerRead = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)));
        Assert.Equal(CallerUnsafeMode.Implicit, pointerRead.CallerUnsafeMode);
    }

    [Fact]
    public void CallerUnsafeMode_CallingUnsafeApiIsNotRequiresUnsafe()
    {
        // The authoritative model is RequiresUnsafeAttribute || pointer signature.
        // Calling an unsafe API does not itself make a method requires-unsafe —
        // that is the heuristic's domain, deliberately excluded from the model.
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var callsUnsafeAs = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)));
        Assert.Equal(CallerUnsafeMode.None, callsUnsafeAs.CallerUnsafeMode);
    }

    [Fact]
    public void GeneratedFrameworkTypeNames_DetectsProtobufAndGrpcGeneratedTypes()
    {
        var index = LibraryBodyIndex.Open(typeof(FakeProtobufReflection).Assembly.Location);
        var generated = index.GeneratedFrameworkTypeNames;

        // protobuf reflection holder: its initializer calls FileDescriptor.FromGeneratedCode.
        Assert.Contains(typeof(FakeProtobufReflection).FullName!, generated);
        // protobuf message: its initializer constructs MessageParser<T>.
        Assert.Contains(typeof(FakeProtobufMessage).FullName!, generated);
        // gRPC service stub: binds via ServerServiceDefinition and declares __Helper_ members.
        Assert.Contains(typeof(FakeGrpcServiceStub).FullName!, generated);
        // A normal protobuf-using type that doesn't bootstrap generated infrastructure stays out.
        Assert.DoesNotContain(typeof(NormalProtobufConsumer).FullName!, generated);
        // Hand-written gRPC registration calling ServerServiceDefinition.CreateBuilder (without
        // generated __* members) must not be flagged — gRPC calls alone are not a signal.
        Assert.DoesNotContain(typeof(HandWrittenGrpcRegistration).FullName!, generated);
    }

    [Fact]
    public void OptimizationOpportunities_SuppressesSourceGeneratedTypes()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A [GeneratedCode] type (source generators: JSON/regex/etc.) is not an actionable
        // source-shape target, so none of its methods produce optimization opportunities,
        // even though MakesSmallArray would otherwise be a small-array row (#1273).
        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == nameof(SourceGeneratedOptimizationFixtures));
    }

    [Fact]
    public void OptimizationOpportunities_TracksFieldAccessAndClearsStaleConstants()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var fieldAccessMethod = Assert.Single(index.OptimizationOpportunities.Where(opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.MakesArrayAfterFieldAccess)));
        Assert.Equal("small-array", fieldAccessMethod.Shape);

        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.MakesArrayAfterCallAndArgument)
            && opportunity.Shape == "small-array");
    }

    [Fact]
    public void OptimizationOpportunities_PromotesProvablyLocalArrayToStackalloc()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var local = Assert.Single(ArrayShapes(index, nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal)));
        Assert.Equal("stackalloc-candidate", local);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.ReturnsSmallArray))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StoresArrayToField))]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalArrayPassedToCall))]
    [InlineData(nameof(OptimizationOpportunityFixtures.LocalStringArrayStaysLocal))]
    public void OptimizationOpportunities_KeepsEscapingOrIneligibleArrayAsSmallArray(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var shape = Assert.Single(ArrayShapes(index, methodName));
        Assert.Equal("small-array", shape);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.SpanToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.MutableSpanToArrayCopy))]
    public void OptimizationOpportunities_FlagsSpanToArrayCopy(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == methodName && o.Shape == "span-to-array-copy"));
        // The IL offset must point at the real ToArray call (oracle-verifiable), not be inferred.
        Assert.NotNull(opportunity.ILOffset);
        Assert.Equal("medium", opportunity.Confidence);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagListToArrayAsCopy()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // List<T>.ToArray() is intentionally not promoted (too common to flag without
        // escape/usage analysis), so no span-to-array-copy row is emitted for it.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ListToArrayNotFlagged)
            && o.Shape == "span-to-array-copy");
    }

    [Fact]
    public void OptimizationOpportunities_CapturingLambdaIsCapturingDelegate_SingleRow()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var shape = Assert.Single(DelegateShapes(index, nameof(OptimizationOpportunityFixtures.CapturingLambda)));
        Assert.Equal("capturing-delegate", shape);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.NonCapturingLambda))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StaticMethodGroup))]
    public void OptimizationOpportunities_NonCapturingDelegate_NotReported(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Non-capturing lambdas and static method groups are cached by the compiler, so they
        // are not a high-value allocation signal and no delegate row is emitted for them.
        Assert.Empty(DelegateShapes(index, methodName));
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.InstanceMethodGroup))]
    [InlineData(nameof(OptimizationOpportunityFixtures.VirtualInstanceMethodGroup))]
    public void OptimizationOpportunities_InstanceMethodGroup_IsInstanceMethodGroupDelegate(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // An instance method group binds a runtime receiver and is never compiler-cached, so
        // it allocates a delegate per call -> a single instance-method-group-delegate row.
        var shape = Assert.Single(DelegateShapes(index, methodName));
        Assert.Equal("instance-method-group-delegate", shape);
    }

    [Fact]
    public void OptimizationOpportunities_CarryContainingMethodRootReach()
    {
        var index = LibraryBodyIndex.Open(typeof(OpportunityLeverageFixtures).Assembly.Location);

        // Root1/Root2 both reach Allocator, so its small-array opportunity should carry the
        // method's Root Reach of 2 (the leverage join), matching the Top Leverage ranking.
        var expected = Assert.Single(
            index.TopLeverage(int.MaxValue, m => m.DeclaringType.Name == nameof(OpportunityLeverageFixtures))
                .Where(e => e.Method.Name == nameof(OpportunityLeverageFixtures.Allocator)));
        Assert.Equal(2, expected.RootReach);

        var opportunity = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OpportunityLeverageFixtures.Allocator) && o.Shape == "small-array"));
        Assert.Equal(2, opportunity.RootReach);
    }

    [Fact]
    public void OptimizationOpportunities_SuppressesCompilerGeneratedRecordMembers()
    {
        var index = LibraryBodyIndex.Open(typeof(OpportunityRecordFixture).Assembly.Location);

        // Record synthesized members (e.g. get_EqualityContract) are [CompilerGenerated],
        // so they are excluded from optimization-opportunity scanning entirely. None should
        // surface.
        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == nameof(OpportunityRecordFixture));
    }

    static IEnumerable<string> ArrayShapes(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape is "small-array" or "stackalloc-candidate")
            .Select(o => o.Shape);

    static IEnumerable<string> DelegateShapes(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape is "delegate-allocation" or "capturing-delegate" or "instance-method-group-delegate")
            .Select(o => o.Shape);


    [Fact]
    public void TopUnsafeLeverage_RanksRequiresUnsafeMethodsByCallers()
    {
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var top = index.TopUnsafeLeverage(count: 100);

        Assert.Contains(top, e =>
            e.Method.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && e.Mode == CallerUnsafeMode.Implicit);
        Assert.DoesNotContain(top, e => e.Method.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs));
    }
}

public class OptimizationOpportunityFixtures
{
    private readonly int _field = 3;
    private int[]? _arrayField;

    public int[] MakesArrayAfterFieldAccess()
    {
        var value = _field;
        return new int[3];
    }

    public static int[] MakesArrayAfterCallAndArgument(int length)
    {
        Console.WriteLine(42);
        return new int[length];
    }

    // --- Escape analysis (small-array vs stackalloc-candidate) ---

    // Array created, written, and read entirely locally -> provably non-escaping.
    public static int LocalArrayStaysLocal()
    {
        var a = new int[4];
        a[0] = 1;
        a[3] = 2;
        return a[0] + a[3];
    }

    // Non-escaping but a managed (reference) element type -> not stackalloc-eligible.
    public static int LocalStringArrayStaysLocal()
    {
        var a = new string[4];
        a[0] = "x";
        a[3] = "yz";
        return a[0].Length + a[3].Length;
    }

    // Returned -> escapes.
    public static int[] ReturnsSmallArray() => new int[4];

    // --- Copy allocation (span-to-array) ---

    // ReadOnlySpan<T>.ToArray() materializes a copy -> span-to-array-copy.
    public static int[] SpanToArrayCopy(System.ReadOnlySpan<int> span) => span.ToArray();

    // Span<T>.ToArray() also copies -> span-to-array-copy.
    public static int[] MutableSpanToArrayCopy(System.Span<int> span) => span.ToArray();

    // List<T>.ToArray() is a common, usually-legitimate snapshot -> deliberately NOT flagged
    // as span-to-array-copy (kept out to avoid flooding the section).
    public static int[] ListToArrayNotFlagged(System.Collections.Generic.List<int> list) => list.ToArray();

    // Stored to a field -> escapes.
    public void StoresArrayToField() => _arrayField = new int[4];

    // Stored to a local but then passed to a call -> escapes.
    public static void LocalArrayPassedToCall()
    {
        var a = new int[4];
        a[0] = 1;
        ConsumeArray(a);
    }

    private static void ConsumeArray(int[] data) => Console.WriteLine(data.Length);

    // --- Delegate allocation (capture detection + de-dup) ---

    // Captures a local -> capturing delegate (one row).
    public static Func<int> CapturingLambda(int seed)
    {
        return () => seed + 1;
    }

    // Non-capturing lambda -> compiler-cached delegate (one row, not capturing).
    public static Func<int> NonCapturingLambda()
    {
        return () => 42;
    }

    // Static method group -> non-capturing delegate (one row).
    public static Func<string, int> StaticMethodGroup()
    {
        return ParseLength;
    }

    private static int ParseLength(string value) => value.Length;

    // Instance method group -> per-call delegate binding the receiver (never cached).
    public Func<string, int> InstanceMethodGroup()
    {
        return InstanceParseLength;
    }

    private int InstanceParseLength(string value) => value.Length + _field;

    // Virtual instance method group -> ldvirtftn, also a per-call delegate.
    public Func<int> VirtualInstanceMethodGroup()
    {
        return VirtualHelper;
    }

    public virtual int VirtualHelper() => _field;
}

// A source-generated type (mirrors the [GeneratedCode] System.Text.Json source generator
// emits): its MakesSmallArray would be a small-array opportunity, but #1273 suppresses
// optimization opportunities for [GeneratedCode] types.
[System.CodeDom.Compiler.GeneratedCode("TestSourceGenerator", "1.0")]
public class SourceGeneratedOptimizationFixtures
{
    public static int[] MakesSmallArray() => new int[4];
}

public static class OpportunityLeverageFixtures
{
    // Root1/Root2 are roots (no in-assembly caller); both reach Allocator, whose small
    // array opportunity should carry the joined Root Reach of 2.
    public static void Root1() => Consume(Allocator());

    public static void Root2() => Consume(Allocator());

    public static int[] Allocator() => new int[3];

    private static void Consume(int[] data) => Console.WriteLine(data.Length);
}

// A positional record: every synthesized member (EqualityContract/PrintMembers/Equals/
// GetHashCode/ToString/Deconstruct) is [CompilerGenerated], so none should yield
// optimization-opportunity rows.
public record OpportunityRecordFixture(int Value, string Name);

public static class CallerTreeFixtures
{
    public static void RootCall() => Mid();

    public static void Mid() => Inner();

    public static void Inner() => Console.WriteLine("leaf");
}

public static class LeverageFixtures
{
    public static void A() => Hot();

    public static void B() => Hot();

    public static void C()
    {
        Hot();
        Cold();
    }

    public static void Hot() => Console.WriteLine("hot");

    public static void Cold() => Console.WriteLine("cold");

    public static void Fanned()
    {
        A();
        B();
        C();
        for (int i = 0; i < 3; i++)
            Hot();
    }
}

public static class LeverageDepthFixtures
{
    public static void ChainTop() => ChainMid();

    public static void ChainMid() => ChainLeaf();

    public static void ChainLeaf() => Console.WriteLine("leaf");

    // Mutually recursive pair; Pong also reaches the chain. Used to confirm the
    // longest-chain walk terminates on cycles and reports a path-length that does
    // not depend on which method's ranking computed a shared node first.
    public static void Ping() => Pong();

    public static void Pong()
    {
        Ping();
        ChainTop();
    }
}

public static class LeverageRootReachFixtures
{
    // Two distinct roots (no in-assembly caller) both funnel into Single through Funnel.
    // Demonstrates that Root Reach (distinct reaching roots) can exceed direct fanin:
    // Single has one direct caller yet sits under two roots.
    public static void Root1() => Funnel();

    public static void Root2() => Funnel();

    public static void Funnel() => Single();

    public static void Single() => Console.WriteLine("single");
}

public static class LeverageSelfRootFixtures
{
    // Self-recursive entry point with no other in-assembly caller: still a root.
    public static void SelfRoot()
    {
        if (Environment.TickCount > 0)
            SelfRoot();
        Helper();
    }

    public static void Helper() => Console.WriteLine("helper");
}

public class OpaqueUnsafeTests
{
    static MethodIdentity Method(string name, CallerUnsafeMode mode, TypeRef returnType, params TypeRef[] parameterTypes)
        => new(
            AssemblyName: "Fixture",
            ModuleVersionId: Guid.Empty,
            DeclaringType: TypeRef.Definition("Fixture", "Ns", "Holder"),
            Name: name,
            ParameterTypes: [.. parameterTypes],
            ReturnType: returnType,
            MetadataToken: name.GetHashCode(),
            IsStatic: true,
            CallerUnsafeMode: mode);

    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void IsOpaque_RequiresUnsafeWithNoPointerSignature_IsOpaque()
    {
        // unsafe modifier / RequiresUnsafeAttribute on a pointerless signature:
        // the obligation is invisible to a caller reading parameter/return types.
        var contract = Method("ContractUnsafe", CallerUnsafeMode.Explicit, Int, TypeRef.SzArray(Int));
        Assert.True(OpaqueUnsafe.IsOpaque(contract));
    }

    [Fact]
    public void IsOpaque_PointerSignature_IsNotOpaque()
    {
        // The pointer is visible in the signature, so the unsafety is not hidden.
        var pointerParam = Method("TakesPointer", CallerUnsafeMode.Implicit, Int, TypeRef.Pointer(Int));
        var pointerReturn = Method("ReturnsPointer", CallerUnsafeMode.Explicit, TypeRef.Pointer(Int), Int);
        Assert.False(OpaqueUnsafe.IsOpaque(pointerParam));
        Assert.False(OpaqueUnsafe.IsOpaque(pointerReturn));
    }

    [Fact]
    public void IsOpaque_NotRequiresUnsafe_IsNotOpaque()
    {
        var safe = Method("Safe", CallerUnsafeMode.None, Int, Int);
        Assert.False(OpaqueUnsafe.IsOpaque(safe));
    }

    [Fact]
    public void Collect_SelectsOnlyOpaqueMethods_OrderedByToken()
    {
        var methods = ImmutableArray.Create(
            Method("Safe", CallerUnsafeMode.None, Int, Int),
            Method("TakesPointer", CallerUnsafeMode.Implicit, Int, TypeRef.Pointer(Int)),
            Method("Contract", CallerUnsafeMode.Explicit, Int, TypeRef.SzArray(Int)),
            Method("Hollow", CallerUnsafeMode.Explicit, Int));

        var opaque = OpaqueUnsafe.Collect(methods);

        Assert.Equal(["Contract", "Hollow"], opaque.Select(o => o.Method.Name).Order());
        Assert.All(opaque, o => Assert.NotEqual(CallerUnsafeMode.None, o.Mode));
    }

    [Fact]
    public void OpaqueUnsafeMethods_PointerSignatureFixtureIsNotOpaque()
    {
        // The in-test fixture assembly is not opted into the updated memory-safety
        // rules, so its only requires-unsafe methods carry a pointer signature —
        // none should be reported as opaque.
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var opaque = index.OpaqueUnsafeMethods();

        Assert.DoesNotContain(opaque, o => o.Method.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead));
        Assert.All(opaque, o => Assert.False(
            o.Method.ParameterTypes.Any(t => t.ContainsPointer()) || o.Method.ReturnType.ContainsPointer()));
    }
}

public class HollowUnsafeTests
{
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");

    static MethodIdentity Method(string name, CallerUnsafeMode mode, TypeRef? returnType = null, params TypeRef[] parameterTypes)
        => new(
            AssemblyName: "Fixture",
            ModuleVersionId: Guid.Empty,
            DeclaringType: TypeRef.Definition("Fixture", "Ns", "Holder"),
            Name: name,
            ParameterTypes: [.. parameterTypes],
            ReturnType: returnType ?? Int,
            MetadataToken: name.GetHashCode(),
            IsStatic: true,
            CallerUnsafeMode: mode);

    static UnsafeEvidence Structural(MethodIdentity method, string kind)
        => new(method, "structural", kind, kind, ILOffset: null, OperandToken: null);

    static UnsafeEvidence Realized(MethodIdentity method, string kind)
        => new(method, "Unsafe operation", kind, kind, ILOffset: 0x10, OperandToken: null);

    [Fact]
    public void IsHollow_RequiresUnsafeWithNoEvidence_IsHollow()
    {
        var hollow = Method("HollowUnsafe", CallerUnsafeMode.Explicit);
        Assert.True(HollowUnsafe.IsHollow(hollow, []));
    }

    [Fact]
    public void IsHollow_OnlyStructuralEvidence_IsHollow()
    {
        // A pointer signature / pointer local is structural (no IL offset); the
        // body still shows no realized unsafe operation.
        var signatureOnly = Method("SignatureOnlyUnsafe", CallerUnsafeMode.Explicit, returnType: TypeRef.CoreLib("System", "Boolean"), TypeRef.Pointer(Int));
        ImmutableArray<UnsafeEvidence> evidence = [Structural(signatureOnly, "signature")];
        Assert.True(HollowUnsafe.IsHollow(signatureOnly, evidence));
    }

    [Fact]
    public void IsHollow_RealizedBodyOp_IsNotHollow()
    {
        var real = Method("RealUnsafePointer", CallerUnsafeMode.Explicit, returnType: Int, TypeRef.Pointer(Int));
        ImmutableArray<UnsafeEvidence> evidence = [Structural(real, "signature"), Realized(real, "opcode")];
        Assert.False(HollowUnsafe.IsHollow(real, evidence));
    }

    [Fact]
    public void IsHollow_NotRequiresUnsafe_IsNotHollow()
    {
        var safe = Method("Safe", CallerUnsafeMode.None);
        Assert.False(HollowUnsafe.IsHollow(safe, []));
    }

    [Fact]
    public void Collect_SelectsRequiresUnsafeMethodsWithoutRealizedOps_OrderedByToken()
    {
        var safe = Method("Safe", CallerUnsafeMode.None);
        var hollow = Method("Hollow", CallerUnsafeMode.Explicit);
        var signatureOnly = Method("SignatureOnly", CallerUnsafeMode.Explicit, returnType: Int, TypeRef.Pointer(Int));
        var real = Method("Real", CallerUnsafeMode.Explicit, returnType: Int, TypeRef.Pointer(Int));
        var delegated = Method("Delegated", CallerUnsafeMode.Implicit, returnType: Int, TypeRef.Pointer(Int));

        var methods = ImmutableArray.Create(safe, hollow, signatureOnly, real, delegated);
        ImmutableArray<UnsafeEvidence> evidence =
        [
            Structural(signatureOnly, "signature"),
            Structural(real, "signature"),
            Realized(real, "opcode"),
            Structural(delegated, "signature"),
            Realized(delegated, "call"),   // an unsafe call is a realized body op
        ];

        var result = HollowUnsafe.Collect(methods, evidence);

        // Hollow (no realized op) and SignatureOnly (only structural) qualify;
        // Real (deref) and Delegated (unsafe call) do not; Safe is not unsafe.
        Assert.Equal(["Hollow", "SignatureOnly"], result.Select(h => h.Method.Name).Order());
        Assert.All(result, h => Assert.NotEqual(CallerUnsafeMode.None, h.Mode));
    }

    [Fact]
    public void HollowUnsafeMethods_PointerDereferenceFixtureIsNotHollow()
    {
        // UnsafePointerRead dereferences its pointer parameter, so it carries a
        // realized (IL-offset-anchored) body op and must not be reported hollow.
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var hollow = index.HollowUnsafeMethods();

        Assert.DoesNotContain(hollow, h => h.Method.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead));
        Assert.All(hollow, h => Assert.False(
            HollowUnsafe.HasRealizedUnsafeOp(h.Method, index.UnsafeEvidence)));
    }
}

public class CallTreeTests
{
    static readonly LibraryBodyIndex Index = LibraryBodyIndex.Open(typeof(CallTreeFixtures).Assembly.Location);

    static int Token(string methodName)
        => Index.Methods
            .First(method => method.DeclaringType.Name == nameof(CallTreeFixtures) && method.Name == methodName)
            .MetadataToken;

    static CallTreeNode? Find(CallTreeNode node, string memberName)
    {
        if (node.Member.Name == memberName)
            return node;
        foreach (var child in node.Children)
            if (Find(child, memberName) is { } match)
                return match;
        return null;
    }

    [Fact]
    public void BuildCallTree_ExpandsInAssemblyChain()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 5, maxNodes: 100);

        Assert.Equal(nameof(CallTreeFixtures.Root), tree.Member.Name);
        var one = Assert.Single(tree.Children, child => child.Member.Name == nameof(CallTreeFixtures.LevelOne));
        var two = Assert.Single(one.Children);
        Assert.Equal(nameof(CallTreeFixtures.LevelTwo), two.Member.Name);
        var three = Assert.Single(two.Children);
        Assert.Equal(nameof(CallTreeFixtures.LevelThree), three.Member.Name);
    }

    [Fact]
    public void BuildCallTree_PopulatesAllocationAndCopySignals()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.AllocatesAndCopies)), maxDepth: 1, maxNodes: 100);

        // new List<int>(...) and the object[] literal -> two newobj allocations.
        Assert.True(tree.Perf?.SignalsOrNone.Allocations >= 2, $"expected >= 2 allocations, got {tree.Perf?.SignalsOrNone.Allocations}");
        // data.ToArray() -> one copy.
        Assert.True(tree.Perf?.SignalsOrNone.Copies >= 1, $"expected >= 1 copy, got {tree.Perf?.SignalsOrNone.Copies}");
    }

    [Fact]
    public void BuildCallTree_CountsArrayAllocationsInAllocSignal()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.AllocatesArray)), maxDepth: 1, maxNodes: 100);

        // newarr is folded into Allocations, and its IL offset is retained as evidence.
        Assert.True(tree.Perf?.SignalsOrNone.Allocations >= 1, $"expected >= 1 alloc, got {tree.Perf?.SignalsOrNone.Allocations}");
        Assert.NotEmpty(tree.Perf!.SignalsOrNone.Evidence);
    }

    [Fact]
    public void BuildCallTree_PopulatesThrowSignal()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Throws)), maxDepth: 1, maxNodes: 100);

        Assert.True(tree.Perf?.SignalsOrNone.Throws >= 1, $"expected >= 1 throw, got {tree.Perf?.SignalsOrNone.Throws}");
    }

    [Fact]
    public void BuildCallTree_PopulatesConstructedExceptionTypesSeparatelyFromThrowSites()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Throws)), maxDepth: 1, maxNodes: 100);
        var signals = tree.Perf?.SignalsOrNone;

        // Throw-site count and constructed exception type are distinct signals (#1277).
        Assert.True(signals?.Throws >= 1);
        Assert.Contains("InvalidOperationException", signals!.ExceptionTypes);
    }

    [Fact]
    public void BuildCallTree_PopulatesCatchAndFinallySignals()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.TryCatchFinally)), maxDepth: 1, maxNodes: 100);

        Assert.True(tree.Perf?.SignalsOrNone.Catches >= 1, $"expected >= 1 catch, got {tree.Perf?.SignalsOrNone.Catches}");
        Assert.True(tree.Perf?.SignalsOrNone.Finallys >= 1, $"expected >= 1 finally, got {tree.Perf?.SignalsOrNone.Finallys}");
    }

    [Fact]
    public void BuildCallTree_PopulatesReflectionSignal()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Reflects)), maxDepth: 1, maxNodes: 100);

        // Type.GetMethods + Activator.CreateInstance == 2; the two typeof lowerings
        // (Type.GetTypeFromHandle) must not be counted.
        Assert.Equal(2, tree.Perf?.SignalsOrNone.Reflection);
    }

    [Fact]
    public void BuildCallTree_StopsAtDepthLimit()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 2, maxNodes: 100);

        var two = Find(tree, nameof(CallTreeFixtures.LevelTwo));
        Assert.NotNull(two);
        Assert.Equal(CallTreeStatus.DepthLimited, two!.Status);
        // Depth-limited, but LevelTwo still calls LevelThree: true fan-out is reported.
        Assert.Equal(1, two.Perf?.Fanout);
        Assert.Empty(two.Children);
        Assert.Null(Find(tree, nameof(CallTreeFixtures.LevelThree)));
    }

    [Fact]
    public void BuildCallTree_RecordsCrossAssemblyCalleesAsExternalLeaves()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.LevelThree)), maxDepth: 5, maxNodes: 100);

        var external = Assert.Single(tree.Children);
        Assert.Equal("WriteLine", external.Member.Name);
        Assert.Equal(CallTreeStatus.External, external.Status);
        Assert.Empty(external.Children);
    }

    [Fact]
    public void BuildCallTree_MarksCyclesAsAlreadyShown()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Ping)), maxDepth: 5, maxNodes: 100);

        var pong = Assert.Single(tree.Children);
        Assert.Equal(nameof(CallTreeFixtures.Pong), pong.Member.Name);
        var pingAgain = Assert.Single(pong.Children);
        Assert.Equal(nameof(CallTreeFixtures.Ping), pingAgain.Member.Name);
        Assert.Equal(CallTreeStatus.AlreadyShown, pingAgain.Status);
        // Shown above, but Ping still calls Pong: true fan-out is reported.
        Assert.Equal(1, pingAgain.Perf?.Fanout);
        Assert.Empty(pingAgain.Children);
    }

    [Fact]
    public void BuildCallTree_TruncatesAtNodeCap()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Root)), maxDepth: 5, maxNodes: 2);

        Assert.Equal(CallTreeStatus.Truncated, tree.Status);
        Assert.Single(tree.Children);
        // Only one child fit the node budget, but Root's true fan-out (LevelOne + External) is 2.
        Assert.Equal(2, tree.Perf?.Fanout);
    }

    [Fact]
    public void BuildCallTree_LeafMethodHasNoChildren()
    {
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.Leaf)), maxDepth: 5, maxNodes: 100);

        Assert.Equal(CallTreeStatus.Leaf, tree.Status);
        Assert.Empty(tree.Children);
    }
}

public static class CallSiteFixtures
{
    public static void CallsConsoleWriteLine() => Console.WriteLine("hello");

    public static void CallsConsoleWriteLineInLoop(int iterations)
    {
        for (int i = 0; i < iterations; i++)
            CallsConsoleWriteLine();
    }

    // Exercises a forward branch (the early-return guard) appearing before the loop.
    // A loop-region scan that double-advances on the forward branch desyncs and misses
    // the loop's backward branch, so the in-loop call would be misreported as not-in-loop.
    public static void GuardThenCallsInLoop(int iterations, bool skip)
    {
        if (skip)
            return;
        for (int i = 0; i < iterations; i++)
            CallsConsoleWriteLine();
    }

    public static string? CallsVirtualToString(object value) => value.ToString();

    public static void CallsListAdd(List<int> values) => values.Add(42);

    // Intra-assembly generic method: the call site below references it by a
    // MethodSpec token (the int instantiation), not the method's MethodDef token.
    public static T GenericEcho<T>(T value) => value;

    public static int CallsGenericEcho() => GenericEcho(42);
}

public interface ICallerGraphTarget
{
    void Target();
}

public static class BodilessRootFixtures
{
    // The callvirt references ICallerGraphTarget.Target by its (bodiless) interface
    // method token, so a Caller Graph rooted at that token must still find this caller.
    public static void InvokesThroughInterface(ICallerGraphTarget target) => target.Target();
}

public static class CallTreeFixtures
{
    public static void Root()
    {
        LevelOne();
        External();
    }

    public static void LevelOne() => LevelTwo();

    public static void LevelTwo() => LevelThree();

    public static void LevelThree() => Console.WriteLine("leaf");

    public static void External() => Console.WriteLine("external");

    public static void Ping() => Pong();

    public static void Pong() => Ping();

    public static void Leaf() { }

    // Two newobj (List<int> ×2) -> allocations; ToArray -> copy.
    public static int AllocatesAndCopies(int[] data)
    {
        var list = new List<int>(data);
        var more = new List<int>();
        var copy = data.ToArray();
        return list.Count + more.Count + copy.Length;
    }

    // newarr -> folded into Allocations (alloc), with an evidence offset.
    public static int[] AllocatesArray() => new int[4];

    // throw site (and a newobj for the exception object).
    public static void Throws(int x)
    {
        if (x < 0)
            throw new InvalidOperationException("negative");
    }

    // try/catch + finally -> one catch clause, one finally clause.
    public static int TryCatchFinally(int x)
    {
        try { return 100 / x; }
        catch (DivideByZeroException) { return -1; }
        finally { GC.KeepAlive(x); }
    }

    // System.Type reflection (GetMethods) + System.Activator.CreateInstance -> reflection.
    // The typeof(...) lowering (Type.GetTypeFromHandle) must NOT count as reflection.
    public static object? Reflects()
    {
        _ = typeof(CallTreeFixtures).GetMethods();
        return System.Activator.CreateInstance(typeof(CallTreeFixtures));
    }
}

public static partial class UnsafeEvidenceFixtures
{
    public static unsafe int UnsafePointerRead(int* value) => *value;

    public static uint CallsUnsafeAs(ref int value) => Unsafe.As<int, uint>(ref value);

    [DllImport("kernel32.dll")]
    public static extern int PInvokeOnly();
}
