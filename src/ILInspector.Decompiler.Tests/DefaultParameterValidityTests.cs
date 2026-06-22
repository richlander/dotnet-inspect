using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class DefaultParameterValidityTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.DefaultParameterFixtures";

    [Fact]
    public void DefaultParameterMatrix_ReportsCurrentInvalidHeaders()
    {
        using var stream = File.OpenRead(typeof(DefaultParameterFixtures).Assembly.Location);
        using var pe = new PEReader(stream);
        var fixture = Assert.Single(ApiSurfaceExtractor.Extract(pe, includeAll: true).Types,
            t => t.FullName == FixtureType);
        var results = fixture.Members
            .Where(m => m.Kind == "method")
            .ToDictionary(m => m.Name, CompileSignature, StringComparer.Ordinal);

        AssertInvalid(results, nameof(DefaultParameterFixtures.CharControlDefault));
        AssertInvalid(results, nameof(DefaultParameterFixtures.DecimalDefault), "SIGDEFAULT");

        AssertClean(results, nameof(DefaultParameterFixtures.ValueTypeStructDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.EnumNonZeroDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.EnumZeroDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.CharPrintableDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.StringSpecialDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.StringPlainDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.NullableValueDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.ReferenceTypeDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.IntDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.BoolDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.DoubleDefault));
        AssertClean(results, nameof(DefaultParameterFixtures.FloatDefault));

        var invalid = results
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(DefaultParameterFixtures.CharControlDefault),
                nameof(DefaultParameterFixtures.DecimalDefault),
            ],
            invalid);
    }

    static List<string> CompileSignature(ApiMember member)
    {
        var diagnostics = new List<string>();
        if (member.Signature is null)
            return diagnostics;

        if (member.Name == nameof(DefaultParameterFixtures.DecimalDefault)
            && !member.Signature.Contains('='))
        {
            diagnostics.Add("SIGDEFAULT");
            return diagnostics;
        }

        string source = $$"""
            #pragma warning disable
            using System;
            using System.Threading;
            class __DefaultParameterSignatureShell
            {
                public static {{member.Signature}} => default;
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        diagnostics.AddRange(tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id));
        if (diagnostics.Count > 0)
            return diagnostics;

        var compilation = CSharpCompilation.Create(
            "default-parameter-signature-check",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        diagnostics.AddRange(compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id));
        return diagnostics;
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return builder.ToImmutable();
    }

    static void AssertInvalid(
        IReadOnlyDictionary<string, List<string>> results,
        string method,
        params string[] expectedCodes)
    {
        Assert.True(results.TryGetValue(method, out var codes), $"Missing fixture result for {method}.");
        Assert.NotEmpty(codes);
        if (expectedCodes.Length > 0)
            Assert.Contains(expectedCodes, codes.Contains);
    }

    static void AssertClean(IReadOnlyDictionary<string, List<string>> results, string method)
    {
        Assert.True(results.TryGetValue(method, out var codes), $"Missing fixture result for {method}.");
        Assert.Empty(codes);
    }
}
