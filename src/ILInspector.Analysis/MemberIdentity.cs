using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

public enum MemberKind
{
    Method,
    Constructor,
    FunctionPointer,
    Unsupported,
}

public enum CallKind
{
    Call,
    CallVirtual,
    NewObject,
    LoadFunction,
    LoadVirtualFunction,
    CallIndirect,
}

internal enum ParameterDirection
{
    Value,
    Ref,
    Out,
    In,
    UnknownByRef,
}

/// <summary>
/// A member's safety under the updated memory-safety rules, mirroring Roslyn's
/// <c>CallerUnsafeMode</c> (see <c>PEMethodSymbol.CallerUnsafeMode</c>). A member
/// "requires unsafe" when it carries <c>RequiresUnsafeAttribute</c> or has a
/// pointer / function pointer in its signature; the distinction below is then
/// gated on whether the containing module opted into the updated rules via
/// <c>MemorySafetyRulesAttribute</c>.
/// </summary>
public enum CallerUnsafeMode
{
    /// <summary>Not considered unsafe under the updated rules.</summary>
    None,

    /// <summary>
    /// Requires unsafe, but the module has not opted into the updated rules — the
    /// legacy implicit notion (e.g. an existing pointer-signature API).
    /// </summary>
    Implicit,

    /// <summary>
    /// Requires unsafe in a module that opted into the updated rules — the
    /// authoritative mark (the <c>unsafe</c>/<c>extern</c> modifier).
    /// </summary>
    Explicit,
}

/// <summary>
/// How many methods in an assembly fall into each <see cref="CallerUnsafeMode"/>,
/// counted across every method (including bodiless extern/abstract members), not
/// just those with an IL body.
/// </summary>
public sealed record UnsafeModeBreakdown(int None, int Implicit, int Explicit)
{
    public int Total => None + Implicit + Explicit;

    /// <summary>Methods that require unsafe (implicitly or explicitly).</summary>
    public int Unsafe => Implicit + Explicit;
}

public sealed record MethodIdentity(
    string AssemblyName,
    Guid ModuleVersionId,
    TypeRef DeclaringType,
    string Name,
    ImmutableArray<TypeRef> ParameterTypes,
    TypeRef ReturnType,
    int MetadataToken,
    bool IsStatic,
    bool IsExtension = false,
    CallerUnsafeMode CallerUnsafeMode = CallerUnsafeMode.None,
    int GenericArity = 0,
    ImmutableArray<string> GenericParameterNames = default)
{
    ImmutableArray<TypeRef> _parameterTypes
        = ImmutableArrayValueEquality.RequireInitialized(ParameterTypes, nameof(ParameterTypes));
    ImmutableArray<string> _genericParameterNames
        = ImmutableArrayValueEquality.EmptyIfDefault(GenericParameterNames, nameof(GenericParameterNames));

    public ImmutableArray<TypeRef> ParameterTypes
    {
        get => _parameterTypes;
        init => _parameterTypes = ImmutableArrayValueEquality.RequireInitialized(value, nameof(ParameterTypes));
    }

    public ImmutableArray<string> GenericParameterNames
    {
        get => _genericParameterNames;
        init => _genericParameterNames = ImmutableArrayValueEquality.EmptyIfDefault(value, nameof(GenericParameterNames));
    }

    internal byte SignatureHeader { get; init; }
    internal int RequiredParameterCount { get; init; } = -1;
    internal bool IsVirtualDispatchOpen { get; init; }

    public bool Equals(MethodIdentity? other)
        => other is not null
            && AssemblyName == other.AssemblyName
            && ModuleVersionId == other.ModuleVersionId
            && Equals(DeclaringType, other.DeclaringType)
            && Name == other.Name
            && ImmutableArrayValueEquality.SequenceEqual(ParameterTypes, other.ParameterTypes)
            && Equals(ReturnType, other.ReturnType)
            && MetadataToken == other.MetadataToken
            && IsStatic == other.IsStatic
            && IsExtension == other.IsExtension
            && CallerUnsafeMode == other.CallerUnsafeMode
            && GenericArity == other.GenericArity
            && (SignatureHeader & 0x4F)
                == (other.SignatureHeader & 0x4F)
            && ((SignatureHeader & 0x0F) != 0x05
                || RequiredParameterCount
                    == other.RequiredParameterCount)
            && ImmutableArrayValueEquality.SequenceEqual(
                GenericParameterNames,
                other.GenericParameterNames);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AssemblyName);
        hash.Add(ModuleVersionId);
        hash.Add(DeclaringType);
        hash.Add(Name);
        ImmutableArrayValueEquality.AddToHash(ref hash, ParameterTypes);
        hash.Add(ReturnType);
        hash.Add(MetadataToken);
        hash.Add(IsStatic);
        hash.Add(IsExtension);
        hash.Add(CallerUnsafeMode);
        hash.Add(GenericArity);
        hash.Add(SignatureHeader & 0x4F);
        if ((SignatureHeader & 0x0F) == 0x05)
            hash.Add(RequiredParameterCount);
        ImmutableArrayValueEquality.AddToHash(ref hash, GenericParameterNames);
        return hash.ToHashCode();
    }
}

