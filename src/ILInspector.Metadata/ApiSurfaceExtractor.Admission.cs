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
    /// accessibility or its effective access is public.
    /// </summary>
    static bool AdmitsMethodAccess(
        MethodAttributes effectiveAccess,
        bool includeAll)
        => includeAll || effectiveAccess == MethodAttributes.Public;

    /// <summary>
    /// A method's effective access. A member that implements an interface
    /// member through a MethodImpl is reachable by exactly the consumers who
    /// can see that interface, so its access is the wider of its own and the
    /// interface's. Every other method, including a finalizer, keeps its own.
    /// </summary>
    static MethodAttributes MethodEffectiveAccess(
        MethodAttributes ownAccess,
        MethodDefinitionHandle methodHandle,
        Dictionary<MethodDefinitionHandle, InterfaceImplementationAccess>
            interfaceImplementations)
    {
        if (!interfaceImplementations.TryGetValue(
                methodHandle,
                out InterfaceImplementationAccess implemented))
        {
            return ownAccess;
        }

        MethodAttributes access = ownAccess;
        if (implemented.KnownInterfaceAccess is { } known)
            access = JoinAccess(access, known);
        // An interface declared in another assembly is public to this
        // assembly's consumers. A referenced member's Type cannot be told
        // apart from a class without resolving that assembly, so only a
        // private body, the explicit-implementation shape, takes that rule.
        if (implemented.ImplementsReferencedMember
            && ownAccess == MethodAttributes.Private)
        {
            access = MethodAttributes.Public;
        }

        return access;
    }

    /// <summary>
    /// The interface members a Type's MethodImpl bodies implement: the widest
    /// declared access among implemented interfaces in this assembly, and
    /// whether any implemented member is declared in another assembly.
    /// </summary>
    internal readonly record struct InterfaceImplementationAccess(
        MethodAttributes? KnownInterfaceAccess,
        bool ImplementsReferencedMember);

    internal static Dictionary<MethodDefinitionHandle, InterfaceImplementationAccess>
        GetInterfaceImplementations(
            MetadataReader reader,
            TypeDefinition typeDef)
    {
        Dictionary<MethodDefinitionHandle, InterfaceImplementationAccess>
            implementations = [];
        foreach (MethodImplementationHandle implementationHandle
            in typeDef.GetMethodImplementations())
        {
            MethodImplementation implementation =
                reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind != HandleKind.MethodDefinition)
                continue;

            var body = (MethodDefinitionHandle)implementation.MethodBody;
            MethodAttributes? known = null;
            bool referenced = false;
            switch (implementation.MethodDeclaration.Kind)
            {
                case HandleKind.MethodDefinition:
                    known = InterfaceAccess(
                        reader,
                        reader.GetMethodDefinition(
                            (MethodDefinitionHandle)implementation
                                .MethodDeclaration)
                            .GetDeclaringType());
                    break;
                case HandleKind.MemberReference:
                    EntityHandle parent = reader.GetMemberReference(
                        (MemberReferenceHandle)implementation
                            .MethodDeclaration)
                        .Parent;
                    if (DeclaringTypeDefinition(reader, parent)
                        is { } definition)
                    {
                        known = InterfaceAccess(reader, definition);
                    }
                    else
                    {
                        referenced = true;
                    }
                    break;
            }

            if (known is null && !referenced)
                continue;

            if (implementations.TryGetValue(
                    body,
                    out InterfaceImplementationAccess existing))
            {
                known = existing.KnownInterfaceAccess is { } prior
                    ? known is { } next ? JoinAccess(prior, next) : prior
                    : known;
                referenced |= existing.ImplementsReferencedMember;
            }
            implementations[body] = new(known, referenced);
        }

        return implementations;
    }

    /// <summary>
    /// The Type definition a MemberRef parent names in this assembly, or null
    /// when it names a Type in another assembly.
    /// </summary>
    static TypeDefinitionHandle? DeclaringTypeDefinition(
        MetadataReader reader,
        EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeDefinition:
                return (TypeDefinitionHandle)parent;
            case HandleKind.TypeSpecification:
                BlobReader blob = reader.GetBlobReader(
                    reader.GetTypeSpecification(
                        (TypeSpecificationHandle)parent).Signature);
                if (blob.ReadSignatureTypeCode()
                    != SignatureTypeCode.GenericTypeInstance)
                {
                    return null;
                }
                blob.ReadSignatureTypeCode();
                EntityHandle generic = blob.ReadTypeHandle();
                return generic.Kind == HandleKind.TypeDefinition
                    ? (TypeDefinitionHandle)generic
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// An interface's declared accessibility as a member access level, or
    /// null when the Type is not an interface.
    /// </summary>
    static MethodAttributes? InterfaceAccess(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle)
    {
        TypeAttributes attributes =
            reader.GetTypeDefinition(typeHandle).Attributes;
        if ((attributes & TypeAttributes.Interface) == 0)
            return null;

        return (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic
                => MethodAttributes.Public,
            TypeAttributes.NestedFamORAssem => MethodAttributes.FamORAssem,
            TypeAttributes.NestedFamily => MethodAttributes.Family,
            TypeAttributes.NestedFamANDAssem => MethodAttributes.FamANDAssem,
            TypeAttributes.NestedPrivate => MethodAttributes.Private,
            _ => MethodAttributes.Assembly,
        };
    }

    /// <summary>
    /// The join of two access levels in the ECMA-335 accessibility order:
    /// <c>protected</c> and <c>internal</c> join to <c>protected internal</c>.
    /// </summary>
    static MethodAttributes JoinAccess(
        MethodAttributes left,
        MethodAttributes right)
    {
        if (left == right)
            return left;
        if (left == MethodAttributes.Public || right == MethodAttributes.Public)
            return MethodAttributes.Public;
        if (left == MethodAttributes.FamORAssem
            || right == MethodAttributes.FamORAssem
            || (left == MethodAttributes.Family
                && right == MethodAttributes.Assembly)
            || (left == MethodAttributes.Assembly
                && right == MethodAttributes.Family))
        {
            return MethodAttributes.FamORAssem;
        }
        if (left == MethodAttributes.Family || right == MethodAttributes.Family)
            return MethodAttributes.Family;
        if (left == MethodAttributes.Assembly
            || right == MethodAttributes.Assembly)
        {
            return MethodAttributes.Assembly;
        }
        if (left == MethodAttributes.FamANDAssem
            || right == MethodAttributes.FamANDAssem)
        {
            return MethodAttributes.FamANDAssem;
        }
        return MethodAttributes.Private;
    }

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
