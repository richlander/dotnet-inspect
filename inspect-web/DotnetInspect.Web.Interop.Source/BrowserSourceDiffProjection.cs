using System.Runtime.Versioning;
using System.Text;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using Inspector.Findings;
using ILInspector.SourceLink;
using Markout;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
internal static class BrowserSourceDiffProjection
{
    internal const int MaximumRequestBytes = 8 * 1024;
    internal const int MaximumRawEndpointBytes = 128 * 1024;
    internal const int MaximumRawEndpointLines = 1_024;
    internal const int MaximumRelations = 2_048;
    internal const int MaximumCoordinateOccurrences = 2_048;
    internal const int MaximumMappedChanges = 2_048;
    internal const int MaximumInnerMappings = 1_024;
    internal const int MaximumAnnotations = 512;
    internal const int MaximumAnnotationTextBytes = 64 * 1024;
    internal const int MaximumAuxiliaryTextBytes = 16 * 1024;
    internal const int MaximumEncodedResultBytes = 1024 * 1024;

    internal static void AdmitRequest(string requestJson)
    {
        ArgumentNullException.ThrowIfNull(requestJson);
        Admit(
            BrowserSourceDiffCapacityDimension.RequestBytes,
            Encoding.UTF8.GetByteCount(requestJson),
            MaximumRequestBytes);
    }

    internal static void AdmitPair(AssemblyMemberSourcePairResult pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        AdmitEndpoint(
            pair.Before,
            BrowserSourceDiffCapacityDimension.RawBeforeBytes,
            BrowserSourceDiffCapacityDimension.RawBeforeLines);
        AdmitEndpoint(
            pair.After,
            BrowserSourceDiffCapacityDimension.RawAfterBytes,
            BrowserSourceDiffCapacityDimension.RawAfterLines);
    }

    internal static BrowserSourceDiff Project(
        MemberSourcePairDiffPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        AnalysisDiff<string> analysis = presentation.Analysis;
        MappedTextDiff mapped = presentation.Diff;

        int coordinateOccurrences = analysis.Relations.Sum(CoordinateOccurrences);
        int innerMappings = mapped.Changes.Sum(change => change.InnerMappings.Length);
        int annotations = mapped.Changes.Sum(change => change.Annotations.Length);
        int annotationTextBytes = mapped.Changes.Sum(change =>
            change.Annotations.Sum(annotation => Encoding.UTF8.GetByteCount(annotation.Text)));
        AdmitProjectedShape(
            analysis.Relations.Length,
            coordinateOccurrences,
            mapped.Changes.Length,
            innerMappings,
            annotations,
            annotationTextBytes);

        return new(
            Version: 1,
            Sequence(mapped.Before),
            Sequence(mapped.After),
            [.. analysis.Relations.Select(Relation)],
            new(
                presentation.Statistics.Added,
                presentation.Statistics.Removed,
                presentation.Statistics.ChangedBefore,
                presentation.Statistics.ChangedAfter,
                presentation.Statistics.MovedBefore,
                presentation.Statistics.MovedAfter),
            [.. mapped.Changes.Select(Change)]);
    }

    internal static string? BrowseUrl(string? resolvedUrl)
        => SourceLinkProvenance.BrowseUrl(resolvedUrl);

    internal static void AdmitAuxiliaryText(IEnumerable<string?> values)
    {
        int bytes = values.Sum(value =>
            value is null ? 0 : Encoding.UTF8.GetByteCount(value));
        Admit(
            BrowserSourceDiffCapacityDimension.AuxiliaryTextBytes,
            bytes,
            MaximumAuxiliaryTextBytes);
    }

    internal static void AdmitProjectedShape(
        int relations,
        int coordinateOccurrences,
        int mappedChanges,
        int innerMappings,
        int annotations,
        int annotationTextBytes)
    {
        Admit(
            BrowserSourceDiffCapacityDimension.Relations,
            relations,
            MaximumRelations);
        Admit(
            BrowserSourceDiffCapacityDimension.CoordinateOccurrences,
            coordinateOccurrences,
            MaximumCoordinateOccurrences);
        Admit(
            BrowserSourceDiffCapacityDimension.MappedChanges,
            mappedChanges,
            MaximumMappedChanges);
        Admit(
            BrowserSourceDiffCapacityDimension.InnerMappings,
            innerMappings,
            MaximumInnerMappings);
        Admit(
            BrowserSourceDiffCapacityDimension.Annotations,
            annotations,
            MaximumAnnotations);
        Admit(
            BrowserSourceDiffCapacityDimension.AnnotationTextBytes,
            annotationTextBytes,
            MaximumAnnotationTextBytes);
    }

    static void AdmitEndpoint(
        AssemblyMemberSourcePairEndpoint endpoint,
        BrowserSourceDiffCapacityDimension byteDimension,
        BrowserSourceDiffCapacityDimension lineDimension)
    {
        if (endpoint is not AssemblyMemberSourcePairEndpoint.Resolved
            {
                Source: AssemblyMemberPdbSourceAttempt.Available available,
            })
        {
            return;
        }

        string text = available.Inspection.Text
            ?? throw new InvalidOperationException(
                "Available authored Source has no text.");
        AdmitEndpointText(text, byteDimension, lineDimension);
    }

    internal static void AdmitEndpointText(
        string text,
        BrowserSourceDiffCapacityDimension byteDimension,
        BrowserSourceDiffCapacityDimension lineDimension)
    {
        Admit(
            byteDimension,
            Encoding.UTF8.GetByteCount(text),
            MaximumRawEndpointBytes);
        Admit(
            lineDimension,
            PhysicalLineCount(text),
            MaximumRawEndpointLines);
    }

