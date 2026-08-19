using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Core;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Loads and emits inspection definition records. Untrusted JSON enters through
/// <see cref="HardenedJson"/> (duplicate properties rejected), then binds through a
/// source-generated context that rejects unmapped members.
/// </summary>
public static class InspectionDefinitionJson
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Maximum UTF-8 JSON bytes for one standalone definition file.</summary>
    public const int MaxUtf8ByteLength = 1_048_576;

    /// <summary>Maximum coordinates accepted across one record's nested lists.</summary>
    public const int MaxCoordinatesPerRecord = 1_024;

    public static InspectionDefinitionRecord Parse(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        var utf8 = Encoding.UTF8.GetBytes(json);
        return Parse(utf8);
    }

    public static InspectionDefinitionRecord Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length == 0)
            throw new ArgumentException("Definition JSON is empty.", nameof(utf8Json));
        if (utf8Json.Length > MaxUtf8ByteLength)
        {
            throw new InspectionDefinitionException(
                $"Definition JSON exceeds the {MaxUtf8ByteLength}-byte limit.");
        }

        JsonDocument document;
        try
        {
            document = HardenedJson.Parse(utf8Json);
        }
        catch (JsonException ex)
        {
            throw new InspectionDefinitionException($"Definition JSON is invalid: {ex.Message}", ex);
        }

        using (document)
            return Bind(document.RootElement);
    }

    public static string Serialize(InspectionDefinitionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var dto = ToDto(record);
        return JsonSerializer.Serialize(dto, InspectionDefinitionJsonContext.Default.InspectionDefinitionDto);
    }

    internal static InspectionDefinitionRecord Bind(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InspectionDefinitionException("Definition JSON must be a single object.");

        InspectionDefinitionDto dto;
        try
        {
            dto = JsonSerializer.Deserialize(root, InspectionDefinitionJsonContext.Default.InspectionDefinitionDto)
                ?? throw new InspectionDefinitionException("Definition JSON deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InspectionDefinitionException($"Definition JSON is invalid: {ex.Message}", ex);
        }

        return FromDto(dto);
    }

    internal static InspectionDefinitionRecord FromDto(InspectionDefinitionDto dto)
    {
        if (dto.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InspectionDefinitionException(
                $"Unsupported definition schema version {dto.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(dto.Kind))
            throw new InspectionDefinitionException("Definition record requires kind.");
        if (string.IsNullOrWhiteSpace(dto.Id))
            throw new InspectionDefinitionException("Definition record requires id.");

        var kind = dto.Kind.Trim().ToLowerInvariant();
        var coordinateCount = 0;
        try
        {
            return kind switch
            {
                "catalog" => CreateCatalog(dto, ref coordinateCount),
                "workspace" => CreateWorkspace(dto, ref coordinateCount),
                "query" => CreateQuery(dto),
                "view" => CreateView(dto),
                "navigation" => CreateNavigation(dto, ref coordinateCount),
                "scenario" => CreateScenario(dto),
                _ => throw new InspectionDefinitionException($"Unknown definition kind '{dto.Kind}'."),
            };
        }
        catch (ArgumentException ex)
        {
            throw new InspectionDefinitionException(ex.Message, ex);
        }
    }

    private static CatalogDefinition CreateCatalog(InspectionDefinitionDto dto, ref int coordinateCount)
    {
        RejectForeignRecordFields(
            dto,
            "catalog",
            title: true,
            description: true,
            contexts: true,
            queryId: true,
            lens: true,
            type: true,
            memberAnchor: true,
            memberSignature: true,
            memberKey: true,
            section: true,
            library: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true);
        return new CatalogDefinition(
            dto.SchemaVersion,
            dto.Id!,
            MapGroups(dto.Groups, ref coordinateCount));
    }

    private static WorkspaceDefinition CreateWorkspace(InspectionDefinitionDto dto, ref int coordinateCount)
    {
        RejectForeignRecordFields(
            dto,
            "workspace",
            queryId: true,
            lens: true,
            type: true,
            memberAnchor: true,
            memberSignature: true,
            memberKey: true,
            section: true,
            library: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true);
        return new WorkspaceDefinition(
            dto.SchemaVersion,
            dto.Id!,
            MapContexts(dto.Contexts, ref coordinateCount),
            dto.Title,
            dto.Description,
            MapGroups(dto.Groups, ref coordinateCount));
    }

    private static QueryDefinition CreateQuery(InspectionDefinitionDto dto)
    {
        RejectForeignRecordFields(
            dto,
            "query",
            title: true,
            description: true,
            groups: true,
            contexts: true,
            lens: true,
            type: true,
            memberAnchor: true,
            memberSignature: true,
            memberKey: true,
            section: true,
            library: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true);
        return new QueryDefinition(dto.SchemaVersion, dto.Id!, dto.QueryId);
    }

    private static ViewDefinition CreateView(InspectionDefinitionDto dto)
    {
        RejectForeignRecordFields(
            dto,
            "view",
            title: true,
            description: true,
            groups: true,
            contexts: true,
            queryId: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true);
        return new ViewDefinition(
            dto.SchemaVersion,
            dto.Id!,
            dto.Lens,
            dto.Type,
            dto.MemberAnchor,
            dto.MemberSignature,
            dto.MemberKey,
            dto.Section,
            dto.Library);
    }

    private static NavigationDefinition CreateNavigation(InspectionDefinitionDto dto, ref int coordinateCount)
    {
        RejectForeignRecordFields(
            dto,
            "navigation",
            title: true,
            description: true,
            groups: true,
            contexts: true,
            queryId: true,
            lens: true,
            type: true,
            memberAnchor: true,
            memberSignature: true,
            memberKey: true,
            section: true,
            library: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true);
        return new NavigationDefinition(
            dto.SchemaVersion,
            dto.Id!,
            MapTabs(dto.Tabs, ref coordinateCount),
            dto.Focus ?? throw new InspectionDefinitionException("Navigation requires focus."));
    }

    private static ScenarioDefinition CreateScenario(InspectionDefinitionDto dto)
    {
        RejectForeignRecordFields(
            dto,
            "scenario",
            groups: true,
            contexts: true,
            queryId: true,
            lens: true,
            type: true,
            memberAnchor: true,
            memberSignature: true,
            memberKey: true,
            section: true,
            library: true,
            tabs: true,
            focus: true);
        return new ScenarioDefinition(
            dto.SchemaVersion,
            dto.Id!,
            dto.Title,
            dto.Description,
            dto.Workspace,
            dto.Context,
            dto.Input,
            dto.Query,
            dto.View,
            dto.Navigation);
    }

    private static void RejectForeignRecordFields(
        InspectionDefinitionDto dto,
        string kind,
        bool title = false,
        bool description = false,
        bool groups = false,
        bool contexts = false,
        bool queryId = false,
        bool lens = false,
        bool type = false,
        bool memberAnchor = false,
        bool memberSignature = false,
        bool memberKey = false,
        bool section = false,
        bool library = false,
        bool tabs = false,
        bool focus = false,
        bool workspace = false,
        bool context = false,
        bool input = false,
        bool query = false,
        bool view = false,
        bool navigation = false)
    {
        void Check(bool reject, string name, object? value)
        {
            if (reject && value is not null)
            {
                throw new InspectionDefinitionException(
                    $"{kind} definition must not set '{name}'.");
            }
        }

        Check(title, "title", dto.Title);
        Check(description, "description", dto.Description);
        Check(groups, "groups", dto.Groups);
        Check(contexts, "contexts", dto.Contexts);
        Check(queryId, "queryId", dto.QueryId);
        Check(lens, "lens", dto.Lens);
        Check(type, "type", dto.Type);
        Check(memberAnchor, "memberAnchor", dto.MemberAnchor);
        Check(memberSignature, "memberSignature", dto.MemberSignature);
        Check(memberKey, "memberKey", dto.MemberKey);
        Check(section, "section", dto.Section);
        Check(library, "library", dto.Library);
        Check(tabs, "tabs", dto.Tabs);
        Check(focus, "focus", dto.Focus);
        Check(workspace, "workspace", dto.Workspace);
        Check(context, "context", dto.Context);
        Check(input, "input", dto.Input);
        Check(query, "query", dto.Query);
        Check(view, "view", dto.View);
        Check(navigation, "navigation", dto.Navigation);
    }

    internal static InspectionDefinitionDto ToDto(InspectionDefinitionRecord record) =>
        record switch
        {
            CatalogDefinition catalog => new InspectionDefinitionDto
            {
                SchemaVersion = catalog.SchemaVersion,
                Kind = "catalog",
                Id = catalog.Id,
                Groups = catalog.Groups.Select(ToGroupDto).ToList(),
            },
            WorkspaceDefinition workspace => new InspectionDefinitionDto
            {
                SchemaVersion = workspace.SchemaVersion,
                Kind = "workspace",
                Id = workspace.Id,
                Title = workspace.Title,
                Description = workspace.Description,
                Contexts = workspace.Contexts.Select(ToContextDto).ToList(),
                Groups = workspace.Groups.Count == 0 ? null : workspace.Groups.Select(ToGroupDto).ToList(),
            },
            QueryDefinition query => new InspectionDefinitionDto
            {
                SchemaVersion = query.SchemaVersion,
                Kind = "query",
                Id = query.Id,
                QueryId = query.QueryId,
            },
            ViewDefinition view => new InspectionDefinitionDto
            {
                SchemaVersion = view.SchemaVersion,
                Kind = "view",
                Id = view.Id,
                Lens = view.Lens,
                Type = view.Type,
                MemberAnchor = view.MemberAnchor,
                MemberSignature = view.MemberSignature,
                MemberKey = view.MemberKey,
                Section = view.Section,
                Library = view.Library,
            },
            NavigationDefinition navigation => new InspectionDefinitionDto
            {
                SchemaVersion = navigation.SchemaVersion,
                Kind = "navigation",
                Id = navigation.Id,
                Tabs = navigation.Tabs.Select(ToTabDto).ToList(),
                Focus = navigation.Focus,
            },
            ScenarioDefinition scenario => new InspectionDefinitionDto
            {
                SchemaVersion = scenario.SchemaVersion,
                Kind = "scenario",
                Id = scenario.Id,
                Title = scenario.Title,
                Description = scenario.Description,
                Workspace = scenario.Workspace,
                Context = scenario.Context,
                Input = scenario.Input,
                Query = scenario.Query,
                View = scenario.View,
                Navigation = scenario.Navigation,
            },
            _ => throw new InspectionDefinitionException($"Unsupported record type {record.GetType().Name}."),
        };

    private static List<CatalogGroupDefinition> MapGroups(
        List<CatalogGroupDto>? groups,
        ref int coordinateCount)
    {
        if (groups is null || groups.Count == 0)
            return [];

        var mapped = new List<CatalogGroupDefinition>(groups.Count);
        foreach (var group in groups)
        {
            if (group is null)
                throw new InspectionDefinitionException("Catalog group entry must not be null.");
            mapped.Add(MapGroup(group, ref coordinateCount));
        }

        return mapped;
    }

    private static CatalogGroupDefinition MapGroup(CatalogGroupDto dto, ref int coordinateCount)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InspectionDefinitionException("Catalog group requires name.");

        return new CatalogGroupDefinition(
            dto.Name,
            MapCoordinates(dto.Members, ref coordinateCount),
            MapGroups(dto.Children, ref coordinateCount));
    }

    private static List<WorkspaceContextDefinition> MapContexts(
        List<WorkspaceContextDto>? contexts,
        ref int coordinateCount)
    {
        if (contexts is null || contexts.Count == 0)
            throw new InspectionDefinitionException("Workspace requires at least one context.");

        var mapped = new List<WorkspaceContextDefinition>(contexts.Count);
        foreach (var context in contexts)
        {
            if (context is null)
                throw new InspectionDefinitionException("Workspace context entry must not be null.");
            mapped.Add(MapContext(context, ref coordinateCount));
        }

        return mapped;
    }

    private static WorkspaceContextDefinition MapContext(
        WorkspaceContextDto dto,
        ref int coordinateCount)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InspectionDefinitionException("Workspace context requires name.");

        return new WorkspaceContextDefinition(
            dto.Name,
            dto.Framework,
            ChooseRuntimeIdentifier(dto.Rid, dto.RuntimeIdentifier, "Workspace context"),
            dto.Subscribe,
            MapCoordinates(dto.Members, ref coordinateCount));
    }

    private static List<NavigationTabDefinition> MapTabs(
        List<NavigationTabDto>? tabs,
        ref int coordinateCount)
    {
        if (tabs is null || tabs.Count == 0)
            throw new InspectionDefinitionException("Navigation requires at least one tab.");

        var mapped = new List<NavigationTabDefinition>(tabs.Count);
        foreach (var tab in tabs)
        {
            if (tab is null)
                throw new InspectionDefinitionException("Navigation tab entry must not be null.");
            mapped.Add(MapTab(tab, ref coordinateCount));
        }

        return mapped;
    }

    private static NavigationTabDefinition MapTab(NavigationTabDto dto, ref int coordinateCount)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
            throw new InspectionDefinitionException("Navigation tab requires id.");

        DefinitionMemberCoordinate? coordinate = null;
        if (dto.Coordinate is not null)
            coordinate = MapCoordinate(dto.Coordinate, ref coordinateCount);

        return new NavigationTabDefinition(
            dto.Id,
            coordinate,
            dto.Subscribe,
            dto.Framework,
            ChooseRuntimeIdentifier(dto.Rid, dto.RuntimeIdentifier, "Navigation tab"));
    }

    private static List<DefinitionMemberCoordinate> MapCoordinates(
        List<MemberCoordinateDto>? members,
        ref int coordinateCount)
    {
        if (members is null || members.Count == 0)
            return [];

        var mapped = new List<DefinitionMemberCoordinate>(members.Count);
        foreach (var member in members)
        {
            if (member is null)
                throw new InspectionDefinitionException("Member coordinate entry must not be null.");
            mapped.Add(MapCoordinate(member, ref coordinateCount));
        }

        return mapped;
    }

    private static DefinitionMemberCoordinate MapCoordinate(
        MemberCoordinateDto dto,
        ref int coordinateCount)
    {
        coordinateCount++;
        if (coordinateCount > MaxCoordinatesPerRecord)
        {
            throw new InspectionDefinitionException(
                $"Definition exceeds the {MaxCoordinatesPerRecord}-coordinate limit.");
        }

        if (string.IsNullOrWhiteSpace(dto.Kind))
            throw new InspectionDefinitionException("Member coordinate requires kind.");

        var kind = dto.Kind.Trim().ToLowerInvariant();
        return kind switch
        {
            "package" => CreatePackageCoordinate(dto),
            "platform" => CreatePlatformCoordinate(dto),
            "embedded" => CreateEmbeddedCoordinate(dto),
            "project" => CreateProjectCoordinate(dto),
            "local" => CreateLocalCoordinate(dto),
            "directory" => CreateDirectoryCoordinate(dto),
            _ => throw new InspectionDefinitionException($"Unknown member coordinate kind '{dto.Kind}'."),
        };
    }

    private static DefinitionMemberCoordinate.PackageCoordinate CreatePackageCoordinate(MemberCoordinateDto dto)
    {
        RejectForeignCoordinateFields(
            dto,
            "package",
            family: true,
            assembly: true,
            contentRef: true,
            digest: true,
            declaredName: true,
            path: true);
        return new DefinitionMemberCoordinate.PackageCoordinate(
            Require(dto.Id, "package id"),
            dto.Version,
            dto.Framework,
            ChooseRuntimeIdentifier(dto.Rid, dto.RuntimeIdentifier, "Package coordinate"));
    }

    private static DefinitionMemberCoordinate.PlatformCoordinate CreatePlatformCoordinate(MemberCoordinateDto dto)
    {
        RejectForeignCoordinateFields(
            dto,
            "platform",
            id: true,
            contentRef: true,
            digest: true,
            declaredName: true,
            path: true,
            rid: true,
            runtimeIdentifier: true);
        return new DefinitionMemberCoordinate.PlatformCoordinate(
            Require(dto.Family, "platform family"),
            dto.Assembly,
            dto.Version,
            dto.Framework);
    }

    private static DefinitionMemberCoordinate.EmbeddedCoordinate CreateEmbeddedCoordinate(MemberCoordinateDto dto)
    {
        RejectForeignCoordinateFields(
            dto,
            "embedded",
            id: true,
            version: true,
            framework: true,
            family: true,
            assembly: true,
            path: true,
            rid: true,
            runtimeIdentifier: true);
        return new DefinitionMemberCoordinate.EmbeddedCoordinate(
            Require(dto.ContentRef, "embedded contentRef"),
            Require(dto.Digest, "embedded digest"),
            Require(dto.DeclaredName, "embedded declaredName"));
    }

    private static DefinitionMemberCoordinate.ProjectCoordinate CreateProjectCoordinate(MemberCoordinateDto dto)
    {
        RejectForeignCoordinateFields(
            dto,
            "project",
            id: true,
            version: true,
            family: true,
            assembly: true,
            contentRef: true,
            digest: true,
            declaredName: true);
        return new DefinitionMemberCoordinate.ProjectCoordinate(
            Require(dto.Path, "project path"),
            dto.Framework,
            ChooseRuntimeIdentifier(dto.Rid, dto.RuntimeIdentifier, "Project coordinate"));
    }

    private static DefinitionMemberCoordinate.LocalCoordinate CreateLocalCoordinate(MemberCoordinateDto dto)
    {
        RejectForeignCoordinateFields(
            dto,
            "local",
            id: true,
            version: true,
            framework: true,
            family: true,
            assembly: true,
            contentRef: true,
            digest: true,
            declaredName: true,
            rid: true,
            runtimeIdentifier: true);
        return new DefinitionMemberCoordinate.LocalCoordinate(Require(dto.Path, "local path"));
    }

    private static DefinitionMemberCoordinate.DirectoryCoordinate CreateDirectoryCoordinate(MemberCoordinateDto dto)
    {
        RejectForeignCoordinateFields(
            dto,
            "directory",
            id: true,
            version: true,
            family: true,
            assembly: true,
            contentRef: true,
            digest: true,
            declaredName: true);
        return new DefinitionMemberCoordinate.DirectoryCoordinate(
            Require(dto.Path, "directory path"),
            dto.Framework,
            ChooseRuntimeIdentifier(dto.Rid, dto.RuntimeIdentifier, "Directory coordinate"));
    }

    private static void RejectForeignCoordinateFields(
        MemberCoordinateDto dto,
        string kind,
        bool id = false,
        bool version = false,
        bool framework = false,
        bool family = false,
        bool assembly = false,
        bool contentRef = false,
        bool digest = false,
        bool declaredName = false,
        bool path = false,
        bool rid = false,
        bool runtimeIdentifier = false)
    {
        void Check(bool reject, string name, object? value)
        {
            if (reject && value is not null)
            {
                throw new InspectionDefinitionException(
                    $"{kind} coordinate must not set '{name}'.");
            }
        }

        Check(id, "id", dto.Id);
        Check(version, "version", dto.Version);
        Check(framework, "framework", dto.Framework);
        Check(family, "family", dto.Family);
        Check(assembly, "assembly", dto.Assembly);
        Check(contentRef, "contentRef", dto.ContentRef);
        Check(digest, "digest", dto.Digest);
        Check(declaredName, "declaredName", dto.DeclaredName);
        Check(path, "path", dto.Path);
        Check(rid, "rid", dto.Rid);
        Check(runtimeIdentifier, "runtimeIdentifier", dto.RuntimeIdentifier);
    }


    private static string? ChooseRuntimeIdentifier(string? rid, string? runtimeIdentifier, string owner)
    {
        if (rid is not null && runtimeIdentifier is not null)
        {
            throw new InspectionDefinitionException(
                $"{owner} specifies both rid and runtimeIdentifier; use only one spelling.");
        }

        return rid ?? runtimeIdentifier;
    }

    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InspectionDefinitionException($"Member coordinate requires {name}.");
        return value;
    }

    private static CatalogGroupDto ToGroupDto(CatalogGroupDefinition group) => new()
    {
        Name = group.Name,
        Members = group.Members.Count == 0 ? null : group.Members.Select(ToCoordinateDto).ToList(),
        Children = group.Children.Count == 0 ? null : group.Children.Select(ToGroupDto).ToList(),
    };

    private static WorkspaceContextDto ToContextDto(WorkspaceContextDefinition context) => new()
    {
        Name = context.Name,
        Framework = context.Framework,
        Rid = context.RuntimeIdentifier,
        Subscribe = context.Subscribe,
        Members = context.Members.Count == 0 ? null : context.Members.Select(ToCoordinateDto).ToList(),
    };

    private static NavigationTabDto ToTabDto(NavigationTabDefinition tab) => new()
    {
        Id = tab.Id,
        Coordinate = tab.Coordinate is null ? null : ToCoordinateDto(tab.Coordinate),
        Subscribe = tab.Subscribe,
        Framework = tab.Framework,
        Rid = tab.RuntimeIdentifier,
    };

    private static MemberCoordinateDto ToCoordinateDto(DefinitionMemberCoordinate coordinate) =>
        coordinate switch
        {
            DefinitionMemberCoordinate.PackageCoordinate package => new MemberCoordinateDto
            {
                Kind = "package",
                Id = package.Id,
                Version = package.Version,
                Framework = package.Framework,
                Rid = package.RuntimeIdentifier,
            },
            DefinitionMemberCoordinate.PlatformCoordinate platform => new MemberCoordinateDto
            {
                Kind = "platform",
                Family = platform.Family,
                Assembly = platform.Assembly,
                Version = platform.Version,
                Framework = platform.Framework,
            },
            DefinitionMemberCoordinate.EmbeddedCoordinate embedded => new MemberCoordinateDto
            {
                Kind = "embedded",
                ContentRef = embedded.ContentRef,
                Digest = embedded.Digest,
                DeclaredName = embedded.DeclaredName,
            },
            DefinitionMemberCoordinate.ProjectCoordinate project => new MemberCoordinateDto
            {
                Kind = "project",
                Path = project.Path,
                Framework = project.Framework,
                Rid = project.RuntimeIdentifier,
            },
            DefinitionMemberCoordinate.LocalCoordinate local => new MemberCoordinateDto
            {
                Kind = "local",
                Path = local.Path,
            },
            DefinitionMemberCoordinate.DirectoryCoordinate directory => new MemberCoordinateDto
            {
                Kind = "directory",
                Path = directory.Path,
                Framework = directory.Framework,
                Rid = directory.RuntimeIdentifier,
            },
            _ => throw new InspectionDefinitionException($"Unsupported coordinate type {coordinate.GetType().Name}."),
        };
}

