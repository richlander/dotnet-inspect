using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using QuerySpace;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;
using UntrustedDocuments;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Loads and emits inspection definition records. Untrusted JSON enters through
/// <see cref="HardenedJson"/> (duplicate properties rejected), then binds through a
/// source-generated context that rejects unmapped members.
/// </summary>
public static class InspectionDefinitionJson
{
    /// <summary>Maximum UTF-8 JSON bytes for one standalone definition file.</summary>
    public const int MaxUtf8ByteLength = 1_048_576;

    /// <summary>Maximum coordinates accepted across one record's nested lists.</summary>
    public const int MaxCoordinatesPerRecord = 1_024;

    /// <summary>Maximum nested catalog-group levels in one portable record.</summary>
    public const int MaxGroupDepth = 30;

    /// <summary>Maximum catalog-group nodes in one portable record.</summary>
    public const int MaxGroupsPerRecord = 1_024;

    private static readonly Encoding s_utf8Strict = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static InspectionDefinitionRecord Parse(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        int byteCount;
        byte[] utf8;
        try
        {
            byteCount = s_utf8Strict.GetByteCount(json);
            if (byteCount > MaxUtf8ByteLength)
            {
                throw new InspectionDefinitionException(
                    $"Definition JSON exceeds the {MaxUtf8ByteLength}-byte limit.");
            }

            utf8 = s_utf8Strict.GetBytes(json);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InspectionDefinitionException(
                "Definition JSON contains invalid UTF-16 text.",
                ex);
        }

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
        ValidatePortableRecord(record);
        string json;
        try
        {
            json = record is CommittedNavigationDefinition navigation
                ? JsonSerializer.Serialize(
                    ToCommittedNavigationDto(navigation),
                    InspectionDefinitionJsonContext.Default
                        .CommittedNavigationDefinitionDto)
                : JsonSerializer.Serialize(
                    ToDto(record),
                    InspectionDefinitionJsonContext.Default
                        .InspectionDefinitionDto);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InspectionDefinitionException(
                $"Definition cannot be serialized: {ex.Message}",
                ex);
        }

        var byteCount = s_utf8Strict.GetByteCount(json);
        if (byteCount > MaxUtf8ByteLength)
        {
            throw new InspectionDefinitionException(
                $"Serialized definition JSON exceeds the {MaxUtf8ByteLength}-byte limit.");
        }

        return json;
    }

    internal static void ValidatePortableRecord(
        InspectionDefinitionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureWithinPortableLimits(record);
        EnsureWellFormedUtf16(record);
    }

    /// <summary>
    /// Portable emit must reject in-memory records the parser would refuse, so serialize→parse
    /// remains a closed boundary for well-formed product definitions.
    /// </summary>
    private static void EnsureWithinPortableLimits(InspectionDefinitionRecord record)
    {
        EnsurePortableWorkspaceRegistrations(record);
        EnsureGroupLimits(record);
        var coordinateCount = CountCoordinates(record);
        if (coordinateCount > MaxCoordinatesPerRecord)
        {
            throw new InspectionDefinitionException(
                $"Definition exceeds the {MaxCoordinatesPerRecord}-coordinate limit.");
        }
    }

    private static void EnsurePortableWorkspaceRegistrations(
        InspectionDefinitionRecord record)
    {
        if (record is not WorkspaceDefinition workspace)
            return;

        for (int index = 0; index < workspace.Registrations.Count; index++)
        {
            WorkspaceRegistration registration = workspace.Registrations[index];
            switch (registration)
            {
                case WorkspaceRegistration.ExactLibrary exact
                    when exact.Coordinate is ExactLibrarySourceCoordinate.Package
                        or ExactLibrarySourceCoordinate.Platform:
                    _ = ToPortableLibraryIdentity(
                        exact.Coordinate.LibraryIdentity);
                    break;
                case WorkspaceRegistration.ExactLibrary:
                    throw new InspectionDefinitionException(
                        $"Workspace registration {index} has no portable exact-Library source coordinate.");
                case WorkspaceRegistration.PackagePrefix:
                    break;
                case WorkspaceRegistration.Ecosystem ecosystem:
                    if (ecosystem.Declaration.IntegrationScanner is not null)
                    {
                        throw new InspectionDefinitionException(
                            $"Workspace ecosystem registration '{ecosystem.Declaration.Id}' has a non-portable integration scanner.");
                    }
                    foreach (WorkspaceEcosystemPopulationDeclaration population
                        in ecosystem.Declaration.Populations)
                    {
                        if (population
                            is WorkspaceEcosystemPopulationDeclaration.ExactLibrary
                                library)
                        {
                            _ = ToPortableLibraryIdentity(
                                library.Coordinate.LibraryIdentity);
                        }
                    }
                    break;
                default:
                    throw new InspectionDefinitionException(
                        $"Workspace registration {index} has an unknown registration kind.");
            }
        }
    }

    private static void EnsureGroupLimits(InspectionDefinitionRecord record)
    {
        IReadOnlyList<CatalogGroupDefinition> roots = record switch
        {
            CatalogDefinition catalog => catalog.Groups,
            WorkspaceDefinition workspace => workspace.Groups,
            _ => Array.Empty<CatalogGroupDefinition>(),
        };
        if (roots.Count == 0)
            return;

        var pending = new Stack<(CatalogGroupDefinition Group, int Depth)>();
        for (int index = roots.Count - 1; index >= 0; index--)
            pending.Push((roots[index], 1));

        int count = 0;
        while (pending.TryPop(out var item))
        {
            count++;
            if (count > MaxGroupsPerRecord)
            {
                throw new InspectionDefinitionException(
                    $"Definition exceeds the {MaxGroupsPerRecord}-group limit.");
            }
            if (item.Depth > MaxGroupDepth)
            {
                throw new InspectionDefinitionException(
                    $"Definition exceeds the {MaxGroupDepth}-level group depth limit.");
            }

            for (int index = item.Group.Children.Count - 1; index >= 0; index--)
            {
                pending.Push((
                    item.Group.Children[index],
                    item.Depth + 1));
            }
        }
    }

    /// <summary>
    /// System.Text.Json replaces unpaired surrogates with U+FFFD. Reject before emit so identities
    /// are never silently rewritten on the portable boundary.
    /// </summary>
    private static void EnsureWellFormedUtf16(InspectionDefinitionRecord record)
    {
        switch (record)
        {
            case CatalogDefinition catalog:
                EnsureUtf16(catalog.Id, "id");
                EnsureGroupUtf16(catalog.Groups);
                break;
            case WorkspaceDefinition workspace:
                EnsureUtf16(workspace.Id, "id");
                EnsureUtf16(workspace.Title, "title");
                EnsureUtf16(workspace.Description, "description");
                foreach (var context in workspace.Contexts)
                    EnsureContextUtf16(context);
                foreach (WorkspaceRegistration registration
                    in workspace.Registrations)
                {
                    EnsureRegistrationUtf16(registration);
                }
                EnsureGroupUtf16(workspace.Groups);
                break;
            case QueryDefinition query:
                EnsureUtf16(query.Id, "id");
                EnsureUtf16(query.QueryId, "queryId");
                break;
            case CommittedQueryDefinition query:
                EnsureUtf16(query.Id, "id");
                EnsureUtf16(query.QueryId, "queryId");
                EnsureUtf16(query.Payload, "payload");
                break;
            case ViewDefinition view:
                EnsureUtf16(view.Id, "id");
                EnsureUtf16(view.Lens, "lens");
                EnsureUtf16(view.Type, "type");
                EnsureUtf16(view.MemberAnchor, "memberAnchor");
                EnsureUtf16(view.MemberSignature, "memberSignature");
                EnsureUtf16(view.MemberKey, "memberKey");
                EnsureUtf16(view.Section, "section");
                foreach (string library in view.Libraries)
                    EnsureUtf16(library, "libraries");
                break;
            case NavigationDefinition navigation:
                EnsureUtf16(navigation.Id, "id");
                EnsureUtf16(navigation.Focus, "focus");
                foreach (var tab in navigation.Tabs)
                    EnsureTabUtf16(tab);
                break;
            case CommittedNavigationDefinition navigation:
                EnsureUtf16(navigation.Id, "id");
                EnsureUtf16(navigation.Focus, "focus");
                foreach (var tab in navigation.Tabs)
                    EnsureTabUtf16(tab);
                break;
            case CommittedViewDefinition view:
                EnsureUtf16(view.Id, "id");
                foreach (CommittedViewStateDefinition state in view.States)
                {
                    EnsureUtf16(state.Navigation, "state.navigation");
                    EnsureUtf16(state.Facet, "state.facet");
                    foreach (string query in state.Queries)
                        EnsureUtf16(query, "state.queries");
                    foreach (PortableLibraryIdentity library in state.Libraries)
                        EnsureLibraryUtf16(library);
                    if (state.Context is { } context)
                        EnsureContextUtf16(context);
                }
                break;
            case ScenarioDefinition scenario:
                EnsureUtf16(scenario.Id, "id");
                EnsureUtf16(scenario.Title, "title");
                EnsureUtf16(scenario.Description, "description");
                EnsureUtf16(scenario.Workspace, "workspace");
                EnsureUtf16(scenario.Context, "context");
                EnsureUtf16(scenario.Input, "input");
                EnsureUtf16(scenario.Query, "query");
                EnsureUtf16(scenario.View, "view");
                EnsureUtf16(scenario.Navigation, "navigation");
                break;
            default:
                EnsureUtf16(record.Id, "id");
                break;
        }
    }

