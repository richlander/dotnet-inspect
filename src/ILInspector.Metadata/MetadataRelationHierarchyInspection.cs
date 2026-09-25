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
        MetadataVisibilityClassification visibility,
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
        foreach (TypeDefinitionHandle handle
            in reader.TypeDefinitions)
        {
            if (!request.IncludesType(reader, handle))
                continue;
            considered++;
            if ((!request.IncludeNonPublic
                    && !visibility.IsExternallyVisible(handle))
                || (!request.IncludeHidden
                    && AttributeReader.HasHiddenAttribute(
                        reader,
                        reader.GetTypeDefinition(handle)
                            .GetCustomAttributes())))
            {
                excluded++;
            }
        }
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
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                if ((!request.IncludeNonPublic
                        && !visibility.IsExternallyVisible(handle))
                    || (!request.IncludeHidden
                        && AttributeReader.HasHiddenAttribute(
                            reader,
                            reader.GetTypeDefinition(handle)
                                .GetCustomAttributes())))
                {
                    continue;
                }

                TypeDefinition definition =
                    reader.GetTypeDefinition(handle);
                MetadataTypeDefinitionName? sourceName =
                    ReadTypeName(
                        reader,
                        handle,
                        MetadataRelationFamily.Hierarchy,
                        diagnostics);
                if (sourceName is null)
                {
                    unavailable++;
                    continue;
                }
                int diagnosticCount = diagnostics.Count;
                MetadataTypeDefinitionAddress source =
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        handle);
                GenericContext context =
                    GenericContext.ForType(reader, definition);

                if (!definition.BaseType.IsNil)
                {
                    AddHierarchy(
                        reader,
                        definition.BaseType,
                        context,
                        source,
                        sourceName,
                        MetadataHierarchyRelationKind.BaseType,
                        operation,
                        evidence,
                        diagnostics,
                        MetadataTokens.GetToken(handle));
                }

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
                        context,
                        source,
                        sourceName,
                        MetadataHierarchyRelationKind.Interface,
                        operation,
                        evidence,
                        diagnostics,
                        MetadataTokens.GetToken(row));
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
        GenericContext context,
        MetadataTypeDefinitionAddress source,
        MetadataTypeDefinitionName sourceName,
        MetadataHierarchyRelationKind kind,
        MetadataOperationContext operation,
        ImmutableArray<MetadataHierarchyRelationEvidence>.Builder
            evidence,
        ImmutableArray<MetadataRelationDiagnostic>.Builder
            diagnostics,
        int? occurrenceToken = null)
    {
        MetadataTypeIdentityDecodeResult decoded =
            MetadataTypeIdentityDecoder.Decode(
                reader,
                target,
                context,
                operation);
        if (decoded is MetadataTypeIdentityDecodeResult.Rejected rejected)
        {
            diagnostics.Add(
                UnsupportedDiagnostic(
                    MetadataRelationFamily.Hierarchy,
                    occurrenceToken
                        ?? MetadataTokens.GetToken(target),
                    rejected.Detail));
            return;
        }

        operation.Charge(
            MetadataOperationDimension.RelationshipEdges);
        evidence.Add(
            new(
                source,
                sourceName,
                kind,
                ((MetadataTypeIdentityDecodeResult.Decoded)decoded)
                    .Identity,
                occurrenceToken
                    ?? MetadataTokens.GetToken(target)));
    }
}
