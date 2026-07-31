using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Issue #1759: a reference-typed value from `as`/isinst tested for truthiness in
// a branch must render `is null`/`is not null`, not boolean negation. `!x` on an
// IDisposable is CS0023. The reference classification comes from the isinst
// provenance because the interface type is cross-assembly (TypeShape.Unknown).
public class FinallyDisposePrinterTests
{
    [Fact]
    public void ReferenceTruthinessInFinally_RendersIsNull_NotBangOperator()
    {
        string body = RenderFixture(nameof(FinallyDisposeSamples.DisposeEnumeratorInFinally));

        Assert.Contains("is null", body);
        Assert.DoesNotContain("!S_", body);
        Assert.DoesNotContain("!enumerator", body);
        AssertCompiles("public static void M(System.Collections.IDictionary dictionary)", body);
    }

    [Fact]
    public void ReferenceTruthiness_NestedFunctionReusesSlot_StillRendersIsNull()
    {
        // A nested local function reuses the same stack-slot number; provenance is
        // scoped to the current function body, so the outer finally-dispose slot
        // is still recognized as isinst-produced and renders `is null` (#1759 review).
        using var source = MetadataSource.Open(typeof(FinallyDisposeNestedSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(FinallyDisposeNestedSamples).FullName!,
            nameof(FinallyDisposeNestedSamples.DisposeWithNestedLocalFunction));
        Assert.NotNull(function);
        var body = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method)).Output!;
        // Asserted after raising, which is the state the rendered output describes: the
        // nested local function is raised into a declaration here, so nothing degrades.
        // Before raising the body still holds a Call to the synthesized <...>g__ method,
        // which is honestly Partial — that is the #3631 contract.
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);

        Assert.Contains("is null", body);
        Assert.DoesNotContain("!S_", body);
    }

    static string RenderFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(FinallyDisposeSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(FinallyDisposeSamples).FullName!, methodName);
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static void AssertCompiles(string header, string body)
    {
        var errors = Recompile(header, body)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(errors.Length == 0, "Rendered body must compile, got:\n  " + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body)
    {
        string source = $$"""
            using System;
            using System.Collections;
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;
}