    private static void EnsureGroupUtf16(IReadOnlyList<CatalogGroupDefinition> groups)
    {
        foreach (var group in groups)
        {
            EnsureUtf16(group.Name, "group.name");
            foreach (var member in group.Members)
                EnsureCoordinateUtf16(member);
            EnsureGroupUtf16(group.Children);
        }
    }

    private static void EnsureContextUtf16(WorkspaceContextDefinition context)
    {
        EnsureUtf16(context.Name, "context.name");
        EnsureUtf16(context.Framework, "context.framework");
        EnsureUtf16(context.RuntimeIdentifier, "context.runtimeIdentifier");
        EnsureUtf16(context.Subscribe, "context.subscribe");
        foreach (var member in context.Members)
            EnsureCoordinateUtf16(member);
    }

    private static void EnsureRegistrationUtf16(
        WorkspaceRegistration registration)
    {
        switch (registration)
        {
            case WorkspaceRegistration.ExactLibrary exact:
                EnsureExactLibraryUtf16(exact.Coordinate);
                break;
            case WorkspaceRegistration.PackagePrefix prefix:
                EnsureUtf16(prefix.Prefix.Prefix, "registration.prefix");
                break;
            case WorkspaceRegistration.Ecosystem ecosystem:
                WorkspaceEcosystemRegistrationDeclaration declaration =
                    ecosystem.Declaration;
                EnsureUtf16(declaration.Id.Value, "registration.declaration.id");
                foreach (string root in declaration.NamespaceRoots)
                {
                    EnsureUtf16(
                        root,
                        "registration.declaration.namespaceRoots");
                }
                foreach (PackageCoordinate package in declaration.CorePackages)
                {
                    EnsureUtf16(
                        package.PackageId,
                        "registration.declaration.corePackages");
                }
                foreach (WorkspaceEcosystemPopulationDeclaration population
                    in declaration.Populations)
                {
                    switch (population)
                    {
                        case WorkspaceEcosystemPopulationDeclaration.ExactLibrary
                            library:
                            EnsureExactLibraryUtf16(library.Coordinate);
                            break;
                        case WorkspaceEcosystemPopulationDeclaration.PackagePrefix
                            prefixPopulation:
                            EnsureUtf16(
                                prefixPopulation.Prefix.Prefix,
                                "registration.declaration.populations.prefix");
                            break;
                    }
                }
                break;
        }
    }

    private static void EnsureExactLibraryUtf16(
        ExactLibrarySourceCoordinate coordinate)
    {
        switch (coordinate)
        {
            case ExactLibrarySourceCoordinate.Package package:
                EnsureUtf16(
                    package.PackageCoordinate.PackageId,
                    "registration.coordinate.id");
                EnsureUtf16(
                    package.PackageCoordinate.Version,
                    "registration.coordinate.version");
                break;
        }

        AssemblyReferenceIdentity identity = coordinate.LibraryIdentity.Identity;
        EnsureUtf16(identity.Name, "registration.coordinate.library.name");
        EnsureUtf16(identity.Culture, "registration.coordinate.library.culture");
        EnsureUtf16(
            identity.PublicKeyToken,
            "registration.coordinate.library.publicKeyToken");
    }

    private static void EnsureTabUtf16(NavigationTabDefinition tab)
    {
        EnsureUtf16(tab.Id, "tab.id");
        EnsureUtf16(tab.Subscribe, "tab.subscribe");
        EnsureUtf16(tab.Framework, "tab.framework");
        EnsureUtf16(tab.RuntimeIdentifier, "tab.runtimeIdentifier");
        if (tab.Coordinate is not null)
            EnsureCoordinateUtf16(tab.Coordinate);
    }

    private static void EnsureCoordinateUtf16(DefinitionMemberCoordinate coordinate)
    {
        switch (coordinate)
        {
            case DefinitionMemberCoordinate.PackageCoordinate package:
                EnsureUtf16(package.Id, "package.id");
                EnsureUtf16(package.Version, "package.version");
                EnsureUtf16(package.Framework, "package.framework");
                EnsureUtf16(package.RuntimeIdentifier, "package.runtimeIdentifier");
                break;
            case DefinitionMemberCoordinate.PlatformCoordinate platform:
                EnsureUtf16(platform.Family, "platform.family");
                EnsureUtf16(platform.Assembly, "platform.assembly");
                EnsureUtf16(platform.Version, "platform.version");
                EnsureUtf16(platform.Framework, "platform.framework");
                break;
            case DefinitionMemberCoordinate.EmbeddedCoordinate embedded:
                EnsureUtf16(embedded.ContentRef, "embedded.contentRef");
                EnsureUtf16(embedded.Digest, "embedded.digest");
                EnsureUtf16(embedded.DeclaredName, "embedded.declaredName");
                break;
            case DefinitionMemberCoordinate.ProjectCoordinate project:
                EnsureUtf16(project.Path, "project.path");
                EnsureUtf16(project.Framework, "project.framework");
                EnsureUtf16(project.RuntimeIdentifier, "project.runtimeIdentifier");
                break;
            case DefinitionMemberCoordinate.LocalCoordinate local:
                EnsureUtf16(local.Path, "local.path");
                break;
            case DefinitionMemberCoordinate.DirectoryCoordinate directory:
                EnsureUtf16(directory.Path, "directory.path");
                EnsureUtf16(directory.Framework, "directory.framework");
                EnsureUtf16(directory.RuntimeIdentifier, "directory.runtimeIdentifier");
                break;
        }
    }

    private static void EnsureContextUtf16(
        PortableRetainedSubjectContext context)
    {
        switch (context)
        {
            case PortableRetainedSubjectContext.Library library:
                EnsureLibraryUtf16(library.LibraryIdentity);
                break;
            case PortableRetainedSubjectContext.Type type:
                EnsureLibraryUtf16(type.LibraryIdentity);
                EnsureTypeUtf16(type.TypeIdentity);
                break;
            case PortableRetainedSubjectContext.Member member:
                EnsureLibraryUtf16(member.LibraryIdentity);
                EnsureTypeUtf16(member.TypeIdentity);
                EnsureUtf16(member.MemberAnchor, "context.memberAnchor");
                EnsureUtf16(member.MemberSignature, "context.memberSignature");
                break;
            case PortableRetainedSubjectContext.EscapedType type:
                EnsureLibraryUtf16(type.LibraryIdentity);
                EnsureUtf16(
                    type.EscapedTypeIdentity,
                    "context.escapedTypeIdentity");
                break;
            case PortableRetainedSubjectContext.EscapedMember member:
                EnsureLibraryUtf16(member.LibraryIdentity);
                EnsureUtf16(
                    member.EscapedTypeIdentity,
                    "context.escapedTypeIdentity");
                EnsureUtf16(member.MemberAnchor, "context.memberAnchor");
                EnsureUtf16(member.MemberSignature, "context.memberSignature");
                break;
        }
    }

    private static void EnsureLibraryUtf16(PortableLibraryIdentity library)
    {
        EnsureUtf16(library.Name, "library.name");
        EnsureUtf16(library.Version, "library.version");
        EnsureUtf16(library.Culture, "library.culture");
        EnsureUtf16(library.PublicKeyToken, "library.publicKeyToken");
    }

    private static void EnsureTypeUtf16(MetadataTypeDefinitionName type)
    {
        EnsureUtf16(type.Namespace, "type.namespace");
        foreach (string segment in type.Segments)
            EnsureUtf16(segment, "type.segments");
    }

    private static void EnsureUtf16(string? value, string fieldName)
    {
        if (value is null)
            return;

        try
        {
            _ = s_utf8Strict.GetByteCount(value);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InspectionDefinitionException(
                $"Definition field '{fieldName}' contains invalid UTF-16 text.",
                ex);
        }
    }

