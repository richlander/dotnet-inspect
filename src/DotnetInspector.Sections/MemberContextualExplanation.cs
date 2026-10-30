using System.Collections.Immutable;
using System.Globalization;
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

public sealed record ResolvedMemberExplanationBasis
{
    public ResolvedMemberExplanationBasis(
        ResolvedInspectionSource source,
        MemberDocumentResolutionOutcome resolution,
        ViewFacetId? defaultFacet,
        InspectionSemanticDemand semanticDemand,
        IEnumerable<string> defaultSections)
    {
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
        Resolution = resolution
            ?? throw new ArgumentNullException(nameof(resolution));
        if (resolution
            is not (MemberDocumentResolutionOutcome.Overview
                or MemberDocumentResolutionOutcome.Exact))
        {
            throw new ArgumentException(
                "A resolved Member explanation requires an available "
                    + "Member overview or exact Member document.",
                nameof(resolution));
        }

        DefaultFacet = defaultFacet;
        SemanticDemand = semanticDemand
            ?? throw new ArgumentNullException(nameof(semanticDemand));
        ArgumentNullException.ThrowIfNull(defaultSections);
        DefaultSections = [.. defaultSections];
    }

    public ResolvedInspectionSource Source { get; }
    public MemberDocumentResolutionOutcome Resolution { get; }
    public ViewFacetId? DefaultFacet { get; }
    public InspectionSemanticDemand SemanticDemand { get; }
    public ImmutableArray<string> DefaultSections { get; }
}

public static class MemberContextualExplanationOperation
{
    private static readonly ExplanationOwnerIdentity Owner =
        new("member");
    private static readonly ExplanationSchemaIdentity SchemaIdentity =
        new(Owner, "contextual-explanation");
    private static readonly ExplanationSchemaVersion Version = new(2);
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
        ExplainResolvedMember(ResolvedMemberExplanationBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);

