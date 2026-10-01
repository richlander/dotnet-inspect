using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.CSharp;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class MemberDocumentOutput
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    internal static bool IsSelected(
        ApiType type,
        MemberOptions options,
        ResolvedMemberInspectionPlan plan)
    {
        bool hasOrdinal = options.OverloadIndex.HasValue;
        bool hasFingerprint =
            !string.IsNullOrWhiteSpace(options.MemberDigest);
        if (type.DefinitionName is null
            || options.MemberFilter.Count != 1
            || hasOrdinal == hasFingerprint
            || options.MemberGenericArity.HasValue
            || options.KindFilter.Count > 0
            || options.IncludeAll
            || options.UnsafeOnly
            || options.IncludeSections is { Count: > 0 }
            || options.Select is { Length: > 0 }
            || options.SelectDefault
            || options.Rows is not null
            || options.Limit.HasValue
            || options.Count
            || options.Print
            || options.Value
            || options.Urls
            || options.Paths
            || options.JsonOutput
            || options.Tabular
            || options.Jsonl
            || options.PlainText
            || options.MermaidOutput
            || options.EmbeddedMermaid
            || options.EnvelopeOutput
            || options.NoHeader
            || options.Schema
            || options.ShapeOutput
            || options.SourceParts
            || options.SourcePart is not null
            || options.ShowSamples
            || options.ShareFormat is not null
            || options.EffectiveDiscovery
            || options.UserVerbosity >= Verbosity.Detailed
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 }
            || options.Tree
            || options.FormatFlagExplicitlySet
            || plan.Selection.Catalog
                != InspectionCatalogIdentity.ApiMemberDetail)
        {
            return false;
        }

        string memberName = options.MemberFilter.Single();
        return !memberName.Contains('*', StringComparison.Ordinal)
            && !memberName.Contains('?', StringComparison.Ordinal)
            && MemberGroupDocumentOutput.ResolveCanonicalMethodName(
                type,
                memberName) is not null;
    }

    internal static bool IsSourceSelected(
        ApiType type,
        MemberOptions options,
        ResolvedMemberInspectionPlan plan) =>
        options.IncludeSections is { Count: 1 } sections
        && sections.Contains(SectionNames.Source)
        && options.MemberSourceInspection is not null
        && IsSelected(
            type,
            options with
            {
                IncludeSections = null,
            },
            plan);

    internal static async Task<int> WriteAsync(
        ApiType type,
        MemberOptions options,
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        MetadataTypeDefinitionName definition =
            type.DefinitionName
            ?? throw new ArgumentException(
                "An exact metadata Type definition is required.",
                nameof(type));
        string memberName =
            MemberGroupDocumentOutput.ResolveCanonicalMethodName(
                type,
                options.MemberFilter.Single())
            ?? throw new InvalidOperationException(
                "The native Member route requires one unambiguous ordinary "
                    + "method name.");
        MemberDocumentSelector selector =
            options.OverloadIndex is { } ordinal
                ? new(baselineOrdinal: ordinal)
                : new(fingerprintPrefix: options.MemberDigest);
        var plan = new MemberDocumentInspectionPlan(
            new MemberGroupSubject(definition, memberName),
            selector,
            s_bounds,
            documentation:
                options.ShowDocs
                    ? new(DocumentationDemand.CompiledXml)
                    : null,
            source:
                options.IncludeSections is { Count: 1 } sections
                    && sections.Contains(SectionNames.Source)
                    && options.MemberSourceInspection is not null
                    ? new()
                    : null);
        MemberSourceAttachmentProvider? sourceProvider =
            plan.Source is null
                ? null
                : (subject, _, _) =>
                    ValueTask.FromResult(
                        new MemberSourceAttachment(
                            subject,
                            options.MemberSourceInspection!.Content));
        InspectionEnvelope<MemberDocumentInspectionOutcome>? inspection =
            plan.Documentation is null && plan.Source is null
                ? await ExactLibraryInspectionExecutor.ExecuteAsync<
                    InspectionEnvelope<MemberDocumentInspectionOutcome>>(
                    assemblyPath,
                    "Member document",
                    session =>
                        session.ExecuteMemberDocument(
                            plan,
                            cancellationToken),
                    cancellationToken)
                : await ExactLibraryInspectionExecutor.ExecuteComposedAsync<
                    InspectionEnvelope<MemberDocumentInspectionOutcome>>(
                    assemblyPath,
                    "Member document",
                    session =>
                        session.ExecuteMemberDocumentAsync(
                            plan,
                            sourceProvider,
                            cancellationToken),
                    cancellationToken);
        if (inspection is null)
            return 1;

        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            CommandError.WriteLine(
                $"{diagnostic.Code}: {diagnostic.Summary}");
        }
        if (inspection.Content
            is not MemberDocumentInspectionOutcome.Available available)
        {
            CommandError.Write(Describe(inspection.Content));
            return 1;
        }

        MemberDocument document = available.Document;
        if (document.Source is { } source)
            return WriteSource(source.Outcome);

        string signature =
            $"{document.Accessibility} "
                + ReceiverPrefix(document.Receiver)
                + document.DisplaySignature;
        var writer = new MarkoutWriter(
            Console.Out,
            new MarkdownFormatter(),
            new MarkoutWriterOptions
            {
                SectionOrder = ["Signature", "Documentation"],
            });
        writer.WriteHeading(2, "Signature");
        writer.WriteTable(
            ["Signature", "Digest", "Canonical Signature"],
            ["signature", "digest", "canonical_signature"],
            [
                [
                    MarkoutInline.Code(
                        CSharpIdentifier.ContainRenderedText(signature)),
                    MarkoutInline.Code(
                        document.Subject.Fingerprint.ToString()),
                    MarkoutInline.Code(
                        document.CanonicalSignature.ToString()),
                ],
            ]);
        if (document.Documentation is { } documentation)
        {
            writer.WriteHeading(2, "Documentation");
            writer.WriteParagraph(
                DescribeDocumentation(documentation.Outcome));
        }
        writer.Flush();
        return 0;
    }

    private static int WriteSource(
        AssemblyMemberSourceEntry outcome)
    {
        if (outcome
            is not AssemblyMemberSourceEntry.Available available)
        {
            CommandError.Write(
                outcome switch
                {
                    AssemblyMemberSourceEntry.Rejected rejected =>
                        $"Member source was rejected "
                            + $"({rejected.Failure.Kind}).",
                    AssemblyMemberSourceEntry.Unavailable unavailable =>
                        unavailable.Failure.Detail,
                    _ => "Member source returned an unknown outcome.",
                });
            return 1;
        }

        MarkoutSerializer.Serialize(
            new ExactMemberSourceView(
                new CodeSection(
                    "csharp",
                    available.Source.Text)),
            Console.Out,
            new MarkdownFormatter(),
            ExactMemberSourceViewContext.Default);
        return 0;
    }

    private static string ReceiverPrefix(MemberReceiver receiver) =>
        receiver switch
        {
            MemberReceiver.This => string.Empty,
            MemberReceiver.Static => "static ",
            MemberReceiver.Extension => "extension ",
            _ => throw new InvalidOperationException(
                $"Unknown Member receiver '{receiver}'."),
        };

    private static string Describe(
        MemberDocumentInspectionOutcome outcome) =>
        outcome switch
        {
            MemberDocumentInspectionOutcome.Rejected rejected =>
                $"The Member document was rejected ({rejected.Reason}).",
            MemberDocumentInspectionOutcome.Incomplete incomplete =>
                $"The Member document reached {incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            MemberDocumentInspectionOutcome.Failed failed =>
                $"The Member document failed ({failed.Reason}).",
            _ => "The Member document returned an unknown outcome.",
        };

    internal static string DescribeDocumentation(
        DocumentationQueryOutcome outcome) =>
        outcome switch
        {
            DocumentationQueryOutcome.Completed completed =>
                completed.Fields.Summary.Contributions
                    .Select(static contribution => contribution.Value)
                    .FirstOrDefault()
                    ?? DescribeCompiledDocumentation(
                        completed.CompiledXml),
            DocumentationQueryOutcome.RequestRejected rejected =>
                $"Documentation was rejected ({rejected.Reason}).",
            DocumentationQueryOutcome.Failed failed =>
                $"Documentation failed ({failed.Reason}).",
            DocumentationQueryOutcome.Incomplete incomplete =>
                $"Documentation was incomplete ({incomplete.Reason}).",
            _ => throw new InvalidOperationException(
                "Unknown Member documentation outcome."),
        };

    private static string DescribeCompiledDocumentation(
        CompiledDocumentationOutcome? outcome) =>
        outcome switch
        {
            CompiledDocumentationOutcome.Available =>
                "No documentation summary was found.",
            CompiledDocumentationOutcome.Absent =>
                "No compiled documentation was found.",
            CompiledDocumentationOutcome.Unavailable =>
                "Compiled documentation is unavailable.",
            CompiledDocumentationOutcome.Ambiguous =>
                "Compiled documentation was ambiguous.",
            CompiledDocumentationOutcome.ContributionsRejected =>
                "Compiled documentation contributions were rejected.",
            CompiledDocumentationOutcome.MalformedOrUnreadableDocument =>
                "Compiled documentation was malformed or unreadable.",
            CompiledDocumentationOutcome.Incomplete incomplete =>
                $"Compiled documentation was incomplete ({incomplete.Reason}).",
            CompiledDocumentationOutcome.RequestRejected rejected =>
                $"Compiled documentation was rejected ({rejected.Reason}).",
            CompiledDocumentationOutcome.ContentAccessFailed =>
                "Compiled documentation content could not be read.",
            null => "No documentation summary was found.",
            _ => throw new InvalidOperationException(
                "Unknown compiled documentation outcome."),
        };
}

internal sealed record ExactMemberSourceView(
    [property: MarkoutSection(Name = SectionNames.Source)]
    CodeSection Source);

[MarkoutContext(typeof(ExactMemberSourceView))]
internal partial class ExactMemberSourceViewContext :
    MarkoutSerializerContext;
