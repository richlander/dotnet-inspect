using System.Collections.Immutable;

namespace ILInspector.Decompiler;

/// <summary>
/// Typed identity of a member operand (call target, field access, function
/// pointer), resolved once by <see cref="ILAstBuilder"/> so downstream
/// consumers match on structure instead of parsing rendered operand strings.
/// The display string in <see cref="ILAstExpression.Operand"/> is unchanged
/// and remains the source for human-facing output.
/// </summary>
public sealed record MemberRefInfo
{
    /// <summary>Declaring type, C#-style full name; empty when unresolved.</summary>
    public required string DeclaringType { get; init; }

    /// <summary>Member name (no type qualifier, no parameter list).</summary>
    public required string Name { get; init; }

    /// <summary>Return type for methods, field type for fields.</summary>
    public string? ReturnOrFieldType { get; init; }

    public ImmutableArray<string> ParameterTypes { get; init; } = [];

    public bool IsStatic { get; init; }

    /// <summary>
    /// Splits a <c>Declaring.Type::Name</c> qualified name produced by the
    /// builder's resolution helpers. The single place a member string is parsed.
    /// </summary>
    public static MemberRefInfo FromQualifiedName(
        string qualifiedName,
        string? returnOrFieldType = null,
        ImmutableArray<string> parameterTypes = default,
        bool isStatic = false)
    {
        int separator = qualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        string declaringType = separator >= 0 ? qualifiedName[..separator] : "";
        string name = separator >= 0 ? qualifiedName[(separator + 2)..] : qualifiedName;

        // Generic method display names can carry a <args> suffix.
        int angle = name.IndexOf('<');
        if (angle > 0)
            name = name[..angle];

        return new MemberRefInfo
        {
            DeclaringType = declaringType,
            Name = name,
            ReturnOrFieldType = returnOrFieldType,
            ParameterTypes = parameterTypes.IsDefault ? [] : parameterTypes,
            IsStatic = isStatic,
        };
    }
}