public sealed record MemberRef(
    TypeRef DeclaringType,
    string Name,
    ImmutableArray<TypeRef> ParameterTypes,
    TypeRef ReturnType,
    MemberKind Kind)
{
    ImmutableArray<TypeRef> _parameterTypes
        = ImmutableArrayValueEquality.RequireInitialized(ParameterTypes, nameof(ParameterTypes));
    ImmutableArray<TypeRef> _typeArguments = [];
    ImmutableArray<TypeRef> _openParameterTypes = [];
    ImmutableArray<ParameterDirection>
        _parameterDirections = [];

    public ImmutableArray<TypeRef> ParameterTypes
    {
        get => _parameterTypes;
        init => _parameterTypes = ImmutableArrayValueEquality.RequireInitialized(value, nameof(ParameterTypes));
    }

    public ImmutableArray<TypeRef> TypeArguments
    {
        get => _typeArguments;
        init => _typeArguments = ImmutableArrayValueEquality.RequireInitialized(value, nameof(TypeArguments));
    }

    internal ImmutableArray<ParameterDirection>
        ParameterDirections
    {
        get => _parameterDirections;
        init => _parameterDirections =
            ImmutableArrayValueEquality
                .RequireInitialized(
                    value,
                    nameof(ParameterDirections));
    }

    /// <summary>
    /// True when the method has a `this` parameter (instance call), so a call site pops one
    /// extra value (the receiver) beyond <see cref="ParameterTypes"/>. Used by stack-effect
    /// reasoning over call sites.
    /// </summary>
    public bool HasThis { get; init; }

    /// <summary>
    /// Raw ECMA-335 method-signature header. This preserves calling convention and
    /// <c>explicitthis</c>/<c>generic</c> flags that parameter and return types do not encode.
    /// </summary>
    public byte SignatureHeader { get; init; }

    internal int RequiredParameterCount { get; init; } = -1;

    // Candidate metadata only; this affects rewrite compatibility, not method identity.
    internal bool TrailingParameterCanBeOmitted { get; init; }

    /// <summary>The method-signature generic parameter count.</summary>
    public int GenericArity { get; init; }

    /// <summary>
    /// The parameter signature with generic markers (VAR/MVAR) preserved — i.e. before
    /// the declaring-type / method-type instantiation that <see cref="ParameterTypes"/>
    /// carries. Cross-assembly caller-graph identity keys on this so a constructed call
    /// site and the open definition reduce to the same generic shape, and a literal type
    /// stays distinct from a type-parameter instantiation (#1731). Empty when not
    /// separately captured, in which case <see cref="OpenSignatureParameters"/> falls
    /// back to <see cref="ParameterTypes"/>.
    /// </summary>
    public ImmutableArray<TypeRef> OpenParameterTypes
    {
        get => _openParameterTypes;
        init => _openParameterTypes = ImmutableArrayValueEquality.RequireInitialized(value, nameof(OpenParameterTypes));
    }

    public ImmutableArray<TypeRef> OpenSignatureParameters
        => OpenParameterTypes.IsDefaultOrEmpty ? ParameterTypes : OpenParameterTypes;

    /// <summary>
    /// The return type with generic markers preserved (before instantiation), for the
    /// same cross-assembly keying reason as <see cref="OpenParameterTypes"/> (#1741).
    /// Null when not separately captured, in which case <see cref="OpenSignatureReturn"/>
    /// falls back to <see cref="ReturnType"/>.
    /// </summary>
    public TypeRef? OpenReturnType { get; init; }

    public TypeRef OpenSignatureReturn => OpenReturnType ?? ReturnType;

    public string ToQualifiedDisplayString()
        => $"{DeclaringType.ToQualifiedDisplayString()}::{Name}";

    public bool Equals(MemberRef? other)
        => other is not null
            && Equals(DeclaringType, other.DeclaringType)
            && Name == other.Name
            && ImmutableArrayValueEquality.SequenceEqual(ParameterTypes, other.ParameterTypes)
            && Equals(ReturnType, other.ReturnType)
            && Kind == other.Kind
            && ImmutableArrayValueEquality.SequenceEqual(TypeArguments, other.TypeArguments)
            && HasThis == other.HasThis
            && SignatureHeader == other.SignatureHeader
            && ((SignatureHeader & 0x0F) != 0x05
                || RequiredParameterCount
                    == other.RequiredParameterCount)
            && GenericArity == other.GenericArity
            && ImmutableArrayValueEquality.SequenceEqual(OpenParameterTypes, other.OpenParameterTypes)
            && Equals(OpenReturnType, other.OpenReturnType);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeclaringType);
        hash.Add(Name);
        ImmutableArrayValueEquality.AddToHash(ref hash, ParameterTypes);
        hash.Add(ReturnType);
        hash.Add(Kind);
        ImmutableArrayValueEquality.AddToHash(ref hash, TypeArguments);
        hash.Add(HasThis);
        hash.Add(SignatureHeader);
        if ((SignatureHeader & 0x0F) == 0x05)
            hash.Add(RequiredParameterCount);
        hash.Add(GenericArity);
        ImmutableArrayValueEquality.AddToHash(ref hash, OpenParameterTypes);
        hash.Add(OpenReturnType);
        return hash.ToHashCode();
    }

    public static MemberRef Unsupported(string reason)
        => new(TypeRef.Unsupported(reason), "?", [], TypeRef.Unsupported("unknown return"), MemberKind.Unsupported);
}

