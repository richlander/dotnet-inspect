using System.Collections.ObjectModel;
using System.Collections.Immutable;
using QuerySpace;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Definitions;

/// <summary>Supported inspection-definition schema identities.</summary>
public static class InspectionDefinitionSchema
{
    public const int Version1 = 1;
    public const int Version2 = 2;
    public const int Version3 = 3;
    public const int Version4 = 4;
    public const int Version5 = 5;

    internal static bool IsSupported(int value) =>
        value is Version1 or Version2 or Version3 or Version4 or Version5;
}

/// <summary>
/// Discriminator for a portable inspection definition record.
/// </summary>
public enum InspectionDefinitionKind
{
    Catalog = 0,
    Workspace = 1,
    Query = 2,
    View = 3,
    Navigation = 4,
    Scenario = 5,
}

/// <summary>
/// One declarative inspection definition record. Records remain separate and compose by id.
/// </summary>
public abstract record InspectionDefinitionRecord
{
    private protected InspectionDefinitionRecord(int schemaVersion, string id)
    {
        if (!InspectionDefinitionSchema.IsSupported(schemaVersion))
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"Unsupported definition schema version {schemaVersion}.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        SchemaVersion = schemaVersion;
        Id = id;
    }

    public int SchemaVersion { get; }

    public string Id { get; }

    public abstract InspectionDefinitionKind Kind { get; }
}

/// <summary>A catalog of named assembly groups.</summary>
public sealed record CatalogDefinition : InspectionDefinitionRecord
{
    public CatalogDefinition(int schemaVersion, string id, IReadOnlyList<CatalogGroupDefinition> groups)
        : base(schemaVersion, id)
    {
        Groups = DefinitionCollections.Freeze(groups);
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.Catalog;

    public IReadOnlyList<CatalogGroupDefinition> Groups { get; }
}

/// <summary>One named group entry in a catalog (or a workspace-local group list).</summary>
public sealed record CatalogGroupDefinition
{
    public CatalogGroupDefinition(
        string name,
        IReadOnlyList<DefinitionMemberCoordinate>? members = null,
        IReadOnlyList<CatalogGroupDefinition>? children = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Members = DefinitionCollections.Freeze(members);
        Children = DefinitionCollections.Freeze(children);
    }

    public string Name { get; }

    public IReadOnlyList<DefinitionMemberCoordinate> Members { get; }

    public IReadOnlyList<CatalogGroupDefinition> Children { get; }
}

/// <summary>A workspace definition: one or more named contexts.</summary>
public sealed record WorkspaceDefinition : InspectionDefinitionRecord
{
    public WorkspaceDefinition(
        int schemaVersion,
        string id,
        IReadOnlyList<WorkspaceContextDefinition> contexts,
        string? title = null,
        string? description = null,
        IReadOnlyList<CatalogGroupDefinition>? groups = null,
        IReadOnlyList<WorkspaceRegistration>? registrations = null,
        IReadOnlyList<WorkspacePackageSourceDefinition>? packageSources = null)
        : base(schemaVersion, id)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        // Freeze first, then validate the retained snapshot (emptiness and uniqueness).
        var frozenContexts = DefinitionCollections.Freeze(contexts);
        var frozenRegistrations = DefinitionCollections.Freeze(registrations);
        var frozenPackageSources = DefinitionCollections.Freeze(packageSources);
        if (schemaVersion is not (
                InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4
                or InspectionDefinitionSchema.Version5)
            && frozenRegistrations.Count != 0)
        {
            throw new ArgumentException(
                "Workspace registrations require schema version 3, 4, or 5.",
                nameof(registrations));
        }
        if (schemaVersion != InspectionDefinitionSchema.Version5
            && frozenPackageSources.Count != 0)
        {
            throw new ArgumentException(
                "Workspace package sources require schema version 5.",
                nameof(packageSources));
        }
        if (schemaVersion == InspectionDefinitionSchema.Version5
            && frozenPackageSources.Count == 0)
        {
            throw new ArgumentException(
                "A schema-version-5 workspace definition requires at least one package source.",
                nameof(packageSources));
        }
        if (schemaVersion is InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5)
        {
            if (frozenContexts.Count == 0 && frozenRegistrations.Count == 0)
            {
                throw new ArgumentException(
                    $"A schema-version-{schemaVersion} workspace definition requires at least one context or registration.",
                    nameof(contexts));
            }
        }
        else if (schemaVersion != InspectionDefinitionSchema.Version3
            && frozenContexts.Count == 0)
        {
            throw new ArgumentException(
                "A workspace definition requires at least one context.",
                nameof(contexts));
        }

        var contextNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in frozenContexts)
        {
            if (!contextNames.Add(context.Name))
            {
                throw new ArgumentException(
                    $"Duplicate workspace context name '{context.Name}'.",
                    nameof(contexts));
            }
        }

        WorkspacePackageSourceDefinition.ValidateSet(frozenPackageSources);

        ImmutableArray<WorkspaceRegistration> registrationArray =
            [.. frozenRegistrations];
        if (WorkspacePlan.ValidateRegistrations(registrationArray)
            is { } registrationRejection)
        {
            throw new ArgumentException(
                $"The workspace registration set is invalid ({registrationRejection}).",
                nameof(registrations));
        }

        Title = title;
        Description = description;
        Contexts = frozenContexts;
        Groups = DefinitionCollections.Freeze(groups);
        Registrations = frozenRegistrations;
        PackageSources = frozenPackageSources;
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.Workspace;

    public string? Title { get; }

    public string? Description { get; }

    public IReadOnlyList<WorkspaceContextDefinition> Contexts { get; }

    /// <summary>
    /// Optional document-local groups for a self-contained workspace file.
    /// Bundle authors should prefer a catalog record.
    /// </summary>
    public IReadOnlyList<CatalogGroupDefinition> Groups { get; }

    /// <summary>
    /// Ordered resource-free registrations. Present only in schema versions 3 through 5.
    /// </summary>
    public IReadOnlyList<WorkspaceRegistration> Registrations { get; }

    /// <summary>
    /// Ordered credential-free package source declarations. Present only in
    /// schema version 5.
    /// </summary>
    public IReadOnlyList<WorkspacePackageSourceDefinition> PackageSources { get; }
}

/// <summary>
/// Authentication required to realize one portable Workspace package source.
/// </summary>
public enum WorkspacePackageSourceAuthentication
{
    Anonymous = 0,
    AuthenticationRequired = 1,
}

