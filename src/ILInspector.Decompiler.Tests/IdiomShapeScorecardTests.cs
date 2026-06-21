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
        new("SwitchRaisingPass", nameof(CfgSampleClass.SmallStringSwitch), SyntaxKind.SwitchStatement, [SyntaxKind.GotoStatement]),
        new("SwitchRaisingPass", nameof(CfgSampleClass.ClassifyMode), SyntaxKind.SwitchStatement, [SyntaxKind.IfStatement]),
        new("TupleCreationPass", nameof(CfgSampleClass.TuplePair), SyntaxKind.TupleExpression, [SyntaxKind.ObjectCreationExpression]),
        new("TupleBinaryOperatorPass", nameof(CfgSampleClass.TupleValueEquals), SyntaxKind.EqualsExpression, [SyntaxKind.ConditionalExpression]),
        new("TupleBinaryOperatorPass", nameof(CfgSampleClass.TupleValueNotEquals), SyntaxKind.NotEqualsExpression, [SyntaxKind.ConditionalExpression]),
        new("AnonymousObjectPass", nameof(CfgSampleClass.AnonShorthand), SyntaxKind.AnonymousObjectCreationExpression, [SyntaxKind.ObjectCreationExpression]),
        new("AwaitRecoveryPass", nameof(CfgSampleClass.AwaitOnce), SyntaxKind.AwaitExpression, [SyntaxKind.InvocationExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.StringInterpolation), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.AddExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.InterpolationToLocal), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.AddExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.InterpolationAsArgument), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.AddExpression]),
        new("UsingStatementPass", nameof(CfgSampleClass.NormalUsing), SyntaxKind.UsingStatement, [SyntaxKind.TryStatement]),
        new("LockSugarPass", nameof(CfgSampleClass.ClassicLock), SyntaxKind.LockStatement, [SyntaxKind.TryStatement]),
        new("NullCoalescingAssignmentPass", nameof(CfgSampleClass.NullCoalescingAssignLocal), SyntaxKind.CoalesceAssignmentExpression, [SyntaxKind.IfStatement]),
        new("NullCoalescingAssignmentPass", nameof(CfgSampleClass.NullCoalescingAssignStaticField), SyntaxKind.CoalesceAssignmentExpression, [SyntaxKind.IfStatement]),
        new("NullCoalescingAssignmentPass", nameof(CfgSampleClass.NullCoalescingAssignInstanceField), SyntaxKind.CoalesceAssignmentExpression, [SyntaxKind.IfStatement]),
        new("NullConditionalPass", nameof(CfgSampleClass.NullConditionalProperty), SyntaxKind.ConditionalAccessExpression, [SyntaxKind.IfStatement]),
        new("BooleanFoldingPass", nameof(CfgSampleClass.TernaryInt), SyntaxKind.ConditionalExpression, [SyntaxKind.IfStatement]),
        new("BooleanFoldingPass", nameof(CfgSampleClass.NullCoalesce), SyntaxKind.CoalesceExpression, [SyntaxKind.IfStatement]),
        new("IsPatternPass", nameof(CfgSampleClass.IsPatternProperty), SyntaxKind.PropertyPatternClause, [SyntaxKind.LogicalAndExpression]),
        new("DoWhileLoopPass", nameof(CfgSampleClass.DoWhileLoop), SyntaxKind.DoStatement, [SyntaxKind.WhileStatement]),
        new("ForLoopPass", nameof(CfgSampleClass.LoopWithBreak), SyntaxKind.ForStatement, [SyntaxKind.WhileStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachLoop), SyntaxKind.ForEachStatement, [SyntaxKind.UsingStatement, SyntaxKind.WhileStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachArray), SyntaxKind.ForEachStatement, [SyntaxKind.ForStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachString), SyntaxKind.ForEachStatement, [SyntaxKind.ForStatement]),
        new("FixedStatementPass", nameof(CfgSampleClass.SumPinnedArray), SyntaxKind.FixedStatement, []),
        new("InlineArrayCollectionPass", nameof(CfgSampleClass.InlineArraySpan), SyntaxKind.CollectionExpression, [SyntaxKind.ObjectCreationExpression]),
        new("RangeFromGetSubArrayPass", nameof(CfgSampleClass.ArrayRangeBoth), SyntaxKind.RangeExpression, [SyntaxKind.InvocationExpression]),
        new("RangeFromGetSubArrayPass", nameof(CfgSampleClass.StringRangeBoth), SyntaxKind.RangeExpression, [SyntaxKind.InvocationExpression]),
        new("RangeFromGetSubArrayPass", nameof(CfgSampleClass.SpanRangeBoth), SyntaxKind.RangeExpression, [SyntaxKind.InvocationExpression]),
        new("IndexFromEndPass", nameof(CfgSampleClass.LastElement), SyntaxKind.IndexExpression, [SyntaxKind.SubtractExpression]),
        new("IndexFromEndPass", nameof(CfgSampleClass.NthFromEnd), SyntaxKind.IndexExpression, [SyntaxKind.SubtractExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructTuplePair), SyntaxKind.DeclarationExpression, [SyntaxKind.SimpleMemberAccessExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructIntoExistingLocals), SyntaxKind.TupleExpression, [SyntaxKind.SimpleMemberAccessExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructViaMethod), SyntaxKind.DeclarationExpression, [SyntaxKind.InvocationExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructMixedLocal), SyntaxKind.DeclarationExpression, [SyntaxKind.SimpleMemberAccessExpression]),
        new("LambdaRaisingPass", nameof(CfgSampleClass.NonCapturingLambda), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        new("LambdaRaisingPass", nameof(CfgSampleClass.CapturingLambda), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        new("LambdaRaisingPass", nameof(CfgSampleClass.StatementBodyLambda), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        // Local display-class environments (allocated and field-set across statements).
        new("LambdaRaisingPass", nameof(CfgSampleClass.InvokeLocalCapture), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        new("LambdaRaisingPass", nameof(CfgSampleClass.SharedCaptureLambdas), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        new("LambdaRaisingPass", nameof(CfgSampleClass.ClosureWithLinq), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        new("LambdaRaisingPass", nameof(CfgSampleClass.LocalBodyLambda), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        new("LambdaRaisingPass", nameof(CfgSampleClass.CapturingLocalBodyLambda), SyntaxKind.SimpleLambdaExpression, [SyntaxKind.ObjectCreationExpression]),
        new("LocalFunctionRaisingPass", nameof(CfgSampleClass.DoubleViaLocalFunction), SyntaxKind.LocalFunctionStatement, [SyntaxKind.SimpleMemberAccessExpression]),
        new("LocalFunctionRaisingPass", nameof(CfgSampleClass.CapturingLocalFunction), SyntaxKind.LocalFunctionStatement, [SyntaxKind.SimpleMemberAccessExpression]),
        new("LocalFunctionRaisingPass", nameof(CfgSampleClass.CaptureSecondParam), SyntaxKind.LocalFunctionStatement, [SyntaxKind.SimpleMemberAccessExpression]),
        new("LocalFunctionRaisingPass", nameof(CfgSampleClass.CaptureTwoVariables), SyntaxKind.LocalFunctionStatement, [SyntaxKind.SimpleMemberAccessExpression]),
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
