using DotnetInspector.Sections;
using DotnetInspector.Vocabulary;

namespace DotnetInspect.Web.Interop.Catalog;

internal static class BrowserVocabulary
{
    internal static BrowserVocabularyInspection ToBrowserInspection(
        InspectionEnvelope<VocabularySnapshot> inspection) =>
        new(
            ToBrowserSnapshot(inspection.Content),
            ToBrowserShare(inspection.Share),
            [.. inspection.Diagnostics.Select(ToBrowserDiagnostic)]);

    private static BrowserVocabularySnapshot ToBrowserSnapshot(
        VocabularySnapshot snapshot) =>
        new(
            snapshot.FormatVersion,
            ToBrowserIdentity(snapshot.Catalog),
            new BrowserVocabularySnapshotIdentity(snapshot.Identity.Value),
            [.. snapshot.Vocabularies.Select(ToBrowserVocabulary)]);

    private static BrowserVocabularyDefinition ToBrowserVocabulary(
        VocabularyDefinition vocabulary) =>
        new(
            new BrowserVocabularyDefinitionIdentity(
                vocabulary.Identity.Value),
            vocabulary.DisplayLabel,
            vocabulary.Summary,
            [.. vocabulary.Maps.Select(ToBrowserMap)],
            [.. vocabulary.Terms.Select(ToBrowserTerm)]);

    private static BrowserVocabularyMapDefinition ToBrowserMap(
        VocabularyMapDefinition map) =>
        new(
            new BrowserVocabularyMapDefinitionIdentity(
                map.Identity.Value),
            map.DisplayLabel,
            map.Summary,
            ToBrowserTarget(map.Target),
            ToBrowserCardinality(map.Cardinality),
            ToBrowserCoverage(map.Coverage));

    private static BrowserVocabularyTerm ToBrowserTerm(VocabularyTerm term) =>
        new(
            new BrowserVocabularyTermDefinitionIdentity(
                term.Identity.Value),
            term.DisplayLabel,
            term.Summary,
            [.. term.MapEntries.Select(ToBrowserEntry)]);

    private static BrowserVocabularyMapEntry ToBrowserEntry(
        VocabularyMapEntry entry) =>
        new(
            new BrowserVocabularyMapDefinitionIdentity(
                entry.Map.Value),
            [.. entry.Values.Select(ToBrowserValue)]);

    private static BrowserVocabularyMapTarget ToBrowserTarget(
        VocabularyMapTarget target) =>
        target switch
        {
            VocabularyMapTarget.Scalar scalar =>
                new BrowserVocabularyScalarMapTarget(
                    ToBrowserScalarKind(scalar.Kind)),
            VocabularyMapTarget.Terms terms =>
                new BrowserVocabularyTermsMapTarget(
                    ToBrowserReference(terms.Reference)),
            _ => throw new InvalidOperationException(
                "Unknown vocabulary map target."),
        };

    private static BrowserVocabularyTermSetReference ToBrowserReference(
        VocabularyTermSetReference reference) =>
        reference switch
        {
            VocabularyTermSetReference.Local local =>
                new BrowserVocabularyLocalTermSetReference(
                    new BrowserVocabularyDefinitionIdentity(
                        local.Vocabulary.Value)),
            VocabularyTermSetReference.External external =>
                new BrowserVocabularyExternalTermSetReference(
                    new BrowserVocabularySnapshotIdentity(
                        external.Snapshot.Value),
                    ToBrowserIdentity(external.Vocabulary)),
            _ => throw new InvalidOperationException(
                "Unknown vocabulary term-set reference."),
        };

    private static BrowserVocabularyMapValue ToBrowserValue(
        VocabularyMapValue value) =>
        value switch
        {
            VocabularyMapValue.Term term =>
                new BrowserVocabularyTermMapValue(
                    ToBrowserIdentity(term.Identity)),
            VocabularyMapValue.Scalar scalar =>
                ToBrowserScalarValue(scalar.Value),
            _ => throw new InvalidOperationException(
                "Unknown vocabulary map value."),
        };

