using System.Collections.ObjectModel;

namespace DotnetInspector.Queries.Definitions;

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
        if (schemaVersion != InspectionDefinitionJson.CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"Unsupported definition schema version {schemaVersion}; expected {InspectionDefinitionJson.CurrentSchemaVersion}.");
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
        IReadOnlyList<CatalogGroupDefinition>? groups = null)
        : base(schemaVersion, id)
    {
        if (contexts is null || contexts.Count == 0)
            throw new ArgumentException("A workspace definition requires at least one context.", nameof(contexts));

        var contextNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in contexts)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!contextNames.Add(context.Name))
            {
                throw new ArgumentException(
                    $"Duplicate workspace context name '{context.Name}'.",
                    nameof(contexts));
            }
        }

        Title = title;
        Description = description;
        Contexts = DefinitionCollections.Freeze(contexts);
        Groups = DefinitionCollections.Freeze(groups);
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
        QueryId = DefinitionText.NormalizeOptional(queryId, nameof(queryId));
    }

    public override InspectionDefinitionKind Kind => InspectionDefinitionKind.Query;

    /// <summary>Optional product query identity; reserved for preset-input validation.</summary>
    public string? QueryId { get; }
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
        string? library = null)
        : base(schemaVersion, id)
    {
        lens = DefinitionText.NormalizeOptional(lens, nameof(lens));
        type = DefinitionText.NormalizeOptional(type, nameof(type));
        memberAnchor = DefinitionText.NormalizeOptional(memberAnchor, nameof(memberAnchor));
        memberSignature = DefinitionText.NormalizeOptional(memberSignature, nameof(memberSignature));
        memberKey = DefinitionText.NormalizeOptional(memberKey, nameof(memberKey));
        section = DefinitionText.NormalizeOptional(section, nameof(section));
        library = DefinitionText.NormalizeOptional(library, nameof(library));

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
        Library = library;
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

    public string? Library { get; }
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
        ArgumentException.ThrowIfNullOrWhiteSpace(focus);
        if (tabs is null || tabs.Count == 0)
            throw new ArgumentException("A navigation preset requires at least one tab.", nameof(tabs));

        Tabs = DefinitionCollections.Freeze(tabs);
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

/// <summary>One acquisition coordinate in definition JSON.</summary>
public abstract record DefinitionMemberCoordinate
{
    private protected DefinitionMemberCoordinate()
    {
    }

    public abstract string Kind { get; }

    public sealed record PackageCoordinate(
        string Id,
        string? Version = null,
        string? Framework = null,
        string? RuntimeIdentifier = null) : DefinitionMemberCoordinate
    {
        public override string Kind => "package";
    }

    public sealed record PlatformCoordinate(
        string Family,
        string? Assembly = null,
        string? Version = null,
        string? Framework = null) : DefinitionMemberCoordinate
    {
        public override string Kind => "platform";
    }

    public sealed record EmbeddedCoordinate(
        string ContentRef,
        string Digest,
        string DeclaredName) : DefinitionMemberCoordinate
    {
        public override string Kind => "embedded";
    }

    public sealed record ProjectCoordinate(
        string Path,
        string? Framework = null,
        string? RuntimeIdentifier = null) : DefinitionMemberCoordinate
    {
        public override string Kind => "project";
    }

    public sealed record LocalCoordinate(string Path) : DefinitionMemberCoordinate
    {
        public override string Kind => "local";
    }

    public sealed record DirectoryCoordinate(
        string Path,
        string? Framework = null,
        string? RuntimeIdentifier = null) : DefinitionMemberCoordinate
    {
        public override string Kind => "directory";
    }
}

file static class DefinitionCollections
{
    public static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T>? values) =>
        values is null || values.Count == 0
            ? Array.Empty<T>()
            : new ReadOnlyCollection<T>(values is T[] array ? [.. array] : [.. values]);
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
}
