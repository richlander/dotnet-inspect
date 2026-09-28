using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

internal sealed record LibrarySignatureUseReferenceResult(
    int TypeCount,
    int SiteCount,
    ImmutableArray<MetadataLibrarySignatureUseOccurrence> Occurrences);

internal static class LibrarySignatureUseReferenceScanner
{
    internal static LibrarySignatureUseReferenceResult Scan(
        string path,
        CancellationToken cancellationToken)
    {
        using FileStream stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        return Scan(image, cancellationToken);
    }

    internal static LibrarySignatureUseReferenceResult Scan(
        PEReader image,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        MetadataReader reader =
            image.GetMetadataReader(MetadataReaderOptions.None);
        Guid moduleVersionId =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        AssemblyReferenceIdentity assembly =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
        var types = new List<ReferenceType>();
        var typesByName =
            new Dictionary<
                MetadataTypeDefinitionName,
                ReferenceType>();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionName name = ReadName(reader, handle);
            if (name.Namespace.Length == 0
                && name.Segments is ["<Module>"])
            {
                continue;
            }

            var type = new ReferenceType(
                handle,
                MetadataTypeDefinitionAddress.FromHandle(reader, handle),
                name);
            types.Add(type);
            if (!typesByName.TryAdd(name, type))
            {
                throw new BadImageFormatException(
                    "The reference route found duplicate exact Type names.");
            }
        }

        var occurrences =
            ImmutableArray.CreateBuilder<
                MetadataLibrarySignatureUseOccurrence>();
        int sites = 0;
        foreach (ReferenceType source in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition definition =
                reader.GetTypeDefinition(source.Handle);
            if (!definition.BaseType.IsNil)
            {
                AddEntity(
                    definition.BaseType,
                    MetadataLibrarySignatureUseSiteKind.BaseType,
                    MetadataTokens.GetToken(source.Handle));
            }

            foreach (InterfaceImplementationHandle implementationHandle
                in definition.GetInterfaceImplementations())
            {
                InterfaceImplementation implementation =
                    reader.GetInterfaceImplementation(
                        implementationHandle);
                AddEntity(
                    implementation.Interface,
                    MetadataLibrarySignatureUseSiteKind.Interface,
                    MetadataTokens.GetToken(implementationHandle));
            }

            AddConstraints(
                definition.GetGenericParameters(),
                MetadataLibrarySignatureUseSiteKind.TypeConstraint);

            foreach (FieldDefinitionHandle fieldHandle
                in definition.GetFields())
            {
                FieldDefinition field =
                    reader.GetFieldDefinition(fieldHandle);
                AddSite(
                    MetadataTokens.GetToken(fieldHandle),
                    provider =>
                    {
                        RequireSafe(
                            field.Signature,
                            SignatureBlobGuard.Kind.Field);
                        return
                        [
                            (
                                field.DecodeSignature(
                                    provider,
                                    genericContext: (object?)null),
                                MetadataLibrarySignatureUseSiteKind.FieldType),
                        ];
                    });
            }

            foreach (PropertyDefinitionHandle propertyHandle
                in definition.GetProperties())
            {
                PropertyDefinition property =
                    reader.GetPropertyDefinition(propertyHandle);
                AddSite(
                    MetadataTokens.GetToken(propertyHandle),
                    provider =>
                    {
                        RequireSafe(
                            property.Signature,
                            SignatureBlobGuard.Kind.Method);
                        MethodSignature<
                            ImmutableArray<SignatureNamedTypeOccurrence>>
                            signature =
                                property.DecodeSignature(
                                    provider,
                                    genericContext: (object?)null);
                        var parts =
                            new List<(
                                ImmutableArray<
                                    SignatureNamedTypeOccurrence>,
                                MetadataLibrarySignatureUseSiteKind)>
                            {
                                (
                                    signature.ReturnType,
                                    MetadataLibrarySignatureUseSiteKind
                                        .PropertyType),
                            };
                        parts.AddRange(
                            signature.ParameterTypes.Select(
                                static parameter =>
                                    (
                                        parameter,
                                        MetadataLibrarySignatureUseSiteKind
                                            .PropertyParameter)));
                        return parts;
                    });
            }

            foreach (EventDefinitionHandle eventHandle
                in definition.GetEvents())
            {
                AddEntity(
                    reader.GetEventDefinition(eventHandle).Type,
                    MetadataLibrarySignatureUseSiteKind.EventType,
                    MetadataTokens.GetToken(eventHandle));
            }

            foreach (MethodDefinitionHandle methodHandle
                in definition.GetMethods())
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                AddSite(
                    MetadataTokens.GetToken(methodHandle),
                    provider =>
                    {
                        RequireSafe(
                            method.Signature,
                            SignatureBlobGuard.Kind.Method);
                        MethodSignature<
                            ImmutableArray<SignatureNamedTypeOccurrence>>
                            signature =
                                method.DecodeSignature(
                                    provider,
                                    genericContext: (object?)null);
                        var parts =
                            new List<(
                                ImmutableArray<
                                    SignatureNamedTypeOccurrence>,
                                MetadataLibrarySignatureUseSiteKind)>
                            {
                                (
                                    signature.ReturnType,
                                    MetadataLibrarySignatureUseSiteKind
                                        .MethodReturn),
                            };
                        parts.AddRange(
                            signature.ParameterTypes.Select(
                                static parameter =>
                                    (
                                        parameter,
                                        MetadataLibrarySignatureUseSiteKind
                                            .MethodParameter)));
                        return parts;
                    });
                AddConstraints(
                    method.GetGenericParameters(),
                    MetadataLibrarySignatureUseSiteKind.MethodConstraint);
            }

