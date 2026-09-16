using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.CommandLine;

internal abstract record PackageAssemblySemanticQueryPopulationPlan
{
    private protected PackageAssemblySemanticQueryPopulationPlan()
    {
    }

    internal sealed record Exact(string PackageId)
        : PackageAssemblySemanticQueryPopulationPlan;

    internal sealed record Prefix(
        PackagePrefixDeclaration Declaration,
        int MaximumCandidates)
        : PackageAssemblySemanticQueryPopulationPlan;
}

internal sealed record PackageAssemblySemanticQueryCliPlan(
    PackageAssemblyPatternRequest Pattern,
    PackageHouseTargetContext Target,
    PackageAssemblySemanticQueryPopulationPlan Population,
    bool IncludePrerelease)
{
    internal static PackageAssemblySemanticQueryCliPlan Create(
        string input,
        string literal,
        string targetFramework,
        int? candidateTake,
        bool includePrerelease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);

        int maximumCandidates =
            candidateTake ?? PackageAcquisitionPopulation.MaximumCandidates;
        PackageQueryPlanResult inputPlan = PackageQuery.PlanInput(
            input,
            maximumCandidates: maximumCandidates,
            maximumMatches: null,
            includePrerelease: includePrerelease);
        if (inputPlan is PackageQueryPlanResult.Rejected rejected)
            throw new ArgumentException(rejected.Failure.Message, nameof(input));

        PackageQueryPlan packagePlan =
            ((PackageQueryPlanResult.Accepted)inputPlan).Plan;
        PackageAssemblySemanticQueryPopulationPlan population =
            packagePlan.PackageInput switch
            {
                SourceSelector.Package exact
                    when candidateTake is null or 1 =>
                    new PackageAssemblySemanticQueryPopulationPlan.Exact(
                        exact.Coordinate.PackageId),
                SourceSelector.Package =>
                    throw new ArgumentException(
                        "An exact package query admits one candidate; omit --take or use --take 1.",
                        nameof(candidateTake)),
                SourceSelector.PackagePrefix prefix
                    when maximumCandidates is >= 1
                        and <= PackageAcquisitionPopulation.MaximumCandidates =>
                    new PackageAssemblySemanticQueryPopulationPlan.Prefix(
                        new PackagePrefixDeclaration(prefix.Request.Prefix),
                        maximumCandidates),
                SourceSelector.PackagePrefix =>
                    throw new ArgumentException(
                        "--library-literal --take must be between 1 and "
                        + $"{PackageAcquisitionPopulation.MaximumCandidates}.",
                        nameof(candidateTake)),
                _ => throw new InvalidOperationException(
                    "Unknown Package Query input selection."),
            };

        return new(
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                literal),
            PackageHouseTargetContext.Exact(targetFramework),
            population,
            includePrerelease);
    }
}
