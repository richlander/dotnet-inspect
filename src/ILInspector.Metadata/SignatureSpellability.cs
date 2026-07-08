using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Answers whether metadata signatures can be spelled from a generated C#
/// assembly. Public metadata can expose non-public referenced types, especially
/// in non-C# or friend-assembly-produced libraries; those signatures are valid
/// metadata but cannot be named by a normal compile-back shell.
/// </summary>
public sealed class SignatureSpellability
{
    readonly IAssemblyReferenceResolver _resolver;
    readonly Dictionary<ReferenceKey, HashSet<string>?> _nonPublicTypes = new();

    public SignatureSpellability(IAssemblyReferenceResolver resolver)
        => _resolver = resolver;

    public bool CanSpellField(MetadataReader reader, FieldDefinition field, GenericContext context)
    {
        try { return !field.DecodeSignature(new InaccessibleTypeDetector(this), context); }
        catch (Exception ex) when (IsDecodeException(ex)) { return true; }
    }

    public bool CanSpellProperty(MetadataReader reader, PropertyDefinition property, GenericContext context)
    {
        try { return !property.DecodeSignature(new InaccessibleTypeDetector(this), context).ReturnType; }
        catch (Exception ex) when (IsDecodeException(ex)) { return true; }
    }

    public bool CanSpellMethod(MetadataReader reader, MethodDefinition method, GenericContext context)
    {
        try
        {
            var signature = method.DecodeSignature(new InaccessibleTypeDetector(this), context);
            return !signature.ReturnType && !signature.ParameterTypes.Any(inaccessible => inaccessible);
        }
        catch (Exception ex) when (IsDecodeException(ex)) { return true; }
    }

    static bool IsDecodeException(Exception ex)
        => ex is BadImageFormatException or InvalidOperationException or ArgumentException;

    bool IsInaccessible(MetadataReader reader, TypeReferenceHandle handle)
    {
        if (AssemblyScope(reader, handle) is not { } reference)
            return false;

        string fullName = reader.GetFullTypeName(reader.GetTypeReference(handle));
        return NonPublicTypes(reference)?.Contains(fullName) == true;
    }

    HashSet<string>? NonPublicTypes(ReferenceKey reference)
    {
        if (_nonPublicTypes.TryGetValue(reference, out var cached))
            return cached;

        if (Resolve(reference) is not { } resolved)
        {
            _nonPublicTypes[reference] = null;
            return null;
        }

        var types = new HashSet<string>(StringComparer.Ordinal);
        Stream? stream = null;
        PEReader? pe = null;
        try
        {
            stream = resolved.OpenRead();
            pe = new PEReader(stream);
            if (pe.HasMetadata)
            {
                var reader = pe.GetMetadataReader();
                foreach (var handle in reader.TypeDefinitions)
                {
                    if (!IsExternallyVisible(reader, handle))
                        types.Add(reader.GetFullTypeName(reader.GetTypeDefinition(handle)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            _nonPublicTypes[reference] = null;
            return null;
        }
        finally
        {
            pe?.Dispose();
            stream?.Dispose();
        }

        _nonPublicTypes[reference] = types;
        return types;
    }

    ResolvedAssemblyReference? Resolve(ReferenceKey reference)
    {
        if (_resolver.Resolve(reference.Identity, reference.Scope) is { } exact)
            return exact;

        var relaxed = reference.Identity with { Version = null };
        return _resolver.Resolve(relaxed, reference.Scope);
    }

    static bool IsExternallyVisible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        return (typeDef.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public => true,
            TypeAttributes.NestedPublic => !typeDef.GetDeclaringType().IsNil
                && IsExternallyVisible(reader, typeDef.GetDeclaringType()),
            _ => false,
        };
    }

    static ReferenceKey? AssemblyScope(MetadataReader reader, TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        return typeRef.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => ReferenceKey.From(reader, (AssemblyReferenceHandle)typeRef.ResolutionScope),
            HandleKind.TypeReference => AssemblyScope(reader, (TypeReferenceHandle)typeRef.ResolutionScope),
            _ => null,
        };
    }

    sealed record ReferenceKey(AssemblyReferenceIdentity Identity, AssemblyResolutionScope Scope)
    {
        public static ReferenceKey From(MetadataReader reader, AssemblyReferenceHandle handle)
        {
            var identity = AssemblyReferenceIdentity.From(reader, handle);
            var scope = PlatformKeys.IsPlatform(identity.PublicKeyToken)
                ? AssemblyResolutionScope.Platform
                : AssemblyResolutionScope.Any;
            return new ReferenceKey(identity, scope);
        }
    }

    sealed class InaccessibleTypeDetector(SignatureSpellability spellability)
        : ISignatureTypeProvider<bool, GenericContext?>
    {
        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => spellability.IsInaccessible(reader, handle);
        public bool GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        public bool GetSZArrayType(bool elementType) => elementType;
        public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
        public bool GetByReferenceType(bool elementType) => elementType;
        public bool GetPointerType(bool elementType) => elementType;
        public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
            => genericType || typeArguments.Any(inaccessible => inaccessible);
        public bool GetGenericMethodParameter(GenericContext? context, int index) => false;
        public bool GetGenericTypeParameter(GenericContext? context, int index) => false;
        public bool GetFunctionPointerType(MethodSignature<bool> signature)
            => signature.ReturnType || signature.ParameterTypes.Any(inaccessible => inaccessible);
        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;
        public bool GetPinnedType(bool elementType) => elementType;
    }
}
