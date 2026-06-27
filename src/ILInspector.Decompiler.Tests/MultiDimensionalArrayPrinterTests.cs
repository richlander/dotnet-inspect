using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// Issue #1608: rectangular-array element/creation pseudo-members (Get/Set/Address
// calls and the rank-shaped .ctor) must lower to C# indexer / array-creation
// syntax instead of the unspellable a.Get(i, j) / new int[,](n0, n1) forms.
public class MultiDimensionalArrayPrinterTests
{
    [Theory]
    [InlineData(nameof(RectangularArraySamples.MdGet), "public static int M(int[,] a, int i, int j)", "return a[i, j];")]
    [InlineData(nameof(RectangularArraySamples.MdAddress), "public static ref int M(int[,] a, int i, int j)", "return ref a[i, j];")]
    [InlineData(nameof(RectangularArraySamples.MdNew), "public static int[,] M()", "return new int[3, 4];")]
    [InlineData(nameof(RectangularArraySamples.Md3Get), "public static int M(int[,,] a, int i, int j, int k)", "return a[i, j, k];")]
    public void RectangularElementAndCreationOps_LowerToIndexerSyntax_AndCompile(string methodName, string header, string expected)
        => AssertImportedBodyAndCompile(methodName, header, expected);

    [Fact]
    public void RectangularSet_LowersToIndexerAssignment_AndCompiles()
    {
        string body = RenderFixture(nameof(RectangularArraySamples.MdSet));
        Assert.Contains("a[i, j] = v;", body);
        DoesNotContainPseudoMembers(body);
        AssertCompiles("public static void M(int[,] a, int i, int j, int v)", body);
    }

    [Fact]
    public void RectangularAddress_PassedByRefAndOut_LowersToIndexerLvalue_AndCompiles()
    {
        string refBody = RenderFixture(nameof(RectangularArraySamples.MdRefArg));
        Assert.Contains("ref a[i, j]", refBody);
        DoesNotContainPseudoMembers(refBody);

        string outBody = RenderFixture(nameof(RectangularArraySamples.MdOutArg));
        Assert.Contains("out a[i, j]", outBody);
        DoesNotContainPseudoMembers(outBody);
    }

    [Theory]
    [InlineData(nameof(RectangularArraySamples.MdLength), "a.GetLength(0)")]
    [InlineData(nameof(RectangularArraySamples.MdRank), "a.Rank")]
    [InlineData(nameof(RectangularArraySamples.SingleDim), "a[i]")]
    [InlineData(nameof(RectangularArraySamples.Jagged), "a[i][j]")]
    public void NonRewrittenArrayShapes_StayUnchanged(string methodName, string expectedFragment)
    {
        string body = RenderFixture(methodName);
        Assert.Contains(expectedFragment, body);
    }

    // A user type that happens to declare Get/Set is NOT a rectangular array, so
    // its calls must keep the ordinary receiver.Method(args) spelling.
    [Theory]
    [InlineData(nameof(UserGridCalls.UserGet), "g.Get(i, j)")]
    [InlineData(nameof(UserGridCalls.UserSet), "g.Set(i, j, v)")]
    public void UserDeclaredGetSet_AreNotRewrittenAsIndexers(string methodName, string expectedCall)
    {
        var type = typeof(UserGridCalls).FullName!;
        string body = RenderFixture(methodName, type);
        Assert.Contains(expectedCall, body);
    }

    static void AssertImportedBodyAndCompile(string methodName, string header, string expected)
    {
        string body = RenderFixture(methodName).Trim();
        Assert.Equal(expected, body);
        DoesNotContainPseudoMembers(body);
        AssertCompiles(header, body);
    }

    static void DoesNotContainPseudoMembers(string body)
    {
        Assert.DoesNotContain(".Get(", body);
        Assert.DoesNotContain(".Set(", body);
        Assert.DoesNotContain(".Address(", body);
        Assert.DoesNotContain("[,](", body);
    }

    static string RenderFixture(string methodName, string? declaringType = null)
    {
        using var source = MetadataSource.Open(typeof(RectangularArraySamples).Assembly.Location);
        var function = IrImporter.Import(source, declaringType ?? typeof(RectangularArraySamples).FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
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
