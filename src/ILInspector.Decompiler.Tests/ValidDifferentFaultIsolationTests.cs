using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Services;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class ValidDifferentFaultIsolationTests
{
    [Fact]
    public void AuthoredBodyThatReproducesOriginalIl_ClassifiesBodyDefect()
    {
        using var fixture = FidelityFixture.Create(
            """
            public class Class1
            {
                public int M(int value) { return value + 1; }
            }
            """,
            rejectedBody: "return value + 2;",
            authoredBody: "return value + 1;");

        var result = fixture.Isolate();

        Assert.NotNull(result);
        Assert.Equal(ReturnToSender.FaultIsolationKind.BodyDefect, result.Kind);
        Assert.Equal(ReturnToSender.FaultIsolationMethod.FidelityControl, result.Method);
        Assert.Contains("reproduced the original IL", result.Detail);
    }

    [Fact]
    public void AuthoredBodyThatAlsoDiffers_ClassifiesShellOrClosureDefect()
    {
        using var fixture = FidelityFixture.Create(
            """
            public class Class1
            {
                public int M(int value) { return value + 1; }
            }
            """,
            rejectedBody: "return value + 2;",
            authoredBody: "return value + 3;");

        var result = fixture.Isolate();

        Assert.NotNull(result);
        Assert.Equal(ReturnToSender.FaultIsolationKind.ShellOrClosureDefect, result.Kind);
        Assert.Equal(ReturnToSender.FaultIsolationMethod.FidelityControl, result.Method);
        Assert.Contains("also produced", result.Detail);
    }

    [Fact]
    public void AuthoredBodyThatDoesNotCompile_ClassifiesShellOrClosureDefect()
    {
        using var fixture = FidelityFixture.Create(
            """
            public class Class1
            {
                public int M(int value) { return value + 1; }
            }
            """,
            rejectedBody: "return value + 2;",
            authoredBody: "return Missing.Symbol;");

        var result = fixture.Isolate();

        Assert.NotNull(result);
        Assert.Equal(ReturnToSender.FaultIsolationKind.ShellOrClosureDefect, result.Kind);
        Assert.Equal(ReturnToSender.FaultIsolationMethod.FidelityControl, result.Method);
        Assert.Contains("did not compile", result.Detail);
    }

    [Fact]
    public void AuthoredConstructorBody_PreservesTheFinalRtsConstructorChain()
    {
        using var fixture = FidelityFixture.Create(
            """
            public class Class1
            {
                public Class1() : this(1) { }
                public Class1(int value) { }
            }
            """,
            rejectedBody: "",
            authoredBody: "",
            methodName: ".ctor",
            constructorChain: "this(1)");

        var result = fixture.Isolate();

        Assert.NotNull(result);
        Assert.True(
            result.Kind == ReturnToSender.FaultIsolationKind.BodyDefect,
            result.Detail);
        Assert.Equal(ReturnToSender.FaultIsolationMethod.FidelityControl, result.Method);
    }

    [Fact]
    public void UnavailableFullComparison_ReturnsNoVerdict()
    {
        var unavailable = new IlBodyDiffResult(
            IlBodyDiffOutcome.Unavailable,
            Failure: "body comparison unavailable",
            Rows: []);

        Assert.Null(ReturnToSender.ClassifyFidelityControlStatus(
            opcodesExact: false,
            unavailable));
    }

    [Fact]
    public void MissingAuthoredBody_ReturnsNoVerdict()
    {
        using var fixture = FidelityFixture.Create(
            """
            public class Class1
            {
                public int M(int value) { return value + 1; }
            }
            """,
            rejectedBody: "return value + 2;",
            authoredBody: null);

        Assert.Null(fixture.Isolate());
    }

    /// <summary>
    /// Non-vacuity gate for the successful RTS path. The fixture is a committed
    /// compiler-shape frontier whose decompiled body recompiles but differs in IL;
    /// removing the call from <c>CompileBackTarget</c> leaves the status unchanged
    /// and fails only this attribution assertion.
    /// </summary>
    [Fact]
    public void SuccessfulOpcodeDiff_RunsTheAuthoredFidelityControl()
    {
        using var fixture = FidelityFixture.Create(
            """
            namespace GeneratedFixtures.MinimalSwitchTwoCaseLowersIf;

            public class Class1
            {
                public string Method1(int value)
                {
                    switch (value)
                    {
                        case 0:
                            return "zero";
                        case 1:
                            return "one";
                        default:
                            return "many";
                    }
                }
            }
            """,
            rejectedBody: "",
            authoredBody:
                """
                switch (value)
                {
                    case 0:
                        return "zero";
                    case 1:
                        return "one";
                    default:
                        return "many";
                }
                """,
            fullType: "GeneratedFixtures.MinimalSwitchTwoCaseLowersIf.Class1",
            methodName: "Method1");

        var result = Assert.Single(ReturnToSender.CompileBackTargets(
            fixture.AssemblyPath,
            [fixture.Target],
            fixture.SourceIndex));

        Assert.False(result.UsedCompileBackFloor);
        Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, result.Status);
        Assert.Equal(ReturnToSender.FaultIsolationKind.BodyDefect, result.FaultIsolation?.Kind);
        Assert.Equal(ReturnToSender.FaultIsolationMethod.FidelityControl, result.FaultIsolation?.Method);
    }

    [Fact]
    public void SuccessfulExactResult_DoesNotRunTheFidelityControl()
    {
        using var fixture = FidelityFixture.Create(
            """
            public class Class1
            {
                public int M() { return 42; }
            }
            """,
            rejectedBody: "",
            authoredBody: "return 42;");

        var result = Assert.Single(ReturnToSender.CompileBackTargets(
            fixture.AssemblyPath,
            [fixture.Target],
            fixture.SourceIndex));

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        Assert.Null(result.FaultIsolation);
    }

    sealed class FidelityFixture : IDisposable
    {
        readonly string _directory;
        readonly MetadataContext _metadata;
        readonly MetadataSource _source;
        readonly PEReader _pe;

        FidelityFixture(
            string directory,
            string assemblyPath,
            MetadataContext metadata,
            MetadataSource source,
            PEReader pe,
            MetadataReader reader,
            MethodDefinitionHandle method,
            ArtifactRequest request,
            ReturnToSender.RequestedTarget target,
            ReturnToSenderSourceIndex sourceIndex)
        {
            _directory = directory;
            AssemblyPath = assemblyPath;
            _metadata = metadata;
            _source = source;
            _pe = pe;
            Reader = reader;
            Method = method;
            Request = request;
            Target = target;
            SourceIndex = sourceIndex;
        }

        public string AssemblyPath { get; }
        public MetadataReader Reader { get; }
        public MethodDefinitionHandle Method { get; }
        public ArtifactRequest Request { get; }
        public ReturnToSender.RequestedTarget Target { get; }
        public ReturnToSenderSourceIndex SourceIndex { get; }

        public static FidelityFixture Create(
            string assemblySource,
            string rejectedBody,
            string? authoredBody,
            string fullType = "Class1",
            string methodName = "M",
            string? constructorChain = null)
        {
            string directory = Path.Combine(Path.GetTempPath(), $"rts-fidelity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string assemblyPath = Path.Combine(directory, "Fixture.dll");
            Compile(assemblySource, assemblyPath);

            var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var metadata = CorpusMetadata.Create([assemblyPath]);
            var source = MetadataSource.Open(assemblyPath, context: metadata);
            var (typeHandle, methodHandle) = FindMethod(reader, fullType, methodName);
            var function = IrImporter.Import(source, fullType, methodName, 0)
                ?? throw new InvalidOperationException($"Could not import {fullType}::{methodName}.");
            var type = reader.GetTypeDefinition(typeHandle);
            string? signature = SignatureIdentity.ForMetadataMethod(reader, type, methodHandle);
            var target = new ReturnToSender.RequestedTarget(fullType, methodName, 0, signature);
            var request = new MethodArtifactRequest(
                AssemblyPath: assemblyPath,
                Reader: reader,
                Function: function,
                TargetType: typeHandle,
                TargetMethod: methodHandle,
                TargetBody: new ProductTargetBody(rejectedBody, [], constructorChain),
                FullType: fullType,
                MethodName: methodName,
                Overload: 0,
                SignatureText: "",
                ClosureRoots: new HashSet<TypeDefinitionHandle> { typeHandle },
                ClosureFacts: new Dictionary<TypeDefinitionHandle, List<CompileBackFact>>());
            var sourceIndex = ReturnToSenderSourceIndex.FromCorrelatedMembers(
                [
                    new ReturnToSenderSourceMember(
                        fullType,
                        methodName,
                        0,
                        signature ?? "",
                        Path.Combine(directory, "Fixture.cs"),
                        authoredBody,
                        MetadataTokens.GetToken(methodHandle),
                        reader.GetGuid(reader.GetModuleDefinition().Mvid)),
                ],
                reader);

            return new FidelityFixture(
                directory,
                assemblyPath,
                metadata,
                source,
                pe,
                reader,
                methodHandle,
                request,
                target,
                sourceIndex);
        }

        public ReturnToSender.FaultIsolationResult? Isolate()
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var compileOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable,
                allowUnsafe: true);

            return ReturnToSender.TryIsolateFidelityDifference(
                Request,
                _pe,
                Reader,
                Method,
                SourceIndex,
                parseOptions,
                compileOptions,
                RoslynTestReferences.TrustedPlatform.ToArray());
        }

        public void Dispose()
        {
            _source.Dispose();
            _metadata.Dispose();
            _pe.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        static void Compile(string source, string assemblyPath)
        {
            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(assemblyPath),
                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
                RoslynTestReferences.TrustedPlatform,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));
            var emit = compilation.Emit(assemblyPath);
            Assert.True(
                emit.Success,
                string.Join(Environment.NewLine, emit.Diagnostics));
        }

        static (TypeDefinitionHandle Type, MethodDefinitionHandle Method) FindMethod(
            MetadataReader reader,
            string fullType,
            string methodName)
        {
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if (!string.Equals(reader.GetFullTypeName(type), fullType, StringComparison.Ordinal))
                    continue;

                foreach (var methodHandle in type.GetMethods())
                {
                    if (reader.StringComparer.Equals(reader.GetMethodDefinition(methodHandle).Name, methodName))
                        return (typeHandle, methodHandle);
                }
            }

            throw new InvalidOperationException($"Could not find {fullType}::{methodName}.");
        }
    }
}
