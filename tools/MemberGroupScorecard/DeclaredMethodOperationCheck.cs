using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace MemberGroupScorecard;

internal static class DeclaredMethodOperationCheck
{
    internal static async Task<int> CheckAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataTypeDefinitionName type =
            MetadataTypeDefinitionName.Create(
                "System.Text.Json",
                ["JsonSerializer"])
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : throw new InvalidOperationException(
                    "The scorecard Type name was invalid.");
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "declared-method scorecard"));
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var count = AssertCounted(
            Execute(
                group,
                participant,
                type,
                QuerySpaceTerminalRequirement.Count,
                cancellationToken));
        var rows = AssertRead(
            Execute(
                group,
                participant,
                type,
                QuerySpaceTerminalRequirement.Rows,
                cancellationToken));
        if (count.Count != rows.Count
            || count.Count != rows.Rows.Length)
        {
            throw new InvalidOperationException(
                "QuerySpace declared MethodDef Count and Rows disagree.");
        }
        if (count.Receipt.MethodDefinitionHandlesVisited != 0
            || count.Receipt.MethodDefinitionRowsRead != 0
            || count.Receipt.MethodNamesDecoded != 0
            || count.Receipt.MethodSignaturesDecoded != 0
            || count.Receipt.MethodAttributesDecoded != 0
            || count.Receipt.ProjectedRows != 0)
        {
            throw new InvalidOperationException(
                "QuerySpace declared MethodDef Count performed per-method work.");
        }

        return count.Count;
    }

    private static TypeDeclaredMethodPopulationOutcome Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        MetadataTypeDefinitionName type,
        QuerySpaceTerminalRequirement terminal,
        CancellationToken cancellationToken) =>
        TypeDeclaredMethodPopulationInspectionOperation.Execute(
            new(
                group,
                participant,
                type,
                TypeDeclaredMethodPopulationQuery.CreateRequest(terminal)),
            cancellationToken).Content;

    private static TypeDeclaredMethodPopulationOutcome.Counted
        AssertCounted(TypeDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as TypeDeclaredMethodPopulationOutcome.Counted
            ?? throw new InvalidOperationException(
                $"Expected QuerySpace Counted, got {outcome}.");

    private static TypeDeclaredMethodPopulationOutcome.Read
        AssertRead(TypeDeclaredMethodPopulationOutcome outcome) =>
            outcome
                as TypeDeclaredMethodPopulationOutcome.Read
            ?? throw new InvalidOperationException(
                $"Expected QuerySpace Read, got {outcome}.");
}
