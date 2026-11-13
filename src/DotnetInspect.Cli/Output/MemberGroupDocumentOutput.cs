using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class MemberGroupDocumentOutput
{
    internal static bool RequestsCompatibilityDocumentation(MemberOptions options) =>
        !options.Count
        && (options.IncludeSections is null
            || options.IncludeSections.Any(static section =>
                section is SectionNames.Values
                    or SectionNames.Constructors
                    or SectionNames.Finalizer
                    or SectionNames.Fields
                    or SectionNames.Properties
                    or SectionNames.Methods
                    or SectionNames.Operators
                    or SectionNames.ExplicitInterfaceImplementations
                    or SectionNames.ExtensionMethods
                    or SectionNames.Events));

    internal static bool RequestsOverloadSections(ApiOptions options) =>
        options.IncludeSections?.Any(
            static section => section is SectionNames.Overloads
                or SectionNames.DocumentedOverloads) == true;

    internal static bool IsDefaultReportSelected(
        ApiType type,
        MemberOptions options,
        ResolvedMemberInspectionPlan plan) =>
        !options.Tree
        && !options.VerbosityExplicitlySet
        && options.Columns is not { Length: > 0 }
        && !options.HasCallerScope
        && IsSelected(type, options, plan);

    internal static async Task<MemberOptions?> AttachSectionsAsync(
        ApiType type,
        MemberOptions options,
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        MemberGroupSubject? subject = ResolveSubject(type, options);
        if (subject is null)
        {
            CommandError.Write(
                "Overload sections require one ordinary method-group name "
                    + "without an ordinal or digest.");
            return null;
        }

        var plan = new MemberDocumentResolutionPlan(
            subject,
            s_bounds,
            accessibility: options.IncludeAll
                ? MemberOverloadAccessibilityFilter.All
                : MemberOverloadAccessibilityFilter.Public,
            includeHidden: options.IncludeAll);
        MemberDocumentResolutionOutcome resolution;
        MemberDocumentationAttachmentOutcome? documentation = null;
        if (options.IncludeSections!.Contains(
                SectionNames.DocumentedOverloads))
        {
            var inspection =
                await ExactLibraryInspectionExecutor.ExecuteComposedAsync<
                    InspectionEnvelope<MemberDocumentAttachmentCompositionResult>>(
                    assemblyPath,
                    "documented overload sections",
                    session =>
                        session.ExecuteMemberDocumentAttachmentCompositionAsync(
                            plan,
                            new(DocumentationDemand.CompiledXml),
                            cancellationToken),
                    includeCompiledDocumentation: true,
                    cancellationToken: cancellationToken);
            if (inspection is null)
                return null;
            WriteDiagnostics(inspection.Diagnostics);
            resolution = inspection.Content.Resolution;
            documentation = inspection.Content.Documentation;
        }
        else
        {
            var inspection =
                await ExactLibraryInspectionExecutor.ExecuteAsync<
                    InspectionEnvelope<MemberDocumentResolutionOutcome>>(
                    assemblyPath,
                    "overload sections",
                    session => session.ExecuteMemberDocumentResolution(
                        plan,
                        cancellationToken),
                    cancellationToken);
            if (inspection is null)
                return null;
            WriteDiagnostics(inspection.Diagnostics);
            resolution = inspection.Content;
        }
        if (resolution is not MemberDocumentResolutionOutcome.Exact
            and not MemberDocumentResolutionOutcome.Overview)
        {
            CommandError.Write(Describe(resolution));
            return null;
        }
        return options with
        {
            OverloadSectionResolution = resolution,
            OverloadSectionDocumentation = documentation,
        };
    }

    internal static List<OverloadRow>? Rows(
        ApiType type,
        MemberOptions options) =>
        DeclarationRows(type, options)?.Select(row => new OverloadRow(
            MarkoutInline.Code(
                $"{row.Selector.SelectorName}:{row.Selector.SelectorIndex}"),
            MarkoutInline.Code(row.Selector.StableSelector),
            MarkoutInline.Code(
                MemberGroupPresentation.FormatOverload(row.Declaration))))
            .ToList();

    internal static List<DocumentedOverloadRow>? DocumentedRows(
        ApiType type,
        MemberOptions options)
    {
        var attachments =
            (options.OverloadSectionDocumentation
                as MemberDocumentationAttachmentOutcome.Available)
                ?.Attachments.ToDictionary(
                    static attachment => attachment.Subject);
        return DeclarationRows(type, options)?.Select(row =>
            new DocumentedOverloadRow(
                MarkoutInline.Code(
                    $"{row.Selector.SelectorName}:{row.Selector.SelectorIndex}"),
                MarkoutInline.Code(row.Selector.StableSelector),
                MarkoutInline.Code(
                    MemberGroupPresentation.FormatOverload(row.Declaration)),
                attachments is not null
                    ? MemberDocumentOutput.DescribeDocumentation(
                        attachments[row.Declaration.Subject].Outcome)
                    : Describe(options.OverloadSectionDocumentation!)))
            .ToList();
    }

    private static IEnumerable<(
        MemberDeclaration Declaration,
        MemberTargetCandidate Selector)>? DeclarationRows(
            ApiType type,
            MemberOptions options)
    {
        IReadOnlyList<MemberDeclaration>? declarations =
            options.OverloadSectionResolution switch
            {
                MemberDocumentResolutionOutcome.Exact exact =>
                    [exact.Document],
                MemberDocumentResolutionOutcome.Overview overview =>
                    overview.Document.Members,
                _ => null,
            };
        if (declarations is null)
            return null;
        var selectors = MemberTargetResolver.GetCandidates(
                type,
                new(options.MemberFilter.Single(),
                    options.MemberFilter.Single()))
            .ToDictionary(static candidate =>
                candidate.Member.MetadataToken!.Value);
        return declarations
            .OrderBy(declaration =>
                selectors[declaration.Subject.MetadataToken].SelectorIndex)
            .Take(options.Limit ?? int.MaxValue)
            .Select(declaration =>
                (declaration, selectors[declaration.Subject.MetadataToken]));
    }

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
        MemberGroupSubject? resolvedSubject = null,
        IReadOnlyList<ResultProperty>? properties = null)
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
        return await WriteResolvedAsync(
                type,
                subject,
                assemblyPath,
                cancellationToken,
                properties ?? [])
                .ConfigureAwait(false);
    }

    private static async Task<int> WriteResolvedAsync(
        ApiType type,
        MemberGroupSubject subject,
        string assemblyPath,
        CancellationToken cancellationToken,
        IReadOnlyList<ResultProperty> properties)
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
        IReadOnlyDictionary<int, int> selectorIndices =
            MemberTargetResolver.GetCandidates(
                    type,
                    new(subject.Name, subject.Name))
                .ToDictionary(
                    static candidate => candidate.Member.MetadataToken!.Value,
                    static candidate => candidate.SelectorIndex);
        switch (inspection.Content)
        {
            case MemberDocumentResolutionOutcome.Exact exact:
                MemberGroupPresentation.WriteTree(
                    exact.Document,
                    type.FullName,
                    Console.Out,
                    properties,
                    selectorIndices);
                return 0;
            case MemberDocumentResolutionOutcome.Overview overview:
                MemberGroupPresentation.WriteTree(
                    overview.Document,
                    type.FullName,
                    Console.Out,
                    properties,
                    selectorIndices);
                return 0;
            default:
                CommandError.Write(Describe(inspection.Content));
                return 1;
        }
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
