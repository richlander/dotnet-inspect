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
    public void BuildCallerTree_WithScope_OmitsCallersOfSameNameMemberInAnotherAssembly()
    {
        // Root the caller graph at the real Target.Api.Ping. One scope (CallerGraphCaller) calls
        // the real target; the other (CallerGraphLookalikeCaller) calls its own in-assembly
        // Target.Api.Ping lookalike with the same fully-qualified name. Only the real caller may
        // be reported — the cross-assembly key must carry callee assembly identity (#1579).
        var targetIndex = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphTarget"));
        var realCaller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));
        var lookalikeCaller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphLookalikeCaller"));

        var ping = targetIndex.Methods.First(method => method.DeclaringType.Name == "Api" && method.Name == "Ping");
        var tree = targetIndex.BuildCallerTree(ping.MetadataToken, new[] { realCaller, lookalikeCaller }, maxDepth: 2, maxNodes: 50);

        var sources = tree.Children.Select(child => child.Perf?.Source).ToList();
        Assert.Contains("ILInspector.Analysis.CallerGraphCaller", sources);
        Assert.DoesNotContain("ILInspector.Analysis.CallerGraphLookalikeCaller", sources);
        Assert.Single(tree.Children);
    }

    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameSignatureCallersFromDifferentAssembliesDistinct()
    {
        // Two caller assemblies declare the identical Shared.Entry.Run signature and both call the
        // real Target.Api.Ping. They must remain two distinct direct caller nodes; a key that omits
        // the caller source assembly collapses them into one (#1579).
        var targetIndex = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphTarget"));
        var caller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));
        var twin = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCallerTwin"));

        var ping = targetIndex.Methods.First(method => method.DeclaringType.Name == "Api" && method.Name == "Ping");
        var tree = targetIndex.BuildCallerTree(ping.MetadataToken, new[] { caller, twin }, maxDepth: 2, maxNodes: 50);

        var sources = tree.Children.Select(child => child.Perf?.Source).ToList();
        Assert.Equal(2, tree.Children.Length);
        Assert.Contains("ILInspector.Analysis.CallerGraphCaller", sources);
        Assert.Contains("ILInspector.Analysis.CallerGraphCallerTwin", sources);
    }

    [Fact]
    public void BuildCallerTree_WithScope_KeepsTargetOverloadsDistinct()
    {
        // Root the caller graph at the int overload of Target.Api.Ping. The caller assembly
        // invokes the int and string overloads from separate methods (RunInt/RunString). Only
        // RunInt may be reported: the cross-assembly reverse map keys on CallerGraphKey, which
        // must carry parameter types, or the two overloads collapse and cross-link callers
        // (#1623 rung 1; non-vacuous because same-assembly resolution is token-based).
        var targetIndex = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphTarget"));
        var caller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));

        var intOverload = targetIndex.Methods.Single(method =>
            method.DeclaringType.Name == "Api" && method.Name == "Ping"
            && method.ParameterTypes.Length == 1 && method.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Int32")));

        var tree = targetIndex.BuildCallerTree(intOverload.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50);

        var callerNames = tree.Children.Select(child => child.Member.Name).ToList();
        Assert.Contains("RunInt", callerNames);
        Assert.DoesNotContain("RunString", callerNames);
        Assert.DoesNotContain("Run", callerNames);
    }

    [Fact]
    public void BuildCallerTree_WithScope_LinksConstructedGenericTypeMemberCaller()
    {
        // Root the caller graph at the open Box<T>.Store(T). Another assembly calls
        // Box<int>.Store(1) — a member reference keyed on the List<int>-style instantiation. The
        // cross-assembly reverse map must normalize that constructed declaring type to its open
        // definition so the caller is reported (#1339); before, it under-reported as zero.
        var targetIndex = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphTarget"));
        var caller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));

        var store = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericParameter);
        var tree = targetIndex.BuildCallerTree(store.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50);

        var callerNames = tree.Children.Select(child => child.Member.Name).ToList();
        Assert.Contains("UseBox", callerNames);
        Assert.Contains("ILInspector.Analysis.CallerGraphCaller", tree.Children.Select(child => child.Perf?.Source));
    }

    // #1731: Box<T>.Store(T) and Box<T>.Store(List<T>) share name and arity on the same
    // generic declaring type. Cross-assembly caller graph identity must keep them distinct
    // — rooting at one overload reports only its own constructed caller, not the other's.
    // The prior arity-only erasure (open declaring + name + parameter count) collapsed them
    // and cross-linked the callers, which de-verified #1623 rung 1.
    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameArityGenericOverloadsDistinct()
    {
        var targetIndex = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphTarget"));
        var caller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));

        var storeValue = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericParameter);
        var storeList = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericInstance);

        var valueCallers = targetIndex.BuildCallerTree(storeValue.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();
        var listCallers = targetIndex.BuildCallerTree(storeList.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();

        Assert.Contains("UseBox", valueCallers);
        Assert.DoesNotContain("UseBoxList", valueCallers);
        Assert.Contains("UseBoxList", listCallers);
        Assert.DoesNotContain("UseBox", listCallers);
    }

    // #1741 (review): Box<T> (Box`1) and Box<T1,T2> (Box`2) share a simple name but have
    // different generic arity, each with a same-name/same-arity Store. The declaring-type
    // portion of the caller-graph key must preserve arity so the two Stores stay distinct.
    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameNameGenericTypesOfDifferentArityDistinct()
    {
        var targetIndex = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphTarget"));
        var caller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));

        var box1Store = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`1" && method.Name == "Store"
            && method.ParameterTypes[0].Kind == TypeRefKind.GenericParameter);
        var box2Store = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "Box`2" && method.Name == "Store");

        var box1Callers = targetIndex.BuildCallerTree(box1Store.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();
        var box2Callers = targetIndex.BuildCallerTree(box2Store.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();

        Assert.Contains("UseBox", box1Callers);
        Assert.DoesNotContain("UseBox2", box1Callers);
        Assert.Contains("UseBox2", box2Callers);
        Assert.DoesNotContain("UseBox", box2Callers);
    }

    // #1731 (adversarial review): the cross-assembly caller-graph shape keys on the OPEN
    // signature (VAR/MVAR markers), not the substituted concrete types. A constructed
    // Box<int>.Store(T) call carries a concrete int as its instantiated parameter, but the
    // open signature retains the type-parameter marker — so a type-parameter instantiation
    // stays distinct from a literal of the same type, and the shape matches the open target.
    [Fact]
    public void ResolvedCall_PreservesOpenGenericMarker_DistinctFromInstantiatedParameter()
    {
        var caller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));

        var storeValueCall = caller.DirectCalls.First(call =>
            call.Callee.Name == "Store" && call.Callee.ParameterTypes[0].Kind == TypeRefKind.Definition);

        // Instantiated parameter is the concrete int; the open signature keeps the marker.
        Assert.Equal(TypeRefKind.Definition, storeValueCall.Callee.ParameterTypes[0].Kind);
        Assert.Equal(TypeRefKind.GenericParameter, storeValueCall.Callee.OpenSignatureParameters[0].Kind);
    }

    // #1731 (adversarial review, finding B): the erased generic shape qualifies a generic
    // instance by namespace, so N1.Foo<T> and N2.Foo<T> (same metadata name, different
    // namespace) do not collapse.
    [Fact]
    public void ErasedParameterShape_QualifiesGenericInstanceByNamespace()
    {
        var typeParameter = TypeRef.GenericParameter(0, "T");
        var foo1 = TypeRef.GenericInstance(TypeRef.Definition("AsmA", "N1", "Foo`1"), [typeParameter]);
        var foo2 = TypeRef.GenericInstance(TypeRef.Definition("AsmB", "N2", "Foo`1"), [typeParameter]);

        Assert.NotEqual(
            GenericMemberIdentity.ErasedParameterShape([foo1]),
            GenericMemberIdentity.ErasedParameterShape([foo2]));
    }

    [Fact]
    public void BuildCallerTree_WithScope_LinksConstructedGenericMethodCaller()
    {
        // Root the caller graph at the open Echo<T>(T). Another assembly calls Echo<int>(1) — a
        // MethodSpec keyed on the instantiation. Normalizing generic method identity to the open
        // definition + parameter arity links the caller across assemblies (#1339).
        var targetIndex = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphTarget"));
        var caller = LibraryBodyIndex.Open(CallerGraphFixturePath("ILInspector.Analysis.CallerGraphCaller"));

        var echo = targetIndex.Methods.First(method =>
            method.DeclaringType.Name == "GenericApi" && method.Name == "Echo");
        var tree = targetIndex.BuildCallerTree(echo.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50);

        var callerNames = tree.Children.Select(child => child.Member.Name).ToList();
        Assert.Contains("UseEcho", callerNames);
    }

    [Fact]
    public void MatchesCrossAssembly_MatchesConstructedGenericMemberAgainstOpenTarget()
    {
        // Open target Box<T>.Store(T): declaring type is the open List`1-style definition, the
        // parameter is the type parameter T.
        var openBox = TypeRef.Definition("Lib", "N", "Box`1");
        var pattern = MemberPattern.Method(openBox, "Store", [TypeRef.GenericParameter(0, "T")]);

        // Constructed call site Box<int>.Store(int): declaring type is the instantiation, the
        // parameter is the concrete int.
        var constructedBox = TypeRef.GenericInstance(openBox, [TypeRef.CoreLib("System", "Int32")]);
        var callSite = new MemberRef(constructedBox, "Store", [TypeRef.CoreLib("System", "Int32")], TypeRef.CoreLib("System", "Void"), MemberKind.Method);

        Assert.True(pattern.MatchesCrossAssembly(callSite));
        // The exact same-assembly matcher cannot bridge the open/closed spelling gap.
        Assert.False(pattern.Matches(callSite));
    }

    [Fact]
    public void MatchesCrossAssembly_StillDiscriminatesGenericMembersByArity()
    {
        // A generic member is erased to arity, not dropped entirely: a one-parameter target must
        // not absorb a two-parameter call site on the same generic type.
        var openBox = TypeRef.Definition("Lib", "N", "Box`1");
        var pattern = MemberPattern.Method(openBox, "Store", [TypeRef.GenericParameter(0, "T")]);

        var constructedBox = TypeRef.GenericInstance(openBox, [TypeRef.CoreLib("System", "Int32")]);
        var twoArg = new MemberRef(
            constructedBox,
            "Store",
            [TypeRef.CoreLib("System", "Int32"), TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

        Assert.False(pattern.MatchesCrossAssembly(twoArg));
    }

    static string CallerGraphFixturePath(string projectName)
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName,
            "..",
            "..",
            projectName,
            outputDirectory.Name,
            projectName + ".dll"));
        Assert.True(File.Exists(path), $"Expected caller-graph fixture assembly at {path}");
        return path;
    }

    static string NamedFixturePath(string projectName, string assemblyName)
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName,
            "..",
            "..",
            projectName,
            outputDirectory.Name,
            assemblyName + ".dll"));
        Assert.True(File.Exists(path), $"Expected fixture assembly at {path}");
        return path;
    }

    // #1741: two Ping overloads whose parameter types share the FQN Shared.Token but come
    // from different assemblies (DiffAsmLibA vs DiffAsmLibB). The cross-assembly caller
    // graph must keep them distinct — rooting at one overload reports only its own caller.
    // The prior key rendered parameter types with ToQualifiedDisplayString(), which omits
    // assembly, so both collapsed and cross-linked the callers (de-verified #1623 rung 1).
    [Fact]
    public void BuildCallerTree_WithScope_KeepsSameFqnParametersFromDifferentAssembliesDistinct()
    {
        var target = LibraryBodyIndex.Open(NamedFixturePath("DiffAsmFixtures.Target", "DiffAsmTarget"));
        var caller = LibraryBodyIndex.Open(NamedFixturePath("DiffAsmFixtures.Caller", "DiffAsmCaller"));

        var pingA = target.Methods.First(method =>
            method.Name == "Ping" && method.ParameterTypes[0].Assembly == "DiffAsmLibA");
        var pingB = target.Methods.First(method =>
            method.Name == "Ping" && method.ParameterTypes[0].Assembly == "DiffAsmLibB");

        var aCallers = target.BuildCallerTree(pingA.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();
        var bCallers = target.BuildCallerTree(pingB.MetadataToken, new[] { caller }, maxDepth: 2, maxNodes: 50)
            .Children.Select(child => child.Member.Name).ToList();

        Assert.Contains("UseA", aCallers);
        Assert.DoesNotContain("UseB", aCallers);
        Assert.Contains("UseB", bCallers);
        Assert.DoesNotContain("UseA", bCallers);
    }

    // #1741 (unit): the key fragment for a type includes its assembly, so same-FQN types
    // from different assemblies do not collapse.
    [Fact]
    public void KeyFragment_QualifiesNamedTypeByAssembly()
    {
        var tokenA = TypeRef.Definition("LibA", "Shared", "Token");
        var tokenB = TypeRef.Definition("LibB", "Shared", "Token");

        Assert.NotEqual(GenericMemberIdentity.KeyFragment(tokenA), GenericMemberIdentity.KeyFragment(tokenB));
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
    public void TopLeverage_TreatsMutuallyRecursiveEntryComponentAsRoot()
    {
        var index = LibraryBodyIndex.Open(typeof(LeverageDepthFixtures).Assembly.Location);

        var ranked = index.TopLeverage(count: 100, scope: method => method.DeclaringType.Name == nameof(LeverageDepthFixtures));
        var byName = ranked.ToDictionary(entry => entry.Method.Name);

        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.Ping)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.Pong)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.ChainTop)].RootReach);
        Assert.Equal(1, byName[nameof(LeverageDepthFixtures.ChainLeaf)].RootReach);
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
    public void TopLeverage_CountsCallerOfConstructedGenericDeclaringType()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);

        var ranked = index.TopLeverage(count: 200,
            scope: method => method.DeclaringType.Name == "GenericDeclaringTarget`1");

        var target = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(GenericDeclaringTarget<int>.Target)));
        Assert.Equal(1, target.DirectCallerCount);
        Assert.Equal(1, target.RootReach);

        var targetWithParameter = Assert.Single(ranked.Where(entry => entry.Method.Name == nameof(GenericDeclaringTarget<int>.TargetWithParameter)));
        Assert.Equal(1, targetWithParameter.DirectCallerCount);
    }

    [Fact]
    public void BuildCallerTree_LinksConstructedGenericDeclaringTypeCaller()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);
        var target = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == "GenericDeclaringTarget`1"
            && method.Name == nameof(GenericDeclaringTarget<int>.Target)));

        var tree = index.BuildCallerTree(target.MetadataToken, maxDepth: 2, maxNodes: 20);

        Assert.Contains(tree.Children, child =>
            child.Member.Name == nameof(GenericDeclaringCallers.CallGenericTarget));
    }

    [Fact]
    public void BuildCallTree_LinksConstructedGenericDeclaringTypeCallee()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);
        var caller = Assert.Single(index.Methods.Where(method =>
            method.Name == nameof(GenericDeclaringCallers.CallGenericTarget)));

        var tree = index.BuildCallTree(caller.MetadataToken, maxDepth: 2, maxNodes: 20);

        var child = Assert.Single(tree.Children.Where(node =>
            node.Member.Name == nameof(GenericDeclaringTarget<int>.Target)));
        Assert.Equal(CallTreeStatus.Leaf, child.Status);
        Assert.Equal("ILInspector.Analysis.Tests.GenericDeclaringTarget<int>", child.Member.DeclaringType.ToQualifiedDisplayString());
    }

    // #1623 rung 4: the FORWARD call tree must mark a callee edge invoked inside a loop.
    // The reverse caller tree already pins this (BuildCallerTree_MarksCallerNodeInLoop);
    // BuildCallTree forwards edge.InLoop but had no tree-level guard.
    [Fact]
    public void BuildCallTree_MarksCalleeNodeInLoop_WhenInvokedInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(CallSiteFixtures).Assembly.Location);
        int root = index.Methods.First(m => m.Name == nameof(CallSiteFixtures.CallsConsoleWriteLineInLoop)).MetadataToken;

        var tree = index.BuildCallTree(root, maxDepth: 2, maxNodes: 50);

        var child = Assert.Single(tree.Children.Where(c => c.Member.Name == nameof(CallSiteFixtures.CallsConsoleWriteLine)));
        Assert.True(child.Perf?.InLoop, "forward call-tree must mark the in-loop callee edge");
    }

    // #1623 rung 4: ranking stability. Two methods tied on every leverage metric
    // (caller-count / root-reach / fanout / loop-calls) must order deterministically by
    // metadata token, and that order must not depend on the input method order. Guards the
    // final ThenBy(MetadataToken) tie-break, which was unguarded.
    [Fact]
    public void TopLeverage_TieBreak_IsDeterministicAndStableAcrossInputOrder()
    {
        var root = LeverageMethod("Root", 0x06000010);
        var tieA = LeverageMethod("TieA", 0x06000001);
        var tieB = LeverageMethod("TieB", 0x06000002);
        // Root calls each tied method exactly once -> equal caller-count(1)/reach(1)/fanout(0)/loop(0).
        var calls = ImmutableArray.Create(LeverageCall(root, tieA), LeverageCall(root, tieB));

        var forward = MethodLeverageRanking.Top(calls, [root, tieA, tieB], count: 10)
            .Select(e => e.Method.Name).Where(n => n is "TieA" or "TieB").ToList();
        var reversed = MethodLeverageRanking.Top(calls, [tieB, tieA, root], count: 10)
            .Select(e => e.Method.Name).Where(n => n is "TieA" or "TieB").ToList();

        Assert.Equal(["TieA", "TieB"], forward);   // deterministic: lower token first
        Assert.Equal(forward, reversed);            // stable across input order
    }

    static MethodIdentity LeverageMethod(string name, int token)
        => new("Asm", Guid.Empty, TypeRef.Definition("Asm", "Ns", "Type"), name, [], TypeRef.CoreLib("System", "Void"), token, IsStatic: true);

    static DirectCall LeverageCall(MethodIdentity caller, MethodIdentity callee)
    {
        var calleeRef = new MemberRef(callee.DeclaringType, callee.Name, callee.ParameterTypes, callee.ReturnType, MemberKind.Method);
        return new DirectCall(caller, calleeRef, ILOffset: 0, OperandToken: callee.MetadataToken, CalleeDefinitionToken: callee.MetadataToken, CallKind.Call);
    }

    [Fact]
    public void TopUnsafeLeverage_CountsCallerOfConstructedGenericDeclaringType()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericDeclaringCallers).Assembly.Location);

        var unsafeTarget = Assert.Single(index.TopUnsafeLeverage(count: 100).Where(entry =>
            entry.Method.DeclaringType.Name == "GenericUnsafeTarget`1"
            && entry.Method.Name == nameof(GenericUnsafeTarget<int>.UnsafeTarget)));

        Assert.Equal(1, unsafeTarget.DirectCallerCount);
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
    public void NestedDeclaringType_DisplayString_PreservesContainingPath()
    {
        var index = LibraryBodyIndex.Open(typeof(NestedLeft).Assembly.Location);
        var ns = typeof(NestedLeft).Namespace;

        // The two `Target` methods live in NestedLeft.Dup and NestedRight.Dup. Their
        // declaring-type display must keep the containing-type path so they do not
        // collapse to a single `<ns>.Dup` identity.
        var displays = index.Methods
            .Where(method => method.Name == nameof(NestedLeft.Dup.Target)
                && method.DeclaringType.Name.EndsWith("+Dup", StringComparison.Ordinal))
            .Select(method => method.DeclaringType.ToQualifiedDisplayString())
            .Distinct()
            .OrderBy(display => display, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, displays.Count);
        Assert.Equal($"{ns}.NestedLeft.Dup", displays[0]);
        Assert.Equal($"{ns}.NestedRight.Dup", displays[1]);
    }

    [Fact]
    public void FindCalls_NestedTypesWithSameSimpleName_StayDistinct()
    {
        var index = LibraryBodyIndex.Open(typeof(NestedLeft).Assembly.Location);
        var ns = typeof(NestedLeft).Namespace;

        var leftCalls = index.FindCalls(MemberPattern.Method($"{ns}.NestedLeft.Dup", nameof(NestedLeft.Dup.Target)));
        var rightCalls = index.FindCalls(MemberPattern.Method($"{ns}.NestedRight.Dup", nameof(NestedRight.Dup.Target)));

        // Each pattern resolves to exactly its own containing type's call site, not both.
        var left = Assert.Single(leftCalls);
        Assert.Equal($"{ns}.NestedLeft", left.Caller.DeclaringType.ToQualifiedDisplayString());
        var right = Assert.Single(rightCalls);
        Assert.Equal($"{ns}.NestedRight", right.Caller.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public void NestedTypeUnderGenericOuter_DisplayString_PreservesPathAndStripsArity()
    {
        var index = LibraryBodyIndex.Open(typeof(GenericOuter<int>).Assembly.Location);
        var ns = typeof(GenericOuter<>).Namespace;

        var leaf = Assert.Single(index.Methods.Where(method =>
            method.Name == nameof(GenericOuter<int>.Inner.Leaf)
            && method.DeclaringType.Name.StartsWith("GenericOuter", StringComparison.Ordinal)));
        Assert.Equal($"{ns}.GenericOuter.Inner", leaf.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public void FindCalls_DistinguishesOverloadsBySignature()
    {
        var index = LibraryBodyIndex.Open(typeof(OverloadTargets).Assembly.Location);
        var declaring = $"{typeof(OverloadTargets).Namespace}.{nameof(OverloadTargets)}";

        var noArg = index.FindCalls(MemberPattern.Method(declaring, nameof(OverloadTargets.M), ImmutableArray<TypeRef>.Empty));
        var intArg = index.FindCalls(MemberPattern.Method(declaring, nameof(OverloadTargets.M), ImmutableArray.Create(TypeRef.CoreLib("System", "Int32"))));
        var stringArg = index.FindCalls(MemberPattern.Method(declaring, nameof(OverloadTargets.M), ImmutableArray.Create(TypeRef.CoreLib("System", "String"))));

        // Each signature pattern resolves to exactly its own overload's call site.
        Assert.Empty(Assert.Single(noArg).Callee.ParameterTypes);
        Assert.Equal(TypeRef.CoreLib("System", "Int32"), Assert.Single(Assert.Single(intArg).Callee.ParameterTypes));
        Assert.Equal(TypeRef.CoreLib("System", "String"), Assert.Single(Assert.Single(stringArg).Callee.ParameterTypes));
    }

    [Fact]
    public void TopLeverage_KeepsOverloadsDistinct()
    {
        var index = LibraryBodyIndex.Open(typeof(OverloadTargets).Assembly.Location);

        // Same-assembly calls resolve by MethodDef token, so this guards token-based
        // overload distinctness. The param-bearing key collapse is exercised separately
        // by BuildCallerTree_WithScope_KeepsTargetOverloadsDistinct (cross-assembly).
        var overloads = index.TopLeverage(count: 200,
                scope: method => method.DeclaringType.Name == nameof(OverloadTargets) && method.Name == nameof(OverloadTargets.M))
            .Where(entry => entry.Method.Name == nameof(OverloadTargets.M))
            .ToList();

        // All three overloads remain separate leverage entries, and each is credited
        // with exactly its own single caller. If the keys collapsed, one entry would
        // absorb all three calls and the others would show zero.
        Assert.Equal(3, overloads.Count);
        Assert.All(overloads, entry => Assert.Equal(1, entry.DirectCallerCount));
    }

    [Fact]
    public void BuildCallerTree_OverloadResolvesToOwnDefinition()
    {
        var index = LibraryBodyIndex.Open(typeof(OverloadTargets).Assembly.Location);

        var intOverload = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == nameof(OverloadTargets) && method.Name == nameof(OverloadTargets.M)
            && method.ParameterTypes.Length == 1 && method.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Int32"))));
        var stringOverload = Assert.Single(index.Methods.Where(method =>
            method.DeclaringType.Name == nameof(OverloadTargets) && method.Name == nameof(OverloadTargets.M)
            && method.ParameterTypes.Length == 1 && method.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "String"))));

        // Distinct definitions...
        Assert.NotEqual(intOverload.MetadataToken, stringOverload.MetadataToken);

        // ...and the caller graph for one overload does not pull in the other.
        var tree = index.BuildCallerTree(intOverload.MetadataToken, maxDepth: 2, maxNodes: 20);
        Assert.Contains(tree.Children, child => child.Member.Name == nameof(OverloadCallers.CallsEachOverloadOnce));
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
        // A user type with a generated-looking __Helper_* member but no Grpc.Core call must not
        // be flagged — a generated member name alone is not enough (#1574).
        Assert.DoesNotContain(typeof(GeneratedLookalike).FullName!, generated);
        // Because it is not classified as generated, its optimization opportunity stays visible
        // (Performance Triage suppresses opportunities only for generated-framework types).
        Assert.Contains(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == nameof(GeneratedLookalike)
            && opportunity.Method.Name == nameof(GeneratedLookalike.MakesLocalArrayUnsuppressed));
    }

    [Fact]
    public void GeneratedFrameworkTypeNames_IgnoresUserDefinedProtobufLookalikes()
    {
        // The lookalike fixture assembly is NOT named Google.Protobuf; it declares its own
        // Google.Protobuf.* bootstrap-shaped types and calls them from product code. The
        // protobuf generated-bootstrap predicates require real Google.Protobuf assembly
        // identity, so the calling product type must not be classified as generated (#1580).
        var index = LibraryBodyIndex.Open(LookalikeFixturePath());
        var generated = index.GeneratedFrameworkTypeNames;

        const string lookalikeType = "ILInspector.Analysis.LookalikeFixtures.ProtobufBootstrapLookalike";
        Assert.DoesNotContain(lookalikeType, generated);

        // Because it is not classified as generated, its optimization opportunity stays
        // visible — Performance Triage suppresses opportunities only for generated types.
        Assert.Contains(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.DeclaringType.Name == "ProtobufBootstrapLookalike"
            && opportunity.Method.Name == "ShouldStillBeActionable");
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
    public void OptimizationOpportunities_FlagsLinqMembershipScanInsideLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LinqScanInLoop)
            && o.Shape == "linq-scan-in-loop"));
        Assert.True(op.InLoop);
        Assert.Equal("medium", op.Confidence);
        Assert.Contains("Any", op.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagLinqScanOutsideLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LinqScanOutsideLoop)
            && o.Shape == "linq-scan-in-loop");
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagLazyWhereInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Enumerable.Where is lazy: calling it in a loop does not enumerate, so it is
        // not a repeated scan and must not be flagged.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LazyWhereInLoopNotFlagged)
            && o.Shape == "linq-scan-in-loop");
    }

    [Fact]
    public void OptimizationOpportunities_FlagsScanMethodInvokedInCallerLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ContainsKey)
            && o.Shape == "scan-method-in-loop-call"));
        Assert.True(op.InLoop);
        Assert.Equal("low", op.Confidence);
        Assert.Contains(nameof(OptimizationOpportunityFixtures.CallsScanHelperInLoop), op.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagScanMethodInvokedOnlyOutsideLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ContainsKeyNeverLooped)
            && o.Shape == "scan-method-in-loop-call");
    }

    [Fact]
    public void OptimizationOpportunities_FlagsLazyReturningScanMethodInvokedInCallerLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A helper that returns a deferred Where query, enumerated once per caller-loop
        // iteration, is the cross-method shape of a repeated scan (e.g. Aspire's
        // GetChildSpans().Any()). Flagged at low confidence against the helper.
        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.FilterLazy)
            && o.Shape == "scan-method-in-loop-call"));
        Assert.Equal("low", op.Confidence);
        Assert.Contains("Where", op.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizationOpportunities_DoesNotFlagParameterlessTerminalsInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // First()/Any()/Count() with no predicate are O(1) (positional read or the
        // ICollection.Count fast path), so neither shape may flag them in a loop.
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.ParameterlessTerminalsInLoopNotFlagged)
            && (o.Shape == "linq-scan-in-loop" || o.Shape == "scan-method-in-loop-call"));
    }

    [Theory]
    [InlineData("System.Linq")]   // .NET 5+ reference assemblies
    [InlineData("System.Core")]   // .NET Framework
    [InlineData("netstandard")]   // canonicalizes to the core library
    public void IsLinqMembershipScan_MatchesPredicateOverloadAcrossTargetFrameworks(string assembly)
    {
        var enumerable = TypeRef.Definition(assembly, "System.Linq", "Enumerable");
        var anyPredicate = new MemberRef(
            enumerable,
            "Any",
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), TypeRef.CoreLib("System", "Func`2")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);

        Assert.True(LibraryBodyIndex.IsLinqMembershipScan(anyPredicate, out var op));
        Assert.Equal("Any", op);
    }

    [Theory]
    [InlineData("Any")]
    [InlineData("First")]
    [InlineData("Single")]
    [InlineData("Count")]
    [InlineData("Last")]
    public void IsLinqMembershipScan_RejectsParameterlessOverload(string name)
    {
        var enumerable = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable");
        var parameterless = new MemberRef(
            enumerable,
            name,
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);

        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(parameterless, out _));
    }

    [Fact]
    public void IsLinqMembershipScan_RejectsLazyOperatorAndUserTypeLookalike()
    {
        var enumerable = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable");
        var where = new MemberRef(
            enumerable,
            "Where",
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), TypeRef.CoreLib("System", "Func`2")],
            TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"),
            MemberKind.Method);
        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(where, out _));

        // A user-defined Enumerable in a different namespace/assembly must not match.
        var userEnumerable = TypeRef.Definition("MyLib", "My.Linq", "Enumerable");
        var userAny = new MemberRef(
            userEnumerable,
            "Any",
            [TypeRef.CoreLib("System", "Object"), TypeRef.CoreLib("System", "Object")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);
        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(userAny, out _));
    }

    [Fact]
    public void IsLinqMembershipScan_RejectsSameNameAssemblyWithoutFrameworkKey()
    {
        // #1708 Row A: an assembly literally named System.Linq but without a framework
        // public-key-token (trusted = false) must not be classified as real LINQ for the
        // #1725 repeated-scan shapes — the matcher must honor the trust gate, not just the
        // simple name.
        var spoof = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable", trustedFrameworkAssembly: false);
        var anyPredicate = new MemberRef(
            spoof,
            "Any",
            [TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), TypeRef.CoreLib("System", "Func`2")],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);

        Assert.False(LibraryBodyIndex.IsLinqMembershipScan(anyPredicate, out _));
    }

    [Fact]
    public void OptimizationOpportunities_CapturingLambdaIsCapturingDelegate_SingleRow()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var shape = Assert.Single(DelegateShapes(index, nameof(OptimizationOpportunityFixtures.CapturingLambda)));
        Assert.Equal("capturing-delegate", shape);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateConfidence_IsLoopGated()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A one-shot capturing delegate (not in a loop) is low-value -> low confidence,
        // especially since .NET 10+ partially stack-allocates non-escaping closures.
        var oneShot = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambda)
            && o.Shape == "capturing-delegate"));
        Assert.False(oneShot.InLoop);
        Assert.Equal("low", oneShot.Confidence);

        // A capturing delegate allocated inside a loop is a repeated allocation -> high.
        var inLoop = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop)
            && o.Shape == "capturing-delegate"));
        Assert.True(inLoop.InLoop);
        Assert.Equal("high", inLoop.Confidence);
    }

    [Theory]
    // Non-loop delegate on a high-reach method -> lifted from low to medium.
    [InlineData("capturing-delegate", false, "low", LibraryBodyIndex.DelegateHotRootReach, "medium")]
    [InlineData("instance-method-group-delegate", false, "low", 50, "medium")]
    // Below the reach threshold -> stays low (cold one-shot).
    [InlineData("capturing-delegate", false, "low", LibraryBodyIndex.DelegateHotRootReach - 1, "low")]
    // In-loop (already high) and non-delegate shapes are never adjusted.
    [InlineData("capturing-delegate", true, "high", 99, "high")]
    [InlineData("box-value-type", false, "low", 99, "low")]
    public void AdjustDelegateConfidenceForReach_LiftsHotNonLoopDelegatesOnly(
        string shape, bool inLoop, string confidence, int rootReach, string expected)
    {
        Assert.Equal(expected, LibraryBodyIndex.AdjustDelegateConfidenceForReach(shape, inLoop, confidence, rootReach));
    }

    [Fact]
    public void OptimizationOpportunities_SuppressesBlazorRenderMethods()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // RenderLikeMethod takes a RenderTreeBuilder and allocates a capturing delegate, but
        // Razor render plumbing is suppressed (intrinsic component-model cost, not actionable).
        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.RenderLikeMethod));
    }

    [Fact]
    public void OptimizationOpportunities_DelegateConsumedByLazyLinq_FixDescribesMovedAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambdaConsumedByWhere)
            && o.Shape == "capturing-delegate"));
        // The surfaced Fix text (not just the dropped Caveat) must convey that the closure
        // rewrite reduces but does not eliminate the allocation (the lazy iterator remains).
        Assert.Contains("reduced, not eliminated", op.SafeFixDirection, StringComparison.Ordinal);
        Assert.Contains("iterator", op.SafeFixDirection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateNotConsumedByLinq_KeepsDefaultFix()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambdaConsumedByNonLinq)
            && o.Shape == "capturing-delegate"));
        Assert.DoesNotContain("iterator", op.SafeFixDirection, StringComparison.OrdinalIgnoreCase);
        // Not consumed by LINQ, so the lazy/iterator override does not fire; the row keeps
        // the default escape-awareness caveat (calibration), not the iterator caveat.
        Assert.NotNull(op.Caveat);
        Assert.Contains("escapes", op.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iterator", op.Caveat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateConsumedByMembershipTerminal_NotGivenLazyIteratorFix()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A predicate consumed by an EAGER membership terminal (Any) allocates no iterator,
        // so it must NOT receive the lazy/iterator "moved allocation" wording. The repeated
        // scan itself is covered separately by the linq-scan-in-loop shape. The row keeps the
        // default escape-awareness caveat, not the iterator caveat.
        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.LinqScanInLoop)
            && o.Shape == "capturing-delegate"));
        Assert.DoesNotContain("iterator", op.SafeFixDirection, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(op.Caveat);
        Assert.Contains("escapes", op.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iterator", op.Caveat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OptimizationOpportunities_DelegateShapes_CarryEscapeAwarenessCaveat()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Calibration (#1714): on .NET 10+ the JIT stack-allocates non-escaping
        // closures/delegates, so the high-confidence delegate shapes must carry an
        // escape-awareness caveat (mirroring box-value-type), not assert an
        // unconditional allocation.
        var op = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.CapturingLambda)
            && o.Shape == "capturing-delegate"));
        Assert.NotNull(op.Caveat);
        Assert.Contains("escapes", op.Caveat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".NET 10", op.Caveat, StringComparison.Ordinal);
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
    public void OptimizationOpportunities_UserDisplayClassName_IsInstanceMethodGroupDelegate()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var shape = Assert.Single(DelegateShapes(index, nameof(OptimizationOpportunityFixtures.UserTypeNameContainsDisplayClass)));
        Assert.Equal("instance-method-group-delegate", shape);
    }

    [Fact]
    public void OptimizationOpportunities_GenericCachedLambda_NotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures.GenericOptimizationOpportunityFixtures<>).Assembly.Location);

        Assert.Empty(index.OptimizationOpportunities
            .Where(o => o.Method.DeclaringType.Name.EndsWith("+GenericOptimizationOpportunityFixtures`1", StringComparison.Ordinal)
                && o.Method.Name == nameof(OptimizationOpportunityFixtures.GenericOptimizationOpportunityFixtures<int>.NonCapturingLambda)
                && o.Shape is "delegate-allocation" or "capturing-delegate" or "instance-method-group-delegate"));
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

    static System.Collections.Generic.List<OptimizationOpportunity> BoxRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "box-value-type")
            .ToList();

    static System.Collections.Generic.List<OptimizationOpportunity> HotspotRows(LibraryBodyIndex index, string methodName)
        => index.OptimizationOpportunities
            .Where(o => o.Method.Name == methodName && o.Shape == "allocation-hotspot")
            .ToList();

    [Fact]
    public void OptimizationOpportunities_AllocationDenseNonLoopMethod_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A dense but NON-loop method is usually intrinsic one-shot construction, not
        // reducible repeated waste -> no hotspot row (avoids flooding allocation-heavy code).
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyObjects)));
    }

    [Fact]
    public void OptimizationOpportunities_AllocationDenseLoop_IsMediumHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Dense allocation inside a loop, matching no specific shape -> a medium hotspot.
        var row = Assert.Single(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)));
        Assert.Equal("medium", row.Confidence);
        Assert.True(row.InLoop);
        Assert.Contains("in a loop", row.Evidence);
    }

    [Fact]
    public void OptimizationOpportunities_AllocationDenseLoopWithSpecificShape_IsDeduped()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // The method is dense-in-loop but already has a specific (capturing-delegate) row, so
        // the vague aggregate hotspot row is deduped away.
        Assert.NotEmpty(index.OptimizationOpportunities.Where(o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.AllocatesDenselyInLoopWithDelegate)
            && o.Shape == "capturing-delegate"));
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesDenselyInLoopWithDelegate)));
    }

    [Fact]
    public void OptimizationOpportunities_ValueTypeConstructionInLoop_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A loop densely constructing VALUE types (Nullable + an in-assembly struct) clears the
        // >= 16 newobj count but allocates nothing on the heap -> must not be a hotspot.
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ConstructsManyValueTypesInLoop)));
    }

    [Fact]
    public void OptimizationOpportunities_LowAllocationMethod_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A method below the allocation threshold does not produce a hotspot row (avoids noise).
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)));
    }

    [Fact]
    public void OptimizationOpportunities_ManyExceptionArms_IsNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Exception construction only allocates on throw paths, so a method that is mostly
        // `throw new ...` arms is not steady-state allocation pay-dirt and must not be a hotspot.
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyThrowArms)));
    }

    [Fact]
    public void OptimizationOpportunities_PseudoExceptionAllocationsInLoop_AreAllocationHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // Pseudo-exceptions (non-Exception types named like exceptions) are real steady-state
        // allocations and must not be excluded as exception construction.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyPseudoExceptionsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Allocations >= 16, $"expected >= 16 allocations, got {s.Allocations}");

        var row = Assert.Single(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyPseudoExceptionsInLoop)));
        Assert.Equal("medium", row.Confidence);
        Assert.True(row.InLoop);
    }

    [Fact]
    public void OptimizationOpportunities_PlainObjectAllocationsNonLoop_AreNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // Plain custom objects are counted as allocations (not excluded), but a non-loop
        // dense method is no longer flagged as a hotspot.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyPlainObjects)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Allocations >= 8, $"expected >= 8 allocations, got {s.Allocations}");

        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesManyPlainObjects)));
    }

    [Fact]
    public void OptimizationOpportunities_CustomExceptionThrowArms_AreNotHotspot()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyCustomThrowArms)));
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyDerivedCustomThrowArms)));
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.ManyCrossAssemblyCustomThrowArms)));
    }

    [Fact]
    public void OptimizationOpportunities_BoxIntoObjectApi_IsBoxValueType()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Passing an int to an object-typed API boxes it -> a real heap allocation.
        var row = Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)));
        Assert.Equal("medium", row.Confidence);
        Assert.False(row.InLoop);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesGuidValue))]
    [InlineData(nameof(OptimizationOpportunityFixtures.BoxesDateTimeValue))]
    public void OptimizationOpportunities_ExternalWellKnownValueTypeBox_IsBoxValueType(string methodName)
    {
        // #1623 rung 3: boxing a referenced (TypeReference) framework struct is recognized
        // only via IsWellKnownValueType's curated set, since the SRM-direct product cannot
        // resolve the external type definition. Recall the curated external value types so
        // dropping one is not silent.
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var row = Assert.Single(BoxRows(index, methodName));
        Assert.Equal("medium", row.Confidence);
        Assert.False(row.InLoop);
    }

    [Fact]
    public void OptimizationOpportunities_BoxInLoop_IsHighConfidence()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // Boxing inside a loop is repeated cost -> promoted to high confidence.
        var row = Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesInLoop)));
        Assert.Equal("high", row.Confidence);
        Assert.True(row.InLoop);
    }

    [Fact]
    public void MethodSignals_Allocations_CountBoxing()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // A method that boxes a value type but performs no newobj/newarr must still report a
        // heap allocation in its signals (box allocates, like newobj/newarr).
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Allocations >= 1, $"expected boxing to count as an allocation, got {s.Allocations}");
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.UserJoinLookalike))]
    [InlineData(nameof(OptimizationOpportunityFixtures.UserConcatLookalike))]
    [InlineData(nameof(OptimizationOpportunityFixtures.UserSubstringLookalike))]
    public void MethodSignals_UserCopyNameLookalikes_DoNotCountCopies(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        int copies = signals.TryGetValue(method.MetadataToken, out var s) ? s.Copies : 0;
        Assert.Equal(0, copies);
    }

    [Theory]
    [InlineData(nameof(OptimizationOpportunityFixtures.EnumerableToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ListToArrayNotFlagged))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StringConcatCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StringJoinCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.StringSubstringCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.EnumerableToListCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.ArrayCopyToCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.SpanCopyToCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.MutableSpanCopyToCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.SpanToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.MutableSpanToArrayCopy))]
    [InlineData(nameof(OptimizationOpportunityFixtures.RangeSubArrayCopy))]
    public void MethodSignals_FrameworkCopyApis_CountCopies(string methodName)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s), $"expected copy signal for {methodName}");
        Assert.True(s.Copies >= 1, $"expected at least one copy for {methodName}, got {s.Copies}");
    }

    // #1623 rung 3 / #1715: reflection recall per IsReflectionApi branch. Without these,
    // a regression removing the System.Reflection.* namespace path, the Expressions
    // path, or a System.Type curated-set member would not fail any recall gate.
    [Theory]
    [InlineData(nameof(ReflectionRecallFixtures.InvokesViaReflection), 1)]
    [InlineData(nameof(ReflectionRecallFixtures.EnumeratesAssemblyTypes), 1)]
    [InlineData(nameof(ReflectionRecallFixtures.BuildsExpression), 1)]
    [InlineData(nameof(ReflectionRecallFixtures.CallsTypeMemberSet), 4)]
    public void MethodSignals_ReflectionApis_CountReflection(string methodName, int expected)
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s), $"expected reflection signal for {methodName}");
        Assert.True(s.Reflection >= expected, $"expected >= {expected} reflection for {methodName}, got {s.Reflection}");
    }

    // #1623 rung 3 (signal recall): one consolidated per-signal recall scorecard over a
    // labeled in-assembly fixture. Each row names a method seeded with a known signal and
    // asserts the analysis detects it, so a whole signal family (or one recognized
    // framework-API variant) silently going dark fails here in one place rather than only
    // in a single distant test. Recall is measured on hand-seeded in-assembly fixtures
    // (the SRM-direct no-referenced-assembly-loading boundary applies, per #1623
    // Invariant 1); real-world / corpus recall is rungs 8-10, not this rung.
    [Fact]
    public void Rung3_SignalRecall_DetectsEverySeededSignalFamily()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        MethodSignals Signal(string method)
        {
            var m = Assert.Single(index.Methods.Where(x => x.Name == method));
            Assert.True(signals.TryGetValue(m.MetadataToken, out var s), $"no signals computed for {method}");
            return s!;
        }

        bool HasOpportunity(string method, string shape)
            => index.OptimizationOpportunities.Any(o => o.Method.Name == method && o.Shape == shape);

        bool HasUnsafe(string method)
            => index.UnsafeEvidence.Any(e => e.Member.Name == method);

        // --- MethodSignals families ---
        Assert.True(Signal(nameof(CallTreeFixtures.AllocatesAndCopies)).Allocations >= 2, "object allocation (newobj)");
        Assert.True(Signal(nameof(CallTreeFixtures.AllocatesArray)).Allocations >= 1, "array allocation (newarr)");
        Assert.True(Signal(nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat)).Allocations >= 1, "boxing allocation (box)");
        Assert.True(Signal(nameof(CallTreeFixtures.AllocatesAndCopies)).Copies >= 1, "copy signal");
        Assert.True(Signal(nameof(OptimizationOpportunityFixtures.SpanToArrayCopy)).Copies >= 1, "copy: span ToArray");
        Assert.Equal(2, Signal(nameof(CallTreeFixtures.Reflects)).Reflection); // seeded: Type.GetMethods + Activator.CreateInstance
        Assert.True(Signal(nameof(ReflectionRecallFixtures.EnumeratesAssemblyTypes)).Reflection >= 1, "reflection: System.Reflection.* namespace");
        Assert.True(Signal(nameof(ReflectionRecallFixtures.BuildsExpression)).Reflection >= 1, "reflection: System.Linq.Expressions");
        Assert.True(Signal(nameof(ReflectionRecallFixtures.CallsTypeMemberSet)).Reflection >= 4, "reflection: System.Type curated member set");
        Assert.True(HasUnsafe(nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)), "unsafe evidence");
        Assert.True(Signal(nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)).Unsafe, "unsafe signal (folded MethodSignals.Unsafe field)");
        Assert.True(Signal(nameof(CallTreeFixtures.Throws)).Throws >= 1, "throw signal");
        Assert.True(Signal(nameof(CallTreeFixtures.TryCatchFinally)).Catches >= 1, "catch signal");
        Assert.True(Signal(nameof(CallTreeFixtures.TryCatchFinally)).Finallys >= 1, "finally signal");
        Assert.Contains("InvalidOperationException", Signal(nameof(CallTreeFixtures.Throws)).ExceptionTypes);
        Assert.True(Signal(nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)).AllocInLoop, "alloc-in-loop hot path");

        // --- OptimizationOpportunity shapes ---
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop), "allocation-hotspot"), "allocation-hotspot");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.LocalArrayStaysLocal), "stackalloc-candidate"), "stackalloc-candidate");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.SpanToArrayCopy), "span-to-array-copy"), "span-to-array-copy");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.FirstValueByte), "temporary-byte-array-copy"), "temporary-byte-array-copy");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.BoxesIntoStringFormat), "box-value-type"), "box-value-type");
        Assert.True(HasOpportunity(nameof(OptimizationOpportunityFixtures.BoxesGuidValue), "box-value-type"), "box: external well-known value type (Guid)");

        // A dense in-loop allocation hotspot (matching no specific shape) is recalled at medium.
        Assert.Contains(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)
            && o.Shape == "allocation-hotspot" && o.InLoop && o.Confidence == "medium");
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueForLoopAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyObjectsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForOneTimeAllocation()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesManyObjects)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueBelowHotspotThreshold_WithoutOpportunity()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // The key fidelity case: a single allocation in a loop is hot, but below the
        // hotspot threshold and matching no shape, so it surfaces no opportunity. The
        // loop bit must still be set (it is not gated on the opportunity machinery).
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesOnceInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesOnceInLoop)));
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueForInAssemblyPseudoExceptionLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // #1607: a same-assembly type whose name ends with Exception is not an
        // exception unless the metadata base chain proves it derives from System.Exception.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.AllocatesPseudoExceptionOnceInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
        Assert.DoesNotContain(nameof(PseudoException), s.ExceptionTypes);
        Assert.Empty(HotspotRows(index, nameof(OptimizationOpportunityFixtures.AllocatesPseudoExceptionOnceInLoop)));
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForExceptionConstructionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // Exception construction inside a loop only allocates on the throw path, so the
        // hot-allocation bit must exclude it.
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.ThrowsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForInitializedExceptionConstructionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.ThrowsInitializedExceptionInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_FalseForConditionalExceptionConstructionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.ThrowsConditionalExceptionInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.False(s.AllocInLoop);
    }

    [Fact]
    public void MethodSignals_AllocInLoop_TrueForRetainedExceptionInLoop()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var signals = index.GetMethodSignals();

        // A real exception retained (stored) per iteration is a steady-state hot
        // allocation, distinct from throw-path construction, so the loop bit is set (#1610).
        var method = Assert.Single(index.Methods.Where(m =>
            m.Name == nameof(OptimizationOpportunityFixtures.RetainsExceptionsInLoop)));
        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.AllocInLoop);
    }

    [Fact]
    public void OptimizationOpportunities_BoxOnThrowPathInLoop_NotPromoted()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A box that feeds an exception message only allocates on the throw path, so even inside
        // a loop it is not hot pay-dirt: reported, but medium and not loop-promoted.
        var row = Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesIntoThrowMessage)));
        Assert.Equal("medium", row.Confidence);
        Assert.False(row.InLoop);
    }

    [Fact]
    public void OptimizationOpportunities_GenericParameterBox_NotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // `box !!T` is compiler-mandated and JIT-specialized; not a user-actionable allocation.
        Assert.Empty(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesGenericParameter)));
    }

    [Fact]
    public void OptimizationOpportunities_NullableBox_NotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // `box Nullable<T>` pushes null (no allocation) when the value is absent, so it is
        // conservatively not reported.
        Assert.Empty(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesNullable)));
    }

    [Fact]
    public void OptimizationOpportunities_InAssemblyStructBox_IsBoxValueType()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A non-generic in-assembly struct is positively identified as a value type via its
        // System.ValueType base, so boxing it into an object-typed API is reported.
        Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesInAssemblyStruct)));
    }

    [Fact]
    public void OptimizationOpportunities_GenericStructBox_IsBoxValueType()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // A constructed generic struct boxes via a TypeSpec; value-type-ness is read from the
        // signature blob, so it is reported (not missed for lacking a well-known name).
        Assert.Single(BoxRows(index, nameof(OptimizationOpportunityFixtures.BoxesGenericStruct)));
    }

    [Fact]
    public void OptimizationOpportunities_BitConverterGetBytes_IsTemporaryByteArrayCopy()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        // BitConverter.GetBytes allocates a transient byte[] that is usually replaceable by
        // BinaryPrimitives.Write* / a stackalloc span.
        Assert.Contains(index.OptimizationOpportunities, o =>
            o.Method.Name == nameof(OptimizationOpportunityFixtures.FirstValueByte)
            && o.Shape == "temporary-byte-array-copy");
    }

    [Fact]
    public void FrameworkApiPredicates_AcceptRealFrameworkApis()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var signals = index.GetMethodSignals();
        var reflects = Assert.Single(index.Methods.Where(method =>
            method.Name == nameof(CallTreeFixtures.Reflects)));
        Assert.True(signals.TryGetValue(reflects.MetadataToken, out var reflectSignals));
        Assert.Equal(2, reflectSignals.Reflection);

        Assert.Contains(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs)
            && evidence.Reason == "Unsafe call"
            && evidence.Detail.Contains("System.Runtime.CompilerServices.Unsafe.As<int, uint>", StringComparison.Ordinal));

        Assert.Contains(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.Name == nameof(OptimizationOpportunityFixtures.FirstValueByte)
            && opportunity.Shape == "temporary-byte-array-copy");
    }

    [Fact]
    public void FrameworkApiPredicates_IgnoreUserDefinedLookalikes()
    {
        var index = LibraryBodyIndex.Open(LookalikeFixturePath());
        var signals = index.GetMethodSignals();

        var fakeReflection = Assert.Single(index.Methods.Where(method =>
            method.Name == "CallsFakeReflection"));
        signals.TryGetValue(fakeReflection.MetadataToken, out var fakeReflectionSignals);
        Assert.Equal(0, fakeReflectionSignals?.Reflection ?? 0);

        Assert.DoesNotContain(index.UnsafeEvidence, evidence =>
            evidence.Member.Name == "CallsFakeUnsafe"
            && evidence.Reason == "Unsafe call");

        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Method.Name == "CallsFakeBitConverter"
            && opportunity.Shape == "temporary-byte-array-copy");
    }

    static string LookalikeFixturePath()
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName,
            "..",
            "..",
            "ILInspector.Analysis.LookalikeFixtures",
            outputDirectory.Name,
            "ILInspector.Analysis.LookalikeFixtures.dll"));
        Assert.True(File.Exists(path), $"Expected lookalike fixture assembly at {path}");
        return path;
    }

    static string FacadeFixturePath()
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName,
            "..",
            "..",
            "ILInspector.Analysis.FacadeFixtures",
            outputDirectory.Name,
            "ILInspector.Analysis.FacadeFixtures.dll"));
        Assert.True(File.Exists(path), $"Expected facade fixture assembly at {path}");
        return path;
    }

    static string SpoofFixturePath()
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName,
            "..",
            "..",
            "ILInspector.Analysis.SpoofFixtures",
            outputDirectory.Name,
            "System.Linq.dll"));
        Assert.True(File.Exists(path), $"Expected spoof fixture assembly at {path}");
        return path;
    }

    // #1708 Row A end-to-end. A real assembly literally named "System.Linq" (unsigned,
    // so no framework public-key-token) exposes a System.Linq.Enumerable.ToArray
    // lookalike. Simple-name identity accepted it as a framework copy; strong
    // (public-key-token) identity must reject it. The decoder lowers
    // TrustedFrameworkAssembly from the AssemblyDefinition's (empty) key.
    [Fact]
    public void MethodSignals_CopyApis_RejectSimpleNameSpoofWithoutFrameworkKey()
    {
        var index = LibraryBodyIndex.Open(SpoofFixturePath());
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == "CallsFakeEnumerableToArray"));

        int copies = signals.TryGetValue(method.MetadataToken, out var s) ? s.Copies : 0;
        Assert.Equal(0, copies);
    }

    static string SpoofRuntimeFixturePath()
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName,
            "..",
            "..",
            "ILInspector.Analysis.SpoofRuntimeFixtures",
            outputDirectory.Name,
            "System.Runtime.dll"));
        Assert.True(File.Exists(path), $"Expected runtime spoof fixture assembly at {path}");
        return path;
    }

    // #1708 Row A, span-to-array opportunity path. An assembly named "System.Runtime"
    // (unsigned -> canonicalizes to corelib, no framework key) exposes a System.Span<T>
    // lookalike. The span-to-array-copy opportunity must require a trusted framework key,
    // not just the corelib-canonical name.
    [Fact]
    public void OptimizationOpportunities_SpanToArray_RejectSimpleNameSpoofWithoutFrameworkKey()
    {
        var index = LibraryBodyIndex.Open(SpoofRuntimeFixturePath());

        Assert.DoesNotContain(index.OptimizationOpportunities, o =>
            o.Method.Name == "CallsFakeSpanToArray" && o.Shape == "span-to-array-copy");
    }
    // netstandard facade (assembly canonicalizes to corelib), reproducing the
    // legacy-TFM recall gap where copy predicates expected the modern split assembly.
    [Theory]
    [InlineData("EnumerableToArrayCopy")]
    [InlineData("EnumerableToListCopy")]
    public void MethodSignals_CopyApis_RecognizeLegacyFacadeAssemblies(string methodName)
    {
        var index = LibraryBodyIndex.Open(FacadeFixturePath());
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == methodName));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s), $"no signals for {methodName}");
        Assert.True(s.Copies >= 1, $"expected copy on facade assembly for {methodName}, got {s.Copies}");
    }

    [Fact]
    public void MethodSignals_Reflection_RecognizesLegacyFacadeExpressions()
    {
        var index = LibraryBodyIndex.Open(FacadeFixturePath());
        var signals = index.GetMethodSignals();
        var method = Assert.Single(index.Methods.Where(m => m.Name == "BuildsExpression"));

        Assert.True(signals.TryGetValue(method.MetadataToken, out var s));
        Assert.True(s.Reflection >= 1, $"expected reflection on facade assembly, got {s.Reflection}");
    }

    // #1708 Row B (.NET Framework path). Real net48 assemblies cannot be built on this
    // host, so exercise the predicate directly: Enumerable lives in System.Core on net48
    // and in the netstandard/corelib facade bucket on netstandard, yet a same-named user
    // assembly must still be rejected (precision preserved).
    [Theory]
    [InlineData("System.Linq", 1)]   // modern split assembly
    [InlineData("System.Core", 1)]   // .NET Framework facade
    [InlineData("netstandard", 1)]   // canonicalizes to corelib
    [InlineData("Contoso.Data", 0)]  // user assembly lookalike -> not a copy
    public void Collect_CopyApiRecall_HonorsFrameworkFacadesButNotUserLookalikes(string calleeAssembly, int expectedCopies)
    {
        const int callerToken = 0x06000123;
        var calls = ImmutableArray.Create(EnumerableToArrayCall(calleeAssembly, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int copies = signals.TryGetValue(callerToken, out var s) ? s.Copies : 0;
        Assert.Equal(expectedCopies, copies);
    }

    static DirectCall EnumerableToArrayCall(string calleeAssembly, int callerToken)
    {
        var enumerable = TypeRef.Definition(calleeAssembly, "System.Linq", "Enumerable");
        var callee = new MemberRef(enumerable, "ToArray", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        return FrameworkCall(callee, callerToken);
    }

    // #1708 Row B reflection path. Expression trees live in System.Core on net48 and in
    // the netstandard/corelib facade bucket on netstandard, but a same-named user
    // assembly must not be classified as reflection.
    [Theory]
    [InlineData("System.Linq.Expressions", 1)] // modern split assembly
    [InlineData("System.Core", 1)]             // .NET Framework facade
    [InlineData("netstandard", 1)]             // canonicalizes to corelib
    [InlineData("Contoso.Expr", 0)]            // user assembly lookalike -> not reflection
    public void Collect_ReflectionRecall_HonorsFrameworkFacadesButNotUserLookalikes(string calleeAssembly, int expectedReflection)
    {
        const int callerToken = 0x06000124;
        var calls = ImmutableArray.Create(ExpressionConstantCall(calleeAssembly, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int reflection = signals.TryGetValue(callerToken, out var s) ? s.Reflection : 0;
        Assert.Equal(expectedReflection, reflection);
    }

    static DirectCall ExpressionConstantCall(string calleeAssembly, int callerToken)
    {
        var expression = TypeRef.Definition(calleeAssembly, "System.Linq.Expressions", "Expression");
        var callee = new MemberRef(expression, "Constant", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        return FrameworkCall(callee, callerToken);
    }

    // #1708 Row A. The same framework-named assembly is accepted only when it carries a
    // known framework public-key-token (TrustedFrameworkAssembly). A simple-name match
    // alone (trusted = false) must not fire the copy or reflection signal.
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Collect_CopyApi_RequiresTrustedFrameworkAssembly(bool trusted, int expectedCopies)
    {
        const int callerToken = 0x06000201;
        var enumerable = TypeRef.Definition("System.Linq", "System.Linq", "Enumerable", trustedFrameworkAssembly: trusted);
        var callee = new MemberRef(enumerable, "ToArray", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        var calls = ImmutableArray.Create(FrameworkCall(callee, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int copies = signals.TryGetValue(callerToken, out var s) ? s.Copies : 0;
        Assert.Equal(expectedCopies, copies);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Collect_ReflectionApi_RequiresTrustedFrameworkAssembly(bool trusted, int expectedReflection)
    {
        const int callerToken = 0x06000202;
        var expression = TypeRef.Definition("System.Linq.Expressions", "System.Linq.Expressions", "Expression", trustedFrameworkAssembly: trusted);
        var callee = new MemberRef(expression, "Constant", [], TypeRef.Unsupported("ret"), MemberKind.Method);
        var calls = ImmutableArray.Create(FrameworkCall(callee, callerToken));

        var signals = MethodSignalAnalysis.Collect(calls, ImmutableArray<UnsafeEvidence>.Empty);

        int reflection = signals.TryGetValue(callerToken, out var s) ? s.Reflection : 0;
        Assert.Equal(expectedReflection, reflection);
    }

    static DirectCall FrameworkCall(MemberRef callee, int callerToken)
    {
        var callerType = TypeRef.Definition("Caller.Assembly", "Caller.Ns", "CallerType");
        var caller = new MethodIdentity(
            "Caller.Assembly",
            Guid.Empty,
            callerType,
            "Caller",
            [],
            TypeRef.CoreLib("System", "Void"),
            callerToken,
            IsStatic: true);
        return new DirectCall(caller, callee, ILOffset: 0, OperandToken: 0, CalleeDefinitionToken: 0, CallKind.Call);
    }


    [Fact]
    public void TopUnsafeLeverage_RanksRequiresUnsafeMethodsByCallers()
    {
        var index = LibraryBodyIndex.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        var top = index.TopUnsafeLeverage(count: 100);

        Assert.Contains(top, e =>
            e.Method.Name == nameof(UnsafeEvidenceFixtures.UnsafePointerRead)
            && e.Mode == CallerUnsafeMode.Implicit);
        var pointerExtern = Assert.Single(top.Where(e =>
            e.Method.Name == nameof(UnsafeEvidenceFixtures.PointerExtern)));
        Assert.Equal(2, pointerExtern.DirectCallerCount);
        Assert.Equal(CallerUnsafeMode.Implicit, pointerExtern.Mode);
        Assert.DoesNotContain(top, e => e.Method.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs));
    }
}

public class OptimizationOpportunityFixtures
{
    private readonly int _field = 3;
    private readonly UserDisplayClassTarget _displayClassTarget = new();
    private int[]? _arrayField;

    // A capturing delegate created INSIDE a loop: a repeated allocation -> high confidence.
    public static int CapturingDelegateInLoop(int[] values, int seed)
    {
        var total = 0;
        foreach (var v in values)
        {
            System.Func<int> f = () => v + seed;
            total += f();
        }
        return total;
    }

    // Razor/Blazor render plumbing: a method taking a RenderTreeBuilder. Even though it
    // allocates a capturing delegate, it must be suppressed (intrinsic component-model cost).
    public static void RenderLikeMethod(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder, int seed)
    {
        System.Func<int> handler = () => seed + 1;
        builder.Use(handler);
    }

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

    public static int[] EnumerableToArrayCopy(System.Collections.Generic.IEnumerable<int> values)
        => values.ToArray();

    // --- Repeated LINQ scan inside a loop (linq-scan-in-loop) ---

    // A membership LINQ scan (Enumerable.Any) runs once per loop iteration over a
    // sequence whose size scales with the input -> O(n*m). The canonical fix is to
    // build a HashSet once outside the loop.
    public static int LinqScanInLoop(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var count = 0;
        foreach (var key in keys)
        {
            if (System.Linq.Enumerable.Any(source, x => x == key))
            {
                count++;
            }
        }
        return count;
    }

    // The same membership scan, but not inside any loop -> not a repeated scan.
    public static bool LinqScanOutsideLoop(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Any(source, x => x == key);

    // A lazy operator (Enumerable.Where) called in a loop does not itself enumerate,
    // so it is not a terminal scan and must not be flagged.
    public static int LazyWhereInLoopNotFlagged(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var seen = 0;
        foreach (var key in keys)
        {
            var filtered = System.Linq.Enumerable.Where(source, x => x == key);
            seen += filtered is null ? 0 : 1;
        }
        return seen;
    }

    // --- Cross-method repeated scan (scan-method-in-loop-call) ---

    // A scanning helper: contains a membership scan but no loop in its own body.
    public static bool ContainsKey(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Any(source, x => x == key);

    // Invokes the scanning helper once per loop iteration -> the scan runs O(m) times.
    public static int CallsScanHelperInLoop(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            if (ContainsKey(source, key))
            {
                n++;
            }
        }
        return n;
    }

    // A scanning helper that is only ever invoked outside any loop -> not a repeated scan.
    public static bool ContainsKeyNeverLooped(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Any(source, x => x == key);

    public static bool CallsScanHelperOnceNoLoop(System.Collections.Generic.IEnumerable<int> source, int key)
        => ContainsKeyNeverLooped(source, key);

    // Parameterless positional/aggregate terminals are O(1) (a positional read or the
    // ICollection.Count fast path), so they must NOT be flagged even inside a loop.
    public static int ParameterlessTerminalsInLoopNotFlagged(System.Collections.Generic.List<int> list, int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            if (System.Linq.Enumerable.Any(list))
            {
                n += System.Linq.Enumerable.Count(list);
            }
            if (System.Linq.Enumerable.First(list) == key)
            {
                n++;
            }
        }
        return n;
    }

    // A lazy-returning helper: hands back a deferred Where query without enumerating it.
    public static System.Collections.Generic.IEnumerable<int> FilterLazy(System.Collections.Generic.IEnumerable<int> source, int key)
        => System.Linq.Enumerable.Where(source, x => x == key);

    // Enumerates the lazy helper's result once per loop iteration -> the deferred linear
    // scan runs O(m) times across the call boundary.
    public static int CallsLazyHelperInLoop(System.Collections.Generic.IEnumerable<int> source, int[] keys)
    {
        var n = 0;
        foreach (var key in keys)
        {
            foreach (var _ in FilterLazy(source, key))
            {
                n++;
            }
        }
        return n;
    }

    public static string StringConcatCopy(string left, string right)
        => string.Concat(left, right);

    public static string StringJoinCopy(string[] values)
        => string.Join(",", values);

    public static string StringSubstringCopy(string value)
        => value.Substring(1);

    // Copy-signal variant recall (#1623 rung 3). IsCopyApi recognizes ToList, CopyTo
    // (span/array), and RuntimeHelpers.GetSubArray (range slicing) in addition to the
    // ToArray/Substring/Concat/Join forms above, but those variants had no recall
    // guard — a regression dropping any of them would have been silent.
    public static System.Collections.Generic.List<int> EnumerableToListCopy(System.Collections.Generic.IEnumerable<int> values)
        => System.Linq.Enumerable.ToList(values);

    public static void ArrayCopyToCopy(int[] source, int[] destination)
        => source.CopyTo(destination, 0);

    public static void SpanCopyToCopy(System.ReadOnlySpan<int> source, System.Span<int> destination)
        => source.CopyTo(destination);

    public static void MutableSpanCopyToCopy(System.Span<int> source, System.Span<int> destination)
        => source.CopyTo(destination);

    public static int[] RangeSubArrayCopy(int[] values)
        => values[1..];

    public static string UserJoinLookalike(int a, int b)
        => UserCopyLookalikes.Join(a, b);

    public static string UserConcatLookalike(int a, int b)
        => UserCopyLookalikes.Concat(a, b);

    public static string UserSubstringLookalike(string value)
        => UserCopyLookalikes.Substring(value);

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

    // A capturing lambda consumed by a lazy LINQ operator (Where). Rewriting the lambda as
    // an iterator/local function only MOVES the allocation to the state machine, so the
    // capturing-delegate row carries a "moved allocation" caveat.
    public static System.Collections.Generic.IEnumerable<int> CapturingLambdaConsumedByWhere(
        System.Collections.Generic.IEnumerable<int> source, int threshold)
        => System.Linq.Enumerable.Where(source, x => x > threshold);

    // A capturing lambda NOT consumed by a LINQ operator: ordinary use, no moved-allocation
    // caveat (the local-function rewrite genuinely removes the allocation here).
    public static int CapturingLambdaConsumedByNonLinq(int threshold)
    {
        Func<int, bool> predicate = x => x > threshold;
        return predicate(5) ? 1 : 0;
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

    public Func<int> UserTypeNameContainsDisplayClass()
    {
        return _displayClassTarget.GetValue;
    }

    // --- Boxing (box-value-type) ---

    // Boxes an int into an object-typed API argument -> escapes via call.
    public static string BoxesIntoStringFormat(int value)
    {
        return string.Format("v={0}", value);
    }

    // Boxes a value type per iteration into a non-generic collection -> in a loop (high).
    public static void BoxesInLoop(int[] values, System.Collections.ArrayList sink)
    {
        foreach (int v in values)
        {
            sink.Add(v);
        }
    }

    // External well-known value-type box recall (#1623 rung 3). Boxing a referenced
    // (TypeReference) framework struct is recognized only via IsWellKnownValueType's
    // curated (namespace, name) set, since the SRM-direct product cannot resolve the
    // external type definition. Canary representative members of that set so dropping
    // one is not silent; the boxed value escapes via the return.
    public static object BoxesGuidValue(System.Guid value) => value;

    public static object BoxesDateTimeValue(System.DateTime value) => value;

    public static class UserCopyLookalikes
    {
        public static string Join(int a, int b) => $"{a}:{b}";
        public static string Concat(int a, int b) => $"{a}{b}";
        public static string Substring(string value) => value;
    }

    public class GenericOptimizationOpportunityFixtures<T>
    {
        public Func<T, T> NonCapturingLambda()
        {
            return value => value;
        }
    }

    public sealed class UserDisplayClassTarget
    {
        public int GetValue() => 42;
    }

    // Boxes a value into an exception message on a throw path inside a loop -> the box only
    // allocates when throwing, so it is NOT hot pay-dirt: reported, but medium (not loop-promoted).
    public static void BoxesIntoThrowMessage(int code, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (code == i)
            {
                throw new System.InvalidOperationException(string.Format("dup {0}", code));
            }
        }
    }

    // Boxes a generic parameter (compiler-mandated) -> NOT reported.
    public static object BoxesGenericParameter<T>(T value) where T : struct
    {
        return value;
    }

    // Boxing a Nullable<T> allocates only when non-null -> conservatively NOT reported.
    public static object? BoxesNullable(int? value)
    {
        return value;
    }

    // Boxing an in-assembly struct that escapes into an object-typed API -> reported
    // (value-typeness resolved authoritatively via the struct's System.ValueType base).
    public static void BoxesInAssemblyStruct(BoxFixtureStruct value, System.Collections.ArrayList sink)
    {
        sink.Add(value);
    }

    // BitConverter.GetBytes allocates a transient byte[] -> temporary-byte-array-copy.
    public static byte FirstValueByte(int value)
    {
        return System.BitConverter.GetBytes(value)[0];
    }

    // Boxing a constructed in-assembly generic STRUCT (a TypeSpec) -> reported: value-type-ness
    // comes from the TypeSpec signature blob (ELEMENT_TYPE_VALUETYPE), not the well-known list.
    public static object BoxesGenericStruct(BoxFixtureStruct<int> value)
    {
        return value;
    }

    // >= threshold heap allocations, none in a loop -> a medium allocation-hotspot row.
    public static object AllocatesManyObjects()
    {
        var items = new System.Collections.Generic.List<object>();
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        items.Add(new object());
        return items;
    }

    // >= threshold heap allocations inside a loop, matching no specific shape -> a medium
    // allocation-hotspot (repeated, dense, not otherwise pinpointed).
    public static object AllocatesManyObjectsInLoop(int n)
    {
        var items = new System.Collections.Generic.List<object>();
        for (int i = 0; i < n; i++)
        {
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
        }
        return items;
    }

    // Dense allocations inside a loop, but the method ALSO allocates a capturing delegate (a
    // specific shape). The hotspot row is deduped away so it doesn't double-count the
    // already-pinpointed method.
    public static object AllocatesDenselyInLoopWithDelegate(int n)
    {
        var items = new System.Collections.Generic.List<object>();
        System.Func<int> captured = () => n + 1;
        items.Add(captured());
        for (int i = 0; i < n; i++)
        {
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
            items.Add(new object()); items.Add(new object()); items.Add(new object());
        }
        return items;
    }

    // Many exception constructors on mutually-exclusive throw paths -> NOT a hotspot: these
    // allocate only when throwing, not in steady state, so they are excluded from the count.
    public static void ManyThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new System.InvalidOperationException("0");
            case 1: throw new System.ArgumentException("1");
            case 2: throw new System.ArgumentNullException("2");
            case 3: throw new System.NotSupportedException("3");
            case 4: throw new System.FormatException("4");
            case 5: throw new System.OverflowException("5");
            case 6: throw new System.InvalidCastException("6");
            case 7: throw new System.TimeoutException("7");
            case 8: throw new System.NotImplementedException("8");
        }
    }

    public static int AllocatesManyPseudoExceptions()
    {
        var a0 = new PseudoException(0);
        var a1 = new PseudoException(1);
        var a2 = new PseudoException(2);
        var a3 = new PseudoException(3);
        var a4 = new PseudoException(4);
        var a5 = new PseudoException(5);
        var a6 = new PseudoException(6);
        var a7 = new PseudoException(7);
        return a0.Value + a1.Value + a2.Value + a3.Value + a4.Value + a5.Value + a6.Value + a7.Value;
    }

    // Pseudo-exceptions (non-Exception types named like exceptions) are real steady-state
    // allocations, so a loop densely constructing them is a hotspot (confirms they are not
    // wrongly excluded as exception construction).
    public static int AllocatesManyPseudoExceptionsInLoop(int n)
    {
        var total = 0;
        for (int i = 0; i < n; i++)
        {
            total += new PseudoException(0).Value + new PseudoException(1).Value + new PseudoException(2).Value
                + new PseudoException(3).Value + new PseudoException(4).Value + new PseudoException(5).Value
                + new PseudoException(6).Value + new PseudoException(7).Value + new PseudoException(8).Value
                + new PseudoException(9).Value + new PseudoException(10).Value + new PseudoException(11).Value
                + new PseudoException(12).Value + new PseudoException(13).Value + new PseudoException(14).Value
                + new PseudoException(15).Value + new PseudoException(16).Value + new PseudoException(17).Value;
        }
        return total;
    }

    // Densely constructs VALUE types in a loop (Nullable + an in-assembly struct). None of
    // these `newobj` allocate on the heap, so the method must NOT be an allocation-hotspot
    // even though it clears the >= 16 newobj count.
    public static int ConstructsManyValueTypesInLoop(int n)
    {
        var total = 0;
        for (int i = 0; i < n; i++)
        {
            System.Nullable<int> n0 = new System.Nullable<int>(0); System.Nullable<int> n1 = new System.Nullable<int>(1);
            System.Nullable<int> n2 = new System.Nullable<int>(2); System.Nullable<int> n3 = new System.Nullable<int>(3);
            System.Nullable<int> n4 = new System.Nullable<int>(4); System.Nullable<int> n5 = new System.Nullable<int>(5);
            System.Nullable<int> n6 = new System.Nullable<int>(6); System.Nullable<int> n7 = new System.Nullable<int>(7);
            System.Nullable<int> n8 = new System.Nullable<int>(8); System.Nullable<int> n9 = new System.Nullable<int>(9);
            var p0 = new PlainValue(0); var p1 = new PlainValue(1); var p2 = new PlainValue(2);
            var p3 = new PlainValue(3); var p4 = new PlainValue(4); var p5 = new PlainValue(5);
            var p6 = new PlainValue(6); var p7 = new PlainValue(7);
            total += (n0 ?? 0) + (n1 ?? 0) + (n2 ?? 0) + (n3 ?? 0) + (n4 ?? 0) + (n5 ?? 0) + (n6 ?? 0)
                + (n7 ?? 0) + (n8 ?? 0) + (n9 ?? 0) + p0.V + p1.V + p2.V + p3.V + p4.V + p5.V + p6.V + p7.V;
        }
        return total;
    }

    public static int AllocatesManyPlainObjects()
    {
        var a0 = new PlainObject(0);
        var a1 = new PlainObject(1);
        var a2 = new PlainObject(2);
        var a3 = new PlainObject(3);
        var a4 = new PlainObject(4);
        var a5 = new PlainObject(5);
        var a6 = new PlainObject(6);
        var a7 = new PlainObject(7);
        return a0.Value + a1.Value + a2.Value + a3.Value + a4.Value + a5.Value + a6.Value + a7.Value;
    }

    public static void ManyCustomThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new FixtureException("0");
            case 1: throw new FixtureException("1");
            case 2: throw new FixtureException("2");
            case 3: throw new FixtureException("3");
            case 4: throw new FixtureException("4");
            case 5: throw new FixtureException("5");
            case 6: throw new FixtureException("6");
            case 7: throw new FixtureException("7");
        }
    }

    public static void ManyDerivedCustomThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new DerivedFixtureException("0");
            case 1: throw new DerivedFixtureException("1");
            case 2: throw new DerivedFixtureException("2");
            case 3: throw new DerivedFixtureException("3");
            case 4: throw new DerivedFixtureException("4");
            case 5: throw new DerivedFixtureException("5");
            case 6: throw new DerivedFixtureException("6");
            case 7: throw new DerivedFixtureException("7");
        }
    }

    public static void ManyCrossAssemblyCustomThrowArms(int code)
    {
        switch (code)
        {
            case 0: throw new CrossAssemblyDerivedFixtureException("0");
            case 1: throw new CrossAssemblyDerivedFixtureException("1");
            case 2: throw new CrossAssemblyDerivedFixtureException("2");
            case 3: throw new CrossAssemblyDerivedFixtureException("3");
            case 4: throw new CrossAssemblyDerivedFixtureException("4");
            case 5: throw new CrossAssemblyDerivedFixtureException("5");
            case 6: throw new CrossAssemblyDerivedFixtureException("6");
            case 7: throw new CrossAssemblyDerivedFixtureException("7");
        }
    }

    // A single steady-state allocation inside a loop, below the hotspot threshold and
    // matching no specific shape, so it surfaces NO opportunity row. It still allocates
    // in a loop, so MethodSignals.AllocInLoop must be true (the loop bit is independent
    // of the opportunity/threshold machinery).
    public static object AllocatesOnceInLoop(int n)
    {
        object last = new object();
        for (int i = 0; i < n; i++)
        {
            last = new object();
        }
        return last;
    }

    public static object AllocatesPseudoExceptionOnceInLoop(int n)
    {
        object last = new PseudoException(0);
        for (int i = 0; i < n; i++)
        {
            last = new PseudoException(i);
        }
        return last;
    }

    // Exception construction inside a loop only allocates on the throw path, so it must
    // not set AllocInLoop (error-path allocation is not steady-state hot pay-dirt).
    public static void ThrowsInLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i < 0)
                throw new System.InvalidOperationException("never");
        }
    }

    public static void ThrowsInitializedExceptionInLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i < 0)
                throw new System.InvalidOperationException("never") { HelpLink = "https://example.invalid" };
        }
    }

    public static void ThrowsConditionalExceptionInLoop(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (i < 0)
                throw i == -1
                    ? new System.InvalidOperationException("never")
                    : new System.ArgumentException("never");
        }
    }

    // A real exception object retained (stored) per iteration is a steady-state hot
    // allocation, not a throw-path one, so MethodSignals.AllocInLoop must be true (#1610).
    public static System.Exception[] RetainsExceptionsInLoop(int count)
    {
        var errors = new System.Exception[count];
        for (int i = 0; i < count; i++)
        {
            errors[i] = new System.InvalidOperationException(i.ToString());
        }
        return errors;
    }
}

