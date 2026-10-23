using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class MemberGroupDocumentOutput
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
        bool returnedRowDocumentation =
            RequestsReturnedRowDocumentation(options);
        if (type.DefinitionName is null
            || options.MemberFilter.Count != 1
            || options.OverloadIndex.HasValue
            || !string.IsNullOrWhiteSpace(options.MemberDigest)
            || options.MemberGenericArity.HasValue
            || options.KindFilter.Count > 0
            || options.IncludeAll
            || options.UnsafeOnly
            || options.IncludeSections is { Count: > 0 }
            || !plan.Intent.Sections.Selectors.IsEmpty
            || plan.Intent.Sections.SelectDefault
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
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 }
                && !returnedRowDocumentation
            || plan.Selection.Catalog
                != InspectionCatalogIdentity.ApiMemberOverload)
        {
            return false;
        }

        return ResolveSubject(type, options) is not null
            && (options.Tree || !options.FormatFlagExplicitlySet);
    }

    internal static async Task<int> WriteAsync(
        ApiType type,
        MemberOptions options,
        string assemblyPath,
        CancellationToken cancellationToken,
        MemberGroupSubject? resolvedSubject = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        MemberGroupSubject subject =
            resolvedSubject
            ?? ResolveSubject(type, options)
            ?? throw new InvalidOperationException(
                "The native MemberGroup route requires one unambiguous "
                + "ordinary method name.");
        MemberDocumentationAttachmentRequest? documentation =
            options.ShowDocs
                && RequestsReturnedRowDocumentation(options)
                ? new(DocumentationDemand.CompiledXml)
                : null;
        return documentation is null
            ? await WriteResolvedAsync(
                type,
                subject,
                assemblyPath,
                cancellationToken)
                .ConfigureAwait(false)
            : await WriteDocumentedAsync(
                type,
                subject,
                assemblyPath,
                documentation,
                cancellationToken)
                .ConfigureAwait(false);
    }

    private static async Task<int> WriteResolvedAsync(
        ApiType type,
        MemberGroupSubject subject,
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        var plan = new MemberDocumentResolutionPlan(
            subject,
            s_bounds);
        InspectionEnvelope<MemberDocumentResolutionOutcome>? inspection =
            await ExactLibraryInspectionExecutor.ExecuteAsync<
                InspectionEnvelope<MemberDocumentResolutionOutcome>>(
                assemblyPath,
                "member group document",
                session =>
                session.ExecuteMemberDocumentResolution(
                    plan,
                    cancellationToken),
                cancellationToken);
        if (inspection is null)
            return 1;

        WriteDiagnostics(inspection.Diagnostics);
        switch (inspection.Content)
        {
            case MemberDocumentResolutionOutcome.Exact exact:
                MemberGroupPresentation.WriteTree(
                    exact.Document,
                    type.FullName,
                    Console.Out);
                return 0;
            case MemberDocumentResolutionOutcome.Overview overview:
                MemberGroupPresentation.WriteTree(
                    overview.Document,
                    type.FullName,
                    Console.Out);
                return 0;
            default:
                CommandError.Write(Describe(inspection.Content));
                return 1;
        }
    }

    private static async Task<int> WriteDocumentedAsync(
        ApiType type,
        MemberGroupSubject subject,
        string assemblyPath,
        MemberDocumentationAttachmentRequest documentation,
        CancellationToken cancellationToken)
    {
        var plan = new MemberDocumentResolutionPlan(
            subject,
            s_bounds);
        InspectionEnvelope<MemberDocumentAttachmentCompositionResult>?
            inspection =
            await ExactLibraryInspectionExecutor.ExecuteComposedAsync<
                InspectionEnvelope<
                    MemberDocumentAttachmentCompositionResult>>(
                assemblyPath,
                "member group document",
                session =>
                session.ExecuteMemberDocumentAttachmentCompositionAsync(
                    plan,
                    documentation,
                    cancellationToken),
                includeCompiledDocumentation: true,
                cancellationToken: cancellationToken);
        if (inspection is null)
            return 1;
        WriteDiagnostics(inspection.Diagnostics);
        MemberGroupSubject resolvedSubject;
        IReadOnlyList<MemberDeclaration> declarations;
        switch (inspection.Content.Resolution)
        {
            case MemberDocumentResolutionOutcome.Exact exact:
                resolvedSubject = exact.Document.Subject.Group;
                declarations =
                [
                    exact.Document,
                ];
                break;
            case MemberDocumentResolutionOutcome.Overview overview:
                resolvedSubject = overview.Document.Subject;
                declarations = overview.Document.Members;
                break;
            default:
                CommandError.Write(
                    Describe(inspection.Content.Resolution));
                return 1;
        }
        if (inspection.Content.Documentation
                is not MemberDocumentationAttachmentOutcome.Available
                    available)
        {
            CommandError.Write(
                inspection.Content.Documentation is { } attachment
                    ? Describe(attachment)
                    : "The member-group documentation attachment did not settle.");
            return 1;
        }

        Console.WriteLine(
            MemberGroupPresentation.FormatTitle(
                resolvedSubject,
                type.FullName,
                declarations.Count));
        var writer = new MarkoutWriter(
            Console.Out,
            new MarkdownFormatter());
        IReadOnlyDictionary<MemberSubject, MemberDocumentationAttachment>
            attachmentsBySubject =
                available.Attachments.ToDictionary(
                    static attachment =>
                        attachment.Subject);
        writer.WriteTable(
            ["Signature", "Description"],
            ["signature", "description"],
            [
                .. declarations.Select(declaration =>
                    new string[]
                    {
                        MarkoutInline.Code(
                            MemberGroupPresentation.FormatOverload(
                                declaration)),
                        MemberDocumentOutput
                            .DescribeDocumentation(
                                attachmentsBySubject[
                                    declaration.Subject]
                                    .Outcome),
                    }),
            ]);
        return 0;
    }

    private static void WriteDiagnostics(
        IReadOnlyList<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            CommandError.WriteLine(
                $"{diagnostic.Code}: {diagnostic.Summary}");
        }
    }

    internal static string? ResolveCanonicalMethodName(
        ApiType type,
        string requestedName)
    {
        string? canonicalName = null;
        foreach (ApiMember candidate in type.Members.Where(member =>
                     string.Equals(
                         member.Name,
                         requestedName,
                         StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.Equals(
                    candidate.Kind,
                    "method",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            canonicalName ??= candidate.Name;
            if (!string.Equals(
                    canonicalName,
                    candidate.Name,
                    StringComparison.Ordinal))
            {
                return null;
            }
        }

        return canonicalName;
    }

    internal static MemberGroupSubject? ResolveSubject(
        ApiType type,
        MemberOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        if (type.DefinitionName is not { } definition
            || options.MemberFilter.Count != 1
            || options.OverloadIndex.HasValue
            || !string.IsNullOrWhiteSpace(options.MemberDigest)
            || options.MemberGenericArity.HasValue
            || !IsCompatibleMethodKindFilter(options.KindFilter))
        {
            return null;
        }

        string requestedName = options.MemberFilter.Single();
        if (requestedName.Contains('*', StringComparison.Ordinal)
            || requestedName.Contains('?', StringComparison.Ordinal))
        {
            return null;
        }

        string? canonicalName =
            ResolveCanonicalMethodName(type, requestedName);
        return canonicalName is null
            ? null
            : new MemberGroupSubject(definition, canonicalName);
    }

    private static bool IsCompatibleMethodKindFilter(
        HashSet<string> kindFilter) =>
        kindFilter.Count == 0
        || kindFilter.Count == 1
            && kindFilter.Contains("method");

    private static bool RequestsReturnedRowDocumentation(
        MemberOptions options) =>
        options.Columns is { Length: 2 } columns
        && columns.Contains(
            "Signature",
            StringComparer.OrdinalIgnoreCase)
        && columns.Contains(
            "Description",
            StringComparer.OrdinalIgnoreCase);

    private static string Describe(
        MemberDocumentationAttachmentOutcome outcome) =>
        outcome switch
        {
            MemberDocumentationAttachmentOutcome.Failed failed =>
                "The member-group documentation attachment failed "
                    + $"({failed.Reason}).",
            _ =>
                "The member-group documentation attachment returned an unknown outcome.",
        };

    private static string Describe(
        MemberDocumentResolutionOutcome outcome) =>
        outcome switch
        {
            MemberDocumentResolutionOutcome.Rejected rejected =>
                $"The member-group document was rejected ({rejected.Reason}).",
            MemberDocumentResolutionOutcome.Incomplete incomplete =>
                $"The member-group document reached {incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            MemberDocumentResolutionOutcome.Failed failed =>
                $"The member-group document failed ({failed.Reason}).",
            _ => "The member-group document returned an unknown outcome.",
        };

}
