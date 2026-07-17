using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>
/// The C# kind of a reconstructed type shell. Neutral spelling of the kinds the
/// seam can compose, independent of any consumer's planning vocabulary.
/// </summary>
public enum CSharpTypeShellKind
{
    Class,
    Record,
    Struct,
    Interface,
    Enum,
    Delegate,
}

/// <summary>
/// A neutral, IR- and planner-free description of a type shell to compose into a
/// <see cref="CSharpTypePrintRequest"/>. The consumer (for example the ReturnToSender
/// harness) discovers the members and resolves the C# spelling of the type's own
/// name, base type, and interfaces; the seam owns turning this description plus the
/// type's metadata into the <see cref="ApiType"/> and print-request tree. Every field
/// is a plain string, a metadata handle, or an already-seam/Metadata type — no
/// consumer planning type crosses this boundary.
/// </summary>
/// <param name="Handle">The type definition whose metadata supplies the remaining
/// shell facts (type parameters, custom attributes, and abstract/sealed/static
/// modifiers).</param>
/// <param name="Namespace">The type's C# namespace, or the empty string for the
/// global namespace.</param>
/// <param name="MetadataName">The type's metadata name (arity ticks preserved),
/// used verbatim for both <see cref="ApiType.Name"/> and
/// <see cref="ApiType.MetadataName"/>.</param>
/// <param name="Kind">The reconstructed C# kind.</param>
/// <param name="BaseTypeDisplayName">The C# display spelling of the reconstructed
/// base type, or null when no base is reconstructed.</param>
/// <param name="InterfaceDisplayNames">The C# display spellings of the reconstructed
/// implemented interfaces.</param>
/// <param name="MemberPolicies">The members to emit with their per-member body
/// policies, in declaration order.</param>
/// <param name="PrimaryConstructorParameters">The primary-constructor parameter list,
/// or empty when the type has no primary constructor.</param>
/// <param name="NestedTypes">The nested type shells, composed recursively.</param>
public sealed record CSharpTypeShellSpec(
    TypeDefinitionHandle Handle,
    string Namespace,
    string MetadataName,
    CSharpTypeShellKind Kind,
    string? BaseTypeDisplayName,
    IReadOnlyList<string> InterfaceDisplayNames,
    IReadOnlyList<CSharpMemberPolicy> MemberPolicies,
    IReadOnlyList<ApiParameter> PrimaryConstructorParameters,
    IReadOnlyList<CSharpTypeShellSpec> NestedTypes);
