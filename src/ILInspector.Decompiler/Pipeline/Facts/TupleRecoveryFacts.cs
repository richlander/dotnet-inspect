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
            PositiveCoverage: "DeconstructionAssignmentPassTests ValueTuple field-store, mixed fresh/existing local, and Deconstruct-method fixtures",
            AdversarialCoverage: "DeconstructionAssignmentPassTests manual tuple field access, user ValueTuple lookalike, and side-effecting Deconstruct receiver negatives",
            MissingDiscriminator: "nested/rest tuples, non-local existing targets, and temp-then-copy receiver forms still owed"),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.TupleBinaryOperator)),
            typeof(TupleBinaryOperatorPass),
            [
                new FactPrimitive("member.corelib-identity:System.ValueTuple", "MemberIdentity.IsSupportedValueTupleType"),
                new FactPrimitive("pdb.hidden-local", "absence of source local names on operand spills"),
            ],
            PositiveCoverage: "TupleBinaryOperatorPassTests whole-tuple equality fixtures including arity 3, arity-2 whole-tuple inequality, tuple-literal equality/inequality (including arity 3), mixed literal-vs-variable, side-effect-order, and const-element fixtures",
            AdversarialCoverage: "TupleBinaryOperatorPassTests lazy short-circuit comparison, hand-written mixed tuple-field comparison, direct manual field comparison, and source-named local field comparison",
            MissingDiscriminator: "whole-tuple inequality arity 3+ control-flow forms, nested/rest tuples, and no-PDB source-local comparisons still owed"),
    ];
}