    private static int CountCoordinates(InspectionDefinitionRecord record) =>
        record switch
        {
            CatalogDefinition catalog => CountGroupCoordinates(catalog.Groups),
            WorkspaceDefinition workspace =>
                CountGroupCoordinates(workspace.Groups)
                + workspace.Contexts.Sum(context => context.Members.Count)
                + CountRegistrationCoordinates(workspace.Registrations),
            NavigationDefinition navigation =>
                navigation.Tabs.Count(tab => tab.Coordinate is not null),
            CommittedNavigationDefinition navigation =>
                navigation.Tabs.Count(tab => tab.Coordinate is not null),
            _ => 0,
        };

    private static int CountRegistrationCoordinates(
        IReadOnlyList<WorkspaceRegistration> registrations)
    {
        int count = 0;
        foreach (WorkspaceRegistration registration in registrations)
        {
            count += registration switch
            {
                WorkspaceRegistration.ExactLibrary => 1,
                WorkspaceRegistration.Ecosystem ecosystem =>
                    ecosystem.Declaration.Populations.Count(population =>
                        population
                            is WorkspaceEcosystemPopulationDeclaration
                                .ExactLibrary),
                _ => 0,
            };
        }

        return count;
    }

    private static int CountGroupCoordinates(IReadOnlyList<CatalogGroupDefinition> groups)
    {
        var count = 0;
        foreach (var group in groups)
        {
            count += group.Members.Count;
            count += CountGroupCoordinates(group.Children);
        }

        return count;
    }

    internal static InspectionDefinitionRecord Bind(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InspectionDefinitionException("Definition JSON must be a single object.");

        if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || !schemaVersion.TryGetInt32(out int version))
        {
            throw new InspectionDefinitionException(
                "Definition record requires integer schemaVersion.");
        }
        if (!InspectionDefinitionSchema.IsSupported(version))
        {
            throw new InspectionDefinitionException(
                $"Unsupported definition schema version {version}.");
        }

        ValidateRecordShape(root, version);

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