/// <summary>
/// Credential-free configuration for one portable Workspace NuGet v3 source.
/// </summary>
public sealed record WorkspacePackageSourceDefinition
{
    public WorkspacePackageSourceDefinition(
        string endpoint,
        WorkspacePackageSourceAuthentication authentication =
            WorkspacePackageSourceAuthentication.Anonymous)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || parsed.UserInfo.Length != 0
            || parsed.Query.Length != 0
            || parsed.Fragment.Length != 0)
        {
            throw new ArgumentException(
                "A portable Workspace package source must be an absolute HTTPS "
                    + "URL without user information, query, or fragment.",
                nameof(endpoint));
        }
        if (authentication is not (
                WorkspacePackageSourceAuthentication.Anonymous
                or WorkspacePackageSourceAuthentication.AuthenticationRequired))
        {
            throw new ArgumentOutOfRangeException(
                nameof(authentication),
                authentication,
                "Unsupported Workspace package source authentication.");
        }

        Endpoint = parsed.AbsoluteUri;
        Authentication = authentication;
    }

    public string Endpoint { get; }

    public WorkspacePackageSourceAuthentication Authentication { get; }

    public static void ValidateSet(
        IReadOnlyList<WorkspacePackageSourceDefinition> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        var originModes = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (WorkspacePackageSourceDefinition source in sources)
        {
            if (!endpoints.Add(source.Endpoint))
            {
                throw new ArgumentException(
                    $"Workspace package source endpoint '{source.Endpoint}' is duplicated.",
                    nameof(sources));
            }

            string origin = new Uri(source.Endpoint)
                .GetLeftPart(UriPartial.Authority);
            bool requiresAuthentication =
                source.Authentication
                    == WorkspacePackageSourceAuthentication.AuthenticationRequired;
            if (originModes.TryGetValue(
                    origin,
                    out bool existingRequiresAuthentication)
                && existingRequiresAuthentication != requiresAuthentication)
            {
                throw new ArgumentException(
                    $"Workspace package source origin '{origin}' mixes "
                        + "anonymous and authenticated sources.",
                    nameof(sources));
            }
            originModes[origin] = requiresAuthentication;
        }
    }
}

/// <summary>One binding-consistent context inside a workspace definition.</summary>
public sealed record WorkspaceContextDefinition
{
    public WorkspaceContextDefinition(
        string name,
        string? framework = null,
        string? runtimeIdentifier = null,
        string? subscribe = null,
        IReadOnlyList<DefinitionMemberCoordinate>? members = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (subscribe is not null && string.IsNullOrWhiteSpace(subscribe))
        {
            throw new ArgumentException(
                "A workspace context subscribe must not be blank.",
                nameof(subscribe));
        }

        Name = name;
        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
        Subscribe = subscribe;
        Members = DefinitionCollections.Freeze(members);
        if (Subscribe is null && Members.Count == 0)
        {
            throw new ArgumentException(
                "A workspace context requires subscribe, members, or both.",
                nameof(members));
        }
    }

    public string Name { get; }

    public string? Framework { get; }

    public string? RuntimeIdentifier { get; }

    public string? Subscribe { get; }

    public IReadOnlyList<DefinitionMemberCoordinate> Members { get; }
}

/// <summary>A named query preset. Payload shape is owned by the query-plan owner.</summary>
public sealed record QueryDefinition : InspectionDefinitionRecord
{
    public QueryDefinition(int schemaVersion, string id, string? queryId = null)
        : base(schemaVersion, id)
    {
        if (schemaVersion != InspectionDefinitionSchema.Version1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "QueryDefinition is the schema-version-1 query record.");
        }

        QueryId = DefinitionText.NormalizeOptional(queryId, nameof(queryId));
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.Query;

    /// <summary>Optional product query identity; reserved for preset-input validation.</summary>
    public string? QueryId { get; }
}

/// <summary>
/// A schema-version-2-through-5 query preset with one canonical portable identity.
/// </summary>
public sealed record CommittedQueryDefinition : InspectionDefinitionRecord
{
    public CommittedQueryDefinition(
        int schemaVersion,
        string id,
        PortableQueryIdentity identity)
        : base(schemaVersion, id)
    {
        if (schemaVersion is not (
            InspectionDefinitionSchema.Version2
            or InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "CommittedQueryDefinition requires schema version 2, 3, 4, or 5.");
        }

        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (string.IsNullOrWhiteSpace(identity.Vocabulary))
        {
            throw new ArgumentException(
                "A committed query requires a nonblank vocabulary identity.",
                nameof(identity));
        }
        Intent = PortableQueryPayloadCodec.Decode(identity.Payload);
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.Query;

    public PortableQueryIdentity Identity { get; }

    public PortableQueryIntent Intent { get; }

    public string QueryId => Identity.Vocabulary;

    public string Payload => Identity.Payload;
}

public enum PortableQueryInputRequirement
{
    Forbidden = 0,
    Optional = 1,
    Required = 2,
}

/// <summary>
/// Structural inputs one portable query purpose accepts from its attachment.
/// </summary>
public sealed class PortableQueryDefinitionInputs
{
    private PortableQueryDefinitionInputs(
        bool coordinateFreePrimary,
        IReadOnlyList<PortableSubjectRequestKind> subjectKinds,
        IReadOnlyList<string> facetIds,
        PortableQueryInputRequirement stateCoordinate,
        PortableQueryInputRequirement selectedContext,
        PortableQueryInputRequirement stateLibraryScope)
    {
        CoordinateFreePrimary = coordinateFreePrimary;
        SubjectKinds = subjectKinds;
        FacetIds = facetIds;
        StateCoordinate = stateCoordinate;
        SelectedContext = selectedContext;
        StateLibraryScope = stateLibraryScope;
    }

    public bool CoordinateFreePrimary { get; }

    public IReadOnlyList<PortableSubjectRequestKind> SubjectKinds { get; }

    public IReadOnlyList<string> FacetIds { get; }

    public PortableQueryInputRequirement StateCoordinate { get; }

    public PortableQueryInputRequirement SelectedContext { get; }

    public PortableQueryInputRequirement StateLibraryScope { get; }

    public static PortableQueryDefinitionInputs CoordinateFree() =>
        new(
            coordinateFreePrimary: true,
            [],
            [],
            PortableQueryInputRequirement.Forbidden,
            PortableQueryInputRequirement.Forbidden,
            PortableQueryInputRequirement.Forbidden);

