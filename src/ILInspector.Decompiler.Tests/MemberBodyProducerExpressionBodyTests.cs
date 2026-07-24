using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Issue #3088: a method (or accessor) whose entire body is a single
// `return <switch-expression>;` renders as an expression-bodied member
// (`head => <scrutinee> switch { ... };`) instead of a brace block. The
// signal is computed structurally in the printer (BodyIsSingleReturnExpression)
// from the raised IR and threaded to the CSharp layout layer, which owns the
// block-vs-expression-body decision and re-indents the switch block under the
// member. These fixtures compile real C#, decompile the full member, and assert
// the end-to-end shape — including the close negative where a preceding
// statement forces the block form to remain.
[Trait("Area", "RoundTrip")]
public class MemberBodyProducerExpressionBodyTests
{
    [Fact]
    public void SingleSwitchReturn_RendersExpressionBodied()
    {
        using var assembly = Compile("""
            public class Fx
            {
                public static int Area(object shape) => shape switch
                {
                    string s => s.Length,
                    int[] a => a.Length,
                    _ => 0,
                };
            }
            """);

        string source = ComposeType(assembly.Path, "Fx");

        // Expression-bodied member: arrow on the header line, switch block
        // re-indented under the member (member at indent 4, arms at indent 8,
        // closing `};` back at indent 4).
        Assert.Contains(
            """
                public static int Area(object shape) => shape switch
                {
                    string V_0 => V_0.Length,
                    int[] V_1 => V_1.Length,
                    _ => 0,
                };
            """.ReplaceLineEndings("\n").Trim('\n'),
            source);

        // The old block form must be gone.
        Assert.DoesNotContain("return shape switch", source);
        Assert.DoesNotContain("return V_0 switch", source);
    }

    [Fact]
    public void SwitchReturnAfterAnotherStatement_StaysBlock()
    {
        using var assembly = Compile("""
            public class Fx
            {
                public static int TwoStatements(object shape, out bool matched)
                {
                    matched = shape is string;
                    return shape switch
                    {
                        string s => s.Length,
                        int[] a => a.Length,
                        _ => 0,
                    };
                }
            }
            """);

        string source = ComposeType(assembly.Path, "Fx");

        // A method with a statement preceding the switch return is not a single
        // return expression, so it must keep the brace-block body (arrow header
        // is absent; the body opens with `{` on the next line).
        Assert.Contains("public static int TwoStatements(object shape, out bool matched)\n    {", source);
        Assert.DoesNotContain("TwoStatements(object shape, out bool matched) =>", source);
    }

    static string ComposeType(string path, string fullName)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(surface.Types, t => t.FullName == fullName);
        var source = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(source);
        return source!.ReplaceLineEndings("\n");
    }

    static TempAssembly Compile(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-exprbody-{Guid.NewGuid():N}.dll");
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return new TempAssembly(path);
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;

    sealed class TempAssembly(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            try { File.Delete(Path); }
            catch { }
        }
    }
}
