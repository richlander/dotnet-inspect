using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 1 completion guard for the decompiler product quality ladder (#1599 /
/// #1603): Hello World / C# 1 core. It decompiles the in-repo
/// <see cref="LadderRung1.Program"/> fixture and pins the scoped rung 1 bar so a
/// regression below it fails fast in PR CI:
/// <list type="bullet">
/// <item>the fixture exposes exactly the rung 1 member set (incl. property
/// accessors and the constructor) — no construct is silently dropped;</item>
/// <item>every member imports and renders as <c>Full</c>;</item>
/// <item>every member is fully raised — <c>--gaps</c> sees zero residual control
/// flow;</item>
/// <item>the rendered C# is recognizable for the rung 1 constructs (fields,
/// property/method calls, branches) — this catches intra-fixture misrenders that
/// the validity shell filters as member-resolution noise;</item>
/// <item><c>--validity-check</c> sees zero malformed <c>Full</c> methods and zero
/// non-noise semantic-binding defects.</item>
/// </list>
/// This is the depth-scoped completion bar for rung 1, deliberately fast (not a
/// corpus sweep): compile-back opcode exactness is evidence elsewhere
/// (<see cref="FidelityGateTests"/>), not the rung 1 bar — <c>IsPositive</c>
/// recompiles its branch as <c>cgt</c> while still rendering valid C#.
/// </summary>
public class LadderRung1GateTests
{
    static string FixturePath => typeof(LadderRung1.Program).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung1.Program).FullName!;

    // The exact rung 1 construct set. Locked so a future fixture edit that drops a
    // construct (e.g. removing the multi-return Classify, or an accessor) fails
    // loudly instead of silently shrinking the measured scope.
    static readonly string[] ExpectedMembers =
    [
        ".ctor", "Add", "Classify", "Greet", "IsPositive", "Main",
        "get_Count", "get_Prefix", "set_Count",
    ];

    [Fact]
    public void Rung1Fixture_ExposesExactMemberSet_AllFullAndFullyRaised()
    {
        var members = LoadRaisedMembers();

        Assert.Equal(ExpectedMembers, members.Keys.Order(StringComparer.Ordinal).ToArray());

        var notFull = members
            .Where(m => m.Value.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Rung 1 requires every fixture member to render Full; not Full: " + string.Join(", ", notFull));

        var gaps = members
            .Where(m => Completeness.Residual(m.Value) is not null)
            .Select(m => m.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Rung 1 requires every fixture member to be fully raised (zero residual gaps); gaps: " + string.Join(", ", gaps));
    }

    [Fact]
    public void Rung1Fixture_RendersRecognizableConstructs()
    {
        var members = LoadRaisedMembers();
        string Body(string name) => CSharpPrinter.PrintRaised(members[name]).Output?.Trim() ?? "";

        // Exact small bodies: field reads/writes, literals, and arithmetic must
        // render verbatim. These catch intra-fixture member/field misrenders
        // (e.g. `_count` -> `_count_BROKEN`) that ValidityCheck's external shell
        // filters as CS0103/CS1061 member-resolution noise.
        Assert.Equal("return _count;", Body("get_Count"));
        Assert.Equal("return \"Hello\";", Body("get_Prefix"));
        Assert.Equal("return left + right;", Body("Add"));
        Assert.Equal("return value > 0;", Body("IsPositive"));

        // Setter branch writes the backing field on both arms.
        var setCount = Body("set_Count");
        Assert.Contains("if (value < 0)", setCount);
        Assert.Contains("_count = 0;", setCount);
        Assert.Contains("_count = value;", setCount);

        // String concat / call spelling reads the getter-only property and mutates
        // the field.
        var greet = Body("Greet");
        Assert.Contains("string.Concat(Prefix, \", \", name)", greet);
        Assert.Contains("_count++;", greet);

        // Multi-return branch classification.
        var classify = Body("Classify");
        Assert.Contains("if (value == 0)", classify);
        Assert.Contains("if (value < 3)", classify);
        Assert.Contains("return 0;", classify);
        Assert.Contains("return 2;", classify);

        // Object construction, property/method calls, Console.WriteLine, branch.
        var main = Body("Main");
        Assert.Contains("new Program()", main);
        Assert.Contains("program.Greet(\"world\")", main);
        Assert.Contains("program.IsPositive(program.Count)", main);
        Assert.Contains("Console.WriteLine(", main);
    }

    [Fact]
    public void Rung1Fixture_HasNoMalformedOrSemanticDefects()
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
            "Rung 1 requires zero malformed Full methods; malformed: " + string.Join("; ", malformed));

        var semantic = results
            .Where(r => r.HasSemanticDefect)
            .Select(r => $"{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semantic.Length == 0,
            "Rung 1 requires zero semantic-binding defects; defects: " + string.Join("; ", semantic));

        // Guard against a vacuous pass: every Full member must actually have been
        // bound (not skipped by the cap), so the semantic assertion has teeth.
        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.MethodName} was not semantically bound."));
    }

    static Dictionary<string, IrFunction> LoadRaisedMembers()
    {
        var members = new Dictionary<string, IrFunction>(StringComparer.Ordinal);
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != FixtureType)
                continue;
            IrPasses.Run(function);
            members[methodName] = function;
        }
        return members;
    }
}
