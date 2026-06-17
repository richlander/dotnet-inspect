using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Tests;

public class DecompilerResultTests : IDisposable
{
    readonly Stack<IDisposable> _disposables = new();

    public void Dispose()
    {
        while (_disposables.Count > 0)
            _disposables.Pop().Dispose();
    }

    [Fact]
    public void Decompile_ValidMethod_ReportsFullFidelity()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(peReader, "System.String", "IsNullOrEmpty");
        Assert.NotNull(context);

        var result = CSharpEmitter.Decompile(context);

        Assert.True(result.Succeeded);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(CSharpEmitter.Emit(context), result.Output);
    }

    [Fact]
    public void Decompile_CorruptIL_FailsWithInternalErrorDiagnostic()
    {
        var result = CSharpEmitter.Decompile(CorruptContext());

        Assert.False(result.Succeeded);
        Assert.Null(result.Output);
        Assert.Equal(DecompilationFidelity.Failed, result.Fidelity);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticIds.InternalError, diagnostic.Id);
        Assert.Contains(diagnostic.Id, diagnostic.ToString());
    }

    [Fact]
    public void AnnotatedDecompile_ValidMethod_MatchesEmit()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(peReader, "System.String", "IsNullOrEmpty");
        Assert.NotNull(context);

        var result = AnnotatedILEmitter.Decompile(context, ILAnnotationDepth.Structured);

        Assert.True(result.Succeeded);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(AnnotatedILEmitter.Emit(context, ILAnnotationDepth.Structured), result.Output);
    }

    [Fact]
    public void EmptyBody_IsValidCSharpProjection_NotAFailure()
    {
        // 'void M() { }' legitimately renders an empty body; only projections
        // that always have output (the IL views) treat emptiness as DEC0003.
        var stream = File.OpenRead(typeof(CfgSampleClass).Assembly.Location);
        var peReader = new PEReader(stream);
        _disposables.Push(peReader);
        _disposables.Push(stream);
        var context = MethodBodyContext.Create(
            peReader, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Noop));
        Assert.NotNull(context);

        var csharp = CSharpEmitter.Decompile(context);
        var annotated = AnnotatedILEmitter.Decompile(context);

        Assert.True(csharp.Succeeded);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(annotated.Succeeded);
        Assert.NotEqual("", annotated.Output!.Trim());
    }

    [Fact]
    public void RawProjection_SurvivesAnalysisFailures()
    {
        // 'add' on an empty stack: stack simulation cannot make sense of
        // this, but the raw IL projection is the fallback layer and must
        // render without touching CFG or stack analysis.
        var analysis = MethodAnalysis.Create(ContextWithIL([0x58, 0x2A]));  // add; ret

        var raw = AnnotatedILEmitter.Decompile(analysis, ILAnnotationDepth.Raw);

        Assert.True(raw.Succeeded);
        Assert.Contains("add", raw.Output);
    }

    /// <summary>A context whose IL is a truncated two-byte opcode — the reader throws in both configurations.</summary>
    MethodBodyContext CorruptContext() => ContextWithIL([0xFE]);

    MethodBodyContext ContextWithIL(byte[] ilBytes)
    {
        // The PEReader must outlive the context: MetadataReader points into
        // its memory block (disposing it under a live context is a
        // use-after-free that crashes the process, not a test failure).
        var stream = File.OpenRead(typeof(object).Assembly.Location);
        var peReader = new PEReader(stream);
        _disposables.Push(peReader);
        _disposables.Push(stream);
        var reader = peReader.GetMetadataReader();
        return new MethodBodyContext(
            ilBytes: ilBytes,
            exceptionRegions: ImmutableArray<ExceptionRegion>.Empty,
            maxStack: 8,
            localTypes: [],
            reader: reader,
            parameterCount: 0,
            hasThis: false,
            hasReturnValue: false,
            parameterTypes: [],
            parameterNames: [],
            returnType: "void");
    }
}

