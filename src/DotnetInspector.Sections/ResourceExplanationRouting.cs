using System.Collections.Immutable;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

/// <summary>
/// The role of one routing declaration: something a reader inspects, work a
/// reader performs on a subject, or a shared reference family.
/// </summary>
public enum ResourceExplanationRoutingRole
{
    SubjectKind,
    Operation,
    Reference,
}

/// <summary>
/// One owner-issued routing declaration: the hint the explanation root
/// projects for one subject kind, operation, or reference collection. The
/// owner writes every value; Resource Explanation validates and projects it.
/// </summary>
public sealed record ResourceExplanationRoutingDeclaration
{
    public ResourceExplanationRoutingDeclaration(
        ResourceExplanationRoutingRole role,
        string identity,
        string title,
        string issuer,
        string purpose,
        IEnumerable<string> questions,
        IEnumerable<ExplanationResourceKey> destinations,
        string? notYetExplainable = null,
        string? grain = null,
        string? scope = null,
        IEnumerable<string>? acceptedSubjectKinds = null)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!ResourcePath.IsCanonicalSegment(identity))
        {
            throw new ArgumentException(
                $"Routing identity '{identity}' is not a canonical "
                + "resource-path segment.",
                nameof(identity));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(destinations);
        Role = role;
        Identity = identity;
        Title = title;
        Issuer = issuer;
        Purpose = purpose;
        Questions = [.. questions];
        Destinations = [.. destinations];
        NotYetExplainable = notYetExplainable;
        Grain = grain;
        Scope = scope;
        AcceptedSubjectKinds = [.. acceptedSubjectKinds ?? []];

        if (Questions.IsEmpty
            || Questions.Length > ResourceExplanationVocabulary
                .RoutingQuestionLimit
            || Questions.Any(string.IsNullOrWhiteSpace)
            || Questions.Distinct(StringComparer.Ordinal).Count()
                != Questions.Length)
        {
            throw new ArgumentException(
                "A routing declaration carries between one and "
                + $"{ResourceExplanationVocabulary.RoutingQuestionLimit} "
                + "distinct representative questions.",
                nameof(questions));
        }
        if (Destinations.Any(static destination => destination is null)
            || Destinations.Distinct().Count() != Destinations.Length)
        {
            throw new ArgumentException(
                "Routing destinations are unique and non-null.",
                nameof(destinations));
        }
        if (Destinations.IsEmpty == (notYetExplainable is null))
        {
            throw new ArgumentException(
                "A routing declaration has either one or more destinations "
                + "or a not-yet-explainable state, not both or neither.",
                nameof(destinations));
        }
        if (notYetExplainable is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(notYetExplainable);
        if (grain is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(grain);
        if (scope is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (role == ResourceExplanationRoutingRole.Operation
            ? AcceptedSubjectKinds.IsEmpty
            : !AcceptedSubjectKinds.IsEmpty)
        {
            throw new ArgumentException(
                "An operation names the subject kinds it accepts, and only "
                + "an operation does.",
                nameof(acceptedSubjectKinds));
        }
        if (AcceptedSubjectKinds.Distinct(StringComparer.Ordinal).Count()
            != AcceptedSubjectKinds.Length)
        {
            throw new ArgumentException(
                "Accepted subject kinds are unique.",
                nameof(acceptedSubjectKinds));
        }
    }

    public ResourceExplanationRoutingRole Role { get; }

    public string Identity { get; }

    public string Title { get; }

    public string Issuer { get; }

    public string Purpose { get; }

    public ImmutableArray<string> Questions { get; }

    public ImmutableArray<ExplanationResourceKey> Destinations { get; }

    public string? NotYetExplainable { get; }

    public string? Grain { get; }

    public string? Scope { get; }

    public ImmutableArray<string> AcceptedSubjectKinds { get; }
}

/// <summary>
/// A host's composed catalog with its explanation root, and the resources
/// the root does not yet reach. The host publishes the root only when that
/// set is empty; the catalog is valid either way.
/// </summary>
public sealed record ResourceExplanationRootComposition(
    ResourceExplanationCatalog Catalog,
    ResourcePath RootPath,
    ImmutableArray<ResourcePath> Unreachable)
{
    public bool IsPublishable => Unreachable.IsEmpty;
}

public sealed partial class ResourceExplanationCatalog
{
    public const string RoutingSubjectKindsSegment = "subject-kinds";
    public const string RoutingOperationsSegment = "operations";
    public const string RoutingReferencesSegment = "references";

    /// <summary>
    /// The explanation key of the structural catalog Resource Explanation
    /// issues for <paramref name="catalog"/>, the destination a Schema Query
    /// routing declaration names.
    /// </summary>
    public static ExplanationResourceKey StructuralCatalogKey(string catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        return ResourceExplanationVocabulary.Key(
            ResourceExplanationVocabulary.CatalogType,
            Pack(catalog));
    }

    /// <summary>
    /// The explanation key of the inspection document a capability module
    /// registers as <paramref name="documentIdentity"/>, the destination an
    /// Inspection Capability Composition routing declaration names.
    /// </summary>
    public static ExplanationResourceKey CapabilityDocumentKey(
        string documentIdentity) =>
        CapabilityKey(
            new InspectionCapabilityResourceIdentity(
                InspectionCapabilityResourceKind.Document,
                documentIdentity));

    /// <summary>
    /// The explanation key of the collection that lists every registered
    /// analysis.
    /// </summary>
    public static ExplanationResourceKey AnalysesCollectionKey { get; } =
        ResourceExplanationVocabulary.Key(
            ResourceExplanationVocabulary.NavigationCollectionType,
            Pack("analysis-collection"));

    /// <summary>
    /// The root's destination relationship for each routable resource type.
    /// An adopting owner adds the relationship for its destination type.
    /// </summary>
    private static readonly IReadOnlyDictionary<
        ExplanationResourceTypeIdentity,
        string> RoutingDestinationRelationships =
        new Dictionary<ExplanationResourceTypeIdentity, string>
        {
            [ResourceExplanationVocabulary.CatalogType] =
                "routing-destination-catalog",
            [ResourceExplanationVocabulary.InspectionDocumentType] =
                "routing-destination-document",
            [ResourceExplanationVocabulary.NavigationCollectionType] =
                "routing-destination-collection",
        };

    /// <summary>
    /// Projects the explanation root over a host's composed catalog from the
    /// routing declarations the host admitted, and reports every resource
    /// the root does not reach. A duplicate identity within a role, a
    /// destination that is not one installed resource of a destination
    /// type, or an operation naming an undeclared subject kind is an invalid
    /// registry.
    /// </summary>
    public static ResourceExplanationRootComposition ComposeRoot(
        ResourceExplanationCatalog composed,
        ResourcePath rootPath,
        IEnumerable<ResourceExplanationRoutingDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(composed);
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentNullException.ThrowIfNull(declarations);
        ImmutableArray<ResourceExplanationRoutingDeclaration> admitted =
            [.. declarations];
        if (admitted.Any(static declaration => declaration is null))
        {
            throw new ArgumentException(
                "Routing declarations are non-null.",
                nameof(declarations));
        }

        var keys =
            new Dictionary<
                (ResourceExplanationRoutingRole Role, string Identity),
                ExplanationResourceKey>();
        foreach (ResourceExplanationRoutingDeclaration declaration
                 in admitted)
        {
            if (!keys.TryAdd(
                    (declaration.Role, declaration.Identity),
                    RoutingEntryKey(declaration.Role, declaration.Identity)))
            {
                throw new ArgumentException(
                    $"Routing {RoleText(declaration.Role)} "
                    + $"'{declaration.Identity}' is declared more than once.",
                    nameof(declarations));
            }
            foreach (ExplanationResourceKey destination
                     in declaration.Destinations)
            {
                if (!composed._resourcesByKey.TryGetValue(
                        destination,
                        out CatalogResource? resource))
                {
                    throw new ArgumentException(
                        $"Routing {RoleText(declaration.Role)} "
                        + $"'{declaration.Identity}' destination "
                        + $"'{destination}' is not an installed resource.",
                        nameof(declarations));
                }
                if (!RoutingDestinationRelationships.ContainsKey(
                        resource.Snapshot.Key.ResourceType))
                {
                    throw new ArgumentException(
                        $"Routing {RoleText(declaration.Role)} "
                        + $"'{declaration.Identity}' destination "
                        + $"'{destination}' has no routing destination "
                        + "relationship for its resource type.",
                        nameof(declarations));
                }
            }
        }
        foreach (ResourceExplanationRoutingDeclaration operation
                 in admitted.Where(static declaration =>
                     declaration.Role
                         == ResourceExplanationRoutingRole.Operation))
        {
            foreach (string subjectKind in operation.AcceptedSubjectKinds)
            {
                if (!keys.ContainsKey(
                        (ResourceExplanationRoutingRole.SubjectKind,
                            subjectKind)))
                {
                    throw new ArgumentException(
                        $"Routing operation '{operation.Identity}' accepts "
                        + $"undeclared subject kind '{subjectKind}'.",
                        nameof(declarations));
                }
            }
        }

        ExplanationResourceKey rootKey =
            ResourceExplanationVocabulary.Key(
                ResourceExplanationVocabulary.ExplanationRootType,
                Pack(rootPath.Value));
        (ResourceExplanationRoutingRole Role, string Segment, string Name)[]
            collections =
            [
                (
                    ResourceExplanationRoutingRole.SubjectKind,
                    RoutingSubjectKindsSegment,
                    "Subject kinds"),
                (
                    ResourceExplanationRoutingRole.Operation,
                    RoutingOperationsSegment,
                    "Operations"),
                (
                    ResourceExplanationRoutingRole.Reference,
                    RoutingReferencesSegment,
                    "References"),
            ];
        var snapshots = new List<ExplanationResourceSnapshot>
        {
            Snapshot(
                rootPath,
                rootKey,
                [
                    ResourceExplanationVocabulary.TextFact(
                        ResourceExplanationVocabulary.ExplanationRootType,
                        "name",
                        "Explanation root"),
                    ResourceExplanationVocabulary.TextFact(
                        ResourceExplanationVocabulary.ExplanationRootType,
                        "coverage",
                        "Each collection covers the routing declarations "
                        + "registered with this host's catalog, not every "
                        + "command the host exposes."),
                    ResourceExplanationVocabulary.IntegerFact(
                        ResourceExplanationVocabulary.ExplanationRootType,
                        "members",
                        collections.Length),
                ],
                RelationshipObservations(
                    ResourceExplanationVocabulary.ExplanationRootType,
                    new Dictionary<string, IEnumerable<ExplanationResourceKey>>
                    {
                        ["root-collection"] = collections.Select(collection =>
                            RoutingCollectionKey(
                                rootPath,
                                collection.Segment)),
                    })),
        };
        foreach ((ResourceExplanationRoutingRole role, string segment, string name)
                 in collections)
        {
            ImmutableArray<ResourceExplanationRoutingDeclaration> members =
            [
                .. admitted.Where(declaration => declaration.Role == role),
            ];
            snapshots.Add(Snapshot(
                rootPath.Append(segment),
                RoutingCollectionKey(rootPath, segment),
                [
                    ResourceExplanationVocabulary.TextFact(
                        ResourceExplanationVocabulary.RoutingCollectionType,
                        "name",
                        name),
                    ResourceExplanationVocabulary.IntegerFact(
                        ResourceExplanationVocabulary.RoutingCollectionType,
                        "members",
                        members.Length),
                ],
                RelationshipObservations(
                    ResourceExplanationVocabulary.RoutingCollectionType,
                    new Dictionary<string, IEnumerable<ExplanationResourceKey>>
                    {
                        ["collection-routing-entry"] = members.Select(member =>
                            keys[(member.Role, member.Identity)]),
                    })));
            foreach (ResourceExplanationRoutingDeclaration declaration
                     in members)
            {
                snapshots.Add(RoutingEntrySnapshot(
                    rootPath.Append(segment, declaration.Identity),
                    declaration,
                    keys,
                    admitted));
            }
        }

        ResourceExplanationCatalog catalog = Create(
            composed._schemas,
            [
                .. composed._catalogResources.Select(static resource =>
                    resource.Snapshot),
                .. snapshots,
            ]);
        return new(catalog, rootPath, catalog.Unreachable(rootKey));
    }

    private ImmutableArray<ResourcePath> Unreachable(
        ExplanationResourceKey root)
    {
        ILookup<ExplanationResourceKey, ExplanationResourceKey> targets =
            _relationships
                .SelectMany(static relationship =>
                    relationship.Targets.Select(target =>
                        (relationship.Source, target.Resource)))
                .ToLookup(
                    static edge => edge.Source,
                    static edge => edge.Resource);
        var reached = new HashSet<ExplanationResourceKey> { root };
        var pending = new Queue<ExplanationResourceKey>([root]);
        while (pending.TryDequeue(out ExplanationResourceKey? source))
        {
            foreach (ExplanationResourceKey target in targets[source])
            {
                if (reached.Add(target))
                    pending.Enqueue(target);
            }
        }
        return
        [
            .. _catalogResources
                .Where(resource => !reached.Contains(resource.Snapshot.Key))
                .Select(static resource => resource.Path!)
                .OrderBy(static path => path.Value, StringComparer.Ordinal),
        ];
    }

    private static ExplanationResourceSnapshot RoutingEntrySnapshot(
        ResourcePath path,
        ResourceExplanationRoutingDeclaration declaration,
        IReadOnlyDictionary<
            (ResourceExplanationRoutingRole Role, string Identity),
            ExplanationResourceKey> keys,
        ImmutableArray<ResourceExplanationRoutingDeclaration> admitted)
    {
        ExplanationResourceTypeIdentity type =
            ResourceExplanationVocabulary.RoutingEntryType;
        var targets =
            new Dictionary<string, IEnumerable<ExplanationResourceKey>>
            {
                ["accepts-subject-kind"] =
                    declaration.AcceptedSubjectKinds.Select(subjectKind =>
                        keys[(
                            ResourceExplanationRoutingRole.SubjectKind,
                            subjectKind)]),
                ["accepted-by-operation"] =
                    declaration.Role
                        == ResourceExplanationRoutingRole.SubjectKind
                        ? admitted
                            .Where(operation =>
                                operation.AcceptedSubjectKinds.Contains(
                                    declaration.Identity))
                            .Select(operation =>
                                keys[(operation.Role, operation.Identity)])
                        : [],
            };
        foreach (IGrouping<string, ExplanationResourceKey> destinations
                 in declaration.Destinations.GroupBy(destination =>
                     RoutingDestinationRelationships[destination.ResourceType]))
        {
            targets[destinations.Key] = destinations;
        }
        return Snapshot(
            path,
            keys[(declaration.Role, declaration.Identity)],
            [
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "identity",
                    declaration.Identity),
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "role",
                    RoleText(declaration.Role)),
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "name",
                    declaration.Title),
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "issuer",
                    declaration.Issuer),
                ResourceExplanationVocabulary.TextFact(
                    type,
                    "purpose",
                    declaration.Purpose),
                ResourceExplanationVocabulary.TextsFact(
                    type,
                    "questions",
                    declaration.Questions),
                OptionalTextFact(type, "grain", declaration.Grain),
                OptionalTextFact(type, "scope", declaration.Scope),
                OptionalTextFact(
                    type,
                    "not-yet-explainable",
                    declaration.NotYetExplainable),
            ],
            RelationshipObservations(type, targets));
    }

    private static ExplanationResourceKey RoutingEntryKey(
        ResourceExplanationRoutingRole role,
        string identity) =>
        ResourceExplanationVocabulary.Key(
            ResourceExplanationVocabulary.RoutingEntryType,
            Pack(RoleText(role), identity));

    private static ExplanationResourceKey RoutingCollectionKey(
        ResourcePath rootPath,
        string segment) =>
        ResourceExplanationVocabulary.Key(
            ResourceExplanationVocabulary.RoutingCollectionType,
            Pack(rootPath.Value, segment));

    private static string RoleText(ResourceExplanationRoutingRole role) =>
        role switch
        {
            ResourceExplanationRoutingRole.SubjectKind => "subject-kind",
            ResourceExplanationRoutingRole.Operation => "operation",
            ResourceExplanationRoutingRole.Reference => "reference",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
}
