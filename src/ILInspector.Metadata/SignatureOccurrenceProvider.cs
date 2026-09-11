using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal sealed class SignatureOccurrenceProvider(
    PEReader image,
    SignatureOccurrenceWorkBudget budget) :
    ISignatureTypeProvider<ImmutableArray<SignatureNamedTypeOccurrence>, object?>
{
    public ImmutableArray<SignatureNamedTypeOccurrence> GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        budget.Node();
        string name = typeCode switch
        {
            PrimitiveTypeCode.Boolean => "Boolean",
            PrimitiveTypeCode.Byte => "Byte",
            PrimitiveTypeCode.SByte => "SByte",
            PrimitiveTypeCode.Char => "Char",
            PrimitiveTypeCode.Int16 => "Int16",
            PrimitiveTypeCode.UInt16 => "UInt16",
            PrimitiveTypeCode.Int32 => "Int32",
            PrimitiveTypeCode.UInt32 => "UInt32",
            PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.UInt64 => "UInt64",
            PrimitiveTypeCode.Single => "Single",
            PrimitiveTypeCode.Double => "Double",
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.String => "String",
            PrimitiveTypeCode.Object => "Object",
            PrimitiveTypeCode.Void => "Void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => throw new BadImageFormatException("Invalid signature primitive."),
        };
        var valid = (MetadataTypeDefinitionNameResult.Valid)
            MetadataTypeDefinitionName.Create("System", [name]);
        return Named(new MetadataTypeReferenceScope.IntrinsicCoreLibrary(), valid.Name);
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetTypeFromDefinition(
        MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        budget.Node();
        var name = RequireName(MetadataTypeDefinitionNameReader.Read(
            reader, handle, chargeChain: budget.TypeDefinitionChain,
            chargeCharacters: budget.TypeNameCharacters));
        return Named(new MetadataTypeReferenceScope.CurrentAssembly(), name,
            new SignatureTypeDefinitionOrigin(image, handle));
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetTypeFromReference(
        MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        budget.Node();
        var name = RequireName(MetadataTypeDefinitionNameReader.Read(
            reader, handle, chargeChain: budget.TypeReferenceNameChain,
            chargeCharacters: budget.TypeNameCharacters));
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        bool walked = MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
            reader, handle, chain, out int consumedNodes, out EntityHandle terminal, out _);
        budget.Work(SignatureOccurrenceMetric.TypeReferenceScopeChainNodes, consumedNodes);
        if (!walked)
            throw new SignatureOccurrenceRejectedException(
                SignatureOccurrenceRejectionReason.RelationshipTraversal);

        MetadataTypeReferenceScope scope = terminal.Kind switch
        {
            HandleKind.AssemblyReference when !terminal.IsNil =>
                ReadAssemblyScope(reader, (AssemblyReferenceHandle)terminal),
            HandleKind.ModuleReference when !terminal.IsNil =>
                ReadModuleScope(reader, (ModuleReferenceHandle)terminal),
            HandleKind.ModuleDefinition => new MetadataTypeReferenceScope.CurrentAssembly(),
            _ when terminal.IsNil => new MetadataTypeReferenceScope.CurrentAssembly(),
            _ => throw new SignatureOccurrenceRejectedException(
                SignatureOccurrenceRejectionReason.InvalidTypeScope),
        };
        return Named(scope, name);
    }

    MetadataTypeReferenceScope ReadAssemblyScope(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        var reference = reader.GetAssemblyReference(handle);
        budget.Work(SignatureOccurrenceMetric.AssemblyReferenceNameBytes,
            reader.GetBlobReader(reference.Name).Length);
        budget.Work(SignatureOccurrenceMetric.AssemblyReferenceCultureBytes,
            reader.GetBlobReader(reference.Culture).Length);
        bool isPublicKey = (reference.Flags & AssemblyFlags.PublicKey) != 0;
        int keyLength = reader.GetBlobReader(reference.PublicKeyOrToken).Length;
        if (!isPublicKey && !reference.PublicKeyOrToken.IsNil && keyLength != 8)
            throw new BadImageFormatException("An assembly-reference token must contain exactly 8 bytes.");
        budget.Work(isPublicKey
                ? SignatureOccurrenceMetric.AssemblyReferenceFullKeyBytes
                : SignatureOccurrenceMetric.AssemblyReferenceTokenBytes,
            keyLength);
        string? token = AssemblyReferenceIdentity.TokenOrNull(reader, reference.PublicKeyOrToken, isPublicKey);
        return new MetadataTypeReferenceScope.AssemblyReference(
            AssemblyReferenceIdentity.Create(reader, reference, token));
    }

    MetadataTypeReferenceScope ReadModuleScope(MetadataReader reader, ModuleReferenceHandle handle)
    {
        var module = reader.GetModuleReference(handle);
        budget.Work(SignatureOccurrenceMetric.ModuleReferenceNameBytes,
            reader.GetBlobReader(module.Name).Length);
        string name = reader.GetString(module.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.InvalidTypeScope);
        return new MetadataTypeReferenceScope.ModuleReference(name);
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetTypeFromSpecification(
        MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        budget.Node();
        var specification = reader.GetTypeSpecification(handle);
        int length = reader.GetBlobReader(specification.Signature).Length;
        if (length > TypeSpecGuard.MaxCumulativeBytes)
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.TypeSpecificationBudget);
        budget.Work(SignatureOccurrenceMetric.TypeSpecificationBytes, length);
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
            reader, specification.Signature, SignatureBlobGuard.Kind.TypeSpecification, out var measurements))
        {
            budget.ObserveGuard(measurements);
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.UnsafeSignature);
        }
        budget.ObserveGuard(measurements);
        if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.TypeSpecificationBudget);
        using (scope)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetArrayType(
        ImmutableArray<SignatureNamedTypeOccurrence> elementType, ArrayShape shape)
    {
        budget.Node();
        return elementType;
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetSZArrayType(
        ImmutableArray<SignatureNamedTypeOccurrence> elementType)
    {
        budget.Node();
        return elementType;
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetByReferenceType(
        ImmutableArray<SignatureNamedTypeOccurrence> elementType)
    {
        budget.Node();
        return elementType;
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetPointerType(
        ImmutableArray<SignatureNamedTypeOccurrence> elementType)
    {
        budget.Node();
        return elementType;
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetPinnedType(
        ImmutableArray<SignatureNamedTypeOccurrence> elementType)
    {
        budget.Node();
        return elementType;
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetGenericTypeParameter(object? genericContext, int index)
    {
        budget.Node();
        return [];
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetGenericMethodParameter(object? genericContext, int index)
    {
        budget.Node();
        return [];
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetGenericInstantiation(
        ImmutableArray<SignatureNamedTypeOccurrence> genericType,
        ImmutableArray<ImmutableArray<SignatureNamedTypeOccurrence>> typeArguments)
    {
        budget.Node();
        return Combine(genericType, typeArguments);
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetFunctionPointerType(
        MethodSignature<ImmutableArray<SignatureNamedTypeOccurrence>> signature)
    {
        budget.Node();
        return Combine(signature.ReturnType, signature.ParameterTypes);
    }

    public ImmutableArray<SignatureNamedTypeOccurrence> GetModifiedType(
        ImmutableArray<SignatureNamedTypeOccurrence> modifier,
        ImmutableArray<SignatureNamedTypeOccurrence> unmodifiedType,
        bool isRequired)
    {
        budget.Node();
        int length = modifier.Length + unmodifiedType.Length;
        budget.Copies(length);
        var result = ImmutableArray.CreateBuilder<SignatureNamedTypeOccurrence>(length);
        foreach (var occurrence in modifier)
            result.Add(isRequired ? occurrence : occurrence with { Participates = false });
        result.AddRange(unmodifiedType);
        return result.MoveToImmutable();
    }

    internal void ObserveGuard(SignatureBlobGuardMeasurements measurements) =>
        budget.ObserveGuard(measurements);

    internal ImmutableArray<SignatureNamedTypeOccurrence> Combine(
        ImmutableArray<SignatureNamedTypeOccurrence> first,
        ImmutableArray<ImmutableArray<SignatureNamedTypeOccurrence>> rest)
    {
        int length = first.Length;
        foreach (var part in rest)
            length = checked(length + part.Length);
        budget.Copies(length);
        var result = ImmutableArray.CreateBuilder<SignatureNamedTypeOccurrence>(length);
        result.AddRange(first);
        foreach (var part in rest)
            result.AddRange(part);
        return result.MoveToImmutable();
    }

    ImmutableArray<SignatureNamedTypeOccurrence> Named(
        MetadataTypeReferenceScope scope,
        MetadataTypeDefinitionName name,
        SignatureTypeDefinitionOrigin? definition = null)
    {
        budget.Copies(1);
        return [new SignatureNamedTypeOccurrence(new MetadataNamedTypeReference(scope, name), true, definition)];
    }

    static MetadataTypeDefinitionName RequireName(MetadataTypeDefinitionNameReadResult result)
    {
        if (result is MetadataTypeDefinitionNameReadResult.Read read)
            return read.Name;
        var rejected = (MetadataTypeDefinitionNameReadResult.Rejected)result;
        throw new SignatureOccurrenceRejectedException(rejected.Failure.RelationshipKind switch
        {
            RelationshipTraversalRejectionKind.NameBudget => SignatureOccurrenceRejectionReason.TypeNameBudget,
            RelationshipTraversalRejectionKind.NodeBudget or RelationshipTraversalRejectionKind.Cycle =>
                SignatureOccurrenceRejectionReason.RelationshipTraversal,
            RelationshipTraversalRejectionKind.MalformedMetadata => SignatureOccurrenceRejectionReason.MalformedMetadata,
            _ => SignatureOccurrenceRejectionReason.InvalidTypeName,
        });
    }
}
