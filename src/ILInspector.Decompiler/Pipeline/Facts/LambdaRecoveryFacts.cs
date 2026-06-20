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
            PositiveCoverage: "LambdaRaisingPassTests non-capturing and capturing fixtures",
            AdversarialCoverage: "LambdaRaisingPassTests generated-name lookalike without metadata",
            MissingDiscriminator: "local-bound bodies and expression trees need additional closure/state facts"),

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
            PositiveCoverage: "LocalFunctionRaisingPassTests static local function fixture",
            AdversarialCoverage: "LocalFunctionRaisingPass guards reject capturing, recursive, nested, unsupported, or local-bodied forms",
            MissingDiscriminator: "capturing, recursive, and nested local functions are still owed"),
    ];
}