static class ImmutableArrayValueEquality
{
    public static ImmutableArray<T> RequireInitialized<T>(
        ImmutableArray<T> values,
        string parameterName)
        where T : notnull
    {
        if (values.IsDefault)
            throw new ArgumentException("Collection must be initialized.", parameterName);
        if (values.Any(value => value is null))
            throw new ArgumentException("Collection must not contain null values.", parameterName);
        return values;
    }

    public static ImmutableArray<T> EmptyIfDefault<T>(
        ImmutableArray<T> values,
        string parameterName)
        where T : notnull
        => RequireInitialized(values.IsDefault ? [] : values, parameterName);

    public static bool SequenceEqual<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
        => left.SequenceEqual(right, EqualityComparer<T>.Default);

    public static void AddToHash<T>(
        ref HashCode hash,
        ImmutableArray<T> values)
    {
        hash.Add(values.Length);
        foreach (var value in values)
            hash.Add(value, EqualityComparer<T>.Default);
    }
}

/// <summary>This IL instruction definitely references this metadata token.</summary>
/// <param name="OperandToken">The raw metadata token in the call instruction's operand (may be a MethodSpec).</param>
/// <param name="CalleeDefinitionToken">
/// The intra-assembly <c>MethodDef</c> token the call targets. For a generic-method
/// call the operand is a <c>MethodSpec</c> (the instantiation); this peels it to the
/// underlying method definition so callers can be matched by token. Equal to
/// <paramref name="OperandToken"/> when no peeling applies.
/// </param>
public sealed record DirectCall(
    MethodIdentity Caller,
    MemberRef Callee,
    int ILOffset,
    int OperandToken,
    int CalleeDefinitionToken,
    CallKind Kind,
    bool InLoop = false)
{
    /// <summary>
    /// Method body containing the physical IL instruction. This differs from
    /// <see cref="Caller"/> when a synthesized body is attributed to its
    /// declared source method.
    /// </summary>
    public MethodIdentity EvidenceMethod { get; init; } = Caller;
    public string Opcode { get; init; } = "";
    public int? ReturnAddress { get; init; }
    public AllocationMultiplicity Multiplicity { get; init; }
    public bool ExactTarget { get; init; }
    /// <summary>
    /// Conservative use of the value produced by this call.
    /// <c>MethodCallAnalysisTests.ClassifiesReturnedAndDiscardedCallResults</c>
    /// gates direct uses; unresolved flows remain <see cref="DirectCallResultUse.Unknown"/>.
    /// </summary>
    public DirectCallResultUse ResultUse { get; init; }
    /// <summary>
    /// IL offset of the return, discard, or consuming call identified by
    /// <see cref="ResultUse"/>.
    /// </summary>
    public int? ResultConsumerOffset { get; init; }
    /// <summary>
    /// Direct-call provenance for each declared argument when Analysis can
    /// interpret the body evaluation stack. Unknown, raw, or merged values
    /// have <see cref="CallArgumentSource.IsComplete"/> set to false.
    /// <c>MethodCallAnalysisTests.RejectsMergedEvaluationStackResultSources</c>
    /// gates the fail-closed boundary.
    /// </summary>
    public CallArgumentSources ArgumentSources { get; init; } =
        CallArgumentSources.Empty;
}

