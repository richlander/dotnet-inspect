namespace ILInspector.Metadata;

/// <summary>
/// The raw, metadata-free string shapes Roslyn and the runtime use for
/// compiler-synthesized names: closure environments, state machines, local
/// functions, lambdas, and hoisted/backing fields. This is shape parsing
/// only — whether a name carrying one of these shapes should be <em>trusted</em>
/// is a separate, consumer-owned policy (for example, requiring
/// <c>[CompilerGenerated]</c> attribute evidence before acting on a name), and
/// is intentionally not decided here. See
/// <see cref="MetadataNameArity"/> for the analogous generic-arity grammar this
/// follows the shape of.
/// </summary>
/// <remarks>
/// Before this type existed, the same shapes were independently re-derived in
/// Analysis (<c>CompilerGeneratedNames</c>), the decompiler
/// (<c>GeneratedCodeIdentity</c>), and Metadata (<c>TypeFilters</c> /
/// <c>MemberFilters</c>), and had already drifted — see issue #4692. Each of
/// those keeps its own policy (attribute gating, declared-owner requirements,
/// API-surface visibility) layered on top of these shared primitives.
/// </remarks>
public static class GeneratedNameGrammar
{
    /// <summary>Closure environment types: <c>&lt;&gt;c__DisplayClass...</c>.</summary>
    public const string DisplayClassPrefix = "<>c__DisplayClass";

    /// <summary>
    /// The non-capturing lambda cache singleton type, named exactly this —
    /// distinct from a display class, which additionally captures state.
    /// </summary>
    public const string NonCapturingLambdaHolderName = "<>c";

    /// <summary>
    /// The static holder type for compiler-synthesized helpers such as string-switch
    /// hash computation and inline-array element-ref intrinsics.
    /// </summary>
    public const string PrivateImplementationDetailsTypeName = "<PrivateImplementationDetails>";

    /// <summary>Dynamic call-site storage container types: <c>&lt;&gt;o__...</c>.</summary>
    public const string DynamicCallSiteContainerPrefix = "<>o__";

    /// <summary>Iterator/async state-machine types: <c>&lt;...&gt;d__...</c>.</summary>
    public const string StateMachineInfix = ">d__";

    /// <summary>Synthesized local-function methods: <c>&lt;...&gt;g__...</c>.</summary>
    public const string LocalFunctionInfix = ">g__";

    /// <summary>Synthesized lambda-body methods: <c>&lt;...&gt;b__...</c>.</summary>
    public const string LambdaInfix = ">b__";

    /// <summary>
    /// Hoisted user-local fields — the lifted form of a source local or
    /// parameter inside a state machine: <c>&lt;name&gt;5__N</c>.
    /// </summary>
    public const string HoistedLocalInfix = ">5__";

    /// <summary>Auto-property backing fields: <c>&lt;Prop&gt;k__BackingField</c>.</summary>
    public const string BackingFieldSuffix = ">k__BackingField";

    /// <summary>
    /// True when a single (non-nested) metadata name uses one of the reserved
    /// prefixes the runtime/Roslyn use for synthesized names: <c>&lt;</c>
    /// (closures, state machines, display classes, hoisted/plumbing fields) or
    /// <c>__</c> (e.g. <c>__StaticArrayInitTypeSize=...</c>).
    /// </summary>
    public static bool IsGeneratedName(string name)
        => name.StartsWith('<') || name.StartsWith("__", StringComparison.Ordinal);

    /// <summary>
    /// The innermost segment of a possibly nested-qualified (<c>+</c>-separated)
    /// metadata name. Metadata readers qualify a nested type as
    /// <c>Outer+Inner</c>, so a synthesized display class or state machine
    /// nested under ordinary types appears as <c>Outer+&lt;Method&gt;d__0</c>;
    /// this returns just <c>&lt;Method&gt;d__0</c>.
    /// </summary>
    public static string LeafSegment(string name)
    {
        int nested = name.LastIndexOf('+');
        return nested < 0 ? name : name[(nested + 1)..];
    }

    /// <summary>
    /// True when a leaf (already-unqualified) type name is a closure
    /// environment: <c>&lt;&gt;c__DisplayClass...</c>. The non-capturing lambda
    /// cache singleton is named exactly <see cref="NonCapturingLambdaHolderName"/>
    /// and does not match this prefix.
    /// </summary>
    public static bool IsDisplayClassLeaf(string leafTypeName)
        => leafTypeName.StartsWith(DisplayClassPrefix, StringComparison.Ordinal);

    /// <summary>
    /// True when a leaf (already-unqualified) type name is an iterator or
    /// async state machine: <c>&lt;...&gt;d__...</c>.
    /// </summary>
    public static bool IsStateMachineLeaf(string leafTypeName)
        => leafTypeName.Contains(StateMachineInfix, StringComparison.Ordinal);

    /// <summary>
    /// True when a method name carries the synthesized local-function infix
    /// <c>&gt;g__</c>, regardless of a leading <c>&lt;</c>.
    /// </summary>
    public static bool IsLocalFunctionMethodName(string methodName)
        => methodName.Contains(LocalFunctionInfix, StringComparison.Ordinal);

    /// <summary>
    /// True when a method name carries the synthesized lambda-body infix
    /// <c>&gt;b__</c>, regardless of a leading <c>&lt;</c>.
    /// </summary>
    public static bool IsLambdaMethodName(string methodName)
        => methodName.Contains(LambdaInfix, StringComparison.Ordinal);

    /// <summary>
    /// A synthesized local-function method name: <c>&lt;Enclosing&gt;g__Name|N_M</c>.
    /// </summary>
    public static bool IsSynthesizedLocalFunctionName(string name)
        => name.StartsWith('<') && IsLocalFunctionMethodName(name);

    /// <summary>
    /// A synthesized lambda-body method name: <c>&lt;Enclosing&gt;b__N_M</c>.
    /// </summary>
    public static bool IsSynthesizedLambdaMethodName(string name)
        => name.StartsWith('<') && IsLambdaMethodName(name);

    /// <summary>
    /// True for any compiler-generated field name — state-machine plumbing
    /// (<c>&lt;&gt;1__state</c>, <c>&lt;&gt;2__current</c>) or a hoisted local
    /// (<c>&lt;i&gt;5__2</c>) alike. The leading <c>&lt;</c> is unspeakable in
    /// C# source, so it alone is reliable evidence a field was synthesized.
    /// </summary>
    public static bool IsGeneratedFieldName(string name)
        => name.StartsWith('<');

    /// <summary>
    /// A hoisted user-local field — <c>&lt;name&gt;5__N</c>, the lifted form of
    /// a source local or parameter inside a state machine. The single
    /// <c>&lt;</c> marks it generated; the absent <c>&lt;&gt;</c> double prefix
    /// and the <see cref="HoistedLocalInfix"/> infix together distinguish it
    /// from pure state-machine plumbing (<c>&lt;&gt;1__state</c>,
    /// <c>&lt;&gt;2__current</c>).
    /// </summary>
    public static bool IsHoistedLocalFieldName(string name)
        => IsGeneratedFieldName(name)
            && !name.StartsWith("<>", StringComparison.Ordinal)
            && name.Contains(HoistedLocalInfix, StringComparison.Ordinal);
}
