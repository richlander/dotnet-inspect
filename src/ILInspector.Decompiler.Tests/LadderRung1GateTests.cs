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
/// <item>every fixture member (including property accessors and the constructor)
/// imports and renders as <c>Full</c>;</item>
/// <item>every member is fully raised — <c>--gaps</c> sees zero residual control
/// flow;</item>
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

    [Fact]
    public void Rung1Fixture_AllMembersImportRenderFullAndFullyRaised()
    {
        var members = new List<(string Name, IrFunction Function)>();
        using (var source = MetadataSource.Open(FixturePath))
        {
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                if (typeName != FixtureType)
                    continue;
                IrPasses.Run(function);
                members.Add((methodName, function));
            }
        }

        Assert.NotEmpty(members);

        // Property accessors must be included, not only ordinary method groups.
        Assert.Contains(members, m => m.Name == "get_Count");
        Assert.Contains(members, m => m.Name == "set_Count");
        Assert.Contains(members, m => m.Name == "get_Prefix");

        var notFull = members
            .Where(m => m.Function.Fidelity != DecompilationFidelity.Full)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(notFull.Length == 0,
            "Rung 1 requires every fixture member to render Full; not Full: " + string.Join(", ", notFull));

        var gaps = members
            .Where(m => Completeness.Residual(m.Function) is not null)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Rung 1 requires every fixture member to be fully raised (zero residual gaps); gaps: " + string.Join(", ", gaps));
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
}