        InspectionDefinitionRecord record = FromDto(dto);
        EnsureWithinPortableLimits(record);
        return record;
    }

    private static void ValidateRecordShape(JsonElement root, int schemaVersion)
    {
        if (!TryGetExactString(root, "kind", out var kind))
            throw new InspectionDefinitionException("Definition record requires kind.");

        HashSet<string> allowed = (schemaVersion, kind) switch
        {
            (_, "catalog") => ["schemaVersion", "kind", "id", "groups"],
            (InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4, "workspace") =>
            [
                "schemaVersion", "kind", "id", "title", "description",
                "contexts", "registrations", "groups",
            ],
            (_, "workspace") =>
                ["schemaVersion", "kind", "id", "title", "description", "contexts", "groups"],
            (InspectionDefinitionSchema.Version1, "query") =>
                ["schemaVersion", "kind", "id", "queryId"],
            (InspectionDefinitionSchema.Version2
                or InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4, "query") =>
                ["schemaVersion", "kind", "id", "queryId", "payload"],
            (InspectionDefinitionSchema.Version1, "view") =>
            [
                "schemaVersion", "kind", "id", "lens", "type", "memberAnchor", "memberSignature",
                "memberKey", "section", "library", "libraries",
            ],
            (InspectionDefinitionSchema.Version1, "navigation") =>
                ["schemaVersion", "kind", "id", "tabs", "focus"],
            (InspectionDefinitionSchema.Version2
                or InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4, "view") =>
                ["schemaVersion", "kind", "id", "states"],
            (InspectionDefinitionSchema.Version2
                or InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4, "navigation") =>
                ["schemaVersion", "kind", "id", "tabs", "focus"],
            (_, "scenario") =>
            [
                "schemaVersion", "kind", "id", "title", "description", "workspace", "context",
                "input", "query", "view", "navigation",
            ],
            _ => throw new InspectionDefinitionException($"Unknown definition kind '{kind}'."),
        };

        RejectUnknownProperties(root, allowed, $"{kind} definition");

        if (root.TryGetProperty("groups", out var groups))
            ValidateGroups(groups, "groups");
        if (kind == "workspace")
        {
            if (!root.TryGetProperty(
                    "contexts",
                    out JsonElement contexts)
                || contexts.ValueKind != JsonValueKind.Array)
            {
                throw new InspectionDefinitionException(
                    "Workspace requires a contexts array.");
            }

            ValidateContexts(contexts);
        }
        if (schemaVersion is (
                InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4)
            && kind == "workspace")
        {
            if (!root.TryGetProperty(
                    "registrations",
                    out JsonElement registrations)
                || registrations.ValueKind != JsonValueKind.Array)
            {
                throw new InspectionDefinitionException(
                    $"Schema-version-{schemaVersion} Workspace requires a registrations array.");
            }

            ValidateRegistrations(registrations);
        }
        if (schemaVersion is InspectionDefinitionSchema.Version2
                or InspectionDefinitionSchema.Version3
            && kind == "query")
        {
            if (!TryGetExactString(root, "queryId", out string queryId)
                || string.IsNullOrWhiteSpace(queryId))
            {
                throw new InspectionDefinitionException(
                    $"Schema-version-{schemaVersion} query requires a nonblank queryId.");
            }
            if (!root.TryGetProperty("payload", out JsonElement payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                throw new InspectionDefinitionException(
                    $"Schema-version-{schemaVersion} query requires a payload object.");
            }
        }
        if (root.TryGetProperty("tabs", out var tabs))
            ValidateTabs(tabs);
        if (root.TryGetProperty("states", out var states))
            ValidateCommittedStates(states, schemaVersion);
        if ((schemaVersion == InspectionDefinitionSchema.Version2
                || schemaVersion == InspectionDefinitionSchema.Version3
                || schemaVersion == InspectionDefinitionSchema.Version4)
            && kind == "navigation")
        {
            if (!root.TryGetProperty("focus", out JsonElement focus)
                || focus.ValueKind is not JsonValueKind.String
                    and not JsonValueKind.Null)
            {
                throw new InspectionDefinitionException(
                    "Committed navigation requires string or null focus.");
            }
        }
    }

    private static void ValidateRegistrations(JsonElement registrations)
    {
        foreach (JsonElement registration in registrations.EnumerateArray())
        {
            if (registration.ValueKind != JsonValueKind.Object)
            {
                throw new InspectionDefinitionException(
                    "Workspace registration entry must be an object.");
            }
            if (!TryGetExactString(registration, "kind", out string kind))
            {
                throw new InspectionDefinitionException(
                    "Workspace registration requires kind.");
            }

            switch (kind)
            {
                case "exactLibrary":
                    RejectUnknownProperties(
                        registration,
                        ["kind", "coordinate"],
                        "Exact Library registration");
                    ValidateExactLibraryCoordinate(
                        RequiredProperty(
                            registration,
                            "coordinate",
                            "Exact Library registration"));
                    break;
                case "packagePrefix":
                    RejectUnknownProperties(
                        registration,
                        ["kind", "prefix"],
                        "Package Prefix registration");
                    RequireStringProperty(
                        registration,
                        "prefix",
                        "Package Prefix registration");
                    break;
                case "ecosystem":
                    RejectUnknownProperties(
                        registration,
                        ["kind", "declaration"],
                        "Ecosystem registration");
                    ValidateEcosystemDeclaration(
                        RequiredProperty(
                            registration,
                            "declaration",
                            "Ecosystem registration"));
                    break;
                default:
                    throw new InspectionDefinitionException(
                        $"Unknown Workspace registration kind '{kind}'.");
            }
        }
    }

    private static void ValidateExactLibraryCoordinate(JsonElement coordinate)
    {
        if (coordinate.ValueKind != JsonValueKind.Object)
        {
            throw new InspectionDefinitionException(
                "Exact Library coordinate must be an object.");
        }
        if (!TryGetExactString(coordinate, "kind", out string kind))
        {
            throw new InspectionDefinitionException(
                "Exact Library coordinate requires kind.");
        }

        switch (kind)
        {
            case "package":
                RejectUnknownProperties(
                    coordinate,
                    ["kind", "id", "version", "library"],
                    "Package exact Library coordinate");
                RequireStringProperty(
                    coordinate,
                    "id",
                    "Package exact Library coordinate");
                RequireStringProperty(
                    coordinate,
                    "version",
                    "Package exact Library coordinate");
                break;
            case "platform":
                RejectUnknownProperties(
                    coordinate,
                    ["kind", "family", "library"],
                    "Platform exact Library coordinate");
                RequireStringProperty(
                    coordinate,
                    "family",
                    "Platform exact Library coordinate");
                break;
            default:
                throw new InspectionDefinitionException(
                    $"Unknown exact Library coordinate kind '{kind}'.");
        }

        ValidateLibrary(
            RequiredProperty(
                coordinate,
                "library",
                "Exact Library coordinate"));
    }

    private static void ValidateEcosystemDeclaration(JsonElement declaration)
    {
        if (declaration.ValueKind != JsonValueKind.Object)
        {
            throw new InspectionDefinitionException(
                "Workspace Ecosystem declaration must be an object.");
        }
        RejectUnknownProperties(
            declaration,
            ["id", "namespaceRoots", "corePackages", "populations"],
            "Workspace Ecosystem declaration");
        RequireStringProperty(
            declaration,
            "id",
            "Workspace Ecosystem declaration");
        ValidateStringArray(
            RequiredProperty(
                declaration,
                "namespaceRoots",
                "Workspace Ecosystem declaration"),
            "Workspace Ecosystem namespaceRoots");
        ValidateStringArray(
            RequiredProperty(
                declaration,
                "corePackages",
                "Workspace Ecosystem declaration"),
            "Workspace Ecosystem corePackages");

        JsonElement populations = RequiredProperty(
            declaration,
            "populations",
            "Workspace Ecosystem declaration");
        if (populations.ValueKind != JsonValueKind.Array)
        {
            throw new InspectionDefinitionException(
                "Workspace Ecosystem populations must be an array.");
        }
        foreach (JsonElement population in populations.EnumerateArray())
        {
            if (population.ValueKind != JsonValueKind.Object
                || !TryGetExactString(population, "kind", out string kind))
            {
                throw new InspectionDefinitionException(
                    "Workspace Ecosystem population requires an object with kind.");
            }

            switch (kind)
            {
                case "exactLibrary":
                    RejectUnknownProperties(
                        population,
                        ["kind", "coordinate"],
                        "Exact Library Ecosystem population");
                    ValidateExactLibraryCoordinate(
                        RequiredProperty(
                            population,
                            "coordinate",
                            "Exact Library Ecosystem population"));
                    break;
                case "platform":
                    RejectUnknownProperties(
                        population,
                        ["kind", "family"],
                        "Platform Ecosystem population");
                    RequireStringProperty(
                        population,
                        "family",
                        "Platform Ecosystem population");
                    break;
                case "packagePrefix":
                    RejectUnknownProperties(
                        population,
                        ["kind", "prefix"],
                        "Package Prefix Ecosystem population");
                    RequireStringProperty(
                        population,
                        "prefix",
                        "Package Prefix Ecosystem population");
                    break;
                default:
                    throw new InspectionDefinitionException(
                        $"Unknown Workspace Ecosystem population kind '{kind}'.");
            }
        }
    }

    private static JsonElement RequiredProperty(
        JsonElement owner,
        string name,
        string description)
    {
        if (!owner.TryGetProperty(name, out JsonElement property))
        {
            throw new InspectionDefinitionException(
                $"{description} requires {name}.");
        }

        return property;
    }

    private static void RequireStringProperty(
        JsonElement owner,
        string name,
        string description)
    {
        if (RequiredProperty(owner, name, description).ValueKind
            != JsonValueKind.String)
        {
            throw new InspectionDefinitionException(
                $"{description} {name} must be a string.");
        }
    }

    private static void ValidateStringArray(
        JsonElement values,
        string description)
    {
        if (values.ValueKind != JsonValueKind.Array
            || values.EnumerateArray().Any(
                value => value.ValueKind != JsonValueKind.String))
        {
            throw new InspectionDefinitionException(
                $"{description} must be an array of strings.");
        }
    }

    private static void ValidateCommittedStates(
        JsonElement states,
        int schemaVersion)
    {
        if (states.ValueKind != JsonValueKind.Array)
            throw new InspectionDefinitionException("states must be an array.");

        foreach (JsonElement state in states.EnumerateArray())
        {
            if (state.ValueKind != JsonValueKind.Object)
            {
                throw new InspectionDefinitionException(
                    "Committed view state entry must be an object.");
            }

            RejectUnknownProperties(
                state,
                ["navigation", "subject", "context", "facet", "queries", "libraries"],
                "Committed view state");
            if (!state.TryGetProperty(
                    "navigation",
                    out JsonElement navigation)
                || navigation.ValueKind is not JsonValueKind.String
                    and not JsonValueKind.Null)
            {
                throw new InspectionDefinitionException(
                    "Committed view state requires string or null navigation.");
            }
            if (state.TryGetProperty("subject", out JsonElement subject))
                ValidateSubject(subject, schemaVersion);
            if (state.TryGetProperty("context", out JsonElement context))
                ValidateRetainedContext(context);
            if (state.TryGetProperty("queries", out JsonElement queries)
                && queries.ValueKind != JsonValueKind.Array)
            {
                throw new InspectionDefinitionException(
                    "Committed view state queries must be an array.");
            }
            if (state.TryGetProperty("libraries", out JsonElement libraries))
                ValidateLibraries(libraries);
        }
    }

    private static void ValidateSubject(
        JsonElement subject,
        int schemaVersion)
    {
        if (subject.ValueKind != JsonValueKind.Object)
        {
            throw new InspectionDefinitionException(
                "Committed subject must be an object.");
        }
        if (!TryGetExactString(subject, "kind", out string kind))
            throw new InspectionDefinitionException("Committed subject requires kind.");
        if (kind is not "workspace" and not "package"
            && (schemaVersion != InspectionDefinitionSchema.Version4
                || kind is not ("library" or "type" or "member")))
        {
            throw new InspectionDefinitionException(
                $"Committed subject kind '{kind}' is not supported by schema version {schemaVersion}.");
        }

        RejectUnknownProperties(subject, ["kind"], "Committed subject");
    }

    private static void ValidateRetainedContext(JsonElement context)
    {
        if (context.ValueKind != JsonValueKind.Object)
        {
            throw new InspectionDefinitionException(
                "Retained context must be an object.");
        }
        if (!TryGetExactString(context, "kind", out string kind))
            throw new InspectionDefinitionException("Retained context requires kind.");

        HashSet<string> allowed = kind switch
        {
            "package" or "allLibraries" => ["kind"],
            "library" => ["kind", "library"],
            "type" => ["kind", "library", "type"],
            "member" =>
                ["kind", "library", "type", "memberAnchor", "memberSignature"],
            _ => throw new InspectionDefinitionException(
                $"Unknown retained context kind '{kind}'."),
        };
        RejectUnknownProperties(context, allowed, "Retained context");

        if (context.TryGetProperty("library", out JsonElement library))
            ValidateLibrary(library);
        if (context.TryGetProperty("type", out JsonElement type))
            ValidateType(type);
    }

    private static void ValidateLibraries(JsonElement libraries)
    {
        if (libraries.ValueKind != JsonValueKind.Array)
        {
            throw new InspectionDefinitionException(
                "Committed Library scope must be an array.");
        }
        foreach (JsonElement library in libraries.EnumerateArray())
            ValidateLibrary(library);
    }

    private static void ValidateLibrary(JsonElement library)
    {
        if (library.ValueKind != JsonValueKind.Object)
        {
            throw new InspectionDefinitionException(
                "Portable Library identity must be an object.");
        }
        RejectUnknownProperties(
            library,
            ["name", "version", "culture", "publicKeyToken"],
            "Portable Library identity");
        foreach (string required in
            new[] { "name", "version", "culture", "publicKeyToken" })
        {
            if (!library.TryGetProperty(required, out JsonElement value))
            {
                throw new InspectionDefinitionException(
                    $"Portable Library identity requires {required}.");
            }
            if (required is "culture" or "publicKeyToken")
            {
                if (value.ValueKind is not JsonValueKind.String
                    and not JsonValueKind.Null)
                {
                    throw new InspectionDefinitionException(
                        $"Portable Library identity {required} must be string or null.");
                }
            }
            else if (value.ValueKind != JsonValueKind.String)
            {
                throw new InspectionDefinitionException(
                    $"Portable Library identity {required} must be a string.");
            }
        }
    }

    private static void ValidateType(JsonElement type)
    {
        if (type.ValueKind != JsonValueKind.Object)
        {
            throw new InspectionDefinitionException(
                "Portable Type identity must be an object.");
        }
        RejectUnknownProperties(
            type,
            ["namespace", "segments"],
            "Portable Type identity");
        if (!type.TryGetProperty("namespace", out JsonElement @namespace)
            || @namespace.ValueKind != JsonValueKind.String
            || !type.TryGetProperty("segments", out JsonElement segments)
            || segments.ValueKind != JsonValueKind.Array)
        {
            throw new InspectionDefinitionException(
                "Portable Type identity requires string namespace and array segments.");
        }
    }

    private static void ValidateGroups(JsonElement groups, string path)
    {
        if (groups.ValueKind != JsonValueKind.Array)
            throw new InspectionDefinitionException($"{path} must be an array.");

        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind == JsonValueKind.Null)
                throw new InspectionDefinitionException("Catalog group entry must not be null.");
            if (group.ValueKind != JsonValueKind.Object)
                throw new InspectionDefinitionException("Catalog group entry must be an object.");

            RejectUnknownProperties(
                group,
                ["name", "members", "children"],
                "Catalog group");
            if (group.TryGetProperty("members", out var members))
                ValidateCoordinates(members, "members");
            if (group.TryGetProperty("children", out var children))
                ValidateGroups(children, "children");
        }
    }

    private static void ValidateContexts(JsonElement contexts)
    {
        if (contexts.ValueKind != JsonValueKind.Array)
            throw new InspectionDefinitionException("contexts must be an array.");

        foreach (var context in contexts.EnumerateArray())
        {
            if (context.ValueKind == JsonValueKind.Null)
                throw new InspectionDefinitionException("Workspace context entry must not be null.");
            if (context.ValueKind != JsonValueKind.Object)
                throw new InspectionDefinitionException("Workspace context entry must be an object.");

            RejectUnknownProperties(
                context,
                ["name", "framework", "rid", "runtimeIdentifier", "subscribe", "members"],
                "Workspace context");
            RejectDualRuntimeIdentifierProperties(context, "Workspace context");
            if (context.TryGetProperty("members", out var members))
                ValidateCoordinates(members, "members");
        }
    }

    private static void ValidateTabs(JsonElement tabs)
    {
        if (tabs.ValueKind != JsonValueKind.Array)
            throw new InspectionDefinitionException("tabs must be an array.");

        foreach (var tab in tabs.EnumerateArray())
        {
            if (tab.ValueKind == JsonValueKind.Null)
                throw new InspectionDefinitionException("Navigation tab entry must not be null.");
            if (tab.ValueKind != JsonValueKind.Object)
                throw new InspectionDefinitionException("Navigation tab entry must be an object.");

            RejectUnknownProperties(
                tab,
                ["id", "coordinate", "subscribe", "framework", "rid", "runtimeIdentifier"],
                "Navigation tab");
            RejectDualRuntimeIdentifierProperties(tab, "Navigation tab");
            if (tab.TryGetProperty("coordinate", out var coordinate))
            {
                if (coordinate.ValueKind == JsonValueKind.Null)
                    throw new InspectionDefinitionException("Navigation tab coordinate must not be null.");
                ValidateCoordinateObject(coordinate, "Navigation tab coordinate");
            }
        }
    }

    private static void ValidateCoordinates(JsonElement members, string path)
    {
        if (members.ValueKind != JsonValueKind.Array)
            throw new InspectionDefinitionException($"{path} must be an array.");

        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind == JsonValueKind.Null)
                throw new InspectionDefinitionException("Member coordinate entry must not be null.");
            ValidateCoordinateObject(member, "Member coordinate");
        }
    }

    private static void ValidateCoordinateObject(JsonElement coordinate, string owner)
    {
        if (coordinate.ValueKind != JsonValueKind.Object)
            throw new InspectionDefinitionException($"{owner} must be an object.");
        if (!TryGetExactString(coordinate, "kind", out var kind))
            throw new InspectionDefinitionException($"{owner} requires kind.");

        HashSet<string> allowed = kind switch
        {
            "package" => ["kind", "id", "version", "framework", "rid", "runtimeIdentifier"],
            "platform" => ["kind", "family", "assembly", "version", "framework"],
            "embedded" => ["kind", "contentRef", "digest", "declaredName"],
            "project" => ["kind", "path", "framework", "rid", "runtimeIdentifier"],
            "local" => ["kind", "path"],
            "directory" => ["kind", "path", "framework", "rid", "runtimeIdentifier"],
            _ => throw new InspectionDefinitionException($"Unknown member coordinate kind '{kind}'."),
        };

        RejectUnknownProperties(coordinate, allowed, $"{kind} coordinate");
        RejectDualRuntimeIdentifierProperties(coordinate, $"{kind} coordinate");
    }

    private static void RejectDualRuntimeIdentifierProperties(JsonElement obj, string owner)
    {
        if (obj.TryGetProperty("rid", out _) && obj.TryGetProperty("runtimeIdentifier", out _))
        {
            throw new InspectionDefinitionException(
                $"{owner} specifies both rid and runtimeIdentifier; use only one spelling.");
        }
    }

    private static void RejectUnknownProperties(JsonElement obj, HashSet<string> allowed, string owner)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new InspectionDefinitionException(
                    $"{owner} must not set '{property.Name}'.");
            }
        }
    }

    private static bool TryGetExactString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        try
        {
            value = property.GetString() ?? "";
        }
        catch (InvalidOperationException ex)
        {
            throw new InspectionDefinitionException(
                $"Definition JSON string '{name}' is not well-formed UTF-16.",
                ex);
        }

        return value.Length > 0;
    }

    internal static InspectionDefinitionRecord FromDto(InspectionDefinitionDto dto)
    {
        if (!InspectionDefinitionSchema.IsSupported(dto.SchemaVersion))
        {
            throw new InspectionDefinitionException(
                $"Unsupported definition schema version {dto.SchemaVersion}.");
        }

        if (string.IsNullOrEmpty(dto.Kind))
            throw new InspectionDefinitionException("Definition record requires kind.");
        if (string.IsNullOrWhiteSpace(dto.Id))
            throw new InspectionDefinitionException("Definition record requires id.");

        // Kind spelling is closed by ValidateRecordShape (exact ordinal match).
        var kind = dto.Kind;
        var coordinateCount = 0;
        try
        {
            return (dto.SchemaVersion, kind) switch
            {
                (_, "catalog") => CreateCatalog(dto, ref coordinateCount),
                (_, "workspace") => CreateWorkspace(dto, ref coordinateCount),
                (InspectionDefinitionSchema.Version1, "query") =>
                    CreateQuery(dto),
                (InspectionDefinitionSchema.Version2
                    or InspectionDefinitionSchema.Version3
                    or InspectionDefinitionSchema.Version4, "query") =>
                    CreateCommittedQuery(dto),
                (InspectionDefinitionSchema.Version1, "view") =>
                    CreateView(dto),
                (InspectionDefinitionSchema.Version1, "navigation") =>
                    CreateNavigation(dto, ref coordinateCount),
                (InspectionDefinitionSchema.Version2
                    or InspectionDefinitionSchema.Version3
                    or InspectionDefinitionSchema.Version4, "view") =>
                    CreateCommittedView(dto),
                (InspectionDefinitionSchema.Version2
                    or InspectionDefinitionSchema.Version3
                    or InspectionDefinitionSchema.Version4, "navigation") =>
                    CreateCommittedNavigation(dto, ref coordinateCount),
                (_, "scenario") => CreateScenario(dto),
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
            libraries: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true,
            states: true);
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
            registrations: false,
            queryId: true,
            lens: true,
            type: true,
            memberAnchor: true,
            memberSignature: true,
            memberKey: true,
            section: true,
            library: true,
            libraries: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true,
            states: true);
        return new WorkspaceDefinition(
            dto.SchemaVersion,
            dto.Id!,
            MapContexts(
                dto.Contexts,
                dto.SchemaVersion,
                ref coordinateCount),
            dto.Title,
            dto.Description,
            MapGroups(dto.Groups, ref coordinateCount),
            MapRegistrations(
                dto.Registrations,
                dto.SchemaVersion));
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
            libraries: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true,
            states: true);
        return new QueryDefinition(dto.SchemaVersion, dto.Id!, dto.QueryId);
    }

    private static CommittedQueryDefinition CreateCommittedQuery(
        InspectionDefinitionDto dto)
    {
        RejectForeignRecordFields(
            dto,
            "query",
            title: true,
            description: true,
            groups: true,
            contexts: true,
            queryId: false,
            payload: false,
            lens: true,
            type: true,
            memberAnchor: true,
            memberSignature: true,
            memberKey: true,
            section: true,
            library: true,
            libraries: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true,
            states: true);
        if (string.IsNullOrWhiteSpace(dto.QueryId)
            || dto.Payload is not JsonElement payload)
        {
            throw new InspectionDefinitionException(
                $"Schema-version-{dto.SchemaVersion} query requires queryId and payload.");
        }

        try
        {
            PortableQueryIntent intent =
                PortableQueryPayloadCodec.ParseJson(payload.GetRawText());
            return new CommittedQueryDefinition(
                dto.SchemaVersion,
                dto.Id!,
                PortableQueryIdentity.Create(dto.QueryId, intent));
        }
        catch (PortableQueryPayloadException ex)
        {
            throw new InspectionDefinitionException(
                $"Query '{dto.Id}' payload is invalid: {ex.Message}",
                ex);
        }
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
            navigation: true,
            states: true);
        if (dto.Library is null && dto.Libraries is { Count: < 2 })
        {
            throw new InspectionDefinitionException(
                "View field 'libraries' requires at least two identities; use 'library' for one.");
        }

        return new ViewDefinition(
            dto.SchemaVersion,
            dto.Id!,
            dto.Lens,
            dto.Type,
            dto.MemberAnchor,
            dto.MemberSignature,
            dto.MemberKey,
            dto.Section,
            dto.Library,
            dto.Libraries);
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
            libraries: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true,
            states: true);
        return new NavigationDefinition(
            dto.SchemaVersion,
            dto.Id!,
            MapTabs(
                dto.Tabs,
                InspectionDefinitionSchema.Version1,
                ref coordinateCount),
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
            libraries: true,
            tabs: true,
            focus: true,
            states: true);
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

    private static CommittedNavigationDefinition CreateCommittedNavigation(
        InspectionDefinitionDto dto,
        ref int coordinateCount)
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
            libraries: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true,
            states: true);
        return new CommittedNavigationDefinition(
            dto.SchemaVersion,
            dto.Id!,
            MapTabs(
                dto.Tabs,
                dto.SchemaVersion,
                ref coordinateCount),
            dto.Focus);
    }

    private static CommittedViewDefinition CreateCommittedView(
        InspectionDefinitionDto dto)
    {
        RejectForeignRecordFields(
            dto,
            "view",
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
            libraries: true,
            tabs: true,
            focus: true,
            workspace: true,
            context: true,
            input: true,
            query: true,
            view: true,
            navigation: true);
        return new CommittedViewDefinition(
            dto.SchemaVersion,
            dto.Id!,
            MapCommittedStates(
                dto.States,
                dto.SchemaVersion));
    }

    private static void RejectForeignRecordFields(
        InspectionDefinitionDto dto,
        string kind,
        bool title = false,
        bool description = false,
        bool groups = false,
        bool contexts = false,
        bool queryId = false,
        bool payload = true,
        bool lens = false,
        bool type = false,
        bool memberAnchor = false,
        bool memberSignature = false,
        bool memberKey = false,
        bool section = false,
        bool library = false,
        bool libraries = false,
        bool tabs = false,
        bool focus = false,
        bool workspace = false,
        bool context = false,
        bool input = false,
        bool query = false,
        bool view = false,
        bool navigation = false,
        bool states = false,
        bool registrations = true)
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
        Check(payload, "payload", dto.Payload);
        Check(lens, "lens", dto.Lens);
        Check(type, "type", dto.Type);
        Check(memberAnchor, "memberAnchor", dto.MemberAnchor);
        Check(memberSignature, "memberSignature", dto.MemberSignature);
        Check(memberKey, "memberKey", dto.MemberKey);
        Check(section, "section", dto.Section);
        Check(library, "library", dto.Library);
        Check(libraries, "libraries", dto.Libraries);
        Check(tabs, "tabs", dto.Tabs);
        Check(focus, "focus", dto.Focus);
        Check(workspace, "workspace", dto.Workspace);
        Check(context, "context", dto.Context);
        Check(input, "input", dto.Input);
        Check(query, "query", dto.Query);
        Check(view, "view", dto.View);
        Check(navigation, "navigation", dto.Navigation);
        Check(states, "states", dto.States);
        Check(registrations, "registrations", dto.Registrations);
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
                Registrations =
                    workspace.SchemaVersion is InspectionDefinitionSchema.Version3
                        or InspectionDefinitionSchema.Version4
                        ? workspace.Registrations
                            .Select(ToRegistrationDto)
                            .ToList()
                        : null,
                Groups = workspace.Groups.Count == 0 ? null : workspace.Groups.Select(ToGroupDto).ToList(),
            },
            QueryDefinition query => new InspectionDefinitionDto
            {
                SchemaVersion = query.SchemaVersion,
                Kind = "query",
                Id = query.Id,
                QueryId = query.QueryId,
            },
            CommittedQueryDefinition query => new InspectionDefinitionDto
            {
                SchemaVersion = query.SchemaVersion,
                Kind = "query",
                Id = query.Id,
                QueryId = query.QueryId,
                Payload = ToPayloadElement(query.Payload),
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
                Library = view.Libraries.Count == 1 ? view.Libraries[0] : null,
                Libraries = view.Libraries.Count > 1 ? view.Libraries.ToList() : null,
            },
            NavigationDefinition navigation => new InspectionDefinitionDto
            {
                SchemaVersion = navigation.SchemaVersion,
                Kind = "navigation",
                Id = navigation.Id,
                Tabs = navigation.Tabs.Select(ToTabDto).ToList(),
                Focus = navigation.Focus,
            },
            CommittedNavigationDefinition navigation => new InspectionDefinitionDto
            {
                SchemaVersion = navigation.SchemaVersion,
                Kind = "navigation",
                Id = navigation.Id,
                Tabs = navigation.Tabs.Select(ToTabDto).ToList(),
                Focus = navigation.Focus,
            },
            CommittedViewDefinition view => new InspectionDefinitionDto
            {
                SchemaVersion = view.SchemaVersion,
                Kind = "view",
                Id = view.Id,
                States = view.States
                    .Select(state => ToCommittedStateDto(
                        state,
                        view.SchemaVersion))
                    .ToList(),
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

    private static JsonElement ToPayloadElement(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<CommittedViewStateDefinition>
        MapCommittedStates(
            List<CommittedViewStateDto>? states,
            int schemaVersion)
    {
        if (states is null || states.Count == 0)
        {
            throw new InspectionDefinitionException(
                "Committed view requires at least one state.");
        }

        var mapped = new List<CommittedViewStateDefinition>(states.Count);
        foreach (CommittedViewStateDto? state in states)
        {
            if (state is null)
            {
                throw new InspectionDefinitionException(
                    "Committed view state entry must not be null.");
            }

            mapped.Add(new CommittedViewStateDefinition(
                state.Navigation,
                MapSubject(state.Subject, schemaVersion),
                MapRetainedContext(state.Context),
                state.Facet,
                state.Queries,
                MapLibraries(state.Libraries)));
        }

        return mapped;
    }

    private static PortableSubjectRequest? MapSubject(
        PortableSubjectRequestDto? subject,
        int schemaVersion)
    {
        if (subject is null)
            return null;

        return subject.Kind switch
        {
            "workspace" => new PortableSubjectRequest.Workspace(),
            "package" => new PortableSubjectRequest.Package(),
            "library" when schemaVersion == InspectionDefinitionSchema.Version4 =>
                new PortableSubjectRequest.Library(),
            "type" when schemaVersion == InspectionDefinitionSchema.Version4 =>
                new PortableSubjectRequest.Type(),
            "member" when schemaVersion == InspectionDefinitionSchema.Version4 =>
                new PortableSubjectRequest.Member(),
            _ => throw new InspectionDefinitionException(
                $"Committed subject kind '{subject.Kind}' is not supported by schema version {schemaVersion}."),
        };
    }

    private static PortableRetainedSubjectContext? MapRetainedContext(
        PortableRetainedSubjectContextDto? context)
    {
        if (context is null)
            return null;

        return context.Kind switch
        {
            "package" => new PortableRetainedSubjectContext.Package(),
            "allLibraries" => new PortableRetainedSubjectContext.AllLibraries(),
            "library" => new PortableRetainedSubjectContext.Library(
                MapRequiredLibrary(context.Library)),
            "type" => new PortableRetainedSubjectContext.Type(
                MapRequiredLibrary(context.Library),
                MapRequiredType(context.Type)),
            "member" => new PortableRetainedSubjectContext.Member(
                MapRequiredLibrary(context.Library),
                MapRequiredType(context.Type),
                context.MemberAnchor,
                context.MemberSignature),
            _ => throw new InspectionDefinitionException(
                $"Unknown retained context kind '{context.Kind}'."),
        };
    }

    private static IReadOnlyList<PortableLibraryIdentity> MapLibraries(
        List<PortableLibraryIdentityDto>? libraries)
    {
        if (libraries is null || libraries.Count == 0)
            return Array.Empty<PortableLibraryIdentity>();

        var mapped = new List<PortableLibraryIdentity>(libraries.Count);
        foreach (PortableLibraryIdentityDto? library in libraries)
        {
            if (library is null)
            {
                throw new InspectionDefinitionException(
                    "Portable Library identity entry must not be null.");
            }
            mapped.Add(MapLibrary(library));
        }

        return mapped;
    }

    private static PortableLibraryIdentity MapRequiredLibrary(
        PortableLibraryIdentityDto? library) =>
        library is null
            ? throw new InspectionDefinitionException(
                "Retained context requires library.")
            : MapLibrary(library);

    private static PortableLibraryIdentity MapLibrary(
        PortableLibraryIdentityDto library) =>
        new(
            library.Name
                ?? throw new InspectionDefinitionException(
                    "Portable Library identity requires name."),
            library.Version
                ?? throw new InspectionDefinitionException(
                    "Portable Library identity requires version."),
            library.Culture,
            library.PublicKeyToken);

    private static MetadataTypeDefinitionName MapRequiredType(
        PortableTypeDefinitionNameDto? type)
    {
        if (type is null)
        {
            throw new InspectionDefinitionException(
                "Retained context requires type.");
        }
        if (type.Namespace is null || type.Segments is null)
        {
            throw new InspectionDefinitionException(
                "Portable Type identity requires namespace and segments.");
        }
        if (type.Segments.Any(segment => segment is null))
        {
            throw new InspectionDefinitionException(
                "Portable Type identity segments must not contain null.");
        }

        MetadataTypeDefinitionNameResult result =
            MetadataTypeDefinitionName.Create(
                type.Namespace,
                type.Segments.ToImmutableArray()!);
        return result switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            MetadataTypeDefinitionNameResult.Rejected rejected =>
                throw new InspectionDefinitionException(
                    $"Invalid portable Type identity: {rejected.Rejection.Kind}."),
            _ => throw new InspectionDefinitionException(
                "Unexpected portable Type identity result."),
        };
    }

    private static CommittedViewStateDto ToCommittedStateDto(
        CommittedViewStateDefinition state,
        int schemaVersion) =>
        new()
        {
            Navigation = state.Navigation,
            Subject = state.Subject is null
                ? null
                : ToSubjectDto(state.Subject, schemaVersion),
            Context = state.Context is null ? null : ToContextDto(state.Context),
            Facet = state.Facet,
            Queries = state.Queries.Count == 0
                ? null
                : state.Queries.ToList(),
            Libraries = state.Libraries.Count == 0
                ? null
                : state.Libraries.Select(ToLibraryDto).ToList(),
        };

    private static CommittedNavigationDefinitionDto ToCommittedNavigationDto(
        CommittedNavigationDefinition navigation) =>
        new()
        {
            SchemaVersion = navigation.SchemaVersion,
            Kind = "navigation",
            Id = navigation.Id,
            Tabs = navigation.Tabs.Select(ToTabDto).ToList(),
            Focus = navigation.Focus,
        };

    private static PortableSubjectRequestDto ToSubjectDto(
        PortableSubjectRequest subject,
        int schemaVersion) =>
        new()
        {
            Kind = subject.Kind switch
            {
                PortableSubjectRequestKind.Workspace => "workspace",
                PortableSubjectRequestKind.Package => "package",
                PortableSubjectRequestKind.Library
                    when schemaVersion == InspectionDefinitionSchema.Version4 =>
                    "library",
                PortableSubjectRequestKind.Type
                    when schemaVersion == InspectionDefinitionSchema.Version4 =>
                    "type",
                PortableSubjectRequestKind.Member
                    when schemaVersion == InspectionDefinitionSchema.Version4 =>
                    "member",
                _ => throw new InspectionDefinitionException(
                    $"Portable subject kind {subject.Kind} is not supported by schema version {schemaVersion}."),
            },
        };

    private static PortableRetainedSubjectContextDto ToContextDto(
        PortableRetainedSubjectContext context) =>
        context switch
        {
            PortableRetainedSubjectContext.Package => new()
            {
                Kind = "package",
            },
            PortableRetainedSubjectContext.AllLibraries => new()
            {
                Kind = "allLibraries",
            },
            PortableRetainedSubjectContext.Library library => new()
            {
                Kind = "library",
                Library = ToLibraryDto(library.LibraryIdentity),
            },
            PortableRetainedSubjectContext.Type type => new()
            {
                Kind = "type",
                Library = ToLibraryDto(type.LibraryIdentity),
                Type = ToTypeDto(type.TypeIdentity),
            },
            PortableRetainedSubjectContext.Member member => new()
            {
                Kind = "member",
                Library = ToLibraryDto(member.LibraryIdentity),
                Type = ToTypeDto(member.TypeIdentity),
                MemberAnchor = member.MemberAnchor,
                MemberSignature = member.MemberSignature,
            },
            _ => throw new InspectionDefinitionException(
                $"Unsupported retained context type {context.GetType().Name}."),
        };

    private static PortableLibraryIdentityDto ToLibraryDto(
        PortableLibraryIdentity library) =>
        new()
        {
            Name = library.Name,
            Version = library.Version,
            Culture = library.Culture,
            PublicKeyToken = library.PublicKeyToken,
        };

    private static WorkspaceRegistrationDto ToRegistrationDto(
        WorkspaceRegistration registration) =>
        registration switch
        {
            WorkspaceRegistration.ExactLibrary exact => new()
            {
                Kind = "exactLibrary",
                Coordinate = ToExactLibraryCoordinateDto(exact.Coordinate),
            },
            WorkspaceRegistration.PackagePrefix prefix => new()
            {
                Kind = "packagePrefix",
                Prefix = prefix.Prefix.Prefix,
            },
            WorkspaceRegistration.Ecosystem ecosystem => new()
            {
                Kind = "ecosystem",
                Declaration = new WorkspaceEcosystemDeclarationDto
                {
                    Id = ecosystem.Declaration.Id.Value,
                    NamespaceRoots =
                        [.. ecosystem.Declaration.NamespaceRoots],
                    CorePackages =
                    [
                        .. ecosystem.Declaration.CorePackages.Select(
                            package => package.PackageId),
                    ],
                    Populations =
                    [
                        .. ecosystem.Declaration.Populations.Select(
                            ToEcosystemPopulationDto),
                    ],
                },
            },
            _ => throw new InspectionDefinitionException(
                $"Unsupported Workspace registration type {registration.GetType().Name}."),
        };

    private static ExactLibrarySourceCoordinateDto
        ToExactLibraryCoordinateDto(
            ExactLibrarySourceCoordinate coordinate)
    {
        PortableLibraryIdentityDto library = ToLibraryDto(
            ToPortableLibraryIdentity(coordinate.LibraryIdentity));
        return coordinate switch
        {
            ExactLibrarySourceCoordinate.Package package => new()
            {
                Kind = "package",
                Id = package.PackageCoordinate.PackageId,
                Version = package.PackageCoordinate.Version,
                Library = library,
            },
            ExactLibrarySourceCoordinate.Platform platform => new()
            {
                Kind = "platform",
                Family = platform.Population.Family.ToString(),
                Library = library,
            },
            _ => throw new InspectionDefinitionException(
                "Exact Library registration has no portable source coordinate."),
        };
    }

    private static WorkspaceEcosystemPopulationDto
        ToEcosystemPopulationDto(
            WorkspaceEcosystemPopulationDeclaration population) =>
        population switch
        {
            WorkspaceEcosystemPopulationDeclaration.ExactLibrary exact =>
                new()
                {
                    Kind = "exactLibrary",
                    Coordinate =
                        ToExactLibraryCoordinateDto(exact.Coordinate),
                },
            WorkspaceEcosystemPopulationDeclaration.Platform platform =>
                new()
                {
                    Kind = "platform",
                    Family = platform.Population.Family.ToString(),
                },
            WorkspaceEcosystemPopulationDeclaration.PackagePrefix prefix =>
                new()
                {
                    Kind = "packagePrefix",
                    Prefix = prefix.Prefix.Prefix,
                },
            _ => throw new InspectionDefinitionException(
                $"Unsupported Ecosystem population type {population.GetType().Name}."),
        };

    private static PortableLibraryIdentity ToPortableLibraryIdentity(
        ManagedMetadataIdentity.Assembly library)
    {
        AssemblyReferenceIdentity identity = library.Identity;
        Version version = identity.Version
            ?? throw new InspectionDefinitionException(
                "An exact Library registration requires an assembly version.");
        return new PortableLibraryIdentity(
            identity.Name,
            version.ToString(4),
            identity.Culture,
            identity.PublicKeyToken);
    }

    private static PortableTypeDefinitionNameDto ToTypeDto(
        MetadataTypeDefinitionName type) =>
        new()
        {
            Namespace = type.Namespace,
            Segments = type.Segments.ToList(),
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
        int schemaVersion,
        ref int coordinateCount)
    {
        if (contexts is null)
            throw new InspectionDefinitionException("Workspace requires contexts.");
        if (contexts.Count == 0
            && schemaVersion is not (
                InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4))
        {
            throw new InspectionDefinitionException("Workspace requires at least one context.");
        }

        var mapped = new List<WorkspaceContextDefinition>(contexts.Count);
        foreach (var context in contexts)
        {
            if (context is null)
                throw new InspectionDefinitionException("Workspace context entry must not be null.");
            mapped.Add(MapContext(context, ref coordinateCount));
        }

        return mapped;
    }

    private static List<WorkspaceRegistration> MapRegistrations(
        List<WorkspaceRegistrationDto>? registrations,
        int schemaVersion)
    {
        if (schemaVersion is not (
            InspectionDefinitionSchema.Version3
            or InspectionDefinitionSchema.Version4))
            return [];
        if (registrations is null)
        {
            throw new InspectionDefinitionException(
                $"Schema-version-{schemaVersion} Workspace requires registrations.");
        }

        var mapped = new List<WorkspaceRegistration>(registrations.Count);
        foreach (WorkspaceRegistrationDto? registration in registrations)
        {
            if (registration is null)
            {
                throw new InspectionDefinitionException(
                    "Workspace registration entry must not be null.");
            }

            mapped.Add(registration.Kind switch
            {
                "exactLibrary" => new WorkspaceRegistration.ExactLibrary(
                    MapExactLibraryCoordinate(registration.Coordinate)),
                "packagePrefix" => new WorkspaceRegistration.PackagePrefix(
                    new PackagePrefixDeclaration(
                        registration.Prefix
                            ?? throw new InspectionDefinitionException(
                                "Package Prefix registration requires prefix."))),
                "ecosystem" => new WorkspaceRegistration.Ecosystem(
                    MapEcosystemDeclaration(registration.Declaration)),
                _ => throw new InspectionDefinitionException(
                    $"Unknown Workspace registration kind '{registration.Kind}'."),
            });
        }

        return mapped;
    }

    private static ExactLibrarySourceCoordinate MapExactLibraryCoordinate(
        ExactLibrarySourceCoordinateDto? coordinate)
    {
        if (coordinate is null)
        {
            throw new InspectionDefinitionException(
                "Exact Library registration requires coordinate.");
        }

        PortableLibraryIdentity library =
            MapRequiredLibrary(coordinate.Library);
        var identity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                library.Name,
                Version.Parse(library.Version),
                library.Culture,
                library.PublicKeyToken));
        return coordinate.Kind switch
        {
            "package" => new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    coordinate.Id
                        ?? throw new InspectionDefinitionException(
                            "Package exact Library coordinate requires id."),
                    coordinate.Version
                        ?? throw new InspectionDefinitionException(
                            "Package exact Library coordinate requires version.")),
                identity),
            "platform" => new ExactLibrarySourceCoordinate.Platform(
                new PlatformLibraryPopulationDeclaration(
                    ParsePlatformFamily(coordinate.Family)),
                identity),
            _ => throw new InspectionDefinitionException(
                $"Unknown exact Library coordinate kind '{coordinate.Kind}'."),
        };
    }

    private static WorkspaceEcosystemRegistrationDeclaration
        MapEcosystemDeclaration(
            WorkspaceEcosystemDeclarationDto? declaration)
    {
        if (declaration is null)
        {
            throw new InspectionDefinitionException(
                "Ecosystem registration requires declaration.");
        }
        if (declaration.NamespaceRoots is null
            || declaration.CorePackages is null
            || declaration.Populations is null)
        {
            throw new InspectionDefinitionException(
                "Ecosystem declaration requires namespaceRoots, corePackages, and populations.");
        }

        return new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create(
                declaration.Id
                    ?? throw new InspectionDefinitionException(
                        "Ecosystem declaration requires id.")),
            declaration.NamespaceRoots,
            declaration.CorePackages.Select(package => new PackageCoordinate(
                package
                    ?? throw new InspectionDefinitionException(
                        "Ecosystem corePackages cannot contain null."))),
            declaration.Populations.Select(MapEcosystemPopulation));
    }

    private static WorkspaceEcosystemPopulationDeclaration
        MapEcosystemPopulation(
            WorkspaceEcosystemPopulationDto? population)
    {
        if (population is null)
        {
            throw new InspectionDefinitionException(
                "Ecosystem population entry must not be null.");
        }

        return population.Kind switch
        {
            "exactLibrary" =>
                new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                    MapExactLibraryCoordinate(population.Coordinate)),
            "platform" =>
                new WorkspaceEcosystemPopulationDeclaration.Platform(
                    new PlatformLibraryPopulationDeclaration(
                        ParsePlatformFamily(population.Family))),
            "packagePrefix" =>
                new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    new PackagePrefixDeclaration(
                        population.Prefix
                            ?? throw new InspectionDefinitionException(
                                "Package Prefix population requires prefix."))),
            _ => throw new InspectionDefinitionException(
                $"Unknown Ecosystem population kind '{population.Kind}'."),
        };
    }

    private static PlatformFamily ParsePlatformFamily(string? value)
    {
        if (value is null
            || !Enum.TryParse(value, ignoreCase: false, out PlatformFamily family)
            || !Enum.IsDefined(family))
        {
            throw new InspectionDefinitionException(
                "Platform family must be 'DotNetRuntime' or 'AspNetCore'.");
        }

        return family;
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
        int schemaVersion,
        ref int coordinateCount)
    {
        if (tabs is null)
            throw new InspectionDefinitionException("Navigation requires tabs.");
        if (tabs.Count == 0
            && schemaVersion is not (
                InspectionDefinitionSchema.Version3
                or InspectionDefinitionSchema.Version4))
        {
            throw new InspectionDefinitionException("Navigation requires at least one tab.");
        }

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

        if (string.IsNullOrEmpty(dto.Kind))
            throw new InspectionDefinitionException("Member coordinate requires kind.");

        var kind = dto.Kind;
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

    public List<WorkspaceRegistrationDto>? Registrations { get; set; }

    public string? QueryId { get; set; }

    public JsonElement? Payload { get; set; }

    public string? Lens { get; set; }

    public string? Type { get; set; }

    public string? MemberAnchor { get; set; }

    public string? MemberSignature { get; set; }

    public string? MemberKey { get; set; }

    public string? Section { get; set; }

    public string? Library { get; set; }

    public List<string>? Libraries { get; set; }

    public List<NavigationTabDto>? Tabs { get; set; }

    public string? Focus { get; set; }

    public string? Workspace { get; set; }

    public string? Context { get; set; }

    public string? Input { get; set; }

    public string? Query { get; set; }

    public string? View { get; set; }

    public string? Navigation { get; set; }

    public List<CommittedViewStateDto>? States { get; set; }
}

