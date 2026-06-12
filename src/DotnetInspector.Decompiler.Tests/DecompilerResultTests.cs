using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Decompiler;

namespace DotnetInspector.Decompiler.Tests;

public class DecompilerResultTests
{
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

    /// <summary>A context whose IL is a truncated two-byte opcode — the reader throws in both configurations.</summary>
    static MethodBodyContext CorruptContext()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        return new MethodBodyContext(
            ilBytes: [0xFE],  // extended-opcode prefix with no second byte
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
