using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>How a by-reference argument must be spelled at a C# call site — recovered from the callee's parameter metadata.</summary>
public enum ArgumentRefKind { Value, Ref, Out, In }

/// <summary>A metadata fact whose evidence may be unavailable from the current token.</summary>
public enum MetadataFactState { Unknown, No, Yes }

/// <summary>Whether by-ref parameter keyword metadata was needed and recovered.</summary>
public enum ParameterRefKindFacts { Unknown, NotRequired, Known }

/// <summary>
/// A callee parameter's declared C# default (<c>[Optional]</c> + a <c>Constant</c>
/// row), recovered from metadata and aligned 1:1 with
/// <see cref="MethodRef.ParameterTypes"/>. <see cref="HasDefault"/> is false for a
/// required parameter (and <see cref="Value"/> is then meaningless). When true,
/// <see cref="Value"/> is the typed constant the compiler bakes into every call
/// site (an integral/bool/char/enum arrives boxed as its underlying primitive,
/// a reference/nullable <c>= null</c> default arrives as <see langword="null"/>).
/// The optional-argument elision pass compares a trailing constant argument to
/// this value; equality means the source omitted the argument.
/// </summary>
public readonly record struct ParameterDefault(bool HasDefault, object? Value);

/// <summary>Positive metadata evidence that a method is a property/event accessor.</summary>
public enum AccessorKind { Unknown, None, PropertyGet, PropertySet, EventAdd, EventRemove }

/// <summary>A materialized method reference — callee identity with symbolic types, no metadata handles.</summary>
/// <summary>
/// What <see cref="ILInspector.Decompiler.Pipeline.LocalFunctionRaisingPass"/> decided
/// about a reference to a compiler-synthesized local-function method. Only that pass can
/// answer this: before it runs, a local function that WILL be raised carries exactly the
/// same <c>&lt;Enclosing&gt;g__Name|N_M</c> name as one that will not, so the name shape
/// is not a discriminator (#3631).
/// </summary>
public enum LocalFunctionRaiseState
{
    /// <summary>The pass has not run, or this is not a local-function reference.</summary>
    None,

    /// <summary>
    /// Raised: a <c>Name(...)</c> declaration IS emitted in the host body, so the source
    /// spelling resolves. A reference must be spelled <c>Name</c> UNQUALIFIED — the
    /// declaration is a local function, not a member of the declaring type, so the
    /// <c>Type.Name</c> spelling a static method group would otherwise take is CS0117.
    /// </summary>
    Raised,

    /// <summary>
    /// Declined: no declaration is emitted, so the source spelling resolves to nothing.
    /// The reference is sanitized to keep the compiler-generated identity visible, and
    /// fidelity degrades to <see cref="DecompilationFidelity.Partial"/>.
    /// </summary>
    Declined,
}

internal readonly record struct LambdaOutputInference(
    int TypeArgumentIndex,
    int ArgumentIndex);

