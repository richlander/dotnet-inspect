using System.Collections.ObjectModel;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Opaque caller-owned association for one package runtime-population choice.
/// </summary>
public sealed class PackageRuntimePopulationChoiceOperationAssociation
{
    private PackageRuntimePopulationChoiceOperationAssociation()
    {
    }

    public static PackageRuntimePopulationChoiceOperationAssociation Create() =>
        new();

    public override string ToString() =>
        nameof(PackageRuntimePopulationChoiceOperationAssociation);
}

/// <summary>The authority, if any, for one exact runtime identifier.</summary>
public abstract class PackageRuntimePopulationAuthority
{
    private protected PackageRuntimePopulationAuthority()
    {
    }

    /// <summary>Apply the package-local sole-RID and product-default rules.</summary>
    public sealed class None : PackageRuntimePopulationAuthority
    {
    }

    /// <summary>Use one exact RID supplied by a product gesture or API.</summary>
    public sealed class Explicit : PackageRuntimePopulationAuthority
    {
        public Explicit(string runtimeIdentifier)
        {
            if (!PackageCoordinateResolver.IsCanonicalRuntimeIdentifier(
                    runtimeIdentifier))
            {
                throw new ArgumentException(
                    "A Selection RID must use its canonical lowercase spelling.",
                    nameof(runtimeIdentifier));
            }

            RuntimeIdentifier = runtimeIdentifier;
        }

        public string RuntimeIdentifier { get; }
    }

    /// <summary>Reuse one exact owner-issued Package coordinate association.</summary>
    public sealed class RetainedCoordinate :
        PackageRuntimePopulationAuthority
    {
        public RetainedCoordinate(
            PackageRuntimePopulationCoordinateAssociation association)
        {
            ArgumentNullException.ThrowIfNull(association);
            Association = association;
        }

        public PackageRuntimePopulationCoordinateAssociation Association
        { get; }
    }
}

/// <summary>
/// One immutable request to choose a RID-qualified runtime population.
/// </summary>
public sealed class PackageRuntimePopulationChoiceRequest
{
    public PackageRuntimePopulationChoiceRequest(
        PackageRuntimePopulationChoiceOperationAssociation
            operationAssociation,
        PackageSourceCoordinate coordinate,
        PackageAssetSelectionReceipt selectionReceipt,
        PackageRuntimePopulationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(operationAssociation);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(selectionReceipt);
        ArgumentNullException.ThrowIfNull(authority);
        if (selectionReceipt.RequestedRuntimeIdentifier is not null)
        {
            throw new ArgumentException(
                "Runtime-population choice requires policy-neutral lower-owner inventory.",
                nameof(selectionReceipt));
        }

        OperationAssociation = operationAssociation;
        Coordinate = coordinate;
        SelectionReceipt = selectionReceipt;
        Authority = authority;
    }

    public PackageRuntimePopulationChoiceOperationAssociation
        OperationAssociation { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageAssetSelectionReceipt SelectionReceipt { get; }

    public PackageRuntimePopulationAuthority Authority { get; }
}

/// <summary>How one runtime population was selected.</summary>
public enum PackageRuntimePopulationChoiceSource
{
    Explicit,
    RetainedCoordinate,
    SingleEligible,
    ProductDefault,
}

/// <summary>
/// Owner-issued association for one exact Package coordinate and selected RID
/// population.
/// </summary>
public sealed class PackageRuntimePopulationCoordinateAssociation
{
    internal PackageRuntimePopulationCoordinateAssociation(
        PackageRuntimePopulationChoiceRequest request,
        PackageRuntimeAssetPopulation population)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(population);
        Coordinate = request.Coordinate;
        Generation = request.SelectionReceipt.Generation;
        RequestedTargetFramework =
            request.SelectionReceipt.RequestedTargetFramework;
        Population = population;
    }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageContentGenerationIdentity Generation { get; }

    public string? RequestedTargetFramework { get; }

    public string SelectedTargetFramework =>
        Population.TargetFramework;

    public string RuntimeIdentifier =>
        Population.RuntimeIdentifier;

    public PackageRuntimeAssetPopulation Population { get; }

    internal bool Matches(
        PackageRuntimePopulationChoiceRequest request,
        PackageRuntimeAssetPopulation population) =>
        Coordinate == request.Coordinate
        && ReferenceEquals(
            Generation,
            request.SelectionReceipt.Generation)
        && string.Equals(
            RequestedTargetFramework,
            request.SelectionReceipt.RequestedTargetFramework,
            StringComparison.Ordinal)
        && RuntimePopulationMatches(Population, population);

    private static bool RuntimePopulationMatches(
        PackageRuntimeAssetPopulation left,
        PackageRuntimeAssetPopulation right) =>
        left.RuntimeIdentifier.Equals(
            right.RuntimeIdentifier,
            StringComparison.Ordinal)
        && left.TargetFramework.Equals(
            right.TargetFramework,
            StringComparison.Ordinal)
        && left.PopulationPath.Equals(
            right.PopulationPath,
            StringComparison.Ordinal)
        && left.Assets.SequenceEqual(right.Assets);
}

