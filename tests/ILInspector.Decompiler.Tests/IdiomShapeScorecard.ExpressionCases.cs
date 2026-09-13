using Microsoft.CodeAnalysis.CSharp;

using Case = ILInspector.Decompiler.Tests.IdiomShapeScorecardTests.Case;

namespace ILInspector.Decompiler.Tests;

internal sealed class ExpressionScorecardCases : IdiomShapeScorecardTests.ICaseProvider
{
    public IEnumerable<Case> Cases =>
    [
        new("AnonymousObjectPass", nameof(CfgSampleClass.AnonShorthand), SyntaxKind.AnonymousObjectCreationExpression, [SyntaxKind.ObjectCreationExpression]),
        new("AnonymousObjectPass", nameof(CfgSampleClass.AnonNested), SyntaxKind.AnonymousObjectCreationExpression, [SyntaxKind.ObjectCreationExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.StringInterpolation), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.AddExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.InterpolationToLocal), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.AddExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.InterpolationAsArgument), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.AddExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.InterpolationWithFormat), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.InvocationExpression]),
        new("StringInterpolationPass", nameof(CfgSampleClass.InterpolationWithAlignmentAndFormat), SyntaxKind.InterpolatedStringExpression, [SyntaxKind.InvocationExpression]),
        new("NullCoalescingAssignmentPass", nameof(CfgSampleClass.NullCoalescingAssignLocal), SyntaxKind.CoalesceAssignmentExpression, [SyntaxKind.IfStatement]),
        new("NullCoalescingAssignmentPass", nameof(CfgSampleClass.NullCoalescingAssignStaticField), SyntaxKind.CoalesceAssignmentExpression, [SyntaxKind.IfStatement]),
        new("NullCoalescingAssignmentPass", nameof(CfgSampleClass.NullCoalescingAssignInstanceField), SyntaxKind.CoalesceAssignmentExpression, [SyntaxKind.IfStatement]),
        new("NullConditionalPass", nameof(CfgSampleClass.NullConditionalProperty), SyntaxKind.ConditionalAccessExpression, [SyntaxKind.IfStatement]),
        new("BooleanFoldingPass", nameof(CfgSampleClass.TernaryInt), SyntaxKind.ConditionalExpression, [SyntaxKind.IfStatement]),
        new("BooleanFoldingPass", nameof(CfgSampleClass.NullCoalesce), SyntaxKind.CoalesceExpression, [SyntaxKind.IfStatement]),
        new("InlineArrayCollectionPass", nameof(CfgSampleClass.InlineArraySpan), SyntaxKind.CollectionExpression, [SyntaxKind.ObjectCreationExpression]),
        new("InlineArrayCollectionPass", nameof(CfgSampleClass.ArraySpreadWithTail), SyntaxKind.CollectionExpression, [SyntaxKind.ArrayCreationExpression]),
        new("InlineArrayCollectionPass", nameof(CfgSampleClass.InlineArrayFieldAsSpan), SyntaxKind.CastExpression, [SyntaxKind.InvocationExpression]),
        new("RangeFromGetSubArrayPass", nameof(CfgSampleClass.ArrayRangeBoth), SyntaxKind.RangeExpression, [SyntaxKind.InvocationExpression]),
        new("RangeFromGetSubArrayPass", nameof(CfgSampleClass.StringRangeBoth), SyntaxKind.RangeExpression, [SyntaxKind.InvocationExpression]),
        new("RangeFromGetSubArrayPass", nameof(CfgSampleClass.SpanRangeBoth), SyntaxKind.RangeExpression, [SyntaxKind.InvocationExpression]),
        new("IndexFromEndPass", nameof(CfgSampleClass.LastElement), SyntaxKind.IndexExpression, [SyntaxKind.SubtractExpression]),
        new("IndexFromEndPass", nameof(CfgSampleClass.NthFromEnd), SyntaxKind.IndexExpression, [SyntaxKind.SubtractExpression]),
        new("IndexFromEndPass", nameof(CfgSampleClass.ReadOnlySpanLast), SyntaxKind.IndexExpression, [SyntaxKind.SubtractExpression]),
    ];
}
