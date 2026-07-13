using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;

namespace ILInspector.Metadata;

public readonly record struct SignatureSpellabilityResult(
    bool CanSpell,
    SignatureDecodeStatus? DecodeStatus);

/// <summary>
/// Answers whether metadata signatures can be spelled from a generated C#
/// assembly. Public metadata can expose non-public referenced types, especially
/// in non-C# or friend-assembly-produced libraries; those signatures are valid
/// metadata but cannot be named by a normal compile-back shell.
/// </summary>
public sealed class SignatureSpellability
{
    readonly IAssemblyReferenceResolver _resolver;
    readonly ConcurrentDictionary<ReferenceKey, Lazy<NonPublicTypeSet>> _nonPublicTypes = new();

    public SignatureSpellability(IAssemblyReferenceResolver resolver)
        => _resolver = resolver;

    public bool CanSpellField(MetadataReader reader, FieldDefinition field, GenericContext context)
        => InspectField(reader, field, context).CanSpell;

    public SignatureSpellabilityResult InspectField(
        MetadataReader reader,
        FieldDefinition field,
        GenericContext context)
    {
        try
        {
            var decoded = GuardedProviderDecode.FieldResult(
                reader,
                field,
                new InaccessibleTypeDetector(this),
                context,
                SpellabilityEvidence.Degraded);
            return Result(decoded.Value, decoded.IsDegraded);
        }
        catch (Exception ex) when (IsDecodeException(ex))
        {
            return DegradedResult;
        }
    }

    public bool CanSpellProperty(MetadataReader reader, PropertyDefinition property, GenericContext context)
        => InspectProperty(reader, property, context).CanSpell;

    public SignatureSpellabilityResult InspectProperty(
        MetadataReader reader,
        PropertyDefinition property,
        GenericContext context)
    {
        try
        {
            var decoded = GuardedProviderDecode.PropertyResult(
                reader,
                property,
                new InaccessibleTypeDetector(this),
                context,
                SpellabilityEvidence.Degraded);
            return Result(SpellabilityEvidence.Combine(decoded.Value), decoded.IsDegraded);
        }
        catch (Exception ex) when (IsDecodeException(ex))
        {
            return DegradedResult;
        }
    }

    public bool CanSpellMethod(MetadataReader reader, MethodDefinition method, GenericContext context)
        => InspectMethod(reader, method, context).CanSpell;

    public SignatureSpellabilityResult InspectMethod(
        MetadataReader reader,
        MethodDefinition method,
        GenericContext context)
    {
        try
        {
            var decoded = GuardedProviderDecode.MethodResult(
                reader,
                method,
                new InaccessibleTypeDetector(this),
                context,
                SpellabilityEvidence.Degraded);
            return Result(SpellabilityEvidence.Combine(decoded.Value), decoded.IsDegraded);
        }
        catch (Exception ex) when (IsDecodeException(ex))
        {
            return DegradedResult;
        }
    }

    static SignatureSpellabilityResult Result(SpellabilityEvidence evidence, bool topLevelDegraded)
    {
        bool isDegraded = topLevelDegraded || evidence.IsDegraded;
        return new SignatureSpellabilityResult(
            CanSpell: !evidence.IsInaccessible && !isDegraded,
            isDegraded ? SignatureDecodeStatus.Degraded : null);
    }

    static SignatureSpellabilityResult DegradedResult =>
        new(CanSpell: false, SignatureDecodeStatus.Degraded);

    static bool IsDecodeException(Exception ex)
        => ex is BadImageFormatException or InvalidOperationException or ArgumentException;

    bool IsInaccessible(MetadataReader reader, TypeReferenceHandle handle)
    {
        if (AssemblyScope(reader, handle) is not { } reference)
            return false;

        string fullName = reader.GetFullTypeName(reader.GetTypeReference(handle));
        return NonPublicTypes(reference).Types?.Contains(fullName) == true;
    }

    NonPublicTypeSet NonPublicTypes(ReferenceKey reference)
        => _nonPublicTypes.GetOrAdd(
            reference,
            key => new Lazy<NonPublicTypeSet>(() => LoadNonPublicTypes(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    NonPublicTypeSet LoadNonPublicTypes(ReferenceKey reference)
    {
        if (Resolve(reference) is not { } resolved)
            return new NonPublicTypeSet(null);

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
            return new NonPublicTypeSet(null);
        }
        finally
        {
            pe?.Dispose();
            stream?.Dispose();
        }

        return new NonPublicTypeSet(types);
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

    sealed record NonPublicTypeSet(HashSet<string>? Types);

    sealed class InaccessibleTypeDetector(SignatureSpellability spellability)
        : ISignatureTypeProvider<SpellabilityEvidence, GenericContext?>
    {
        public SpellabilityEvidence GetPrimitiveType(PrimitiveTypeCode typeCode) => default;
        public SpellabilityEvidence GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => default;
        public SpellabilityEvidence GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => new(spellability.IsInaccessible(reader, handle), IsDegraded: false);
        public SpellabilityEvidence GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return SpellabilityEvidence.Degraded;
            using (scope)
            {
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            }
        }
        public SpellabilityEvidence GetSZArrayType(SpellabilityEvidence elementType) => elementType;
        public SpellabilityEvidence GetArrayType(SpellabilityEvidence elementType, ArrayShape shape) => elementType;
        public SpellabilityEvidence GetByReferenceType(SpellabilityEvidence elementType) => elementType;
        public SpellabilityEvidence GetPointerType(SpellabilityEvidence elementType) => elementType;
        public SpellabilityEvidence GetGenericInstantiation(
            SpellabilityEvidence genericType,
            ImmutableArray<SpellabilityEvidence> typeArguments)
            => SpellabilityEvidence.Combine(genericType, typeArguments);
        public SpellabilityEvidence GetGenericMethodParameter(GenericContext? context, int index) => default;
        public SpellabilityEvidence GetGenericTypeParameter(GenericContext? context, int index) => default;
        public SpellabilityEvidence GetFunctionPointerType(MethodSignature<SpellabilityEvidence> signature)
            => SpellabilityEvidence.Combine(signature);
        public SpellabilityEvidence GetModifiedType(
            SpellabilityEvidence modifier,
            SpellabilityEvidence unmodifiedType,
            bool isRequired)
            => new(
                (isRequired && modifier.IsInaccessible) || unmodifiedType.IsInaccessible,
                modifier.IsDegraded || unmodifiedType.IsDegraded);
        public SpellabilityEvidence GetPinnedType(SpellabilityEvidence elementType) => elementType;
    }

    readonly record struct SpellabilityEvidence(bool IsInaccessible, bool IsDegraded)
    {
        public static SpellabilityEvidence Degraded =>
            new(IsInaccessible: false, IsDegraded: true);

        public static SpellabilityEvidence Combine(
            SpellabilityEvidence first,
            ImmutableArray<SpellabilityEvidence> rest)
            => new(
                first.IsInaccessible || rest.Any(static type => type.IsInaccessible),
                first.IsDegraded || rest.Any(static type => type.IsDegraded));

        public static SpellabilityEvidence Combine(MethodSignature<SpellabilityEvidence> signature)
            => Combine(signature.ReturnType, signature.ParameterTypes);
    }
}
