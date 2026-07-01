using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 1 <em>product-path</em> guard for the decompiler product quality
/// ladder (#1599 / #1695). The other rung gates ride the bare per-method seam
/// (no cross-method import callback, no platform assembly locator), which is fine
/// for self-contained syntax but <em>under-resolves</em> the constructs that only
/// recover through the path users actually hit (<c>dotnet-inspect type … -S
/// "Decompiled Source"</c>): lambdas and LINQ need the cross-method import seam,
/// and cross-assembly <c>out</c>/<c>ref</c>/<c>in</c> arguments need a platform
/// locator. On the seam alone those degrade (lambdas leak <c>&lt;&gt;c</c> names,
/// a cross-assembly <c>out</c> arg renders <c>ref</c>) even though the product
/// renders them correctly — exactly the seam-vs-product gap that made the
/// cross-assembly out-variable look like a bug (#1692).
///
/// <para>This gate composes the <see cref="LadderRung1.ProductSurface"/> fixture
/// through the same wiring the product uses — a TPA-backed platform locator on
/// the <see cref="MetadataSource"/> plus the cross-method import seam on
/// <see cref="CSharpPrinter"/> — and pins the real product rendering: lambdas
/// raise to <c>x =&gt; …</c>, LINQ to a raised operator chain, and the
/// cross-assembly out-variable to <c>out</c>. It is the reusable model for any
/// rung that needs to guard seam-sensitive constructs at product fidelity.</para>
/// </summary>
public class LadderRung1ProductPathGateTests
{
    static string FixturePath => typeof(LadderRung1.ProductSurface).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung1.ProductSurface).FullName!;

    // Platform locator backed by the running runtime's trusted-platform-assembly
    // list — always populated for the host runtime, so it resolves the fixture's
    // framework references (System.Private.CoreLib for int.TryParse) without
    // depending on SDK/shared-framework discovery. Only Platform-trust references
    // resolve, so a planted local copy can never satisfy a platform name. This is
    // the same convention LadderRung2GateTests uses.
    static readonly Lazy<IReadOnlyDictionary<string, string>> s_runtimeAssemblies = new(() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase));

    static readonly AssemblyLocator RuntimeLocator = (name, trust) =>
        trust == AssemblyResolutionScope.Platform && s_runtimeAssemblies.Value.TryGetValue(name, out var path)
            ? path
            : null;

    static readonly string[] ExpectedMembers =
    [
        ".ctor", "CapturingLambda", "Lambda", "LinqQuery", "OutVariable",
    ];

    [Fact]
    public void Rung1ProductPath_ExposesExactMemberSet_AllFullAndFullyRaised()
    {
        var members = LoadProductPathMembers();

        Assert.Equal(
            ExpectedMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());

        var notFull = members
            .Where(m => m.Function.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Rung 1 product path requires every member to render Full; not Full: " + string.Join(", ", notFull));

        var gaps = members
            .Where(m => Completeness.Residual(m.Function) is not null)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Rung 1 product path requires every member to be fully raised; gaps: " + string.Join(", ", gaps));
    }

    [Fact]
    public void Rung1ProductPath_RaisesSeamSensitiveConstructs_AtProductFidelity()
    {
        var members = LoadProductPathMembers();
        string Body(string name) => members.Single(m => m.Name == name).Body;

        // Lambdas raise to expression-lambda syntax (cross-method import seam).
        Assert.Contains("x => x * 2", Body("Lambda"));
        Assert.Contains("x => x * factor", Body("CapturingLambda"));

        // LINQ raises to a recognizable operator chain with raised lambda args.
        var linq = Body("LinqQuery");
        Assert.Contains(".Where", linq);
        Assert.Contains(".Select", linq);
        Assert.Contains("value => value > 0", linq);
        Assert.Contains("value => value * 2", linq);

        // The cross-assembly out-variable resolves to `out`, not `ref` — the
        // platform locator reads int.TryParse's real parameter rows (#1692).
        var outVar = Body("OutVariable");
        Assert.Contains("int.TryParse(text, out result)", outVar);
        Assert.DoesNotContain("ref result", outVar);

        // No member leaks compiler-generated scaffolding into Full product output.
        foreach (var (name, _, body) in members)
        {
            Assert.DoesNotContain("<>", body);
            Assert.DoesNotContain("b__", body);
            Assert.DoesNotContain("DisplayClass", body);
        }
    }

    [Fact]
    public void Rung1ProductPath_OutputIsSyntacticallyValid()
    {
        var members = LoadProductPathMembers();

        // Parsing each body as a method body catches the degraded forms this gate
        // exists to prevent: a leaked `<>c` name is not a legal C# identifier, so
        // seam/locator regressions surface as parse errors here, not just as a
        // failed substring assertion.
        foreach (var (name, _, body) in members)
        {
            var tree = CSharpSyntaxTree.ParseText($"class Shell {{ object M() {{ {body} }} }}",
                cancellationToken: TestContext.Current.CancellationToken);
            var errors = tree.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToArray();
            Assert.True(errors.Length == 0,
                $"Rung 1 product path member {name} did not parse as valid C#: " + string.Join("; ", errors));
        }
    }

    static List<(string Name, IrFunction Function, string Body)> LoadProductPathMembers()
    {
        var members = new List<(string Name, IrFunction Function, string Body)>();
        using var source = MetadataSource.Open(FixturePath, locator: RuntimeLocator);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != FixtureType)
                continue;
            // Product wiring: the import seam raises cross-method lambda bodies,
            // and the locator on `source` resolves cross-assembly ref-kinds.
            var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            members.Add((methodName, function, (result.Output ?? "").Trim()));
        }
        return members;
    }
}
