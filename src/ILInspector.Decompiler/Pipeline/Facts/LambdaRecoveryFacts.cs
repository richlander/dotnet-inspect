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
            PositiveCoverage: "LambdaRaisingPassTests non-capturing, capturing, nested-lambda, captured-local materialization, non-capturing local-bodied, and parameter-capturing local-bodied fixtures",
            AdversarialCoverage: "LambdaRaisingPassTests generated-name lookalike without metadata and outer-local capturing local-bodied guard",
            MissingDiscriminator: "outer-local capturing bodies with their own local storage need additional closure/state facts"),

        new(
            new LoweringFactKey(LoweringFactRegister.ClosureConversion, nameof(ClosureCoverage.CapturedClosure)),
            typeof(LambdaRaisingPass),
            [
                new FactPrimitive("generated-type:display-class", "GeneratedCodeIdentity.IsCapturingLambdaMethod"),
                new FactPrimitive("place.re-evaluable", "PlaceIdentity same-place atoms for safe environment substitution"),
            ],
            PositiveCoverage: "LambdaRaisingPassTests folded captures, local display-class environments, captured-local materialization, shared captures, and parameter-capturing local-bodied fixtures",
            AdversarialCoverage: "LambdaRaisingPassTests guards for unsupported outer-local local-bodied and generated-name lookalike forms",
            MissingDiscriminator: "nested display-class environments and display classes captured by local functions remain owed"),

        new(
            new LoweringFactKey(LoweringFactRegister.ClosureConversion, nameof(ClosureCoverage.LocalFunction)),
            typeof(LocalFunctionRaisingPass),
            [
                new FactPrimitive("generated-method:local-function", "GeneratedCodeIdentity.IsLocalFunctionMethod"),
                new FactPrimitive("cross-method-import", "PassContext.ImportMethodBody"),
            ],
            PositiveCoverage: "LocalFunctionRaisingPassTests static, self-recursive, mutually recursive static, static local-bodied, and capturing fixtures, each called once and more than once where applicable",
            AdversarialCoverage: "LocalFunctionRaisingPass guards reject shared environments, one-way and externally dependent components, non-call component references, direct component nesting, post-mutation captures, unsupported bodies, and capturing local-bodied forms",
            MissingDiscriminator: "capturing dependency components, nested local functions outside admitted static components, and environments spread across statements are still owed"),
    ];
}
