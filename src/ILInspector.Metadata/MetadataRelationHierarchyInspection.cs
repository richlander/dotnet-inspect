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
        if (request.HierarchyTarget is not null)
        {
            return ScanTargetedHierarchy(
                reader,
                request,
                operation,
                cancellationToken);
        }

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
                        MetadataTokens.GetToken(handle));
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
                            MetadataTokens.GetToken(row));
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

    private static MetadataRelationFamilyResult<
        MetadataHierarchyRelationEvidence> ScanTargetedHierarchy(
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        MetadataOperationContext operation,
        CancellationToken cancellationToken)
    {
        MetadataRelationFamilyResult<
            MetadataHierarchyRelationAnalysisRow> analysis =
                ScanHierarchyAnalysis(
                    reader,
                    request,
                    operation,
                    cancellationToken,
                    materializeRows: true,
                    forwardPlan: null,
                    out _,
                    out _);
        var evidence =
            ImmutableArray.CreateBuilder<
                MetadataHierarchyRelationEvidence>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        diagnostics.AddRange(analysis.Diagnostics);
        var failedSources = new HashSet<MetadataTypeDefinitionAddress>();

        foreach (MetadataHierarchyRelationAnalysisRow row
            in analysis.Evidence)
        {
            TypeDefinitionHandle sourceHandle =
                (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                    row.Source.Definition.Value);
            TypeDefinition sourceDefinition =
                reader.GetTypeDefinition(sourceHandle);
            foreach (int occurrenceToken in row.MetadataTokens)
            {
                EntityHandle target =
                    row.Kind == MetadataHierarchyRelationKind.BaseType
                        ? sourceDefinition.BaseType
                        : reader.GetInterfaceImplementation(
                            (InterfaceImplementationHandle)
                                MetadataTokens.EntityHandle(
                                    occurrenceToken))
                            .Interface;
                int before = diagnostics.Count;
                AddHierarchy(
                    reader,
                    target,
                    sourceDefinition,
                    sourceHandle,
                    row.Kind,
                    operation,
                    evidence,
                    diagnostics,
                    occurrenceToken,
                    relationshipAlreadyCharged: true);
                if (diagnostics.Count != before)
                    failedSources.Add(row.Source);
            }
        }

        MetadataRelationCoverage coverage =
            analysis.Coverage
            ?? throw new InvalidOperationException(
                "Targeted hierarchy analysis requires coverage.");
        if (failedSources.Count != 0)
        {
            int newlyUnavailable =
                Math.Min(failedSources.Count, coverage.Examined);
            coverage = new(
                coverage.Considered,
                coverage.Examined - newlyUnavailable,
                coverage.Excluded,
                checked(coverage.Unavailable + newlyUnavailable),
                coverage.Limited);
        }
        return CompleteOrPartial(
            evidence,
            diagnostics,
            coverage);
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
        bool relationshipAlreadyCharged = false)
    {
        GenericContext? context = null;

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

        if (!relationshipAlreadyCharged)
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
}
