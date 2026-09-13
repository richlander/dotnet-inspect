using Microsoft.CodeAnalysis.CSharp;

using Case = ILInspector.Decompiler.Tests.IdiomShapeScorecardTests.Case;

namespace ILInspector.Decompiler.Tests;

internal sealed class ClosureScorecardCases : IdiomShapeScorecardTests.ICaseProvider
{
    public IEnumerable<Case> Cases =>
    [
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
}
