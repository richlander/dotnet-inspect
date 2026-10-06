using System.Collections.Immutable;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

public sealed record ResolvedMemberGroupExplanationBasis
{
    public ResolvedMemberGroupExplanationBasis(
        ResolvedInspectionSource source,
        MemberGroupSubject subject,
        ViewFacetId? defaultFacet,
        InspectionSemanticDemand semanticDemand)
    {
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        DefaultFacet = defaultFacet;
        SemanticDemand = semanticDemand
            ?? throw new ArgumentNullException(nameof(semanticDemand));
    }

    public ResolvedInspectionSource Source { get; }
    public MemberGroupSubject Subject { get; }
    public ViewFacetId? DefaultFacet { get; }
    public InspectionSemanticDemand SemanticDemand { get; }
}

public static class MemberContextualExplanationOperation
{
    private static readonly ExplanationOwnerIdentity Owner =
        new("member");
    private static readonly ExplanationSchemaIdentity SchemaIdentity =
        new(Owner, "contextual-explanation");
    private static readonly ExplanationSchemaVersion Version = new(1);
    private static readonly ExplanationResourceTypeIdentity CommandType =
        new(SchemaIdentity, "command");
    private static readonly ExplanationResourceTypeIdentity MemberGroupType =
        new(SchemaIdentity, "member-group");
    private static readonly ExplanationResourceTypeIdentity ExactMemberType =
        new(SchemaIdentity, "exact-member");
    private static readonly ExplanationResourceTypeIdentity OperationType =
        new(SchemaIdentity, "related-operation");
    private static readonly ImmutableArray<ExplanationSchema> Schemas =
    [
        .. ResourceExplanationVocabulary.Schemas,
        CreateSchema(),
    ];

    public static InspectionEnvelope<ResourceExplanationDocument>
        ExplainCommand(IEnumerable<string> defaultSections)
    {
        ArgumentNullException.ThrowIfNull(defaultSections);
        var path = new ResourcePath("member");
        ExplanationResourceSnapshot root =
            ExplanationConformance.CreateSnapshot(
                Schemas,
                Key(CommandType, "member"),
                Version,
                ExplanationSnapshotScope.Installed,
                [ResourceExplanationVocabulary.Address(path)],
                CommonFacts(
                    CommandType,
                    name: "member",
                    title: "Explain member",
                    context: "Member command",
                    typeName: null,
                    library: null,
                    package: null,
                    framework: null,
                    defaultView: null,
                    selectedSections: [.. defaultSections]),
                relationships: []);
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.Create(Schemas, [root]);
        ResourcePathResolution.Resolved resolved =
            catalog.Resolve(path.Value)
                as ResourcePathResolution.Resolved
            ?? throw new InvalidOperationException(
                "The Member command explanation resource was not registered.");
        return catalog.Explain(
            resolved,
            new(
                depth: 0,
                resourceLimit: 1,
                relationshipLimit: 1));
    }

    public static InspectionEnvelope<ResourceExplanationDocument>
        ExplainMemberGroup(
            ResolvedMemberGroupExplanationBasis basis,
            IEnumerable<string> defaultSections)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(defaultSections);

