using System.Collections.Immutable;
using DotnetInspector.Sections;
using Markout;
using QuerySpace.Explanation;

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

    public string? Shape
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? Cardinality
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? Identity
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? Summary
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? Key
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? SubjectRole
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? ResultGrain
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? Profile
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? ResultContract
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    [MarkoutJoin(", ")]
    public List<string> Operators { get; init; } = [];

    public string? ValueKind
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    [MarkoutJoin(", ")]
    public List<string> Values { get; init; } = [];

    [MarkoutJoin(", ")]
    public List<string> Examples { get; init; } = [];

    [MarkoutJoin(", ")]
    public List<string> AcceptedBy { get; init; } = [];

    [MarkoutJoin("; ")]
    public List<string> Fields { get; init; } = [];

    [MarkoutJoin(", ")]
    public List<string> Defaults { get; init; } = [];

    public string? ValueRows
    {
        get;
        init => field = value is null
            ? null
            : LibraryViewText.Contain(value);
    }

    [MarkoutJoin(", ")]
    public List<string> Effects { get; init; } = [];

    public string? ConsumerKind
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? Gesture
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    public string? ItemKind
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    [MarkoutJoin(", ")]
    public List<string> Formats { get; init; } = [];

    public int? Members { get; init; }

    public string? Context
    {
        get;
        init => field = value is null
            ? null
            : LibraryViewText.Contain(value);
    }

    public string? Subject
    {
        get;
        init => field = value is null
            ? null
            : LibraryViewText.Contain(value);
    }

    public string? Source
    {
        get;
        init => field = value is null
            ? null
            : LibraryViewText.Contain(value);
    }

    public string? DefaultView
    {
        get;
        init => field = value is null
            ? null
            : LibraryViewText.Contain(value);
    }

    [MarkoutJoin(", ")]
    public List<string> SelectedContent { get; init; } = [];

    [MarkoutSection(Name = "Related Operations")]
    public List<MemberExplanationOperationRow> RelatedOperations
    {
        get;
        init;
    } = [];

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
    [MarkoutIgnoreColumnWhen(
        nameof(TargetOwnersEmpty),
        nameof(ResourceExplanationRelationshipRow.TargetOwner))]
    public List<ResourceExplanationRelationshipRow> Relationships
    {
        get;
        init;
    } = [];

    [MarkoutSection(Name = "Traversal")]
    public List<ResourceExplanationTraversalRow> Traversal { get; init; } = [];

    public static ResourceExplanationView Create(
        ResourceExplanationDocument document) =>
        Create(document, context: null);

    public static ResourceExplanationView Create(
        MemberContextualExplanationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        MemberContextualExplanationSubject? subject = document.Subject;
        string? source = subject is null
            ? null
            : string.Join(
                " / ",
                new[]
                {
                    subject.Package,
                    subject.Library,
                    subject.Framework,
                }.Where(static value =>
                    !string.IsNullOrWhiteSpace(value)));
        (string title, string context, string? identity) =
            (document.Kind, subject) switch
            {
                (MemberContextualExplanationKind.Command, null) =>
                    ("Explain member", "Member command", null),
                (
                    MemberContextualExplanationKind.MemberGroup,
                    MemberGroupContextualExplanationSubject group) =>
                    (
                        $"Explain {group.TypeName}.{group.Group.Name}",
                        "MemberGroup",
                        $"{group.Group.Category} / {group.Group.Role}"),
                (
                    MemberContextualExplanationKind.ExactMember,
                    ExactMemberContextualExplanationSubject exact) =>
                    (
                        $"Explain {exact.TypeName}.{exact.StableSelector}",
                        "Exact Member",
                        exact.CanonicalSignature),
                _ => throw new InvalidOperationException(
                    "The contextual Member explanation kind and subject "
                        + "do not agree."),
            };
        return Create(
            document.Resource,
            new(
                title,
                context,
                identity,
                source,
                document.DefaultFacet?.Value,
                [
                    .. document.SelectedSections.Select(
                        section =>
                            LibraryViewText.Contain(section)!),
                ],
                [
                    .. document.RelatedOperations.Select(operation =>
                        new MemberExplanationOperationRow(
                            operation.Id.Value,
                            operation.Summary)),
                ]));
    }

    private static ResourceExplanationView Create(
        ResourceExplanationDocument document,
        ContextualDetails? context)
    {
        ArgumentNullException.ThrowIfNull(document);
        var pathsByIdentity =
            document.Resources.ToDictionary(
                static resource => resource.Key,
                static resource => resource.Path.Value);
        ResourceExplanationResource root = document.Resources[0];
        ResourceExplanationResourceRow rootRow =
            ResourceExplanationResourceRow.Create(root);
        RootDetails details = RootDetails.Create(root);
        return new ResourceExplanationView
        {
            Title =
                context?.Title
                ?? $"Explain {document.RequestedPath.Value}",
            Kind = rootRow.Kind,
            Name = rootRow.Name,
            Owner = rootRow.Owner,
            Shape = details.Shape,
            Cardinality = details.Cardinality,
            Identity = details.Identity,
            Summary = details.Summary,
            Key = details.Key,
            SubjectRole = details.SubjectRole,
            ResultGrain = details.ResultGrain,
            Profile = details.Profile,
            ResultContract = details.ResultContract,
            Operators = details.Operators,
            ValueKind = details.ValueKind,
            Values = details.Values,
            Examples = details.Examples,
            AcceptedBy = details.AcceptedBy ?? [],
            Fields = details.Fields ?? [],
            Defaults = details.Defaults ?? [],
            ValueRows = details.ValueRows,
            Effects = details.Effects,
            ConsumerKind = details.ConsumerKind,
            Gesture = details.Gesture,
            ItemKind = rootRow.ItemKind,
            Formats = rootRow.Formats,
            Members = rootRow.Members,
            Context = context?.Context,
            Subject = context?.Subject,
            Source = context?.Source,
            DefaultView = context?.DefaultView,
            SelectedContent = context?.SelectedContent ?? [],
            RelatedOperations = context?.RelatedOperations ?? [],
            ExpandedResources =
            [
                .. document.Resources.Skip(1).Select(
                    ResourceExplanationResourceRow.Create),
            ],
            Relationships =
                context is not null
                    ? []
                    :
                    [
                        .. document.Relationships.SelectMany(relationship =>
                            ResourceExplanationRelationshipRow.Create(
                                document.Schemas,
                                relationship,
                                pathsByIdentity.GetValueOrDefault(
                                    relationship.Source))),
                    ],
            Traversal =
                context is not null
                || (document.Traversal.RequestedDepth == 0
                    && document.Traversal.Completeness
                        == ResourceExplanationCompleteness.Complete)
                    ? []
                    :
                    [
                        ResourceExplanationTraversalRow.Create(
                            document.Traversal),
                    ],
        };
    }

    private sealed record ContextualDetails(
        string Title,
        string Context,
        string? Subject,
        string? Source,
        string? DefaultView,
        List<string> SelectedContent,
        List<MemberExplanationOperationRow> RelatedOperations);

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

    public static bool TargetOwnersEmpty(
        List<ResourceExplanationRelationshipRow>? rows) =>
        rows is null
        || rows.All(static row => row.TargetOwner.Length == 0);

    private sealed record RootDetails(
        string? Identity,
        string? Summary,
        string? Key,
        string? SubjectRole,
        string? ResultGrain,
        string? Profile,
        string? ResultContract,
        List<string> Operators,
        string? ValueKind,
        List<string> Values,
        List<string> Examples,
        List<string> Effects,
        string? ConsumerKind,
        string? Gesture,
        string? Shape = null,
        string? Cardinality = null,
        List<string>? AcceptedBy = null,
        List<string>? Fields = null,
        List<string>? Defaults = null,
        string? ValueRows = null)
    {
        internal static RootDetails Create(
            ResourceExplanationResource resource)
        {
            string? summary =
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "summary");
            if (resource.ResourceType.Value == "analysis")
            {
                summary =
                    $"Revision "
                    + $"{ResourceExplanationFactView.OptionalInteger(
                        resource,
                        "revision")}; cost "
                    + $"{ResourceExplanationFactView.OptionalText(
                        resource,
                        "cost")}. "
                    + string.Join(
                        "; ",
                        ResourceExplanationFactView.Texts(
                            resource,
                            "participations"))
                    + ".";
            }

            return new(
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "identity"),
                summary,
                ResourceExplanationFactView.OptionalText(resource, "key"),
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "subject-role"),
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "result-grain"),
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "profile"),
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "result-contract"),
                [
                    .. ResourceExplanationFactView.Texts(
                        resource,
                        "operators"),
                ],
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "value-kind"),
                [
                    .. ResourceExplanationFactView.Texts(
                        resource,
                        "values"),
                ],
                [
                    .. ResourceExplanationFactView.Texts(
                        resource,
                        "examples"),
                ],
                [
                    .. ResourceExplanationFactView.Texts(
                        resource,
                        "effects"),
                ],
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "consumer-kind"),
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "gesture"),
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "shape"),
                ResourceExplanationFactView.OptionalText(
                    resource,
                    "cardinality"),
                [
                    .. ResourceExplanationFactView.Texts(
                        resource,
                        "accepted-by"),
                ],
                [
                    .. ResourceExplanationFactView.Texts(
                        resource,
                        "fields"),
                ],
                [
                    .. ResourceExplanationFactView.Texts(
                        resource,
                        "defaults"),
                ],
                resource.ResourceType.Value == "value-vocabulary"
                    ? $"vocabulary -S \"{
                        ResourceExplanationFactView.RequiredText(
                            resource,
                            "name")}\""
                    : null);
        }

        private static RootDetails Empty(
            string? identity = null,
            string? summary = null,
            string? subjectRole = null,
            string? resultGrain = null,
            string? profile = null,
            string? resultContract = null,
            string? consumerKind = null,
            string? gesture = null) =>
            new(
                identity,
                summary,
                null,
                subjectRole,
                resultGrain,
                profile,
                resultContract,
                [],
                null,
                [],
                [],
                [],
                consumerKind,
                gesture);

    }

}

