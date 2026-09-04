using CSharpText;
using DotnetInspector.Queries;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Text;
using Markout;

namespace DotnetInspector.Presentation;

/// <summary>The two-sided line populations reported by a member source diff.</summary>
public readonly record struct MemberSourceDiffStatistics(
    int Added,
    int Removed,
    int ChangedBefore,
    int ChangedAfter,
    int MovedBefore,
    int MovedAfter)
{
    public bool HasDifferences =>
        Added > 0
        || Removed > 0
        || ChangedBefore > 0
        || ChangedAfter > 0
        || MovedBefore > 0
        || MovedAfter > 0;

    public static MemberSourceDiffStatistics Create(
        AnalysisDiff<string> analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        int added = 0;
        int removed = 0;
        int changedBefore = 0;
        int changedAfter = 0;
        int movedBefore = 0;
        int movedAfter = 0;

        foreach (AnalysisDiffRelation relation in analysis.Relations)
        {
            switch (relation)
            {
                case AnalysisDiffRelation.Addition addition:
                    added += addition.AfterCoordinates.Length;
                    break;
                case AnalysisDiffRelation.Removal removal:
                    removed += removal.BeforeCoordinates.Length;
                    break;
                case AnalysisDiffRelation.Correspondence correspondence:
                    if (correspondence.Content == AnalysisDiffContentKind.Changed)
                    {
                        changedBefore += correspondence.BeforeCoordinates.Length;
                        changedAfter += correspondence.AfterCoordinates.Length;
                    }
                    if (correspondence.Placement == AnalysisDiffPlacementKind.Moved)
                    {
                        movedBefore += correspondence.BeforeCoordinates.Length;
                        movedAfter += correspondence.AfterCoordinates.Length;
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Member source analysis contains an unknown relation kind.");
            }
        }

        return new MemberSourceDiffStatistics(
            added,
            removed,
            changedBefore,
            changedAfter,
            movedBefore,
            movedAfter);
    }
}

/// <summary>A deterministic failure while projecting complete comparison evidence.</summary>
public sealed record MemberSourceDiffProjectionFailure(
    MemberSourceDiffProjectionFailureKind Kind,
    string Detail);

public enum MemberSourceDiffProjectionFailureKind
{
    DeclaringTypeNameNotRepresentable,
    MissingMemberBoundary,
    AmbiguousMemberBoundary,
    InconsistentDecompilerIndentation,
}

/// <summary>
/// The host-neutral canonical text, analysis, statistics, and Markout lowering for one member.
/// </summary>
public sealed record MemberSourceDiffPresentation(
    AssemblyMemberSourceComparisonEntry.Available Comparison,
    AssemblyMemberPdbSourceAttempt.Available Pdb,
    AssemblyMemberDecompiledSourceAttempt.Available Decompiled,
    string BeforeText,
    string AfterText,
    AnalysisDiff<string> Analysis,
    MemberSourceDiffStatistics Statistics,
    MappedTextDiff Diff);

/// <summary>The closed outcome of presenting one member source-comparison query result.</summary>
public abstract record MemberSourceDiffPresentationResult(
    AssemblyMemberSourceComparisonEntry Comparison)
{
    public sealed record Available(MemberSourceDiffPresentation Presentation)
        : MemberSourceDiffPresentationResult(Presentation.Comparison);

    public sealed record Unavailable(AssemblyMemberSourceComparisonEntry Comparison)
        : MemberSourceDiffPresentationResult(Comparison);

    public sealed record Failed(
        AssemblyMemberSourceComparisonEntry.Available SuccessfulComparison,
        MemberSourceDiffProjectionFailure Failure)
        : MemberSourceDiffPresentationResult(SuccessfulComparison);
}

/// <summary>Creates the shared member source diff presentation.</summary>
public static class MemberSourceDiffPresentationAdapter
{
    public const string BeforeLabel = "PDB comparison";
    public const string AfterLabel = "Decompiled comparison";

    static readonly FindingSubject Subject =
        new("member.source.diff", "Member source diff");

    public static MemberSourceDiffPresentationResult Create(
        AssemblyMemberSourceComparisonEntry comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        if (comparison
                is not AssemblyMemberSourceComparisonEntry.Available available
            || available.Pdb
                is not AssemblyMemberPdbSourceAttempt.Available pdb
            || available.Decompiled
                is not AssemblyMemberDecompiledSourceAttempt.Available decompiled)
        {
            return new MemberSourceDiffPresentationResult.Unavailable(comparison);
        }

        string leafName = MetadataNameArity.StripFromSegment(
            available.Request.Type.Segments[^1]);
        if (CSharpIdentifier.AdmitTypeDeclaration(leafName)
            is not CSharpTypeDeclarationIdentifierAdmission.Admitted admitted)
        {
            return Failed(
                available,
                MemberSourceDiffProjectionFailureKind.DeclaringTypeNameNotRepresentable,
                $"Declaring type name '{leafName}' cannot be represented exactly in a C# type declaration.");
        }

        BoundaryResult beforeBoundary =
            FindBoundary(pdb.Inspection.Text!, admitted.Spelling);
        if (beforeBoundary.Failure is { } beforeFailure)
            return new MemberSourceDiffPresentationResult.Failed(available, beforeFailure);

        BoundaryResult afterBoundary =
            FindBoundary(decompiled.Result.Text!, admitted.Spelling);
        if (afterBoundary.Failure is { } afterFailure)
            return new MemberSourceDiffPresentationResult.Failed(available, afterFailure);

        string beforeText = beforeBoundary.Text!;
        string? afterText = AlignDecompilerPlacement(
            afterBoundary.Text!,
            beforeBoundary.PlacementPrefix!,
            out MemberSourceDiffProjectionFailure? alignmentFailure);
        if (alignmentFailure is not null)
        {
            return new MemberSourceDiffPresentationResult.Failed(
                available,
                alignmentFailure);
        }

        AnalysisDiff<string> analysis =
            TextFindings.CreateAnalysisDiff(beforeText, afterText!, Subject);
        MemberSourceDiffStatistics statistics =
            MemberSourceDiffStatistics.Create(analysis);
        MappedTextDiff diff =
            TextAnalysisDiffPresentation.CreateMappedTextDiff(
                analysis,
                BeforeLabel,
                TextDiffLineTerminator.Absent,
                AfterLabel,
                TextDiffLineTerminator.Absent);

        return new MemberSourceDiffPresentationResult.Available(
            new MemberSourceDiffPresentation(
                available,
                pdb,
                decompiled,
                beforeText,
                afterText!,
                analysis,
                statistics,
                diff));
    }

    static BoundaryResult FindBoundary(string text, string typeName)
    {
        string[] lines = text.Split('\n');
        const int EndpointStartLine = 3;
        int endpointEndLine = EndpointStartLine + lines.Length - 1;
        DeclarationIndex index =
            DeclarationIndex.Build($"class {typeName}\n{{\n{text}\n}}");
        int wrapperIndex = -1;
        for (int declarationIndex = 0;
            declarationIndex < index.Declarations.Length;
            declarationIndex++)
        {
            DeclarationSpan declaration =
                index.Declarations[declarationIndex];
            if (declaration.ParentIndex == -1
                && declaration.Kind == DeclarationKind.Class
                && declaration.Name == typeName.TrimStart('@'))
            {
                wrapperIndex = declarationIndex;
                break;
            }
        }
        if (wrapperIndex < 0)
        {
            return BoundaryResult.Fail(
                MemberSourceDiffProjectionFailureKind.MissingMemberBoundary,
                "The synthetic declaring type boundary was not recognized.");
        }

        DeclarationSpan[] candidates =
        [
            .. index.Declarations.Where(
                declaration =>
                    declaration.ParentIndex == wrapperIndex
                    && declaration.SpanKnown
                    && declaration.TriviaStartLine == EndpointStartLine
                    && declaration.SignatureStartLine >= EndpointStartLine
                    && declaration.SignatureStartLine <= endpointEndLine
                    && declaration.EndLine == endpointEndLine),
        ];
        if (candidates.Length == 0)
        {
            return BoundaryResult.Fail(
                MemberSourceDiffProjectionFailureKind.MissingMemberBoundary,
                "The endpoint does not contain one complete direct child member declaration.");
        }
        if (candidates.Length != 1)
        {
            return BoundaryResult.Fail(
                MemberSourceDiffProjectionFailureKind.AmbiguousMemberBoundary,
                "The endpoint contains more than one complete direct child member declaration.");
        }

        DeclarationSpan member = candidates[0];
        int signatureLineIndex =
            member.SignatureStartLine - EndpointStartLine;
        string signatureLine = lines[signatureLineIndex];
        if (member.SignatureStartColumn > signatureLine.Length)
        {
            return BoundaryResult.Fail(
                MemberSourceDiffProjectionFailureKind.MissingMemberBoundary,
                "The member signature coordinate falls outside its endpoint line.");
        }

        int placementLength = 0;
        while (placementLength < signatureLine.Length
            && char.IsWhiteSpace(signatureLine[placementLength]))
        {
            placementLength++;
        }
        string placementPrefix = signatureLine[..Math.Min(
            placementLength,
            member.SignatureStartColumn)];
        string firstLine =
            placementPrefix + signatureLine[member.SignatureStartColumn..];
        string canonical = string.Join(
            "\n",
            new[] { firstLine }.Concat(lines[(signatureLineIndex + 1)..]));
        return new BoundaryResult(canonical, placementPrefix, Failure: null);
    }

    static string? AlignDecompilerPlacement(
        string text,
        string placementPrefix,
        out MemberSourceDiffProjectionFailure? failure)
    {
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Length == 0)
                continue;
            if (!lines[index].StartsWith("    ", StringComparison.Ordinal))
            {
                failure = new MemberSourceDiffProjectionFailure(
                    MemberSourceDiffProjectionFailureKind.InconsistentDecompilerIndentation,
                    $"Complete decompiled member line {index + 1} does not carry the required four-space type-body prefix.");
                return null;
            }
            lines[index] = placementPrefix + lines[index][4..];
        }

        failure = null;
        return string.Join("\n", lines);
    }

    static MemberSourceDiffPresentationResult.Failed Failed(
        AssemblyMemberSourceComparisonEntry.Available comparison,
        MemberSourceDiffProjectionFailureKind kind,
        string detail)
        => new(comparison, new MemberSourceDiffProjectionFailure(kind, detail));

    readonly record struct BoundaryResult(
        string? Text,
        string? PlacementPrefix,
        MemberSourceDiffProjectionFailure? Failure)
    {
        public static BoundaryResult Fail(
            MemberSourceDiffProjectionFailureKind kind,
            string detail)
            => new(
                Text: null,
                PlacementPrefix: null,
                new MemberSourceDiffProjectionFailure(kind, detail));
    }
}
