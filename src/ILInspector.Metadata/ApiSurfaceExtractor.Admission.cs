using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

// One home for each rule that admits a Type's metadata record as an API
// declaration. API-surface extraction, the compact public Member Count, and the
// declarable field surface call these rules instead of repeating them, so a
// Count and the Rows it counts cannot drift apart.
public static partial class ApiSurfaceExtractor
{
    /// <summary>
    /// A method record passes the access rule when the population admits every
    /// accessibility, when it is public, or when it is a MethodImpl body.
    /// </summary>
    static bool AdmitsMethodAccess(
        MethodAttributes methodAccess,
        bool isExplicitImplementation,
        bool includeAll)
        => includeAll
            || methodAccess == MethodAttributes.Public
            || isExplicitImplementation;

    /// <summary>
    /// A C# property or event accessor is represented by its property or event
    /// row, except a private MethodImpl accessor: that is the explicit-interface
    /// shape, whose property or event row is private.
    /// </summary>
    static bool IsFoldedAccessorMethod(
        Dictionary<MethodDefinitionHandle, ApiMethodSemanticsKind> accessorMethods,
        MethodDefinitionHandle methodHandle,
        bool isExplicitImplementation,
        MethodAttributes methodAccess)
        => accessorMethods.TryGetValue(
                methodHandle,
                out ApiMethodSemanticsKind semantics)
            && IsCSharpAccessor(semantics)
            && !(isExplicitImplementation
                && methodAccess == MethodAttributes.Private);

    /// <summary>
    /// A compiler-named method or field (its name starts with <c>&lt;</c>) is
    /// admitted only when compiler-generated records are requested.
    /// </summary>
    static bool IsExcludedCompilerNamedMember(
        string name,
        bool includeCompilerGenerated)
        => name.StartsWith('<') && !includeCompilerGenerated;

    /// <summary>
    /// A hidden (<c>EditorBrowsable(Never)</c>) method is omitted from the
    /// public-facing population. A MethodImpl body is exempt.
    /// </summary>
    static bool IsHiddenMethod(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        bool isExplicitImplementation,
        Action<int>? beforeMaterialize = null)
        => !isExplicitImplementation
            && IsHiddenMember(reader, attributes, beforeMaterialize);

    /// <summary>
    /// A hidden (<c>EditorBrowsable(Never)</c>) property, field, or event is
    /// omitted from the public-facing population.
    /// </summary>
    static bool IsHiddenMember(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        Action<int>? beforeMaterialize = null)
        => AttributeReader.HasEditorBrowsableNeverAttribute(
            reader,
            attributes,
            beforeMaterialize);

    /// <summary>
    /// A property's access is its most accessible accessor's access.
    /// </summary>
    static MethodAttributes PropertyAccess(
        MetadataReader reader,
        PropertyAccessors accessors)
    {
        MethodAttributes access = 0;
        if (!accessors.Getter.IsNil)
        {
            access = reader.GetMethodDefinition(accessors.Getter).Attributes
                & MethodAttributes.MemberAccessMask;
        }
        if (!accessors.Setter.IsNil)
        {
            MethodAttributes setterAccess =
                reader.GetMethodDefinition(accessors.Setter).Attributes
                & MethodAttributes.MemberAccessMask;
            if (setterAccess > access)
                access = setterAccess;
        }

        return access;
    }

    /// <summary>
    /// An event's access is its adder's access; an event without an adder is
    /// not an API declaration.
    /// </summary>
    static MethodAttributes? EventAccess(
        MetadataReader reader,
        EventAccessors accessors)
        => accessors.Adder.IsNil
            ? null
            : reader.GetMethodDefinition(accessors.Adder).Attributes
                & MethodAttributes.MemberAccessMask;

    /// <summary>
    /// A property, field, or event passes the access rule when the population
    /// admits every accessibility or the record is public.
    /// </summary>
    static bool AdmitsMemberAccess(bool isPublic, bool includeAll)
        => includeAll || isPublic;

    /// <summary>
    /// An enum's <c>value__</c> storage slot supplies a Type fact, not a member.
    /// </summary>
    static bool IsEnumStorageField(bool isEnum, string fieldName)
        => isEnum && fieldName == "value__";
}
