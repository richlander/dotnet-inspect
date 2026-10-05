using System.Collections.Immutable;
using System.Reflection;

using CSharpText;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Commands;

internal static class TypeOverviewDocumentTreeCommand
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    internal static bool CanExecute(TypeOptions options)
    {
        bool nativeTree =
            options.Count
            || options.ShapeOutput
            || options.Tree
            || (!options.HasSectionQuery
                && !options.JsonOutput
                && !options.Tabular
                && !options.Tsv
                && !options.Jsonl
                && !options.NoHeader
                && !options.PlainText
                && !options.MarkdownExplicitlySet);
        return nativeTree
            && (!options.HasSectionQuery
                || options.DocumentSelection
                    is ExactTypeDocumentSelection.Overview)
            && options.DocumentSelection
                is not ExactTypeDocumentSelection.Complete
            && !string.IsNullOrWhiteSpace(options.TypeName)
            && !TypeMatcher.IsTypeGlobPattern(options.TypeName)
            && string.IsNullOrWhiteSpace(options.TypeFilter)
            && !options.ShowDocs
            && !options.DocsExplicitlySet
            && !options.ShowSamples
            && !options.PreferRenderedUrls
            && options.MemberFilter.Count == 0
            && options.KindFilter.Count == 0
            && !options.UnsafeOnly
            && !options.Limit.HasValue
            && !options.MemberLimit.HasValue
            && options.Rows is null
            && !options.EffectiveDiscovery
            && !options.Schema
            && !options.EnvelopeOutput
            && !options.MermaidOutput
            && !options.EmbeddedMermaid
            && !options.Print
            && !options.Value
            && !options.Urls
            && !options.Paths
            && options.Columns is not { Length: > 0 }
            && options.Fields is not { Length: > 0 }
            && !options.PerformanceTriage.HasFilters
            && !options.BodyKindQuery.HasFilter
            && !options.CloneCandidateQuery.HasPredicates
            && options.RequestReadableLocalNames is false
            && options.SourceRepositories.Length == 0
            && options.DllPath is null
            && options.PdbPath is null
            && (options.AssemblyPath is null
                || options.Count
                || options.Tree
                || options.ShapeOutput);
    }

    internal static async Task<int?> TryExecuteAsync(
        ApiSourceResult source,
        TypeOptions options,
        CancellationToken cancellationToken)
    {
        if (!CanExecute(options))
            return null;

        string? assemblyPath =
            ApiServices.FindApiDll(
                source.SearchPath,
                source.Context.Logger);
        if (assemblyPath is null)
            return 1;

        TypeOverviewDocumentTreeExecution? execution =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                    assemblyPath,
                    "Type overview document tree",
                    session =>
                        Execute(
                            session,
                            source.TypeName!,
                            options,
                            cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        if (execution is null)
            return 1;
        if (execution is TypeOverviewDocumentTreeExecution.NotMatched)
            return null;
        if (execution
            is not TypeOverviewDocumentTreeExecution.Completed completed)
        {
            return 1;
        }

        return Write(
            completed.Envelope,
            options,
            source.PackageName,
            source.PackageVersion);
    }

    private static TypeOverviewDocumentTreeExecution Execute(
        ExactLibraryInspectionSession session,
        string typeName,
        TypeOptions options,
        CancellationToken cancellationToken)
    {
        LibraryTypeListingResult? listing =
            LibraryTypeListingCommand.ReadRows(
                session,
                new(
                    LibraryTypeDeclarationSelection.Definitions,
                    ApiTypeInventoryKinds.All),
                cancellationToken,
                includeMemberCount: false,
                accessibility:
                    options.IncludeAll
                        ? LibraryTypeAccessibility.All
                        : LibraryTypeAccessibility.Public);
        if (listing is null)
            return new TypeOverviewDocumentTreeExecution.Failed();

        LibraryTypeShape[] matches =
        [
            .. listing.Rows.Where(row =>
                TypeMatcher.MatchesExactTypeName(
                    row.Identity.ToEscapedFullName(),
                    typeName)),
        ];
        if (matches.Length != 1)
            return new TypeOverviewDocumentTreeExecution.NotMatched();

        TypeMemberGroupAccessibilityFilter accessibility =
            options.IncludeAll
                ? TypeMemberGroupAccessibilityFilter.All
                : TypeMemberGroupAccessibilityFilter.Public;
        TypeOverviewDocumentInspectionPlan plan =
            options.Count
                ? TypeOverviewDocumentInspectionPlans.DeclaredMemberCount(
                    matches[0].Identity,
                    s_bounds,
                    TypeMemberGroupSpelling.CSharp,
                    accessibility,
                    includeHidden: options.IncludeAll)
                : TypeOverviewDocumentInspectionPlans.DeclaredMemberRows(
                    matches[0].Identity,
                    s_bounds,
                    TypeMemberGroupSpelling.CSharp,
                    accessibility,
                    includeHidden: options.IncludeAll,
                    s_bounds.MaxMembers);
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>? envelope =
            session.ExecuteTypeOverviewDocument(
                plan,
                cancellationToken);
        return envelope is null
            ? new TypeOverviewDocumentTreeExecution.Failed()
            : new TypeOverviewDocumentTreeExecution.Completed(envelope);
    }

    private static int Write(
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> envelope,
        TypeOptions options,
        string? packageName,
        string? packageVersion)
    {
        if (WriteDiagnostics(envelope.Diagnostics))
            return 1;
        if (envelope.Content
            is not TypeOverviewDocumentInspectionOutcome.Available available)
        {
            WriteDocumentFailure(envelope.Content);
            return 1;
        }
        if (available.Document.Declarations
            is not TypeOverviewDocumentDeclarations.Available declarations)
        {
            WriteDeclarationFailure(
                available.Document.Declarations);
            return 1;
        }
        if (options.Count)
            return WriteCount(declarations.Population);
        if (declarations.Population.Rows
            is not TypeMemberGroupRowsOutcome.Read
            {
                Continuation: null,
            } rows)
        {
            WriteRowsFailure(declarations.Population.Rows);
            return 1;
        }

        string header =
            FormatHeader(
                available.Document.Subject,
                packageName,
                packageVersion);
        Console.WriteLine(CSharpIdentifier.ContainRenderedText(header));
        if (rows.Items.IsEmpty)
            return 0;

        var writer =
            new MarkoutWriter(
                Console.Out,
                new MarkdownFormatter());
        writer.WriteTree(
            [
                .. rows.Items
                    .GroupBy(row => row.Binding.Category)
                    .OrderBy(group => CategoryOrder(group.Key))
                    .Select(group => CategoryNode(group.Key, [.. group])),
            ]);
        return 0;
    }

    private static int WriteCount(
        TypeMemberGroupPopulationResult population)
    {
        if (population.Composition is not { } composition)
        {
            CommandError.Write(
                "The Type overview document did not return its declaration count.");
            return 1;
        }

        int count =
            population.Binding.Accessibility switch
            {
                TypeMemberGroupAccessibilityFilter.Public =>
                    composition.Public,
                TypeMemberGroupAccessibilityFilter.Protected =>
                    composition.Protected,
                TypeMemberGroupAccessibilityFilter.Internal =>
                    composition.Internal,
                TypeMemberGroupAccessibilityFilter.Private =>
                    composition.Private,
                TypeMemberGroupAccessibilityFilter.All =>
                    composition.Public
                        + composition.Protected
                        + composition.Internal
                        + composition.Private,
                _ => throw new InvalidOperationException(
                    "Unknown Type Member accessibility."),
            };
        CountOutput.WriteCount(count);
        return 0;
    }

    private static TreeNode CategoryNode(
        MemberGroupCategory category,
        ImmutableArray<TypeMemberGroupShape> groups)
    {
        int exactMembers =
            groups.Sum(group =>
                group.ExactMemberCount
                ?? throw new InvalidOperationException(
                    "The Type Tree requested exact Member counts."));
        string noun = CategoryLabel(category);
        string label =
            IsOverloadGrouped(category)
            && exactMembers != groups.Length
                ? $"{noun} ({groups.Length} logical, "
                    + $"{exactMembers} overloads)"
                : $"{noun} ({groups.Length})";
        return new TreeNode(label)
        {
            Children =
            [
                .. groups
                    .OrderBy(
                        group => group.Binding.Name.ToString(),
                        StringComparer.Ordinal)
                    .Select(group =>
                    {
                        string name = DisplayName(group);
                        return new TreeNode(
                            IsOverloadGrouped(category)
                            && group.ExactMemberCount > 1
                                ? $"{name} "
                                    + $"({group.ExactMemberCount} overloads)"
                                : name);
                    }),
            ],
        };
    }

    private static string FormatHeader(
        TypeSubject subject,
        string? packageName,
        string? packageVersion)
    {
        string modifiers =
            subject.Category switch
            {
                MetadataTypeDeclarationCategory.Class
                    when subject.Attributes.HasFlag(
                            TypeAttributes.Abstract)
                        && subject.Attributes.HasFlag(
                            TypeAttributes.Sealed) =>
                    "static ",
                MetadataTypeDeclarationCategory.Class
                    when subject.Attributes.HasFlag(
                        TypeAttributes.Abstract) =>
                    "abstract ",
                MetadataTypeDeclarationCategory.Class
                    when subject.Attributes.HasFlag(
                        TypeAttributes.Sealed) =>
                    "sealed ",
                MetadataTypeDeclarationCategory.Struct
                    when subject.IsByRefLike =>
                    "ref ",
                _ => "",
            };
        string kind =
            subject.Category.ToString().ToLowerInvariant();
        string provenance =
            !string.IsNullOrWhiteSpace(packageName)
                ? packageVersion is null
                    ? $" ({packageName})"
                    : $" ({packageName} {packageVersion})"
                : $" ({subject.Assembly.Name})";
        return $"{modifiers}{kind} "
            + $"{FormatTypeName(subject)}{provenance}";
    }

    private static string FormatTypeName(TypeSubject subject)
    {
        var segments = new string[subject.Type.Segments.Length];
        for (int index = 0; index < segments.Length; index++)
        {
            string metadataName = subject.Type.Segments[index];
            int arityMarker =
                metadataName.LastIndexOf('`');
            string name =
                arityMarker < 0
                    ? metadataName
                    : metadataName[..arityMarker];
            string[] parameters =
            [
                .. subject.Signature.GenericParameters
                    .Where(parameter =>
                        parameter.DefinitionSegmentIndex == index)
                    .OrderBy(parameter => parameter.MetadataIndex)
                    .Select(parameter => parameter.Name.ToString()),
            ];
            segments[index] =
                parameters.Length == 0
                    ? name
                    : $"{name}<{string.Join(", ", parameters)}>";
        }

        string prefix =
            string.IsNullOrEmpty(subject.Type.Namespace)
                ? ""
                : subject.Type.Namespace + ".";
        return prefix + string.Join(".", segments);
    }

    private static bool WriteDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        bool hasErrors = false;
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            string message =
                diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(message);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(message);
                    break;
                case InspectionDiagnosticSeverity.Error:
                    CommandError.Write(message);
                    hasErrors = true;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Type overview document diagnostic severity.");
            }
        }

        return hasErrors;
    }

    private static void WriteDocumentFailure(
        TypeOverviewDocumentInspectionOutcome outcome)
    {
        switch (outcome)
        {
            case TypeOverviewDocumentInspectionOutcome.Rejected rejected:
                CommandError.Write(
                    "The Type overview document was rejected.",
                    $"Reason: {rejected.Reason}");
                break;
            case TypeOverviewDocumentInspectionOutcome.Incomplete incomplete:
                CommandError.Write(
                    "The Type overview document exceeded an inspection bound.",
                    [
                        $"Bound: {incomplete.Bound}",
                        $"Limit: {incomplete.Limit}",
                        $"Measured: {incomplete.Measured}",
                    ]);
                break;
            case TypeOverviewDocumentInspectionOutcome.Failed failed:
                CommandError.Write(
                    "The Type overview document failed.",
                    $"Reason: {failed.Reason}");
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Type overview document outcome.");
        }
    }

    private static void WriteDeclarationFailure(
        TypeOverviewDocumentDeclarations declarations)
    {
        switch (declarations)
        {
            case TypeOverviewDocumentDeclarations.NotRequested:
                CommandError.Write(
                    "The Type overview document did not request declarations.");
                break;
            case TypeOverviewDocumentDeclarations.Rejected rejected:
                CommandError.Write(
                    "The Type declaration population was rejected.",
                    $"Reason: {rejected.Reason}");
                break;
            case TypeOverviewDocumentDeclarations.Incomplete incomplete:
                CommandError.Write(
                    "The Type declaration population exceeded an "
                        + "inspection bound.",
                    [
                        $"Bound: {incomplete.Bound}",
                        $"Limit: {incomplete.Limit}",
                        $"Measured: {incomplete.Measured}",
                    ]);
                break;
            case TypeOverviewDocumentDeclarations.Failed failed:
                CommandError.Write(
                    "The Type declaration population failed.",
                    $"Reason: {failed.Reason}");
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Type overview document declarations outcome.");
        }
    }

    private static void WriteRowsFailure(
        TypeMemberGroupRowsOutcome? rows)
    {
        switch (rows)
        {
            case null:
                CommandError.Write(
                    "The Type overview document did not return declaration rows.");
                break;
            case TypeMemberGroupRowsOutcome.Rejected rejected:
                CommandError.Write(
                    "The Type declaration rows were rejected.",
                    $"Reason: {rejected.Reason}");
                break;
            case TypeMemberGroupRowsOutcome.Incomplete incomplete:
                CommandError.Write(
                    "The Type declaration rows exceeded an inspection "
                        + "bound.",
                    [
                        $"Bound: {incomplete.Bound}",
                        $"Limit: {incomplete.Limit}",
                        $"Measured: {incomplete.Measured}",
                    ]);
                break;
            case TypeMemberGroupRowsOutcome.Failed failed:
                CommandError.Write(
                    "The Type declaration rows failed.",
                    $"Reason: {failed.Reason}");
                break;
            case TypeMemberGroupRowsOutcome.Read:
                CommandError.Write(
                    "The Type declaration rows were truncated.");
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Type declaration rows outcome.");
        }
    }

    private static int CategoryOrder(
        MemberGroupCategory category) =>
        category switch
        {
            MemberGroupCategory.Constructor => 0,
            MemberGroupCategory.Finalizer => 1,
            MemberGroupCategory.Field => 2,
            MemberGroupCategory.Property => 3,
            MemberGroupCategory.Method => 4,
            MemberGroupCategory.Operator => 5,
            MemberGroupCategory.ExplicitInterfaceImplementation => 6,
            MemberGroupCategory.Event => 7,
            _ => 8,
        };

    private static string CategoryLabel(
        MemberGroupCategory category) =>
        category switch
        {
            MemberGroupCategory.Constructor => "Constructors",
            MemberGroupCategory.Finalizer => "Finalizer",
            MemberGroupCategory.Field => "Fields",
            MemberGroupCategory.Property => "Properties",
            MemberGroupCategory.Method => "Methods",
            MemberGroupCategory.Operator => "Operators",
            MemberGroupCategory.ExplicitInterfaceImplementation =>
                "Explicit Interface Implementations",
            MemberGroupCategory.Event => "Events",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private static bool IsOverloadGrouped(
        MemberGroupCategory category) =>
        category
            is MemberGroupCategory.Constructor
            or MemberGroupCategory.Method
            or MemberGroupCategory.Operator
            or MemberGroupCategory.ExplicitInterfaceImplementation;

    private static string DisplayName(TypeMemberGroupShape group)
    {
        string name = group.Binding.Name.ToString();
        return group.SharedGenericParameters is { Length: > 0 } parameters
            ? $"{name}<{string.Join(
                ", ",
                parameters.Select(parameter => parameter.ToString()))}>"
            : name;
    }

    private abstract record TypeOverviewDocumentTreeExecution
    {
        private TypeOverviewDocumentTreeExecution()
        {
        }

        internal sealed record Completed(
            InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> Envelope)
            : TypeOverviewDocumentTreeExecution;

        internal sealed record NotMatched
            : TypeOverviewDocumentTreeExecution;

        internal sealed record Failed
            : TypeOverviewDocumentTreeExecution;
    }
}
