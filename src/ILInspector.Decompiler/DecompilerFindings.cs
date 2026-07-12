using System.Collections.Immutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Decompiler;

/// <summary>Decompiler-owned observations and comparisons over the Finding substrate.</summary>
public static class DecompilerFindings
{
    public static readonly FindingDescriptor FidelityCauseDescriptor =
        new("decompiler.fidelity-cause", "Decompiler fidelity cause");

    public static readonly FindingDescriptor FidelityInspectionDescriptor =
        new("decompiler.fidelity.inspect", "Decompiler fidelity inspection");

    /// <summary>
    /// Inspects one final decompiler IR tree. A null tree means no method body was
    /// available; an importer/pipeline failure remains an operation failure rather
    /// than being projected as a fidelity-cause observation.
    /// </summary>
    public static FindingInspection<DecompilerFidelityCause> InspectFidelityCauses(
        IrFunction? function,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (function is null)
        {
            return new FindingInspection<DecompilerFidelityCause>.Absent(
                "Method has no decompiler IR body.");
        }

        foreach (var diagnostic in function.Diagnostics)
        {
            if (!IsOperationFailure(diagnostic.Id))
                continue;

            return new FindingInspection<DecompilerFidelityCause>.Failed(
                new InspectionError(
                    subject,
                    FidelityInspectionDescriptor,
                    $"{diagnostic.Id}: {diagnostic.Message}"));
        }

        var causes = FidelityRemarks.CollectCauses(function);
        var findings = ImmutableArray.CreateBuilder<Finding<DecompilerFidelityCause>>(causes.Count);
        for (int i = 0; i < causes.Count; i++)
        {
            var cause = causes[i];
            findings.Add(new Finding<DecompilerFidelityCause>(
                subject,
                FidelityCauseDescriptor,
                new FindingKey(GetFidelityCauseIdentityKey(cause)),
                cause,
                Ordinal: i,
                Detail: cause.Reason));
        }

        return new FindingInspection<DecompilerFidelityCause>.Complete(
            findings.MoveToImmutable());
    }

    public static FindingComparison<DecompilerFidelityCause> CompareFidelityCauses(
        IrFunction? oldFunction,
        IrFunction? newFunction,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare(
                InspectFidelityCauses(oldFunction, subject),
                InspectFidelityCauses(newFunction, subject),
                acceptanceThreshold: acceptanceThreshold)
            .TransformPairs(ClassifyFacetChanges);
    }

    public static string GetFidelityCauseIdentityKey(DecompilerFidelityCause cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        // Fidelity cause matching is an ordered stream keyed by the stable DEC#### cause code.
        // Location kind/offset/slot are version-local coordinates, not correspondence identity.
        return cause.Code;
    }

    static bool IsOperationFailure(string id)
        => id is DiagnosticIds.InternalError
            or DiagnosticIds.ContextUnavailable
            or DiagnosticIds.EmptyOutput;

    static ImmutableArray<PairFinding<DecompilerFidelityCause>> ClassifyFacetChanges(
        ImmutableArray<PairFinding<DecompilerFidelityCause>> pairs)
    {
        var builder = ImmutableArray.CreateBuilder<PairFinding<DecompilerFidelityCause>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<DecompilerFidelityCause>.Present present
                && !SameSemanticFacets(present.Old.Payload, present.New.Payload))
            {
                builder.Add(new PairFinding<DecompilerFidelityCause>.Changed(
                    present.Old,
                    present.New,
                    present.Difference,
                    DescribeFacetChanges(present.Old.Payload, present.New.Payload)));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.MoveToImmutable();
    }

    static bool SameSemanticFacets(
        DecompilerFidelityCause oldCause,
        DecompilerFidelityCause newCause)
        // Deliberate allow list: only stable producer-owned classification facets can turn a
        // matched cause into Changed. Human prose, CLR implementation type names, rendered node
        // text, and location coordinates are diagnostic/display details and may drift without
        // changing the logical fidelity cause.
        => oldCause.Code == newCause.Code
            && oldCause.Discriminator == newCause.Discriminator;

    static string DescribeFacetChanges(
        DecompilerFidelityCause oldCause,
        DecompilerFidelityCause newCause)
    {
        var changes = new List<string>();
        AddChange(changes, "code", oldCause.Code, newCause.Code);
        AddChange(changes, "discriminator", oldCause.Discriminator, newCause.Discriminator);
        return changes.Count == 0 ? "other fidelity-cause facets changed" : string.Join("; ", changes);
    }

    static void AddChange<T>(List<string> changes, string name, T oldValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
            return;

        changes.Add($"{name}: {oldValue?.ToString() ?? "none"} -> {newValue?.ToString() ?? "none"}");
    }
}
