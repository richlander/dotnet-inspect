using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Rung 1 owned-residual guard for the decompiler product quality ladder (#1599 /
/// #1693). The <see cref="LadderRung1.OwnedResiduals"/> fixture holds mainstream
/// C# 2-7 forms that decompile to <em>valid, fully raised</em> C# at a lower
/// source altitude than the original spelling. This gate keeps those residuals
/// honest and owned: it asserts the output is <c>Full</c> and free of malformed /
/// semantic defects (so it can never silently regress to invalid), while pinning
/// the current lower-altitude shape so a future raise that improves the altitude
/// is a deliberate, visible change.
/// </summary>
public class LadderRung1ResidualGateTests
{
    static string FixturePath => typeof(LadderRung1.OwnedResiduals).Assembly.Location;
    static readonly string FixtureType = typeof(LadderRung1.OwnedResiduals).FullName!;

    [Fact]
    public void ThrowExpression_RendersValidLowerAltitudeNullCheck_OwnedResidual()
    {
        var (name, function) = LoadRaisedMembers().Single(m => m.Name == "ThrowExpression");

        // Owned residual: valid and fully raised, just below `?? throw` altitude.
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Null(Completeness.Residual(function));

        var body = CSharpPrinter.PrintRaised(function).Output ?? "";
        // The current (owned) altitude is the null-check statement form, and the
        // nameof argument is correctly constant-folded to the literal "s".
        Assert.Contains("is null", body);
        Assert.Contains("throw new ArgumentNullException(\"s\")", body);
        // Pin the residual: it is NOT yet the `?? throw` expression. If a future
        // change raises the altitude, this assertion forces an intentional update.
        Assert.DoesNotContain("?? throw", body);
    }

    [Fact]
    public void Residuals_HaveNoMalformedOrSemanticDefects()
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
            "Owned residuals must stay valid (zero malformed Full methods); malformed: " + string.Join("; ", malformed));

        var semantic = results
            .Where(r => r.HasSemanticDefect)
            .Select(r => $"{r.MethodName}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semantic.Length == 0,
            "Owned residuals must stay valid (zero semantic-binding defects); defects: " + string.Join("; ", semantic));

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
