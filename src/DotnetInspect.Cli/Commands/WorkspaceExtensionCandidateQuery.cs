using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class WorkspaceExtensionCandidateQuery
{
    const int DefaultMaximumRowsPerParticipant = 20;

    internal static WorkspaceExtensionCandidatePopulation?
        ExecuteIfRequested(
            AssemblyContextGroup group,
            ApiType type,
            ResolvedAssemblyReference assembly,
            ApiOptions options,
            CancellationToken cancellationToken)
    {
        if (!IsExplicitlyRequested(options))
            return null;

        MetadataTypeDefinitionName receiverType =
            type.DefinitionName
            ?? throw new InvalidOperationException(
                "A selected workspace Type requires a structured Metadata identity.");
        int maximumRows =
            options.Limit ?? DefaultMaximumRowsPerParticipant;
        if (maximumRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.Limit));

        var receiver = new MetadataExtensionReceiverSelection(
            assembly.Identity,
            receiverType);
        AssemblyContextResult<MetadataExtensionRelationPopulationOutcome>
            result = AssemblyContextExtensionCandidatesQuery.Execute(
                group,
                new AssemblyContextExtensionCandidatePopulationRequest(
                    receiver,
                MetadataOperationPolicy.Unbounded,
                count: new(),
                rows: new(maximumRows),
                includeNonPublic: options.IncludeAll),
            cancellationToken);
        return new WorkspaceExtensionCandidatePopulation(
            receiver,
            result);
    }

    internal static bool IsComplete(
        WorkspaceExtensionCandidatePopulation? population)
    {
        if (population is null)
            return true;
        AssemblyContextResult<MetadataExtensionRelationPopulationOutcome>
            result = population.Result;
        if (result.Assemblies.IsEmpty)
            return false;

        return result.Assemblies.All(
            static entry =>
                entry is AssemblyContextEntry<
                    MetadataExtensionRelationPopulationOutcome>.Available
                {
                    Value:
                        MetadataExtensionRelationPopulationOutcome.Available
                        {
                            Result:
                            {
                                Disposition:
                                    MetadataRelationFamilyDisposition.Complete,
                                Count:
                                    MetadataExtensionRelationPopulationCountOutcome.Counted,
                                Rows:
                                    MetadataExtensionRelationPopulationRowsOutcome.Read,
                            },
                        },
                });
    }

    internal static bool IsExplicitlyRequested(ApiOptions options) =>
        options.IncludeSections?.Contains(
            SectionNames.ExtensionMethods,
            StringComparer.OrdinalIgnoreCase) == true
        || options.Select?.Contains(
            SectionNames.ExtensionMethods,
            StringComparer.OrdinalIgnoreCase) == true;
}

internal sealed record WorkspaceExtensionCandidatePopulation(
    MetadataExtensionReceiverSelection Receiver,
    AssemblyContextResult<MetadataExtensionRelationPopulationOutcome>
        Result);
