using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 3 completion guard for the decompiler product quality ladder (#1599):
/// C# 6 sugar. The scoped bar is deliberately explicit:
/// null propagation and simple interpolation are measured as method-body
/// recoveries. Expression-bodied declarations are IL-identical to block-bodied
/// members, so this guard treats their arrow spelling as a whole-type
/// type-source rendering preference, not as independent source recovery proof.
/// </summary>
public class LadderRung3GateTests
{
    static string FixturePath => typeof(LadderRung3.Program).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung3.Program).FullName!;

    static readonly string[] ExpectedMembers =
    [
        ".ctor", "Describe", "Format", "Greet", "Main", "get_Prefix",
    ];

    [Fact]
    public void Rung3Fixture_ExposesExactMemberSet_AllFullAndFullyRaised()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        var notFull = members
            .Where(m => m.Function.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Rung 3 requires every scoped fixture member to render Full; not Full: " + string.Join(", ", notFull));

        var gaps = members
            .Where(m => Completeness.Residual(m.Function) is not null)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Rung 3 requires every scoped fixture member to be fully raised (zero residual gaps); gaps: " + string.Join(", ", gaps));
    }

    [Fact]
    public void Rung3Fixture_RendersCSharp6MethodBodySugar()
    {
        var members = LoadRaisedMembers();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        Assert.Equal("return \"Hello\";", Body("get_Prefix"));

        var describe = Body("Describe");
        Assert.Contains("node?.Label ?? \"none\"", describe);
        Assert.DoesNotContain("goto", describe);

        var format = Body("Format");
        Assert.Contains("return $\"Count={count}; Label={label}\";", format);
        Assert.DoesNotContain("DefaultInterpolatedStringHandler", format);

        var greet = Body("Greet");
        Assert.Contains("Prefix", greet);
        Assert.Contains("node?.Label ?? \"guest\"", greet);
    }

    [Fact]
    public void Rung3Fixture_RendersExpressionBodiedTypeSourceStyle()
    {
        var (type, source) = ComposeFixtureType();

        var deltas = TypeSourceCheck.CompareType(type, source);
        Assert.DoesNotContain(deltas, d => d.Kind == TypeSourceCheck.Deltas.MemberMissing);

        Assert.Contains("public string Prefix => \"Hello\";", source);
        Assert.Contains("public static string Describe(Node node) => node?.Label ?? \"none\";", source);
        Assert.Contains("public static string Format(int count, string label) => $\"Count={count}; Label={label}\";", source);
    }

    [Fact]
    public void Rung3Fixture_HasNoMalformedOrSemanticDefects()
    {
        var results = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName == FixtureType)
            .ToList();

        Assert.NotEmpty(results);

        var malformed = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformed.Length == 0,
            "Rung 3 requires zero malformed Full methods; malformed: " + string.Join("; ", malformed));

        var semantic = results
            .Where(r => r.HasSemanticDefect)
            .Select(r => $"{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semantic.Length == 0,
            "Rung 3 requires zero semantic-binding defects; defects: " + string.Join("; ", semantic));

        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.MethodName} was not semantically bound."));
    }

    static (ApiType Type, string Source) ComposeFixtureType()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == FixtureType);
        var source = TypeSourceComposer.Compose(type, FixturePath, pdbPath: null);
        Assert.NotNull(source);
        return (type, source);
    }

    static List<(string Name, IrFunction Function)> LoadRaisedMembers()
    {
        var members = new List<(string Name, IrFunction Function)>();
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != FixtureType)
                continue;
            IrPasses.Run(function);
            members.Add((methodName, function));
        }
        return members;
    }
}
