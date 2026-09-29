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
        using var analysis =
            new MetadataHierarchyRelationAnalysis(
                reader,
                operation,
                visibility);
        try
        {
            foreach (TypeDefinitionHandle handle
                in reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.IncludesType(reader, handle))
                    continue;
                considered++;
                if (!analysis.IncludesSource(
                        handle,
                        request.IncludeNonPublic,
                        request.IncludeHidden))
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
                        analysis,
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
                        InterfaceImplementation implementation =
                            analysis.ReadInterfaceImplementation(row);
                        AddHierarchy(
                            reader,
                            implementation.Interface,
                            definition,
                            handle,
                            MetadataHierarchyRelationKind.Interface,
                            analysis,
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
        MetadataHierarchyRelationAnalysis analysis,
        MetadataOperationContext operation,
        ImmutableArray<MetadataHierarchyRelationEvidence>.Builder
            evidence,
        ImmutableArray<MetadataRelationDiagnostic>.Builder
            diagnostics,
        int? occurrenceToken,
        MetadataHierarchyTargetSelection? targetSelection)
    {
        if (targetSelection is not null)
        {
            MetadataHierarchyRelationAnalysisResult result =
                analysis.Analyze(
                    sourceHandle,
                    sourceDefinition,
                    target,
                    kind,
                    occurrenceToken
                        ?? MetadataTokens.GetToken(target),
                    targetSelection);
            if (result
                is MetadataHierarchyRelationAnalysisResult.Rejected rejected)
            {
                diagnostics.Add(
                    UnsupportedDiagnostic(
                        MetadataRelationFamily.Hierarchy,
                        occurrenceToken,
                        rejected.Detail));
                return;
            }
            if (result
                is MetadataHierarchyRelationAnalysisResult.NoMatch)
                return;
            evidence.Add(
                ((MetadataHierarchyRelationAnalysisResult.Selected)result)
                    .Evidence);
            return;
        }

        MetadataTypeDefinitionName? sourceName =
            ReadTypeName(
                reader,
                sourceHandle,
                MetadataRelationFamily.Hierarchy,
                diagnostics);
        if (sourceName is null)
            return;
        GenericContext context = GenericContext.ForType(
            reader,
            sourceDefinition);
        MetadataTypeIdentityDecodeResult decoded =
            MetadataTypeIdentityDecoder.Decode(
                reader,
                target,
                context,
                operation);
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

        operation.Charge(
            MetadataOperationDimension.RelationshipEdges);
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
}
