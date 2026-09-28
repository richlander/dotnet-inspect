using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal static partial class MetadataRelationInspection
{
    private static MetadataRelationFamilyResult<
        MetadataHierarchyRelationEvidence> ScanHierarchy(
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        MetadataVisibilityClassification? visibility,
        MetadataOperationContext operation,
        CancellationToken cancellationToken)
    {
        var evidence =
            ImmutableArray.CreateBuilder<
                MetadataHierarchyRelationEvidence>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        int considered = 0;
        int excluded = 0;
        int examined = 0;
        int unavailable = 0;
        bool limited = false;
        try
        {
            foreach (TypeDefinitionHandle handle
                in reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.IncludesType(reader, handle))
                    continue;
                considered++;
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                if ((!request.IncludeNonPublic
                        && !(visibility?.IsExternallyVisible(handle)
                            ?? MetadataVisibility.IsExternallyVisible(
                                reader,
                                handle)))
                    || (!request.IncludeHidden
                        && AttributeReader.HasHiddenAttribute(
                            reader,
                            reader.GetTypeDefinition(handle)
                                .GetCustomAttributes())))
                {
                    excluded++;
                    continue;
                }

                TypeDefinition definition =
                    reader.GetTypeDefinition(handle);
                int diagnosticCount = diagnostics.Count;
                if (!definition.BaseType.IsNil)
                {
                    AddHierarchy(
                        reader,
                        definition.BaseType,
                        definition,
                        handle,
                        MetadataHierarchyRelationKind.BaseType,
                        operation,
                        evidence,
                        diagnostics,
                        MetadataTokens.GetToken(handle),
                        request.HierarchyTarget);
                }

                if (request.HierarchyTarget?.Kind
                    is not MetadataHierarchyRelationKind.BaseType)
                {
                    foreach (InterfaceImplementationHandle row
                        in definition.GetInterfaceImplementations())
                    {
                        operation.Charge(
                            MetadataOperationDimension
                                .InterfaceImplementationRows);
                        InterfaceImplementation implementation =
                            reader.GetInterfaceImplementation(row);
                        AddHierarchy(
                            reader,
                            implementation.Interface,
                            definition,
                            handle,
                            MetadataHierarchyRelationKind.Interface,
                            operation,
                            evidence,
                            diagnostics,
                            MetadataTokens.GetToken(row),
                            request.HierarchyTarget);
                    }
                }
                if (diagnostics.Count == diagnosticCount)
                    examined++;
                else
                    unavailable++;
            }
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            limited = true;
            diagnostics.Add(
                LimitDiagnostic(
                    MetadataRelationFamily.Hierarchy,
                    exception));
        }
        catch (BadImageFormatException exception)
        {
            diagnostics.Add(
                MalformedDiagnostic(
                    MetadataRelationFamily.Hierarchy,
                    null,
                    exception.Message));
        }

        int remaining =
            considered - examined - excluded - unavailable;
        if (considered == 0 && diagnostics.Count != 0)
        {
            return CompleteOrPartial(
                evidence,
                diagnostics,
                new(1, 0, 0, 1, 0));
        }
        if (!limited)
            unavailable += remaining;
        return CompleteOrPartial(
            evidence,
            diagnostics,
            new(
                considered,
                examined,
                excluded,
                unavailable,
                limited ? remaining : 0));
    }

    private static void AddHierarchy(
        MetadataReader reader,
        EntityHandle target,
        TypeDefinition sourceDefinition,
        TypeDefinitionHandle sourceHandle,
        MetadataHierarchyRelationKind kind,
        MetadataOperationContext operation,
        ImmutableArray<MetadataHierarchyRelationEvidence>.Builder
            evidence,
        ImmutableArray<MetadataRelationDiagnostic>.Builder
            diagnostics,
        int? occurrenceToken,
        MetadataHierarchyTargetSelection? targetSelection)
    {
        if (targetSelection?.Kind is { } kindSelection
            && kindSelection != kind)
        {
            return;
        }

        GenericContext? context = null;
        MetadataTypeIdentity? selectedTarget = null;
        if (targetSelection is not null)
        {
            operation.Charge(
                MetadataOperationDimension.RelationshipEdges);
            MetadataTypeNameFailure? directFailure = null;
            MetadataTypeDefinitionNameMatchResult? directMatch;
            if (target.Kind == HandleKind.TypeDefinition)
            {
                directMatch = MetadataTypeDefinitionName.Matches(
                    reader,
                    (TypeDefinitionHandle)target,
                    targetSelection.Type,
                    out directFailure);
            }
            else if (target.Kind == HandleKind.TypeReference)
            {
                directMatch = MetadataTypeDefinitionName.Matches(
                    reader,
                    (TypeReferenceHandle)target,
                    targetSelection.Type,
                    out directFailure);
            }
            else if (target.Kind == HandleKind.TypeSpecification)
            {
                directMatch = PrefilterTypeSpecification(
                    reader,
                    (TypeSpecificationHandle)target,
                    targetSelection.Type,
                    out directFailure);
            }
            else
            {
                directMatch = null;
            }
            if (directMatch
                is MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                diagnostics.Add(
                    UnsupportedDiagnostic(
                        MetadataRelationFamily.Hierarchy,
                        occurrenceToken,
                        directFailure?.Detail
                            ?? "The hierarchy target name could not be read."));
                return;
            }
            if (directMatch
                is MetadataTypeDefinitionNameMatchResult.NoMatch)
            {
                return;
            }
            if (directMatch is null)
            {
                context = GenericContext.ForType(
                    reader,
                    sourceDefinition);
                MetadataTypeIdentityDecodeResult candidate =
                    MetadataTypeIdentityDecoder.Decode(
                        reader,
                        target,
                        context,
                        operation);
                if (candidate
                    is MetadataTypeIdentityDecodeResult.Rejected rejected)
                {
                    diagnostics.Add(
                        UnsupportedDiagnostic(
                            MetadataRelationFamily.Hierarchy,
                            occurrenceToken,
                            rejected.Detail));
                    return;
                }
                selectedTarget =
                    ((MetadataTypeIdentityDecodeResult.Decoded)candidate)
                        .Identity;
                MetadataNamedTypeIdentity? named = selectedTarget switch
                {
                    MetadataTypeIdentity.Named value => value.Definition,
                    MetadataTypeIdentity.GenericInstance value =>
                        value.Definition,
                    _ => null,
                };
                if (named is null
                    || MetadataTypeDefinitionName.Create(
                            named.Namespace.ToString(),
                            [.. named.Segments.Select(static segment =>
                                segment.ToString())])
                        is not MetadataTypeDefinitionNameResult.Valid valid
                    || valid.Name != targetSelection.Type)
                {
                    return;
                }
            }
        }

        MetadataTypeDefinitionName? sourceName =
            ReadTypeName(
                reader,
                sourceHandle,
                MetadataRelationFamily.Hierarchy,
                diagnostics);
        if (sourceName is null)
            return;
        context ??= GenericContext.ForType(
            reader,
            sourceDefinition);
        MetadataTypeIdentityDecodeResult decoded =
            selectedTarget is null
                ? MetadataTypeIdentityDecoder.Decode(
                    reader,
                    target,
                    context,
                    operation)
                : new MetadataTypeIdentityDecodeResult.Decoded(
                    selectedTarget);
        if (decoded
            is MetadataTypeIdentityDecodeResult.Rejected rejectedTarget)
        {
            diagnostics.Add(
                UnsupportedDiagnostic(
                    MetadataRelationFamily.Hierarchy,
                    occurrenceToken
                        ?? MetadataTokens.GetToken(target),
                    rejectedTarget.Detail));
            return;
        }

        if (targetSelection is null)
        {
            operation.Charge(
                MetadataOperationDimension.RelationshipEdges);
        }
        evidence.Add(
            new(
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    sourceHandle),
                sourceName,
                kind,
                ((MetadataTypeIdentityDecodeResult.Decoded)decoded)
                    .Identity,
                occurrenceToken
                    ?? MetadataTokens.GetToken(target)));
    }

    static MetadataTypeDefinitionNameMatchResult?
        PrefilterTypeSpecification(
            MetadataReader reader,
            TypeSpecificationHandle handle,
            MetadataTypeDefinitionName target,
            out MetadataTypeNameFailure? failure)
    {
        failure = null;
        try
        {
            BlobReader signature =
                reader.GetBlobReader(
                    reader.GetTypeSpecification(handle).Signature);
            if (signature.ReadSignatureTypeCode()
                != SignatureTypeCode.GenericTypeInstance)
            {
                return null;
            }

            if (signature.ReadSignatureTypeCode()
                != SignatureTypeCode.TypeHandle)
            {
                return null;
            }
            EntityHandle definition = signature.ReadTypeHandle();
            return definition.Kind switch
            {
                HandleKind.TypeDefinition =>
                    MetadataTypeDefinitionName.Matches(
                        reader,
                        (TypeDefinitionHandle)definition,
                        target,
                        out failure),
                HandleKind.TypeReference =>
                    MetadataTypeDefinitionName.Matches(
                        reader,
                        (TypeReferenceHandle)definition,
                        target,
                        out failure),
                _ => null,
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
