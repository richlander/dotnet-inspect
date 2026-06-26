namespace ILInspector.Decompiler.Pipeline;

internal sealed class LockAndNullRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.LockStatement)),
            typeof(LockSugarPass),
            [
                new FactPrimitive("member.corelib-identity:Monitor.Enter", "MemberIdentity.IsMonitorEnter"),
                new FactPrimitive("member.corelib-identity:Monitor.Exit", "MemberIdentity.IsMonitorExit"),
                new FactPrimitive("ownership.local-references-only-within", "ReferenceOwnership.LocalReferencesOnlyWithin"),
            ],
            PositiveCoverage: "IrImporterTests VoidLock/parameter/static-field lock fixtures and LockSugarSoundnessTests clean synthetic lock shape",
            AdversarialCoverage: "LockSugarSoundnessTests lockTaken external read, user Monitor lookalike, wrong Enter signature, extra finally work, and mismatched Exit object negatives",
            MissingDiscriminator: "static-field receivers intentionally preserve the copied receiver local instead of spelling the field in the lock header"),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.ConditionalAccess)),
            typeof(NullConditionalPass),
            [
                new FactPrimitive("place.variable", "PlaceIdentity.SameVariable"),
                new FactPrimitive("type-shape.non-nullable-value", "TypeFamilies.IsKnownNonNullableValueType"),
            ],
            PositiveCoverage: "IrImporterTests NullConditional_RaisesQuestionDot_AndSoundlyTypesSlot and IdiomShapeScorecardTests NullConditionalPass conditional-access fixture",
            AdversarialCoverage: "NullValueTypeGuardTests re-evaluable and spilled value-type receiver negatives; PlaceIdentityTests SameVariable atom negatives",
            MissingDiscriminator: "nullable value-type ?. forms lower through Nullable<T> and are not represented by the bare null arm this pass raises"),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.NullCoalescingAssignmentOperator)),
            typeof(NullCoalescingAssignmentPass),
            [
                new FactPrimitive("place.variable", "PlaceIdentity.SameVariable"),
                new FactPrimitive("place.operands", "PlaceIdentity.SameOperands"),
                new FactPrimitive("type-shape.non-nullable-value", "TypeFamilies.IsKnownNonNullableValueType"),
                new FactPrimitive("dataflow.stack-slot-single-assignment", "NullCoalescingAssignmentPass.RecoverSpilledValue slot confinement checks"),
            ],
            PositiveCoverage: "NullCoalescingAssignmentPassTests local, field, property, indexer, constant-index, and dictionary-indexer ??= fixtures",
            AdversarialCoverage: "NullCoalescingAssignmentPassTests extra-then, incompatible accessor, mismatched index type/value, and different constant-index negatives; NullValueTypeGuardTests value-type local negative",
            MissingDiscriminator: "nested-struct compound null-coalescing assignment remains outside this pass"),
    ];
}
