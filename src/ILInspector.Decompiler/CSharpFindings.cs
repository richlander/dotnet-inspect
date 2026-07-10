using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Decompiler;

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
                    InspectionError(subject, failed.Reason)),
        };
    }

    public static CSharpFindingsResult Compare(
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
        if (oldInspection is FindingInspection<CSharpCanonicalLine>.Failed
            || newInspection is FindingInspection<CSharpCanonicalLine>.Failed)
            return CSharpFindingsResult.Failed(oldInspection, newInspection);

        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    internal static CSharpFindingsResult CompareCanonicalized(
        ImmutableArray<CSharpCanonicalLine> oldLines,
        ImmutableArray<CSharpCanonicalLine> newLines,
        FindingSubject subject,
        int acceptanceThreshold = 100)
        => CompareInspections(
            new FindingInspection<CSharpCanonicalLine>.Complete(ProjectAtoms(oldLines, subject)),
            new FindingInspection<CSharpCanonicalLine>.Complete(ProjectAtoms(newLines, subject)),
            acceptanceThreshold);

    static CSharpFindingsResult CompareInspections(
        FindingInspection<CSharpCanonicalLine> oldInspection,
        FindingInspection<CSharpCanonicalLine> newInspection,
        int acceptanceThreshold)
    {
        var oldAtoms = InspectionAtoms(oldInspection);
        var newAtoms = InspectionAtoms(newInspection);

        var match = FindingMatcher.Match(oldAtoms.Keys(), newAtoms.Keys());
        var pairs = FindingFold.ToPairs(match, oldAtoms, newAtoms, acceptanceThreshold);
        return new CSharpFindingsResult(
            pairs,
            match,
            oldInspection,
            newInspection);
    }

    internal static ImmutableArray<Finding<CSharpCanonicalLine>> InspectionAtoms(
        FindingInspection<CSharpCanonicalLine> inspection)
        => inspection switch
        {
            FindingInspection<CSharpCanonicalLine>.Complete complete => complete.Findings,
            FindingInspection<CSharpCanonicalLine>.Absent => [],
            FindingInspection<CSharpCanonicalLine>.Failed => [],
        };

    static CensusError InspectionError(FindingSubject subject, string reason)
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
                i,
                lines[i]));
        }

        return builder.MoveToImmutable();
    }
}

/// <summary>The outcome of a <see cref="CSharpFindings.Compare"/> call.</summary>
public sealed record CSharpFindingsResult(
    ImmutableArray<PairFinding<CSharpCanonicalLine>> Pairs,
    FindingMatch Match,
    FindingInspection<CSharpCanonicalLine> OldInspection,
    FindingInspection<CSharpCanonicalLine> NewInspection)
{
    public ImmutableArray<PairFinding<CSharpCanonicalLine>> Pairs { get; }
        = Pairs.IsDefault
            ? throw new ArgumentException("Pairs must be initialized.", nameof(Pairs))
            : Pairs;

    public FindingMatch Match { get; }
        = Match ?? throw new ArgumentNullException(nameof(Match));

    public FindingInspection<CSharpCanonicalLine> OldInspection { get; }
        = OldInspection ?? throw new ArgumentNullException(nameof(OldInspection));

    public FindingInspection<CSharpCanonicalLine> NewInspection { get; }
        = NewInspection ?? throw new ArgumentNullException(nameof(NewInspection));

    public ImmutableArray<Finding<CSharpCanonicalLine>> OldAtoms
        => CSharpFindings.InspectionAtoms(OldInspection);

    public ImmutableArray<Finding<CSharpCanonicalLine>> NewAtoms
        => CSharpFindings.InspectionAtoms(NewInspection);

    public string? Failure
        => (OldInspection, NewInspection) switch
        {
            (FindingInspection<CSharpCanonicalLine>.Failed oldFailed,
                FindingInspection<CSharpCanonicalLine>.Failed newFailed) =>
                $"old: {oldFailed.Error.Reason}; new: {newFailed.Error.Reason}",
            (FindingInspection<CSharpCanonicalLine>.Failed oldFailed, _) =>
                $"old: {oldFailed.Error.Reason}",
            (_, FindingInspection<CSharpCanonicalLine>.Failed newFailed) =>
                $"new: {newFailed.Error.Reason}",
            _ => null,
        };

    /// <summary>
    /// True when both methods have the same body-presence state and their lines are exact under the
    /// fidelity fold (no adds/removes/moves).
    /// </summary>
    public bool IsExact
        => Failure is null
           && SameBodyState(OldInspection, NewInspection)
           && FindingEquivalence.Exact.IsEquivalent(Pairs);

    internal static CSharpFindingsResult Failed(
        FindingInspection<CSharpCanonicalLine> oldInspection,
        FindingInspection<CSharpCanonicalLine> newInspection)
    {
        if (oldInspection is not FindingInspection<CSharpCanonicalLine>.Failed
            && newInspection is not FindingInspection<CSharpCanonicalLine>.Failed)
        {
            throw new ArgumentException("At least one inspection must have failed.");
        }

        return new(
            [],
            new FindingMatch([], []),
            oldInspection,
            newInspection);
    }

    static bool SameBodyState(
        FindingInspection<CSharpCanonicalLine> oldInspection,
        FindingInspection<CSharpCanonicalLine> newInspection)
        => (oldInspection, newInspection) switch
        {
            (FindingInspection<CSharpCanonicalLine>.Complete,
                FindingInspection<CSharpCanonicalLine>.Complete) => true,
            (FindingInspection<CSharpCanonicalLine>.Absent,
                FindingInspection<CSharpCanonicalLine>.Absent) => true,
            _ => false,
        };

}