        string typeName = basis.Subject.DeclaringType.ToMetadataFullName();
        string subject = $"{basis.Subject.Category} / {basis.Subject.Role}";
        ExplanationResourceSnapshot root = DetachedSnapshot(
            Key(
                MemberGroupType,
                $"{SourceIdentity(basis.Source)}|{typeName}|"
                    + $"{basis.Subject.Name}|{subject}"),
            [
                .. CommonFacts(
                    MemberGroupType,
                    basis.Subject.Name,
                    $"Explain {typeName}.{basis.Subject.Name}",
                    "MemberGroup",
                    typeName,
                    basis.Source.LibraryKey,
                    FormatPackage(basis.Source),
                    basis.Source.Framework,
                    basis.DefaultFacet?.Value,
                    SelectSections(
                        basis.SemanticDemand,
                        defaultSections)),
                TextFact(MemberGroupType, "subject", subject),
                TextFact(
                    MemberGroupType,
                    "group-name",
                    basis.Subject.Name),
                TextFact(
                    MemberGroupType,
                    "group-category",
                    basis.Subject.Category.ToString()),
                TextFact(
                    MemberGroupType,
                    "group-role",
                    basis.Subject.Role.ToString()),
            ],
            RelatedOperations(MemberGroupType));
        return Explain(root, OperationSnapshots());
    }

    public static InspectionEnvelope<ResourceExplanationDocument>
        ExplainExactMember(
            ResolvedMemberInspectionBasis basis,
            IEnumerable<string> defaultSections)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(defaultSections);

        string typeName = basis.Target.TypeName;
        string selector = basis.Target.Member.StableSelector;
        ExplanationResourceSnapshot root = DetachedSnapshot(
            Key(
                ExactMemberType,
                $"{SourceIdentity(basis.Source)}|{typeName}|"
                    + basis.Target.Member.Fingerprint),
            [
                .. CommonFacts(
                    ExactMemberType,
                    selector,
                    $"Explain {typeName}.{selector}",
                    "Exact Member",
                    typeName,
                    basis.Source.LibraryKey,
                    FormatPackage(basis.Source),
                    basis.Source.Framework,
                    basis.DefaultFacet?.Value,
                    SelectSections(
                        basis.SemanticDemand,
                        defaultSections)),
                TextFact(
                    ExactMemberType,
                    "subject",
                    basis.Target.Member.CanonicalSignature),
                TextFact(
                    ExactMemberType,
                    "stable-selector",
                    selector),
                TextFact(
                    ExactMemberType,
                    "canonical-signature",
                    basis.Target.Member.CanonicalSignature),
                TextFact(
                    ExactMemberType,
                    "fingerprint",
                    basis.Target.Member.Fingerprint),
            ],
            RelatedOperations(ExactMemberType));
        return Explain(root, OperationSnapshots());
    }

    private static InspectionEnvelope<ResourceExplanationDocument> Explain(
        ExplanationResourceSnapshot root,
        IEnumerable<ExplanationResourceSnapshot> related) =>
        ResourceExplanationCatalog.ExplainDetached(
            Schemas,
            root,
            related,
            new(
                depth: 1,
                resourceLimit: 32,
                relationshipLimit: 32),
            "member-contextual-explanation");

    private static ExplanationResourceSnapshot DetachedSnapshot(
        ExplanationResourceKey key,
        IEnumerable<ExplanationFactObservation> facts,
        IEnumerable<ExplanationRelationshipObservation> relationships) =>
        ExplanationConformance.CreateSnapshot(
            Schemas,
            key,
            Version,
            ExplanationSnapshotScope.Detached,
            addresses: [],
            facts,
            relationships);

    private static ImmutableArray<ExplanationFactObservation> CommonFacts(
        ExplanationResourceTypeIdentity type,
        string name,
        string title,
        string context,
        string? typeName,
        string? library,
        string? package,
        string? framework,
        string? defaultView,
        ImmutableArray<string> selectedSections) =>
    [
        TextFact(type, "name", name),
        TextFact(type, "title", title),
        TextFact(type, "context", context),
        OptionalTextFact(type, "type-name", typeName),
        OptionalTextFact(type, "library", library),
        OptionalTextFact(type, "package", package),
        OptionalTextFact(type, "framework", framework),
        OptionalTextFact(type, "default-view", defaultView),
        TextsFact(type, "selected-content", selectedSections),
    ];

    private static ImmutableArray<ExplanationResourceSnapshot>
        OperationSnapshots() =>
    [
        .. MemberRelatedOperationAffordances.All.Select(operation =>
            DetachedSnapshot(
                Key(OperationType, operation.Id.Value),
                [
                    TextFact(
                        OperationType,
                        "name",
                        operation.Title),
                    TextFact(
                        OperationType,
                        "identity",
                        operation.Id.Value),
                    TextFact(
                        OperationType,
                        "summary",
                        operation.Summary),
                ],
                relationships: [])),
    ];

    private static ImmutableArray<ExplanationRelationshipObservation>
        RelatedOperations(ExplanationResourceTypeIdentity sourceType) =>
    [
        ResourceExplanationVocabulary.Targets(
            sourceType,
            "related-operation",
            MemberRelatedOperationAffordances.All.Select(operation =>
                Key(OperationType, operation.Id.Value))),
    ];

    private static ImmutableArray<string> SelectSections(
        InspectionSemanticDemand demand,
        IEnumerable<string> defaultSections) =>
        demand.Sections.IsEmpty
            ? [.. defaultSections]
            : demand.Sections;

    private static string? FormatPackage(
        ResolvedInspectionSource source) =>
        source.Package is { } packageAsset
            ? $"{packageAsset.PackageId}@{packageAsset.PackageVersion}"
            : null;

    private static string SourceIdentity(
        ResolvedInspectionSource source) =>
        $"{FormatPackage(source) ?? "platform"}|{source.LibraryKey}|"
            + (source.Framework ?? "");

    private static ExplanationResourceKey Key(
        ExplanationResourceTypeIdentity type,
        string value) =>
        new(Owner, type, ResourceExplanationVocabulary.Text(value));

    private static ExplanationFactObservation TextFact(
        ExplanationResourceTypeIdentity type,
        string identity,
        string value) =>
        ResourceExplanationVocabulary.TextFact(type, identity, value);

    private static ExplanationFactObservation OptionalTextFact(
        ExplanationResourceTypeIdentity type,
        string identity,
        string? value) =>
        value is null
            ? ResourceExplanationVocabulary.AbsentFact(type, identity)
            : TextFact(type, identity, value);

    private static ExplanationFactObservation TextsFact(
        ExplanationResourceTypeIdentity type,
        string identity,
        IEnumerable<string> values) =>
        ResourceExplanationVocabulary.TextsFact(type, identity, values);

    private static ExplanationSchema CreateSchema()
    {
        ExplanationResourceTypeDeclaration command =
            Type(
                CommandType,
                "Member command",
                "One installed explanation of the Member command.",
                CommonFactDeclarations(CommandType),
                addressed: true);
        ExplanationResourceTypeDeclaration group =
            Type(
                MemberGroupType,
                "Member group",
                "One detached explanation of a resolved MemberGroup.",
                [
                    .. CommonFactDeclarations(MemberGroupType),
                    RequiredText(
                        MemberGroupType,
                        "subject",
                        "Subject"),
                    RequiredText(
                        MemberGroupType,
                        "group-name",
                        "Group name"),
                    RequiredText(
                        MemberGroupType,
                        "group-category",
                        "Group category"),
                    RequiredText(
                        MemberGroupType,
                        "group-role",
                        "Group role"),
                ],
                relatedOperations: true);
        ExplanationResourceTypeDeclaration exact =
            Type(
                ExactMemberType,
                "Exact Member",
                "One detached explanation of a resolved exact Member.",
                [
                    .. CommonFactDeclarations(ExactMemberType),
                    RequiredText(
                        ExactMemberType,
                        "subject",
                        "Subject"),
                    RequiredText(
                        ExactMemberType,
                        "stable-selector",
                        "Stable selector"),
                    RequiredText(
                        ExactMemberType,
                        "canonical-signature",
                        "Canonical signature"),
                    RequiredText(
                        ExactMemberType,
                        "fingerprint",
                        "Fingerprint"),
                ],
                relatedOperations: true);
        ExplanationResourceTypeDeclaration operation =
            Type(
                OperationType,
                "Related operation",
                "One owner-issued operation related to a Member subject.",
                [
                    RequiredText(OperationType, "name", "Name"),
                    RequiredText(
                        OperationType,
                        "identity",
                        "Identity"),
                    RequiredText(
                        OperationType,
                        "summary",
                        "Summary"),
                ]);
        return new(
            SchemaIdentity,
            Version,
            dataShapes: [],
            resourceTypes: [command, group, exact, operation],
            addressKinds: []);
    }

    private static ImmutableArray<ExplanationFactDeclaration>
        CommonFactDeclarations(
            ExplanationResourceTypeIdentity type) =>
    [
        RequiredText(type, "name", "Name"),
        RequiredText(type, "title", "Title"),
        RequiredText(type, "context", "Context"),
        OptionalText(type, "type-name", "Type name"),
        OptionalText(type, "library", "Library"),
        OptionalText(type, "package", "Package"),
        OptionalText(type, "framework", "Framework"),
        OptionalText(type, "default-view", "Default view"),
        ManyText(type, "selected-content", "Selected content"),
    ];

    private static ExplanationResourceTypeDeclaration Type(
        ExplanationResourceTypeIdentity type,
        string displayName,
        string meaning,
        IEnumerable<ExplanationFactDeclaration> facts,
        bool relatedOperations = false,
        bool addressed = false) =>
        new(
            type,
            displayName,
            meaning,
            ResourceExplanationVocabulary.TextShape,
            facts,
            relatedOperations
                ?
                [
                    new(
                        ResourceExplanationVocabulary.Relationship(
                            type,
                            "related-operation"),
                        "Related operation",
                        "An operation related to the contextual Member subject.",
                        OperationType,
                        ExplanationCardinality.OrderedMany,
                        ExplanationObservationStates.Available),
                ]
                : [],
            addressKinds:
                addressed
                    ?
                    [
                        ResourceExplanationVocabulary
                            .ResourcePathAddressKind,
                    ]
                    : []);

    private static ExplanationFactDeclaration RequiredText(
        ExplanationResourceTypeIdentity type,
        string identity,
        string displayName) =>
        new(
            ResourceExplanationVocabulary.Fact(type, identity),
            displayName,
            $"The {displayName.ToLowerInvariant()} fact.",
            ResourceExplanationVocabulary.TextShape,
            ExplanationCardinality.RequiredOne,
            ExplanationObservationStates.Available);

    private static ExplanationFactDeclaration OptionalText(
        ExplanationResourceTypeIdentity type,
        string identity,
        string displayName) =>
        new(
            ResourceExplanationVocabulary.Fact(type, identity),
            displayName,
            $"The optional {displayName.ToLowerInvariant()} fact.",
            ResourceExplanationVocabulary.TextShape,
            ExplanationCardinality.OptionalOne,
            ExplanationObservationStates.Available
                | ExplanationObservationStates.Absent);

    private static ExplanationFactDeclaration ManyText(
        ExplanationResourceTypeIdentity type,
        string identity,
        string displayName) =>
        new(
            ResourceExplanationVocabulary.Fact(type, identity),
            displayName,
            $"The ordered {displayName.ToLowerInvariant()} facts.",
            ResourceExplanationVocabulary.TextShape,
            ExplanationCardinality.OrderedMany,
            ExplanationObservationStates.Available,
            maximumValueCount: 64);
}