    public static PortableQueryDefinitionInputs StateBound(
        IReadOnlyList<PortableSubjectRequestKind> subjectKinds,
        IReadOnlyList<string> facetIds,
        PortableQueryInputRequirement stateCoordinate,
        PortableQueryInputRequirement selectedContext,
        PortableQueryInputRequirement stateLibraryScope)
    {
        IReadOnlyList<PortableSubjectRequestKind> frozenSubjects =
            DefinitionCollections.Freeze(subjectKinds);
        IReadOnlyList<string> frozenFacets =
            FreezeOrderedValues(facetIds, nameof(facetIds));
        if (frozenSubjects.Count == 0)
        {
            throw new ArgumentException(
                "A state-bound query descriptor requires at least one subject kind.",
                nameof(subjectKinds));
        }
        if (frozenSubjects.Any(static kind => !Enum.IsDefined(kind)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectKinds),
                "A state-bound query descriptor requires defined subject kinds.");
        }
        if (frozenSubjects.Distinct().Count() != frozenSubjects.Count)
        {
            throw new ArgumentException(
                "A state-bound query descriptor requires unique subject kinds.",
                nameof(subjectKinds));
        }
        if (frozenFacets.Count == 0)
        {
            throw new ArgumentException(
                "A state-bound query descriptor requires at least one facet id.",
                nameof(facetIds));
        }
        if (!Enum.IsDefined(stateCoordinate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stateCoordinate),
                stateCoordinate,
                "A state-bound query descriptor requires a defined coordinate requirement.");
        }
        if (!Enum.IsDefined(selectedContext))
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedContext),
                selectedContext,
                "A state-bound query descriptor requires a defined selected-context requirement.");
        }
        if (!Enum.IsDefined(stateLibraryScope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stateLibraryScope),
                stateLibraryScope,
                "A state-bound query descriptor requires a defined Library-scope requirement.");
        }

        return new(
            coordinateFreePrimary: false,
            frozenSubjects,
            frozenFacets,
            stateCoordinate,
            selectedContext,
            stateLibraryScope);
    }

    private static IReadOnlyList<string> FreezeOrderedValues(
        IReadOnlyList<string> values,
        string paramName)
    {
        ArgumentNullException.ThrowIfNull(values);
        IReadOnlyList<string> frozen = DefinitionCollections.Freeze(values);
        string? previous = null;
        foreach (string value in frozen)
        {
            DefinitionText.Require(value, paramName);
            if (previous is not null
                && string.CompareOrdinal(previous, value) >= 0)
            {
                throw new ArgumentException(
                    $"{paramName} must contain unique values in ascending ordinal order.",
                    paramName);
            }

            previous = value;
        }

        return frozen;
    }
}

public abstract record PortableQueryDefinitionResolution<TPlan>
{
    private protected PortableQueryDefinitionResolution()
    {
    }

    public sealed record Accepted(TPlan Plan)
        : PortableQueryDefinitionResolution<TPlan>;

    public sealed record Rejected
        : PortableQueryDefinitionResolution<TPlan>
    {
        public Rejected(string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            Message = message;
        }

        public string Message { get; }
    }
}

/// <summary>
/// Structural values supplied to one query attachment after descriptor input
/// admission.
/// </summary>
public sealed class PortableQueryDefinitionAttachment
{
    internal PortableQueryDefinitionAttachment(
        PortableSubjectRequestKind? subjectKind,
        string? facetId,
        DefinitionMemberCoordinate? stateCoordinate,
        WorkspaceContextDefinition? selectedContext,
        IReadOnlyList<PortableLibraryIdentity>? stateLibraryScope)
    {
        SubjectKind = subjectKind;
        FacetId = facetId;
        StateCoordinate = stateCoordinate;
        SelectedContext = selectedContext;
        StateLibraryScope = DefinitionCollections.Freeze(stateLibraryScope);
    }

    public PortableSubjectRequestKind? SubjectKind { get; }

    public string? FacetId { get; }

    public DefinitionMemberCoordinate? StateCoordinate { get; }

    public WorkspaceContextDefinition? SelectedContext { get; }

    public IReadOnlyList<PortableLibraryIdentity> StateLibraryScope { get; }
}

/// <summary>
/// Owner-issued portable query purpose, structural inputs, and typed binder.
/// </summary>
public abstract class PortableQueryDefinitionDescriptor
{
    private protected PortableQueryDefinitionDescriptor(
        string queryId,
        string purpose,
        PortableQueryDefinitionInputs inputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        QueryId = queryId;
        Purpose = purpose;
        Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    }

    public string QueryId { get; }

    public string Purpose { get; }

    public PortableQueryDefinitionInputs Inputs { get; }

    internal abstract BoundCommittedQuery Bind(
        CommittedQueryDefinition definition,
        PortableQueryDefinitionAttachment attachment,
        CancellationToken cancellationToken);
}

public sealed class PortableQueryDefinitionDescriptor<TPlan>
    : PortableQueryDefinitionDescriptor
{
    private readonly Func<
        PortableQueryIntent,
        PortableQueryDefinitionAttachment,
        CancellationToken,
        PortableQueryDefinitionResolution<TPlan>> _resolve;

    public PortableQueryDefinitionDescriptor(
        string queryId,
        string purpose,
        PortableQueryDefinitionInputs inputs,
        Func<
            PortableQueryIntent,
            PortableQueryDefinitionAttachment,
            CancellationToken,
            PortableQueryDefinitionResolution<TPlan>> resolve)
        : base(queryId, purpose, inputs)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    internal override BoundCommittedQuery Bind(
        CommittedQueryDefinition definition,
        PortableQueryDefinitionAttachment attachment,
        CancellationToken cancellationToken) =>
        _resolve(definition.Intent, attachment, cancellationToken) switch
        {
            PortableQueryDefinitionResolution<TPlan>.Accepted accepted =>
                new BoundCommittedQuery<TPlan>(
                    definition,
                    this,
                    attachment,
                    accepted.Plan),
            PortableQueryDefinitionResolution<TPlan>.Rejected rejected =>
                throw new InspectionDefinitionException(
                    $"Query '{definition.Id}' was rejected by vocabulary "
                        + $"'{QueryId}': {rejected.Message}"),
            _ => throw new InvalidOperationException(
                "Unknown portable query definition resolution."),
        };
}

public abstract record BoundCommittedQuery(
    CommittedQueryDefinition Definition,
    PortableQueryDefinitionDescriptor Descriptor,
    PortableQueryDefinitionAttachment Attachment);

public sealed record BoundCommittedQuery<TPlan>
    : BoundCommittedQuery
{
    public BoundCommittedQuery(
        CommittedQueryDefinition definition,
        PortableQueryDefinitionDescriptor<TPlan> descriptor,
        PortableQueryDefinitionAttachment attachment,
        TPlan plan)
        : base(definition, descriptor, attachment)
    {
        TypedDescriptor = descriptor;
        Plan = plan;
    }

    public PortableQueryDefinitionDescriptor<TPlan> TypedDescriptor { get; }

    public TPlan Plan { get; }
}

