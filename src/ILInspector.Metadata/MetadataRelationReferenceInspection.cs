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
        MetadataAssemblyReferenceRelationEvidence>
        ScanAssemblyReferences(
            MetadataReader reader,
            MetadataOperationContext operation,
            CancellationToken cancellationToken)
    {
        var evidence =
            ImmutableArray.CreateBuilder<
                MetadataAssemblyReferenceRelationEvidence>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        int considered = reader.AssemblyReferences.Count;
        bool limited = false;
        if (!reader.IsAssembly)
        {
            diagnostics.Add(
                new(
                    MetadataRelationFamily.AssemblyReferences,
                    MetadataRelationDiagnosticKind.UnsupportedShape,
                    null,
                    "A module without an Assembly row cannot issue assembly-reference relations."));
            return new(
                true,
                MetadataRelationFamilyDisposition.Unavailable,
                new(1, 0, 0, 1, 0),
                [],
                diagnostics);
        }

        try
        {
            AssemblyReferenceIdentity source =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader);
            foreach (AssemblyReferenceHandle handle
                in reader.AssemblyReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                AssemblyReferenceIdentity target =
                    AssemblyReferenceIdentity.From(reader, handle);
                operation.Charge(
                    MetadataOperationDimension.RelationshipEdges);
                evidence.Add(
                    new(
                        source,
                        target,
                        MetadataTokens.GetToken(handle)));
            }
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            limited = true;
            diagnostics.Add(
                LimitDiagnostic(
                    MetadataRelationFamily.AssemblyReferences,
                    exception));
        }
        catch (BadImageFormatException exception)
        {
            diagnostics.Add(
                MalformedDiagnostic(
                    MetadataRelationFamily.AssemblyReferences,
                    null,
                    exception.Message));
        }

        int remaining = considered - evidence.Count;
        return CompleteOrPartial(
            evidence,
            diagnostics,
            new(
                considered,
                evidence.Count,
                0,
                limited ? 0 : remaining,
                limited ? remaining : 0));
    }
}