public class TypeSourceComposerTests
{
    [Fact]
    public void Compose_WholeType_RendersDeclarationFieldsAndBodies()
    {
        string dll = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Collections.dll");
        Metadata.ApiType type;
        using (var stream = File.OpenRead(dll))
        using (var peReader = new PEReader(stream))
        {
            var surface = Metadata.ApiSurfaceExtractor.Extract(peReader);
            type = surface.Types.Single(t => t.FullName == "System.Collections.Generic.Stack`1");
        }

        string? listing = TypeSourceComposer.Compose(type, dll, pdbPath: null);

        Assert.NotNull(listing);
        Assert.Contains("namespace System.Collections.Generic;", listing);
        Assert.Contains("private T[] _array;", listing);
        Assert.Contains("_array = Array.Empty<T>();", listing);
    }

    [Fact]
    public void Compose_FlagsEnum_EmitsTypeAttribute()
    {
        // AttributeTargets is a [Flags] enum in CoreLib; the type-level
        // attribute renders short above the declaration.
        string dll = typeof(object).Assembly.Location;
        Metadata.ApiType type;
        using (var stream = File.OpenRead(dll))
        using (var peReader = new PEReader(stream))
            type = Metadata.ApiSurfaceExtractor.Extract(peReader).Types.Single(t => t.FullName == "System.AttributeTargets");

        string? listing = TypeSourceComposer.Compose(type, dll, pdbPath: null);

        Assert.NotNull(listing);
        Assert.Contains("[Flags]", listing);
        Assert.Contains("enum AttributeTargets", listing);
    }

    [Fact]
    public void Compose_Method_EmitsAttribute()
    {
        // Buffer.MemoryCopy carries [CLSCompliant(false)]; the method-level
        // attribute renders above the signature with its bool argument.
        string dll = typeof(object).Assembly.Location;
        Metadata.ApiType type;
        using (var stream = File.OpenRead(dll))
        using (var peReader = new PEReader(stream))
            type = Metadata.ApiSurfaceExtractor.Extract(peReader).Types.Single(t => t.FullName == "System.Buffer");

        string? listing = TypeSourceComposer.Compose(type, dll, pdbPath: null);

        Assert.NotNull(listing);
        Assert.Contains("[CLSCompliant(false)]", listing);
    }
}

public class MethodAnalysisTests : IDisposable
{
    readonly Stack<IDisposable> _disposables = new();

    public void Dispose()
    {
        while (_disposables.Count > 0)
            _disposables.Pop().Dispose();
    }

    MethodBodyContext CoreLibContext(string type, string method)
    {
        // PEReader kept alive for the test's lifetime — the context's
        // MetadataReader points into its memory block.
        var stream = File.OpenRead(typeof(object).Assembly.Location);
        var peReader = new PEReader(stream);
        _disposables.Push(peReader);
        _disposables.Push(stream);
        var context = MethodBodyContext.Create(peReader, type, method);
        Assert.NotNull(context);
        return context;
    }

    [Fact]
    public void SharedAnalysis_ProducesSameOutputsAsDirectPaths()
    {
        var context = CoreLibContext("System.String", "IsNullOrWhiteSpace");
        var analysis = MethodAnalysis.Create(context);

        Assert.Equal(CSharpEmitter.Emit(context), CSharpEmitter.Emit(analysis));
        Assert.Equal(
            AnnotatedILEmitter.Emit(context, ILAnnotationDepth.Structured),
            AnnotatedILEmitter.Emit(analysis, ILAnnotationDepth.Structured));
    }

    [Fact]
    public void Stages_ComputeOnce()
    {
        var analysis = MethodAnalysis.Create(CoreLibContext("System.String", "IsNullOrEmpty"));

        Assert.Same(analysis.Cfg, analysis.Cfg);
        Assert.Same(analysis.Stack, analysis.Stack);
        Assert.Same(analysis.Ast, analysis.Ast);
        Assert.Same(analysis.Structure, analysis.Structure);
    }

    [Fact]
    public void AnnotatedIL_FromSharedAnalysis_UnaffectedByCSharpEmission()
    {
        // The IL projection must be identical whether or not the C# emitter
        // (and its raising transforms) ran first against the same analysis.
        var context = CoreLibContext("System.String", "IsNullOrWhiteSpace");
        string ilAlone = AnnotatedILEmitter.Emit(
            MethodAnalysis.Create(context), ILAnnotationDepth.Structured);

        var shared = MethodAnalysis.Create(context);
        _ = CSharpEmitter.Emit(shared);
        string ilAfterCSharp = AnnotatedILEmitter.Emit(shared, ILAnnotationDepth.Structured);

        Assert.Equal(ilAlone, ilAfterCSharp);
    }
}
