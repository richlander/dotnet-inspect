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
public sealed record DirectCall(
    MethodIdentity Caller,
    MemberRef Callee,
    int ILOffset,
    int OperandToken,
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
    readonly string? _declaringTypeName;

    MemberPattern(TypeRef? declaringType, string? declaringTypeName, string name, ImmutableArray<TypeRef> parameterTypes, bool matchParameterTypes)
    {
        _declaringType = declaringType;
        _declaringTypeName = declaringTypeName;
        Name = name;
        ParameterTypes = parameterTypes;
        MatchParameterTypes = matchParameterTypes;
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
}
