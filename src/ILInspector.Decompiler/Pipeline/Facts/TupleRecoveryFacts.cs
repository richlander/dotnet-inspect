namespace ILInspector.Decompiler.Pipeline;

internal sealed class TupleRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.TupleCreationExpression)),
            typeof(TupleCreationPass),
            [
                new FactPrimitive("member.corelib-identity:System.ValueTuple", "MemberIdentity.IsValueTupleConstructor"),
            ],
            PositiveCoverage: "TupleCreationPassTests ValueTuple constructor fixture",
            AdversarialCoverage: "TupleCreationPassTests user ValueTuple lookalike and expression-statement negative fixtures",
            MissingDiscriminator: "nested/rest tuples and tuple element names not recovered"),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.DeconstructionAssignmentOperator)),
            typeof(DeconstructionAssignmentPass),
            [
                new FactPrimitive("member.corelib-identity:System.ValueTuple", "MemberIdentity.IsSupportedValueTupleType"),
                new FactPrimitive("place.stack-slot", "PlaceIdentity.SameStackSlot-equivalent slot ownership checks"),
            ],
            PositiveCoverage: "DeconstructionAssignmentPassTests ValueTuple field-store and Deconstruct-method fixtures",
            AdversarialCoverage: "DeconstructionAssignmentPassTests manual tuple field access and user ValueTuple lookalike",
            MissingDiscriminator: "nested/rest tuples, mixed declaration/assignment targets, and broader receiver forms still owed"),
    ];
}
