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
            PositiveCoverage: "TupleCreationPassTests ValueTuple constructor and RestTuple eight-or-more nested-TRest fixtures",
            AdversarialCoverage: "TupleCreationPassTests user ValueTuple lookalike, expression-statement, and non-inline rest tuple negative fixtures",
            MissingDiscriminator: "tuple element names not recovered"),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.DeconstructionAssignmentOperator)),
            typeof(DeconstructionAssignmentPass),
            [
                new FactPrimitive("member.corelib-identity:System.ValueTuple", "MemberIdentity.IsSupportedValueTupleType"),
                new FactPrimitive("place.stack-slot", "PlaceIdentity.SameStackSlot-equivalent slot ownership checks"),
            ],
            PositiveCoverage: "DeconstructionAssignmentPassTests ValueTuple field-store, mixed fresh/existing local, non-local target (argument, static field, this-instance field, and local/field mixes), and Deconstruct-method fixtures",
            AdversarialCoverage: "DeconstructionAssignmentPassTests manual tuple field access, user ValueTuple lookalike, side-effecting Deconstruct receiver, non-this instance-field target, reused-seed temp, and field-load (temp-copy) Deconstruct receiver negatives",
            MissingDiscriminator: "nested/rest tuples still owed; Deconstruct-method-form non-local targets and non-this instance-field targets are confirmed to decline rather than over-match, so raising those forms is a future slice"),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.TupleBinaryOperator)),
            typeof(TupleBinaryOperatorPass),
            [
                new FactPrimitive("member.corelib-identity:System.ValueTuple", "MemberIdentity.IsSupportedValueTupleType"),
                new FactPrimitive("pdb.hidden-local", "absence of source local names on operand spills"),
            ],
            PositiveCoverage: "TupleBinaryOperatorPassTests whole-tuple equality fixtures including arity 3, arity-2 whole-tuple inequality, tuple-literal equality/inequality (including arity 3), mixed literal-vs-variable, side-effect-order, and const-element fixtures",
            AdversarialCoverage: "TupleBinaryOperatorPassTests lazy short-circuit comparison, hand-written mixed tuple-field comparison, direct manual field comparison, source-named local field comparison, and tuple-equality AND-ed with a trailing unbacked comparison (last-comparison must consume a prologue spill, so `(a,b)==(c,d) && e==f` is not collapsed to arity 3)",
            MissingDiscriminator: "whole-tuple inequality arity 3+ control-flow forms, nested/rest tuples, and no-PDB source-local comparisons still owed"),
    ];
}
