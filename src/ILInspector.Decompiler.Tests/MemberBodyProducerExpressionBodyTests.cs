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
// signal is computed structurally in the printer (BodyIsSingleExpressionBody)
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

    [Fact]
    public void SingleWrappedFluentReturn_RendersExpressionBodied()
    {
        // Issue #3084: the single-return expression-body fold is not
        // switch-specific. A method whose whole body is one `return <fluent
        // chain>;` wide enough to wrap also folds to an expression-bodied
        // member — the arrow trails the signature with the chain receiver after
        // it, continuations one level deeper.
        using var assembly = Compile("""
            public class Fx
            {
                public static string Build(System.Text.StringBuilder builder)
                {
                    return builder.Append("alphabet").Append("bravissimo").Append("charlateral").Append("deltatango").Append("echolocation").Append("foxtrotter").ToString();
                }
            }
            """);

        string source = ComposeType(assembly.Path, "Fx");

        Assert.Contains("public static string Build(StringBuilder builder) => builder", source);
        Assert.Contains("\n        .Append(\"alphabet\")", source);
        Assert.Contains("\n        .ToString();", source);
        // The old block form (a brace block wrapping a lone `return`) is gone.
        Assert.DoesNotContain("return builder", source);
        Assert.DoesNotContain("Build(StringBuilder builder)\n    {", source);
    }

    [Fact]
    public void WrappedFluentReturnAfterAnotherStatement_StaysBlock()
    {
        // Close negative: a statement preceding the wrapped return makes the
        // body two statements, not a single return expression, so it keeps the
        // brace-block body.
        using var assembly = Compile("""
            public class Fx
            {
                public static string Build(System.Text.StringBuilder builder)
                {
                    builder.Append("prefix");
                    return builder.Append("alphabet").Append("bravissimo").Append("charlateral").Append("deltatango").Append("echolocation").Append("foxtrotter").ToString();
                }
            }
            """);

        string source = ComposeType(assembly.Path, "Fx");

        Assert.Contains("public static string Build(StringBuilder builder)\n    {", source);
        Assert.DoesNotContain("Build(StringBuilder builder) =>", source);
        Assert.Contains("return builder", source);
    }

    [Fact]
    public void SingleVoidFluentExpressionStatement_RendersExpressionBodied()
    {
        // Issue #3084 (this slice): a void method whose whole body is one
        // expression statement — a fluent call chain wide enough to wrap — folds
        // to an expression-bodied member too. There is no `return`, so the arrow
        // trails the signature with the chain receiver after it and the chained
        // calls follow one level deeper.
        using var assembly = Compile("""
            public class Fx
            {
                public static void Build(System.Text.StringBuilder builder)
                {
                    builder.Append("alphabet").Append("bravissimo").Append("charlateral").Append("deltatango").Append("echolocation").Append("foxtrotter");
                }
            }
            """);

        string source = ComposeType(assembly.Path, "Fx");

        Assert.Contains("public static void Build(StringBuilder builder) => builder", source);
        Assert.Contains("\n        .Append(\"alphabet\")", source);
        Assert.Contains("\n        .Append(\"foxtrotter\");", source);
        // The old block form (a brace block wrapping the lone statement) is gone.
        Assert.DoesNotContain("Build(StringBuilder builder)\n    {", source);
    }

    [Fact]
    public void VoidFluentExpressionStatementAfterAnotherStatement_StaysBlock()
    {
        // Close negative: a statement preceding the fluent chain makes the body
        // two statements, not a single expression body, so it keeps the
        // brace-block form.
        using var assembly = Compile("""
            public class Fx
            {
                public static void Build(System.Text.StringBuilder builder)
                {
                    builder.Clear();
                    builder.Append("alphabet").Append("bravissimo").Append("charlateral").Append("deltatango").Append("echolocation").Append("foxtrotter");
                }
            }
            """);

        string source = ComposeType(assembly.Path, "Fx");

        Assert.Contains("public static void Build(StringBuilder builder)\n    {", source);
        Assert.DoesNotContain("Build(StringBuilder builder) =>", source);
    }

    [Fact]
    public void SingleStackallocPointerReturn_StaysBlock()
    {
        // GPT review of #3141: a lone `return stackalloc ...;` whose value the
        // printer expands into a lifted local declaration (inside an `unsafe`
        // block) is still one top-level Return, and its output is multi-line, so
        // it satisfied the earlier flag guard. But its printed body does not
        // begin with a bare `return `, so it is not a foldable expression body.
        // The text guard keeps BodyIsSingleExpressionBody off, matching what
        // MultilineExpressionBodyLines would accept, and the member stays a
        // brace block. (Output was already correct via downstream re-gating;
        // this locks the member framing so a future guard relaxation cannot
        // silently fold a multi-statement expansion.)
        using var assembly = Compile("""
            public unsafe class Fx
            {
                public static int* Grab()
                {
                    unsafe
                    {
                        int* p = stackalloc int[10];
                        return p;
                    }
                }
            }
            """, allowUnsafe: true);

        string source = ComposeType(assembly.Path, "Fx");

        // Brace-block body (arrow header absent; body opens with `{`), with the
        // lifted stackalloc declaration inside.
        Assert.DoesNotContain("Grab() =>", source);
        Assert.Contains("Grab()\n    {", source);
        Assert.Contains("stackalloc byte[40]", source);
        Assert.Contains("return (int*)__stackalloc;", source);
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

    static TempAssembly Compile(string source, bool allowUnsafe = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-exprbody-{Guid.NewGuid():N}.dll");
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: allowUnsafe,
                nullableContextOptions: NullableContextOptions.Enable));

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
