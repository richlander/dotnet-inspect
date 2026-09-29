using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// The unit a Type Member population counts
/// (docs/design/api-population-scope.md#spelling-within-api-visibility-scope):
/// C# spelling composes accessors into their property or event declaration and
/// lists attached extensions; metadata spelling lists each record of the Type.
/// </summary>
public enum MetadataMemberSpelling
{
    CSharp,
    Metadata,
}

internal enum ClassifiedMemberKind
{
    Method,
    Property,
    Field,
    Event,
}

/// <summary>
/// One admitted declaration of a Type's Member population: its record, its
/// access (the source of its <c>api.accessibility</c> bucket), whether it is
/// hidden, and its receiver form.
/// </summary>
internal readonly record struct ClassifiedMember(
    ClassifiedMemberKind Kind,
    EntityHandle Handle,
    MethodAttributes Access,
    bool IsHidden,
    MetadataMethodReceiver Receiver,
    bool IsAttached);

internal interface IClassifiedMemberSink
{
    void Add(in ClassifiedMember member);
}

/// <summary>
/// Receives the module's attached extensions for the receiver Types it wants.
/// </summary>
internal interface IAttachedExtensionSink
{
    bool Wants(in ExtensionReceiver receiver, TypeDefinitionHandle declaringType);

    void Add(in ExtensionReceiver receiver, in ClassifiedMember member);
}

/// <summary>
/// The Type an extension method's first parameter names: a TypeDef, a TypeRef
/// resolving to this module, or a primitive this module defines. It is decoded
/// by the same signature walk as the receiver's name, but reads no name until
/// a sink asks, so a sink that knows its receiver's TypeDef compares handles.
/// </summary>
internal readonly struct ExtensionReceiver
{
    readonly MetadataReader _reader;
    readonly ApiSurfaceExtractor.ReceiverKey _key;

    internal ExtensionReceiver(MetadataReader reader, ApiSurfaceExtractor.ReceiverKey key)
    {
        _reader = reader;
        _key = key;
    }

    /// <summary>
    /// The TypeDef the receiver names directly. Its name equals a Type's name
    /// only when it is that Type's own row, so a caller holding the unique,
    /// readable row of a name compares rows instead of names.
    /// </summary>
    public bool TryGetDefinition(out TypeDefinitionHandle definition)
    {
        if (_key.Handle.Kind == HandleKind.TypeDefinition)
        {
            definition = (TypeDefinitionHandle)_key.Handle;
            return true;
        }
        definition = default;
        return false;
    }

    /// <summary>
    /// The receiver's definition name, or null when it names no readable
    /// definition of this module.
    /// </summary>
    public MetadataTypeDefinitionName? ReadName()
    {
        if (_key.IsPrimitive)
            return ApiSurfaceExtractor.GetLocalPrimitiveDefinition(_key.Primitive);
        MetadataTypeDefinitionNameReadResult? read = _key.Handle.Kind switch
        {
            HandleKind.TypeDefinition => MetadataTypeDefinitionName.Read(
                _reader,
                (TypeDefinitionHandle)_key.Handle),
            HandleKind.TypeReference => MetadataTypeDefinitionName.Read(
                _reader,
                (TypeReferenceHandle)_key.Handle),
            _ => null,
        };
        return read is MetadataTypeDefinitionNameReadResult.Read valid
            ? valid.Name
            : null;
    }
}