/// <summary>Conservative disposition of a direct call's produced value.</summary>
public enum DirectCallResultUse
{
    Unknown,
    MethodReturn,
    CallArgument,
    Discarded,
}

/// <summary>
/// Conservative provenance for one declared direct-call argument.
/// </summary>
/// <param name="ArgumentIndex">Zero-based declared parameter position.</param>
/// <param name="SourceCallOffsets">
/// Physical IL offsets of every directly proven non-void call-result source.
/// Empty is valid only when <paramref name="IsComplete"/> is false.
/// </param>
/// <param name="IsComplete">
/// Whether every control-flow and evaluation-stack source of the argument was
/// proven to be a direct non-void call result.
/// </param>
public sealed class CallArgumentSource :
    IEquatable<CallArgumentSource>
{
    public CallArgumentSource(
        int argumentIndex,
        ImmutableArray<int> sourceCallOffsets,
        bool isComplete)
    {
        ArgumentIndex = argumentIndex;
        SourceCallOffsets =
            ImmutableArrayValueEquality.RequireInitialized(
                sourceCallOffsets,
                nameof(sourceCallOffsets));
        IsComplete = isComplete;
    }

    public int ArgumentIndex { get; }

    public ImmutableArray<int> SourceCallOffsets { get; }

    public bool IsComplete { get; }

    public bool Equals(CallArgumentSource? other)
        => other is not null
            && ArgumentIndex == other.ArgumentIndex
            && IsComplete == other.IsComplete
            && ImmutableArrayValueEquality.SequenceEqual(
                SourceCallOffsets,
                other.SourceCallOffsets);

    public override bool Equals(object? obj)
        => obj is CallArgumentSource other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ArgumentIndex);
        ImmutableArrayValueEquality.AddToHash(
            ref hash,
            SourceCallOffsets);
        hash.Add(IsComplete);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Equality-stable collection of <see cref="CallArgumentSource"/> values.
