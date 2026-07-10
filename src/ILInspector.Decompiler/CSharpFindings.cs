using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Decompiler;

/// <summary>
/// Adapts decompiled C# method bodies onto the domain-free finding substrate. It reuses
/// <see cref="CSharpBodyDiff"/> to render and canonicalize a method into ordered C# statement
/// occurrences, runs the shared <see cref="FindingMatcher"/>, folds the alignment into
/// <see cref="PairFinding{T}"/> transitions, and classifies trivia-only matched text changes as
/// encoding-only.
/// </summary>
public static class CSharpFindings
{
    /// <summary>The finding descriptor for a single decompiled C# statement occurrence.</summary>
    public static readonly FindingDescriptor StatementDescriptor = new("csharp.stmt", "C# statement");

    /// <summary>
    /// The census (one feed): inspects a single decompiled method body and lazily yields one
    /// <see cref="Finding{T}"/> per rendered statement/line. Existence, filtering, and counts are
    /// LINQ over the returned stream; failed canonicalization yields an empty census.
    /// </summary>
    public static IEnumerable<Finding<CSharpCanonicalStatement>> Inspect(
        MetadataSource source,
        MethodDefinitionHandle method,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);

        if (!CSharpBodyDiff.TryCanonicalize(source, method, out var statements, out _))
            return [];

        return ProjectAtoms(statements, subject);
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

        if (!CSharpBodyDiff.TryCanonicalize(oldSource, oldMethod, out var oldStatements, out var oldFailure))
            return CSharpFindingsResult.Failed(oldFailure ?? "old body canonicalization failed");
        if (!CSharpBodyDiff.TryCanonicalize(newSource, newMethod, out var newStatements, out var newFailure))
            return CSharpFindingsResult.Failed(newFailure ?? "new body canonicalization failed");

        return CompareCanonicalized(oldStatements, newStatements, subject, acceptanceThreshold);
    }

    internal static CSharpFindingsResult CompareCanonicalized(
        ImmutableArray<CSharpCanonicalStatement> oldStatements,
        ImmutableArray<CSharpCanonicalStatement> newStatements,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var oldAtoms = ProjectAtoms(oldStatements, subject).ToImmutableArray();
        var newAtoms = ProjectAtoms(newStatements, subject).ToImmutableArray();

        FindingMatch match;
        try
        {
            match = FindingMatcher.Match(oldAtoms.Keys(), newAtoms.Keys());
        }
        catch (ArgumentException ex)
        {
            return CSharpFindingsResult.Failed(ex.Message);
        }

        var pairs = FindingFold.ToPairs(match, oldAtoms, newAtoms, acceptanceThreshold);
        pairs = ApplyTriviaOnlyClassification(pairs);
        return new CSharpFindingsResult(pairs, match, oldAtoms, newAtoms, Failure: null);
    }

    static ImmutableArray<PairFinding<CSharpCanonicalStatement>> ApplyTriviaOnlyClassification(
        ImmutableArray<PairFinding<CSharpCanonicalStatement>> pairs)
    {
        var builder = ImmutableArray.CreateBuilder<PairFinding<CSharpCanonicalStatement>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<CSharpCanonicalStatement>.Present present
                && present.Difference == FindingDifferenceKind.None
                && !string.Equals(present.Old.Payload.Text, present.New.Payload.Text, StringComparison.Ordinal))
            {
                builder.Add(new PairFinding<CSharpCanonicalStatement>.Present(
                    present.Old,
                    present.New,
                    FindingDifferenceKind.EncodingOnly,
                    Detail: "trivia-only"));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.MoveToImmutable();
    }

    static IEnumerable<Finding<CSharpCanonicalStatement>> ProjectAtoms(
        ImmutableArray<CSharpCanonicalStatement> statements,
        FindingSubject subject)
    {
        for (int i = 0; i < statements.Length; i++)
        {
            yield return new Finding<CSharpCanonicalStatement>(
                subject,
                StatementDescriptor,
                new FindingKey(GetIdentityKey(statements[i])),
                i,
                statements[i]);
        }
    }

    /// <summary>
    /// The canonical content key for a rendered C# statement. The key is produced by
    /// <see cref="CSharpBodyDiff"/> so finding matching stays anchored to the existing body-diff
    /// render/canonicalization path.
    /// </summary>
    public static string GetIdentityKey(CSharpCanonicalStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return statement.IdentityKey;
    }
}

/// <summary>The outcome of a <see cref="CSharpFindings.Compare"/> call.</summary>
public sealed record CSharpFindingsResult(
    ImmutableArray<PairFinding<CSharpCanonicalStatement>> Pairs,
    FindingMatch Match,
    ImmutableArray<Finding<CSharpCanonicalStatement>> OldAtoms,
    ImmutableArray<Finding<CSharpCanonicalStatement>> NewAtoms,
    string? Failure)
{
    /// <summary>True when the bodies are exact under the fidelity fold (no adds/removes/moves).</summary>
    public bool IsExact => Failure is null && FindingEquivalence.Exact.IsEquivalent(Pairs);

    public static CSharpFindingsResult Failed(string failure)
        => new([], new FindingMatch([], []), [], [], failure);
}