internal sealed class WorkspaceRegistrationDto
{
    public string? Kind { get; set; }

    public ExactLibrarySourceCoordinateDto? Coordinate { get; set; }

    public string? Prefix { get; set; }

    public WorkspaceEcosystemDeclarationDto? Declaration { get; set; }
}

internal sealed class ExactLibrarySourceCoordinateDto
{
    public string? Kind { get; set; }

    public string? Id { get; set; }

    public string? Version { get; set; }

    public string? Family { get; set; }

    public PortableLibraryIdentityDto? Library { get; set; }
}

internal sealed class WorkspaceEcosystemDeclarationDto
{
    public string? Id { get; set; }

    public List<string>? NamespaceRoots { get; set; }

    public List<string>? CorePackages { get; set; }

    public List<WorkspaceEcosystemPopulationDto>? Populations { get; set; }
}

internal sealed class WorkspaceEcosystemPopulationDto
{
    public string? Kind { get; set; }

    public ExactLibrarySourceCoordinateDto? Coordinate { get; set; }

    public string? Family { get; set; }

    public string? Prefix { get; set; }
}

internal sealed class CommittedNavigationDefinitionDto
{
    public int SchemaVersion { get; set; }

    public string? Kind { get; set; }

    public string? Id { get; set; }

    public List<NavigationTabDto>? Tabs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Focus { get; set; }
}