    private static BrowserVocabularyMapValue ToBrowserScalarValue(
        VocabularyScalarValue value) =>
        value.Kind switch
        {
            VocabularyScalarKind.Text =>
                new BrowserVocabularyTextMapValue(value.Text!),
            VocabularyScalarKind.Integer =>
                new BrowserVocabularyIntegerMapValue(value.Integer),
            VocabularyScalarKind.Boolean =>
                new BrowserVocabularyBooleanMapValue(value.Boolean),
            _ => throw new InvalidOperationException(
                "Unknown vocabulary scalar kind."),
        };

    private static BrowserVocabularyScalarKind ToBrowserScalarKind(
        VocabularyScalarKind kind) =>
        kind switch
        {
            VocabularyScalarKind.Text =>
                BrowserVocabularyScalarKind.Text,
            VocabularyScalarKind.Integer =>
                BrowserVocabularyScalarKind.Integer,
            VocabularyScalarKind.Boolean =>
                BrowserVocabularyScalarKind.Boolean,
            _ => throw new InvalidOperationException(
                "Unknown vocabulary scalar kind."),
        };

    private static BrowserVocabularyInspectionShare ToBrowserShare(
        InspectionShare share) =>
        share switch
        {
            InspectionShare.Available available =>
                new BrowserVocabularyAvailableShare(
                    available.FullUrl,
                    available.Packet),
            InspectionShare.NonProjectable nonProjectable =>
                new BrowserVocabularyNonProjectableShare(
                    nonProjectable.Path,
                    nonProjectable.Reason.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown vocabulary inspection Share outcome."),
        };

    private static BrowserVocabularyInspectionDiagnostic ToBrowserDiagnostic(
        InspectionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            ToBrowserSeverity(diagnostic.Severity),
            diagnostic.Summary.ToString(),
            diagnostic.Correspondence?.ToString());

    private static BrowserVocabularyMapCardinality ToBrowserCardinality(
        VocabularyMapCardinality cardinality) =>
        cardinality switch
        {
            VocabularyMapCardinality.ExactlyOne =>
                BrowserVocabularyMapCardinality.ExactlyOne,
            VocabularyMapCardinality.OptionalOne =>
                BrowserVocabularyMapCardinality.OptionalOne,
            VocabularyMapCardinality.OneOrMore =>
                BrowserVocabularyMapCardinality.OneOrMore,
            VocabularyMapCardinality.ZeroOrMore =>
                BrowserVocabularyMapCardinality.ZeroOrMore,
            _ => throw new InvalidOperationException(
                "Unknown vocabulary map cardinality."),
        };

    private static BrowserVocabularyMapCoverage ToBrowserCoverage(
        VocabularyMapCoverage coverage) =>
        coverage switch
        {
            VocabularyMapCoverage.Complete =>
                BrowserVocabularyMapCoverage.Complete,
            VocabularyMapCoverage.Partial =>
                BrowserVocabularyMapCoverage.Partial,
            _ => throw new InvalidOperationException(
                "Unknown vocabulary map coverage."),
        };

    private static BrowserVocabularyDiagnosticSeverity ToBrowserSeverity(
        InspectionDiagnosticSeverity severity) =>
        severity switch
        {
            InspectionDiagnosticSeverity.Information =>
                BrowserVocabularyDiagnosticSeverity.Information,
            InspectionDiagnosticSeverity.Warning =>
                BrowserVocabularyDiagnosticSeverity.Warning,
            InspectionDiagnosticSeverity.Error =>
                BrowserVocabularyDiagnosticSeverity.Error,
            _ => throw new InvalidOperationException(
                "Unknown vocabulary diagnostic severity."),
        };

    private static BrowserVocabularyCatalogIdentity ToBrowserIdentity(
        VocabularyCatalogIdentity identity) =>
        new(identity.Value);

    private static BrowserVocabularyIdentity ToBrowserIdentity(
        VocabularyIdentity identity) =>
        new(
            ToBrowserIdentity(identity.Catalog),
            identity.Value);

    private static BrowserVocabularyTermIdentity ToBrowserIdentity(
        VocabularyTermIdentity identity) =>
        new(
            ToBrowserIdentity(identity.Vocabulary),
            identity.Value);

}
