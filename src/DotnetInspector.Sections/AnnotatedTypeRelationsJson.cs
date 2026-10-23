using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public static class AnnotatedTypeRelationsJson
{
    public static void Write(
        Utf8JsonWriter writer,
        AnnotatedTypeRelationsDocument document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        JsonSerializer.Serialize(
            writer,
            AnnotatedTypeRelationsJsonDocument.From(document),
            AnnotatedTypeRelationsDocumentJsonContext.Default
                .AnnotatedTypeRelationsJsonDocument);
    }
}

internal sealed record AnnotatedTypeRelationsJsonDocument(
    AnnotatedTypeRelationsJsonSelection Selection,
    AnnotatedTypeRelationsJsonTerminal Terminal,
    AnnotatedTypeRelationsJsonSubject[] Subjects,
    AnnotatedTypeRelationsJsonRelation[] Relations,
    AnnotatedTypeRelationsJsonOccurrence[] Occurrences,
    AnnotatedTypeRelationsJsonAnnotation[] Annotations,
    AnnotatedTypeRelationsJsonFamilyOutcome[] RelationshipOutcomes,
    AnnotatedTypeRelationsJsonFamilyOutcome[] ColorOutcomes,
    AnnotatedTypeRelationsJsonFocusJoin[] FocusJoins)
{
    internal static AnnotatedTypeRelationsJsonDocument From(
        AnnotatedTypeRelationsDocument document) =>
        new(
            AnnotatedTypeRelationsJsonSelection.From(document.Selection),
            AnnotatedTypeRelationsJsonTerminal.From(document.Terminal),
            [.. document.Subjects.Select(
                subject => new AnnotatedTypeRelationsJsonSubject(
                    subject.Id,
                    CallGraphJsonSubject.From(subject.Subject)))],
            [.. document.Relations.Select(
                relation => new AnnotatedTypeRelationsJsonRelation(
                    relation.Id,
                    relation.Identity.Relationship.Id,
                    relation.Form.ToString(),
                    relation.EvidenceKind.ToString(),
                    relation.SourceSubjectId,
                    relation.TargetSubjectId))],
            [.. document.Occurrences.Select(
                occurrence => new AnnotatedTypeRelationsJsonOccurrence(
                    occurrence.Id,
                    occurrence.RelationId,
                    occurrence.Producer.Name,
                    occurrence.ProducerOccurrenceId,
                    occurrence.Identity.Relationship.Id,
                    occurrence.SourceSubjectId,
                    occurrence.TargetSubjectId,
                    CallGraphJsonEvidence.From(occurrence.Evidence),
                    [.. occurrence.DerivedFromProducerOccurrenceIds]))],
            [.. document.Annotations.Select(
                AnnotatedTypeRelationsJsonAnnotation.From)],
            [.. document.RelationshipOutcomes.Select(
                outcome => AnnotatedTypeRelationsJsonFamilyOutcome.From(
                    outcome.Producer.Name,
                    outcome.Contribution.Relationship.Id,
                    outcome.Contribution.Disposition,
                    outcome.Contribution.Coverage,
                    outcome.Contribution.Terminal))],
            [.. document.ColorOutcomes.Select(
                outcome => AnnotatedTypeRelationsJsonFamilyOutcome.From(
                    outcome.Producer.Name,
                    outcome.Contribution.Color.Id,
                    outcome.Contribution.Disposition,
                    outcome.Contribution.Coverage,
                    null))],
            [.. document.FocusJoins.Select(
                join => new AnnotatedTypeRelationsJsonFocusJoin(
                    join.RelationId,
                    join.Direction.ToString(),
                    join.Correspondence.ToString()
                        ?? throw new InvalidOperationException(
                            "Focus correspondence must have a name.")))]);
}

internal sealed record AnnotatedTypeRelationsJsonSelection(
    string[] Relationships,
    string Direction,
    string[] Colors,
    string[] PredicateColors,
    string Terminal)
{
    internal static AnnotatedTypeRelationsJsonSelection From(
        AnnotatedTypeRelationsSelection selection) =>
        new(
            [.. selection.Relationships.Select(
                static relationship => relationship.Id)],
            selection.Direction.ToString(),
            [.. selection.Colors.Select(static color => color.Id)],
            [.. selection.PredicateColors.Select(static color => color.Id)],
            selection.Terminal.ToString());
}

internal sealed record AnnotatedTypeRelationsJsonTerminal(
    string Kind,
    int? Value,
    bool? IsComplete,
    bool? IsExact,
    string? Exists)
{
    internal static AnnotatedTypeRelationsJsonTerminal From(
        AnnotatedTypeRelationsTerminalOutcome terminal) =>
        terminal switch
        {
            AnnotatedTypeRelationsTerminalOutcome.Rows rows =>
                new("Rows", rows.Count, rows.IsComplete, null, null),
            AnnotatedTypeRelationsTerminalOutcome.CountResult count =>
                new("Count", count.Value, null, count.IsExact, null),
            AnnotatedTypeRelationsTerminalOutcome.Exists exists =>
                new("Exists", null, null, null, exists.Value.ToString()),
            _ => throw new NotSupportedException(
                $"Unsupported Annotated Type Relations terminal "
                + $"'{terminal.GetType().FullName}'."),
        };
}