    static int PhysicalLineCount(string text)
    {
        if (text.Length == 0)
            return 0;

        int count = 1;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
                continue;
            count++;
            if (text[index] == '\r'
                && index + 1 < text.Length
                && text[index + 1] == '\n')
            {
                index++;
            }
        }
        return count;
    }

    static int CoordinateOccurrences(AnalysisDiffRelation relation)
        => relation switch
        {
            AnalysisDiffRelation.Addition addition =>
                addition.AfterCoordinates.Length,
            AnalysisDiffRelation.Removal removal =>
                removal.BeforeCoordinates.Length,
            AnalysisDiffRelation.Correspondence correspondence =>
                correspondence.BeforeCoordinates.Length
                + correspondence.AfterCoordinates.Length,
            _ => throw new InvalidOperationException(
                "Unknown analytical Source diff relation."),
        };

    static BrowserSourceDiffSequence Sequence(TextDiffSequence sequence)
        => new(
            sequence.Label,
            [.. sequence.Lines],
            sequence.FinalLineTerminator switch
            {
                TextDiffLineTerminator.Unknown =>
                    BrowserSourceDiffLineTerminator.Unknown,
                TextDiffLineTerminator.Present =>
                    BrowserSourceDiffLineTerminator.Present,
                TextDiffLineTerminator.Absent =>
                    BrowserSourceDiffLineTerminator.Absent,
                _ => throw new InvalidOperationException(
                    "Unknown mapped Source diff line terminator."),
            });

    static BrowserSourceDiffRelation Relation(AnalysisDiffRelation relation)
        => relation switch
        {
            AnalysisDiffRelation.Addition addition => new(
                BrowserSourceDiffRelationKind.Addition,
                [],
                [.. addition.AfterCoordinates],
                null,
                null),
            AnalysisDiffRelation.Removal removal => new(
                BrowserSourceDiffRelationKind.Removal,
                [.. removal.BeforeCoordinates],
                [],
                null,
                null),
            AnalysisDiffRelation.Correspondence correspondence => new(
                BrowserSourceDiffRelationKind.Correspondence,
                [.. correspondence.BeforeCoordinates],
                [.. correspondence.AfterCoordinates],
                correspondence.Content switch
                {
                    AnalysisDiffContentKind.Unchanged =>
                        BrowserSourceDiffContentKind.Unchanged,
                    AnalysisDiffContentKind.Changed =>
                        BrowserSourceDiffContentKind.Changed,
                    _ => throw new InvalidOperationException(
                        "Authored Source correspondence has no content classification."),
                },
                correspondence.Placement switch
                {
                    AnalysisDiffPlacementKind.Stable =>
                        BrowserSourceDiffPlacementKind.Stable,
                    AnalysisDiffPlacementKind.Moved =>
                        BrowserSourceDiffPlacementKind.Moved,
                    _ => throw new InvalidOperationException(
                        "Authored Source correspondence has no placement classification."),
                }),
            _ => throw new InvalidOperationException(
                "Unknown analytical Source diff relation."),
        };

    static BrowserSourceDiffChange Change(TextDiffChange change)
        => new(
            new(change.Before.Start, change.Before.Count),
            new(change.After.Start, change.After.Count),
            [.. change.InnerMappings.Select(mapping => new BrowserSourceDiffInnerMapping(
                Span(mapping.Before),
                Span(mapping.After)))],
            [.. change.Annotations.Select(Annotation)]);

    static BrowserSourceDiffSpan Span(TextDiffSpan span)
        => new(span.Line, span.Start, span.Count);

    static BrowserSourceDiffAnnotation Annotation(TextDiffAnnotation annotation)
        => new(
            annotation.Text,
            annotation.Severity switch
            {
                CalloutSeverity.Note => BrowserSourceDiffSeverity.Note,
                CalloutSeverity.Tip => BrowserSourceDiffSeverity.Tip,
                CalloutSeverity.Important => BrowserSourceDiffSeverity.Important,
                CalloutSeverity.Warning => BrowserSourceDiffSeverity.Warning,
                CalloutSeverity.Caution => BrowserSourceDiffSeverity.Caution,
                _ => throw new InvalidOperationException(
                    "Unknown mapped Source diff annotation severity."),
            },
            annotation.TargetKind switch
            {
                TextDiffAnnotationTargetKind.Change =>
                    BrowserSourceDiffAnnotationTargetKind.Change,
                TextDiffAnnotationTargetKind.Line =>
                    BrowserSourceDiffAnnotationTargetKind.Line,
                TextDiffAnnotationTargetKind.Span =>
                    BrowserSourceDiffAnnotationTargetKind.Span,
                _ => throw new InvalidOperationException(
                    "Unknown mapped Source diff annotation target."),
            },
            annotation.Side switch
            {
                TextDiffSide.Before => BrowserSourceDiffSide.Before,
                TextDiffSide.After => BrowserSourceDiffSide.After,
                TextDiffSide.Both => BrowserSourceDiffSide.Both,
                null => null,
                _ => throw new InvalidOperationException(
                    "Unknown mapped Source diff annotation side."),
            },
            annotation.Line,
            annotation.Span is { } span ? Span(span) : null);

    static void Admit(
        BrowserSourceDiffCapacityDimension dimension,
        int actual,
        int limit)
    {
        if (actual > limit)
            throw new BrowserSourceDiffCapacityException(dimension, limit, actual);
    }
}

internal sealed class BrowserSourceDiffCapacityException(
    BrowserSourceDiffCapacityDimension dimension,
    int limit,
    int actual)
    : InvalidOperationException(
        $"Source diff {dimension} capacity {limit:N0} was exceeded by {actual:N0}.")
{
    internal BrowserSourceDiffCapacity Capacity { get; } =
        new(dimension, limit, actual);
}
