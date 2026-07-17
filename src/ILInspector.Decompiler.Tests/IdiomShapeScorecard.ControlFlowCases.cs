using Microsoft.CodeAnalysis.CSharp;

using Case = ILInspector.Decompiler.Tests.IdiomShapeScorecardTests.Case;

namespace ILInspector.Decompiler.Tests;

internal sealed class ControlFlowScorecardCases : IdiomShapeScorecardTests.ICaseProvider
{
    public IEnumerable<Case> Cases =>
    [
        new("AwaitRecoveryPass", nameof(CfgSampleClass.AwaitOnce), SyntaxKind.AwaitExpression, [SyntaxKind.InvocationExpression]),
        new("UsingStatementPass", nameof(CfgSampleClass.NormalUsing), SyntaxKind.UsingStatement, [SyntaxKind.TryStatement]),
        new("UsingStatementPass", nameof(CfgSampleClass.RefStructPatternUsing), SyntaxKind.UsingStatement, [SyntaxKind.TryStatement]),
        new("UsingStatementPass", nameof(CfgSampleClass.AwaitUsingResource), SyntaxKind.UsingStatement, [SyntaxKind.TryStatement]),
        new("UsingStatementPass", nameof(CfgSampleClass.NestedAwaitUsingResources), SyntaxKind.UsingStatement, [SyntaxKind.TryStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.AwaitForeach), SyntaxKind.ForEachStatement, [SyntaxKind.UsingStatement, SyntaxKind.WhileStatement]),
        new("LockSugarPass", nameof(CfgSampleClass.ClassicLock), SyntaxKind.LockStatement, [SyntaxKind.TryStatement]),
        new("DoWhileLoopPass", nameof(CfgSampleClass.DoWhileLoop), SyntaxKind.DoStatement, [SyntaxKind.WhileStatement]),
        new("DoWhileLoopPass", nameof(CfgSampleClass.PartitionStyleNestedSelfLoops), SyntaxKind.DoStatement, [SyntaxKind.GotoStatement]),
        new("ForLoopPass", nameof(CfgSampleClass.LoopWithBreak), SyntaxKind.ForStatement, [SyntaxKind.WhileStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachLoop), SyntaxKind.ForEachStatement, [SyntaxKind.UsingStatement, SyntaxKind.WhileStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachArray), SyntaxKind.ForEachStatement, [SyntaxKind.ForStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachString), SyntaxKind.ForEachStatement, [SyntaxKind.ForStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachRectangularArray), SyntaxKind.ForEachStatement, [SyntaxKind.ForStatement]),
        new("ForeachStatementPass", nameof(CfgSampleClass.ForeachPatternEnumerable), SyntaxKind.ForEachStatement, [SyntaxKind.WhileStatement]),
        new("ForLoopPass", nameof(CfgSampleClass.Issue2830_ForLoopLeave), SyntaxKind.WhileStatement, [SyntaxKind.ForStatement]),
        new("ForLoopPass", nameof(CfgSampleClass.Issue2830_ForLoopLeaveToAnotherTarget), SyntaxKind.ForStatement, [SyntaxKind.WhileStatement]),
        new("ForLoopPass", nameof(CfgSampleClass.Issue2830_ForLoopEhCleanupContinue), SyntaxKind.WhileStatement, [SyntaxKind.ForStatement]),
        new("ForLoopPass", nameof(CfgSampleClass.Issue2830_ForLoopWithNestedLambdaTargetConflict), SyntaxKind.ForStatement, []),
        new("ForLoopPass", nameof(CfgSampleClass.Issue2830_ForLoopWithNestedLambdaTargetConflict), SyntaxKind.WhileStatement, []),
        new("ForLoopPass", nameof(CfgSampleClass.Issue2830_ForLoopNestedContinue), SyntaxKind.ForStatement, []),
        new("FixedStatementPass", nameof(CfgSampleClass.SumPinnedArray), SyntaxKind.FixedStatement, []),
    ];
}