public sealed record MethodRef(
    TypeRef DeclaringType,
    string Name,
    TypeRef ReturnType,
    ImmutableArray<TypeRef> ParameterTypes,
    bool HasThis)
{
    internal MetadataMethodAddress? ExactDefinitionAddress { get; init; }
    internal object? ExactDefinitionAcquisitionGuard { get; init; }

    /// <summary>
    /// What <see cref="ILInspector.Decompiler.Pipeline.LocalFunctionRaisingPass"/> decided
    /// about this reference to a compiler-synthesized local function. That pass is the
    /// only component that can answer it: the mangled name alone cannot, because before
    /// the pass runs every local-function reference still carries that name. Consumers
    /// that must not present a source spelling with no matching declaration — the printer
    /// via <see cref="CSharpNaming.SourceMethodName(MethodRef)"/> and
    /// <c>CSharpPrinter</c>'s method-group paths, and fidelity via
    /// <see cref="ILInspector.Decompiler.Pipeline.CSharpSpellability"/> — read this rather
    /// than re-deriving it from display text (#3631). Left
    /// <see cref="LocalFunctionRaiseState.None"/> on IR that has not been through the
    /// pass, where the question is genuinely unanswerable.
    /// </summary>
    public LocalFunctionRaiseState LocalFunctionRaise { get; init; }

    /// <summary>Generic method type arguments (MethodSpec instantiations); empty for non-generic callees.</summary>
    public ImmutableArray<TypeRef> TypeArguments { get; init; } = [];

    /// <summary>
    /// The generic method DEFINITION's parameter types, with its own type
    /// parameters left as <c>!!N</c> placeholders (before the MethodSpec
    /// substitution that produces <see cref="ParameterTypes"/>). Populated only
    /// for a generic method instantiation; empty otherwise. Lets a consumer
    /// identify the source member independent of the instantiation — two calls to
    /// <c>G&lt;int&gt;</c> and <c>G&lt;string&gt;</c> of one <c>G&lt;T&gt;(T)</c>
    /// share this signature though their <see cref="ParameterTypes"/> differ.
    /// </summary>
    public ImmutableArray<TypeRef> DefinitionParameterTypes { get; init; } = [];

    /// <summary>
    /// The generic method definition's return type before MethodSpec
    /// substitution. Populated with <see cref="DefinitionParameterTypes"/> so
    /// return-only generic signature distinctions remain available during
    /// cross-assembly method resolution.
    /// </summary>
    public TypeRef? DefinitionReturnType { get; init; }

    /// <summary>
    /// Whether the method's return type, or a by-ref return's element type, was
    /// authored as <c>dynamic</c>. A dynamic return is encoded as
    /// <c>System.Object</c> plus <c>DynamicAttribute</c> on parameter sequence 0;
    /// MemberRefs do not carry that row, so unresolved callees remain
    /// <see cref="MetadataFactState.Unknown"/>.
    /// </summary>
    public MetadataFactState ReturnIsDynamic { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Whether an array return's element type was authored as <c>dynamic</c>.
    /// </summary>
    public MetadataFactState ReturnArrayElementIsDynamic { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Per-parameter call-site ref-kind (ref/out/in), aligned 1:1 with
    /// <see cref="ParameterTypes"/>. Populated for callees resolved as a
    /// MethodDef, from the parameter rows (IsReadOnlyAttribute / the Out flag),
    /// either directly or by resolving a MemberRef through the metadata context.
    /// Empty means either no by-ref facts were needed or the rows were
    /// unreachable; <see cref="ParameterRefKindsFacts"/> disambiguates.
    /// </summary>
    public ImmutableArray<ArgumentRefKind> ParameterRefKinds { get; init; } = [];

    /// <summary>
    /// Whether <see cref="ParameterRefKinds"/> is known. Distinguishes "no by-ref
    /// parameters needed spelling facts" from "a MemberRef did not expose rows."
    /// </summary>
    public ParameterRefKindFacts ParameterRefKindsFacts { get; init; } = ParameterRefKindFacts.Unknown;

    /// <summary>
    /// True when at least one parameter declaration is <c>ref readonly</c>.
    /// Its call-site behavior is represented by <see cref="ArgumentRefKind.In"/>,
    /// but declaration-producing raises must decline until their declaration
    /// model can preserve the distinct keyword.
    /// </summary>
    public bool HasRefReadOnlyParameters { get; init; }

    /// <summary>
    /// Per-parameter C# defaults (<c>[Optional]</c> + <c>Constant</c>), aligned
    /// 1:1 with <see cref="ParameterTypes"/>. Empty means the defaults were not
    /// resolved (the pipeline populates this only for same-assembly
    /// <c>MethodDefinition</c> callees; cross-assembly and generic callees stay
    /// empty and never elide). Consumed by the optional-argument elision pass.
    /// </summary>
    public ImmutableArray<ParameterDefault> ParameterDefaults { get; init; } = [];

    /// <summary>
    /// How many trailing arguments the optional-argument elision pass may drop:
    /// the length of the trailing run of defaulted parameters that is also
    /// <em>overload-safe</em> — no other same-named overload on the declaring
    /// type could rebind the shortened call. Zero (the default) disables elision.
    /// A sound conservative under-approximation: the count only rises when every
    /// competing overload is confidently rejected, so an unresolved or ambiguous
    /// case declines. The recompile/corpus fidelity gate is the empirical
    /// backstop for any residual overload-resolution miss.
    /// </summary>
    public int SafeTrailingElidableCount { get; init; }

    /// <summary>
    /// The callee is a constructor with no observable effect beyond allocating the
    /// fresh instance: its body is exactly <c>ldarg.0; call instance void
    /// System.Object::.ctor(); ret</c> (a direct-<c>Object</c> parameterless ctor
    /// that touches nothing — no field writes, no other calls, no static access, no
    /// branches, no exception regions) AND its declaring type declares no static
    /// constructor, so <c>newobj</c> triggers no type-initializer side effect. Set
    /// only for a same-assembly <see cref="System.Reflection.Metadata.MethodDefinitionHandle"/>
    /// constructor whose body proves the shape (see
    /// <see cref="ConstructorConfinementFacts"/>); a cross-assembly, unresolvable,
    /// or non-trivial ctor stays <see langword="false"/>.
    ///
    /// <para>Consumed by <see cref="Passes.ObjectInitializerPass"/> to admit hoisting
    /// the enclosing call's <c>this</c>-field receiver read across the <c>newobj</c>
    /// when folding an object-initializer argument. The proof is <em>Roslyn-faithful</em>,
    /// not arbitrary-IL-sound: it assumes a non-null <c>this</c> (a hand-crafted
    /// <c>call</c> with null <c>this</c> could make the receiver read throw), matching
    /// the compiler-emitted IL the decompiler targets.</para>
    /// </summary>
    public bool ConstructorEffectFree { get; init; }

    /// <summary>
    /// The callee is <em>requires-unsafe</em>: under the updated memory-safety
    /// rules a member declared <c>unsafe</c>/<c>extern</c> is stamped with
    /// <c>RequiresUnsafeAttribute</c>, and every call site needs an unsafe
    /// context even when no pointer crosses the call boundary. Recovered from
    /// the callee MethodDef's attributes when reachable. The printer's
    /// signature-pointer heuristic covers the compat-mode cross-assembly case
    /// when the attribute cannot be read.
    /// </summary>
    public bool RequiresUnsafe { get; init; }

    /// <summary>
    /// Whether Metadata's normalized module/member contract proves that invoking
    /// this method does or does not require unsafe context.
    /// A known <see cref="MetadataFactState.No"/> is significant under updated
    /// rules: pointer signature shape alone must not recreate a caller contract.
    /// Unresolved MemberRefs remain <see cref="MetadataFactState.Unknown"/>, where
    /// the compatibility signature fallback still applies.
    /// </summary>
    public MetadataFactState RequiresUnsafeFact { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// The callee module's normalized memory-safety model. Invalid states are
    /// retained so fidelity accounting can visibly decline instead of treating a
    /// direct member attribute as authoritative outside a supported module model.
    /// </summary>
    public MemorySafetyRulesState? MemorySafetyRulesState { get; init; }
    public bool MemorySafetyRulesUnavailable { get; init; }
    public bool MemorySafetyContractUnavailable { get; init; }

    /// <summary>
    /// Metadata SpecialName evidence (accessors, operators, constructors). Exact
    /// for MethodDefs; unresolved MemberRefs carry no flags, so the importer may
    /// infer this from compiler-reserved names only to preserve spellability
    /// diagnostics. C# property/event/operator sugar must use narrower facts.
    /// </summary>
    public bool IsSpecialName { get; init; }

    /// <summary>
    /// True when <see cref="IsSpecialName"/> came from a MemberRef name prefix
    /// rather than MethodDef metadata. The flag preserves the distinction between
    /// "could be an accessor/operator if metadata resolves" and "metadata proves
    /// SpecialName" for final spellability checks.
    /// </summary>
    public bool IsSpecialNameInferred { get; init; }

    /// <summary>
    /// Positive metadata evidence that this method is a user-defined C# operator.
    /// MemberRefs may carry name-inferred <see cref="IsSpecialName"/>, but resolved
    /// non-operators use this fact to keep explicit method-call spelling.
    /// </summary>
    public MetadataFactState IsOperator { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Exact property/event accessor evidence from metadata semantics. MemberRefs
    /// whose defining method cannot be resolved stay <see cref="AccessorKind.Unknown"/>
    /// even when their name looks like an accessor; raising property/event syntax
    /// requires a positive value here, not name-inferred <see cref="IsSpecialName"/>.
    /// </summary>
    public AccessorKind AccessorKind { get; init; } = AccessorKind.Unknown;

    /// <summary>
    /// Metadata <c>[CompilerGenerated]</c> evidence on this method, or
    /// <see cref="MetadataFactState.Unknown"/> when the defining MethodDef was
    /// unreachable from the call-site token.
    /// </summary>
    public MetadataFactState CompilerGenerated { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Metadata <c>[CompilerGenerated]</c> evidence on the declaring type, or
    /// <see cref="MetadataFactState.Unknown"/> when the defining TypeDef was
    /// unreachable from the call-site token.
    /// </summary>
    public MetadataFactState DeclaringTypeCompilerGenerated { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Whether the declaring type is a delegate type. The constructor shape
    /// <c>.ctor(object, IntPtr)</c> is not unique to delegates, so delegate
    /// construction raising requires this exact type-shape fact instead of the
    /// signature alone.
    /// </summary>
    public MetadataFactState DeclaringTypeIsDelegate { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// The callee's declaring type was reached through a <em>trusted-platform</em>
    /// assembly reference — a reference whose public-key token is a framework token
    /// (verified at import via <see cref="ILInspector.Metadata.PlatformKeys"/>),
    /// covering both a direct platform reference and a framework forwarding facade.
    /// The simple assembly name is forgeable, so a consumer that must not confuse a
    /// planted lookalike (an unsigned assembly literally named
    /// <c>System.Linq.Expressions</c> defining its own <c>Expression</c>) with the
    /// real framework type keys off this token-anchored fact rather than the name.
    /// <see cref="MetadataFactState.Yes"/> only for token-verified platform
    /// MemberRefs; <see cref="MetadataFactState.Unknown"/> otherwise (a MethodDef,
    /// MethodSpec, or a non-platform MemberRef never sets it).
    /// </summary>
    public MetadataFactState DeclaringTypeIsTrustedPlatform { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Metadata <c>[Extension]</c> evidence on this method: it is an extension
    /// method, so a static call <c>C.M(receiver, args)</c> can render as the
    /// instance form <c>receiver.M(args)</c> the source almost certainly used.
    /// <see cref="MetadataFactState.Unknown"/> until the defining MethodDef is
    /// read (same-assembly at import, cross-assembly through the resolver); the
    /// printer sugars only on <see cref="MetadataFactState.Yes"/>, so an
    /// unresolved callee keeps the explicit static spelling — never a wrong one.
    /// </summary>
    public MetadataFactState IsExtension { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// True only when metadata and the imported call prove that C# can infer
    /// every method type argument from the extension receiver and supported
    /// lambda outputs without changing overload selection. Lambda-output
    /// obligations are revalidated against final raised expressions by the
    /// printer.
    /// </summary>
    internal bool CanOmitTypeArguments { get; init; }

    /// <summary>
    /// Method type arguments not fixed by the receiver, each paired with the
    /// <c>Func</c> argument whose direct result position must infer it. Empty
    /// when the receiver fixes every type argument.
    /// </summary>
    internal ImmutableArray<LambdaOutputInference> TypeArgumentElisionLambdaOutputs { get; init; } = [];

    /// <summary>
    /// Whether the exact declaring type has no competing same-name overload
    /// that could accept this call's fixed argument count after type-argument
    /// inference.
    /// </summary>
    internal MetadataFactState TypeArgumentElisionOverloadSafety { get; init; }

    /// <summary>
    /// Same-name overload signatures that explicit type arguments and the
    /// supported inference sources both admit. Their non-receiver arguments
    /// must independently preserve the recorded method instantiation before
    /// elision is safe.
    /// </summary>
    internal ImmutableArray<ImmutableArray<TypeRef>> TypeArgumentElisionSiblingParameters { get; init; } = [];

    /// <summary>
    /// Metadata PInvokeImpl / <c>[DllImport]</c> evidence on this method. Taking
    /// such a target as <c>&amp;Method</c> is not a source-equivalent
    /// UnmanagedCallersOnly callback address, so method-address raising declines
    /// it when this fact is positively known.
    /// </summary>
    public MetadataFactState IsPInvoke { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Metadata runtime-async evidence (<c>MethodImplAttributes.Async</c>, flag
    /// <c>0x2000</c>) on this method. Runtime-async methods are not valid native
    /// callback targets; method-address raising declines them when this fact is
    /// positively known.
    /// </summary>
    public MetadataFactState IsRuntimeAsync { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Metadata <c>[UnmanagedCallersOnly]</c> evidence on this method. Such a
    /// method is addressable only as an <em>unmanaged</em> <c>delegate*</c>; a
    /// normal managed method is addressable only as a managed <c>delegate*</c>.
    /// The calli-spellability gate uses this to reject casting an <c>&amp;Method</c>
    /// address to a mismatched convention class (CS8757) when only
    /// <see cref="MetadataFactState.Yes"/>/<see cref="MetadataFactState.No"/> is
    /// known.
    /// </summary>
    public MetadataFactState IsUnmanagedCallersOnly { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// True when a managed-reference or unmanaged-pointer argument is passed to
    /// a by-ref parameter of this callee while <see cref="ParameterRefKinds"/> is
    /// empty — the callee resolved as a MemberReference (cross-assembly, or a
    /// same-assembly call on a generic type instance), which carries no parameter
    /// rows, so the call-site <c>out</c>/<c>in</c>/<c>ref</c> kind is unknown.
    /// The printer then spells a default keyword it cannot verify (wrong for
    /// out/in: CS1620/CS1615), so callers lower fidelity rather than claim a
    /// faithful render. <paramref name="nonReceiverArguments"/> aligns 1:1 with
    /// <see cref="ParameterTypes"/> (the instance receiver dropped first).
    /// </summary>
    public bool HasUnverifiableByRefArgument(IReadOnlyList<IrExpression> nonReceiverArguments)
    {
        if (ParameterRefKindsFacts != ParameterRefKindFacts.Unknown || !ParameterRefKinds.IsDefaultOrEmpty)
            return false;
        for (int i = 0; i < ParameterTypes.Length && i < nonReceiverArguments.Count; i++)
            if (ParameterTypes[i].Kind == TypeRefKind.ByRef
                && nonReceiverArguments[i].ResultType is
                    { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer })
                return true;
        return false;
    }

    internal bool TryGetVerifiedOutLocal(
        int parameterIndex,
        IrExpression argument,
        out int local)
    {
        local = -1;
        if (ParameterRefKindsFacts != ParameterRefKindFacts.Known
            || parameterIndex < 0
            || parameterIndex >= ParameterRefKinds.Length
            || ParameterRefKinds[parameterIndex] != ArgumentRefKind.Out
            || argument is not LoadLocalAddress address)
        {
            return false;
        }

        local = address.Index;
        return true;
    }
}

/// <summary>Compiler fixed-buffer source-field metadata decoded from <c>FixedBufferAttribute</c>.</summary>
public sealed record FixedBufferFieldInfo(TypeRef ElementType, int Length);

/// <summary>A materialized field reference.</summary>
public sealed record FieldRef(TypeRef DeclaringType, string Name, TypeRef Type)
{
    /// <summary>
    /// The generic field definition's type before substituting a TypeSpec
    /// parent's type arguments. Retained for exact cross-assembly FieldDef
    /// resolution when the effective <see cref="Type"/> contains constructed
    /// types whose assembly aliases differ between reference and runtime
    /// metadata.
    /// </summary>
    public TypeRef? DefinitionType { get; init; }

    /// <summary>
    /// Whether Metadata resolved this exact FieldDef through the defining
    /// module's normalized memory-safety index. A resolved legacy no-contract
    /// field can still have <see cref="RequiresUnsafeFact"/> unknown because
    /// legacy compatibility is derived from field shape.
    /// </summary>
    public bool HasNormalizedMemorySafetyContract { get; init; }

    /// <summary>
    /// True when the normalized field contract is explicit. Updated callers
    /// enforce explicit contracts; legacy callers do not.
    /// </summary>
    public bool RequiresUnsafe { get; init; }

    /// <summary>
    /// Whether the normalized field contract positively requires or excludes an
    /// unsafe caller context. Legacy no-contract fields retain Unknown and use
    /// their pointer shape only after normalized legacy rules are established.
    /// </summary>
    public MetadataFactState RequiresUnsafeFact { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// The defining module's normalized memory-safety model. Invalid and
    /// unavailable states remain visible so fidelity cannot treat them as a
    /// negative field contract.
    /// </summary>
    public MemorySafetyRulesState? MemorySafetyRulesState { get; init; }
    public bool MemorySafetyRulesUnavailable { get; init; }
    public bool MemorySafetyContractUnavailable { get; init; }

    /// <summary>
    /// Positive metadata evidence that an auto-property backing-field-shaped name
    /// has a corresponding property. Null means no proof, not proof of absence.
    /// </summary>
    public string? BackingPropertyName { get; init; }
    public MetadataFactState DeclaringTypeCompilerGenerated { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// True when this field's top-level type was authored as <c>dynamic</c> (a
    /// <c>System.Object</c> position carrying <c>[DynamicAttribute]</c>) — most
    /// notably a captured <c>dynamic</c> local/parameter hoisted into a
    /// <c>&lt;&gt;c__DisplayClass</c> field. The printer uses this to drop a
    /// redundant <c>(dynamic)</c> cast when a load of this field is the receiver
    /// of a raised dynamic member access.
    /// </summary>
    public bool IsDynamic { get; init; }

    /// <summary>
    /// Whether this field's top-level type was authored as <c>dynamic</c>.
    /// MemberRefs do not carry the defining field's custom attributes, so an
    /// unresolved <c>object</c>-typed field remains
    /// <see cref="MetadataFactState.Unknown"/>.
    /// </summary>
    public MetadataFactState DynamicFact { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Whether an array field's element type was authored as <c>dynamic</c>.
    /// </summary>
    public MetadataFactState ArrayElementIsDynamic { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Positive metadata evidence that this field is a C# fixed buffer source
    /// field. Null means no proof, not proof of absence.
    /// </summary>
    public FixedBufferFieldInfo? FixedBuffer { get; init; }
}
