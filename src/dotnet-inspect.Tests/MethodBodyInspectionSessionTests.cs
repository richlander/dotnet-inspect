using System.Collections.Immutable;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Fixtures;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
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
    public void BodyCorrespondence_DistinguishesFunctionPointerCallingConventions()
    {
        ResolvedAssemblyReference tokenOrigin =
            TestAssemblyReferences.Designated(TestPath);
        ResolvedAssemblyReference bodyAssembly =
            TestAssemblyReferences.Designated(TestPath);
        using var originImage =
            AssemblyInspectionSession.Open(tokenOrigin);
        int[] sourceTokens = originImage.MethodBodies.EnumerateMethods()
            .Where(method =>
                method.DeclaringType.EndsWith(
                    nameof(FunctionPointerConversionFixture),
                    StringComparison.Ordinal)
                && method.Name == "op_Explicit")
            .Select(method => method.MetadataToken)
            .ToArray();
        string[] canonicalSignatures = sourceTokens
            .Select(token =>
                originImage.MethodBodies.ResolveMethodAnchor(token)!
                    .CanonicalSignature)
            .ToArray();

        IReadOnlyDictionary<int, int> correspondence =
            ApiBodyMemberCorrespondence.Resolve(
                sourceTokens,
                tokenOrigin,
                bodyAssembly,
                projectAssetsPath: null,
                targetFramework: null,
                platformFramework: null);

        Assert.Equal(2, sourceTokens.Length);
        Assert.Single(canonicalSignatures.Distinct(StringComparer.Ordinal));
        Assert.Equal(2, correspondence.Count);
        Assert.All(
            sourceTokens,
            token => Assert.Equal(token, correspondence[token]));
        Assert.NotSame(
            tokenOrigin.Registration,
            bodyAssembly.Registration);
    }

    [Fact]
    public void MemberAnalysis_CorrespondsMethodIdentityAcrossDistinctAcquisitions()
    {
        byte[] referenceImage = CompileFixture(
            "TokenCorrespondence",
            """
            namespace Sample;
            public static class Outer
            {
                public static class Widget<T>
                {
                    public static T Target(T value) => value;
                    public static T RegionTarget(T value)
                    {
                        try { return value; }
                        finally { System.GC.KeepAlive(value); }
                    }
                    public static int Other() => 0;
                }
            }
            """);
        byte[] runtimeImage = CompileFixture(
            "TokenCorrespondence",
            """
            namespace Sample;
            public static class Outer
            {
                public static class Widget<T>
                {
                    public static int Other() => System.Math.Abs(-1);
                    public static T Target(T value) => value;
                    public static T RegionTarget(T value)
                    {
                        try { return value; }
                        finally { System.GC.KeepAlive(value); }
                    }
                }
            }
            """);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"token-correspondence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string referencePath = WriteFixture(
                root,
                "ref",
                "TokenCorrespondence.dll",
                referenceImage);
            string runtimePath = WriteFixture(
                root,
                "runtime",
                "TokenCorrespondence.dll",
                runtimeImage);
            ResolvedAssemblyReference tokenOrigin =
                TestAssemblyReferences.Designated(referencePath);
            ResolvedAssemblyReference bodyAssembly =
                TestAssemblyReferences.Designated(runtimePath);
            ApiSurface api = Assert.IsType<ApiSurface>(
                AssemblyReader.ExtractApiSurface(tokenOrigin));
            ApiType type = Assert.Single(
                api.Types,
                candidate => candidate.Members.Any(
                    member => member.Name == "Target"));
            ApiMember target = Assert.Single(
                type.Members,
                member => member.Name == "Target");
            ApiMember regionTarget = Assert.Single(
                type.Members,
                member => member.Name == "RegionTarget");
            var options = new MemberOptions
            {
                TokenOriginAssemblyReference = tokenOrigin,
                AssemblyReference = bodyAssembly,
            };
            var inspection = new ApiMemberAnalysisInspection(
                runtimePath,
                [target],
                new HashSet<string> { SectionNames.Calls },
                callerScopeAssemblies: null,
                options);

            int sourceToken = target.MetadataToken!.Value;
            int bodyToken = inspection.ResolveTargetToken(sourceToken);

            Assert.NotEqual(sourceToken, bodyToken);
            Analysis.MethodIdentity analyzed = Assert.Single(
                inspection.BodyIndex.DeclaredMethods,
                method => method.MetadataToken == bodyToken);
            Assert.Equal("Target", analyzed.Name);
            Assert.Equal(bodyToken, analyzed.MetadataToken);
            Assert.Empty(inspection.BodyIndex.DirectCalls);
            Assert.Single(
                ApiAnalysisInspection.ResolveExceptionRegions(
                    runtimePath,
                    options,
                    [regionTarget]));

            IReadOnlyDictionary<int, int> wholeTypeTokens =
                ApiBodyMemberCorrespondence.Resolve(
                    ApiOutputFormatter.ResolveTypeBodyShapeMethodTokens(type),
                    tokenOrigin,
                    bodyAssembly,
                    projectAssetsPath: null,
                    targetFramework: null,
                    platformFramework: null);
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(runtimePath));
            DecompilerResult wholeType = MemberBodyProducer.Project(
                type,
                bodyAssembly,
                resolver,
                bodyTokens: wholeTypeTokens);
            Assert.True(wholeType.Succeeded);
            Assert.Contains(
                "public static T Target(T value) => value;",
                wholeType.Output);
            Assert.Contains(
                "public static int Other() => Math.Abs(-1);",
                wholeType.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BodyCorrespondence_RejectsUnrelatedReferenceAndDefinitionTypes()
    {
        byte[] signatureTypes = CompileFixture(
            "SignatureTypes",
            "namespace Shared; public class Value { }");
        byte[] referenceImage = CompileFixture(
            "NominalTypeCorrespondence",
            """
            namespace Sample;
            public static class Widget<T>
                where T : Shared.Value
            {
                public static U Target<U>(U value)
                    where U : Shared.Value => value;
            }
            """,
            signatureTypes);
        byte[] runtimeImage = CompileFixture(
            "NominalTypeCorrespondence",
            """
            namespace Shared
            {
                public class Value { }
            }
            namespace Sample
            {
                public static class Widget<T>
                    where T : Shared.Value
                {
                    public static int Other() => 0;
                    public static U Target<U>(U value)
                        where U : Shared.Value => value;
                }
            }
            """);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"nominal-type-correspondence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string referencePath = WriteFixture(
                root,
                "ref",
                "NominalTypeCorrespondence.dll",
                referenceImage);
            WriteFixture(
                root,
                "ref",
                "SignatureTypes.dll",
                signatureTypes);
            string runtimePath = WriteFixture(
                root,
                "runtime",
                "NominalTypeCorrespondence.dll",
                runtimeImage);
            ResolvedAssemblyReference tokenOrigin =
                TestAssemblyReferences.Designated(referencePath);
            ResolvedAssemblyReference bodyAssembly =
                TestAssemblyReferences.Designated(runtimePath);
            ApiSurface api = Assert.IsType<ApiSurface>(
                AssemblyReader.ExtractApiSurface(tokenOrigin));
            ApiMember target = Assert.Single(
                Assert.Single(
                    api.Types,
                    candidate => candidate.Members.Any(
                        member => member.Name == "Target"))
                    .Members);

            InvalidOperationException exception =
                Assert.ThrowsAny<InvalidOperationException>(
                    () => ApiBodyMemberCorrespondence.Resolve(
                        [target.MetadataToken!.Value],
                        tokenOrigin,
                        bodyAssembly,
                        projectAssetsPath: null,
                        targetFramework: null,
                        platformFramework: null));

            Assert.Contains(
                "Cannot correspond",
                exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BodyCorrespondence_RejectsSingletonDependencyOwnerSwap()
    {
        byte[] firstType = CompileFixture(
            "FirstSignatureTypes",
            "namespace Shared; public sealed class Value { }");
        byte[] secondType = CompileFixture(
            "SecondSignatureTypes",
            "namespace Shared; public sealed class Value { }");
        byte[] referenceImage = CompileFixture(
            "DependencyOwnerCorrespondence",
            """
            namespace Sample;
            public static class Widget
            {
                public static void Target(Shared.Value value) { }
            }
            """,
            firstType);
        byte[] runtimeImage = CompileFixture(
            "DependencyOwnerCorrespondence",
            """
            namespace Sample;
            public static class Widget
            {
                public static int Other() => 0;
                public static void Target(Shared.Value value) { }
            }
            """,
            secondType);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"dependency-owner-correspondence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string referencePath = WriteFixture(
                root,
                "ref",
                "DependencyOwnerCorrespondence.dll",
                referenceImage);
            WriteFixture(
                root,
                "ref",
                "FirstSignatureTypes.dll",
                firstType);
            string runtimePath = WriteFixture(
                root,
                "runtime",
                "DependencyOwnerCorrespondence.dll",
                runtimeImage);
            WriteFixture(
                root,
                "runtime",
                "SecondSignatureTypes.dll",
                secondType);
            ResolvedAssemblyReference tokenOrigin =
                TestAssemblyReferences.Designated(referencePath);
            ResolvedAssemblyReference bodyAssembly =
                TestAssemblyReferences.Designated(runtimePath);
            ApiMember target = Assert.Single(
                Assert.Single(
                    Assert.IsType<ApiSurface>(
                        AssemblyReader.ExtractApiSurface(tokenOrigin))
                        .Types)
                    .Members);

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(
                    () => ApiBodyMemberCorrespondence.Resolve(
                        [target.MetadataToken!.Value],
                        tokenOrigin,
                        bodyAssembly,
                        projectAssetsPath: null,
                        targetFramework: null,
                        platformFramework: null));

            Assert.Contains(
                "Cannot correspond",
                exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BodyCorrespondence_ResolvesReferenceIntoSelectedRuntimeRoot()
    {
        byte[] runtimeImage = CompileFixture(
            "SelectedRuntimeRoot",
            """
            namespace Shared
            {
                public sealed class Value { }
                public sealed class Other { }
            }
            namespace Sample
            {
                public static class Widget
                {
                    public static int Other() => 0;
                    public static void Target(Shared.Value value) { }
                    public static void Target(Shared.Other value) { }
                }
            }
            """);
        byte[] referenceImage = CompileFixture(
            "ReferenceSurface",
            """
            namespace Sample;
            public static class Widget
            {
                public static void Target(Shared.Value value) { }
            }
            """,
            runtimeImage);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"selected-runtime-root-correspondence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string referencePath = WriteFixture(
                root,
                "ref",
                "ReferenceSurface.dll",
                referenceImage);
            WriteFixture(
                root,
                "ref",
                "SelectedRuntimeRoot.dll",
                runtimeImage);
            string runtimePath = WriteFixture(
                root,
                "runtime",
                "SelectedRuntimeRoot.dll",
                runtimeImage);
            ResolvedAssemblyReference tokenOrigin =
                TestAssemblyReferences.Designated(referencePath);
            ResolvedAssemblyReference bodyAssembly =
                TestAssemblyReferences.Designated(runtimePath);
            ApiMember target = Assert.Single(
                Assert.Single(
                    Assert.IsType<ApiSurface>(
                        AssemblyReader.ExtractApiSurface(tokenOrigin))
                        .Types)
                    .Members);

            IReadOnlyDictionary<int, int> correspondence =
                ApiBodyMemberCorrespondence.Resolve(
                    [target.MetadataToken!.Value],
                    tokenOrigin,
                    bodyAssembly,
                    projectAssetsPath: null,
                    targetFramework: null,
                    platformFramework: null);

            Assert.Single(correspondence);
            Assert.NotEqual(
                target.MetadataToken.Value,
                correspondence[target.MetadataToken.Value]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BodyCorrespondence_PathlessRootLocalSignaturesDoNotNeedResolver()
    {
        byte[] externalImage = CompileFixture(
            "UnavailableConstraint",
            "namespace External; public class Base { }");
        byte[] referenceImage = CompileFixture(
            "PathlessReference",
            """
            namespace Shared
            {
                public sealed class Value { }
            }
            namespace Unrelated
            {
                public class Generic<T> where T : External.Base { }
            }
            namespace Sample
            {
                public static class Widget
                {
                    public static Shared.Value Target(
                        Shared.Value value) => value;
                }
            }
            """,
            externalImage);
        byte[] runtimeImage = CompileFixture(
            "PathlessRuntime",
            """
            namespace Shared
            {
                public sealed class Value { }
            }
            namespace Unrelated
            {
                public class Generic<T> where T : External.Base { }
            }
            namespace Sample
            {
                public static class Widget
                {
                    public static int Other() => 0;
                    public static Shared.Value Target(
                        Shared.Value value) => value;
                }
            }
            """,
            externalImage);
        ResolvedAssemblyReference tokenOrigin =
            Assert.IsType<ResolvedAssemblyReference>(
                ResolvedAssemblyReference.CreateFromStreamIfManaged(
                    () => new MemoryStream(
                        referenceImage,
                        writable: false),
                    AssemblyResolutionProvenance.Local("test")));
        ResolvedAssemblyReference bodyAssembly =
            Assert.IsType<ResolvedAssemblyReference>(
                ResolvedAssemblyReference.CreateFromStreamIfManaged(
                    () => new MemoryStream(
                        runtimeImage,
                        writable: false),
                    AssemblyResolutionProvenance.Local("test")));
        ApiMember target = Assert.Single(
            Assert.Single(
                Assert.IsType<ApiSurface>(
                    AssemblyReader.ExtractApiSurface(tokenOrigin))
                    .Types,
                type => type.Members.Any(
                    member => member.Name == "Target"))
                .Members);

        IReadOnlyDictionary<int, int> correspondence =
            ApiBodyMemberCorrespondence.Resolve(
                [target.MetadataToken!.Value],
                tokenOrigin,
                bodyAssembly,
                projectAssetsPath: null,
                targetFramework: null,
                platformFramework: null);

        Assert.Single(correspondence);
        Assert.NotEqual(
            target.MetadataToken.Value,
            correspondence[target.MetadataToken.Value]);
    }

    [Fact]
    public void BodyCorrespondence_AmbiguousMetadataNamesFailClosed()
    {
        byte[] firstType = CompileFixture(
            "FirstSignatureTypes",
            "namespace Shared; public sealed class Value { }");
        byte[] secondType = CompileFixture(
            "SecondSignatureTypes",
            "namespace Shared; public sealed class Value { }");
        byte[] referenceImage = CompileAliasedFixture(
            "AmbiguousNominalTypeCorrespondence",
            """
            extern alias First;
            extern alias Second;
            namespace Sample;
            public static class Widget
            {
                public static void Target(First::Shared.Value value) { }
                public static void Target(Second::Shared.Value value) { }
            }
            """,
            ("First", firstType),
            ("Second", secondType));
        byte[] runtimeImage = CompileFixture(
            "AmbiguousNominalTypeCorrespondence",
            """
            namespace Shared
            {
                public sealed class Value { }
            }
            namespace Sample
            {
                public static class Widget
                {
                    public static void Target(Shared.Value value) { }
                }
            }
            """);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"ambiguous-nominal-correspondence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            ResolvedAssemblyReference tokenOrigin =
                TestAssemblyReferences.Designated(
                    WriteFixture(
                        root,
                        "ref",
                        "AmbiguousNominalTypeCorrespondence.dll",
                        referenceImage));
            ResolvedAssemblyReference bodyAssembly =
                TestAssemblyReferences.Designated(
                    WriteFixture(
                        root,
                        "runtime",
                        "AmbiguousNominalTypeCorrespondence.dll",
                        runtimeImage));
            using var originImage =
                AssemblyInspectionSession.Open(tokenOrigin);
            using var bodyImage =
                AssemblyInspectionSession.Open(bodyAssembly);
            int[] sourceTokens = originImage.MethodBodies
                .EnumerateMethods()
                .Where(method => method.Name == "Target")
                .Select(method => method.MetadataToken)
                .ToArray();

            IReadOnlyDictionary<int, MethodBodySelection> correspondence =
                originImage.MethodBodies.ResolveCorrespondingMethods(
                    [sourceTokens[0]],
                    bodyImage.MethodBodies,
                    reference => reference.Type.ToEscapedFullName(),
                    reference => reference.Type.ToEscapedFullName());

            Assert.Equal(2, sourceTokens.Length);
            Assert.Empty(correspondence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PdbSourceToken_CorrespondsReorderedOverloads()
    {
        byte[] referenceImage = CompileFixture(
            "PdbTokenCorrespondence",
            """
            using System.IO;
            namespace Sample;
            public abstract class Widget
            {
                public void Load() { }
                public abstract void Load(Stream stream);
            }
            """);
        byte[] runtimeImage = CompileFixture(
            "PdbTokenCorrespondence",
            """
            using System.IO;
            namespace Sample;
            public abstract class Widget
            {
                public abstract void Load(Stream stream);
                public void Load() { }
            }
            """);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"pdb-token-correspondence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string referencePath = WriteFixture(
                root,
                "ref",
                "PdbTokenCorrespondence.dll",
                referenceImage);
            string runtimePath = WriteFixture(
                root,
                "runtime",
                "PdbTokenCorrespondence.dll",
                runtimeImage);
            ResolvedAssemblyReference tokenOrigin =
                TestAssemblyReferences.Designated(referencePath);
            ResolvedAssemblyReference runtime =
                TestAssemblyReferences.Designated(runtimePath);
            ApiSurface api = Assert.IsType<ApiSurface>(
                AssemblyReader.ExtractApiSurface(tokenOrigin));
            ApiType type = Assert.Single(
                api.Types,
                candidate => candidate.FullName == "Sample.Widget");
            ApiMember selected = Assert.Single(
                type.Members,
                member => ApiMemberIdentity.GetCanonicalSignature(
                    type,
                    member) == "M:Sample.Widget.Load()");

            int runtimeToken = MemberCommand.ResolveSourceMetadataToken(
                selected,
                tokenOrigin,
                runtime,
                options: null);
            using var runtimeImageSession =
                AssemblyInspectionSession.Open(runtime);
            var runtimeAnchor =
                runtimeImageSession.MethodBodies.ResolveMethodAnchor(
                    runtimeToken)!;

            Assert.NotEqual(selected.MetadataToken, runtimeToken);
            Assert.Equal(
                "M:Sample.Widget.Load()",
                runtimeAnchor.CanonicalSignature);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MemberAnalysis_MissingCrossAcquisitionIdentityFailsVisibly()
    {
        byte[] referenceImage = CompileFixture(
            "MissingTokenCorrespondence",
            """
            namespace Sample;
            public static class Widget
            {
                public static int Target(int value) => value;
            }
            """);
        byte[] runtimeImage = CompileFixture(
            "MissingTokenCorrespondence",
            """
            namespace Sample;
            public static class Widget
            {
                public static string Target(string value) => value;
            }
            """);
        string root = Path.Combine(
            Path.GetTempPath(),
            $"missing-token-correspondence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string referencePath = WriteFixture(
                root,
                "ref",
                "MissingTokenCorrespondence.dll",
                referenceImage);
            string runtimePath = WriteFixture(
                root,
                "runtime",
                "MissingTokenCorrespondence.dll",
                runtimeImage);
            ResolvedAssemblyReference tokenOrigin =
                TestAssemblyReferences.Designated(referencePath);
            ResolvedAssemblyReference bodyAssembly =
                TestAssemblyReferences.Designated(runtimePath);
            ApiSurface api = Assert.IsType<ApiSurface>(
                AssemblyReader.ExtractApiSurface(tokenOrigin));
            ApiType type = Assert.Single(
                api.Types,
                candidate => candidate.FullName == "Sample.Widget");
            ApiMember target = Assert.Single(type.Members);

            var inspection = new ApiMemberAnalysisInspection(
                runtimePath,
                [target],
                new HashSet<string> { SectionNames.Signature },
                callerScopeAssemblies: null,
                new MemberOptions
                {
                    TokenOriginAssemblyReference = tokenOrigin,
                    AssemblyReference = bodyAssembly,
                });
            var request = new MemberCodeProvider.Request(
                DecompiledSource: false,
                AnnotatedSource: false,
                CostOverlay: false,
                SemanticsOverlay: false,
                IL: false,
                Attributes: false,
                Calls: false,
                Callers: false,
                CallGraph: false,
                UnsafeOperations: false,
                AssemblyReference: bodyAssembly,
                TokenOriginAssemblyReference: tokenOrigin);

            Assert.Empty(
                MemberCodeProvider.Collect(
                    type,
                    [target],
                    runtimePath,
                    overloadIndex: 0,
                    request));

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(
                    () => inspection.ResolveTargetToken(
                        target.MetadataToken!.Value));

            Assert.Contains(
                "M:Sample.Widget.Target(System.Int32)",
                error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
    public void SourceName_MatchesAssemblyFileName()
    {
        var expected = Path.GetFileNameWithoutExtension(ProductPath);
        Assert.Equal(expected, MethodBodyInspectionSession.Open(ProductPath).SourceName);
    }

    [Fact]
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
    public void CallerEdges_UnknownToken_ReturnsEmpty()
        => Assert.Empty(MethodBodyInspectionSession.Open(ProductPath).CallerEdges(targetToken: 0));

    [Fact]
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
    public void CallGraph_IndependentScopeIdentityConflictRemainsUsable()
    {
        string analysisPath =
            typeof(Analysis.CatalogMemberCorrespondencePlan)
                .Assembly.Location;
        MethodBodyInspectionSession target =
            MethodBodyInspectionSession.Open(analysisPath);
        MethodBodyInspectionSession caller =
            MethodBodyInspectionSession.Open(TestPath);
        Analysis.MethodIdentity typeIdentity =
            target.BodyIndex.DeclaredMethods.Single(method =>
                method.DeclaringType.Name
                    == nameof(Analysis.CallGraphMemberResolver)
                && method.Name == "TypeIdentity");

        CallGraphProjection projection = target.CallGraph(
            typeIdentity.MetadataToken,
            callerScopes: [caller],
            calleeScopes: [],
            out _);

        Assert.NotEmpty(projection.CallSites);
        Assert.Contains(
            projection.Edges,
            edge => edge.CallSiteIds.IsEmpty);
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
        => CompileFixture(
            assemblyName,
            source,
            additionalReferences.Select(image =>
                MetadataReference.CreateFromImage(
                    ImmutableArray.CreateRange(image))));

    static byte[] CompileAliasedFixture(
        string assemblyName,
        string source,
        params (string Alias, byte[] Image)[] additionalReferences)
        => CompileFixture(
            assemblyName,
            source,
            additionalReferences.Select(reference =>
                MetadataReference.CreateFromImage(
                    ImmutableArray.CreateRange(reference.Image),
                    new MetadataReferenceProperties(
                        MetadataImageKind.Assembly,
                        aliases: ImmutableArray.Create(reference.Alias)))));

    static byte[] CompileFixture(
        string assemblyName,
        string source,
        IEnumerable<MetadataReference> additionalReferences)
    {
        ImmutableArray<MetadataReference> references =
        [
            .. PlatformReferences,
            .. additionalReferences,
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
}