public static partial class ApiSurfaceExtractor
{
    /// <summary>
    /// Classifies every admitted declaration of one Type under one spelling,
    /// applying the same admission rules as extraction. A public-only caller
    /// skips non-public records before their hidden and backing-field checks.
    /// A static method of an extension container carrying <c>[Extension]</c>
    /// has the <c>extension</c> receiver form; the caller says whether the
    /// Type is one, since it often already knows.
    /// </summary>
    internal static void ClassifyDeclaredMembers<TSink>(
        MetadataReader reader,
        TypeDefinition typeDef,
        MetadataMemberSpelling spelling,
        bool publicOnly,
        bool extensionContainer,
        ref TSink sink)
        where TSink : struct, IClassifiedMemberSink
    {
        bool csharp = spelling == MetadataMemberSpelling.CSharp;
        var explicitImplementationBodies = GetExplicitImplementationBodies(reader, typeDef);
        // Metadata spelling buckets each record by its own flag, so it reads
        // no interface access.
        var interfaceImplementations = csharp
            ? GetInterfaceImplementations(reader, typeDef)
            : [];
        var accessorMethods = GetSemanticAccessorMethods(reader, typeDef);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var access = MethodEffectiveAccess(
                method.Attributes & MethodAttributes.MemberAccessMask,
                methodHandle,
                interfaceImplementations);
            if (publicOnly && access != MethodAttributes.Public)
                continue;
            if ((csharp && IsFoldedAccessorMethod(accessorMethods, methodHandle))
                || IsExcludedCompilerNamedMember(
                    reader,
                    method.Name,
                    includeCompilerGenerated: false))
            {
                continue;
            }

            sink.Add(new ClassifiedMember(
                ClassifiedMemberKind.Method,
                methodHandle,
                access,
                IsHiddenMethod(
                    reader,
                    method.GetCustomAttributes(),
                    explicitImplementationBodies.Contains(methodHandle)),
                MethodReceiver(reader, method, extensionContainer),
                IsAttached: false));
        }

        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            PropertyAccessors accessors = property.GetAccessors();
            var access = PropertyAccess(reader, accessors, interfaceImplementations);
            if (publicOnly && access != MethodAttributes.Public)
                continue;

