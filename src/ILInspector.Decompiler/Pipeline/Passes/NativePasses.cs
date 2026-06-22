namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The decompiler-native register: the <see cref="IrPasses.Default"/> passes that
/// invert <b>no</b> Roslyn lowering, so they have no <see cref="LoweringCoverage"/>
/// or <see cref="ClosureCoverage"/> entry. The Roslyn-forward registers ask "for
/// each compiler lowering, do we raise it"; this is the inverse — "which of our
/// passes reconstruct what the compiler <em>erased</em> or shaped <em>below</em>
/// the LocalRewriter, rather than un-lowering a named construct."
///
/// <para>Property type = the pass; <see cref="NativeAttribute"/> = what it
/// reconstructs:</para>
/// <list type="bullet">
/// <item><b>EmitArtifact</b> — a codegen/spilling shape the compiler emits below
/// LocalRewriter: temp spilling, control-flow prep, return/constructor spilling,
/// constant-data blobs.</item>
/// <item><b>IlErasure</b> — information the IL type system dropped: constant types,
/// bool-as-int, function-pointer spelling.</item>
/// <item><b>Diagnostic</b> — records an honest residual fidelity gap; does not raise.</item>
/// </list>
///
/// <para><c>LoweringCoverageTests</c> asserts the partition: every
/// <see cref="IrPasses.Default"/> pass type is EITHER a mechanism in a
/// Roslyn-forward register OR here. Internal, trim-removable build-time
/// checklist.</para>
/// </summary>
internal static class NativePasses
{
    // ───────── EmitArtifact — inverse of codegen/spilling, not a LocalRewriter ─────────
    [Native(NativeCategory.EmitArtifact, "inverse of expression spilling — fold single-use temps back into their use")]
    public static ExpressionInliningPass ExpressionInlining => new();
    [Native(NativeCategory.EmitArtifact, "reused evaluation-stack slot live ranges split into distinct typed synthetic carriers")]
    public static StackSlotLiveRangePass StackSlotLiveRange => new();
    [Native(NativeCategory.EmitArtifact, "in-place struct .ctor (ldloca; call S::.ctor) back to s = new S(...)")]
    public static StructConstructorPass StructConstructor => new();
    [Native(NativeCategory.EmitArtifact, "spilled-this base()/this() call back to a chain initializer")]
    public static ConstructorChainPass ConstructorChain => new();
    [Native(NativeCategory.EmitArtifact, "spilled base/this constructor argument back into the chain call")]
    public static ConstructorChainArgumentPass ConstructorChainArgument => new();
    [Native(NativeCategory.EmitArtifact, "shared multi-way return-merge inlined into its arms")]
    public static ReturnMergePass ReturnMerge => new();
    [Native(NativeCategory.EmitArtifact, "whole-method flat guard return dispatch collapsed to ordered guard returns")]
    public static ReturnDispatchPass ReturnDispatch => new();
    [Native(NativeCategory.EmitArtifact, "return-accumulator temp spilled across an EH/lock region eliminated")]
    public static ReturnSinkingPass ReturnSinking => new();
    [Native(NativeCategory.EmitArtifact, "short-circuit OR-guard chains folded so structuring can consume them")]
    public static OrChainGuardPass OrChainGuard => new();
    [Native(NativeCategory.EmitArtifact, "short-circuit OR chain selecting a diamond's two arms folded so structuring can consume it")]
    public static OrChainDiamondPass OrChainDiamond => new();
    [Native(NativeCategory.EmitArtifact, "a spilled-to-slot conditional result (flat slot diamond returning one slot) folded to a conditional return so structuring can consume the surrounding dispatch")]
    public static SlotDiamondPass SlotDiamond => new();
    [Native(NativeCategory.EmitArtifact, "branch-to-adjacent / dead branches removed before structuring")]
    public static RedundantBranchEliminationPass RedundantBranchElimination => new();
    [Native(NativeCategory.EmitArtifact, "identity conversions dropped (ldlen; conv.i4 array-length idiom)")]
    public static IdentityConvertPass IdentityConvert => new();
    [Native(NativeCategory.EmitArtifact, "constant-data RVA blob (exact BCL RuntimeHelpers.CreateSpan) back to a span literal")]
    public static RvaSpanPass RvaSpan => new();
    [Native(NativeCategory.EmitArtifact, "the lazy <>9__ delegate cache (non-capturing lambda / static method group) collapsed to a bare delegate creation")]
    public static LambdaCachePass LambdaCache => new();
    [Native(NativeCategory.EmitArtifact, "the finalizer try/finally + base.Finalize() scaffold emitted for ~T() collapsed back to the destructor body")]
    public static DestructorRecoveryPass DestructorRecovery => new();

    // ───────── IlErasure — reconstruct information the IL type system dropped ─────────
    [Native(NativeCategory.IlErasure, "int constants re-typed to bool/char/enum at typed positions")]
    public static TypedConstantsPass TypedConstants => new();
    [Native(NativeCategory.IlErasure, "bool marshalled as int (cgt against 0) normalized")]
    public static BoolToIntNormalizationPass BoolToIntNormalization => new();
    [Native(NativeCategory.IlErasure, "a standing static function-pointer load (ldftn) spelled &Method")]
    public static MethodAddressPass MethodAddress => new();
    [Native(NativeCategory.IlErasure, "typeof(T) == typeof(U) / typeof type-switch folded")]
    public static TypeOfFoldingPass TypeOfFolding => new();

    // ───────── Diagnostic — record an honest residual fidelity gap (does not raise) ─────────
    [Native(NativeCategory.Diagnostic, "function-pointer load with no C# spelling — DEC0006 residual")]
    public static FunctionPointerDiagnosticsPass FunctionPointerDiagnostics => new();
    [Native(NativeCategory.Diagnostic, "lost in/out/ref kind at a call site — DEC0007 residual")]
    public static RefKindDiagnosticsPass RefKindDiagnostics => new();
    [Native(NativeCategory.Diagnostic, "compiler-generated iterator kickoff acknowledged honestly (Partial) instead of a misleading <X>d__N handoff stub — DEC0004 residual; yield body not yet reconstructed")]
    public static IteratorAcknowledgmentPass IteratorAcknowledgment => new();
}

/// <summary>What a decompiler-native pass reconstructs (it inverts no Roslyn lowering).</summary>
internal enum NativeCategory { EmitArtifact, IlErasure, Diagnostic }

/// <summary>Marks a <see cref="NativePasses"/> entry with what it reconstructs.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class NativeAttribute(NativeCategory category, string what) : Attribute
{
    public NativeCategory Category => category;
    public string What => what;
}
