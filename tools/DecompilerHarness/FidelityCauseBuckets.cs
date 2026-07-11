using System.Collections.Immutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Harness-owned grouping policy over producer-owned fidelity observations.
/// Human diagnostic prose is deliberately not part of the bucket contract.
/// </summary>
internal static class FidelityCauseBuckets
{
    internal readonly record struct Census(
        ImmutableArray<DecompilerFidelityCause> Causes,
        string? Failure)
    {
        internal bool Succeeded => Failure is null;
    }

    internal static Census Inspect(
        IrFunction function,
        string subjectKey)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);

        return DecompilerFindings.InspectFidelityCauses(
            function,
            new FindingSubject(subjectKey, subjectKey)) switch
        {
            FindingInspection<DecompilerFidelityCause>.Complete complete =>
                new Census(
                    [.. complete.Findings.Select(static finding => finding.Payload)],
                    null),
            FindingInspection<DecompilerFidelityCause>.Absent absent =>
                new Census([], absent.Detail ?? "Decompiler IR was absent."),
            FindingInspection<DecompilerFidelityCause>.Failed failed =>
                new Census([], failed.Error.Reason),
        };
    }

    internal static string PrimaryBucket(IrFunction function, string subjectKey)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);

        var census = Inspect(function, subjectKey);
        if (!census.Succeeded)
        {
            return
                function.Diagnostics
                    .FirstOrDefault(static diagnostic =>
                        diagnostic.Id is DiagnosticIds.InternalError
                            or DiagnosticIds.ContextUnavailable
                            or DiagnosticIds.EmptyOutput)
                    .Id
                    ?? "inspection-failed";
        }

        return census.Causes.Length > 0
            ? BucketFor(census.Causes[0])
            : "(typed)";
    }

    internal static string BucketFor(DecompilerFidelityCause cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return cause.Code switch
        {
            DiagnosticIds.UnsupportedConstruct or DiagnosticIds.UnsupportedType =>
                cause.Discriminator ?? cause.Code,
            _ => cause.Code,
        };
    }
}