public sealed class PseudoException
{
    public PseudoException(int value) => Value = value;

    public int Value { get; }
}

public sealed class PlainObject
{
    public PlainObject(int value) => Value = value;

    public int Value { get; }
}

// An in-assembly value type: `new PlainValue(...)` does not allocate on the heap, so it must
// not count toward allocation-hotspot density.
public struct PlainValue
{
    public PlainValue(int v) => V = v;

    public int V { get; }
}

public sealed class FixtureException : Exception
{
    public FixtureException(string message) : base(message)
    {
    }
}

public sealed class DerivedFixtureException : InvalidOperationException
{
    public DerivedFixtureException(string message) : base(message)
    {
    }
}

public sealed class CrossAssemblyDerivedFixtureException : ExceptionBaseFixtures.ExternalFixtureException
{
    public CrossAssemblyDerivedFixtureException(string message) : base(message)
    {
    }
}

public struct BoxFixtureStruct
{
    public int X;
}

public struct BoxFixtureStruct<T>
{
    public T Value;
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
    public void ConstructedExceptionTypes_ExcludesInAssemblyNonExceptionLookalike()
    {
        // #1572: an in-assembly type named *Exception that does not derive from
        // System.Exception is a heap allocation but not a constructed exception.
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.ConstructsLookalikeException)), maxDepth: 1, maxNodes: 100);
        var signals = tree.Perf?.SignalsOrNone;

        Assert.True(signals?.Allocations >= 1, "the lookalike newobj is still an allocation");
        Assert.DoesNotContain(nameof(PseudoException), signals!.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_IncludesInAssemblyCustomException()
    {
        // #1572: an in-assembly type that derives from System.Exception still reports.
        var tree = Index.BuildCallTree(Token(nameof(CallTreeFixtures.ConstructsCustomException)), maxDepth: 1, maxNodes: 100);
        var signals = tree.Perf?.SignalsOrNone;

        Assert.Contains(nameof(CustomDomainException), signals!.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_ExternalSuffixLookalike_PinsSuffixFallbackBoundary()
    {
        // #1623 rung 2 boundary pin. The product is SRM-direct and does not load the
        // referenced assembly, so it cannot prove that ExternalPseudoException does NOT
        // derive from System.Exception. The documented fallback is the "*Exception"
        // name suffix, so this cross-assembly non-exception lookalike IS counted. This
        // is a deliberately-owned false positive at the no-referenced-assembly-loading
        // boundary (Invariant 1) — pinned so the heuristic is explicit, not silent. If
        // referenced-assembly base resolution is ever added, this assertion flips to
        // DoesNotContain.
        var signals = SignalsForMethod(nameof(CallTreeFixtures.ConstructsExternalSuffixLookalike));

        Assert.True(signals.Allocations >= 1, "the cross-assembly newobj is still an allocation");
        Assert.Contains(nameof(ExceptionBaseFixtures.ExternalPseudoException), signals.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_ExternalUnsuffixedException_PinsSuffixFallbackBoundary()
    {
        // #1623 rung 2 boundary pin (the dual of the test above). ExternalErrorState
        // really derives from System.Exception, but its name does not end with
        // "Exception" and its base chain is in a referenced assembly the SRM-direct
        // product does not load, so the suffix fallback misses it. This is a
        // deliberately-owned false negative at the same boundary — the allocation is
        // still counted, only the exception-type classification degrades.
        var signals = SignalsForMethod(nameof(CallTreeFixtures.ConstructsExternalUnsuffixedException));

        Assert.True(signals.Allocations >= 1, "the cross-assembly newobj is still an allocation");
        Assert.DoesNotContain(nameof(ExceptionBaseFixtures.ExternalErrorState), signals.ExceptionTypes);
    }

    static MethodSignals SignalsForMethod(string methodName)
    {
        int token = Index.Methods.First(method => method.Name == methodName).MetadataToken;
        return Index.BuildCallTree(token, maxDepth: 1, maxNodes: 100).Perf!.SignalsOrNone;
    }

    [Fact]
    public void ConstructedExceptionTypes_ExcludesNestedInAssemblyLookalike()
    {
        // #1572 review: a nested in-assembly type decodes as "ExceptionHost+Nested...",
        // so the in-assembly map must key by the decoded name or the lookalike leaks
        // back through the suffix fallback.
        var signals = SignalsForMethod(nameof(ExceptionHost.MakesNestedLookalike));

        Assert.True(signals.Allocations >= 1);
        Assert.DoesNotContain(signals.ExceptionTypes, name => name.Contains("Pseudo", StringComparison.Ordinal));
    }

    [Fact]
    public void ConstructedExceptionTypes_IncludesNestedInAssemblyException()
    {
        var signals = SignalsForMethod(nameof(ExceptionHost.MakesNestedReal));

        Assert.Contains(signals.ExceptionTypes, name => name.Contains(nameof(ExceptionHost.NestedRealException), StringComparison.Ordinal));
    }

    [Fact]
    public void ConstructedExceptionTypes_IncludesExceptionWithClosedGenericBase()
    {
        // The base is a closed generic (TypeSpecification) we do not resolve, so the
        // type is left to the conservative name-suffix fallback — and its name ends
        // with "Exception", so it still reports.
        var signals = SignalsForMethod(nameof(GenericExceptionFixtures.MakesGenericBaseDerived));

        Assert.Contains(nameof(ClosedGenericDerivedException), signals.ExceptionTypes);
    }

    [Fact]
    public void ConstructedExceptionTypes_RecordsGenericExceptionDefinitionName()
    {
        // A constructed generic exception is a GenericInstance with an empty Name; the
        // recorded entry must be the underlying definition name, not a blank string.
        var signals = SignalsForMethod(nameof(GenericExceptionFixtures.MakesDirectGeneric));

        Assert.DoesNotContain("", signals.ExceptionTypes);
        Assert.Contains(signals.ExceptionTypes, name => name.StartsWith("DirectGenericException", StringComparison.Ordinal));
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

public static class GenericDeclaringCallers
{
    public static int CallGenericTarget() => GenericDeclaringTarget<int>.Target();

    public static int CallGenericTargetWithParameter() => GenericDeclaringTarget<int>.TargetWithParameter(41);

    public static unsafe int CallUnsafeGenericTarget(int* value) => GenericUnsafeTarget<int>.UnsafeTarget(value);
}

public static class GenericDeclaringTarget<T>
{
    public static int Target() => 42;

    public static T TargetWithParameter(T value) => value;
}

public static class GenericUnsafeTarget<T>
{
    public static unsafe int UnsafeTarget(int* value) => *value;
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

    // Constructs an in-assembly type whose simple name ends with "Exception" but which
    // does NOT derive from System.Exception (#1572). It is a heap allocation but must
    // not be reported as a constructed exception.
    public static object ConstructsLookalikeException() => new PseudoException(0);

    // Constructs an in-assembly type that actually derives from System.Exception; it
    // must still be reported as a constructed exception.
    public static object ConstructsCustomException() => new CustomDomainException();

    // Cross-assembly boundary pins (#1623 rung 2). These construct types defined in a
    // referenced assembly whose base chain the SRM-direct product cannot walk, so
    // exception classification falls back to the documented "*Exception" suffix.
    public static object ConstructsExternalSuffixLookalike()
        => new ExceptionBaseFixtures.ExternalPseudoException();

    public static object ConstructsExternalUnsuffixedException()
        => new ExceptionBaseFixtures.ExternalErrorState("external");
}

// Reflection-signal recall fixtures (#1623 rung 3 / #1715). One canary per
// IsReflectionApi branch so a regression removing any branch fails recall:
//   - System.Reflection.* namespace prefix (MethodInfo.Invoke, Assembly.GetTypes)
//   - System.Linq.Expressions namespace
//   - System.Type curated member set, beyond the GetMethods covered by Reflects
// System.Activator is already covered by CallTreeFixtures.Reflects.
public static class ReflectionRecallFixtures
{
    public static object? InvokesViaReflection(System.Reflection.MethodInfo method, object target)
        => method.Invoke(target, null);

    public static System.Type[] EnumeratesAssemblyTypes(System.Reflection.Assembly assembly)
        => assembly.GetTypes();

    public static System.Linq.Expressions.ConstantExpression BuildsExpression()
        => System.Linq.Expressions.Expression.Constant(42);

    // Four distinct System.Type curated-set members -> Reflection count 4.
    public static int CallsTypeMemberSet(System.Type type)
    {
        int found = 0;
        if (type.GetProperty("Length") is not null) found++;
        if (type.GetField("value") is not null) found++;
        if (type.GetConstructor(System.Type.EmptyTypes) is not null) found++;
        if (type.MakeArrayType() is not null) found++;
        return found;
    }
}

// An in-assembly custom exception that derives from System.Exception.
public sealed class CustomDomainException : Exception;

// Nested in-assembly types: the call index keys these as "ExceptionHost+Nested*",
// so the in-assembly map must use the same decoded name to match (#1572 review).
public static class ExceptionHost
{
    public sealed class NestedPseudoException;
    public sealed class NestedRealException : Exception;

    public static object MakesNestedLookalike() => new NestedPseudoException();
    public static object MakesNestedReal() => new NestedRealException();
}

// Generic exception shapes: a type whose base is a closed generic (TypeSpecification)
// exception, and a directly-constructed generic exception instance.
public class GenericExceptionBase<T> : Exception;
public sealed class ClosedGenericDerivedException : GenericExceptionBase<int>;
public sealed class DirectGenericException<T> : Exception;

public static class GenericExceptionFixtures
{
    public static object MakesGenericBaseDerived() => new ClosedGenericDerivedException();
    public static object MakesDirectGeneric() => new DirectGenericException<int>();
}

public static partial class UnsafeEvidenceFixtures
{
    public static unsafe int UnsafePointerRead(int* value) => *value;

    public static uint CallsUnsafeAs(ref int value) => Unsafe.As<int, uint>(ref value);

    [DllImport("kernel32.dll")]
    public static extern int PInvokeOnly();

    [DllImport("kernel32.dll")]
    public static unsafe extern int PointerExtern(int* value);

    public static unsafe int PointerExternCallerA(int* value) => PointerExtern(value);

    public static unsafe int PointerExternCallerB(int* value) => PointerExtern(value);
}

// Two independent containing types that each nest a type with the same simple name
// `Dup`. Their nested declaring-type identity must stay distinct (NestedLeft.Dup vs
// NestedRight.Dup); collapsing to the innermost `Dup` merges the two Target methods
// in graph keys / FindCalls (#1554).
public static class NestedLeft
{
    public static class Dup
    {
        public static void Target() { }
    }

    public static void Call() => Dup.Target();
}

public static class NestedRight
{
    public static class Dup
    {
        public static void Target() { }
    }

    public static void Call() => Dup.Target();
}

// A nested type under a generic outer type. The open-definition declaring identity is
// `GenericOuter`1+Inner`; its display must preserve the path and strip per-segment
// arity -> `GenericOuter.Inner`.
public static class GenericOuter<T>
{
    public static class Inner
    {
        public static void Leaf() { }
    }

    public static void Use() => Inner.Leaf();
}

// Three overloads sharing the simple name `M` but differing by signature. Analysis
// identity keys must keep them distinct (FindCalls, Call/Caller Graph, leverage),
// or calls to one overload are mis-credited to another (#1623 rung 1).
public static class OverloadTargets
{
    public static void M() { }

    public static void M(int value) { }

    public static void M(string value) { }
}

public static class OverloadCallers
{
    public static void CallsEachOverloadOnce()
    {
        OverloadTargets.M();
        OverloadTargets.M(1);
        OverloadTargets.M("x");
    }
}
