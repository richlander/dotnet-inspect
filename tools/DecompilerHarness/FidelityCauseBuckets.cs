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
    internal enum CensusState
    {
        Complete,
        Absent,
        Failed,
    }

    internal readonly record struct Census(
        ImmutableArray<DecompilerFidelityCause> Causes,
        CensusState State,
        string? Detail)
    {
        internal bool Succeeded => State == CensusState.Complete;

        internal string? ErrorCode => State switch
        {
            CensusState.Absent => "fidelity-inspection-absent",
            CensusState.Failed => "fidelity-inspection-failed",
            _ => null,
        };
    }

    internal static Census Inspect(
        IrFunction function,
        string subjectKey)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);

        return FromInspection(DecompilerFindings.InspectFidelityCauses(
            function,
            new FindingSubject(subjectKey, subjectKey)));
    }

    internal static Census FromInspection(
        FindingInspection<DecompilerFidelityCause> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        return inspection switch
        {
            FindingInspection<DecompilerFidelityCause>.Complete complete =>
                new Census(
                    [.. complete.Findings.Select(static finding => finding.Payload)],
                    CensusState.Complete,
                    null),
            FindingInspection<DecompilerFidelityCause>.Absent absent =>
                new Census(
                    [],
                    CensusState.Absent,
                    absent.Detail ?? "Decompiler IR was absent."),
            FindingInspection<DecompilerFidelityCause>.Failed failed =>
                new Census([], CensusState.Failed, failed.Error.Reason),
        };
    }

    internal static string PrimaryBucket(Census census)
    {
        if (!census.Succeeded)
            throw new InvalidOperationException("A failed or absent inspection has no fidelity-cause bucket.");
        if (census.Causes.IsEmpty)
            throw new InvalidOperationException("A complete inspection with no causes has no fidelity-cause bucket.");

        return BucketFor(census.Causes[0]);
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
