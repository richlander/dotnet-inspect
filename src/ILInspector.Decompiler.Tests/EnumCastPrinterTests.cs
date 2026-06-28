using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Issues #1766 / #1772: a cross-assembly (framework) enum resolves to
// TypeShape.Unknown, so an integer constant flowing into it renders as a bare
// int — `int->enum` in a conditional arm (CS0266) or `enum |= int` in a bitwise
// compound (CS0019) — while the method is still graded Full. The printer must
// cast the integer to the enum structurally.
public class EnumCastPrinterTests
{
    [Fact]
    public void EnumConstantConditionalArms_IntoCrossAssemblyEnum_CastsEachArm()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumConditional));

        Assert.Contains("(StringComparison)4", body);
        Assert.Contains("(StringComparison)5", body);
        Assert.DoesNotContain("? 4 : 5", body);
        AssertCompiles("public static bool M(string name, bool ci)", body);
    }

    [Fact]
    public void BitwiseCompound_IntoCrossAssemblyFlagsEnum_CastsRightOperand()
    {
        string body = RenderFixture(nameof(EnumCastSamples.EnumFlagsCompound));

        Assert.Contains("|= (AttributeTargets)4", body);
        Assert.Contains("|= (AttributeTargets)8", body);
        Assert.DoesNotContain("|= 4", body);
        Assert.DoesNotContain("|= 8", body);
        AssertCompiles("public static AttributeTargets M(bool a, bool b)", body);
    }

    static string RenderFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(EnumCastSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(EnumCastSamples).FullName!, methodName);
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
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return references.ToImmutable();
    }
}
