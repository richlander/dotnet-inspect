using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Sections;
using ILInspector.CSharp;
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

        string memberName = options.MemberFilter.Single();
        if (memberName.Contains('*', StringComparison.Ordinal)
            || memberName.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        return ResolveCanonicalMethodName(type, memberName) is not null
            && (options.Tree || !options.FormatFlagExplicitlySet);
    }

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
            ResolveCanonicalMethodName(
                type,
                options.MemberFilter.Single())
            ?? throw new InvalidOperationException(
                "The native MemberGroup route requires one unambiguous "
                + "ordinary method name.");
        var plan = new MemberOverloadPopulationInspectionPlan(
            new MemberGroupSubject(definition, memberName),
            new MemberOverloadPopulationRequest(
                new MemberOverloadCountRequest(),
                new MemberOverloadRowsRequest(
                    maximumRows: s_bounds.MaxMembers),
                MemberOverloadAccessibilityFilter.Public,
                MemberOverloadReceiverFilter.All,
                includeHidden: false),
            s_bounds);
        MemberDocumentationAttachmentRequest? documentation =
            options.ShowDocs
                && RequestsReturnedRowDocumentation(options)
                ? new(DocumentationDemand.CompiledXml)
                : null;
        InspectionEnvelope<MemberGroupDocumentInspectionOutcome>? inspection =
            documentation is null
                ? await ExactLibraryInspectionExecutor.ExecuteAsync<
                    InspectionEnvelope<
                        MemberGroupDocumentInspectionOutcome>>(
                    assemblyPath,
                    "member group document",
                    session =>
                        session.ExecuteMemberGroupDocument(
                            plan,
                            cancellationToken),
                    cancellationToken)
                : await ExactLibraryInspectionExecutor.ExecuteComposedAsync<
                    InspectionEnvelope<
                        MemberGroupDocumentInspectionOutcome>>(
                    assemblyPath,
                    "member group document",
                    session =>
                        session.ExecuteMemberGroupDocumentAsync(
                            plan,
                            documentation,
                            cancellationToken),
                    includeCompiledDocumentation: true,
                    cancellationToken: cancellationToken);
        if (inspection is null)
            return 1;

        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            CommandError.WriteLine(
                $"{diagnostic.Code}: {diagnostic.Summary}");
        }
        if (inspection.Content
            is not MemberGroupDocumentInspectionOutcome.Available available)
        {
            CommandError.Write(Describe(inspection.Content));
            return 1;
        }

        MemberGroupDocument document = available.Document;
        if (document.Overloads.Count
                is not MemberOverloadCountOutcome.Counted count)
        {
            CommandError.Write(
                "The member-group document did not produce its requested Count.");
            return 1;
        }
        if (document.Overloads.Rows
                is not MemberOverloadRowsOutcome.Read rows)
        {
            CommandError.Write(
                DescribeRows(document.Overloads.Rows));
            return 1;
        }
        if (rows.Continuation is not null
            || rows.Items.Length != count.Value)
        {
            CommandError.Write(
                "The member-group document did not contain its complete overload population.");
            return 1;
        }

        string displayType =
            CSharpIdentifier.ContainRenderedText(type.FullName);
        string displayMember =
            CSharpIdentifier.ContainRenderedText(document.Subject.Name);
        string suffix = count.Value == 1 ? "overload" : "overloads";
        Console.WriteLine(
            $"method {displayType}.{displayMember} "
                + $"({count.Value} {suffix})");
        var writer = new MarkoutWriter(
            Console.Out,
            new MarkdownFormatter());
        if (document.ReturnedRowDocumentation.IsEmpty)
        {
            writer.WriteTree(
            [
                .. rows.Items.Select(row =>
                    new TreeNode(
                        CSharpIdentifier.ContainRenderedText(
                            $"{row.Accessibility} "
                                + ReceiverPrefix(row.Receiver)
                                + row.DisplaySignature))),
            ]);
        }
        else
        {
            IReadOnlyDictionary<int, MemberDocumentationAttachment>
                attachmentsByOrdinal =
                    document.ReturnedRowDocumentation.ToDictionary(
                        static attachment =>
                            attachment.Subject.BaselineOrdinal);
            writer.WriteTable(
                ["Signature", "Description"],
                ["signature", "description"],
                [
                    .. rows.Items.Select(row =>
                        new string[]
                        {
                            MarkoutInline.Code(
                                CSharpIdentifier.ContainRenderedText(
                                    $"{row.Accessibility} "
                                        + ReceiverPrefix(row.Receiver)
                                        + row.DisplaySignature)),
                            MemberDocumentOutput
                                .DescribeDocumentation(
                                    attachmentsByOrdinal[
                                        row.BaselineOrdinal]
                                        .Outcome),
                        }),
                ]);
        }
        return 0;
    }

    private static string ReceiverPrefix(MemberReceiver receiver) =>
        receiver switch
        {
            MemberReceiver.This => string.Empty,
            MemberReceiver.Static => "static ",
            MemberReceiver.Extension => "extension ",
            _ => throw new InvalidOperationException(
                $"Unknown member receiver '{receiver}'."),
        };

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
        MemberGroupDocumentInspectionOutcome outcome) =>
        outcome switch
        {
            MemberGroupDocumentInspectionOutcome.Rejected rejected =>
                $"The member-group document was rejected ({rejected.Reason}).",
            MemberGroupDocumentInspectionOutcome.Incomplete incomplete =>
                $"The member-group document reached {incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            MemberGroupDocumentInspectionOutcome.Failed failed =>
                $"The member-group document failed ({failed.Reason}).",
            _ => "The member-group document returned an unknown outcome.",
        };

    private static string DescribeRows(MemberOverloadRowsOutcome? outcome) =>
        outcome switch
        {
            null =>
                "The member-group document did not produce its requested Rows.",
            MemberOverloadRowsOutcome.Rejected rejected =>
                $"The member-group Rows were rejected ({rejected.Reason}).",
            MemberOverloadRowsOutcome.Incomplete incomplete =>
                $"The member-group Rows reached {incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            MemberOverloadRowsOutcome.Failed failed =>
                $"The member-group Rows failed ({failed.Reason}).",
            _ =>
                "The member-group document returned an unknown Rows outcome.",
        };
}
