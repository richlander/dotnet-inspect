using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 1 <em>foundation control-flow</em> guard for the decompiler product
/// quality ladder (#1599 / #1691). <see cref="LadderRung1GateTests"/> only guards
/// methods/locals/calls/fields/properties and simple <c>if</c>/return branches,
/// yet rung 1 is the "Straightforward syntax foundation". This gate adds the core
/// C# 1 control-flow surface that was working but unguarded — loops, switch,
/// arrays, try/catch/finally, exception filters, and using statements — and pins
/// the same scoped bar as the <see cref="LadderRung1.Foundation"/> fixture so a
/// regression below it fails fast in PR CI:
/// <list type="bullet">
/// <item>the fixture exposes exactly the foundation member set (incl. the
/// constructor) — no construct is silently dropped;</item>
/// <item>every member imports and renders as <c>Full</c>;</item>
/// <item>every member is fully raised — zero residual control flow;</item>
/// <item>the rendered C# is recognizable for each foundation construct (the loop
/// keyword, a real <c>switch</c>, array creation/indexing, the EH keywords, the
/// <c>using</c> statement) — this catches intra-fixture misrenders that the
/// validity shell would filter as member-resolution noise;</item>
/// <item><c>--validity-check</c> sees zero malformed <c>Full</c> methods and zero
/// non-noise semantic-binding defects.</item>
/// </list>
/// Like <see cref="LadderRung1GateTests"/> this is the fast depth-scoped bar, not
/// a corpus sweep or compile-back opcode check.
/// </summary>
public class LadderRung1FoundationGateTests
{
    static string FixturePath => typeof(LadderRung1.Foundation).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung1.Foundation).FullName!;

    // The exact foundation control-flow construct set. Locked so a future fixture
    // edit that drops a construct fails loudly instead of silently shrinking the
    // measured scope.
    static readonly string[] ExpectedMembers =
    [
        ".ctor",
        "ArrayCreateAndIndex", "ArrayLiteral",
        "DoWhileLoop", "ForEachLoop", "ForLoop",
        "Switch",
        "TryCatchFilter", "TryCatchFinally",
        "UsingStatement", "WhileLoop",
    ];

    [Fact]
    public void Rung1Foundation_ExposesExactMemberSet_AllFullAndFullyRaised()
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
            "Rung 1 foundation requires every member to render Full; not Full: " + string.Join(", ", notFull));

        var gaps = members
            .Where(m => Completeness.Residual(m.Function) is not null)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(gaps.Length == 0,
            "Rung 1 foundation requires every member to be fully raised (zero residual gaps); gaps: " + string.Join(", ", gaps));
    }

    [Fact]
    public void Rung1Foundation_RendersRecognizableControlFlow()
    {
        var members = LoadRaisedMembers();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        // Loops render with their source keyword, not a goto ladder.
        Assert.Contains("for (int i = 0; i < n; i++)", Body("ForLoop"));
        Assert.Contains("while (n > 0)", Body("WhileLoop"));
        var doWhile = Body("DoWhileLoop");
        Assert.Contains("do", doWhile);
        Assert.Contains("while (n > 0);", doWhile);
        Assert.Contains("foreach (int value in values)", Body("ForEachLoop"));

        // A dense switch recovers as a real switch statement.
        var sw = Body("Switch");
        Assert.Contains("switch (code)", sw);
        Assert.Contains("case 0:", sw);
        Assert.Contains("case 5:", sw);
        Assert.Contains("default:", sw);

        // Array creation, indexing, and the array literal.
        var arr = Body("ArrayCreateAndIndex");
        Assert.Contains("new int[n]", arr);
        Assert.Contains("[i] = i * i;", arr);
        Assert.Contains("new int[] { 1, 2, 3, 4 }", Body("ArrayLiteral"));

        // try/catch/finally keywords are all present.
        var tcf = Body("TryCatchFinally");
        Assert.Contains("try", tcf);
        Assert.Contains("catch (DivideByZeroException)", tcf);
        Assert.Contains("finally", tcf);

        // Exception filter renders the when-clause, not a rethrow ladder.
        var filter = Body("TryCatchFilter");
        Assert.Contains("catch (Exception", filter);
        Assert.Contains("when (", filter);

        // using statement renders the using keyword and disposable acquisition.
        Assert.Contains("using (MemoryStream stream = new MemoryStream())", Body("UsingStatement"));
    }

    [Fact]
    public void Rung1Foundation_HasNoMalformedOrSemanticDefects()
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
            "Rung 1 foundation requires zero malformed Full methods; malformed: " + string.Join("; ", malformed));

        var semantic = results
            .Where(r => r.HasSemanticDefect)
            .Select(r => $"{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semantic.Length == 0,
            "Rung 1 foundation requires zero semantic-binding defects; defects: " + string.Join("; ", semantic));

        // Guard against a vacuous pass: every Full member must actually have been
        // bound (not skipped by the cap), so the semantic assertion has teeth.
        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.MethodName} was not semantically bound."));
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
