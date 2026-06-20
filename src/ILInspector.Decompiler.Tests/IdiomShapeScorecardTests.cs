using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Fixture-backed idiom recovery scorecard: proves selected raise passes recover
/// the intended C# syntax altitude, not merely C# that parses or recompiles.
/// </summary>
public class IdiomShapeScorecardTests
{
    private sealed record Case(
        string Pass,
        string Method,
        SyntaxKind Expected,
        SyntaxKind[] Rejected,
        bool CurrentlyRecovered = true);

    private static readonly Case[] Cases =
    [
        new("SwitchRaisingPass", nameof(CfgSampleClass.PowerOfTwo), SyntaxKind.SwitchExpression, [SyntaxKind.SwitchStatement]),
        new("TupleCreationPass", nameof(CfgSampleClass.TuplePair), SyntaxKind.TupleExpression, [SyntaxKind.ObjectCreationExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.StringInterpolation), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.AddExpression]),
        new("UsingStatementPass", nameof(CfgSampleClass.NormalUsing), SyntaxKind.UsingStatement, [SyntaxKind.TryStatement]),
        new("LockSugarPass", nameof(CfgSampleClass.ClassicLock), SyntaxKind.LockStatement, [SyntaxKind.TryStatement]),
        new("NullCoalescingAssignmentPass", nameof(CfgSampleClass.NullCoalescingAssignLocal), SyntaxKind.CoalesceAssignmentExpression, [SyntaxKind.IfStatement]),
        new("NullConditionalPass", nameof(CfgSampleClass.NullConditionalProperty), SyntaxKind.ConditionalAccessExpression, [SyntaxKind.IfStatement]),
        new("BooleanFoldingPass", nameof(CfgSampleClass.TernaryInt), SyntaxKind.ConditionalExpression, [SyntaxKind.IfStatement]),
        new("BooleanFoldingPass", nameof(CfgSampleClass.NullCoalesce), SyntaxKind.CoalesceExpression, [SyntaxKind.IfStatement]),
        new("DoWhileLoopPass", nameof(CfgSampleClass.DoWhileLoop), SyntaxKind.DoStatement, [SyntaxKind.WhileStatement]),
        new("ForLoopPass", nameof(CfgSampleClass.LoopWithBreak), SyntaxKind.ForStatement, [SyntaxKind.WhileStatement]),
        new("FixedStatementPass", nameof(CfgSampleClass.SumPinnedArray), SyntaxKind.FixedStatement, []),
        new("InlineArrayCollectionPass", nameof(CfgSampleClass.InlineArraySpan), SyntaxKind.CollectionExpression, [SyntaxKind.ObjectCreationExpression]),
        new("RangeFromGetSubArrayPass", nameof(CfgSampleClass.ArrayRangeBoth), SyntaxKind.RangeExpression, [SyntaxKind.InvocationExpression]),
        new("IndexFromEndPass", nameof(CfgSampleClass.LastElement), SyntaxKind.IndexExpression, [SyntaxKind.SubtractExpression]),
    ];

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

        var result = CSharpPrinter.PrintRaised(function!);
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
