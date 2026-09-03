using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    public void UpdatedSafePointerSignatureAwait_RemainsReconstructable(
        string runtimeAsync)
    {
        string oracleAssembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(oracleAssembly)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
                new KeyValuePair<string, string>("runtime-async", runtimeAsync),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public static class C
            {
                public static Task<int> Safe(int* value)
                    => Task.FromResult(1);

                public static async Task<int> M(nint value)
                    => await Safe((int*)value);
            }
            """,
            options);
        using var source = MetadataSource.OpenFromPrefetchedImage(
            "updated-safe-pointer-signature.dll",
            ImmutableArray.Create(compiled.Image));
        var function = IrImporter.Import(source, "C", "M");
        Assert.NotNull(function);
        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        function.CheckInvariant();

        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.DoesNotContain(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode is "runtime await" or "classic async");
        Assert.Contains("return await Safe((int*)value);", result.Output);
        Assert.DoesNotContain("unsafe", result.Output);

        using var recompiled = Compile(
            $$"""
            using System.Threading.Tasks;

            public static class D
            {
                public static Task<int> Safe(int* value)
                    => Task.FromResult(1);

                public static async Task<int> M(nint value)
                {
            {{result.Output}}
                }
            }
            """,
            options);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    public void LegacyCaller_AwaitingUpdatedSafePointer_RemainsReconstructable(
        string runtimeAsync)
    {
        var updatedOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var library = Compile(
            """
            using System.Threading.Tasks;

            public static class Library
            {
                public static Task<int> Safe(int* value)
                    => Task.FromResult(1);
            }
            """,
            updatedOptions,
            assemblyName: "UpdatedPointerLibrary");
        var callerOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "runtime-async",
                    runtimeAsync),
            ]);
        MetadataReference libraryReference =
            MetadataReference.CreateFromImage(library.Image);
        using var caller = Compile(
            """
            using System.Threading.Tasks;

            public static class Consumer
            {
                public static async Task<int> M(nint value)
                    => await Library.Safe((int*)value);
            }
            """,
            callerOptions,
            assemblyName: "LegacyPointerConsumer",
            additionalReferences: [libraryReference]);

        DecompilerResult result = DecompileWithSibling(
            "UpdatedPointerLibrary.dll",
            library.Image,
            "LegacyPointerConsumer.dll",
            caller.Image,
            "Consumer",
            "M");

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains(
            "return await Library.Safe((int*)value);",
            result.Output);
        Assert.DoesNotContain("unsafe", result.Output);

        using var recompiled = Compile(
            $$"""
            using System.Threading.Tasks;

            public static class Recompiled
            {
                public static async Task<int> M(nint value)
                {
            {{result.Output}}
                }
            }
            """,
            callerOptions,
            additionalReferences: [libraryReference]);
    }

    [Fact]
    public void UpdatedCrossAssemblyUnsafeMethodGroup_RetainsUnsafeContext()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var library = Compile(
            """
            public static class Library
            {
                public static unsafe void Risky() { }
            }

            public class InstanceLibrary
            {
                public virtual unsafe void Risky() { }
            }
            """,
            options,
            assemblyName: "UnsafeMethodGroupLibrary");
        MetadataReference libraryReference =
            MetadataReference.CreateFromImage(library.Image);
        using var caller = Compile(
            """
            using System;

            public static class Consumer
            {
                public static Action Make()
                {
                    unsafe { return Library.Risky; }
                }

                public static Action MakeVirtual(InstanceLibrary value)
                {
                    unsafe { return value.Risky; }
                }
            }
            """,
            options,
            assemblyName: "UnsafeMethodGroupConsumer",
            additionalReferences: [libraryReference]);

        DecompilerResult result = DecompileWithSibling(
            "UnsafeMethodGroupLibrary.dll",
            library.Image,
            "UnsafeMethodGroupConsumer.dll",
            caller.Image,
            "Consumer",
            "Make");

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("unsafe", result.Output);
        Assert.Contains("new Action(Library.Risky)", result.Output);

        DecompilerResult virtualResult = DecompileWithSibling(
            "UnsafeMethodGroupLibrary.dll",
            library.Image,
            "UnsafeMethodGroupConsumer.dll",
            caller.Image,
            "Consumer",
            "MakeVirtual");

        Assert.Equal(
            DecompilationFidelity.Full,
            virtualResult.Fidelity);
        Assert.Contains("unsafe", virtualResult.Output);
        Assert.Contains(
            "new Action(value.Risky)",
            virtualResult.Output);

        using var recompiled = Compile(
            $$"""
            using System;

            public static class Recompiled
            {
                public static Action Make()
                {
            {{result.Output}}
                }

                public static Action MakeVirtual(InstanceLibrary value)
                {
            {{virtualResult.Output}}
                }
            }
            """,
            options,
            additionalReferences: [libraryReference]);
    }

    [Fact]
    public void LegacyCaller_UpdatedUnsafeMethodGroup_IgnoresExplicitContract()
    {
        var updatedOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var library = Compile(
            """
            public static class Library
            {
                public static unsafe void Risky() { }
            }
            """,
            updatedOptions,
            assemblyName: "UpdatedUnsafeMethodGroupLibrary");
        MetadataReference libraryReference =
            MetadataReference.CreateFromImage(library.Image);
        var callerOptions = new CSharpParseOptions(
            LanguageVersion.Preview);
        using var caller = Compile(
            """
            using System;

            public static class Consumer
            {
                public static Action Make()
                    => Library.Risky;
            }
            """,
            callerOptions,
            assemblyName: "LegacyMethodGroupConsumer",
            additionalReferences: [libraryReference]);

        DecompilerResult result = DecompileWithSibling(
            "UpdatedUnsafeMethodGroupLibrary.dll",
            library.Image,
            "LegacyMethodGroupConsumer.dll",
            caller.Image,
            "Consumer",
            "Make");

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("new Action(Library.Risky)", result.Output);
        Assert.DoesNotContain("unsafe", result.Output);

        using var recompiled = Compile(
            $$"""
            using System;

            public static class Recompiled
            {
                public static Action Make()
                {
            {{result.Output}}
                }
            }
            """,
            callerOptions,
            additionalReferences: [libraryReference]);
    }

    [Fact]
    public void UpdatedCaller_LegacyPointerContract_RequiresUnsafeContext()
    {
        var legacyOptions = new CSharpParseOptions(
            LanguageVersion.Preview);
        using var library = Compile(
            """
            public static class Library
            {
                public static unsafe int Legacy(int* value)
                    => 1;
            }
            """,
            legacyOptions,
            assemblyName: "LegacyPointerLibrary");
        MetadataReference libraryReference =
            MetadataReference.CreateFromImage(library.Image);
        var updatedOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var caller = Compile(
            """
            public static class Consumer
            {
                public static int M(nint value)
                {
                    unsafe { return Library.Legacy((int*)value); }
                }
            }
            """,
            updatedOptions,
            assemblyName: "UpdatedLegacyPointerConsumer",
            additionalReferences: [libraryReference]);

        DecompilerResult result = DecompileWithSibling(
            "LegacyPointerLibrary.dll",
            library.Image,
            "UpdatedLegacyPointerConsumer.dll",
            caller.Image,
            "Consumer",
            "M");

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("unsafe", result.Output);
        Assert.Contains("Library.Legacy", result.Output);

        using var recompiled = Compile(
            $$"""
            public static class Recompiled
            {
                public static int M(nint value)
                {
            {{result.Output}}
                }
            }
            """,
            updatedOptions,
            additionalReferences: [libraryReference]);
    }

    [Fact]
    public void UnsupportedDependencyMemorySafetyRules_DeclinesWithoutPromotingDirectAttribute()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var library = Compile(
            """
            public static class Library
            {
                public static unsafe int Risky() => 1;

                public static int Call()
                {
                    unsafe { return Risky(); }
                }
            }
            """,
            options,
            assemblyName: "UnsupportedRulesLibrary");
        MetadataReference libraryReference =
            MetadataReference.CreateFromImage(library.Image);
        using var caller = Compile(
            """
            public static class Consumer
            {
                public static int M()
                {
                    unsafe { return Library.Risky(); }
                }
            }
            """,
            options,
            assemblyName: "UnsupportedRulesConsumer",
            additionalReferences: [libraryReference]);

        byte[] unsupportedLibrary =
            WithMemorySafetyRulesVersion(library.Image, version: 99);
        DecompilerResult result = DecompileWithSibling(
            "UnsupportedRulesLibrary.dll",
            unsupportedLibrary,
            "UnsupportedRulesConsumer.dll",
            caller.Image,
            "Consumer",
            "M");

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.DoesNotContain("unsafe", result.Output);

        using var sameAssemblySource = MetadataSource.OpenFromPrefetchedImage(
            "UnsupportedRulesLibrary.dll",
            ImmutableArray.Create(unsupportedLibrary));
        var function = IrImporter.Import(
            sameAssemblySource,
            "Library",
            "Call");
        Assert.NotNull(function);
        IrPasses.Run(function);
        var call = Assert.Single(function.Descendants.OfType<Call>());

        Assert.Equal(MetadataFactState.Unknown, call.Callee.RequiresUnsafeFact);
        Assert.Equal(
            MemorySafetyRulesState.Unsupported,
            call.Callee.MemorySafetyRulesState);
        var sameAssemblyResult = CSharpPrinter.Print(function);
        Assert.Equal(DecompilationFidelity.Partial, sameAssemblyResult.Fidelity);
        Assert.DoesNotContain("unsafe", sameAssemblyResult.Output);
    }

    static DecompilerResult DecompileWithSibling(
        string libraryName,
        byte[] libraryImage,
        string consumerName,
        byte[] consumerImage,
        string typeName,
        string methodName)
    {
        string directory = Directory.CreateTempSubdirectory(
            "dotnet-inspect-memory-safety-").FullName;
        string consumerPath = Path.Combine(directory, consumerName);
        try
        {
            File.WriteAllBytes(
                Path.Combine(directory, libraryName),
                libraryImage);
            File.WriteAllBytes(consumerPath, consumerImage);
            using var source = MetadataSource.OpenWithoutSymbols(
                consumerPath);
            var function = IrImporter.Import(
                source,
                typeName,
                methodName);
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();
            return CSharpPrinter.Print(function);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static CompiledAssembly Compile(
        string source,
        CSharpParseOptions options,
        string assemblyName = "CompilerFeatureOptionsTests",
        ImmutableArray<MetadataReference> additionalReferences = default)
    {
        ImmutableArray<MetadataReference> references =
            additionalReferences.IsDefaultOrEmpty
                ? RoslynTestReferences.TrustedPlatform
                : RoslynTestReferences.TrustedPlatform.AddRange(
                    additionalReferences);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, options)],
            references,
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

    static byte[] WithMemorySafetyRulesVersion(byte[] image, int version)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var index = MemorySafetyMetadataIndex.Create(reader);
        var observation = Assert.Single(index.Rules.Observations);
        Assert.Equal(MemorySafetyRulesObservationState.Decoded, observation.State);
        Assert.Equal(2, observation.Version);
        var attribute = reader.GetCustomAttribute(
            (CustomAttributeHandle)MetadataTokens.EntityHandle(
                observation.AttributeToken));
        byte[] original = reader.GetBlobBytes(attribute.Value);
        Assert.Equal(
            [1, 0, 2, 0, 0, 0, 0, 0],
            original);

        var offsets = new List<int>();
        for (int offset = 0; offset <= image.Length - original.Length; offset++)
        {
            if (image.AsSpan(offset, original.Length).SequenceEqual(original))
                offsets.Add(offset);
        }

        int valueOffset = Assert.Single(offsets);
        byte[] mutated = [.. image];
        BitConverter.TryWriteBytes(
            mutated.AsSpan(valueOffset + 2, sizeof(int)),
            version);
        return mutated;
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
