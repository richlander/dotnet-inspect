using System.Reflection;

using CSharpText;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using ILInspector.CSharp;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class TypeDocumentOutput
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    internal static async Task<int?> TryExecuteAsync(
        ApiSourceResult source,
        TypeOptions options,
        CancellationToken cancellationToken)
    {
        if (!CanExecute(options)
            || string.IsNullOrWhiteSpace(source.TypeName)
            || ParseType(source.TypeName)
                is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return null;
        }

        string? assemblyPath =
            ApiServices.FindApiDll(
                source.SearchPath,
                source.Context.Logger);
        if (assemblyPath is null)
            return 1;

        TypeMemberGroupAccessibilityFilter accessibility =
            options.IncludeAll
                ? TypeMemberGroupAccessibilityFilter.All
                : TypeMemberGroupAccessibilityFilter.Public;
        TypeDocumentInspectionPlan plan =
            options.Count
                ? TypeDocumentInspectionPlans.DeclaredMemberCount(
                    valid.Name,
                    s_bounds,
                    TypeMemberGroupSpelling.CSharp,
                    accessibility,
                    includeHidden: options.IncludeAll)
                : TypeDocumentInspectionPlans.DeclaredMemberRows(
                    valid.Name,
                    s_bounds,
                    TypeMemberGroupSpelling.CSharp,
                    accessibility,
                    includeHidden: options.IncludeAll,
                    s_bounds.MaxMembers);
        InspectionEnvelope<TypeDocumentInspectionOutcome>? inspection =
            await ExactLibraryInspectionExecutor.ExecuteAsync<
                InspectionEnvelope<TypeDocumentInspectionOutcome>>(
                assemblyPath,
                "Type document",
                session =>
                    session.ExecuteTypeDocument(
                        plan,
                        cancellationToken),
                cancellationToken);
        if (inspection is null)
            return 1;

        if (inspection.Content
            is TypeDocumentInspectionOutcome.Rejected
            {
                Reason:
                    TypeDocumentInspectionRejection.TypeNotFound
                    or TypeDocumentInspectionRejection.TypeAmbiguous,
            })
        {
            return null;
        }

        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            CommandError.WriteLine(
                $"{diagnostic.Code}: {diagnostic.Summary}");
        }
        if (inspection.Content
            is not TypeDocumentInspectionOutcome.Available available)
        {
            CommandError.Write(Describe(inspection.Content));
            return 1;
        }
        if (available.Document.Declarations
            is not TypeDocumentDeclarations.Available declarations)
        {
            CommandError.Write(
                Describe(available.Document.Declarations));
            return 1;
        }

        if (options.Count)
            return WriteCount(declarations.Population);

        return WriteTree(
            available.Document.Subject,
            declarations.Population);
    }

    private static bool CanExecute(TypeOptions options) =>
        !string.IsNullOrWhiteSpace(options.TypeName)
        && string.IsNullOrWhiteSpace(options.TypeFilter)
        && !options.EffectiveDiscovery
        && !options.HasSectionQuery
        && (options.IncludeSections is not { Count: > 0 }
            || options.CountDefaultPopulation)
        && (options.AssemblyPath is null
            || options.Count
            || options.Tree)
        && options.MemberFilter.Count == 0
        && options.KindFilter.Count == 0
        && !options.UnsafeOnly
        && !options.ShowDocs
        && !options.ShowSamples
        && !options.PreferRenderedUrls
        && options.Rows is null
        && !options.Limit.HasValue
        && !options.MemberLimit.HasValue
        && !options.PerformanceTriage.HasFilters
        && !options.BodyKindQuery.HasFilter
        && !options.CloneCandidateQuery.HasPredicates
        && !options.RequestReadableLocalNames
        && options.SourceRepositories.Length == 0
        && options.DllPath is null
        && options.PdbPath is null
        && !options.JsonOutput
        && !options.EnvelopeOutput
        && !options.Tabular
        && !options.Tsv
        && !options.Jsonl
        && !options.PlainText
        && !options.MermaidOutput
        && !options.EmbeddedMermaid
        && !options.Print
        && !options.Value
        && !options.Urls
        && !options.Paths
        && options.Columns is not { Length: > 0 }
        && options.Fields is not { Length: > 0 }
        && !options.Schema
        && !options.NoHeader
        && (options.Count
            || options.Tree
            || !options.FormatFlagExplicitlySet);

    private static MetadataTypeDefinitionNameResult ParseType(
        string typeName)
    {
        string normalized =
            FqnParser.NormalizeTypeName(typeName.Trim());
        return MetadataTypeDefinitionName.ParseSerialized(normalized);
    }

    private static int WriteCount(
        TypeMemberGroupPopulationResult population)
    {
        if (population.Composition is not { } composition)
        {
            CommandError.Write(
                "The Type document did not produce its requested "
                    + "Composition Count.");
            return 1;
        }

        int count = population.Binding.Accessibility switch
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

    private static int WriteTree(
        TypeSubject subject,
        TypeMemberGroupPopulationResult population)
    {
        if (population.Rows
            is not TypeMemberGroupRowsOutcome.Read
            {
                IsComplete: true,
            } rows)
        {
            CommandError.Write(
                DescribeRows(population.Rows));
            return 1;
        }

        Console.WriteLine(TypeHeading(subject));
        var nodes = new List<TreeNode>();
        foreach (IGrouping<MemberGroupCategory, TypeMemberGroupShape>
            category in rows.Items
                .GroupBy(static row => row.Binding.Category)
                .OrderBy(static group => CategoryOrder(group.Key)))
        {
            int count = category.Sum(static row =>
                row.ExactMemberCount
                    ?? throw new InvalidOperationException(
                        "The native Type tree requires nested exact-member "
                            + "Counts."));
            nodes.Add(
                new TreeNode($"{CategoryLabel(category.Key)} ({count})")
                {
                    Children =
                    [
                        .. category.Select(static row =>
                        {
                            int exactCount =
                                row.ExactMemberCount!.Value;
                            string name =
                                OperatorNames.FormatDisplayName(
                                    row.Binding.Name.ToString());
                            string label = exactCount == 1
                                ? name
                                : $"{name} ({exactCount} overloads)";
                            return new TreeNode(
                                CSharpIdentifier.ContainRenderedText(
                                    label));
                        }),
                    ],
                });
        }

        var writer = new MarkoutWriter(
            Console.Out,
            new MarkdownFormatter());
        writer.WriteTree([.. nodes]);
        return 0;
    }

    private static string TypeHeading(TypeSubject subject)
    {
        string modifiers =
            subject.Category == MetadataTypeDeclarationCategory.Class
            && (subject.Attributes & TypeAttributes.Abstract) != 0
            && (subject.Attributes & TypeAttributes.Sealed) != 0
                ? "static "
                : string.Empty;
        string category =
            subject.Category.ToString().ToLowerInvariant();
        string name = subject.Type.ToEscapedFullName();
        if (!subject.Signature.GenericParameters.IsEmpty)
        {
            int arityMarker = name.LastIndexOf('`');
            if (arityMarker >= 0)
                name = name[..arityMarker];
            name += "<"
                + string.Join(
                    ", ",
                    subject.Signature.GenericParameters.Select(
                        static parameter =>
                            parameter.Name.ToString()))
                + ">";
        }
        return CSharpIdentifier.ContainRenderedText(
            $"{modifiers}{category} {name}");
    }

    private static int CategoryOrder(MemberGroupCategory category) =>
        category switch
        {
            MemberGroupCategory.Constructor => 0,
            MemberGroupCategory.Property => 1,
            MemberGroupCategory.Field => 2,
            MemberGroupCategory.Event => 3,
            MemberGroupCategory.Method => 4,
            MemberGroupCategory.Operator => 5,
            MemberGroupCategory.Finalizer => 6,
            MemberGroupCategory.ExplicitInterfaceImplementation => 7,
            _ => 8,
        };

    private static string CategoryLabel(MemberGroupCategory category) =>
        category switch
        {
            MemberGroupCategory.Method => "Methods",
            MemberGroupCategory.Constructor => "Constructors",
            MemberGroupCategory.Operator => "Operators",
            MemberGroupCategory.Finalizer => "Finalizers",
            MemberGroupCategory.ExplicitInterfaceImplementation =>
                "Explicit Interface Implementations",
            MemberGroupCategory.Property => "Properties",
            MemberGroupCategory.Field => "Fields",
            MemberGroupCategory.Event => "Events",
            _ => throw new InvalidOperationException(
                $"Unknown Member-group category '{category}'."),
        };

    private static string Describe(
        TypeDocumentInspectionOutcome outcome) =>
        outcome switch
        {
            TypeDocumentInspectionOutcome.Rejected rejected =>
                $"The Type document was rejected ({rejected.Reason}).",
            TypeDocumentInspectionOutcome.Incomplete incomplete =>
                $"The Type document reached {incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            TypeDocumentInspectionOutcome.Failed failed =>
                $"The Type document failed ({failed.Reason}).",
            _ => "The Type document was unavailable.",
        };

    private static string Describe(
        TypeDocumentDeclarations declarations) =>
        declarations switch
        {
            TypeDocumentDeclarations.Rejected rejected =>
                $"The Type Member population was rejected "
                    + $"({rejected.Reason}).",
            TypeDocumentDeclarations.Incomplete incomplete =>
                $"The Type Member population reached "
                    + $"{incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            TypeDocumentDeclarations.Failed failed =>
                $"The Type Member population failed ({failed.Reason}).",
            TypeDocumentDeclarations.NotRequested =>
                "The Type Member population was not requested.",
            _ => "The Type Member population was unavailable.",
        };

    private static string DescribeRows(
        TypeMemberGroupRowsOutcome? rows) =>
        rows switch
        {
            TypeMemberGroupRowsOutcome.Rejected rejected =>
                $"The Type Member rows were rejected ({rejected.Reason}).",
            TypeMemberGroupRowsOutcome.Incomplete incomplete =>
                $"The Type Member rows reached {incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            TypeMemberGroupRowsOutcome.Failed failed =>
                $"The Type Member rows failed ({failed.Reason}).",
            TypeMemberGroupRowsOutcome.Read =>
                "The Type Member rows did not contain the complete "
                    + "population.",
            _ => "The Type document did not produce its requested rows.",
        };
}
