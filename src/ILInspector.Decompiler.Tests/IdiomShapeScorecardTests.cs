using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Fixture-backed idiom recovery scorecard: proves selected raise passes recover
/// the intended C# syntax altitude, not merely C# that parses or recompiles.
/// </summary>
[Trait("Speed", "Slow")]
public class IdiomShapeScorecardTests
{
    internal sealed record Case(
        string Pass,
        string Method,
        SyntaxKind Expected,
        SyntaxKind[] Rejected,
        bool CurrentlyRecovered = true);

    internal interface ICaseProvider
    {
        IEnumerable<Case> Cases { get; }
    }

    private static readonly Case[] Cases = LoadCases();

    private static Case[] LoadCases()
    {
        var cases = typeof(IdiomShapeScorecardTests).Assembly.GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && typeof(ICaseProvider).IsAssignableFrom(type))
            .Select(type => (ICaseProvider)Activator.CreateInstance(type)!)
            .SelectMany(provider => provider.Cases)
            .OrderBy(testCase => testCase.Pass, StringComparer.Ordinal)
            .ThenBy(testCase => testCase.Method, StringComparer.Ordinal)
            .ToArray();

        var duplicates = cases
            .GroupBy(testCase => $"{testCase.Pass}/{testCase.Method}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException("Duplicate idiom scorecard cases: " + string.Join(", ", duplicates));

        return cases;
    }

    [Fact]
    public void FixtureIdioms_RecoverExpectedSyntaxShapes()
    {
        List<string> failures = [];
        List<string> unexpectedRecoveries = [];
        var recovered = 0;

        foreach (var testCase in Cases)
        {
            var syntaxKinds = RenderSyntaxKinds(testCase.Method);
            var hasExpected = syntaxKinds.Contains(testCase.Expected);
            var lowerAltitude = testCase.Rejected.Where(syntaxKinds.Contains).ToArray();
            var isRecovered = hasExpected && lowerAltitude.Length == 0;

            if (isRecovered)
            {
                recovered++;
                if (!testCase.CurrentlyRecovered)
                    unexpectedRecoveries.Add($"{testCase.Pass}/{testCase.Method}: now recovers {testCase.Expected}; update the scorecard ratchet");
                continue;
            }

            if (testCase.CurrentlyRecovered)
            {
                if (!hasExpected)
                    failures.Add($"{testCase.Pass}/{testCase.Method}: missing {testCase.Expected}");
                foreach (var rejected in lowerAltitude)
                    failures.Add($"{testCase.Pass}/{testCase.Method}: still contains lower-altitude {rejected}");
            }
        }

        var expectedRecovered = Cases.Count(c => c.CurrentlyRecovered);
        Assert.True(
            failures.Count == 0 && unexpectedRecoveries.Count == 0 && recovered == expectedRecovered,
            $"Idiom recovery scorecard: {recovered}/{Cases.Length} fixture idioms recovered (expected {expectedRecovered}/{Cases.Length}).\n"
            + string.Join('\n', failures.Concat(unexpectedRecoveries)));
    }

    private static HashSet<SyntaxKind> RenderSyntaxKinds(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        // Wire the cross-method import seam so passes that reach a sibling body
        // (lambda raising imports the synthesized method) run as they do on the
        // shipped product path; without it the scorecard would understate them.
        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);

        var text = $$"""
            class C
            {
                object M()
                {
            {{result.Output}}
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(
            text,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var root = tree.GetRoot();
        return root.DescendantNodesAndSelf().Select(node => node.Kind()).ToHashSet();
    }
}
