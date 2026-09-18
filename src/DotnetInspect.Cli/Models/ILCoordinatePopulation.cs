namespace DotnetInspect.Cli.Models;

internal sealed record ILCoordinatePopulation(
    IReadOnlyList<ILCoordinatePopulationRecord> Records);

internal abstract record ILCoordinatePopulationRecord(int LineNumber)
{
    internal sealed record Coordinate(
        int LineNumber,
        string Value,
        string? Label)
        : ILCoordinatePopulationRecord(LineNumber);

    internal sealed record Malformed(
        int LineNumber,
        string Label,
        string Error)
        : ILCoordinatePopulationRecord(LineNumber);
}

internal enum ILCoordinatePopulationFailureKind
{
    FileNotFound,
    FileReadFailed,
    NoCoordinates,
    CoordinatePopulationLimitExceeded,
}

internal sealed record ILCoordinatePopulationFailure(
    ILCoordinatePopulationFailureKind Kind,
    string Path,
    string? Detail = null,
    int? Limit = null,
    int? ObservedLineNumber = null);

internal sealed class ILCoordinatePopulationOutcome
{
    private ILCoordinatePopulationOutcome(
        ILCoordinatePopulation? population,
        ILCoordinatePopulationFailure? failure)
    {
        Population = population;
        Failure = failure;
    }

    internal ILCoordinatePopulation? Population { get; }

    internal ILCoordinatePopulationFailure? Failure { get; }

    internal bool Succeeded => Population is not null;

    internal static ILCoordinatePopulationOutcome Success(
        ILCoordinatePopulation population) =>
        new(population, null);

    internal static ILCoordinatePopulationOutcome Failed(
        ILCoordinatePopulationFailure failure) =>
        new(null, failure);
}
