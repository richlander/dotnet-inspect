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

public sealed record MethodIdentity(
    string AssemblyName,
    Guid ModuleVersionId,
    TypeRef DeclaringType,
    string Name,
    ImmutableArray<TypeRef> ParameterTypes,
    TypeRef ReturnType,
    int MetadataToken,
    bool IsStatic);

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
    CallKind Kind);

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
