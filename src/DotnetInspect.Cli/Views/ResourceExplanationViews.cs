using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Inline)]
public sealed class ResourceExplanationView
{
    [MarkoutIgnore]
    public string Title
    {
        get;
        init => field = LibraryViewText.Contain(value);
    } = "";

    public string Kind
    {
        get;
        init => field = LibraryViewText.Contain(value);
    } = "";

    public string Name
    {
        get;
        init => field = LibraryViewText.Contain(value);
    } = "";

    public string Owner
    {
        get;
        init => field = LibraryViewText.Contain(value);
    } = "";

    public string? ItemKind
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    [MarkoutJoin(", ")]
    public List<string> Formats { get; init; } = [];

    public int? Members { get; init; }

    [MarkoutSection(Name = "Expanded Resources")]
    [MarkoutIgnoreColumnWhen(
        nameof(ItemKindsEmpty),
        nameof(ResourceExplanationResourceRow.ItemKind))]
    [MarkoutIgnoreColumnWhen(
        nameof(FormatsEmpty),
        nameof(ResourceExplanationResourceRow.Formats))]
    public List<ResourceExplanationResourceRow> ExpandedResources
    {
        get;
        init;
    } = [];

    [MarkoutSection(Name = "Related Resources")]
    [MarkoutIgnoreColumnWhen(
        nameof(SourcesUniform),
        nameof(ResourceExplanationRelationshipRow.Source))]
    public List<ResourceExplanationRelationshipRow> Relationships
    {
        get;
        init;
    } = [];

    [MarkoutSection(Name = "Traversal")]
    public List<ResourceExplanationTraversalRow> Traversal { get; init; } = [];

    public static ResourceExplanationView Create(
        ResourceExplanationDocument document)
    {
        var pathsByIdentity =
            document.Resources.ToDictionary(
                static resource => resource.Identity,
                static resource => resource.Path.Value);
        ResourceExplanationResource root = document.Resources[0];
        ResourceExplanationResourceRow rootRow =
            ResourceExplanationResourceRow.Create(root);
        return new ResourceExplanationView
        {
            Title = $"Explain {document.RequestedPath.Value}",
            Kind = rootRow.Kind,
            Name = rootRow.Name,
            Owner = rootRow.Owner,
            ItemKind = rootRow.ItemKind,
            Formats = rootRow.Formats,
            Members = rootRow.Members,
            ExpandedResources =
            [
                .. document.Resources.Skip(1).Select(
                    ResourceExplanationResourceRow.Create),
            ],
            Relationships =
            [
                .. document.Relationships.Select(relationship =>
                    ResourceExplanationRelationshipRow.Create(
                        relationship,
                        pathsByIdentity.GetValueOrDefault(
                            relationship.Source))),
            ],
            Traversal =
                document.Traversal.RequestedDepth == 0
                && document.Traversal.Completeness
                    == ResourceExplanationCompleteness.Complete
                    ? []
                    :
                    [
                        ResourceExplanationTraversalRow.Create(
                            document.Traversal),
                    ],
        };
    }

    public static bool ItemKindsEmpty(
        List<ResourceExplanationResourceRow>? rows) =>
        rows is null || rows.All(static row => row.ItemKind is null);

    public static bool FormatsEmpty(
        List<ResourceExplanationResourceRow>? rows) =>
        rows is null || rows.All(static row => row.Formats.Count == 0);

    public static bool SourcesUniform(
        List<ResourceExplanationRelationshipRow>? rows) =>
        rows is null
        || rows.Select(static row => row.Source)
            .Distinct(StringComparer.Ordinal)
            .Count() <= 1;
}

