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

    public static bool HasFindings<T>(
        this FindingInspection<T>? inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete { Findings.Length: > 0 };

    public static int FindingCount<T>(
        this FindingInspection<T>? inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings.Length
            : 0;

    public static IEnumerable<T> PayloadsForRendering<T>(
        this FindingInspection<T>? inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings.Select(static finding => finding.Payload)
            : [];

    public static InspectionError? Failure<T>(
        this FindingInspection<T>? inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Failed failed ? failed.Error : null;

    public static bool CanRenderWithPresence<T>(
        this FindingInspection<T>? inspection,
        bool hasPresence)
        where T : notnull
        => inspection is not FindingInspection<T>.Failed
           && (inspection.HasFindings() || hasPresence);
}