internal sealed record AnnotatedTypeRelationsJsonSubject(
    int Id,
    CallGraphJsonSubject Subject);

internal sealed record AnnotatedTypeRelationsJsonRelation(
    int Id,
    string Relationship,
    string Form,
    string EvidenceKind,
    int SourceSubjectId,
    int TargetSubjectId);

internal sealed record AnnotatedTypeRelationsJsonOccurrence(
    int Id,
    int RelationId,
    string Producer,
    int ProducerOccurrenceId,
    string Relationship,
    int SourceSubjectId,
    int TargetSubjectId,
    CallGraphJsonEvidence Evidence,
    int[] DerivedFromProducerOccurrenceIds);

internal sealed record AnnotatedTypeRelationsJsonAnnotation(
    int Id,
    string Producer,
    CallGraphJsonCharacteristicDescriptor Descriptor,
    CallGraphJsonValue Value,
    AnnotatedTypeRelationsJsonTarget Target,
    string Derivation,
    AnnotatedTypeRelationsJsonTarget[] Sources,
    AnnotatedTypeRelationsJsonAnnotationEvidence Evidence)
{
    internal static AnnotatedTypeRelationsJsonAnnotation From(
        AnnotatedTypeRelationsDocumentAnnotation annotation) =>
        new(
            annotation.Id,
            annotation.Producer.Name,
            CallGraphJsonCharacteristicDescriptor.From(annotation.Descriptor),
            CallGraphJsonValue.From(annotation.Value),
            AnnotatedTypeRelationsJsonTarget.From(annotation.Target),
            annotation.Derivation.ToString(),
            [.. annotation.Sources.Select(
                AnnotatedTypeRelationsJsonTarget.From)],
            AnnotatedTypeRelationsJsonAnnotationEvidence.From(
                annotation.Evidence));
}

internal sealed record AnnotatedTypeRelationsJsonTarget(string Kind, int Id)
{
    internal static AnnotatedTypeRelationsJsonTarget From(
        AnnotatedTypeRelationsDocumentTarget target) =>
        new(target.Kind.ToString(), target.Id);
}

internal sealed record AnnotatedTypeRelationsJsonAnnotationEvidence(
    string Kind,
    int? CustomAttributeToken,
    string? AttributeTypeName)
{
    internal static AnnotatedTypeRelationsJsonAnnotationEvidence From(
        object evidence) =>
        evidence switch
        {
            CompatibilityAttributeEvidence attribute =>
                new(
                    nameof(CompatibilityAttributeEvidence),
                    attribute.CustomAttributeToken,
                    attribute.AttributeTypeName),
            _ => new(evidence.GetType().Name, null, null),
        };
}

internal sealed record AnnotatedTypeRelationsJsonFamilyOutcome(
    string Producer,
    string Family,
    string Disposition,
    SubjectRelationCoverage Coverage,
    AnnotatedTypeRelationsJsonFamilyTerminal? Terminal)
{
    internal static AnnotatedTypeRelationsJsonFamilyOutcome From(
        string producer,
        string family,
        SubjectRelationProducerDisposition disposition,
        SubjectRelationCoverage coverage,
        AnnotatedTypeRelationsFamilyTerminal? terminal) =>
        new(
            producer,
            family,
            disposition.ToString(),
            coverage,
            terminal is null
                ? null
                : AnnotatedTypeRelationsJsonFamilyTerminal.From(terminal));
}

internal sealed record AnnotatedTypeRelationsJsonFamilyTerminal(
    string Kind,
    int? Value,
    bool? IsExact,
    string? Exists)
{
    internal static AnnotatedTypeRelationsJsonFamilyTerminal From(
        AnnotatedTypeRelationsFamilyTerminal terminal) =>
        terminal switch
        {
            AnnotatedTypeRelationsFamilyTerminal.Rows rows =>
                new("Rows", rows.Values.Length, null, null),
            AnnotatedTypeRelationsFamilyTerminal.Count count =>
                new("Count", count.Value, count.IsExact, null),
            AnnotatedTypeRelationsFamilyTerminal.Exists exists =>
                new("Exists", null, null, exists.Value.ToString()),
            _ => throw new NotSupportedException(
                $"Unsupported Annotated Type Relations family terminal "
                + $"'{terminal.GetType().FullName}'."),
        };
}

internal sealed record AnnotatedTypeRelationsJsonFocusJoin(
    int RelationId,
    string Direction,
    string Correspondence);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AnnotatedTypeRelationsJsonDocument))]
internal partial class AnnotatedTypeRelationsDocumentJsonContext :
    JsonSerializerContext;
