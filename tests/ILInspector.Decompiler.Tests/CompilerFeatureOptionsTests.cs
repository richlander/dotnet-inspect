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

    [Theory]
    [InlineData(2, MemorySafetyMode.Updated)]
    [InlineData(3, null)]
    [InlineData(99, null)]
    public void ModuleMarker_SelectsOnlyRecognizedLanguageModes(
        int version,
        MemorySafetyMode? expectedMode)
    {
        using var pe = new PEReader(new MemoryStream(BuildMarkedModule(version)));
        CompilerFeatureOptions.Resolution resolution =
            CompilerFeatureOptions.Resolve(pe);

        if (expectedMode is { } mode)
        {
            var available = Assert.IsType<
                CompilerFeatureOptions.Resolution.Available>(resolution);
            Assert.Equal(mode, available.Mode);
        }
        else
        {
            Assert.IsType<CompilerFeatureOptions.Resolution.Unavailable>(
                resolution);
        }
    }

    [Fact]
    public void UnmarkedModule_CompilesAsLegacy()
    {
        using var pe = new PEReader(new MemoryStream(BuildMarkedModule(null)));
        var options = CompilerFeatureOptions.ParseOptions(pe);

        Assert.NotEqual(LanguageVersion.Preview, options.LanguageVersion);
        Assert.DoesNotContain(
            options.Features,
            feature => feature.Key == "updated-memory-safety-rules");
    }

    [Fact]
    public void HarnessReplayEnforcesDistinctLegacyAndUpdatedUnsafeRules()
    {
        const string source =
            """
            public static class C
            {
                public static bool M(int* left, int* right) => left < right;
            }
            """;
        using var legacyPe = new PEReader(
            new MemoryStream(BuildMarkedModule(null)));
        using var updatedPe = new PEReader(
            new MemoryStream(BuildMarkedModule(2)));
        var legacyOptions = CompilerFeatureOptions.ParseOptions(legacyPe);
        var updatedOptions = CompilerFeatureOptions.ParseOptions(updatedPe);

        Assert.NotEqual(LanguageVersion.Preview, legacyOptions.LanguageVersion);
        Assert.Equal(LanguageVersion.Preview, updatedOptions.LanguageVersion);
        Assert.Contains(
            CompileDiagnostics(source, legacyOptions),
            diagnostic => diagnostic.Id == "CS0214");
        Assert.DoesNotContain(
            CompileDiagnostics(source, updatedOptions),
            diagnostic => diagnostic.Id == "CS0214");
    }

    public static TheoryData<string, byte[], MemorySafetyMode?> ReplayModeImages() => new()
    {
        { "unmarked", BuildMarkedModule(null), MemorySafetyMode.Legacy },
        { "v2 MethodDef ctor", BuildMarkedModule(2), MemorySafetyMode.Updated },
        { "v3 MethodDef ctor", BuildMarkedModule(3), null },
        { "v99 MethodDef ctor", BuildMarkedModule(99), null },
        { "v2 MemberRef->TypeDef ctor", BuildMarkedModule(2, memberRefConstructor: true), MemorySafetyMode.Updated },
        { "malformed marker blob", BuildMarkedModule(2, malformedValue: true), null },
        { "conflicting markers", BuildMarkedModule(2, secondVersion: 3), null },
    };

    /// <summary>
    /// The harness derives replay mode from the normalized Metadata-owned model,
    /// not from raw marker presence.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReplayModeImages))]
    public void HarnessReplayUsesNormalizedModuleRules(
        string label,
        byte[] image,
        MemorySafetyMode? expectedMode)
    {
        using var pe = new PEReader(new MemoryStream(image));
        CompilerFeatureOptions.Resolution resolution =
            CompilerFeatureOptions.Resolve(pe);

        if (expectedMode is { } mode)
        {
            var available = Assert.IsType<
                CompilerFeatureOptions.Resolution.Available>(resolution);
            Assert.True(
                mode == available.Mode,
                $"{label}: expected {mode}, resolved {available.Mode}");
        }
        else
        {
            Assert.IsType<CompilerFeatureOptions.Resolution.Unavailable>(
                resolution);
        }
    }

    [Fact]
    public void HarnessReplayMatchesPrinterMode()
    {
        const string sourceText =
            """
            public static class C
            {
                public static int M() => 1;
            }
            """;
        var legacyOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var updatedOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
            new KeyValuePair<string, string>(
                "updated-memory-safety-rules",
                "true"),
        ]);
        using var legacy = Compile(
            sourceText,
            legacyOptions,
            assemblyName: "LegacyReplayMode");
        using var updated = Compile(
            sourceText,
            updatedOptions,
            assemblyName: "UpdatedReplayMode");
        byte[] unsupported = WithMemorySafetyRulesVersion(
            updated.Image,
            version: 99);

        foreach ((string label, byte[] image) in new[]
        {
            ("legacy", legacy.Image),
            ("updated", updated.Image),
            ("unsupported", unsupported),
        })
        {
            using var source = MetadataSource.OpenFromPrefetchedImage(
                $"{label}.dll",
                ImmutableArray.Create(image));
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            using var pe = new PEReader(
                new MemoryStream(image, writable: false));
            CompilerFeatureOptions.Resolution harnessMode =
                CompilerFeatureOptions.Resolve(pe);

            if (function.MemorySafetyMode
                is MemorySafetyModeDecision.Available printerAvailable)
            {
                var harnessAvailable = Assert.IsType<
                    CompilerFeatureOptions.Resolution.Available>(harnessMode);
                Assert.Equal(printerAvailable.Mode, harnessAvailable.Mode);
                Assert.True(CSharpPrinter.Print(function).Succeeded);
            }
            else
            {
                Assert.IsType<MemorySafetyModeDecision.Unavailable>(
                    function.MemorySafetyMode);
                Assert.IsType<
                    CompilerFeatureOptions.Resolution.Unavailable>(harnessMode);
                DecompilerResult result = CSharpPrinter.PrintRaised(function);
                Assert.False(result.Succeeded);
                Assert.Contains(
                    result.Diagnostics,
                    diagnostic => diagnostic.Id
                        == DiagnosticIds.MemorySafetyModeUnavailable);
            }
        }
    }

    [Fact]
    public void SimulateModeExplicitlyOverridesUnsupportedModuleRules()
    {
        const string sourceText =
            """
            public static class C
            {
                public static int M() => 1;
            }
            """;
        var updatedOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var updated = Compile(
            sourceText,
            updatedOptions,
            assemblyName: "UnsupportedSimulation");
        byte[] unsupported = WithMemorySafetyRulesVersion(
            updated.Image,
            version: 99);

        using var source = MetadataSource.OpenFromPrefetchedImage(
            "unsupported-simulation.dll",
            ImmutableArray.Create(unsupported));
        source.SimulateNewRules = true;
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, "C", "M"));

        var mode = Assert.IsType<MemorySafetyModeDecision.Available>(
            function.MemorySafetyMode);
        Assert.Equal(MemorySafetyMode.Updated, mode.Mode);
        Assert.True(CSharpPrinter.PrintRaised(function).Succeeded);
    }

    [Fact]
    public void UnavailableMetadataRules_DoNotSelectALanguageMode()
    {
        var rules = new MemorySafetyRulesResult.Unavailable(
            new MemorySafetyMetadataFailure(
                MemorySafetyMetadataFailureKind.BudgetExceeded,
                "synthetic budget"),
            []);

        Assert.IsType<MemorySafetyModeDecision.Unavailable>(
            MemorySafetyModeDecision.Resolve(rules));
    }

    [Fact]
    public void CompileBackReportsUnsupportedModuleModeAsUnavailable()
    {
        var updatedOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var updated = Compile(
            """
            public static class C
            {
                public static int M() => 1;
            }
            """,
            updatedOptions,
            assemblyName: "UnsupportedCompileBack");
        byte[] unsupported = WithMemorySafetyRulesVersion(
            updated.Image,
            version: 99);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"unsupported-compile-back-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, unsupported);
        try
        {
            using (var pe = new PEReader(
                new MemoryStream(unsupported, writable: false)))
            {
                ApiType type = Assert.Single(
                    ApiSurfaceExtractor.Extract(pe).Types,
                    candidate => candidate.FullName == "C");
                DecompilerResult typeProjection =
                    MemberBodyProducer.Project(type, path, pdbPath: null);
                Assert.False(typeProjection.Succeeded);
                Assert.Contains(
                    typeProjection.Diagnostics,
                    diagnostic => diagnostic.Id
                        == DiagnosticIds.MemorySafetyModeUnavailable);

                ApiMember member = Assert.Single(
                    type.Members,
                    candidate => candidate.Name == "M");
                MemberRenderResult memberProjection =
                    MemberBodyProducer.ProduceMember(
                        type,
                        member,
                        path,
                        pdbPath: null);
                Assert.Equal(
                    MemberBodyProductionStatus.Failed,
                    memberProjection.Status);
                Assert.Contains(
                    DiagnosticIds.MemorySafetyModeUnavailable,
                    memberProjection.Text);
            }

            ValidityCheck.MethodResult validity = Assert.Single(
                ValidityCheck.Evaluate(
                    path,
                    sequential: true),
                result => result.TypeName == "C"
                    && result.MethodName == "M");
            Assert.True(validity.CompileBackUnavailable);
            Assert.Contains(
                "Unsupported",
                validity.CompileBackUnavailableReason);

            IReadOnlyList<FidelityCheck.CompileBackResult> fidelity =
                FidelityCheck.Evaluate(
                    path,
                    lowered: false,
                    typeFilter: typeName => typeName == "C");
            FidelityCheck.CompileBackResult row = Assert.Single(
                fidelity,
                result => result.Type == "C"
                    && result.Method == "M");
            Assert.Equal(
                FidelityCheck.CompileBackStatus.FidelityUnavailable,
                row.Status);
            Assert.Contains(
                "memory-safety-mode-unavailable",
                row.Detail);

            using (var source = MetadataSource.OpenWithoutSymbols(path))
            {
                DecompilerResult stageDump = StageDump.DumpMethod(
                    source,
                    "C",
                    "M");
                Assert.False(stageDump.Succeeded);
                Assert.Contains(
                    stageDump.Diagnostics,
                    diagnostic => diagnostic.Id
                        == DiagnosticIds.MemorySafetyModeUnavailable);

                IrFunction crash = Assert.IsType<IrFunction>(
                    IrImporter.Import(
                        source,
                        MetadataTokens.MethodDefinitionHandle(
                            0x00FFFFFE)));
                Assert.Contains(
                    crash.Diagnostics,
                    diagnostic => diagnostic.Id
                        == DiagnosticIds.InternalError);
                Assert.IsType<MemorySafetyModeDecision.Unavailable>(
                    crash.MemorySafetyMode);
                DecompilerResult crashProjection =
                    CSharpPrinter.Print(crash);
                Assert.Contains(
                    crashProjection.Diagnostics,
                    diagnostic => diagnostic.Id
                        == DiagnosticIds.InternalError);
                Assert.Contains(
                    crashProjection.Diagnostics,
                    diagnostic => diagnostic.Id
                        == DiagnosticIds.MemorySafetyModeUnavailable);
            }

            Dictionary<string, RenderAbSensor.RenderedMethod> renders =
                RenderAbSensor.CollectRenders(
                    [path],
                    methodCap: 10,
                    workers: null,
                    sequential: true);
            RenderAbSensor.RenderedMethod render = Assert.Single(
                renders.Values,
                result => result.TypeName == "C"
                    && result.MethodName == "M");
            Assert.Contains(
                "Unsupported",
                render.CompileBackUnavailableReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpdatedByRefPointerLambdaBesideAwait_RemainsReconstructable()
    {
        string oracleAssembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(oracleAssembly)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public unsafe delegate int Callback(ref int* value);

            public static class C
            {
                static int Consume(Callback callback, int value) => value;

                public static async Task<int> M(Task<int> task)
                    => Consume((ref int* value) => 1, await task);
            }
            """,
            options,
            assemblyName: "UpdatedPointerLambdaAwait");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"updated-pointer-lambda-await-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);
            string output = Assert.IsType<string>(result.Output);

            Assert.True(
                result.Fidelity == DecompilationFidelity.Full,
                $"{output}\n{string.Join('\n', result.Diagnostics)}");
            Assert.False(result.RequiresUnsafeBodyModifier);
            Assert.DoesNotContain("unsafe", output);
            Assert.Contains("await", output);
            using var recompiled = Compile(
                $$"""
                using System.Threading.Tasks;

                public unsafe delegate int Callback(ref int* value);

                public static class D
                {
                    static int Consume(Callback callback, int value) => value;

                    public static async Task<int> M(Task<int> task)
                    {
                {{output}}
                    }
                }
                """,
                options,
                assemblyName: "UpdatedPointerLambdaAwaitRoundTrip");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpdatedNestedUnsafeLambdaBesideAwait_RemainsReconstructable()
    {
        string oracleAssembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(oracleAssembly)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public unsafe delegate int Callback(int* value);

            public static class C
            {
                static int Consume(int value, Callback callback) => value;

                public static async Task<int> M(Task<int> task)
                    => Consume(
                        await task,
                        value =>
                        {
                            unsafe
                            {
                                return *value;
                            }
                        });
            }
            """,
            options,
            assemblyName: "UpdatedNestedUnsafeLambdaAwait");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"updated-nested-unsafe-lambda-await-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);
            string output = Assert.IsType<string>(result.Output);

            Assert.True(
                result.Fidelity == DecompilationFidelity.Full,
                $"{output}\n{string.Join('\n', result.Diagnostics)}");
            Assert.Contains("await", output);
            Assert.Contains("=> unsafe(", output);
            Assert.DoesNotContain("unsafe\n{", output);
            using var recompiled = Compile(
                $$"""
                using System.Threading.Tasks;

                public unsafe delegate int Callback(int* value);

                public static class D
                {
                    static int Consume(int value, Callback callback) => value;

                    public static async Task<int> M(Task<int> task)
                    {
                {{output}}
                    }
                }
                """,
                options,
                assemblyName: "UpdatedNestedUnsafeLambdaAwaitRoundTrip");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpdatedVoidUnsafeLambdaBesideAwait_RemainsReconstructable()
    {
        string oracleAssembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(oracleAssembly)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System;
            using System.Threading.Tasks;

            public static class C
            {
                static unsafe void Risky() { }

                public static async Task M(Task task)
                {
                    Action action = () =>
                    {
                        unsafe
                        {
                            Risky();
                        }
                    };
                    action();
                    await task;
                }
            }
            """,
            options,
            assemblyName: "UpdatedVoidUnsafeLambdaAwait");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"updated-void-unsafe-lambda-await-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);
            string output = Assert.IsType<string>(result.Output);

            Assert.True(
                result.Fidelity == DecompilationFidelity.Full,
                $"{output}\n{string.Join('\n', result.Diagnostics)}");
            Assert.Contains("await", output);
            Assert.Contains("unsafe", output);
            Assert.Contains("Risky();", output);
            Assert.DoesNotContain("return Risky();", output);
            Assert.DoesNotContain("await", FirstUnsafeBlockBody(output));
            using var recompiled = Compile(
                $$"""
                using System;
                using System.Threading.Tasks;

                public static class D
                {
                    static unsafe void Risky() { }

                    public static async Task M(Task task)
                    {
                {{output}}
                    }
                }
                """,
                options,
                assemblyName: "UpdatedVoidUnsafeLambdaAwaitRoundTrip");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyPointerForLoopBeforeAwait_RemainsReconstructable()
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public static class C
            {
                public static async Task<int> M(Task task, int[] values)
                {
                    int sum = 0;
                    unsafe
                    {
                        fixed (int* start = values)
                        {
                            for (int* pointer = start;
                                pointer != start + values.Length;
                                pointer++)
                            {
                                sum += *pointer;
                            }
                        }
                    }

                    await task;
                    return sum;
                }
            }
            """,
            options,
            assemblyName: "LegacyPointerForLoopAwait");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"legacy-pointer-for-loop-await-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);
            string output = Assert.IsType<string>(result.Output);

            Assert.True(
                result.Fidelity == DecompilationFidelity.Full,
                $"{output}\n{string.Join('\n', result.Diagnostics)}");
            Assert.Contains("await task", output);
            Assert.DoesNotContain("await", FirstUnsafeBlockBody(output));
            using var recompiled = Compile(
                $$"""
                using System.Threading.Tasks;

                public static class D
                {
                    public static async Task<int> M(Task task, int[] values)
                    {
                {{output}}
                    }
                }
                """,
                options,
                assemblyName: "LegacyPointerForLoopAwaitRoundTrip");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyPointerForLoopWithEscapingReference_DeclinesVisibly()
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public static class C
            {
                public static async Task<int> M(Task task, int[] values)
                {
                    int sum = 0;
                    int steps;
                    unsafe
                    {
                        fixed (int* start = values)
                        {
                            int* pointer;
                            for (pointer = start;
                                pointer != start + values.Length;
                                pointer++)
                            {
                                sum += *pointer;
                            }
                            steps = (int)(pointer - start);
                        }
                    }

                    await task;
                    return sum + steps;
                }
            }
            """,
            options,
            assemblyName: "LegacyPointerForLoopEscape");
        using var source = MetadataSource.OpenFromPrefetchedImage(
            "legacy-pointer-for-loop-escape.dll",
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

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "unsafe await boundary"
                && node.Reason.Contains(
                    "legacy pointer lifetime cannot be scoped outside await",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyRuntimeAsyncPointerOperations_UseAwaitFreeUnsafeBlocks()
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public sealed unsafe class Holder
            {
                public int* Pointer => null;
            }

            public static class C
            {
                static unsafe int Read(int* value) => *value;

                public static async Task<int> M(
                    Task<int> task,
                    Holder holder,
                    int[] values)
                {
                    int result;
                    unsafe
                    {
                        int* pointer = holder.Pointer;
                        result = 0;
                        fixed (int* pinned = values)
                        {
                            result = Read(pointer)
                                + sizeof(int*)
                                + (pointer + 1 > pointer ? 1 : 0)
                                + *pinned;
                        }
                    }
                    return result + await task;
                }
            }
            """,
            options,
            assemblyName: "LegacyRuntimeAsyncUnsafeOperations");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"legacy-runtime-async-unsafe-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);
            string output = Assert.IsType<string>(result.Output);

            Assert.True(
                result.Fidelity == DecompilationFidelity.Full,
                $"{output}\n{string.Join('\n', result.Diagnostics)}");
            Assert.True(result.RequiresAsyncBodyModifier);
            Assert.False(result.RequiresUnsafeBodyModifier);
            Assert.Contains("unsafe", output);
            Assert.DoesNotContain("await", FirstUnsafeBlockBody(output));

            var replay = CompilerFeatureOptions.ParseOptions(path);
            Assert.NotEqual(LanguageVersion.Preview, replay.LanguageVersion);
            using var recompiled = Compile(
                $$"""
                using System.Threading.Tasks;

                public sealed unsafe class Holder
                {
                    public int* Pointer => null;
                }

                public static class D
                {
                    static unsafe int Read(int* value) => *value;

                    public static async Task<int> M(
                        Task<int> task,
                        Holder holder,
                        int[] values)
                    {
                {{output}}
                    }
                }
                """,
                replay,
                assemblyName: "LegacyRuntimeAsyncUnsafeOperationsRoundTrip");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyRuntimeAsyncNestedPointerLocal_DoesNotHoistOutsideUnsafe()
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest)
            .WithFeatures([
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public static class C
            {
                static unsafe int Read(int* value) => *value;

                public static async Task<int> M(
                    Task<int> task,
                    int[] values,
                    bool flag)
                {
                    int result;
                    unsafe
                    {
                        fixed (int* pinned = values)
                        {
                            result = Read(pinned);
                            int* pointer = pinned;
                            if (flag)
                            {
                                pointer++;
                            }
                            result = Read(pointer);
                        }
                    }
                    return result + await task;
                }
            }
            """,
            options,
            assemblyName: "LegacyRuntimeAsyncNestedPointerLocal");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"legacy-runtime-async-nested-pointer-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);
            string output = Assert.IsType<string>(result.Output);

            Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
            Assert.DoesNotContain("int* V_2;", output);
            using var recompiled = Compile(
                $$"""
                using System.Threading.Tasks;

                public static class D
                {
                    static unsafe int Read(int* value) => *value;

                    public static async Task<int> M(
                        Task<int> task,
                        int[] values,
                        bool flag)
                    {
                {{output}}
                    }
                }
                """,
                CompilerFeatureOptions.ParseOptions(path),
                assemblyName: "LegacyRuntimeAsyncNestedPointerLocalRoundTrip");
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] BuildMarkedModule(
        int? version,
        bool memberRefConstructor = false,
        bool malformedValue = false,
        int? secondVersion = null)
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
            0,
            metadata.GetOrAddString("MarkerProbe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MarkerProbe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        BlobHandle constructorSignatureHandle =
            metadata.GetOrAddBlob(constructorSignature);

        MethodDefinitionHandle constructor = metadata.AddMethodDefinition(
            System.Reflection.MethodAttributes.Public
                | System.Reflection.MethodAttributes.SpecialName
                | System.Reflection.MethodAttributes.RTSpecialName,
            System.Reflection.MethodImplAttributes.Runtime,
            metadata.GetOrAddString(".ctor"),
            constructorSignatureHandle,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            constructor);
        TypeDefinitionHandle markerType = metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.NotPublic,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("MemorySafetyRulesAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            constructor);

        EntityHandle constructorToken = memberRefConstructor
            ? metadata.AddMemberReference(
                markerType,
                metadata.GetOrAddString(".ctor"),
                constructorSignatureHandle)
            : constructor;

        if (version is { } marker)
        {
            AddMarker(marker, malformedValue);
        }
        if (secondVersion is { } secondMarker)
            AddMarker(secondMarker, malformed: false);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();

        void AddMarker(int marker, bool malformed)
        {
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            if (!malformed)
            {
                value.WriteInt32(marker);
                value.WriteUInt16(0);
            }

            metadata.AddCustomAttribute(
                module,
                constructorToken,
                metadata.GetOrAddBlob(value));
        }
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

    [Fact]
    public void RuntimeAsyncUnsafeSpillBeforeAwait_ClosesUnsafeRunAndBindsFirstProjection()
    {
        string oracleAssembly =
            typeof(ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures).Assembly.Location;
        var options = CompilerFeatureOptions.ParseOptions(oracleAssembly)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
                new KeyValuePair<string, string>("runtime-async", "on"),
            ]);
        using var compiled = Compile(
            """
            using System.Threading.Tasks;

            public static class C
            {
                public static unsafe int* Get() => null;

                public static async Task<int> M(Task<int> task)
                {
                    bool same;
                    unsafe
                    {
                        int* pointer = Get();
                        same = pointer == null;
                    }
                    return (same ? 1 : 0) + await task;
                }
            }
            """,
            options,
            assemblyName: "RuntimeAsyncUnsafeSpill");
        string path = Path.Combine(
            Path.GetTempPath(),
            $"runtime-async-unsafe-spill-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, compiled.Image);
        try
        {
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var function = IrImporter.Import(source, "C", "M");
            Assert.NotNull(function);
            IrPasses.Run(
                function,
                IrPasses.Default,
                PassContext.ForImport(
                    method => IrImporter.Import(source, method)));
            function.CheckInvariant();

            var result = CSharpPrinter.Print(function);
            string output = Assert.IsType<string>(result.Output);

            Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
            Assert.Contains("unsafe(", output);
            Assert.DoesNotContain("unsafe(await", output);
            Assert.DoesNotContain("unsafe\n{", output);
            Assert.DoesNotContain("S_256_1", output);

            var validity = ValidityCheck.Evaluate(
                    path,
                    importSiblingBodies: true,
                    sequential: true)
                .Single(result =>
                    result.TypeName == "C"
                    && result.MethodName == "M");
            Assert.True(validity.SemanticChecked);
            Assert.Empty(validity.MalformedDiagnostics);
            Assert.Empty(validity.SemanticDiagnostics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ClassicAsyncUnsafeStackallocAwait_DeclinesVisibly()
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
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            [module: SkipLocalsInit]

            public static class C
            {
                static Task<int> Read(Span<int> value)
                    => Task.FromResult(value.Length);

                public static async Task<int> M(int length)
                    => await Read(unsafe(stackalloc int[length]));
            }
            """,
            options,
            assemblyName: "ClassicAsyncUnsafeStackalloc");
        using var source = MetadataSource.OpenFromPrefetchedImage(
            "classic-async-unsafe-stackalloc.dll",
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

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "classic async"
                && node.Reason.Contains(
                    "await operand requires unsafe context",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ClassicAsyncInitializedStackallocAwait_RemainsReconstructable()
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
            using System;
            using System.Threading.Tasks;

            public static class C
            {
                static Task<int> Read(Span<int> value)
                    => Task.FromResult(value.Length);

                public static async Task<int> M(int length)
                    => await Read(stackalloc int[length]);
            }
            """,
            options,
            assemblyName: "ClassicAsyncInitializedStackalloc");
        using var source = MetadataSource.OpenFromPrefetchedImage(
            "classic-async-initialized-stackalloc.dll",
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
        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        Assert.DoesNotContain(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "classic async");
        Assert.Contains(
            "await Read(stackalloc int[length])",
            result.Output);
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
        var call = Assert.Single(function.Descendants.OfType<Call>());

        Assert.Equal(MetadataFactState.Unknown, call.Callee.RequiresUnsafeFact);
        Assert.Equal(
            MemorySafetyRulesState.Unsupported,
            call.Callee.MemorySafetyRulesState);
        var sameAssemblyResult = CSharpPrinter.PrintRaised(function);
        Assert.False(sameAssemblyResult.Succeeded);
        Assert.Contains(
            sameAssemblyResult.Diagnostics,
            diagnostic => diagnostic.Id
                == DiagnosticIds.MemorySafetyModeUnavailable);
    }

    [Fact]
    public void UnsupportedDependencyFieldMemorySafetyRules_DeclinesWithoutPromotingDirectAttribute()
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
                public static unsafe int RiskyField;

                public static int Read()
                {
                    unsafe { return RiskyField; }
                }
            }
            """,
            options,
            assemblyName: "UnsupportedFieldRulesLibrary");
        MetadataReference libraryReference =
            MetadataReference.CreateFromImage(library.Image);
        using var caller = Compile(
            """
            public static class Consumer
            {
                public static int M()
                {
                    unsafe { return Library.RiskyField; }
                }
            }
            """,
            options,
            assemblyName: "UnsupportedFieldRulesConsumer",
            additionalReferences: [libraryReference]);

        byte[] unsupportedLibrary =
            WithMemorySafetyRulesVersion(library.Image, version: 99);
        DecompilerResult result = DecompileWithSibling(
            "UnsupportedFieldRulesLibrary.dll",
            unsupportedLibrary,
            "UnsupportedFieldRulesConsumer.dll",
            caller.Image,
            "Consumer",
            "M");

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.DoesNotContain("unsafe", result.Output);

        using var sameAssemblySource =
            MetadataSource.OpenFromPrefetchedImage(
                "UnsupportedFieldRulesLibrary.dll",
                ImmutableArray.Create(unsupportedLibrary));
        var function = IrImporter.Import(
            sameAssemblySource,
            "Library",
            "Read");
        Assert.NotNull(function);
        var field = Assert.Single(
            function.Descendants.OfType<LoadField>()).Field;

        Assert.True(field.HasNormalizedMemorySafetyContract);
        Assert.Equal(MetadataFactState.Unknown, field.RequiresUnsafeFact);
        Assert.Equal(
            MemorySafetyRulesState.Unsupported,
            field.MemorySafetyRulesState);
        var sameAssemblyResult = CSharpPrinter.PrintRaised(function);
        Assert.False(sameAssemblyResult.Succeeded);
        Assert.Contains(
            sameAssemblyResult.Diagnostics,
            diagnostic => diagnostic.Id
                == DiagnosticIds.MemorySafetyModeUnavailable);
    }

    [Fact]
    public void UnsupportedDependencyMemorySafetyRules_WithExpressionRetainsCloneProvenance()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);
        using var library = Compile(
            """
            public record RecordValue
            {
                public int Field;
            }
            """,
            options,
            assemblyName: "UnsupportedRecordLibrary");
        MetadataReference libraryReference =
            MetadataReference.CreateFromImage(library.Image);
        using var caller = Compile(
            """
            public static class Consumer
            {
                public static RecordValue M(
                    RecordValue value,
                    int replacement)
                    => value with { Field = replacement };
            }
            """,
            options,
            assemblyName: "UnsupportedRecordConsumer",
            additionalReferences: [libraryReference]);

        byte[] unsupportedLibrary =
            WithMemorySafetyRulesVersion(library.Image, version: 99);
        DecompilerResult result = DecompileWithSibling(
            "UnsupportedRecordLibrary.dll",
            unsupportedLibrary,
            "UnsupportedRecordConsumer.dll",
            caller.Image,
            "Consumer",
            "M");

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains(" with { Field = replacement }", result.Output);
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

    static ImmutableArray<Diagnostic> CompileDiagnostics(
        string source,
        CSharpParseOptions options)
    {
        var compilation = CSharpCompilation.Create(
            "CompilerFeatureOptionsDiagnostics",
            [CSharpSyntaxTree.ParseText(source, options)],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true));
        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    static string FirstUnsafeBlockBody(string output)
    {
        int keyword = output.IndexOf("unsafe", StringComparison.Ordinal);
        Assert.True(keyword >= 0, "no unsafe block in output:\n" + output);
        int open = output.IndexOf('{', keyword);
        Assert.True(open >= 0);
        int depth = 0;
        for (int i = open; i < output.Length; i++)
        {
            if (output[i] == '{')
                depth++;
            else if (output[i] == '}' && --depth == 0)
                return output[(open + 1)..i];
        }
        throw new Xunit.Sdk.XunitException(
            "unbalanced unsafe block:\n" + output);
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
