using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

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
    // The printer keys on module marker presence, not version, so the harness
    // must replay updated rules for any marked module. A v2-only harness would
    // compile printer-emitted `unsafe { }` blocks under legacy rules (CS0214)
    // and report a false clean.
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    public void AnyModuleMarker_ReplaysUpdatedRules(int version)
    {
        using var pe = new PEReader(new MemoryStream(BuildMarkedModule(version)));
        var options = CompilerFeatureOptions.ParseOptions(pe);

        Assert.Contains(
            options.Features,
            feature => feature.Key == "updated-memory-safety-rules"
                && feature.Value == "true");
    }

    [Fact]
    public void UnmarkedModule_CompilesAsLegacy()
    {
        using var pe = new PEReader(new MemoryStream(BuildMarkedModule(null)));
        var options = CompilerFeatureOptions.ParseOptions(pe);

        Assert.DoesNotContain(
            options.Features,
            feature => feature.Key == "updated-memory-safety-rules");
    }

    public static TheoryData<string, byte[]> ReplayModeImages() => new()
    {
        { "unmarked", BuildMarkedModule(null) },
        { "v2 MethodDef ctor", BuildMarkedModule(2) },
        { "v3 MethodDef ctor", BuildMarkedModule(3) },
        { "v99 MethodDef ctor", BuildMarkedModule(99) },
        // ECMA-335 lets a MemberRef name a member of a same-module TypeDef, a
        // spelling the printer's constructor decode does not recognize. Reading
        // the marker through any other decoder diverges here.
        { "v2 MemberRef->TypeDef ctor", BuildMarkedModule(2, memberRefConstructor: true) },
        // A truncated fixed-arg blob leaves the marker present but its version
        // undecodable, which version-decoding readers report as absent.
        { "malformed marker blob", BuildMarkedModule(2, malformedValue: true) },
    };

    /// <summary>
    /// The harness must recompile under the exact mode the printer used. This
    /// pins the two predicates together over marker spellings where an
    /// independent reader is known to disagree with the printer, so the harness
    /// cannot silently drift back to deriving the mode on its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReplayModeImages))]
    public void HarnessReplayMatchesPrinterMode(string label, byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        bool printerUsesUpdatedRules =
            IrImporter.ModuleUsesUpdatedMemorySafetyRules(pe.GetMetadataReader());
        bool harnessReplaysUpdatedRules = CompilerFeatureOptions.ParseOptions(pe).Features
            .Any(feature => feature.Key == "updated-memory-safety-rules"
                && feature.Value == "true");

        Assert.True(
            printerUsesUpdatedRules == harnessReplaysUpdatedRules,
            $"{label}: printer replayed updated rules = {printerUsesUpdatedRules}, "
                + $"harness replayed updated rules = {harnessReplaysUpdatedRules}");
    }

    static byte[] BuildMarkedModule(
        int? version,
        bool memberRefConstructor = false,
        bool malformedValue = false)
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
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            if (!malformedValue)
            {
                value.WriteInt32(marker);
                value.WriteUInt16(0);
            }

            metadata.AddCustomAttribute(
                module,
                constructorToken,
                metadata.GetOrAddBlob(value));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
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

        public void Dispose()
        {
            PE.Dispose();
            stream.Dispose();
        }
    }
}
