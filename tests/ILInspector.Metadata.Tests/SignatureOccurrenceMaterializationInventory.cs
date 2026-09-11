namespace ILInspector.Metadata.Tests;

/// <summary>
/// Reviewed source sites, including multiplicity. A is tool-capped: fixed
/// records/delegates/diagnostics, policy-sized names/chains, occurrence-budget
/// arrays, or fixed SHA-1/token output. B is raw author-sized string storage;
/// K is the flag-dependent raw key/token read. New sites fail until classified.
/// Loops are included so moving an existing effect into a new repetition scope
/// does not silently inherit its old classification.
/// </summary>
internal static class SignatureOccurrenceMaterializationInventory
{
    internal const string Sites = """
        A|1|SignatureOccurrenceLimits.Default | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceLimits.SignatureOccurrenceLimits(int, int, int)
        A|1|SignatureOccurrenceDecoder.Decode | ObjectCreation | System.ArgumentException.ArgumentException(string?, string?)
        A|1|SignatureOccurrenceLimits.Validate | ObjectCreation | System.ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?, string?)
        A|2|SignatureOccurrenceDecoder.Decode | ObjectCreation | System.InvalidOperationException.InvalidOperationException(string?)
        A|3|SignatureOccurrenceDecoder.Decode | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceDecodeResult.Rejected.Rejected(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceDecoder.ReadMetadata | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceDecoder.Decode | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceWorkBudget.SignatureOccurrenceWorkBudget(ILInspector.Metadata.SignatureOccurrenceLimits, ILInspector.Metadata.SignatureOccurrenceMetrics?)
        A|1|SignatureOccurrenceDecoder.Decode | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceProvider.SignatureOccurrenceProvider(System.Reflection.PortableExecutable.PEReader, ILInspector.Metadata.SignatureOccurrenceWorkBudget)
        A|2|SignatureOccurrenceWorkBudget.ObserveGuard | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceMeasurement.SignatureOccurrenceMeasurement(long, long, int)
        A|1|SignatureOccurrenceMetrics._measurements | Invocation | System.Enum.GetValues
        A|1|SignatureOccurrenceMetrics._measurements | ArrayCreation | newSignatureOccurrenceMeasurement[Enum.GetValues<SignatureOccurrenceMetric>().Length]
        A|2|SignatureOccurrenceMetrics.Observe | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceMeasurement.SignatureOccurrenceMeasurement(long, long, int)
        A|1|SignatureOccurrenceDecoder.DecodeMethod | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|2|SignatureOccurrenceProvider.Combine | Loop | foreach(varpartinrest)
        A|1|SignatureOccurrenceWorkBudget.Charge | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceProvider.Combine | Invocation | System.Collections.Immutable.ImmutableArray.CreateBuilder
        A|2|SignatureOccurrenceProvider.Combine | Invocation | System.Collections.Immutable.ImmutableArray<T>.Builder.AddRange
        A|1|SignatureOccurrenceProvider.Combine | Invocation | System.Collections.Immutable.ImmutableArray<T>.Builder.MoveToImmutable
        A|1|SignatureOccurrenceDecoder.DecodeField | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceDecoder.DecodeProperty | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceDecoder.Decode | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceDecodeResult.Decoded.Decoded(System.Collections.Immutable.ImmutableArray<ILInspector.Metadata.SignatureNamedTypeOccurrence>)
        A|1|SignatureOccurrenceProvider.GetPrimitiveType | ObjectCreation | System.BadImageFormatException.BadImageFormatException(string?)
        A|1|SignatureOccurrenceProvider.GetPrimitiveType | CollectionExpression | [name]
        A|5|MetadataTypeDefinitionName.Create | ObjectCreation | ILInspector.Metadata.MetadataTypeNameRejection.MetadataTypeNameRejection(ILInspector.Metadata.MetadataTypeNameRejectionKind, int?)
        A|5|MetadataTypeDefinitionName.Create | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionNameResult.Rejected.Rejected(ILInspector.Metadata.MetadataTypeNameRejection)
        A|1|MetadataTypeDefinitionName.Create | Loop | for(inti=0;i<segments.Length;i++)
        A|1|MetadataTypeDefinitionName.Create | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionName.MetadataTypeDefinitionName(string, System.Collections.Immutable.ImmutableArray<string>)
        A|1|MetadataTypeDefinitionName..ctor | ObjectCreation | System.HashCode.HashCode()
        A|2|MetadataTypeDefinitionName..ctor | Invocation | System.HashCode.Add
        A|1|MetadataTypeDefinitionName..ctor | Loop | foreach(stringsegmentinsegments)
        A|1|MetadataTypeDefinitionName.Create | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionNameResult.Valid.Valid(ILInspector.Metadata.MetadataTypeDefinitionName)
        A|1|SignatureOccurrenceProvider.GetPrimitiveType | ObjectCreation | ILInspector.Metadata.MetadataTypeReferenceScope.IntrinsicCoreLibrary.IntrinsicCoreLibrary()
        A|1|SignatureOccurrenceProvider.Named | ObjectCreation | ILInspector.Metadata.MetadataNamedTypeReference.MetadataNamedTypeReference(ILInspector.Metadata.MetadataTypeReferenceScope, ILInspector.Metadata.MetadataTypeDefinitionName)
        A|1|MetadataNamedTypeReference.EquivalentComparer | ObjectCreation | ILInspector.Metadata.MetadataNamedTypeReference.EquivalentIdentityComparer.EquivalentIdentityComparer()
        A|1|SignatureOccurrenceProvider.Named | ObjectCreation | ILInspector.Metadata.SignatureNamedTypeOccurrence.SignatureNamedTypeOccurrence(ILInspector.Metadata.MetadataNamedTypeReference, bool, ILInspector.Metadata.SignatureTypeDefinitionOrigin?)
        A|1|SignatureOccurrenceProvider.Named | CollectionExpression | [newSignatureNamedTypeOccurrence(newMetadataNamedTypeReference(scope,name),true,definition)]
        A|1|SignatureOccurrenceProvider.GetTypeFromDefinition | DelegateCreation | budget.TypeDefinitionChain
        A|1|SignatureOccurrenceProvider.GetTypeFromDefinition | DelegateCreation | budget.TypeNameCharacters
        A|1|MetadataTypeDefinitionNameReader.Read | None | stackallocTypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes]
        A|1|MetadataTypeDefinitionNameReader.RejectedTraversal | Invocation | ILInspector.Metadata.MetadataTypeNameFailure.From
        A|1|MetadataTypeDefinitionNameReader.RejectedTraversal | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionNameReadResult.Rejected.Rejected(ILInspector.Metadata.MetadataTypeNameFailure)
        A|1|MetadataTypeDefinitionNameReader.ReadChain | Invocation | System.Collections.Immutable.ImmutableArray.CreateBuilder
        A|1|MetadataTypeDefinitionNameReader.ReadChain | ObjectCreation | ILInspector.Metadata.MetadataTypeNameBudget.MetadataTypeNameBudget()
        A|2|MetadataTypeDefinitionNameReader.ReadChain | Invocation | ILInspector.Metadata.MetadataTypeNameBudget.TryRead
        A|1|MetadataTypeDefinitionNameReader.NameTooLong | InterpolatedString | $"The structured type name exceeds "
        A|1|MetadataTypeDefinitionNameReader.NameTooLong | InterpolatedString | $"{MetadataSafetyPolicy.MaxTypeNameCharacters} characters."
        A|1|MetadataTypeDefinitionNameReader.NameTooLong | Binary | $"The structured type name exceeds "+$"{MetadataSafetyPolicy.MaxTypeNameCharacters} characters."
        A|1|MetadataTypeDefinitionNameReader.NameTooLong | ObjectCreation | ILInspector.Metadata.RelationshipTraversalRejection.RelationshipTraversalRejection(ILInspector.Metadata.RelationshipTraversalRejectionKind, string, System.Reflection.Metadata.EntityHandle, int)
        A|1|MetadataTypeDefinitionNameReader.NameTooLong | Invocation | ILInspector.Metadata.MetadataTypeNameFailure.From
        A|1|MetadataTypeDefinitionNameReader.NameTooLong | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionNameReadResult.Rejected.Rejected(ILInspector.Metadata.MetadataTypeNameFailure)
        A|1|MetadataTypeDefinitionNameReader.ReadChain | Invocation | System.Collections.Immutable.ImmutableArray<T>.Builder.Add
        A|1|MetadataTypeDefinitionNameReader.RelationshipFailure | ObjectCreation | ILInspector.Metadata.RelationshipTraversalRejection.RelationshipTraversalRejection(ILInspector.Metadata.RelationshipTraversalRejectionKind, string, System.Reflection.Metadata.EntityHandle, int)
        A|1|MetadataTypeDefinitionNameReader.RelationshipFailure | Invocation | ILInspector.Metadata.MetadataTypeNameFailure.From
        A|1|MetadataTypeDefinitionNameReader.Malformed | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionNameReadResult.Rejected.Rejected(ILInspector.Metadata.MetadataTypeNameFailure)
        A|1|MetadataTypeDefinitionNameReader.ReadChain | Loop | for(inti=0;i<rootToLeaf.Length;i++)
        A|1|MetadataTypeDefinitionNameReader.ReadChain | Invocation | System.Collections.Immutable.ImmutableArray<T>.Builder.ToImmutable
        A|1|MetadataTypeDefinitionNameReader.ReadChain | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionNameReadResult.Read.Read(ILInspector.Metadata.MetadataTypeDefinitionName)
        A|1|MetadataTypeDefinitionNameReader.ReadChain | InterpolatedString | $"Invalid structured metadata type name: {invalid.Kind}."
        A|1|MetadataTypeDefinitionNameReader.ReadChain | Invocation | ILInspector.Metadata.MetadataTypeNameFailure.Malformed
        A|1|MetadataTypeDefinitionNameReader.ReadChain | ObjectCreation | ILInspector.Metadata.MetadataTypeDefinitionNameReadResult.Rejected.Rejected(ILInspector.Metadata.MetadataTypeNameFailure)
        A|1|SignatureOccurrenceProvider.RequireName | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceProvider.GetTypeFromDefinition | ObjectCreation | ILInspector.Metadata.MetadataTypeReferenceScope.CurrentAssembly.CurrentAssembly()
        A|1|SignatureOccurrenceProvider.GetTypeFromDefinition | ObjectCreation | ILInspector.Metadata.SignatureTypeDefinitionOrigin.SignatureTypeDefinitionOrigin(System.Reflection.PortableExecutable.PEReader, System.Reflection.Metadata.TypeDefinitionHandle)
        A|1|SignatureOccurrenceProvider.GetTypeFromReference | DelegateCreation | budget.TypeReferenceNameChain
        A|1|SignatureOccurrenceProvider.GetTypeFromReference | DelegateCreation | budget.TypeNameCharacters
        A|1|MetadataTypeDefinitionNameReader.Read | None | stackallocTypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes]
        A|1|SignatureOccurrenceProvider.GetTypeFromReference | None | stackallocTypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes]
        A|2|SignatureOccurrenceProvider.GetTypeFromReference | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceProvider.ReadAssemblyScope | ObjectCreation | System.BadImageFormatException.BadImageFormatException(string?)
        A|1|AssemblyReferenceIdentity.EquivalentComparer | ObjectCreation | ILInspector.Metadata.AssemblyReferenceIdentity.EquivalentIdentityComparer.EquivalentIdentityComparer()
        A|1|AssemblyReferenceIdentity.s_retainedProjections | ObjectCreation | System.Runtime.CompilerServices.ConditionalWeakTable<System.Reflection.Metadata.MetadataReader, ILInspector.Metadata.AssemblyReferenceProjectionCache>.ConditionalWeakTable()
        A|1|AssemblyReferenceIdentity.TokenOrNull | ObjectCreation | System.BadImageFormatException.BadImageFormatException(string?)
        K|1|AssemblyReferenceIdentity.TokenOrNull | Invocation | System.Reflection.Metadata.MetadataReader.GetBlobBytes
        A|1|AssemblyReferenceIdentity.ComputePublicKeyToken | Invocation | System.Security.Cryptography.SHA1.HashData
        A|1|AssemblyReferenceIdentity.ComputePublicKeyToken | None | stackallocbyte[8]
        A|1|AssemblyReferenceIdentity.ComputePublicKeyToken | Loop | for(inti=0;i<token.Length;i++)
        A|1|AssemblyReferenceIdentity.ComputePublicKeyToken | Invocation | System.Convert.ToHexString
        A|1|AssemblyReferenceIdentity.ComputePublicKeyToken | Invocation | string.ToLowerInvariant
        A|1|AssemblyReferenceIdentity.TokenOrNull | Invocation | System.Convert.ToHexString
        A|1|AssemblyReferenceIdentity.TokenOrNull | Invocation | string.ToLowerInvariant
        B|1|AssemblyReferenceIdentity.Create | Invocation | System.Reflection.Metadata.MetadataReader.GetString
        A|1|AssemblyReferenceIdentity.Create | PropertyReference | System.Reflection.Metadata.AssemblyReference.Version
        B|1|AssemblyReferenceIdentity.StringOrNull | Invocation | System.Reflection.Metadata.MetadataReader.GetString
        A|1|AssemblyReferenceIdentity.Create | ObjectCreation | ILInspector.Metadata.AssemblyReferenceIdentity.AssemblyReferenceIdentity(string, System.Version?, string?, string?)
        A|1|SignatureOccurrenceProvider.ReadAssemblyScope | ObjectCreation | ILInspector.Metadata.MetadataTypeReferenceScope.AssemblyReference.AssemblyReference(ILInspector.Metadata.AssemblyReferenceIdentity)
        B|1|SignatureOccurrenceProvider.ReadModuleScope | Invocation | System.Reflection.Metadata.MetadataReader.GetString
        A|1|SignatureOccurrenceProvider.ReadModuleScope | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceProvider.ReadModuleScope | ObjectCreation | ILInspector.Metadata.MetadataTypeReferenceScope.ModuleReference.ModuleReference(string)
        A|2|SignatureOccurrenceProvider.GetTypeFromReference | ObjectCreation | ILInspector.Metadata.MetadataTypeReferenceScope.CurrentAssembly.CurrentAssembly()
        A|3|SignatureOccurrenceProvider.GetTypeFromSpecification | ObjectCreation | ILInspector.Metadata.SignatureOccurrenceRejectedException.SignatureOccurrenceRejectedException(ILInspector.Metadata.SignatureOccurrenceRejectionReason)
        A|1|SignatureOccurrenceProvider.GetGenericTypeParameter | CollectionExpression | []
        A|1|SignatureOccurrenceProvider.GetGenericMethodParameter | CollectionExpression | []
        A|1|SignatureOccurrenceProvider.GetModifiedType | Invocation | System.Collections.Immutable.ImmutableArray.CreateBuilder
        A|1|SignatureOccurrenceProvider.GetModifiedType | With | occurrencewith{Participates=false}
        A|1|SignatureOccurrenceProvider.GetModifiedType | Invocation | System.Collections.Immutable.ImmutableArray<T>.Builder.Add
        A|1|SignatureOccurrenceProvider.GetModifiedType | Loop | foreach(varoccurrenceinmodifier)
        A|1|SignatureOccurrenceProvider.GetModifiedType | Invocation | System.Collections.Immutable.ImmutableArray<T>.Builder.AddRange
        A|1|SignatureOccurrenceProvider.GetModifiedType | Invocation | System.Collections.Immutable.ImmutableArray<T>.Builder.MoveToImmutable
        """;
}
