using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class FidelityCheckGeneratedFilterTests
{
    [Fact]
    public void Evaluate_SkipsGeneratedCodeTypesAndMethods()
    {
        var assemblyPath = CompileFixture("""
            using System.CodeDom.Compiler;

            public class Normal
            {
                public int Echo(int value) => value + 1;
            }

            [GeneratedCode("fixture", "1.0")]
            public class GeneratedType
            {
                public int Hidden() => 42;
            }

            public class Mixed
            {
                [GeneratedCode("fixture", "1.0")]
                public int Hidden() => 42;

                public int Visible() => 7;
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "Normal" && result.Method == "Echo");
            Assert.Contains(results, result => result.Type == "Mixed" && result.Method == "Visible");
            Assert.DoesNotContain(results, result => result.Type == "GeneratedType");
            Assert.DoesNotContain(results, result => result.Type == "Mixed" && result.Method == "Hidden");
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_IncludesCompilerGeneratedAutoPropertyAccessors()
    {
        var assemblyPath = CompileFixture("""
            public class AutoPropertyFixture
            {
                public AutoPropertyFixture(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "AutoPropertyFixture" && result.Method == ".ctor");
            Assert.Contains(results, result => result.Type == "AutoPropertyFixture" && result.Method == "get_Value");
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_SkipsCompilerGeneratedRecordMethodsButKeepsAccessors()
    {
        var assemblyPath = CompileFixture("""
            public record GeneratedRecord(int Value);
            """);
        try
        {
            var results = FidelityCheck.Evaluate(assemblyPath);

            Assert.Contains(results, result => result.Type == "GeneratedRecord" && result.Method == "get_Value");
            Assert.DoesNotContain(results, result =>
                result.Type == "GeneratedRecord" &&
                result.Method is "ToString" or "PrintMembers" or "GetHashCode");
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsConstructorAssignedAutoProperties()
    {
        var assemblyPath = CompileFixture("""
            public class AutoPropertyPairFixture
            {
                public AutoPropertyPairFixture(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Left { get; }
                public int Right { get; }
            }
            """);
        try
        {
            var ctor = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "AutoPropertyPairFixture" && result.Method == ".ctor");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, ctor.Status);
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public void Evaluate_RoundTripsStructObjectToStringDispatch()
    {
        var assemblyPath = CompileFixture("""
            public readonly struct StructWithToString
            {
                public override string ToString() => "value";
            }

            public static class StructWithToStringExtensions
            {
                public static string Humanize(this StructWithToString value, string? format)
                {
                    if (!string.IsNullOrWhiteSpace(format))
                        return format;
                    return value.ToString();
                }
            }
            """);
        try
        {
            var result = Assert.Single(
                FidelityCheck.Evaluate(assemblyPath),
                result => result.Type == "StructWithToStringExtensions" && result.Method == "Humanize");

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    static string CompileFixture(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fidelity-generated-filter-{Guid.NewGuid():N}.dll");
        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable));

        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }
}