/// <summary>A named view preset: portable type/member selectors and facet ids.</summary>
public sealed record ViewDefinition : InspectionDefinitionRecord
{
    public ViewDefinition(
        int schemaVersion,
        string id,
        string? lens = null,
        string? type = null,
        string? memberAnchor = null,
        string? memberSignature = null,
        string? memberKey = null,
        string? section = null,
        string? library = null,
        IReadOnlyList<string>? libraries = null)
        : base(schemaVersion, id)
    {
        if (schemaVersion != InspectionDefinitionSchema.Version1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "ViewDefinition is the schema-version-1 flat view record.");
        }

        lens = DefinitionText.NormalizeOptional(lens, nameof(lens));
        type = DefinitionText.NormalizeOptional(type, nameof(type));
        memberAnchor = DefinitionText.NormalizeOptional(memberAnchor, nameof(memberAnchor));
        memberSignature = DefinitionText.NormalizeOptional(memberSignature, nameof(memberSignature));
        memberKey = DefinitionText.NormalizeOptional(memberKey, nameof(memberKey));
        section = DefinitionText.NormalizeOptional(section, nameof(section));
        library = DefinitionText.NormalizeOptional(library, nameof(library));
        if (library is not null && libraries is not null)
        {
            throw new ArgumentException(
                "library and libraries are mutually exclusive.",
                nameof(libraries));
        }

        if (memberAnchor is not null && memberSignature is not null)
        {
            throw new ArgumentException(
                "memberAnchor and memberSignature are mutually exclusive.",
                nameof(memberSignature));
        }

        if ((memberAnchor is not null || memberSignature is not null || memberKey is not null)
            && type is null)
        {
            throw new ArgumentException(
                "Member selectors require type.",
                nameof(type));
        }

        Lens = lens;
        Type = type;
        MemberAnchor = memberAnchor;
        MemberSignature = memberSignature;
        MemberKey = memberKey;
        Section = section;
        if (libraries is null)
        {
            Libraries = library is null
                ? Array.Empty<string>()
                : new ReadOnlyCollection<string>([library]);
        }
        else
        {
            IReadOnlyList<string> frozenLibraries = DefinitionCollections.Freeze(libraries);
            var seenLibraries = new HashSet<string>(StringComparer.Ordinal);
            string? previousLibrary = null;
            foreach (string item in frozenLibraries)
            {
                _ = DefinitionText.Require(item, nameof(libraries));
                if (!seenLibraries.Add(item))
                {
                    throw new ArgumentException(
                        "libraries must not contain duplicate identities.",
                        nameof(libraries));
                }
                if (previousLibrary is not null
                    && string.CompareOrdinal(previousLibrary, item) >= 0)
                {
                    throw new ArgumentException(
                        "libraries must be in ascending ordinal order.",
                        nameof(libraries));
                }

                previousLibrary = item;
            }

            Libraries = frozenLibraries;
        }
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.View;

    public string? Lens { get; }

    public string? Type { get; }

    public string? MemberAnchor { get; }

    public string? MemberSignature { get; }

    /// <summary>
    /// Workbench member-group key (<c>kind:name</c>). Optional host convenience; not a substitute
    /// for <see cref="MemberAnchor"/> or <see cref="MemberSignature"/>.
    /// </summary>
    public string? MemberKey { get; }

    public string? Section { get; }

    /// <summary>
    /// Legacy single-library projection. Null when the view has zero or
    /// multiple library identities.
    /// </summary>
    public string? Library => Libraries.Count == 1 ? Libraries[0] : null;

    public IReadOnlyList<string> Libraries { get; }
}