        return basis.Resolution switch
        {
            MemberDocumentResolutionOutcome.Overview overview =>
                ExplainMemberOverview(basis, overview.Document),
            MemberDocumentResolutionOutcome.Exact exact =>
                ExplainMemberDocument(basis, exact.Document),
            _ => throw new InvalidOperationException(
                "A resolved Member explanation basis contained a non-success outcome."),
        };
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
                TextFact(
                    MemberGroupType,
                    "group-spelling",
                    basis.Subject.Spelling.ToString()),
                .. PopulationFacts(MemberGroupType, population: null),
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
                .. ExactCorrespondenceFacts(subject: null),
            ],
            RelatedOperations(ExactMemberType));
        return Explain(root, OperationSnapshots());
    }

    private static InspectionEnvelope<ResourceExplanationDocument>
        ExplainMemberOverview(
            ResolvedMemberExplanationBasis basis,
            MemberOverviewDocument document)
    {
        MemberGroupSubject group = document.Subject;
        string typeName = group.DeclaringType.ToMetadataFullName();
        string subject =
            $"{group.Category} / {group.Role} / {group.Spelling}";
        ExplanationResourceSnapshot root = DetachedSnapshot(
            Key(
                MemberGroupType,
                ResolvedIdentity(
                    basis.Source,
                    document.Population,
                    exactMetadataToken: null)),
            [
                .. CommonFacts(
                    MemberGroupType,
                    group.Name,
                    $"Explain {typeName}.{group.Name}",
                    "MemberGroup",
                    typeName,
                    basis.Source.LibraryKey,
                    FormatPackage(basis.Source),
                    basis.Source.Framework,
                    basis.DefaultFacet?.Value,
                    SelectSections(
                        basis.SemanticDemand,
                        basis.DefaultSections)),
                TextFact(MemberGroupType, "subject", subject),
                TextFact(
                    MemberGroupType,
                    "group-name",
                    group.Name),
                TextFact(
                    MemberGroupType,
                    "group-category",
                    group.Category.ToString()),
                TextFact(
                    MemberGroupType,
                    "group-role",
                    group.Role.ToString()),
                TextFact(
                    MemberGroupType,
                    "group-spelling",
                    group.Spelling.ToString()),
                .. PopulationFacts(
                    MemberGroupType,
                    document.Population),
            ],
            RelatedOperations(MemberGroupType));
        return Explain(root, OperationSnapshots());
    }

    private static InspectionEnvelope<ResourceExplanationDocument>
        ExplainMemberDocument(
            ResolvedMemberExplanationBasis basis,
            MemberDocument document)
    {
        MemberSubject subject = document.Subject;
        string typeName =
            subject.Group.DeclaringType.ToMetadataFullName();
        string selector = subject.Anchor.StableSelector;
        ExplanationResourceSnapshot root = DetachedSnapshot(
            Key(
                ExactMemberType,
                ResolvedIdentity(
                    basis.Source,
                    subject.Population,
                    subject.MetadataToken)),
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
                        basis.DefaultSections)),
                TextFact(
                    ExactMemberType,
                    "subject",
                    document.CanonicalSignature.ToString()),
                TextFact(
                    ExactMemberType,
                    "stable-selector",
                    selector),
                TextFact(
                    ExactMemberType,
                    "canonical-signature",
                    document.CanonicalSignature.ToString()),
                TextFact(
                    ExactMemberType,
                    "fingerprint",
                    subject.Fingerprint.ToString()),
                .. ExactCorrespondenceFacts(subject),
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

    private static string ResolvedIdentity(
        ResolvedInspectionSource source,
        MemberOverloadPopulationBinding population,
        int? exactMetadataToken) =>
        Identity(
            FormatPackage(source) ?? "platform",
            source.LibraryKey,
            source.Framework,
            population.Assembly.Name.ToString(),
            population.Assembly.Version.ToString(),
            population.Assembly.Culture?.ToString(),
            population.Assembly.PublicKeyToken?.ToString(),
            population.ModuleVersionId.ToString("N"),
            HexToken(population.TypeDefinitionToken),
            population.Name,
            population.Category.ToString(),
            population.Role.ToString(),
            population.Spelling.ToString(),
            population.Ordering.ToString(),
            population.Accessibility.ToString(),
            population.Receiver.ToString(),
            population.IncludeHidden.ToString(),
            exactMetadataToken is { } token
                ? HexToken(token)
                : null);

    private static string Identity(params string?[] components)
    {
        var identity = new System.Text.StringBuilder();
        foreach (string? component in components)
        {
            if (component is null)
            {
                identity.Append("-;");
                continue;
            }

            identity
                .Append(
                    component.Length.ToString(
                        CultureInfo.InvariantCulture))
                .Append(':')
                .Append(component)
                .Append(';');
        }

        return identity.ToString();
    }

    private static ImmutableArray<ExplanationFactObservation>
        ExactCorrespondenceFacts(MemberSubject? subject) =>
    [
        OptionalTextFact(
            ExactMemberType,
            "group-name",
            subject?.Group.Name),
        OptionalTextFact(
            ExactMemberType,
            "group-category",
            subject?.Group.Category.ToString()),
        OptionalTextFact(
            ExactMemberType,
            "group-role",
            subject?.Group.Role.ToString()),
        OptionalTextFact(
            ExactMemberType,
            "group-spelling",
            subject?.Group.Spelling.ToString()),
        .. PopulationFacts(
            ExactMemberType,
            subject?.Population),
        OptionalTextFact(
            ExactMemberType,
            "metadata-token",
            subject is null
                ? null
                : HexToken(subject.MetadataToken)),
        OptionalTextFact(
            ExactMemberType,
            "baseline-ordinal",
            subject?.BaselineOrdinal.ToString(
                CultureInfo.InvariantCulture)),
        OptionalTextFact(
            ExactMemberType,
            "documentation-id",
            subject?.DocumentationId.ToString()),
    ];

    private static ImmutableArray<ExplanationFactObservation>
        PopulationFacts(
            ExplanationResourceTypeIdentity type,
            MemberOverloadPopulationBinding? population) =>
    [
        OptionalTextFact(
            type,
            "population-assembly-name",
            population?.Assembly.Name.ToString()),
        OptionalTextFact(
            type,
            "population-assembly-version",
            population?.Assembly.Version.ToString()),
        OptionalTextFact(
            type,
            "population-assembly-culture",
            population?.Assembly.Culture?.ToString()),
        OptionalTextFact(
            type,
            "population-public-key-token",
            population?.Assembly.PublicKeyToken?.ToString()),
        OptionalTextFact(
            type,
            "population-module-version-id",
            population?.ModuleVersionId.ToString("D")),
        OptionalTextFact(
            type,
            "population-type-definition-token",
            population is null
                ? null
                : HexToken(population.TypeDefinitionToken)),
        OptionalTextFact(
            type,
            "population-ordering",
            population?.Ordering.ToString()),
        OptionalTextFact(
            type,
            "population-accessibility",
            population?.Accessibility.ToString()),
        OptionalTextFact(
            type,
            "population-receiver",
            population?.Receiver.ToString()),
        OptionalTextFact(
            type,
            "population-include-hidden",
            population?.IncludeHidden.ToString()),
    ];

    private static string HexToken(int token) =>
        token.ToString("X8", CultureInfo.InvariantCulture);

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
                    RequiredText(
                        MemberGroupType,
                        "group-spelling",
                        "Group spelling"),
                    .. PopulationFactDeclarations(MemberGroupType),
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
                    OptionalText(
                        ExactMemberType,
                        "group-name",
                        "Group name"),
                    OptionalText(
                        ExactMemberType,
                        "group-category",
                        "Group category"),
                    OptionalText(
                        ExactMemberType,
                        "group-role",
                        "Group role"),
                    OptionalText(
                        ExactMemberType,
                        "group-spelling",
                        "Group spelling"),
                    .. PopulationFactDeclarations(ExactMemberType),
                    OptionalText(
                        ExactMemberType,
                        "metadata-token",
                        "Metadata token"),
                    OptionalText(
                        ExactMemberType,
                        "baseline-ordinal",
                        "Baseline ordinal"),
                    OptionalText(
                        ExactMemberType,
                        "documentation-id",
                        "Documentation identity"),
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
        PopulationFactDeclarations(
            ExplanationResourceTypeIdentity type) =>
    [
        OptionalText(type, "population-assembly-name", "Population assembly name"),
        OptionalText(
            type,
            "population-assembly-version",
            "Population assembly version"),
        OptionalText(
            type,
            "population-assembly-culture",
            "Population assembly culture"),
        OptionalText(
            type,
            "population-public-key-token",
            "Population public key token"),
        OptionalText(
            type,
            "population-module-version-id",
            "Population module version identity"),
        OptionalText(
            type,
            "population-type-definition-token",
            "Population Type definition token"),
        OptionalText(type, "population-ordering", "Population ordering"),
        OptionalText(
            type,
            "population-accessibility",
            "Population accessibility"),
        OptionalText(type, "population-receiver", "Population receiver"),
        OptionalText(
            type,
            "population-include-hidden",
            "Population hidden admission"),
    ];

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
