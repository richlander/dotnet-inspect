using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

public sealed record CSharpMemberFindingComparison(
    string StableMemberKey,
    MemberAnchor Anchor,
    string Member,
    bool OldMemberPresent,
    bool NewMemberPresent,
    FindingComparison<CSharpCanonicalLine> Comparison);

public sealed record CSharpAssemblyFindingComparisonResult(
    ImmutableArray<CSharpMemberFindingComparison> Comparisons,
    ImmutableArray<CSharpIdentityResolutionFailure> IdentityFailures);

/// <summary>
/// Adapts decompiled C# method bodies onto the domain-free finding substrate. It reuses
/// <see cref="CSharpBodyDiff"/> to render and canonicalize a method into ordered C# line
/// occurrences, runs the shared <see cref="FindingMatcher"/>, and folds the alignment into
/// <see cref="PairFinding{T}"/> transitions. Semantic C# operations remain a separate projection;
/// this producer reports the raw rendered-line census.
/// </summary>
public static class CSharpFindings
{
    /// <summary>The finding descriptor for a single decompiled C# line occurrence.</summary>
    public static readonly FindingDescriptor LineDescriptor = new("csharp.line", "C# line");

    /// <summary>The descriptor for a failure to inspect a decompiled C# line census.</summary>
    public static readonly FindingDescriptor InspectionDescriptor = new(
        "csharp.inspect",
        "C# inspection");

    /// <summary>
    /// Inspects a single decompiled method body and returns an explicit body, absence, or failure
    /// outcome. A body may contain zero rendered lines.
    /// </summary>
    public static FindingInspection<CSharpCanonicalLine> Inspect(
        MetadataSource source,
        MethodDefinitionHandle method,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);

        return CSharpBodyDiff.Canonicalize(source, method) switch
        {
            CSharpCanonicalization.Body body =>
                new FindingInspection<CSharpCanonicalLine>.Complete(
                    ProjectAtoms(body.Lines, subject)),
            CSharpCanonicalization.Absent absent =>
                new FindingInspection<CSharpCanonicalLine>.Absent(absent.Detail),
            CSharpCanonicalization.Failed failed =>
                new FindingInspection<CSharpCanonicalLine>.Failed(
                    CreateInspectionError(subject, failed.Reason)),
        };
    }

    public static FindingComparison<CSharpCanonicalLine> Compare(
        MetadataSource oldSource,
        MethodDefinitionHandle oldMethod,
        MetadataSource newSource,
        MethodDefinitionHandle newMethod,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(subject);

        var oldInspection = Inspect(oldSource, oldMethod, subject);
        var newInspection = Inspect(newSource, newMethod, subject);
        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    public static CSharpAssemblyFindingComparisonResult CompareAssemblies(
        IReadOnlyList<string> oldPaths,
        IReadOnlyList<string> newPaths,
        bool includeNonPublic = false,
        IReadOnlySet<string>? typeFilters = null,
        IReadOnlySet<string>? memberTargetIdentities = null,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldPaths);
        ArgumentNullException.ThrowIfNull(newPaths);

        var oldIndex = CSharpBodyDiff.BuildMethodIndexWithFailures(
            oldPaths,
            includeNonPublic,
            typeFilters,
            "old");
        var newIndex = CSharpBodyDiff.BuildMethodIndexWithFailures(
            newPaths,
            includeNonPublic,
            typeFilters,
            "new");
        var oldMethods = oldIndex.Methods;
        var newMethods = newIndex.Methods;
        var comparisons = ImmutableArray.CreateBuilder<CSharpMemberFindingComparison>();
        using var sources = new CSharpBodyDiff.SourceCache();

        foreach (string key in oldMethods.Keys.Union(newMethods.Keys).Order(StringComparer.Ordinal))
        {
            oldMethods.TryGetValue(key, out var oldMethod);
            newMethods.TryGetValue(key, out var newMethod);
            var representative = newMethod ?? oldMethod!;
            if (memberTargetIdentities is { Count: > 0 }
                && !memberTargetIdentities.Contains(representative.Anchor.StableSelector))
            {
                continue;
            }

            var subject = new FindingSubject(
                representative.Anchor.StableSelector,
                representative.Display);
            var oldInspection = oldMethod is null
                ? new FindingInspection<CSharpCanonicalLine>.Absent("Member is absent.")
                : Inspect(sources.Open(oldMethod.Path), oldMethod.MethodHandle, subject);
            var newInspection = newMethod is null
                ? new FindingInspection<CSharpCanonicalLine>.Absent("Member is absent.")
                : Inspect(sources.Open(newMethod.Path), newMethod.MethodHandle, subject);
            comparisons.Add(new CSharpMemberFindingComparison(
                key,
                representative.Anchor,
                representative.Display,
                oldMethod is not null,
                newMethod is not null,
                CompareInspections(oldInspection, newInspection, acceptanceThreshold)));
        }

        return new CSharpAssemblyFindingComparisonResult(
            comparisons.ToImmutable(),
            [.. oldIndex.Failures, .. newIndex.Failures]);
    }

    internal static FindingComparison<CSharpCanonicalLine> CompareCanonicalized(
        ImmutableArray<CSharpCanonicalLine> oldLines,
        ImmutableArray<CSharpCanonicalLine> newLines,
        FindingSubject subject,
        int acceptanceThreshold = 100)
        => CompareInspections(
            new FindingInspection<CSharpCanonicalLine>.Complete(ProjectAtoms(oldLines, subject)),
            new FindingInspection<CSharpCanonicalLine>.Complete(ProjectAtoms(newLines, subject)),
            acceptanceThreshold);

    static FindingComparison<CSharpCanonicalLine> CompareInspections(
        FindingInspection<CSharpCanonicalLine> oldInspection,
        FindingInspection<CSharpCanonicalLine> newInspection,
        int acceptanceThreshold)
        => FindingComparison.Compare(
            oldInspection,
            newInspection,
            acceptanceThreshold: acceptanceThreshold)
            .TransformPairs(PromoteRawTextChanges);

    static ImmutableArray<PairFinding<CSharpCanonicalLine>> PromoteRawTextChanges(
        ImmutableArray<PairFinding<CSharpCanonicalLine>> pairs)
    {
        var builder = ImmutableArray.CreateBuilder<PairFinding<CSharpCanonicalLine>>(
            pairs.Length);
        foreach (var pair in pairs)
        {
            builder.Add(pair is PairFinding<CSharpCanonicalLine>.Present present
                && !StringComparer.Ordinal.Equals(
                    present.Old.Payload.Text,
                    present.New.Payload.Text)
                    ? new PairFinding<CSharpCanonicalLine>.Changed(
                        present.Old,
                        present.New,
                        present.Difference,
                        "Canonical line text changed.")
                    : pair);
        }

        return builder.MoveToImmutable();
    }

    static InspectionError CreateInspectionError(FindingSubject subject, string reason)
        => new(subject, InspectionDescriptor, reason);

    static ImmutableArray<Finding<CSharpCanonicalLine>> ProjectAtoms(
        ImmutableArray<CSharpCanonicalLine> lines,
        FindingSubject subject)
    {
        var builder = ImmutableArray.CreateBuilder<Finding<CSharpCanonicalLine>>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            builder.Add(new Finding<CSharpCanonicalLine>(
                subject,
                LineDescriptor,
                new FindingKey(lines[i].IdentityKey),
                lines[i],
                Ordinal: i));
        }

        return builder.MoveToImmutable();
    }
}
