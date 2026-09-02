using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Trait("Area", "RoundTrip")]
public class CompilerFeatureOptionsTests
{
    [Fact]
    public void UpdatedMemorySafetyRules_AreImplementedByCompilerOracle()
    {
        string assembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(assembly);

        var diagnostics = Compile(
            """
            public static class C
            {
                public static int M(int* value)
                {
                    unsafe { return *value; }
                }
            }
            """,
            options).Diagnostics;

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0214");
    }

    [Fact]
    public void RuntimeAsync_IsReplayedFromMethodImplementationMetadata()
    {
        string assembly = typeof(CfgSampleClass).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(assembly);

        Assert.Contains(
            options.Features,
            feature => feature.Key == "runtime-async" && feature.Value == "on");

        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public static class C
            {
                public static async Task<int> M()
                {
                    await Task.Yield();
                    return 1;
                }
            }
            """,
            options);
        var reader = compiled.PE.GetMetadataReader();
        var method = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Where(type => reader.GetString(type.Name) == "C")
            .SelectMany(type => type.GetMethods())
            .Select(reader.GetMethodDefinition)
            .Single(method => reader.GetString(method.Name) == "M");

        const System.Reflection.MethodImplAttributes RuntimeAsync =
            (System.Reflection.MethodImplAttributes)0x2000;
        Assert.True((method.ImplAttributes & RuntimeAsync) != 0);
    }

    [Fact]
    public void UpdatedSafePointerComparisonAwait_RemainsReconstructable()
    {
        string oracleAssembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(oracleAssembly)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
                new KeyValuePair<string, string>("runtime-async", "off"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public static class C
            {
                public static unsafe async Task<bool> M(nint value)
                {
                    return await Task.FromResult((int*)value == null);
                }
            }
            """,
            options);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"updated-pointer-await-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            MetadataReader reader = source.Reader;
            TypeDefinitionHandle stateMachine = reader.TypeDefinitions.Single(
                handle => reader.GetString(
                        reader.GetTypeDefinition(handle).Name)
                    .StartsWith("<M>d__", StringComparison.Ordinal));
            MethodDefinitionHandle moveNext = reader.GetTypeDefinition(stateMachine)
                .GetMethods()
                .Single(handle => reader.GetString(
                    reader.GetMethodDefinition(handle).Name) == "MoveNext");
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => method.Name == "MoveNext"
                        ? IrImporter.Import(source, moveNext)
                        : IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);

            Assert.True(
                result.Fidelity == DecompilationFidelity.Full,
                $"{result.Output}\n{string.Join('\n', result.Diagnostics)}\n"
                + string.Join(
                    '\n',
                    function.Descendants
                        .OfType<UnsupportedNode>()
                        .Select(node => $"{node.Opcode}: {node.Reason}")));
            Assert.True(result.RequiresAsyncBodyModifier);
            Assert.Single(function.Descendants.OfType<AwaitExpression>());
            Assert.DoesNotContain(
                function.Descendants.OfType<UnsupportedNode>(),
                node => node.Opcode == "classic async");
            Assert.Contains(
                "return await Task.FromResult<bool>",
                result.Output);

            using var recompiled = Compile(
                $$"""
                using System.Threading.Tasks;

                public static class D
                {
                    public static async Task<bool> M(nint value)
                    {
                {{result.Output}}
                    }
                }
                """,
                options);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static CompiledAssembly Compile(string source, CSharpParseOptions options)
    {
        var compilation = CSharpCompilation.Create(
            "CompilerFeatureOptionsTests",
            [CSharpSyntaxTree.ParseText(source, options)],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                optimizationLevel: OptimizationLevel.Release));
        var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        var diagnostics = emit.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.True(emit.Success, string.Join(Environment.NewLine, diagnostics));
        stream.Position = 0;
        return new CompiledAssembly(
            stream,
            new PEReader(stream, PEStreamOptions.LeaveOpen),
            diagnostics);
    }

    sealed class CompiledAssembly(
        MemoryStream stream,
        PEReader pe,
        ImmutableArray<Diagnostic> diagnostics) : IDisposable
    {
        public PEReader PE { get; } = pe;
        public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
        public byte[] Image => stream.ToArray();

        public void Dispose()
        {
            PE.Dispose();
            stream.Dispose();
        }
    }
}
