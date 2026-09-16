using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// The document-local identity of one dependency-inspection root occurrence.
/// </summary>
public readonly record struct DependencyRootOccurrenceIdentity
{
    [JsonConstructor]
    public DependencyRootOccurrenceIdentity(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    public int Value { get; }
}

/// <summary>
/// Complete package-input evidence and its dependency-document associations.
/// </summary>
public sealed record DependencyInspectionEvidenceDocument
{
    [JsonConstructor]
    public DependencyInspectionEvidenceDocument(
        PackageDependencyEvidenceOutcome packageInputs,
        ImmutableArray<DependencyRootOccurrenceIdentity>
            admittedRootOccurrences,
        ImmutableArray<DependencyRootOccurrenceIdentity?>
            failedRootOccurrences)
    {
        ArgumentNullException.ThrowIfNull(packageInputs);
        admittedRootOccurrences = admittedRootOccurrences.IsDefault
            ? []
            : admittedRootOccurrences;
        failedRootOccurrences = failedRootOccurrences.IsDefault
            ? []
            : failedRootOccurrences;

        if (admittedRootOccurrences.Length != packageInputs.Roots.Length)
        {
            throw new ArgumentException(
                "Each admitted package input requires one root occurrence.",
                nameof(admittedRootOccurrences));
        }
        if (failedRootOccurrences.Length != packageInputs.FailedRoots.Length)
        {
            throw new ArgumentException(
                "Each failed package input requires one association entry.",
                nameof(failedRootOccurrences));
        }

        for (int index = 0; index < failedRootOccurrences.Length; index++)
        {
            if (failedRootOccurrences[index] is null
                && packageInputs.FailedRoots[index]
                    is not PackageDependencyEvidenceRootFailure.PackageProfile)
            {
                throw new ArgumentException(
                    "Only package-prefix producer failures may omit a root occurrence.",
                    nameof(failedRootOccurrences));
            }
        }

        PackageInputs = packageInputs;
        AdmittedRootOccurrences = admittedRootOccurrences;
        FailedRootOccurrences = failedRootOccurrences;
    }

    public PackageDependencyEvidenceOutcome PackageInputs { get; }

    public ImmutableArray<DependencyRootOccurrenceIdentity>
        AdmittedRootOccurrences { get; }

    public ImmutableArray<DependencyRootOccurrenceIdentity?>
        FailedRootOccurrences { get; }
}
