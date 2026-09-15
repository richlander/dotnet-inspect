using System.Collections.Immutable;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspect.Cli.CommandLine;

internal abstract record PackageAssemblySemanticFindPopulationPlan
{
    private protected PackageAssemblySemanticFindPopulationPlan()
    {
    }

    internal sealed record Exact(
        ImmutableArray<PackageSourceCoordinate> Coordinates)
        : PackageAssemblySemanticFindPopulationPlan;

    internal sealed record Prefix(
        PackagePrefixDeclaration Declaration,
        int MaximumCandidates)
        : PackageAssemblySemanticFindPopulationPlan;
}

internal sealed record PackageAssemblySemanticFindCliPlan(
    PackageAssemblyPatternRequest Pattern,
    PackageHouseTargetContext Target,
    PackageAssemblySemanticFindPopulationPlan Population)
{
    internal static PackageAssemblySemanticFindCliPlan Create(
        string literal,
        IReadOnlyList<string> packages,
        string? packagePrefix,
        bool packagePrefixSpecified,
        int? candidateTake,
        string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);

        PackageAssemblyPatternRequest pattern =
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                literal);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact(targetFramework);
        bool hasPrefix =
            packagePrefixSpecified || packagePrefix is not null;
        if (hasPrefix && packages.Count > 0)
        {
            throw new ArgumentException(
                "--literal accepts either explicit --package ID@VERSION coordinates "
                + "or one --package-prefix, not both.");
        }

        PackageAssemblySemanticFindPopulationPlan population;
        if (hasPrefix)
        {
            if (candidateTake is not
                (>= 1 and <=
                    PackageAcquisitionPopulation.MaximumCandidates))
            {
                throw new ArgumentException(
                    "--literal --package-prefix requires --take between 1 and "
                    + $"{PackageAcquisitionPopulation.MaximumCandidates}.");
            }

            population = new PackageAssemblySemanticFindPopulationPlan.Prefix(
                new PackagePrefixDeclaration(packagePrefix!),
                candidateTake.Value);
        }
        else
        {
            if (candidateTake is not null)
            {
                throw new ArgumentException(
                    "--take is available only with find --literal --package-prefix.");
            }

            population = new PackageAssemblySemanticFindPopulationPlan.Exact(
                ParseExactCoordinates(packages, targetFramework));
        }

        return new(pattern, target, population);
    }

    private static ImmutableArray<PackageSourceCoordinate>
        ParseExactCoordinates(
        IReadOnlyList<string> packages,
        string targetFramework)
    {
        if (packages.Count is < 1
            or > PackageAcquisitionPopulation.MaximumCandidates)
        {
            throw new ArgumentException(
                $"An assembly query requires between 1 and "
                + $"{PackageAcquisitionPopulation.MaximumCandidates} explicit "
                + "ID@VERSION packages.",
                nameof(packages));
        }

        var coordinates =
            ImmutableArray.CreateBuilder<PackageSourceCoordinate>(
                packages.Count);
        var distinct = new HashSet<PackageSourceCoordinate>();
        foreach (string text in packages)
        {
            ArgumentNullException.ThrowIfNull(text);
            int separator = text.IndexOf('@');
            if (separator <= 0 || separator == text.Length - 1)
            {
                throw new ArgumentException(
                    "Each assembly-query package must be an exact ID@VERSION coordinate.",
                    nameof(packages));
            }

            var requested = new PackageCoordinate(
                text[..separator],
                text[(separator + 1)..],
                targetFramework,
                null);
            if (PackageCoordinateResolver.Validate(requested) is { } invalid)
                throw new ArgumentException(invalid.Message, nameof(packages));

            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(
                    requested.PackageId,
                    requested.Version!);
            if (!distinct.Add(coordinate))
            {
                throw new ArgumentException(
                    "An assembly query cannot contain duplicate package coordinates.",
                    nameof(packages));
            }
            coordinates.Add(coordinate);
        }

        return coordinates.MoveToImmutable();
    }
}