[MarkoutSerializable]
public sealed record MemberExplanationOperationRow
{
    public MemberExplanationOperationRow(
        string identity,
        string summary)
    {
        Identity = LibraryViewText.Contain(identity);
        Summary = LibraryViewText.Contain(summary);
    }

    public string Identity { get; }

    public string Summary { get; }
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
        return new(
            resource.Path.Value,
            DisplayType(resource.ResourceType.Value),
            ResourceExplanationFactView.RequiredText(resource, "name"),
            DisplayOwner(resource.Owner.Value),
            ResourceExplanationFactView.OptionalText(
                resource,
                "item-kind"),
            [
                .. ResourceExplanationFactView.Texts(
                    resource,
                    "formats"),
            ],
            ResourceExplanationFactView.OptionalInteger(
                resource,
                "members"));
    }

    private static string DisplayType(string value) =>
        value == "query-space"
            ? "Query Space"
            : string.Join(
                ' ',
                value.Split('-').Select((segment, index) =>
                    index == 0
                        ? char.ToUpperInvariant(segment[0])
                            + segment[1..]
                        : segment));

    internal static string DisplayOwner(string value) =>
        value switch
        {
            "resource-explanation" => "Resource Explanation",
            "schema-query" => "Schema Query",
            "inspection-capability-composition" =>
                "Inspection Capability Composition",
            "query-space" => "Query Space",
            "consumer" => "Consumer",
            "analysis-requests" => "Analysis Requests",
            "findings" => "Findings",
            "product-vocabulary" => "Product Vocabulary",
            _ => value,
        };
}