/// <summary>
/// Closed result of choosing one package-local runtime population.
/// </summary>
public abstract class PackageRuntimePopulationChoiceResult
{
    private protected PackageRuntimePopulationChoiceResult(
        PackageRuntimePopulationChoiceRequest request,
        IReadOnlyList<PackageRuntimeAssetPopulation>
            availableRuntimePopulations)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(availableRuntimePopulations);
        Request = request;
        AvailableRuntimePopulations =
            new ReadOnlyCollection<PackageRuntimeAssetPopulation>(
                [.. availableRuntimePopulations]);
        AvailableRuntimeIdentifiers =
            new ReadOnlyCollection<string>(
                [
                    .. AvailableRuntimePopulations.Select(
                        static population =>
                            population.RuntimeIdentifier),
                ]);
    }

    public PackageRuntimePopulationChoiceRequest Request { get; }

    public PackageRuntimePopulationChoiceOperationAssociation
        OperationAssociation =>
        Request.OperationAssociation;

    public PackageSourceCoordinate Coordinate =>
        Request.Coordinate;

    public PackageContentGenerationIdentity Generation =>
        Request.SelectionReceipt.Generation;

    public string? RequestedTargetFramework =>
        Request.SelectionReceipt.RequestedTargetFramework;

    public PackageAssetSelectionReceipt LowerSelectionReceipt =>
        Request.SelectionReceipt;

    /// <summary>
    /// Complete product-order inventory: the default first when present, then
    /// every remaining canonical RID in ordinal order.
    /// </summary>
    public IReadOnlyList<PackageRuntimeAssetPopulation>
        AvailableRuntimePopulations { get; }

    public IReadOnlyList<string> AvailableRuntimeIdentifiers { get; }

    public sealed class Selected :
        PackageRuntimePopulationChoiceResult
    {
        internal Selected(
            PackageRuntimePopulationChoiceRequest request,
            IReadOnlyList<PackageRuntimeAssetPopulation>
                availableRuntimePopulations,
            PackageRuntimeAssetPopulation population,
            PackageRuntimePopulationChoiceSource choiceSource)
            : base(request, availableRuntimePopulations)
        {
            ArgumentNullException.ThrowIfNull(population);
            if (!Enum.IsDefined(choiceSource))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(choiceSource));
            }

            Population = population;
            ChoiceSource = choiceSource;
            CoordinateAssociation =
                new PackageRuntimePopulationCoordinateAssociation(
                    request,
                    population);
        }

        public PackageRuntimeAssetPopulation Population { get; }

        public string RuntimeIdentifier =>
            Population.RuntimeIdentifier;

        public string SelectedTargetFramework =>
            Population.TargetFramework;

        public PackageRuntimePopulationChoiceSource ChoiceSource { get; }

        public PackageRuntimePopulationCoordinateAssociation
            CoordinateAssociation { get; }
    }

    public sealed class NoMatch :
        PackageRuntimePopulationChoiceResult
    {
        internal NoMatch(
            PackageRuntimePopulationChoiceRequest request,
            IReadOnlyList<PackageRuntimeAssetPopulation>
                availableRuntimePopulations)
            : base(request, availableRuntimePopulations)
        {
        }
    }

    public sealed class Ambiguous :
        PackageRuntimePopulationChoiceResult
    {
        internal Ambiguous(
            PackageRuntimePopulationChoiceRequest request,
            IReadOnlyList<PackageRuntimeAssetPopulation>
                availableRuntimePopulations)
            : base(request, availableRuntimePopulations)
        {
        }
    }

    public sealed class Refused :
        PackageRuntimePopulationChoiceResult
    {
        internal Refused(
            PackageRuntimePopulationChoiceRequest request,
            IReadOnlyList<PackageRuntimeAssetPopulation>
                availableRuntimePopulations)
            : base(request, availableRuntimePopulations)
        {
        }
    }

    public sealed class Failed :
        PackageRuntimePopulationChoiceResult
    {
        internal Failed(
            PackageRuntimePopulationChoiceRequest request,
            IReadOnlyList<PackageRuntimeAssetPopulation>
                availableRuntimePopulations)
            : base(request, availableRuntimePopulations)
        {
        }

        public PackageAssetSelection LowerSelection =>
            LowerSelectionReceipt.Selection;
    }
}

/// <summary>Chooses one package-local Selection RID from owner-issued evidence.</summary>
public static class PackageRuntimePopulationChoice
{
    public static string ProductDefaultRuntimeIdentifier { get; } =
        "linux-x64";

