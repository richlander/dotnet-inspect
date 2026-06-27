using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 2 completion guard for the decompiler product quality ladder (#1599):
/// C# 2-3 structural basics. It decompiles the in-repo <see cref="LadderRung2"/>
/// fixture and pins the scoped rung 2 bar across the full capability surface so a
/// regression below it fails fast in PR CI:
/// <list type="bullet">
/// <item>the fixture exposes exactly the rung 2 member set across all three
/// user-authored types — the <c>Program</c> driver, the generic <c>Box&lt;T&gt;</c>,
/// and the extension class — so no construct is silently dropped;</item>
/// <item>every member except the anonymous-type frontier renders <c>Full</c>;</item>
/// <item>every member is fully raised — zero residual control flow;</item>
/// <item>the rendered C# is recognizable for the rung 2 constructs: object and
/// collection initializers (#1624), anonymous-type locals (#1625), nullable
/// value-type coalesce (#1626), generic property/method bodies, and instance-style
/// extension-call spelling;</item>
/// <item><c>--validity-check</c> sees zero malformed <c>Full</c> methods and zero
/// non-noise semantic-binding defects across the fixture.</item>
/// </list>
/// <c>AnonymousSummary</c> is pinned as the rung 2 honest-degrade frontier: the
/// method body raises to a recognizable <c>var info = new { ... }</c> local, but the
/// member stays <c>Partial</c> because the compiler-generated anonymous type is not
/// source-recoverable at the type-declaration level. A future change that recovers it
/// to <c>Full</c> should update this guard deliberately.
/// </summary>
public class LadderRung2GateTests
{
    static string FixturePath => typeof(LadderRung2.Program).Assembly.Location;

    static readonly string ProgramType = typeof(LadderRung2.Program).FullName!;
    static readonly string BoxType = typeof(LadderRung2.Box<>).FullName!;
    static readonly string ExtensionsType = typeof(LadderRung2.LadderRung2Extensions).FullName!;
    static readonly HashSet<string> FixtureTypes = [ProgramType, BoxType, ExtensionsType];

    static readonly Lazy<IReadOnlyDictionary<string, string>> s_runtimeAssemblies = new(() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase));

    static readonly AssemblyLocator RuntimeLocator = (name, trust) =>
        trust == AssemblyTrust.Platform && s_runtimeAssemblies.Value.TryGetValue(name, out var path)
            ? path
            : null;

    // The exact rung 2 construct set across every user-authored fixture type. Locked
    // (type-qualified, duplicates included) so a future fixture edit that drops a
    // construct or adds an overload fails loudly instead of silently shrinking scope.
    static readonly string[] ExpectedMembers =
    [
        // LadderRung2.Box`1 — generics.
        "Box`1::.ctor", "Box`1::FormatWith",
        "Box`1::get_Label", "Box`1::get_Value",
        "Box`1::set_Label", "Box`1::set_Value",
        // LadderRung2.LadderRung2Extensions — extension-call spelling.
        "LadderRung2Extensions::FirstOrZero", "LadderRung2Extensions::OrFallback",
        // LadderRung2.Program — initializers, anonymous types, nullable coalesce.
        "Program::AnonymousSummary", "Program::Main",
        "Program::MakeCollectionInitializer", "Program::MakeDirectReturn",
        "Program::NullableOrDefault",
    ];

    // The anonymous-type local is the honest-degrade frontier: recognizable body, but
    // the member stays Partial because the generated type is not source-recoverable.
    const string AnonymousFrontier = "Program::AnonymousSummary";

    [Fact]
    public void Rung2Fixture_ExposesExactMemberSet_AllFullExceptAnonymousFrontier_AndFullyRaised()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Key).Order(StringComparer.Ordinal).ToArray());

        var notFull = members
            .Where(m => m.Key != AnonymousFrontier && m.Function.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Rung 2 requires every fixture member except the anonymous-type frontier to render Full; not Full: " + string.Join(", ", notFull));

        // The anonymous-type local is pinned Partial: an honest, owned residual. If
        // this ever becomes Full, update the guard deliberately rather than silently.
        var anonymous = members.Single(m => m.Key == AnonymousFrontier).Function;
        Assert.Equal(DecompilationFidelity.Partial, anonymous.Fidelity);

        var gaps = members
            .Where(m => Completeness.Residual(m.Function) is not null)
            .Select(m => m.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Rung 2 requires every fixture member to be fully raised (zero residual gaps); gaps: " + string.Join(", ", gaps));
    }

    [Fact]
    public void Rung2Fixture_RendersInitializerChainsRecognizably()
    {
        var members = LoadRaisedMembers();

        var main = Body(members, "Program::Main");
        Assert.Contains("new Box<int> { Value = 3, Label = \"three\" }", main);
        Assert.Contains("new List<int> { 1, 2, 3 }", main);
        Assert.DoesNotContain(".Value = 3;", main);
        Assert.DoesNotContain(".Label = \"three\";", main);
        Assert.DoesNotContain(".Add(1);", main);

        Assert.Contains(
            "return new List<int> { 1, 2, 3 };",
            Body(members, "Program::MakeCollectionInitializer"));
        Assert.Contains(
            "return new Box<string> { Value = text, Label = text.OrFallback(\"empty\") };",
            Body(members, "Program::MakeDirectReturn"));

        var anonymous = Body(members, "Program::AnonymousSummary");
        Assert.Contains("var info = new { Name = name, Count = count };", anonymous);
        Assert.Contains("info.Name", anonymous);
        Assert.Contains("info.Count", anonymous);
        Assert.DoesNotContain("AnonymousType", anonymous);

        var nullable = Body(members, "Program::NullableOrDefault");
        Assert.Contains("return value ?? 42;", nullable);
        Assert.DoesNotContain("GetValueOrDefault", nullable);
    }

    [Fact]
    public void Rung2Fixture_RendersGenericsAndExtensionCallsRecognizably()
    {
        var members = LoadRaisedMembers();

        // Generic Box<T> property/method bodies render with ordinary field/member
        // access — this catches intra-fixture misrenders the validity shell filters
        // as member-resolution noise.
        Assert.Equal("return this.Value;", Body(members, "Box`1::get_Value"));
        Assert.Equal("this.Value = value;", Body(members, "Box`1::set_Value"));
        Assert.Equal("return this.Label;", Body(members, "Box`1::get_Label"));
        Assert.Equal("this.Label = value;", Body(members, "Box`1::set_Label"));

        var formatWith = Body(members, "Box`1::FormatWith");
        Assert.Contains("string.Concat(", formatWith);
        Assert.Contains("Label", formatWith);
        Assert.Contains("Value", formatWith);

        // Extension method definitions raise to recognizable bodies.
        var firstOrZero = Body(members, "LadderRung2Extensions::FirstOrZero");
        Assert.Contains("values.Count", firstOrZero);
        Assert.Contains("return values[0];", firstOrZero);
        Assert.Contains("return 0;", firstOrZero);

        var orFallback = Body(members, "LadderRung2Extensions::OrFallback");
        Assert.Contains("string.IsNullOrEmpty(value)", orFallback);
        Assert.Contains("return value;", orFallback);
        Assert.Contains("return fallback;", orFallback);

        // Extension calls render in instance form at the call site, not as static
        // helper calls on the extension class.
        var main = Body(members, "Program::Main");
        Assert.Contains("values.FirstOrZero()", main);
        Assert.DoesNotContain("LadderRung2Extensions.FirstOrZero", main);
        Assert.DoesNotContain("LadderRung2Extensions.OrFallback", Body(members, "Program::MakeDirectReturn"));
    }

    [Fact]
    public void Rung2Fixture_HasNoMalformedOrSemanticDefects()
    {
        var results = ValidityCheck.Evaluate(FixturePath)
            .Where(r => FixtureTypes.Contains(r.TypeName))
            .ToList();

        Assert.NotEmpty(results);

        var malformed = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.TypeName}::{r.MethodName}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformed.Length == 0,
            "Rung 2 requires zero malformed Full methods; malformed: " + string.Join("; ", malformed));

        var semantic = results
            .Where(r => r.HasSemanticDefect)
            .Select(r => $"{r.TypeName}::{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semantic.Length == 0,
            "Rung 2 requires zero semantic-binding defects; defects: " + string.Join("; ", semantic));

        // Guard against a vacuous pass: every Full member must actually have been
        // bound (not skipped by the cap), so the semantic assertion has teeth.
        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.TypeName}::{r.MethodName} was not semantically bound."));
    }

    static string Body(List<(string Key, IrFunction Function)> members, string key) =>
        CSharpPrinter.PrintRaised(members.Single(m => m.Key == key).Function).Output?.Trim() ?? "";

    // ImportAssembly yields metadata type names (e.g. LadderRung2.Box`1). Build a
    // stable, namespace-stripped member key so the locked member set stays readable.
    static string Simple(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot >= 0 ? typeName[(dot + 1)..] : typeName;
    }

    static List<(string Key, IrFunction Function)> LoadRaisedMembers()
    {
        var members = new List<(string Key, IrFunction Function)>();
        using var source = MetadataSource.Open(FixturePath, locator: RuntimeLocator);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (!FixtureTypes.Contains(typeName))
                continue;
            IrPasses.Run(function);
            members.Add(($"{Simple(typeName)}::{methodName}", function));
        }
        return members;
    }
}
