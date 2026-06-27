using System.Collections.Immutable;

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
    CallerUnsafeMode CallerUnsafeMode = CallerUnsafeMode.None);

public sealed record MemberRef(
    TypeRef DeclaringType,
    string Name,
    ImmutableArray<TypeRef> ParameterTypes,
    TypeRef ReturnType,
    MemberKind Kind)
{
    public ImmutableArray<TypeRef> TypeArguments { get; init; } = [];

    public static MemberRef Unsupported(string reason)
        => new(TypeRef.Unsupported(reason), "?", [], TypeRef.Unsupported("unknown return"), MemberKind.Unsupported);
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
    bool InLoop = false);

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
    readonly TypeRef? _openDeclaringType;
    readonly string? _declaringTypeName;
    readonly bool _eraseGenericSignature;

    MemberPattern(TypeRef? declaringType, string? declaringTypeName, string name, ImmutableArray<TypeRef> parameterTypes, bool matchParameterTypes)
    {
        _declaringType = declaringType;
        _openDeclaringType = declaringType is null ? null : GenericMemberIdentity.OpenDeclaringType(declaringType);
        _declaringTypeName = declaringTypeName;
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

    public bool Matches(MemberRef member)
    {
        bool declaringMatches = _declaringType is not null
            ? member.DeclaringType.Equals(_declaringType)
            : string.Equals(member.DeclaringType.ToQualifiedDisplayString(), _declaringTypeName, StringComparison.Ordinal);
        if (!declaringMatches || !string.Equals(member.Name, Name, StringComparison.Ordinal))
        {
            return false;
        }
        return !MatchParameterTypes || member.ParameterTypes.SequenceEqual(ParameterTypes);
    }

    /// <summary>
    /// Structural match for <em>cross-assembly</em> callers, where a constructed call
    /// site and the open-definition target disagree on generic spelling. The candidate
    /// declaring type is reduced to its open definition so a constructed generic type
    /// matches its open definition, and a generic target is compared on parameter arity
    /// rather than exact instantiated signature (#1339). Non-generic members fall back
    /// to the same exact comparison as <see cref="Matches"/>.
    /// </summary>
    public bool MatchesCrossAssembly(MemberRef member)
    {
        var candidateDeclaring = GenericMemberIdentity.OpenDeclaringType(member.DeclaringType);
        bool declaringMatches = _openDeclaringType is not null
            ? candidateDeclaring.Equals(_openDeclaringType)
            : string.Equals(candidateDeclaring.ToQualifiedDisplayString(), _declaringTypeName, StringComparison.Ordinal);
        if (!declaringMatches || !string.Equals(member.Name, Name, StringComparison.Ordinal))
        {
            return false;
        }
        if (!MatchParameterTypes)
        {
            return true;
        }
        return _eraseGenericSignature
            ? member.ParameterTypes.Length == ParameterTypes.Length
            : member.ParameterTypes.SequenceEqual(ParameterTypes);
    }
}
