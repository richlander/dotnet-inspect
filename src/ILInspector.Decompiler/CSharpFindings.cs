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

    /// <summary>
    /// Inspects a single decompiled method body and returns an explicit body, absence, or failure
    /// outcome. A body may contain zero rendered lines.
    /// </summary>
    public static CSharpFindingsInspection Inspect(
        MetadataSource source,
        MethodDefinitionHandle method,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);

        bool succeeded = CSharpBodyDiff.TryCanonicalize(
            source, method, out var lines, out var state, out var failure);
        if (!succeeded)
            return new CSharpFindingsInspection.Failed(failure ?? "C# body inspection failed");

        return state switch
        {
            CSharpBodyState.Body => new CSharpFindingsInspection.Body(ProjectAtoms(lines, subject)),
            CSharpBodyState.Absent => new CSharpFindingsInspection.Absent(),
            _ => new CSharpFindingsInspection.Failed(failure ?? "C# body inspection failed"),
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
        if (oldInspection is CSharpFindingsInspection.Failed
            || newInspection is CSharpFindingsInspection.Failed)
        {
            var failures = new List<string>(2);
            if (oldInspection is CSharpFindingsInspection.Failed oldFailed)
                failures.Add($"old: {oldFailed.Reason}");
            if (newInspection is CSharpFindingsInspection.Failed newFailed)
                failures.Add($"new: {newFailed.Reason}");
            return CSharpFindingsResult.Failed(
                oldInspection,
                newInspection,
                string.Join("; ", failures));
        }

        return CompareInspections(oldInspection, newInspection, acceptanceThreshold);
    }

    internal static CSharpFindingsResult CompareCanonicalized(
        ImmutableArray<CSharpCanonicalLine> oldLines,
        ImmutableArray<CSharpCanonicalLine> newLines,
        FindingSubject subject,
        int acceptanceThreshold = 100)
        => CompareInspections(
            new CSharpFindingsInspection.Body(ProjectAtoms(oldLines, subject)),
            new CSharpFindingsInspection.Body(ProjectAtoms(newLines, subject)),
            acceptanceThreshold);

    static CSharpFindingsResult CompareInspections(
        CSharpFindingsInspection oldInspection,
        CSharpFindingsInspection newInspection,
        int acceptanceThreshold)
    {
        var oldAtoms = CSharpFindingsInspection.Atoms(oldInspection);
        var newAtoms = CSharpFindingsInspection.Atoms(newInspection);

        FindingMatch match;
        try
        {
            match = FindingMatcher.Match(oldAtoms.Keys(), newAtoms.Keys());
        }
        catch (ArgumentException ex)
        {
            return CSharpFindingsResult.Failed(oldInspection, newInspection, ex.Message);
        }

        var pairs = FindingFold.ToPairs(match, oldAtoms, newAtoms, acceptanceThreshold);
        return new CSharpFindingsResult(
            pairs,
            match,
            oldInspection,
            newInspection,
            Failure: null);
    }

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

/// <summary>
/// The result of inspecting one method's rendered C# line census. Absence is a valid method state
/// for abstract and extern methods; failure means the body could not be inspected.
/// </summary>
public abstract record CSharpFindingsInspection
{
    private CSharpFindingsInspection()
    {
    }

    public sealed record Body(
        ImmutableArray<Finding<CSharpCanonicalLine>> Findings) : CSharpFindingsInspection
    {
        public ImmutableArray<Finding<CSharpCanonicalLine>> Findings { get; }
            = Findings.IsDefault
                ? throw new ArgumentException("Findings must be initialized.", nameof(Findings))
                : Findings;
    }

    public sealed record Absent : CSharpFindingsInspection;

    public sealed record Failed(string Reason) : CSharpFindingsInspection
    {
        public string Reason { get; } = Reason ?? throw new ArgumentNullException(nameof(Reason));
    }

    internal static ImmutableArray<Finding<CSharpCanonicalLine>> Atoms(
        CSharpFindingsInspection inspection)
        => inspection switch
        {
            Body body => body.Findings,
            Absent => [],
            Failed => [],
            _ => throw new ArgumentOutOfRangeException(nameof(inspection)),
        };
}

/// <summary>The outcome of a <see cref="CSharpFindings.Compare"/> call.</summary>
public sealed record CSharpFindingsResult(
    ImmutableArray<PairFinding<CSharpCanonicalLine>> Pairs,
    FindingMatch Match,
    CSharpFindingsInspection OldInspection,
    CSharpFindingsInspection NewInspection,
    string? Failure)
{
    public ImmutableArray<PairFinding<CSharpCanonicalLine>> Pairs { get; }
        = Pairs.IsDefault
            ? throw new ArgumentException("Pairs must be initialized.", nameof(Pairs))
            : Pairs;

    public FindingMatch Match { get; }
        = Match ?? throw new ArgumentNullException(nameof(Match));

    public CSharpFindingsInspection OldInspection { get; }
        = OldInspection ?? throw new ArgumentNullException(nameof(OldInspection));

    public CSharpFindingsInspection NewInspection { get; }
        = NewInspection ?? throw new ArgumentNullException(nameof(NewInspection));

    public ImmutableArray<Finding<CSharpCanonicalLine>> OldAtoms
        => CSharpFindingsInspection.Atoms(OldInspection);

    public ImmutableArray<Finding<CSharpCanonicalLine>> NewAtoms
        => CSharpFindingsInspection.Atoms(NewInspection);

    /// <summary>
    /// True when both methods have the same body-presence state and their lines are exact under the
    /// fidelity fold (no adds/removes/moves).
    /// </summary>
    public bool IsExact
        => Failure is null
           && SameBodyState(OldInspection, NewInspection)
           && FindingEquivalence.Exact.IsEquivalent(Pairs);

    public static CSharpFindingsResult Failed(
        CSharpFindingsInspection oldInspection,
        CSharpFindingsInspection newInspection,
        string failure)
        => new(
            [],
            new FindingMatch([], []),
            oldInspection,
            newInspection,
            failure ?? throw new ArgumentNullException(nameof(failure)));

    static bool SameBodyState(
        CSharpFindingsInspection oldInspection,
        CSharpFindingsInspection newInspection)
        => (oldInspection, newInspection) switch
        {
            (CSharpFindingsInspection.Body, CSharpFindingsInspection.Body) => true,
            (CSharpFindingsInspection.Absent, CSharpFindingsInspection.Absent) => true,
            _ => false,
        };
}