            void AddConstraints(
                GenericParameterHandleCollection parameters,
                MetadataLibrarySignatureUseSiteKind kind)
            {
                foreach (GenericParameterHandle parameterHandle
                    in parameters)
                {
                    GenericParameter parameter =
                        reader.GetGenericParameter(parameterHandle);
                    foreach (GenericParameterConstraintHandle constraintHandle
                        in parameter.GetConstraints())
                    {
                        GenericParameterConstraint constraint =
                            reader.GetGenericParameterConstraint(
                                constraintHandle);
                        AddEntity(
                            constraint.Type,
                            kind,
                            MetadataTokens.GetToken(constraintHandle));
                    }
                }
            }

            void AddEntity(
                EntityHandle entity,
                MetadataLibrarySignatureUseSiteKind kind,
                int token) =>
                AddSite(
                    token,
                    provider =>
                    [
                        (
                            DecodeEntity(reader, entity, provider),
                            kind),
                    ]);

            void AddSite(
                int token,
                Func<
                    SignatureOccurrenceProvider,
                    IReadOnlyList<(
                        ImmutableArray<SignatureNamedTypeOccurrence>
                            Occurrences,
                        MetadataLibrarySignatureUseSiteKind Kind)>> decode)
            {
                sites++;
                var provider = new SignatureOccurrenceProvider(
                    image,
                    new(
                        SignatureOccurrenceLimits.Default,
                        metrics: null));
                int ordinal = 0;
                foreach (var part in decode(provider))
                {
                    foreach (SignatureNamedTypeOccurrence occurrence
                        in part.Occurrences)
                    {
                        if (occurrence.Reference.Scope
                            is MetadataTypeReferenceScope
                                .IntrinsicCoreLibrary)
                        {
                            continue;
                        }
                        int currentOrdinal = ordinal++;
                        if (!occurrence.Participates
                            || !TryBind(
                                occurrence.Reference,
                                out ReferenceType? target))
                        {
                            continue;
                        }

                        occurrences.Add(
                            new(
                                source.Address,
                                source.Name,
                                target.Address,
                                target.Name,
                                part.Kind,
                                token,
                                currentOrdinal));
                    }
                }
            }
        }

        return new(
            types.Count,
            sites,
            occurrences.ToImmutable());

        bool TryBind(
            MetadataNamedTypeReference reference,
            out ReferenceType target)
        {
            target = null!;
            bool local =
                reference.Scope
                    is MetadataTypeReferenceScope.CurrentAssembly;
            bool selfReference =
                reference.Scope
                    is MetadataTypeReferenceScope.AssemblyReference
                    assemblyReference
                && assemblyReference.Assembly.IsEquivalentTo(assembly);
            return (local || selfReference)
                && typesByName.TryGetValue(reference.Type, out target!);
        }

        void RequireSafe(
            BlobHandle signature,
            SignatureBlobGuard.Kind kind)
        {
            if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                    reader,
                    signature,
                    kind))
            {
                throw new BadImageFormatException(
                    "The reference signature is unsafe.");
            }
        }
    }

    private static ImmutableArray<SignatureNamedTypeOccurrence>
        DecodeEntity(
            MetadataReader reader,
            EntityHandle entity,
            SignatureOccurrenceProvider provider) =>
        entity.Kind switch
        {
            HandleKind.TypeDefinition =>
                provider.GetTypeFromDefinition(
                    reader,
                    (TypeDefinitionHandle)entity,
                    rawTypeKind: 0x12),
            HandleKind.TypeReference =>
                provider.GetTypeFromReference(
                    reader,
                    (TypeReferenceHandle)entity,
                    rawTypeKind: 0x12),
            HandleKind.TypeSpecification =>
                provider.GetTypeFromSpecification(
                    reader,
                    genericContext: null,
                    (TypeSpecificationHandle)entity,
                    rawTypeKind: 0x12),
            _ => throw new BadImageFormatException(
                "A reference relation endpoint has an unsupported handle."),
        };

    private static MetadataTypeDefinitionName ReadName(
        MetadataReader reader,
        TypeDefinitionHandle handle) =>
        MetadataTypeDefinitionNameReader.Read(reader, handle)
            is MetadataTypeDefinitionNameReadResult.Read read
                ? read.Name
                : throw new BadImageFormatException(
                    "A reference TypeDef name is unavailable.");

    private sealed record ReferenceType(
        TypeDefinitionHandle Handle,
        MetadataTypeDefinitionAddress Address,
        MetadataTypeDefinitionName Name);
}
