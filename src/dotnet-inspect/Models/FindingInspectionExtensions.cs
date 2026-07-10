using System.Collections.Immutable;
using ILInspector.Findings;

namespace DotnetInspector.Models;

internal static class FindingInspectionExtensions
{
    public static ImmutableArray<Finding<T>> Findings<T>(
        this FindingInspection<T>? inspection)
        where T : notnull
        => inspection switch
        {
            null => [],
            FindingInspection<T>.Complete complete => complete.Findings,
            FindingInspection<T>.Absent => [],
            FindingInspection<T>.Failed failed => throw new InvalidOperationException(
                $"Finding inspection failed for {failed.Error.Subject.Display}: {failed.Error.Reason}"),
        };

    public static IEnumerable<T> Payloads<T>(
        this FindingInspection<T>? inspection)
        where T : notnull
        => inspection.Findings().Select(static finding => finding.Payload);
}