/// <summary>A named navigation preset: ordered tabs plus one focused tab id.</summary>
public sealed record NavigationDefinition : InspectionDefinitionRecord
{
    public NavigationDefinition(
        int schemaVersion,
        string id,
        IReadOnlyList<NavigationTabDefinition> tabs,
        string focus)
        : base(schemaVersion, id)
    {
        if (schemaVersion != InspectionDefinitionSchema.Version1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "NavigationDefinition is the schema-version-1 navigation record.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(focus);
        ArgumentNullException.ThrowIfNull(tabs);

        // Freeze first, then validate emptiness and focus against the retained snapshot.
        var frozenTabs = DefinitionCollections.Freeze(tabs);
        if (frozenTabs.Count == 0)
            throw new ArgumentException("A navigation preset requires at least one tab.", nameof(tabs));

        Tabs = frozenTabs;
        Focus = focus;

        if (Tabs.All(tab => tab.Id != Focus))
            throw new ArgumentException($"Navigation focus '{Focus}' does not match a tab id.", nameof(focus));
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.Navigation;

    public IReadOnlyList<NavigationTabDefinition> Tabs { get; }

    public string Focus { get; }
}

/// <summary>One navigation tab with exactly one source: coordinate or group subscription.</summary>
public sealed record NavigationTabDefinition
{
    public NavigationTabDefinition(
        string id,
        DefinitionMemberCoordinate? coordinate = null,
        string? subscribe = null,
        string? framework = null,
        string? runtimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (subscribe is not null && string.IsNullOrWhiteSpace(subscribe))
        {
            throw new ArgumentException(
                "A navigation tab subscribe must not be blank.",
                nameof(subscribe));
        }

        var hasCoordinate = coordinate is not null;
        var hasSubscribe = subscribe is not null;
        if (hasCoordinate == hasSubscribe)
        {
            throw new ArgumentException(
                "A navigation tab requires exactly one of coordinate or subscribe.",
                nameof(coordinate));
        }

        Id = id;
        Coordinate = coordinate;
        Subscribe = subscribe;
        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
    }

    public string Id { get; }

    public DefinitionMemberCoordinate? Coordinate { get; }

    public string? Subscribe { get; }

    public string? Framework { get; }

    public string? RuntimeIdentifier { get; }
}

/// <summary>A scenario composition naming peer records by id.</summary>
public sealed record ScenarioDefinition : InspectionDefinitionRecord
{
    public ScenarioDefinition(
        int schemaVersion,
        string id,
        string? title = null,
        string? description = null,
        string? workspace = null,
        string? context = null,
        string? input = null,
        string? query = null,
        string? view = null,
        string? navigation = null)
        : base(schemaVersion, id)
    {
        workspace = DefinitionText.NormalizeOptional(workspace, nameof(workspace));
        input = DefinitionText.NormalizeOptional(input, nameof(input));
        context = DefinitionText.NormalizeOptional(context, nameof(context));
        query = DefinitionText.NormalizeOptional(query, nameof(query));
        view = DefinitionText.NormalizeOptional(view, nameof(view));
        navigation = DefinitionText.NormalizeOptional(navigation, nameof(navigation));

        var hasWorkspace = workspace is not null;
        var hasInput = input is not null;
        if (!hasWorkspace && !hasInput)
        {
            throw new ArgumentException(
                "A scenario requires exactly one of workspace or input.",
                nameof(workspace));
        }

        if (hasWorkspace && hasInput)
        {
            throw new ArgumentException(
                "A scenario cannot set both workspace and input.",
                nameof(input));
        }

        if (!hasWorkspace && context is not null)
        {
            throw new ArgumentException(
                "A scenario context requires workspace.",
                nameof(context));
        }

        if (schemaVersion is InspectionDefinitionSchema.Version2
            or InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5)
        {
            if (hasWorkspace)
            {
                if (query is not null)
                {
                    throw new ArgumentException(
                        "A committed coordinate-backed scenario carries queries through committed view states.",
                        nameof(query));
                }

                if (view is null || navigation is null)
                {
                    throw new ArgumentException(
                        "A committed workspace-backed scenario requires both view and navigation.",
                        nameof(view));
                }
            }
            else if (view is not null || navigation is not null)
            {
                throw new ArgumentException(
                    "A committed workspace-free scenario cannot reference view or navigation.",
                    nameof(view));
            }
        }

        Title = title;
        Description = description;
        Workspace = workspace;
        Context = context;
        Input = input;
        Query = query;
        View = view;
        Navigation = navigation;
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.Scenario;

    public string? Title { get; }

    public string? Description { get; }

    public string? Workspace { get; }

    public string? Context { get; }

    public string? Input { get; }

    public string? Query { get; }

    public string? View { get; }

    public string? Navigation { get; }
}

/// <summary>
/// Schema-version-2-through-5 navigation: ordered tabs and a required nullable
/// focus. Null focus selects the committed Workspace row.
/// </summary>
public sealed record CommittedNavigationDefinition : InspectionDefinitionRecord
{
    public CommittedNavigationDefinition(
        int schemaVersion,
        string id,
        IReadOnlyList<NavigationTabDefinition> tabs,
        string? focus)
        : base(schemaVersion, id)
    {
        if (schemaVersion is not (InspectionDefinitionSchema.Version2
            or InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "CommittedNavigationDefinition requires schema version 2, 3, 4, or 5.");
        }

        ArgumentNullException.ThrowIfNull(tabs);
        Tabs = DefinitionCollections.Freeze(tabs);
        if (schemaVersion == InspectionDefinitionSchema.Version2
            && Tabs.Count == 0)
        {
            throw new ArgumentException(
                "A schema-version-2 committed navigation record requires at least one tab.",
                nameof(tabs));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (NavigationTabDefinition tab in Tabs)
        {
            if (!ids.Add(tab.Id))
            {
                throw new ArgumentException(
                    $"Duplicate navigation tab id '{tab.Id}'.",
                    nameof(tabs));
            }
        }

        Focus = DefinitionText.NormalizeOptional(focus, nameof(focus));
        if (Focus is not null)
        {
            NavigationTabDefinition? focused =
                Tabs.FirstOrDefault(tab => tab.Id == Focus);
            if (focused is null)
            {
                throw new ArgumentException(
                    $"Navigation focus '{Focus}' does not match a tab id.",
                    nameof(focus));
            }
            if (focused.Coordinate
                is not DefinitionMemberCoordinate.PackageCoordinate)
            {
                throw new ArgumentException(
                    "Committed focus must identify a direct Package-coordinate tab.",
                    nameof(focus));
            }
        }
    }

    public override InspectionDefinitionKind Kind =>
        InspectionDefinitionKind.Navigation;

    public IReadOnlyList<NavigationTabDefinition> Tabs { get; }

    public string? Focus { get; }
}

/// <summary>
/// Schema-version-2-through-5 committed view state: one Workspace row plus one row
/// for every navigation tab in exact navigation order.
/// </summary>
public sealed record CommittedViewDefinition : InspectionDefinitionRecord
{
    public CommittedViewDefinition(
        int schemaVersion,
        string id,
        IReadOnlyList<CommittedViewStateDefinition> states)
        : base(schemaVersion, id)
    {
        if (schemaVersion is not (InspectionDefinitionSchema.Version2
            or InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4
            or InspectionDefinitionSchema.Version5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "CommittedViewDefinition requires schema version 2, 3, 4, or 5.");
        }

        ArgumentNullException.ThrowIfNull(states);
        States = DefinitionCollections.Freeze(states);
        if (schemaVersion is not (
                InspectionDefinitionSchema.Version4
                or InspectionDefinitionSchema.Version5)
            && States.Any(state => state.Subject is
                PortableSubjectRequest.Library
                or PortableSubjectRequest.Type
                or PortableSubjectRequest.Member))
        {
            throw new ArgumentException(
                $"Schema version {schemaVersion} does not support active Library, Type, or Member subjects.",
                nameof(states));
        }
        if (States.Count == 0)
        {
            throw new ArgumentException(
                "A committed view requires its leading Workspace state.",
                nameof(states));
        }
        if (States[0].Navigation is not null)
        {
            throw new ArgumentException(
                "The first committed view state must be the null-navigation Workspace row.",
                nameof(states));
        }

        var navigationIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 1; index < States.Count; index++)
        {
            string? navigation = States[index].Navigation;
            if (navigation is null)
            {
                throw new ArgumentException(
                    "Only the leading committed view state may have null navigation.",
                    nameof(states));
            }
            if (!navigationIds.Add(navigation))
            {
                throw new ArgumentException(
                    $"Duplicate committed view navigation id '{navigation}'.",
                    nameof(states));
            }
        }
    }

    public override InspectionDefinitionKind Kind =>
        InspectionDefinitionKind.View;

    public IReadOnlyList<CommittedViewStateDefinition> States { get; }
}

/// <summary>One schema-version-2-through-5 committed state.</summary>
public sealed record CommittedViewStateDefinition
{
    public CommittedViewStateDefinition(
        string? navigation,
        PortableSubjectRequest? subject = null,
        PortableRetainedSubjectContext? context = null,
        string? facet = null,
        IReadOnlyList<string>? queries = null,
        IReadOnlyList<PortableLibraryIdentity>? libraries = null)
    {
        Navigation = DefinitionText.NormalizeOptional(
            navigation,
            nameof(navigation));
        Facet = DefinitionText.NormalizeOptional(facet, nameof(facet));
        Queries = FreezeOrderedStrings(queries, nameof(queries));
        Libraries = FreezeOrderedLibraries(libraries);

        if (Facet is not null && subject is null)
        {
            throw new ArgumentException(
                "An exact committed facet requires an explicit subject.",
                nameof(facet));
        }
        bool coordinateFreeCandidate =
            Navigation is null
            && subject is PortableSubjectRequest.Workspace
            && context is null
            && Facet is null
            && Queries.Count == 1
            && Libraries.Count == 0;
        if (Queries.Count != 0
            && Facet is null
            && !coordinateFreeCandidate)
        {
            throw new ArgumentException(
                "Committed query references require an exact facet unless "
                    + "the state is a coordinate-free primary-query candidate.",
                nameof(queries));
        }
        if (Libraries.Count != 0 && Queries.Count == 0)
        {
            throw new ArgumentException(
                "Committed Library scope requires a query reference.",
                nameof(libraries));
        }
        if (subject is null && context is not null)
        {
            throw new ArgumentException(
                "A subject-less committed state must omit retained context.",
                nameof(context));
        }
        if (subject is PortableSubjectRequest.Workspace
            && context is PortableRetainedSubjectContext.Package)
        {
            throw new ArgumentException(
                "A Workspace committed state must omit Package-only context.",
                nameof(context));
        }
        if (subject is PortableSubjectRequest.Package && context is null)
        {
            throw new ArgumentException(
                "A Package subject requires its retained Package context.",
                nameof(context));
        }
        if (subject is PortableSubjectRequest.Library
            && context is not (
                PortableRetainedSubjectContext.AllLibraries
                or PortableRetainedSubjectContext.Library
                or PortableRetainedSubjectContext.Type
                or PortableRetainedSubjectContext.Member
                or PortableRetainedSubjectContext.EscapedType
                or PortableRetainedSubjectContext.EscapedMember))
        {
            throw new ArgumentException(
                "A Library subject requires retained all-Libraries, Library, Type, or Member context.",
                nameof(context));
        }
        if (subject is PortableSubjectRequest.Type
            && context is not (
                PortableRetainedSubjectContext.Type
                or PortableRetainedSubjectContext.Member
                or PortableRetainedSubjectContext.EscapedType
                or PortableRetainedSubjectContext.EscapedMember))
        {
            throw new ArgumentException(
                "A Type subject requires retained Type or Member context.",
                nameof(context));
        }
        if (subject is PortableSubjectRequest.Member
            && context is not (
                PortableRetainedSubjectContext.Member
                or PortableRetainedSubjectContext.EscapedMember))
        {
            throw new ArgumentException(
                "A Member subject requires retained Member context.",
                nameof(context));
        }

        Subject = subject;
        Context = context;
    }

    public string? Navigation { get; }

    public PortableSubjectRequest? Subject { get; }

    public PortableRetainedSubjectContext? Context { get; }

    public string? Facet { get; }

    public IReadOnlyList<string> Queries { get; }

    public IReadOnlyList<PortableLibraryIdentity> Libraries { get; }

    private static IReadOnlyList<string> FreezeOrderedStrings(
        IReadOnlyList<string>? values,
        string paramName)
    {
        IReadOnlyList<string> frozen = DefinitionCollections.Freeze(values);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        foreach (string value in frozen)
        {
            DefinitionText.Require(value, paramName);
            if (!seen.Add(value)
                || previous is not null
                    && string.CompareOrdinal(previous, value) >= 0)
            {
                throw new ArgumentException(
                    $"{paramName} must contain unique values in ascending ordinal order.",
                    paramName);
            }

            previous = value;
        }

        return frozen;
    }

    private static IReadOnlyList<PortableLibraryIdentity> FreezeOrderedLibraries(
        IReadOnlyList<PortableLibraryIdentity>? values)
    {
        IReadOnlyList<PortableLibraryIdentity> frozen =
            DefinitionCollections.Freeze(values);
        var semanticIdentities = new HashSet<AssemblyReferenceIdentity>(
            frozen.Count,
            AssemblyReferenceIdentity.EquivalentComparer);
        for (int index = 0; index < frozen.Count; index++)
        {
            if (index > 0
                && PortableLibraryIdentityComparer.Instance.Compare(
                    frozen[index - 1],
                    frozen[index]) >= 0)
            {
                throw new ArgumentException(
                    "libraries must contain unique identities in canonical order.",
                    nameof(values));
            }
            if (!semanticIdentities.Add(
                PortableLibraryIdentityComparer.ToMetadataIdentity(
                    frozen[index])))
            {
                throw new ArgumentException(
                    "libraries must not contain semantically equivalent identities.",
                    nameof(values));
            }
        }

        return frozen;
    }
}

/// <summary>
/// A closed portable active-subject request for committed schema versions.
/// </summary>
public abstract record PortableSubjectRequest
{
    private protected PortableSubjectRequest()
    {
    }

    public abstract PortableSubjectRequestKind Kind { get; }

    public sealed record Workspace : PortableSubjectRequest
    {
        public override PortableSubjectRequestKind Kind =>
            PortableSubjectRequestKind.Workspace;
    }

    public sealed record Package : PortableSubjectRequest
    {
        public override PortableSubjectRequestKind Kind =>
            PortableSubjectRequestKind.Package;
    }

    public sealed record Library : PortableSubjectRequest
    {
        public override PortableSubjectRequestKind Kind =>
            PortableSubjectRequestKind.Library;
    }

    public sealed record Type : PortableSubjectRequest
    {
        public override PortableSubjectRequestKind Kind =>
            PortableSubjectRequestKind.Type;
    }

    public sealed record Member : PortableSubjectRequest
    {
        public override PortableSubjectRequestKind Kind =>
            PortableSubjectRequestKind.Member;
    }
}

public enum PortableSubjectRequestKind
{
    Workspace,
    Package,
    Library,
    Type,
    Member,
}

/// <summary>
/// Portable retained descendant context beneath a state row's Package
/// coordinate.
/// </summary>
public abstract record PortableRetainedSubjectContext
{
    private protected PortableRetainedSubjectContext()
    {
    }

    public abstract PortableRetainedSubjectContextKind Kind { get; }

    public sealed record Package : PortableRetainedSubjectContext
    {
        public override PortableRetainedSubjectContextKind Kind =>
            PortableRetainedSubjectContextKind.Package;
    }

    public sealed record AllLibraries : PortableRetainedSubjectContext
    {
        public override PortableRetainedSubjectContextKind Kind =>
            PortableRetainedSubjectContextKind.AllLibraries;
    }

    public sealed record Library(
        PortableLibraryIdentity LibraryIdentity)
        : PortableRetainedSubjectContext
    {
        public PortableLibraryIdentity LibraryIdentity { get; } =
            LibraryIdentity
            ?? throw new ArgumentNullException(nameof(LibraryIdentity));

        public override PortableRetainedSubjectContextKind Kind =>
            PortableRetainedSubjectContextKind.Library;
    }

    public sealed record Type(
        PortableLibraryIdentity LibraryIdentity,
        MetadataTypeDefinitionName TypeIdentity)
        : PortableRetainedSubjectContext
    {
        public PortableLibraryIdentity LibraryIdentity { get; } =
            LibraryIdentity
            ?? throw new ArgumentNullException(nameof(LibraryIdentity));

        public MetadataTypeDefinitionName TypeIdentity { get; } =
            TypeIdentity
            ?? throw new ArgumentNullException(nameof(TypeIdentity));

        public override PortableRetainedSubjectContextKind Kind =>
            PortableRetainedSubjectContextKind.Type;
    }

    public sealed record Member : PortableRetainedSubjectContext
    {
        public Member(
            PortableLibraryIdentity libraryIdentity,
            MetadataTypeDefinitionName typeIdentity,
            string? memberAnchor = null,
            string? memberSignature = null)
        {
            LibraryIdentity =
                libraryIdentity
                ?? throw new ArgumentNullException(nameof(libraryIdentity));
            TypeIdentity =
                typeIdentity
                ?? throw new ArgumentNullException(nameof(typeIdentity));
            memberAnchor = DefinitionText.NormalizeOptional(
                memberAnchor,
                nameof(memberAnchor));
            memberSignature = DefinitionText.NormalizeOptional(
                memberSignature,
                nameof(memberSignature));
            if ((memberAnchor is null) == (memberSignature is null))
            {
                throw new ArgumentException(
                    "A retained Member requires exactly one of memberAnchor or memberSignature.",
                    nameof(memberAnchor));
            }
            if (memberAnchor is not null
                && (memberAnchor.Length != 10
                    || memberAnchor.Any(character =>
                        character is not (>= '0' and <= '9')
                            and not (>= 'a' and <= 'f'))))
            {
                throw new ArgumentException(
                    "memberAnchor must contain exactly 10 lowercase hexadecimal digits.",
                    nameof(memberAnchor));
            }

            MemberAnchor = memberAnchor;
            MemberSignature = memberSignature;
        }

        public PortableLibraryIdentity LibraryIdentity { get; }

        public MetadataTypeDefinitionName TypeIdentity { get; }

        public string? MemberAnchor { get; }

        public string? MemberSignature { get; }

        public override PortableRetainedSubjectContextKind Kind =>
            PortableRetainedSubjectContextKind.Member;
    }

    internal sealed record EscapedType : PortableRetainedSubjectContext
    {
        public EscapedType(
            PortableLibraryIdentity libraryIdentity,
            string escapedTypeIdentity)
        {
            LibraryIdentity =
                libraryIdentity
                ?? throw new ArgumentNullException(nameof(libraryIdentity));
            EscapedTypeIdentity = DefinitionText.Require(
                escapedTypeIdentity,
                nameof(escapedTypeIdentity));
        }

        public PortableLibraryIdentity LibraryIdentity { get; }

        public string EscapedTypeIdentity { get; }

        public override PortableRetainedSubjectContextKind Kind =>
            PortableRetainedSubjectContextKind.Type;
    }

    internal sealed record EscapedMember : PortableRetainedSubjectContext
    {
        public EscapedMember(
            PortableLibraryIdentity libraryIdentity,
            string escapedTypeIdentity,
            string? memberAnchor = null,
            string? memberSignature = null)
        {
            LibraryIdentity =
                libraryIdentity
                ?? throw new ArgumentNullException(nameof(libraryIdentity));
            EscapedTypeIdentity = DefinitionText.Require(
                escapedTypeIdentity,
                nameof(escapedTypeIdentity));
            memberAnchor = DefinitionText.NormalizeOptional(
                memberAnchor,
                nameof(memberAnchor));
            memberSignature = DefinitionText.NormalizeOptional(
                memberSignature,
                nameof(memberSignature));
            if ((memberAnchor is null) == (memberSignature is null))
            {
                throw new ArgumentException(
                    "A retained Member requires exactly one of memberAnchor or memberSignature.",
                    nameof(memberAnchor));
            }
            if (memberAnchor is not null
                && (memberAnchor.Length != 10
                    || memberAnchor.Any(character =>
                        character is not (>= '0' and <= '9')
                            and not (>= 'a' and <= 'f'))))
            {
                throw new ArgumentException(
                    "memberAnchor must contain exactly 10 lowercase hexadecimal digits.",
                    nameof(memberAnchor));
            }

            MemberAnchor = memberAnchor;
            MemberSignature = memberSignature;
        }

        public PortableLibraryIdentity LibraryIdentity { get; }

        public string EscapedTypeIdentity { get; }

        public string? MemberAnchor { get; }

        public string? MemberSignature { get; }

        public override PortableRetainedSubjectContextKind Kind =>
            PortableRetainedSubjectContextKind.Member;
    }
}

public enum PortableRetainedSubjectContextKind
{
    Package,
    AllLibraries,
    Library,
    Type,
    Member,
}

/// <summary>Portable ECMA assembly-definition identity.</summary>
public sealed record PortableLibraryIdentity
{
    public PortableLibraryIdentity(
        string name,
        string version,
        string? culture,
        string? publicKeyToken)
    {
        Name = DefinitionText.Require(name, nameof(name));
        Version = RequireCanonicalVersion(version);
        if (culture is not null
            && (string.IsNullOrWhiteSpace(culture)
                || culture.Equals("neutral", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "culture must be null for neutral culture or a nonblank metadata culture.",
                nameof(culture));
        }
        if (publicKeyToken is not null
            && (publicKeyToken.Length != 16
                || publicKeyToken.Any(character =>
                    character is not (>= '0' and <= '9')
                        and not (>= 'a' and <= 'f'))))
        {
            throw new ArgumentException(
                "publicKeyToken must be null or exactly 16 lowercase hexadecimal digits.",
                nameof(publicKeyToken));
        }

        Culture = culture;
        PublicKeyToken = publicKeyToken;
    }

    public string Name { get; }

    public string Version { get; }

    public string? Culture { get; }

    public string? PublicKeyToken { get; }

    private static string RequireCanonicalVersion(string value)
    {
        DefinitionText.Require(value, nameof(value));
        string[] components = value.Split('.');
        if (components.Length != 4)
        {
            throw new ArgumentException(
                "version must contain exactly four unsigned 16-bit decimal components.",
                nameof(value));
        }

        foreach (string component in components)
        {
            if (component.Length == 0
                || component.Length > 1 && component[0] == '0'
                || component.Any(character =>
                    character is not (>= '0' and <= '9'))
                || !ushort.TryParse(
                    component,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _))
            {
                throw new ArgumentException(
                    "version must contain exactly four canonical unsigned 16-bit decimal components.",
                    nameof(value));
            }
        }

        return value;
    }
}

internal sealed class PortableLibraryIdentityComparer
    : IComparer<PortableLibraryIdentity>
{
    public static PortableLibraryIdentityComparer Instance { get; } = new();

    public int Compare(PortableLibraryIdentity? x, PortableLibraryIdentity? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        int result = string.CompareOrdinal(x.Name, y.Name);
        if (result != 0)
            return result;
        result = CompareVersion(x.Version, y.Version);
        if (result != 0)
            return result;
        result = CompareNullable(x.Culture, y.Culture);
        return result != 0
            ? result
            : CompareNullable(x.PublicKeyToken, y.PublicKeyToken);
    }

    private static int CompareVersion(string x, string y)
    {
        ReadOnlySpan<char> left = x;
        ReadOnlySpan<char> right = y;
        for (int component = 0; component < 4; component++)
        {
            int leftSeparator = left.IndexOf('.');
            int rightSeparator = right.IndexOf('.');
            ReadOnlySpan<char> leftPart =
                leftSeparator < 0 ? left : left[..leftSeparator];
            ReadOnlySpan<char> rightPart =
                rightSeparator < 0 ? right : right[..rightSeparator];
            int result =
                ushort.Parse(leftPart).CompareTo(ushort.Parse(rightPart));
            if (result != 0)
                return result;
            left = leftSeparator < 0 ? [] : left[(leftSeparator + 1)..];
            right = rightSeparator < 0 ? [] : right[(rightSeparator + 1)..];
        }

        return 0;
    }

    private static int CompareNullable(string? x, string? y) =>
        x is null
            ? y is null ? 0 : -1
            : y is null ? 1 : string.CompareOrdinal(x, y);

    public static AssemblyReferenceIdentity ToMetadataIdentity(
        PortableLibraryIdentity identity) =>
        new(
            identity.Name,
            System.Version.Parse(identity.Version),
            identity.Culture,
            identity.PublicKeyToken);
}

/// <summary>One acquisition coordinate in definition JSON.</summary>
public abstract record DefinitionMemberCoordinate
{
    private protected DefinitionMemberCoordinate()
    {
    }

    public abstract string Kind { get; }

    public sealed record PackageCoordinate : DefinitionMemberCoordinate
    {
        public PackageCoordinate(
            string Id,
            string? Version = null,
            string? Framework = null,
            string? RuntimeIdentifier = null)
        {
            this.Id = DefinitionText.Require(Id, nameof(Id));
            this.Version = Version;
            this.Framework = Framework;
            this.RuntimeIdentifier = RuntimeIdentifier;
        }

        public string Id { get; }

        public string? Version { get; }

        public string? Framework { get; }

        public string? RuntimeIdentifier { get; }

        public override string Kind => "package";
    }

    public sealed record PlatformCoordinate : DefinitionMemberCoordinate
    {
        public PlatformCoordinate(
            string Family,
            string? Assembly = null,
            string? Version = null,
            string? Framework = null)
        {
            this.Family = DefinitionText.Require(Family, nameof(Family));
            this.Assembly = Assembly;
            this.Version = Version;
            this.Framework = Framework;
        }

        public string Family { get; }

        public string? Assembly { get; }

        public string? Version { get; }

        public string? Framework { get; }

        public override string Kind => "platform";
    }

    public sealed record EmbeddedCoordinate : DefinitionMemberCoordinate
    {
        public EmbeddedCoordinate(string ContentRef, string Digest, string DeclaredName)
        {
            this.ContentRef = DefinitionText.Require(ContentRef, nameof(ContentRef));
            this.Digest = DefinitionText.Require(Digest, nameof(Digest));
            this.DeclaredName = DefinitionText.Require(DeclaredName, nameof(DeclaredName));
        }

        public string ContentRef { get; }

        public string Digest { get; }

        public string DeclaredName { get; }

        public override string Kind => "embedded";
    }

    public sealed record ProjectCoordinate : DefinitionMemberCoordinate
    {
        public ProjectCoordinate(
            string Path,
            string? Framework = null,
            string? RuntimeIdentifier = null)
        {
            this.Path = DefinitionText.Require(Path, nameof(Path));
            this.Framework = Framework;
            this.RuntimeIdentifier = RuntimeIdentifier;
        }

        public string Path { get; }

        public string? Framework { get; }

        public string? RuntimeIdentifier { get; }

        public override string Kind => "project";
    }

    public sealed record LocalCoordinate : DefinitionMemberCoordinate
    {
        public LocalCoordinate(string Path)
        {
            this.Path = DefinitionText.Require(Path, nameof(Path));
        }

        public string Path { get; }

        public override string Kind => "local";
    }

    public sealed record DirectoryCoordinate : DefinitionMemberCoordinate
    {
        public DirectoryCoordinate(
            string Path,
            string? Framework = null,
            string? RuntimeIdentifier = null)
        {
            this.Path = DefinitionText.Require(Path, nameof(Path));
            this.Framework = Framework;
            this.RuntimeIdentifier = RuntimeIdentifier;
        }

        public string Path { get; }

        public string? Framework { get; }

        public string? RuntimeIdentifier { get; }

        public override string Kind => "directory";
    }
}

file static class DefinitionCollections
{
    public static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T>? values)
    {
        if (values is null || values.Count == 0)
            return Array.Empty<T>();

        // Copy once through the indexer, validate that snapshot, and wrap it. Do not re-enumerate
        // the source: a hostile IReadOnlyList can disagree between indexer and enumerator.
        var copy = new T[values.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            var item = values[i];
            if (item is null)
            {
                throw new ArgumentException(
                    "Collection must not contain null elements.",
                    nameof(values));
            }

            copy[i] = item;
        }

        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>
/// Optional definition text is null-means-absent. Non-null blank values are rejected so
/// callers never treat whitespace as a present identity or selector.
/// </summary>
file static class DefinitionText
{
    public static string? NormalizeOptional(string? value, string paramName)
    {
        if (value is null)
            return null;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{paramName} must not be blank.",
                paramName);
        }

        return value;
    }

    public static string Require(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{paramName} must not be blank.",
                paramName);
        }

        return value;
    }
}
