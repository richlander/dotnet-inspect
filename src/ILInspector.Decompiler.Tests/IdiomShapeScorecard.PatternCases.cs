using Microsoft.CodeAnalysis.CSharp;

using Case = ILInspector.Decompiler.Tests.IdiomShapeScorecardTests.Case;

namespace ILInspector.Decompiler.Tests;

internal sealed class PatternScorecardCases : IdiomShapeScorecardTests.ICaseProvider
{
    public IEnumerable<Case> Cases =>
    [
        new("IsPatternPass", nameof(CfgSampleClass.IsPatternProperty), SyntaxKind.PropertyPatternClause, [SyntaxKind.LogicalAndExpression]),
        new("IsPatternPass", nameof(CfgSampleClass.IsPatternPropertyGreater), SyntaxKind.PropertyPatternClause, [SyntaxKind.LogicalAndExpression]),
        new("IsPatternPass", nameof(CfgSampleClass.IsPatternMultiProperty), SyntaxKind.PropertyPatternClause, [SyntaxKind.LogicalAndExpression]),
        new("IsPatternPass", nameof(CfgSampleClass.PositionalPattern), SyntaxKind.PositionalPatternClause, [SyntaxKind.IfStatement]),
        new("IsPatternPass", nameof(CfgSampleClass.RecursivePropertyPatternBinding), SyntaxKind.PropertyPatternClause, [SyntaxKind.AsExpression]),
        new("ListPatternPass", nameof(CfgSampleClass.SingleElementStringArrayListPattern), SyntaxKind.ListPattern, [SyntaxKind.GotoStatement]),
    ];
}
