using System.Collections.Immutable;
using System.Reflection.Metadata;

using InertText;

namespace ILInspector.Metadata;

internal abstract record MetadataTypeIdentityDecodeResult
{
    private protected MetadataTypeIdentityDecodeResult()
    {
    }

    internal sealed record Decoded(MetadataTypeIdentity Identity)
        : MetadataTypeIdentityDecodeResult;

    internal sealed record Rejected(string Detail)
        : MetadataTypeIdentityDecodeResult;
}

internal abstract record MetadataMethodSignatureDecodeResult
{
    private protected MetadataMethodSignatureDecodeResult()
    {
    }

    internal sealed record Decoded(MetadataMethodSignatureIdentity Signature)
        : MetadataMethodSignatureDecodeResult;

    internal sealed record Rejected(string Detail)
        : MetadataMethodSignatureDecodeResult;
}

internal static class MetadataTypeIdentityDecoder
{
    internal static MetadataTypeIdentityDecodeResult Decode(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext context,
        MetadataOperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            TypeNodeProvider provider = CreateProvider(operation);
            TypeNode node = handle.Kind switch
            {
                HandleKind.TypeDefinition =>
                    provider.GetTypeFromDefinition(
                        reader,
                        (TypeDefinitionHandle)handle,
                        rawTypeKind: 0x12),
                HandleKind.TypeReference =>
                    provider.GetTypeFromReference(
                        reader,
                        (TypeReferenceHandle)handle,
                        rawTypeKind: 0x12),
                HandleKind.TypeSpecification =>
                    DecodeTypeSpecification(
                        reader,
                        (TypeSpecificationHandle)handle,
                        context,
                        provider,
                        operation),
                _ => throw new BadImageFormatException(
                    "A relation endpoint must be a TypeDef, TypeRef, or TypeSpec."),
            };
            return Project(node, context, operation);
        }
        catch (MetadataOperationBudgetExceededException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return new MetadataTypeIdentityDecodeResult.Rejected(
                exception.Message);
        }
    }

    internal static MetadataMethodSignatureDecodeResult DecodeMethod(
        MetadataReader reader,
        TypeDefinition definition,
        MethodDefinition method,
        MetadataOperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            SignatureBlobGuard.CompleteValidationKind validation =
                SignatureBlobGuard.ValidateComplete(
                    reader,
                    method.Signature,
                    SignatureBlobGuard.Kind.Method);
            if (validation
                != SignatureBlobGuard.CompleteValidationKind.Valid)
            {
                return new MetadataMethodSignatureDecodeResult.Rejected(
                    "The method signature is not safe and complete to decode.");
            }

            int signatureBytes =
                reader.GetBlobReader(method.Signature).Length;
            operation.Charge(
                MetadataOperationDimension.SignatureBytes,
                signatureBytes);
            GenericContext context =
                GenericContext.ForMethod(reader, definition, method);
            MethodSignature<TypeNode> signature =
                method.DecodeSignature(
                    CreateProvider(operation),
                    context);
            MetadataTypeIdentityDecodeResult returnType =
                Project(signature.ReturnType, context, operation);
            if (returnType
                is MetadataTypeIdentityDecodeResult.Rejected rejectedReturn)
            {
                return new MetadataMethodSignatureDecodeResult.Rejected(
                    rejectedReturn.Detail);
            }

            var parameters =
                ImmutableArray.CreateBuilder<MetadataTypeIdentity>(
                    signature.ParameterTypes.Length);
            foreach (TypeNode parameter in signature.ParameterTypes)
            {
                MetadataTypeIdentityDecodeResult decoded =
                    Project(parameter, context, operation);
                if (decoded
                    is MetadataTypeIdentityDecodeResult.Rejected rejected)
                {
                    return new MetadataMethodSignatureDecodeResult.Rejected(
                        rejected.Detail);
                }
                parameters.Add(
                    ((MetadataTypeIdentityDecodeResult.Decoded)decoded)
                        .Identity);
            }

            return new MetadataMethodSignatureDecodeResult.Decoded(
                new(
                    signature.Header.RawValue,
                    signature.GenericParameterCount,
                    signature.RequiredParameterCount,
                    ((MetadataTypeIdentityDecodeResult.Decoded)returnType)
                        .Identity,
                    parameters.MoveToImmutable()));
        }
        catch (MetadataOperationBudgetExceededException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return new MetadataMethodSignatureDecodeResult.Rejected(
                exception.Message);
        }
    }

    private static MetadataTypeIdentityDecodeResult Project(
        TypeNode node,
        GenericContext context,
        MetadataOperationContext operation)
    {
        if (node.IsDegraded)
        {
            return new MetadataTypeIdentityDecodeResult.Rejected(
                "The metadata type identity could not be decoded completely.");
        }

        string? invalid = MetadataStructuralTypeValidator.Validate(
            node,
            context.TypeParameters.Count,
            context.MethodParameters.Count,
            "The relation type");
        if (invalid is not null)
            return new MetadataTypeIdentityDecodeResult.Rejected(invalid);

        MetadataTypeIdentity identity =
            new MetadataTypeIdentityProjector(
                () => operation.Charge(
                    MetadataOperationDimension.StructuredNodes),
                text =>
                {
                    operation.Charge(
                        MetadataOperationDimension.RetainedText,
                        text.Length);
                    return new InertString(TextPolicy.Field, text);
                },
                detail => new BadImageFormatException(detail))
            .Project(node);
        return new MetadataTypeIdentityDecodeResult.Decoded(identity);
    }

    private static TypeNode DecodeTypeSpecification(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        GenericContext context,
        TypeNodeProvider provider,
        MetadataOperationContext operation)
    {
        TypeSpecificationRootReadResult? failure =
            TypeSpecificationRoot.ValidateGraph(
                reader,
                handle,
                beforeDecodeBytes: (_, bytes) =>
                    operation.Charge(
                        MetadataOperationDimension.SignatureBytes,
                        bytes),
                beforeDependencyEdge: _ =>
                    operation.Charge(
                        MetadataOperationDimension.RelationshipEdges));
        if (failure is not null)
        {
            throw new BadImageFormatException(
                failure switch
                {
                    TypeSpecificationRootReadResult.BudgetExceeded value =>
                        value.Detail,
                    TypeSpecificationRootReadResult.Malformed value =>
                        value.Detail,
                    TypeSpecificationRootReadResult.Cycle value =>
                        value.Detail,
                    TypeSpecificationRootReadResult.Unsupported value =>
                        value.Detail,
                    _ => "The TypeSpec dependency graph is invalid.",
                });
        }

        return reader.GetTypeSpecification(handle)
            .DecodeSignature(provider, context);
    }

    private static TypeNodeProvider CreateProvider(
        MetadataOperationContext operation) =>
        new(
            beforeRetain: text => operation.Charge(
                MetadataOperationDimension.RetainedText,
                text.Length),
            beforeMaterialize: amount => operation.Charge(
                MetadataOperationDimension.StructuredNodes,
                amount),
            beforeCreateNode: () => operation.Charge(
                MetadataOperationDimension.StructuredNodes),
            beforeTypeSpecificationDecode: (reader, handle) =>
            {
                TypeSpecificationRootReadResult? failure =
                    TypeSpecificationRoot.ValidateGraph(
                        reader,
                        handle,
                        beforeDecodeBytes: (_, bytes) =>
                            operation.Charge(
                                MetadataOperationDimension.SignatureBytes,
                                bytes),
                        beforeDependencyEdge: _ =>
                            operation.Charge(
                                MetadataOperationDimension
                                    .RelationshipEdges));
                if (failure is not null)
                {
                    throw new BadImageFormatException(
                        "A nested TypeSpec dependency graph is invalid.");
                }
            },
            retainExactScope: true,
            beforeTypeDefinitionResolve: (_, _) =>
                operation.Charge(
                    MetadataOperationDimension.RelationshipEdges),
            beforeTypeReferenceResolve: (_, _) =>
                operation.Charge(
                    MetadataOperationDimension.RelationshipEdges),
            relationshipRejected: rejection =>
                throw new BadImageFormatException(rejection.Detail),
            beforeRelationshipFollow: _ =>
                operation.Charge(
                    MetadataOperationDimension.RelationshipEdges));
}