/// </summary>
public sealed class CallArgumentSources :
    IReadOnlyList<CallArgumentSource>,
    IEquatable<CallArgumentSources>
{
    readonly ImmutableArray<CallArgumentSource> _sources;

    public static CallArgumentSources Empty { get; } = new([]);

    public CallArgumentSources(
        ImmutableArray<CallArgumentSource> sources)
    {
        _sources = ImmutableArrayValueEquality.RequireInitialized(
            sources,
            nameof(sources));
    }

    public int Count => _sources.Length;

    public CallArgumentSource this[int index] => _sources[index];

    public IEnumerator<CallArgumentSource> GetEnumerator()
        => ((IEnumerable<CallArgumentSource>)_sources).GetEnumerator();

    System.Collections.IEnumerator
        System.Collections.IEnumerable.GetEnumerator()
        => GetEnumerator();

    public bool Equals(CallArgumentSources? other)
        => other is not null
            && ImmutableArrayValueEquality.SequenceEqual(
                _sources,
                other._sources);

    public override bool Equals(object? obj)
        => obj is CallArgumentSources other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        ImmutableArrayValueEquality.AddToHash(
            ref hash,
            _sources);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The terminal consumer of a value returned by a direct call.
/// </summary>
public enum MethodResultSinkKind
{
    /// <summary>A physical <c>ret</c> instruction.</summary>
    MethodReturn,

    /// <summary>The single argument supplied to a direct call.</summary>
    SingleArgumentCall,
}

/// <summary>
/// Conservative, physical-body evidence for the direct-call producers of a
/// method-result sink.
/// </summary>
/// <param name="Caller">
/// Declared method attributed to the body. This can differ from
/// <paramref name="EvidenceMethod"/> for an async state-machine body.
/// </param>
/// <param name="EvidenceMethod">Physical method body containing the sink.</param>
/// <param name="ILOffset">Physical IL offset of the sink instruction.</param>
/// <param name="Kind">Whether the sink is a return or a single-argument call.</param>
/// <param name="SourceCallOffsets">
/// Physical IL offsets of every directly proven call-result source. Empty is
/// valid only when <paramref name="IsComplete"/> is false.
/// </param>
/// <param name="IsComplete">
/// Whether every reaching definition and evaluation-stack source of the sink
/// value was proven to be a direct non-void call result. Unknown, raw, or
/// merged stack values remain false.
/// </param>
public sealed record MethodResultSink(
    MethodIdentity Caller,
    MethodIdentity EvidenceMethod,
    int ILOffset,
    MethodResultSinkKind Kind,
    ImmutableArray<int> SourceCallOffsets,
    bool IsComplete)
{
    /// <summary>
    /// Direct async source method authenticated by Analysis for this physical
    /// state-machine body. Null when the body is not a proven async
    /// state-machine execution body.
    /// <c>JsonWireContractResolverTests.Build_RejectsUnrelatedAsyncBuilderResultSink</c>
    /// gates the consumer boundary.
    /// </summary>
    public MethodIdentity? AsyncStateMachineSource { get; init; }
}

public sealed record CalledTypeSummary(
    TypeRef Type,
    string Assembly,
    int Calls,
    int Members,
    ImmutableArray<CallKind> CallKinds);

public sealed record UnsafeEvidence(
    MethodIdentity Member,
    string Reason,
    string Detail,
    string Kind,
    int? ILOffset,
    int? OperandToken);

public sealed class MemberPattern
{
    readonly TypeRef? _declaringType;
    readonly string? _declaringTypeName;
    readonly TypeRef? _returnType;
    readonly bool _eraseGenericSignature;

    MemberPattern(
        TypeRef? declaringType,
        string? declaringTypeName,
        string name,
        ImmutableArray<TypeRef> parameterTypes,
        bool matchParameterTypes,
        TypeRef? returnType = null)
    {
        _declaringType = declaringType;
        _declaringTypeName = declaringTypeName;
        _returnType = returnType;
        Name = name;
        ParameterTypes = parameterTypes;
        MatchParameterTypes = matchParameterTypes;
        // A generic target matches cross-assembly on its open declaring type + name +
        // parameter arity, because a constructed caller spells the instantiated
        // signature the open definition never matches (#1339).
        _eraseGenericSignature = matchParameterTypes
            && (declaringType is not null
                ? GenericMemberIdentity.IsGenericType(declaringType) || parameterTypes.Any(GenericMemberIdentity.ContainsGenericParameter)
                : (declaringTypeName is not null && GenericMemberIdentity.HasArity(declaringTypeName)) || parameterTypes.Any(GenericMemberIdentity.ContainsGenericParameter));
    }

    public string Name { get; }
    public ImmutableArray<TypeRef> ParameterTypes { get; }
    public bool MatchParameterTypes { get; }

    public static MemberPattern Method(string declaringType, string name)
        => new(null, declaringType, name, [], matchParameterTypes: false);

    public static MemberPattern Method(TypeRef declaringType, string name)
        => new(declaringType, null, name, [], matchParameterTypes: false);

    public static MemberPattern Method(string declaringType, string name, ImmutableArray<TypeRef> parameterTypes)
        => new(null, declaringType, name, parameterTypes, matchParameterTypes: true);

    public static MemberPattern Method(TypeRef declaringType, string name, ImmutableArray<TypeRef> parameterTypes)
        => new(declaringType, null, name, parameterTypes, matchParameterTypes: true);

    public static MemberPattern Method(MethodIdentity method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return new(
            method.DeclaringType,
            null,
            method.Name,
            method.ParameterTypes,
            matchParameterTypes: true,
            ApiMemberIdentity.IsConversionOperator(method.Name)
                ? method.ReturnType
                : null);
    }

    public bool Matches(MemberRef member)
    {
        bool declaringMatches = _declaringType is not null
            ? member.DeclaringType.Equals(_declaringType)
            : string.Equals(member.DeclaringType.ToQualifiedDisplayString(), _declaringTypeName, StringComparison.Ordinal);
        if (!declaringMatches || !string.Equals(member.Name, Name, StringComparison.Ordinal))
        {
            return false;
        }
        return (!MatchParameterTypes
                || member.ParameterTypes.SequenceEqual(ParameterTypes))
            && (_returnType is null
                || TypeRef.ExactSignatureEquals(
                    member.OpenSignatureReturn,
                    _returnType));
    }

}