    public static PackageRuntimePopulationChoiceResult Evaluate(
        PackageRuntimePopulationChoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackageAssetSelectionReceipt receipt =
            request.SelectionReceipt;
        PackageRuntimeAssetPopulation[] available =
            OrderForProduct(receipt.AvailableRuntimePopulations);

        if (!HasUsableLowerEvidence(receipt, available))
        {
            return new PackageRuntimePopulationChoiceResult.Failed(
                request,
                available);
        }

        return request.Authority switch
        {
            PackageRuntimePopulationAuthority.Explicit explicitAuthority =>
                SelectExact(
                    request,
                    available,
                    explicitAuthority.RuntimeIdentifier,
                    PackageRuntimePopulationChoiceSource.Explicit),
            PackageRuntimePopulationAuthority.RetainedCoordinate
                retainedAuthority =>
                SelectRetained(
                    request,
                    available,
                    retainedAuthority.Association),
            PackageRuntimePopulationAuthority.None =>
                SelectDefault(request, available),
            _ => throw new InvalidOperationException(
                "Unknown runtime-population authority."),
        };
    }

    private static PackageRuntimePopulationChoiceResult SelectDefault(
        PackageRuntimePopulationChoiceRequest request,
        IReadOnlyList<PackageRuntimeAssetPopulation> available)
    {
        if (available.Count == 0)
        {
            return new PackageRuntimePopulationChoiceResult.NoMatch(
                request,
                available);
        }
        if (available.Count == 1)
        {
            return Selected(
                request,
                available,
                available[0],
                PackageRuntimePopulationChoiceSource.SingleEligible);
        }

        PackageRuntimeAssetPopulation? productDefault =
            available.FirstOrDefault(
                population =>
                    population.RuntimeIdentifier.Equals(
                        ProductDefaultRuntimeIdentifier,
                        StringComparison.Ordinal));
        return productDefault is null
            ? new PackageRuntimePopulationChoiceResult.Ambiguous(
                request,
                available)
            : Selected(
                request,
                available,
                productDefault,
                PackageRuntimePopulationChoiceSource.ProductDefault);
    }

    private static PackageRuntimePopulationChoiceResult SelectExact(
        PackageRuntimePopulationChoiceRequest request,
        IReadOnlyList<PackageRuntimeAssetPopulation> available,
        string runtimeIdentifier,
        PackageRuntimePopulationChoiceSource choiceSource)
    {
        PackageRuntimeAssetPopulation? population =
            available.FirstOrDefault(
                candidate =>
                    candidate.RuntimeIdentifier.Equals(
                        runtimeIdentifier,
                        StringComparison.Ordinal));
        return population is null
            ? new PackageRuntimePopulationChoiceResult.NoMatch(
                request,
                available)
            : Selected(
                request,
                available,
                population,
                choiceSource);
    }

    private static PackageRuntimePopulationChoiceResult SelectRetained(
        PackageRuntimePopulationChoiceRequest request,
        IReadOnlyList<PackageRuntimeAssetPopulation> available,
        PackageRuntimePopulationCoordinateAssociation association)
    {
        PackageRuntimeAssetPopulation? population =
            available.FirstOrDefault(
                candidate =>
                    association.Matches(request, candidate));
        return population is null
            ? new PackageRuntimePopulationChoiceResult.Refused(
                request,
                available)
            : Selected(
                request,
                available,
                population,
                PackageRuntimePopulationChoiceSource.RetainedCoordinate);
    }

    private static PackageRuntimePopulationChoiceResult.Selected Selected(
        PackageRuntimePopulationChoiceRequest request,
        IReadOnlyList<PackageRuntimeAssetPopulation> available,
        PackageRuntimeAssetPopulation population,
        PackageRuntimePopulationChoiceSource choiceSource) =>
        new(
            request,
            available,
            population,
            choiceSource);

    private static bool HasUsableLowerEvidence(
        PackageAssetSelectionReceipt receipt,
        IReadOnlyList<PackageRuntimeAssetPopulation> available) =>
        receipt.Selection switch
        {
            PackageAssetSelection.Invalid => false,
            PackageAssetSelection.NoMatch =>
                available.Count == 0,
            PackageAssetSelection.Selected selected =>
                available.Any(population =>
                    population.RuntimeIdentifier.Equals(
                        selected.Universe.RuntimeIdentifier,
                        StringComparison.Ordinal)
                    && population.TargetFramework.Equals(
                        selected.Universe.TargetFramework,
                        StringComparison.Ordinal)
                    && population.Assets.SequenceEqual(
                        selected.Universe.Assets)),
            PackageAssetSelection.Ambiguous =>
                available.Count > 0,
            _ => false,
        };

    private static PackageRuntimeAssetPopulation[] OrderForProduct(
        IEnumerable<PackageRuntimeAssetPopulation> populations) =>
        [
            .. populations
                .OrderBy(
                    population =>
                        population.RuntimeIdentifier.Equals(
                            ProductDefaultRuntimeIdentifier,
                            StringComparison.Ordinal)
                            ? 0
                            : 1)
                .ThenBy(
                    static population =>
                        population.RuntimeIdentifier,
                    StringComparer.Ordinal),
        ];
}
