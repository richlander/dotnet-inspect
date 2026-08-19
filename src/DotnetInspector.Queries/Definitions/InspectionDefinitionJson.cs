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

        using var document = HardenedJson.Parse(utf8Json);
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
        return kind switch
        {
            "catalog" => new CatalogDefinition(
                dto.SchemaVersion,
                dto.Id,
                MapGroups(dto.Groups)),
            "workspace" => new WorkspaceDefinition(
                dto.SchemaVersion,
                dto.Id,
                MapContexts(dto.Contexts),
                dto.Title,
                dto.Description,
                MapGroups(dto.Groups)),
            "query" => new QueryDefinition(dto.SchemaVersion, dto.Id, dto.QueryId),
            "view" => new ViewDefinition(
                dto.SchemaVersion,
                dto.Id,
                dto.Lens,
                dto.Type,
                dto.MemberAnchor,
                dto.MemberSignature,
                dto.MemberKey,
                dto.Section,
                dto.Library),
            "navigation" => new NavigationDefinition(
                dto.SchemaVersion,
                dto.Id,
                MapTabs(dto.Tabs),
                dto.Focus ?? throw new InspectionDefinitionException("Navigation requires focus.")),
            "scenario" => new ScenarioDefinition(
                dto.SchemaVersion,
                dto.Id,
                dto.Title,
                dto.Description,
                dto.Workspace,
                dto.Context,
                dto.Input,
                dto.Query,
                dto.View,
                dto.Navigation),
            _ => throw new InspectionDefinitionException($"Unknown definition kind '{dto.Kind}'."),
        };
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

    private static List<CatalogGroupDefinition> MapGroups(List<CatalogGroupDto>? groups)
    {
        if (groups is null || groups.Count == 0)
            return [];

        return groups.Select(MapGroup).ToList();
    }

    private static CatalogGroupDefinition MapGroup(CatalogGroupDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InspectionDefinitionException("Catalog group requires name.");

        return new CatalogGroupDefinition(
            dto.Name,
            MapCoordinates(dto.Members),
            MapGroups(dto.Children));
    }

    private static List<WorkspaceContextDefinition> MapContexts(List<WorkspaceContextDto>? contexts)
    {
        if (contexts is null || contexts.Count == 0)
            throw new InspectionDefinitionException("Workspace requires at least one context.");

        return contexts.Select(MapContext).ToList();
    }

    private static WorkspaceContextDefinition MapContext(WorkspaceContextDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InspectionDefinitionException("Workspace context requires name.");

        return new WorkspaceContextDefinition(
            dto.Name,
            dto.Framework,
            dto.Rid ?? dto.RuntimeIdentifier,
            dto.Subscribe,
            MapCoordinates(dto.Members));
    }

    private static List<NavigationTabDefinition> MapTabs(List<NavigationTabDto>? tabs)
    {
        if (tabs is null || tabs.Count == 0)
            throw new InspectionDefinitionException("Navigation requires at least one tab.");

        return tabs.Select(MapTab).ToList();
    }

    private static NavigationTabDefinition MapTab(NavigationTabDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
            throw new InspectionDefinitionException("Navigation tab requires id.");

        DefinitionMemberCoordinate? coordinate = null;
        if (dto.Coordinate is not null)
            coordinate = MapCoordinate(dto.Coordinate);

        return new NavigationTabDefinition(
            dto.Id,
            coordinate,
            dto.Subscribe,
            dto.Framework,
            dto.Rid ?? dto.RuntimeIdentifier);
    }

    private static List<DefinitionMemberCoordinate> MapCoordinates(List<MemberCoordinateDto>? members)
    {
        if (members is null || members.Count == 0)
            return [];
        if (members.Count > MaxCoordinatesPerRecord)
        {
            throw new InspectionDefinitionException(
                $"Definition exceeds the {MaxCoordinatesPerRecord}-coordinate limit.");
        }

        return members.Select(MapCoordinate).ToList();
    }

    private static DefinitionMemberCoordinate MapCoordinate(MemberCoordinateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Kind))
            throw new InspectionDefinitionException("Member coordinate requires kind.");

        return dto.Kind.Trim().ToLowerInvariant() switch
        {
            "package" => new DefinitionMemberCoordinate.PackageCoordinate(
                Require(dto.Id, "package id"),
                dto.Version,
                dto.Framework,
                dto.Rid ?? dto.RuntimeIdentifier),
            "platform" => new DefinitionMemberCoordinate.PlatformCoordinate(
                Require(dto.Family, "platform family"),
                dto.Assembly,
                dto.Version,
                dto.Framework),
            "embedded" => new DefinitionMemberCoordinate.EmbeddedCoordinate(
                Require(dto.ContentRef, "embedded contentRef"),
                Require(dto.Digest, "embedded digest"),
                Require(dto.DeclaredName, "embedded declaredName")),
            "project" => new DefinitionMemberCoordinate.ProjectCoordinate(
                Require(dto.Path, "project path"),
                dto.Framework,
                dto.Rid ?? dto.RuntimeIdentifier),
            "local" => new DefinitionMemberCoordinate.LocalCoordinate(
                Require(dto.Path, "local path")),
            "directory" => new DefinitionMemberCoordinate.DirectoryCoordinate(
                Require(dto.Path, "directory path"),
                dto.Framework,
                dto.Rid ?? dto.RuntimeIdentifier),
            _ => throw new InspectionDefinitionException($"Unknown member coordinate kind '{dto.Kind}'."),
        };
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