internal sealed class CommittedViewStateDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Navigation { get; set; }

    public PortableSubjectRequestDto? Subject { get; set; }

    public PortableRetainedSubjectContextDto? Context { get; set; }

    public string? Facet { get; set; }

    public List<string>? Queries { get; set; }

    public List<PortableLibraryIdentityDto>? Libraries { get; set; }
}

internal sealed class PortableSubjectRequestDto
{
    public string? Kind { get; set; }
}

internal sealed class PortableRetainedSubjectContextDto
{
    public string? Kind { get; set; }

    public PortableLibraryIdentityDto? Library { get; set; }

    public PortableTypeDefinitionNameDto? Type { get; set; }

    public string? MemberAnchor { get; set; }

    public string? MemberSignature { get; set; }
}

internal sealed class PortableLibraryIdentityDto
{
    public string? Name { get; set; }

    public string? Version { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Culture { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PublicKeyToken { get; set; }
}

internal sealed class PortableTypeDefinitionNameDto
{
    public string? Namespace { get; set; }

    public List<string>? Segments { get; set; }
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
    NewLine = "\n",
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(InspectionDefinitionDto))]
[JsonSerializable(typeof(CommittedNavigationDefinitionDto))]
internal sealed partial class InspectionDefinitionJsonContext : JsonSerializerContext;
