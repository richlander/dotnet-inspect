using System.Globalization;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Models;

internal readonly record struct IdentifierConfusionCase(
    string Location,
    string Kind,
    IdentifierConfusion Confusion);

internal enum IdentifierConfusionAuditFailureKind
{
    InvalidAssemblyMetadata,
    AssemblyUnreadable,
    InspectionFailed,
}

internal static class IdentifierConfusionAudit
{
    public static string DescribeFailure(
        IdentifierConfusionAuditFailureKind failure) =>
        failure switch
        {
            IdentifierConfusionAuditFailureKind.InvalidAssemblyMetadata =>
                "invalid assembly metadata",
            IdentifierConfusionAuditFailureKind.AssemblyUnreadable =>
                "assembly could not be read",
            IdentifierConfusionAuditFailureKind.InspectionFailed =>
                "assembly inspection failed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                null),
        };

    public static IReadOnlyList<IdentifierConfusionCase> InspectPackage(InspectionResult model)
    {
        List<IdentifierConfusionCase> cases = [];
        Add(cases, nameof(model.PackageName), "Package ID", model.PackageName);

        if (model.Deprecation?.AlternatePackageId is { } alternatePackageId)
        {
            Add(
                cases,
                $"{nameof(model.Deprecation)}.{nameof(model.Deprecation.AlternatePackageId)}",
                "Package ID",
                alternatePackageId);
        }

        if (model.DependencyGroups is { } groups)
        {
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                for (int dependencyIndex = 0;
                    dependencyIndex < groups[groupIndex].Dependencies.Count;
                    dependencyIndex++)
                {
                    PackageDependency dependency = groups[groupIndex].Dependencies[dependencyIndex];
                    Add(
                        cases,
                        $"{nameof(model.DependencyGroups)}[{groupIndex}]."
                            + $"{nameof(DependencyGroup.Dependencies)}[{dependencyIndex}]."
                            + nameof(dependency.Id),
                        "Package ID",
                        dependency.Id);
                }
            }
        }

        if (model.RuntimeDependencies is { } runtimeDependencies)
        {
            for (int index = 0; index < runtimeDependencies.Count; index++)
            {
                Add(
                    cases,
                    $"{nameof(model.RuntimeDependencies)}[{index}].{nameof(PackageDependency.Id)}",
                    "Package ID",
                    runtimeDependencies[index].Id);
            }
        }

        if (model.RuntimeIdentifierPackages is { } runtimePackages)
        {
            for (int index = 0; index < runtimePackages.Count; index++)
            {
                Add(
                    cases,
                    $"{nameof(model.RuntimeIdentifierPackages)}[{index}]."
                        + nameof(RidPackageReference.PackageId),
                    "Package ID",
                    runtimePackages[index].PackageId);
            }
        }

        return cases;
    }

    public static IReadOnlyList<IdentifierConfusionCase> InspectLibrarySummary(
        LibraryInspection model)
        => InspectLibrary(model, includeTransitiveReferences: false);

    public static IReadOnlyList<IdentifierConfusionCase> InspectLibrary(
        LibraryInspection model)
        => InspectLibrary(model, includeTransitiveReferences: true);

    private static IReadOnlyList<IdentifierConfusionCase> InspectLibrary(
        LibraryInspection model,
        bool includeTransitiveReferences)
    {
        List<IdentifierConfusionCase> cases = [];
        if (model.AssemblyInfo is not { } assembly)
            return cases;

        Add(
            cases,
            $"{nameof(model.AssemblyInfo)}.{nameof(assembly.AssemblyName)}",
            "Assembly name",
            assembly.AssemblyName);

        if (assembly.References is { } references)
        {
            for (int index = 0; index < references.Count; index++)
            {
                Add(
                    cases,
                    $"{nameof(model.AssemblyInfo)}.{nameof(assembly.References)}[{index}].Name",
                    "Assembly name",
                    references[index].Name);
            }
        }

        HashSet<string>? directReferenceNames = includeTransitiveReferences
            && assembly.References is { } directReferences
                ? new HashSet<string>(
                    directReferences.Select(reference => reference.Name),
                    StringComparer.OrdinalIgnoreCase)
                : null;
        if (includeTransitiveReferences
            && model.IdentifierConfusionReferenceClosure is { } transitiveReferences)
        {
            for (int index = 0; index < transitiveReferences.Count; index++)
            {
                if (directReferenceNames?.Contains(transitiveReferences[index].Name) == true)
                    continue;

                Add(
                    cases,
                    $"{nameof(model.IdentifierConfusionReferenceClosure)}[{index}]."
                        + nameof(AssemblyReferenceNode.Name),
                    "Assembly name",
                    transitiveReferences[index].Name);
            }
        }

        return cases;
    }

    public static (string Value, string Evidence) Summarize(
        IReadOnlyList<IdentifierConfusionCase> cases,
        string scope)
    {
        if (cases.Count == 0)
            return ("None", $"all inspected {scope} use ASCII characters");

        int homoglyphs = cases.Count(value =>
            (value.Confusion.Concerns & IdentifierConcern.ReservedPrefixHomoglyph) != 0);
        string evidence = $"{cases.Count.ToString(CultureInfo.InvariantCulture)} non-ASCII "
            + $"{(cases.Count == 1 ? "identifier" : "identifiers")}";
        if (homoglyphs > 0)
        {
            string prefixes = string.Join(
                ", ",
                cases
                    .Select(value => value.Confusion.ReservedPrefixMatch?.ReservedPrefix)
                    .Where(static value => value is not null)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal));
            evidence += $"; {homoglyphs.ToString(CultureInfo.InvariantCulture)} reserved-prefix "
                + $"{(homoglyphs == 1 ? "homoglyph" : "homoglyphs")}";
            if (prefixes.Length > 0)
                evidence += $" ({prefixes})";
        }

        return ("Detected", evidence);
    }

    public static string DescribeConcern(IdentifierConfusion confusion)
        => confusion.ReservedPrefixMatch is null
            ? "non-ASCII characters"
            : "non-ASCII characters; reserved-prefix homoglyph";

    public static string? DescribeSimilarity(IdentifierConfusion confusion)
        => confusion.ReservedPrefixMatch is { } match
            ? (match.Similarity * 100).ToString("F0", CultureInfo.InvariantCulture) + "%"
            : null;

    public static string DescribeCharacters(IdentifierConfusion confusion)
    {
        Dictionary<int, char> mappings = confusion.ReservedPrefixMatch?.Homoglyphs
            .ToDictionary(static value => value.CodePoint, static value => value.LooksLike)
            ?? [];
        return string.Join(
            ", ",
            confusion.NonAsciiCodePoints.Select(codePoint =>
                mappings.TryGetValue(codePoint, out char looksLike)
                    ? $"U+{codePoint:X4}→{char.ToUpperInvariant(looksLike)}"
                    : $"U+{codePoint:X4}"));
    }

    private static void Add(
        List<IdentifierConfusionCase> cases,
        string location,
        string kind,
        string? value)
    {
        if (IdentifierConfusionDetector.Inspect(value) is { } confusion)
            cases.Add(new IdentifierConfusionCase(location, kind, confusion));
    }
}