/// <summary>Typed failure while loading or composing inspection definitions.</summary>
public sealed class InspectionDefinitionException : Exception
{
    public InspectionDefinitionException(string message)
        : base(message)
    {
    }

    public InspectionDefinitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class InspectionDefinitionDto
{
    public int SchemaVersion { get; set; }

    public string? Kind { get; set; }

    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public List<CatalogGroupDto>? Groups { get; set; }

    public List<WorkspaceContextDto>? Contexts { get; set; }

    public string? QueryId { get; set; }

    public string? Lens { get; set; }

    public string? Type { get; set; }

    public string? MemberAnchor { get; set; }

    public string? MemberSignature { get; set; }

    public string? MemberKey { get; set; }

    public string? Section { get; set; }

    public string? Library { get; set; }

    public List<NavigationTabDto>? Tabs { get; set; }

    public string? Focus { get; set; }

    public string? Workspace { get; set; }

    public string? Context { get; set; }

    public string? Input { get; set; }

    public string? Query { get; set; }

    public string? View { get; set; }

    public string? Navigation { get; set; }
}

internal sealed class CatalogGroupDto
{
    public string? Name { get; set; }

    public List<MemberCoordinateDto>? Members { get; set; }

    public List<CatalogGroupDto>? Children { get; set; }
}

internal sealed class WorkspaceContextDto
{
    public string? Name { get; set; }

    public string? Framework { get; set; }

    public string? Rid { get; set; }

    public string? RuntimeIdentifier { get; set; }

    public string? Subscribe { get; set; }

    public List<MemberCoordinateDto>? Members { get; set; }
}

internal sealed class NavigationTabDto
{
    public string? Id { get; set; }

    public MemberCoordinateDto? Coordinate { get; set; }

    public string? Subscribe { get; set; }

    public string? Framework { get; set; }

    public string? Rid { get; set; }

    public string? RuntimeIdentifier { get; set; }
}

internal sealed class MemberCoordinateDto
{
    public string? Kind { get; set; }

    public string? Id { get; set; }

    public string? Version { get; set; }

    public string? Framework { get; set; }

    public string? Rid { get; set; }

    public string? RuntimeIdentifier { get; set; }

    public string? Family { get; set; }

    public string? Assembly { get; set; }

    public string? ContentRef { get; set; }

    public string? Digest { get; set; }

    public string? DeclaredName { get; set; }

    public string? Path { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(InspectionDefinitionDto))]
internal sealed partial class InspectionDefinitionJsonContext : JsonSerializerContext;
