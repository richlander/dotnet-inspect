using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Evidence;

namespace ILInspector.Decompiler;

/// <summary>
/// Adapts decompiled C# method bodies onto the domain-free evidence substrate. It reuses
/// <see cref="CSharpBodyDiff"/> to render and canonicalize a method into ordered C# statement
/// occurrences, runs the shared <see cref="EvidenceMatcher"/>, folds the correspondence into
/// <see cref="EvidenceRow"/>s, and classifies trivia-only matched text changes as encoding-only.
/// </summary>
public static class CSharpEvidence
{
    /// <summary>The evidence descriptor for a single decompiled C# statement occurrence.</summary>
    public static readonly EvidenceDescriptor StatementDescriptor = new("csharp.stmt", "C# statement");

    public static CSharpEvidenceResult Compare(
        MetadataSource oldSource,
        MethodDefinitionHandle oldMethod,
        MetadataSource newSource,
        MethodDefinitionHandle newMethod,
        EvidenceSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(subject);

        try
        {
            if (!CSharpBodyDiff.TryCanonicalize(oldSource, oldMethod, out var oldStatements, out var oldFailure))
                return CSharpEvidenceResult.Failed(oldFailure ?? "old body canonicalization failed");
            if (!CSharpBodyDiff.TryCanonicalize(newSource, newMethod, out var newStatements, out var newFailure))
                return CSharpEvidenceResult.Failed(newFailure ?? "new body canonicalization failed");

            return CompareCanonicalized(oldStatements, newStatements, subject, acceptanceThreshold);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return CSharpEvidenceResult.Failed(ex.Message);
        }
    }

    internal static CSharpEvidenceResult CompareCanonicalized(
        ImmutableArray<CSharpCanonicalStatement> oldStatements,
        ImmutableArray<CSharpCanonicalStatement> newStatements,
        EvidenceSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var oldStream = BuildOccurrences(oldStatements);
        var newStream = BuildOccurrences(newStatements);

        EvidenceMatch correspondence;
        try
        {
            correspondence = EvidenceMatcher.Match(oldStream, newStream);
        }
        catch (ArgumentException ex)
        {
            return CSharpEvidenceResult.Failed(ex.Message);
        }

        var rows = EvidenceFold.ToRows(correspondence, oldStream, newStream, subject, StatementDescriptor, acceptanceThreshold);
        rows = ApplyTriviaOnlyClassification(rows, oldStatements, newStatements);
        return new CSharpEvidenceResult(rows, correspondence, oldStream, newStream, Failure: null);
    }

    static ImmutableArray<EvidenceOccurrence> BuildOccurrences(ImmutableArray<CSharpCanonicalStatement> statements)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceOccurrence>(statements.Length);
        foreach (var statement in statements)
        {
            // ScopeKey is left null for rung 0: CSharpBodyDiff aligns rendered lines without block
            // identity, and C# scope-aware equivalence belongs to the future soft tier (#2585).
            builder.Add(new EvidenceOccurrence(GetIdentityKey(statement), ScopeKey: null, Payload: statement.Operation));
        }

        return builder.MoveToImmutable();
    }

    static ImmutableArray<EvidenceRow> ApplyTriviaOnlyClassification(
        ImmutableArray<EvidenceRow> rows,
        ImmutableArray<CSharpCanonicalStatement> oldStatements,
        ImmutableArray<CSharpCanonicalStatement> newStatements)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceRow>(rows.Length);
        foreach (var row in rows)
        {
            if (row.Kind == EvidenceRowKind.Present
                && row.DifferenceKind == EvidenceDifferenceKind.None
                && row.Anchor.OldIndex >= 0
                && row.Anchor.NewIndex >= 0
                && !string.Equals(oldStatements[row.Anchor.OldIndex].Text, newStatements[row.Anchor.NewIndex].Text, StringComparison.Ordinal))
            {
                builder.Add(row with { DifferenceKind = EvidenceDifferenceKind.EncodingOnly, Detail = "trivia-only" });
            }
            else
            {
                builder.Add(row);
            }
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// The canonical content key for a rendered C# statement. The key is produced by
    /// <see cref="CSharpBodyDiff"/> so evidence matching stays anchored to the existing body-diff
    /// render/canonicalization path.
    /// </summary>
    public static string GetIdentityKey(CSharpCanonicalStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return statement.IdentityKey;
    }
}

/// <summary>The outcome of a <see cref="CSharpEvidence.Compare"/> call.</summary>
public sealed record CSharpEvidenceResult(
    ImmutableArray<EvidenceRow> Rows,
    EvidenceMatch Match,
    ImmutableArray<EvidenceOccurrence> OldOccurrences,
    ImmutableArray<EvidenceOccurrence> NewOccurrences,
    string? Failure)
{
    /// <summary>True when the bodies are exact under the fidelity fold (no adds/removes/moves).</summary>
    public bool IsExact => Failure is null && EvidenceEquivalence.Exact.IsEquivalent(Rows);

    public static CSharpEvidenceResult Failed(string failure)
        => new([], new EvidenceMatch([], []), [], [], failure);
}
