namespace ILInspector.Decompiler.Pipeline;

internal sealed class LambdaRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.ClosureConversion, nameof(ClosureCoverage.Lambda)),
            typeof(LambdaRaisingPass),
            [
                new FactPrimitive("generated-type:lambda-holder", "GeneratedCodeIdentity.IsNonCapturingLambdaMethod"),
                new FactPrimitive("generated-type:display-class", "GeneratedCodeIdentity.IsCapturingLambdaMethod"),
            ],
            PositiveCoverage: "LambdaRaisingPassTests non-capturing, capturing, and non-capturing local-bodied fixtures",
            AdversarialCoverage: "LambdaRaisingPassTests generated-name lookalike without metadata and capturing local-bodied guard",
            MissingDiscriminator: "capturing local-bound bodies and expression trees need additional closure/state facts"),

        new(
            new LoweringFactKey(LoweringFactRegister.ClosureConversion, nameof(ClosureCoverage.CapturedClosure)),
            typeof(LambdaRaisingPass),
            [
                new FactPrimitive("generated-type:display-class", "GeneratedCodeIdentity.IsCapturingLambdaMethod"),
                new FactPrimitive("place.re-evaluable", "PlaceIdentity same-place atoms for safe environment substitution"),
            ],
            PositiveCoverage: "LambdaRaisingPassTests capturing lambda fixture",
            AdversarialCoverage: "LambdaRaisingPassTests guards for unsupported local/captured forms",
            MissingDiscriminator: "display classes spread across statements are still owed"),

        new(
            new LoweringFactKey(LoweringFactRegister.ClosureConversion, nameof(ClosureCoverage.LocalFunction)),
            typeof(LocalFunctionRaisingPass),
            [
                new FactPrimitive("generated-method:local-function", "GeneratedCodeIdentity.IsLocalFunctionMethod"),
                new FactPrimitive("cross-method-import", "PassContext.ImportMethodBody"),
            ],
            PositiveCoverage: "LocalFunctionRaisingPassTests static, static local-bodied, and capturing fixtures, each called once and more than once where applicable",
            AdversarialCoverage: "LocalFunctionRaisingPass guards reject shared-environment, recursive, nested, post-mutation capture, unsupported, and capturing local-bodied forms",
            MissingDiscriminator: "capturing local-bodied forms, nested local functions, and environments spread across statements are still owed"),
    ];
}
