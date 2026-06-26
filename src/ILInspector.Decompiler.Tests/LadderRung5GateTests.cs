using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 5 guard for the decompiler product quality ladder (#1599): C# 8-9
/// syntax surface. It decompiles the in-repo <see cref="LadderRung5.Program"/>
/// fixture and pins the scoped rung 5 bar that already holds, so a regression
/// below it fails fast in PR CI:
/// <list type="bullet">
/// <item>the fixture exposes exactly the rung 5 source-visible member set — no
/// construct is silently dropped;</item>
/// <item><b>no invalid <c>Full</c></b>: every member that imports as <c>Full</c>
/// renders as valid C# — zero malformed <c>Full</c> methods and zero non-noise
/// semantic-binding defects (the core ladder bar);</item>
/// <item>the constructs that already meet the rung 5 bar — index/range operators,
/// <c>using</c> declarations, property/type patterns, scalar relational switch
/// expressions, and init-only object initializers — render recognizably.</item>
/// </list>
///
/// Rung 5 is <b>not complete</b>: three source-visible constructs still fall short
/// of the bar and are tracked as focused issues. They are deliberately NOT
/// asserted green here; this guard locks the invariant and the working surface so
/// those fixes can be verified against a stable baseline:
/// <list type="bullet">
/// <item>#1630 — <c>with</c>-expression (<see cref="LadderRung5.Program.Shift"/>)
/// is not raised and renders the invalid <c>&lt;Clone&gt;$</c> identifier (CS1001);
/// it stays <c>Partial</c>, so it does not breach the no-invalid-<c>Full</c>
/// bar.</item>
/// <item>#1631 — tuple-pattern switch expression
/// (<see cref="LadderRung5.Program.Quadrant"/>) decompiles to a goto ladder.</item>
/// <item>#1632 — record <c>Deconstruct</c> drops the receiver and renders
/// <c>X = X;</c>.</item>
/// </list>
/// </summary>
public class LadderRung5GateTests
{
    static string FixturePath => typeof(LadderRung5.Program).Assembly.Location;
    static readonly string ProgramType = typeof(LadderRung5.Program).FullName!;

    // The exact rung 5 source-visible Program member set. Locked so a future
    // fixture edit that drops a construct fails loudly instead of silently
    // shrinking the measured scope.
    static readonly string[] ExpectedProgramMembers =
    [
        ".ctor", "Describe", "FromEnd", "IsOrigin", "IsRealString", "LastElement",
        "MakeScaled", "MiddleSlice", "Prefix", "Quadrant", "Shift", "Size",
        "UsingDeclaration",
    ];

    [Fact]
    public void Rung5Fixture_ExposesExactProgramMemberSet()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == ProgramType).ToList();
        Assert.Equal(
            ExpectedProgramMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
    }

    // The core ladder bar for rung 5: every member that imports as Full must
    // render valid C#. This holds across the whole fixture today (the only
    // malformed member, Shift / #1630, imports as Partial), so a future change
    // that promotes a malformed body to Full — or introduces any malformed Full —
    // fails here.
    [Fact]
    public void Rung5Fixture_HasNoInvalidFull()
    {
        var results = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName.StartsWith("LadderRung5.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(results);

        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.Id}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 5 requires no invalid Full; malformed Full: " + string.Join("; ", malformedFull));

        var semantic = results
            .Where(r => r.HasSemanticDefect)
            .Select(r => $"{r.Id}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semantic.Length == 0,
            "Rung 5 requires zero semantic-binding defects; defects: " + string.Join("; ", semantic));

        // Teeth: every Full member must actually have been bound (not skipped by
        // the cap), so the malformed/semantic assertions are not vacuous.
        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.Id} was not semantically bound."));
    }

    // The rung 5 constructs that already meet the bar render recognizably. These
    // catch intra-fixture misrenders that the validity shell filters as
    // member-resolution noise.
    [Fact]
    public void Rung5Fixture_RendersWorkingConstructs()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == ProgramType).ToList();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        // Index-from-end and range operators.
        Assert.Equal("return values[^1];", Body("LastElement"));
        Assert.Equal("return values[^offset];", Body("FromEnd"));
        Assert.Equal("return values[1..^1];", Body("MiddleSlice"));
        Assert.Equal("return values[..2];", Body("Prefix"));

        // Property pattern.
        Assert.Contains("point is not null && point.X == 0 && point.Y == 0", Body("IsOrigin"));

        // Type pattern with a negated constant pattern.
        var isRealString = Body("IsRealString");
        Assert.Contains("value is string", isRealString);
        Assert.Contains("== \"\"", isRealString);

        // Scalar relational/type switch expressions lower to recognizable
        // branches with the right arms.
        var size = Body("Size");
        Assert.Contains("return \"negative\";", size);
        Assert.Contains("return \"small\";", size);
        Assert.Contains("return \"big\";", size);

        var describe = Body("Describe");
        Assert.Contains("return s;", describe);
        Assert.Contains("\"positive int\"", describe);
        Assert.Contains("\"null\"", describe);

        // using declaration over a disposable local.
        Assert.Contains("using (MemoryStream stream = new MemoryStream(bytes))", Body("UsingDeclaration"));

        // init-only property set through an object initializer on a record.
        Assert.Contains("new Point(x, y) { Magnitude = x + y }", Body("MakeScaled"));
    }

    static List<(string Type, string Name, IrFunction Function)> LoadRaisedMembers()
    {
        var members = new List<(string Type, string Name, IrFunction Function)>();
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (!typeName.StartsWith("LadderRung5.", StringComparison.Ordinal))
                continue;
            IrPasses.Run(function);
            members.Add((typeName, methodName, function));
        }
        return members;
    }
}