            MethodDefinitionHandle accessor = accessors.Getter.IsNil
                ? accessors.Setter
                : accessors.Getter;
            sink.Add(new ClassifiedMember(
                ClassifiedMemberKind.Property,
                propertyHandle,
                access,
                IsHiddenAccessorOwner(
                    reader,
                    property.GetCustomAttributes(),
                    explicitImplementationBodies,
                    accessors.Getter,
                    accessors.Setter),
                AccessorReceiver(reader, accessor),
                IsAttached: false));
        }

        bool isEnum = IsEnum(reader, typeDef);
        GenericContext? context = null;
        HashSet<string>? fieldLikeEventBackingFieldNames = null;
        Dictionary<string, AutoPropertyBackingField>? autoPropertyBackingFields = null;
        bool backingFieldsRead = false;
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            var access = (MethodAttributes)(field.Attributes & FieldAttributes.FieldAccessMask);
            if (publicOnly && access != MethodAttributes.Public)
                continue;

            if (IsEnumStorageField(isEnum, reader, field.Name)
                || IsExcludedCompilerNamedMember(
                    reader,
                    field.Name,
                    includeCompilerGenerated: false))
            {
                continue;
            }
            // Backing fields are never public, so a public-only caller never
            // reads their descriptors.
            if (access != MethodAttributes.Public)
            {
                if (!backingFieldsRead)
                {
                    context = GenericContext.ForType(reader, typeDef);
                    fieldLikeEventBackingFieldNames =
                        FieldLikeEventBackingFieldNames(reader, typeDef);
                    autoPropertyBackingFields =
                        AutoPropertyBackingFieldDescriptors(reader, typeDef, context);
                    backingFieldsRead = true;
                }
                string fieldName = reader.GetString(field.Name);
                if (IsAutoPropertyBackingField(
                        reader,
                        field,
                        fieldName,
                        autoPropertyBackingFields,
                        context!)
                    || IsFieldLikeEventBackingField(
                        reader,
                        field,
                        fieldName,
                        fieldLikeEventBackingFieldNames))
                {
                    continue;
                }
            }

            sink.Add(new ClassifiedMember(
                ClassifiedMemberKind.Field,
                fieldHandle,
                access,
                IsHiddenMember(reader, field.GetCustomAttributes()),
                (field.Attributes & FieldAttributes.Static) != 0
                    ? MetadataMethodReceiver.Static
                    : MetadataMethodReceiver.This,
                IsAttached: false));
        }

        foreach (var eventHandle in typeDef.GetEvents())
        {
            var evt = reader.GetEventDefinition(eventHandle);
            EventAccessors accessors = evt.GetAccessors();
            if (EventAccess(reader, accessors, interfaceImplementations) is not { } access
                || (publicOnly && access != MethodAttributes.Public))
            {
                continue;
            }

            sink.Add(new ClassifiedMember(
                ClassifiedMemberKind.Event,
                eventHandle,
                access,
                IsHiddenAccessorOwner(
                    reader,
                    evt.GetCustomAttributes(),
                    explicitImplementationBodies,
                    accessors.Adder,
                    accessors.Remover),
                AccessorReceiver(reader, accessors.Adder),
                IsAttached: false));
        }
    }

    /// <summary>
    /// Classifies the module's extension methods as attached declarations of
    /// the receiver Types the sink wants. Each takes the narrower of its own
    /// access and its declaring Type's, and a hidden declaring Type hides it.
    /// A public-only caller skips non-public ones before decoding a receiver.
    /// </summary>
    internal static void ClassifyAttachedExtensions<TSink>(
        MetadataReader reader,
        bool publicOnly,
        ref TSink sink)
        where TSink : struct, IAttachedExtensionSink
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            if (!IsExtensionContainer(reader, definition)
                || TypeFilters.IsCompilerGenerated(reader.GetString(definition.Name)))
            {
                continue;
            }

            MethodAttributes declaringAccess = TypeAccess(definition);
            if (publicOnly && declaringAccess != MethodAttributes.Public)
                continue;
            bool declaringHidden = AttributeReader.HasHiddenAttribute(
                reader,
                definition.GetCustomAttributes());
            Dictionary<MethodDefinitionHandle, InterfaceImplementationAccess>?
                interfaceImplementations = null;
            foreach (MethodDefinitionHandle methodHandle in definition.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.Static) == 0
                    || !AttributeReader.HasExtensionAttribute(
                        reader,
                        method.GetCustomAttributes()))
                {
                    continue;
                }

                interfaceImplementations ??= GetInterfaceImplementations(reader, definition);
                MethodAttributes access = NarrowerAccess(
                    MethodEffectiveAccess(
                        method.Attributes & MethodAttributes.MemberAccessMask,
                        methodHandle,
                        interfaceImplementations),
                    declaringAccess);
                if ((publicOnly && access != MethodAttributes.Public)
                    || IsExcludedCompilerNamedMember(
                        reader,
                        method.Name,
                        includeCompilerGenerated: false)
                    || GetExtensionReceiver(reader, definition, method)
                        is not { } receiver
                    || !sink.Wants(receiver, handle))
                {
                    continue;
                }

                sink.Add(
                    receiver,
                    new ClassifiedMember(
                        ClassifiedMemberKind.Method,
                        methodHandle,
                        access,
                        declaringHidden
                            || IsHiddenMethod(
                                reader,
                                method.GetCustomAttributes(),
                                isExplicitImplementation: false),
                        MetadataMethodReceiver.Extension,
                        IsAttached: true));
            }
        }
    }

    /// <summary>
    /// A C# extension container: a static (sealed abstract) class carrying
    /// <c>[Extension]</c>.
    /// </summary>
    static bool IsExtensionContainer(MetadataReader reader, TypeDefinition definition)
        => (definition.Attributes & (TypeAttributes.Sealed | TypeAttributes.Abstract))
                == (TypeAttributes.Sealed | TypeAttributes.Abstract)
            && AttributeReader.HasExtensionAttribute(
                reader,
                definition.GetCustomAttributes());

    /// <summary>A Type's declared accessibility as member access.</summary>
    static MethodAttributes TypeAccess(TypeDefinition definition)
        => (definition.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic => MethodAttributes.Public,
            TypeAttributes.NestedPrivate => MethodAttributes.Private,
            TypeAttributes.NestedFamily => MethodAttributes.Family,
            TypeAttributes.NestedFamANDAssem => MethodAttributes.FamANDAssem,
            TypeAttributes.NestedFamORAssem => MethodAttributes.FamORAssem,
            _ => MethodAttributes.Assembly,
        };

    static MetadataMethodReceiver MethodReceiver(
        MetadataReader reader,
        MethodDefinition method,
        bool extensionContainer)
    {
        if ((method.Attributes & MethodAttributes.Static) == 0)
            return MetadataMethodReceiver.This;

        return extensionContainer
            && AttributeReader.HasExtensionAttribute(
                reader,
                method.GetCustomAttributes())
            ? MetadataMethodReceiver.Extension
            : MetadataMethodReceiver.Static;
    }

    static MetadataMethodReceiver AccessorReceiver(
        MetadataReader reader,
        MethodDefinitionHandle accessor)
        => !accessor.IsNil
            && (reader.GetMethodDefinition(accessor).Attributes & MethodAttributes.Static) != 0
            ? MetadataMethodReceiver.Static
            : MetadataMethodReceiver.This;
}