[MarkoutSerializable]
public sealed record ResourceExplanationRelationshipRow(
    string Source,
    string Relationship,
    string State,
    string Target,
    string TargetOwner,
    string TargetProjection)
{
    public string Source { get; init; } =
        LibraryViewText.Contain(Source);

    public string Relationship { get; init; } =
        LibraryViewText.Contain(Relationship);

    public string State { get; init; } =
        LibraryViewText.Contain(State);

    public string Target { get; init; } =
        LibraryViewText.Contain(Target);

    public string TargetOwner { get; init; } =
        LibraryViewText.Contain(TargetOwner);

    public string TargetProjection { get; init; } =
        LibraryViewText.Contain(TargetProjection);

    public static IEnumerable<ResourceExplanationRelationshipRow> Create(
        IEnumerable<ExplanationSchema> schemas,
        ResourceExplanationRelationship relationship,
        string? sourcePath)
    {
        ExplanationRelationshipDeclaration declaration =
            schemas.SelectMany(static schema => schema.ResourceTypes)
                .SelectMany(static resource => resource.Relationships)
                .Single(candidate =>
                    candidate.Identity == relationship.Relationship);
        string relationshipDisplay =
            $"{declaration.DisplayName} "
            + $"({relationship.Relationship.Value})";
        if (relationship.Targets.IsEmpty)
        {
            yield return CreateRow(target: null);
            yield break;
        }

        foreach (ResourceExplanationRelationshipTarget target
                 in relationship.Targets)
        {
            yield return CreateRow(target);
        }

        ResourceExplanationRelationshipRow CreateRow(
            ResourceExplanationRelationshipTarget? target) =>
            new(
                sourcePath ?? "(external)",
                relationshipDisplay,
                relationship.State.ToString(),
                target is null ? "(none)" : TargetDisplay(target),
                target is null
                    ? ""
                    : ResourceExplanationResourceRow.DisplayOwner(
                        target.Resource.Owner.Value),
                relationship.TargetCompleteness.ToString());
    }

    private static string TargetDisplay(
        ResourceExplanationRelationshipTarget target)
    {
        ExplanationPublicAddress? address = target.Addresses.FirstOrDefault(
            static candidate =>
                candidate.Kind.Value == "resource-path");
        if (address?.Value is ExplanationValue.Scalar
            {
                Value:
                {
                    Kind: ExplanationScalarKind.Text,
                    Text: { } path,
                },
            })
        {
            return path;
        }
        return target.Resource.IdentityValue is ExplanationValue.Scalar
        {
            Value:
            {
                Kind: ExplanationScalarKind.Text,
                Text: { } identity,
            },
        }
            ? identity
            : "(not navigable)";
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

internal static class ResourceExplanationFactView
{
    internal static string RequiredText(
        ResourceExplanationResource resource,
        string identity) =>
        AvailableValues(resource, identity).Single()
            is ExplanationValue.Scalar
            {
                Value:
                {
                    Kind: ExplanationScalarKind.Text,
                    Text: { } text,
                },
            }
                ? text
                : throw new InvalidOperationException(
                    $"Explanation fact '{identity}' is not text.");

    internal static string? OptionalText(
        ResourceExplanationResource resource,
        string identity)
    {
        ExplanationFactObservation? fact = resource.Facts.FirstOrDefault(
            candidate => candidate.Fact.Value == identity);
        if (fact is null
            || fact.State == ExplanationObservationState.Absent)
        {
            return null;
        }
        return RequiredText(resource, identity);
    }

    internal static int? OptionalInteger(
        ResourceExplanationResource resource,
        string identity)
    {
        ExplanationFactObservation? fact = resource.Facts.FirstOrDefault(
            candidate => candidate.Fact.Value == identity);
        if (fact is null
            || fact.State == ExplanationObservationState.Absent)
        {
            return null;
        }
        return AvailableValues(resource, identity).Single()
            is ExplanationValue.Scalar
            {
                Value:
                {
                    Kind: ExplanationScalarKind.Integer,
                    Integer: { } integer,
                },
            }
                && integer >= int.MinValue
                && integer <= int.MaxValue
                ? (int)integer
                : throw new InvalidOperationException(
                    $"Explanation fact '{identity}' is not an Int32.");
    }

    internal static IEnumerable<string> Texts(
        ResourceExplanationResource resource,
        string identity)
    {
        ExplanationFactObservation? fact = resource.Facts.FirstOrDefault(
            candidate => candidate.Fact.Value == identity);
        if (fact is null
            || fact.State == ExplanationObservationState.Absent)
        {
            return [];
        }
        return AvailableValues(resource, identity).Select(value =>
            value is ExplanationValue.Scalar
            {
                Value:
                {
                    Kind: ExplanationScalarKind.Text,
                    Text: { } text,
                },
            }
                ? text
                : throw new InvalidOperationException(
                    $"Explanation fact '{identity}' contains non-text."));
    }

    private static ImmutableArray<ExplanationValue> AvailableValues(
        ResourceExplanationResource resource,
        string identity)
    {
        ExplanationFactObservation fact = resource.Facts.Single(
            candidate => candidate.Fact.Value == identity);
        if (fact.State != ExplanationObservationState.Available)
        {
            throw new InvalidOperationException(
                $"Explanation fact '{identity}' is '{fact.State}'.");
        }
        return fact.Values;
    }
}

[MarkoutContext(typeof(ResourceExplanationView))]
[MarkoutContext(typeof(ResourceExplanationResourceRow))]
[MarkoutContext(typeof(ResourceExplanationRelationshipRow))]
[MarkoutContext(typeof(ResourceExplanationTraversalRow))]
[MarkoutContext(typeof(MemberExplanationOperationRow))]
public partial class ResourceExplanationViewContext :
    MarkoutSerializerContext;
