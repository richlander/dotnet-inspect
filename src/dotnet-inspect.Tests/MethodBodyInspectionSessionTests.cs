using System.Collections.Immutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Fixtures;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests the command-policy and cross-assembly composition owned by
/// <see cref="MethodBodyInspectionSession"/>. Neutral query behavior is covered
/// by its owning Analysis tests or end-to-end CLI section tests.
/// </summary>
public class MethodBodyInspectionSessionTests
{
    readonly unsafe struct FunctionPointerConversionFixture
    {
        public static explicit operator
            delegate* unmanaged[Cdecl]<int, int>(
                FunctionPointerConversionFixture value) =>
            null;

        public static explicit operator
            delegate* unmanaged[Stdcall]<int, int>(
                FunctionPointerConversionFixture value) =>
            null;

        public static delegate* unmanaged[Cdecl]<int, int> CallCdecl(
            FunctionPointerConversionFixture value) =>
            (delegate* unmanaged[Cdecl]<int, int>)value;

        public static delegate* unmanaged[Stdcall]<int, int> CallStdcall(
            FunctionPointerConversionFixture value) =>
            (delegate* unmanaged[Stdcall]<int, int>)value;
    }

    static string ProductPath => typeof(MethodBodyInspectionSession).Assembly.Location;
    static string TestPath => typeof(MethodBodyInspectionSessionTests).Assembly.Location;
    static readonly ImmutableArray<MetadataReference> PlatformReferences =
    [
        .. ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path =>
                (MetadataReference)MetadataReference.CreateFromFile(
                    path)),
    ];

    static int CalledToken(Analysis.LibraryBodyIndex index)
    {
        var methodTokens = index.Methods.Select(m => m.MetadataToken).ToHashSet();
        return index.DirectCalls
            .Select(call => call.CalleeDefinitionToken)
            .First(token => methodTokens.Contains(token));
    }

    [Fact]
    public void Open_ExposesConfiguredNeutralIndex()
    {
        var session = MethodBodyInspectionSession.Open(
            ProductPath,
            includeAllocations: false,
            includeOpportunities: false);

        Assert.NotEmpty(session.BodyIndex.Methods);
        Assert.Empty(session.BodyIndex.GetAllocationOccurrences());
        Assert.Empty(session.BodyIndex.OptimizationOpportunities);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void Open_HonorsBodyScope()
    {
        var fullIndex = Analysis.LibraryBodyIndex.Open(ProductPath);
        var token = fullIndex.GetDirectCallsByCaller().First(entry => entry.Value.Length > 0).Key;

        var scopedIndex = MethodBodyInspectionSession.Open(
            ProductPath,
            includeAllocations: false,
            includeOpportunities: false,
            bodyScope: new HashSet<int> { token }).BodyIndex;

        Assert.NotEmpty(scopedIndex.DirectCalls);
        Assert.All(scopedIndex.DirectCalls, call => Assert.Equal(token, call.Caller.MetadataToken));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void SourceName_MatchesAssemblyFileName()
    {
        var expected = Path.GetFileNameWithoutExtension(ProductPath);
        Assert.Equal(expected, MethodBodyInspectionSession.Open(ProductPath).SourceName);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_MatchSameAssemblyCalls_AndAttributeSource()
    {
        var index = Analysis.LibraryBodyIndex.Open(ProductPath);
        var targetToken = CalledToken(index);
        var identity = index.Methods.First(method => method.MetadataToken == targetToken);
        var pattern = Analysis.MemberPattern.Method(identity);
        var expected = index.DirectCalls
            .Where(call => call.CalleeDefinitionToken == targetToken || pattern.Matches(call.Callee))
            .ToList();

        var session = MethodBodyInspectionSession.Open(ProductPath);
        var actual = session.CallerEdges(targetToken);

        Assert.NotEmpty(actual);
        Assert.Equal(expected.Count, actual.Length);
        Assert.All(actual, edge => Assert.Equal(session.SourceName, edge.Source));
        Assert.Equal(
            expected.Select(call => (call.Caller.MetadataToken, call.ILOffset)).OrderBy(value => value),
            actual.Select(edge => (edge.Call.Caller.MetadataToken, edge.Call.ILOffset)).OrderBy(value => value));
    }

    [Fact]
    public void CallerEdges_ConversionSelectionExcludesSiblingReturnTypes()
    {
        var session = MethodBodyInspectionSession.Open(
            typeof(object).Assembly.Location);
        Analysis.TypeRef decimalType =
            Analysis.TypeRef.CoreLib("System", "Decimal");
        Analysis.TypeRef intType =
            Analysis.TypeRef.CoreLib("System", "Int32");
        Analysis.MethodIdentity target =
            session.BodyIndex.DeclaredMethods.Single(method =>
                method.DeclaringType.Equals(decimalType)
                && method.Name == "op_Explicit"
                && method.ParameterTypes.SequenceEqual([decimalType])
                && method.ReturnType.Equals(intType));
        Analysis.DirectCall sibling =
            session.BodyIndex.DirectCalls.First(call =>
                call.Callee.DeclaringType.Equals(decimalType)
                && call.Callee.Name == target.Name
                && call.Callee.ParameterTypes.SequenceEqual(
                    target.ParameterTypes)
                && !call.Callee.ReturnType.Equals(target.ReturnType));

        ImmutableArray<CallerEdge> actual =
            session.CallerEdges(target.MetadataToken);

        Assert.NotEmpty(actual);
        Assert.DoesNotContain(
            actual,
            edge => edge.Call == sibling);
        Assert.All(
            actual,
            edge => Assert.Equal(
                target.ReturnType,
                edge.Call.Callee.OpenSignatureReturn));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_ConversionSelectionRetainsFunctionPointerShape()
    {
        var session = MethodBodyInspectionSession.Open(TestPath);
        Analysis.DirectCall cdeclCall =
            session.BodyIndex.DirectCalls.Single(call =>
                call.Caller.Name
                    == nameof(
                        FunctionPointerConversionFixture.CallCdecl)
                && call.Callee.Name == "op_Explicit");
        Analysis.DirectCall stdcallCall =
            session.BodyIndex.DirectCalls.Single(call =>
                call.Caller.Name
                    == nameof(
                        FunctionPointerConversionFixture.CallStdcall)
                && call.Callee.Name == "op_Explicit");
        Analysis.MethodIdentity cdecl =
            session.BodyIndex.DeclaredMethods.Single(method =>
                method.MetadataToken
                    == cdeclCall.CalleeDefinitionToken);
        Analysis.MethodIdentity stdcall =
            session.BodyIndex.DeclaredMethods.Single(method =>
                method.MetadataToken
                    == stdcallCall.CalleeDefinitionToken);

        ImmutableArray<CallerEdge> actual =
            session.CallerEdges(cdecl.MetadataToken);

        Assert.Contains(
            actual,
            edge => edge.Call == cdeclCall);
        Assert.DoesNotContain(
            actual,
            edge => edge.Call == stdcallCall);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_IncludeAndAttributeCallerScopeAssemblies()
    {
        var target = MethodBodyInspectionSession.Open(
            ProductPath,
            ApiAnalysisInspection.CreateReferenceResolver(ProductPath));
        var openMethod = target.BodyIndex.Methods.Single(method =>
            method.DeclaringType.Name == nameof(MethodBodyInspectionSession)
            && method.Name == nameof(MethodBodyInspectionSession.Open)
            && method.ParameterTypes.Length > 0
            && method.ParameterTypes[0].Name == "String");
        var scope = MethodBodyInspectionSession.Open(
            TestPath,
            ApiAnalysisInspection.CreateReferenceResolver(TestPath));

        var actual = target.CallerEdges(
            openMethod.MetadataToken,
            [scope]);

        Assert.Contains(actual, edge => edge.Source == scope.SourceName);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_UnknownToken_ReturnsEmpty()
        => Assert.Empty(MethodBodyInspectionSession.Open(ProductPath).CallerEdges(targetToken: 0));

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerTree_SessionScopes_MatchesNeutralIndexComposition()
    {
        var index = Analysis.LibraryBodyIndex.Open(ProductPath);
        var token = CalledToken(index);

        MethodBodyInspectionSession target =
            MethodBodyInspectionSession.Open(ProductPath);
        MethodBodyInspectionSession caller =
            MethodBodyInspectionSession.Open(TestPath);
        using Analysis.CatalogCallGraphScope scope =
            MethodBodyInspectionSession.CreateCallGraphScope(
                [target, caller]);
        var expected = target.BodyIndex.BuildCallerTree(token, scope);
        var actual = target.CallerTree(token, [caller]);

        Assert.Equal(expected.Children.Count(), actual.Children.Count());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerTree_SessionScopes_ProjectsWithUnscopedInstanceCallees()
    {
        MethodBodyInspectionSession target =
            MethodBodyInspectionSession.Open(ProductPath);
        MethodBodyInspectionSession caller =
            MethodBodyInspectionSession.Open(TestPath);
        Analysis.MethodIdentity method =
            target.BodyIndex.DeclaredMethods.Single(candidate =>
                candidate.DeclaringType.Name
                    == nameof(MethodBodyInspectionSession)
                && candidate.Name
                    == nameof(MethodBodyInspectionSession.CallerEdges)
                && candidate.ParameterTypes.Length == 3);

        Analysis.CallTreeNode callerRoot =
            target.CallerTree(method.MetadataToken, [caller]);
        Analysis.CallTreeNode calleeRoot =
            target.BodyIndex.BuildCallTree(method.MetadataToken);
        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);

        Assert.Equal(method.Name, projection.Focus.Member.Name);
        Assert.True(projection.Focus.Member.HasThis);
    }

    [Fact]
    public void CallGraph_SessionScopes_TraversesCalleesAcrossAssemblies()
    {
        string callerPath =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        MethodBodyInspectionSession caller =
            MethodBodyInspectionSession.Open(
                callerPath,
                ApiAnalysisInspection.CreateReferenceResolver(callerPath));
        string targetPath =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        MethodBodyInspectionSession target =
            MethodBodyInspectionSession.Open(
                targetPath,
                ApiAnalysisInspection.CreateReferenceResolver(targetPath));
        Analysis.MethodIdentity root =
            caller.BodyIndex.DeclaredMethods.Single(method =>
                method.DeclaringType.Name == "Entry"
                && method.Name == "RunAcrossBoundary");

        CallGraphProjection projection = caller.CallGraph(
            root.MetadataToken,
            callerScopes: [],
            calleeScopes: [target],
            out Analysis.CatalogCallGraphDiagnostics diagnostics);

        Assert.False(diagnostics.IsIncomplete);
        Assert.Contains(
            projection.Nodes,
            node => node.Member.Name == "Forward"
                && node.Kind == CallGraphNodeKind.Normal);
        Assert.Contains(
            projection.Nodes,
            node => node.Member.Name == "Leaf"
                && node.Kind == CallGraphNodeKind.Normal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallGraph_IndependentScopesReconcileExactBindingIdentity()
    {
        string analysisPath =
            typeof(Analysis.CatalogMemberCorrespondencePlan)
                .Assembly.Location;
        var resolver = new CoreLibraryOnlyResolver();
        MethodBodyInspectionSession target =
            MethodBodyInspectionSession.Open(
                analysisPath,
                resolver);
        MethodBodyInspectionSession caller =
            MethodBodyInspectionSession.Open(
                TestPath,
                resolver);
        Analysis.MethodIdentity typeIdentity =
            target.BodyIndex.DeclaredMethods.Single(method =>
                method.DeclaringType.Name
                    == nameof(Analysis.CallGraphMemberResolver)
                && method.Name == "TypeIdentity");

        CallGraphProjection projection = target.CallGraph(
            typeIdentity.MetadataToken,
            callerScopes: [caller],
            calleeScopes: [],
            out Analysis.CatalogCallGraphDiagnostics diagnostics);

        Assert.NotEmpty(projection.CallSites);
        Assert.Equal(0, diagnostics.BindingIdentityConflictCount);
        Assert.Equal(
            projection.CallSites.Length,
            projection.CallSites
                .Select(site =>
                    (
                        site.Call.EvidenceMethod.AssemblyName,
                        site.Call.EvidenceMethod.ModuleVersionId,
                        site.Call.EvidenceMethod.MetadataToken,
                        site.Call.ILOffset,
                        site.Call.OperandToken))
                .Distinct()
                .Count());
    }

    [Fact]
    public void CallGraph_KeepsReachableVersionSkewedDefinitionDistinct()
    {
        string directory = Directory.CreateTempSubdirectory(
            "callgraph-version-skew-").FullName;

        try
        {
            byte[] v1Image = CompileFixture(
                "VersionedA",
                """
                using System.Reflection;
                [assembly: AssemblyVersion("1.0.0.0")]

                namespace Versioned;

                public static class Api
                {
                    public static void Root() { }
                }
                """);
            byte[] bridgeImage = CompileFixture(
                "Bridge",
                """
                namespace BridgeNs;

                public static class HopApi
                {
                    public static void Hop() =>
                        Versioned.Api.Root();
                }
                """,
                v1Image);
            byte[] v2Image = CompileFixture(
                "VersionedA",
                """
                using System.Reflection;
                [assembly: AssemblyVersion("2.0.0.0")]

                namespace Versioned;

                public static class Api
                {
                    public static void Root() =>
                        BridgeNs.HopApi.Hop();
                }
                """,
                bridgeImage);

            string v1Path = WriteFixture(
                directory,
                "v1",
                "VersionedA.dll",
                v1Image);
            string bridgePath = WriteFixture(
                directory,
                "bridge",
                "Bridge.dll",
                bridgeImage);
            string v2Path = WriteFixture(
                directory,
                "v2",
                "VersionedA.dll",
                v2Image);

            MethodBodyInspectionSession v2 = OpenFixture(v2Path);
            MethodBodyInspectionSession bridge =
                OpenFixture(bridgePath);
            MethodBodyInspectionSession v1 = OpenFixture(v1Path);
            Analysis.MethodIdentity root =
                v2.BodyIndex.DeclaredMethods.Single(method =>
                    method.DeclaringType.Name == "Api"
                    && method.Name == "Root");

            CallGraphProjection projection = v2.CallGraph(
                root.MetadataToken,
                callerScopes: [],
                calleeScopes: [bridge, v1],
                out Analysis.CatalogCallGraphDiagnostics diagnostics);

            CallGraphNode[] roots =
            [
                .. projection.Nodes.Where(node =>
                    node.Member.DeclaringType.Name == "Api"
                    && node.Member.Name == "Root"),
            ];
            CallGraphNode bridgeNode = Assert.Single(
                projection.Nodes,
                node => node.Member.Name == "Hop");
            Assert.Equal(2, roots.Length);
            CallGraphNode v1Node = Assert.Single(
                roots,
                node => node.Id != projection.Focus.Id);
            Assert.Contains(
                projection.Edges,
                edge => edge.From == projection.Focus.Id
                    && edge.To == bridgeNode.Id);
            Assert.Contains(
                projection.Edges,
                edge => edge.From == bridgeNode.Id
                    && edge.To == v1Node.Id);
            Assert.DoesNotContain(
                projection.Edges,
                edge => edge.From == bridgeNode.Id
                    && edge.To == projection.Focus.Id);
            Assert.Equal(
                1,
                diagnostics.BindingIdentityConflictCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CallGraph_KeepsVersionSkewedCallersWhenCalleesAreUnscoped()
    {
        string directory = Directory.CreateTempSubdirectory(
            "callgraph-caller-version-skew-").FullName;

        try
        {
            byte[] targetImage = CompileFixture(
                "TargetLib",
                """
                namespace Target;

                public static class Api
                {
                    public static void Root() { }
                }
                """);
            byte[] callerV1Image = CompileFixture(
                "CallerLib",
                """
                using System.Reflection;
                [assembly: AssemblyVersion("1.0.0.0")]

                namespace Shared;

                public static class Entry
                {
                    public static void Run() =>
                        Target.Api.Root();
                }
                """,
                targetImage);
            byte[] callerV2Image = CompileFixture(
                "CallerLib",
                """
                using System.Reflection;
                [assembly: AssemblyVersion("2.0.0.0")]

                namespace Shared;

                public static class Entry
                {
                    public static void Run() =>
                        Target.Api.Root();
                }
                """,
                targetImage);

            string targetPath = WriteFixture(
                directory,
                "target",
                "TargetLib.dll",
                targetImage);
            string callerV1Path = WriteFixture(
                directory,
                "caller-v1",
                "CallerLib.dll",
                callerV1Image);
            string callerV2Path = WriteFixture(
                directory,
                "caller-v2",
                "CallerLib.dll",
                callerV2Image);

            MethodBodyInspectionSession target =
                OpenFixture(targetPath);
            MethodBodyInspectionSession callerV1 =
                OpenFixture(callerV1Path);
            MethodBodyInspectionSession callerV2 =
                OpenFixture(callerV2Path);
            Analysis.MethodIdentity root =
                target.BodyIndex.DeclaredMethods.Single(method =>
                    method.DeclaringType.Name == "Api"
                    && method.Name == "Root");

            CallGraphProjection projection = target.CallGraph(
                root.MetadataToken,
                callerScopes: [callerV1, callerV2],
                calleeScopes: null,
                out _);

            CallGraphNode[] callers =
            [
                .. projection.Nodes.Where(node =>
                    node.Member.DeclaringType.Name == "Entry"
                    && node.Member.Name == "Run"),
            ];
            Assert.Equal(2, callers.Length);
            Assert.All(
                callers,
                caller => Assert.Contains(
                    projection.Edges,
                    edge => edge.From == caller.Id
                        && edge.To == projection.Focus.Id));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CallerTree_VersionSkewedScopeRetainsIncompleteEvidence()
    {
        string targetV1 =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        MethodBodyInspectionSession target =
            MethodBodyInspectionSession.Open(
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());
        MethodBodyInspectionSession caller =
            MethodBodyInspectionSession.Open(
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
                new DotnetInspector.Services.AssemblyDependencyResolver(
                    new(
                        FixtureCatalog.AnalysisCallerGraphCaller
                            .AssemblyPath())));
        MethodBodyInspectionSession targetV1Session =
            MethodBodyInspectionSession.Open(targetV1);
        Analysis.MethodIdentity ping =
            target.BodyIndex.DeclaredMethods.Single(method =>
                method.DeclaringType.Name == "Api"
                && method.Name == "Ping"
                && method.ParameterTypes.Length == 0);

        Analysis.CallTreeNode tree = target.CallerTree(
            ping.MetadataToken,
            [caller, targetV1Session],
            out Analysis.CatalogCallGraphDiagnostics diagnostics);

        Assert.Empty(tree.Children);
        Assert.True(diagnostics.IsIncomplete);
        Assert.True(diagnostics.BindingIdentityConflictCount > 0);
    }

    static byte[] CompileFixture(
        string assemblyName,
        string source,
        params byte[][] additionalReferences)
    {
        ImmutableArray<MetadataReference> references =
        [
            .. PlatformReferences,
            .. additionalReferences.Select(image =>
                MetadataReference.CreateFromImage(
                    ImmutableArray.CreateRange(image))),
        ];
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview)),
            ],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        return output.ToArray();
    }

    static string WriteFixture(
        string root,
        string directory,
        string fileName,
        byte[] image)
    {
        string path = Path.Combine(
            Directory.CreateDirectory(
                Path.Combine(root, directory)).FullName,
            fileName);
        File.WriteAllBytes(path, image);
        return path;
    }

    static MethodBodyInspectionSession OpenFixture(string path) =>
        MethodBodyInspectionSession.Open(
            path,
            new DotnetInspector.Services.AssemblyDependencyResolver(
                new(
                    path)),
            includeAllocations: false,
            includeOpportunities: false);

    sealed class CoreLibraryOnlyResolver :
        IAssemblyReferenceResolver,
        IAssemblyBindingPolicy
    {
        readonly ResolvedAssemblyReference _coreLibrary =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(object).Assembly.Location,
                AssemblyResolutionProvenance.Platform(
                    "runtime",
                    frameworkVersion: null,
                    "call-graph identity-conflict fixture"));

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope) =>
            null;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                request.Target is AssemblyBindingTarget.IntrinsicCoreLibrary
                ? AssemblyBindingSelection.Found(_coreLibrary)
                : AssemblyBindingSelection.NameNotOwned();
        }
    }
}