[MarkoutSerializable]
public sealed record ResourceExplanationResourceRow(
    string Path,
    string Kind,
    string Name,
    string Owner,
    string? ItemKind,
    List<string> Formats,
    int? Members)
{
    public string Path { get; init; } = LibraryViewText.Contain(Path);

    public string Kind { get; init; } = LibraryViewText.Contain(Kind);

    public string Name { get; init; } = LibraryViewText.Contain(Name);

    public string Owner { get; init; } = LibraryViewText.Contain(Owner);

    public string? ItemKind { get; init; } =
        LibraryViewText.Contain(ItemKind);

    [MarkoutJoin(", ")]
    public List<string> Formats { get; init; } =
        [.. Formats.Select(format => LibraryViewText.Contain(format))];

    public int? Members { get; init; } = Members;

    public static ResourceExplanationResourceRow Create(
        ResourceExplanationResource resource)
    {
        string name;
        string? itemKind = null;
        IEnumerable<DiscoveryOutputMode> outputModes = [];
        int? memberCount;
        switch (resource.Details)
        {
            case ResourceExplanationDetail.CatalogDetails details:
                name = details.Name;
                memberCount = details.EntryCount;
                break;
            case ResourceExplanationDetail.NavigationCollectionDetails details:
                name = details.Name;
                memberCount = details.MemberCount;
                break;
            case ResourceExplanationDetail.StructuralCategoryDetails details:
                name = details.Name;
                outputModes = details.OutputModes;
                memberCount = details.MemberCount;
                break;
            case ResourceExplanationDetail.StructuralSectionDetails details:
                name = details.Name;
                outputModes = details.OutputModes;
                memberCount = details.MemberCount;
                break;
            case ResourceExplanationDetail.StructuralItemDetails details:
                name = details.Name;
                itemKind = details.ItemKind;
                memberCount = null;
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Resource Explanation detail variant.");
        }

        return new(
            resource.Path.Value,
            Display(resource.ResourceKind),
            name,
            Display(resource.Owner),
            itemKind,
            [.. outputModes.Select(Display)],
            memberCount);
    }

    private static string Display<T>(T value)
        where T : struct, Enum =>
        value.ToString()
            .Replace(
                nameof(ResourceExplanationResourceKind.NavigationCollection),
                "Navigation collection",
                StringComparison.Ordinal)
            .Replace(
                nameof(ResourceExplanationResourceKind.StructuralCategory),
                "Structural category",
                StringComparison.Ordinal)
            .Replace(
                nameof(ResourceExplanationResourceKind.StructuralSection),
                "Structural section",
                StringComparison.Ordinal)
            .Replace(
                nameof(ResourceExplanationResourceKind.StructuralItem),
                "Structural item",
                StringComparison.Ordinal)
            .Replace(
                nameof(ResourceExplanationOwner.ResourceExplanation),
                "Resource Explanation",
                StringComparison.Ordinal)
            .Replace(
                nameof(ResourceExplanationOwner.SchemaQuery),
                "Schema Query",
                StringComparison.Ordinal);
}

[MarkoutSerializable]
public sealed record ResourceExplanationRelationshipRow(
    string Source,
    string Relationship,
    string Target,
    string TargetOwner)
{
    public string Source { get; init; } =
        LibraryViewText.Contain(Source);

    public string Relationship { get; init; } =
        LibraryViewText.Contain(Relationship);

    public string Target { get; init; } =
        LibraryViewText.Contain(Target);

    public string TargetOwner { get; init; } =
        LibraryViewText.Contain(TargetOwner);

    public static ResourceExplanationRelationshipRow Create(
        ResourceExplanationRelationship relationship,
        string? sourcePath) =>
        new(
            sourcePath ?? "(external)",
            Display(relationship.RelationshipKind),
            relationship.TargetPath?.Value ?? "(not navigable)",
            relationship.TargetOwner switch
            {
                ResourceExplanationOwner.ResourceExplanation =>
                    "Resource Explanation",
                ResourceExplanationOwner.SchemaQuery => "Schema Query",
                _ => relationship.TargetOwner.ToString(),
            });

    private static string Display<T>(T value)
        where T : struct, Enum
    {
        string text = value.ToString();
        var result = new System.Text.StringBuilder(text.Length + 4);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (index > 0 && char.IsUpper(character))
                result.Append(' ');
            result.Append(
                index == 0
                    ? char.ToUpperInvariant(character)
                    : char.ToLowerInvariant(character));
        }
        return result.ToString();
    }
}

[MarkoutSerializable]
public sealed record ResourceExplanationTraversalRow(
    int RequestedDepth,
    int CompletedDepth,
    int Resources,
    int Relationships,
    string Completeness,
    List<string> TruncatedBy)
{
    public string Completeness { get; init; } =
        LibraryViewText.Contain(Completeness);

    [MarkoutJoin(", ")]
    public List<string> TruncatedBy { get; init; } =
        [.. TruncatedBy.Select(reason => LibraryViewText.Contain(reason))];

    public static ResourceExplanationTraversalRow Create(
        ResourceExplanationTraversalReceipt receipt) =>
        new(
            receipt.RequestedDepth,
            receipt.CompletedDepth,
            receipt.VisitedResourceCount,
            receipt.EmittedRelationshipCount,
            receipt.Completeness.ToString(),
            [.. receipt.TruncationReasons.Select(static reason =>
                reason switch
                {
                    ResourceExplanationTruncationReason.ResourceLimit =>
                        "resource limit",
                    ResourceExplanationTruncationReason.RelationshipLimit =>
                        "relationship limit",
                    _ => reason.ToString().ToLowerInvariant(),
                })]);
}

[MarkoutContext(typeof(ResourceExplanationView))]
[MarkoutContext(typeof(ResourceExplanationResourceRow))]
[MarkoutContext(typeof(ResourceExplanationRelationshipRow))]
[MarkoutContext(typeof(ResourceExplanationTraversalRow))]
public partial class ResourceExplanationViewContext :
    MarkoutSerializerContext;
