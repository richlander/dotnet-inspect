using Microsoft.CodeAnalysis.CSharp;

using Case = ILInspector.Decompiler.Tests.IdiomShapeScorecardTests.Case;

namespace ILInspector.Decompiler.Tests;

internal sealed class SwitchRaisingScorecardCases : IdiomShapeScorecardTests.ICaseProvider
{
    public IEnumerable<Case> Cases =>
    [
        new("SwitchRaisingPass", nameof(CfgSampleClass.PowerOfTwo), SyntaxKind.SwitchExpression, [SyntaxKind.SwitchStatement]),
        new("SwitchRaisingPass", nameof(CfgSampleClass.SmallStringSwitch), SyntaxKind.SwitchStatement, [SyntaxKind.GotoStatement]),
        new("SwitchRaisingPass", nameof(CfgSampleClass.ClassifyMode), SyntaxKind.SwitchStatement, [SyntaxKind.IfStatement]),
    ];
}

internal sealed class TupleScorecardCases : IdiomShapeScorecardTests.ICaseProvider
{
    public IEnumerable<Case> Cases =>
    [
        new("TupleCreationPass", nameof(CfgSampleClass.TuplePair), SyntaxKind.TupleExpression, [SyntaxKind.ObjectCreationExpression]),
        new("TupleCreationPass", nameof(CfgSampleClass.TupleRest), SyntaxKind.TupleExpression, [SyntaxKind.ObjectCreationExpression]),
        new("TupleBinaryOperatorPass", nameof(CfgSampleClass.TupleValueEquals), SyntaxKind.EqualsExpression, [SyntaxKind.ConditionalExpression]),
        new("TupleBinaryOperatorPass", nameof(CfgSampleClass.TupleValueNotEquals), SyntaxKind.NotEqualsExpression, [SyntaxKind.ConditionalExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructTuplePair), SyntaxKind.DeclarationExpression, [SyntaxKind.SimpleMemberAccessExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructIntoExistingLocals), SyntaxKind.TupleExpression, [SyntaxKind.SimpleMemberAccessExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructViaMethod), SyntaxKind.DeclarationExpression, [SyntaxKind.InvocationExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructMixedLocal), SyntaxKind.DeclarationExpression, [SyntaxKind.SimpleMemberAccessExpression]),
        new("DeconstructionAssignmentPass", nameof(CfgSampleClass.DeconstructIntoParameters), SyntaxKind.TupleExpression, [SyntaxKind.SimpleMemberAccessExpression]),
    ];
}
