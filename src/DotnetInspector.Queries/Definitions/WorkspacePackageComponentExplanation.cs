using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using QuerySpace.Explanation;
using UntrustedDocuments;

namespace DotnetInspector.Queries.Definitions;

/// <summary>A validated declaration, without implied realization or parent authority.</summary>
public sealed class WorkspacePackageComponentExplanationInput
{
    internal WorkspacePackageComponentExplanationInput(
        DefinitionMemberCoordinate.PackageCoordinate declaration,
        ImmutableArray<string> defaultedFields,
        WorkspacePackageComponentOccurrence? occurrence)
    {
        Declaration = declaration;
        DefaultedFields = defaultedFields;
        Occurrence = occurrence;
    }

    public DefinitionMemberCoordinate.PackageCoordinate Declaration { get; }

    /// <summary>Omitted optional fields whose declared value defaults to null.</summary>
    public ImmutableArray<string> DefaultedFields { get; }

    /// <summary>Null means parent facts are unavailable, rather than an empty population.</summary>
    public WorkspacePackageComponentOccurrence? Occurrence { get; }
}

/// <summary>One component qualified by its actual immutable parent packet.</summary>
public sealed class WorkspacePackageComponentOccurrence
{
    internal WorkspacePackageComponentOccurrence(
        WorkspaceSharePacket packet,
        WorkspacePackageComponentDescriptor component)
    {
        Packet = packet;
        Component = component;
    }

    public WorkspaceSharePacket Packet { get; }
    public WorkspacePackageComponentDescriptor Component { get; }
}

public enum WorkspaceComponentExplanationFailureKind
{
    InvalidInput,
    UnsupportedContract,
    UnsupportedVersion,
    UnknownComponent,
    ParentMismatch,
}

public sealed class WorkspaceComponentExplanationException(
    WorkspaceComponentExplanationFailureKind kind,
    string message) : Exception(message)
{
    public WorkspaceComponentExplanationFailureKind Kind { get; } = kind;
}

/// <summary>Resource-free owner admission for extracted Package declarations.</summary>
public static class WorkspacePackageComponentExplanation
{
    public const string Contract = "workspace/components/package";
    public const int SchemaVersion = 1;

    // One extracted descriptor cannot exceed its enclosing packet's JSON budget.
    public const int MaxUtf8ByteLength = WorkspaceSharePacketCodec.MaxDecodedUtf8Length;

    public static ExplanationSchema Schema { get; } = CreateSchema();

    public static WorkspacePackageComponentExplanationInput Extract(
        WorkspaceSharePacket packet,
        WorkspacePackageComponentPath path)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(path);
        WorkspacePackageComponentDescriptor? component =
            WorkspaceComponentCatalog.Describe(packet).Packages
                .SingleOrDefault(component => component.Path == path);
        if (component is null)
            throw Failure(WorkspaceComponentExplanationFailureKind.UnknownComponent,
                "The Package component is absent from the supplied parent packet.");

        return new(
            Coordinate(component.PackageId, component.Version,
                component.Framework, component.RuntimeIdentifier),
            [],
            new(packet, component));
    }

    public static WorkspacePackageComponentExplanationInput Parse(
        string json,
        WorkspaceSharePacket? parent = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var encoding = new UTF8Encoding(false, true);
            if (json.Length > MaxUtf8ByteLength
                || encoding.GetByteCount(json) > MaxUtf8ByteLength)
                throw new JsonException("The extracted component exceeds its byte budget.");
            using JsonDocument document = HardenedJson.Parse(encoding.GetBytes(json));
            return Bind(document.RootElement, parent);
        }
        catch (Exception error) when (error is JsonException
            or ArgumentException or InvalidOperationException)
        {
            throw Failure(WorkspaceComponentExplanationFailureKind.InvalidInput,
                $"Invalid Workspace Package component: {error.Message}");
        }
    }

    private static WorkspacePackageComponentExplanationInput Bind(
        JsonElement root,
        WorkspaceSharePacket? parent)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("A component must be an object.");
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name is not ("contract" or "schema_version" or "package_id"
                or "version" or "framework" or "runtime_identifier"
                or "path" or "contexts" or "focused"))
                throw new JsonException($"Unknown component field '{property.Name}'.");
        }
        if (RequiredText(root, "contract") != Contract)
            throw Failure(WorkspaceComponentExplanationFailureKind.UnsupportedContract,
                "The component contract is not supported by the Package owner.");
        if (!root.TryGetProperty("schema_version", out JsonElement schema)
            || !schema.TryGetInt32(out int version))
            throw new JsonException("An integer schema_version is required.");
        if (version != SchemaVersion)
            throw Failure(WorkspaceComponentExplanationFailureKind.UnsupportedVersion,
                "The component explanation schema version is unsupported.");

        string packageId = RequiredText(root, "package_id");
        string? pin = OptionalText(root, "version");
        string? framework = OptionalText(root, "framework");
        string? rid = OptionalText(root, "runtime_identifier");
        // This is the same canonical coordinate gate used by component identity
        // and packet editing, rather than a second set of coordinate rules.
        WorkspacePackageComponentPath path =
            WorkspacePackageComponentPath.Create(packageId, pin, framework, rid);
        if (root.TryGetProperty("path", out JsonElement suppliedPath)
            && (!WorkspacePackageComponentPath.TryCreate(suppliedPath.GetString(),
                    out WorkspacePackageComponentPath? claimedPath, out _)
                || claimedPath != path))
            throw new JsonException("The component path does not match its declaration.");

        ImmutableArray<WorkspaceContextComponentPath>? contexts = null;
        if (root.TryGetProperty("contexts", out JsonElement suppliedContexts))
        {
            if (suppliedContexts.ValueKind != JsonValueKind.Array)
                throw new JsonException("Component contexts must be an array.");
            var paths = ImmutableArray.CreateBuilder<WorkspaceContextComponentPath>();
            foreach (JsonElement item in suppliedContexts.EnumerateArray())
            {
                if (!WorkspaceContextComponentPath.TryCreate(item.GetString(),
                    out WorkspaceContextComponentPath? context, out _)
                    || paths.Contains(context!))
                    throw new JsonException("Component contexts must be unique canonical paths.");
                paths.Add(context!);
            }
            contexts = paths.ToImmutable();
        }
        bool? focused = root.TryGetProperty("focused", out JsonElement focus)
            ? focus.GetBoolean() : null;

        var defaults = ImmutableArray.CreateBuilder<string>();
        foreach (string name in new[] { "version", "framework", "runtime_identifier" })
            if (!root.TryGetProperty(name, out _))
                defaults.Add(name);

        WorkspacePackageComponentOccurrence? occurrence = null;
        if (parent is not null)
        {
            WorkspacePackageComponentExplanationInput extracted = Extract(parent, path);
            occurrence = extracted.Occurrence!;
            if ((contexts is { } claims
                    && !claims.SequenceEqual(occurrence.Component.Contexts))
                || (focused is { } claimFocus
                    && claimFocus != occurrence.Component.Focused))
                throw Failure(WorkspaceComponentExplanationFailureKind.ParentMismatch,
                    "The component context or focus does not match the supplied parent packet.");
        }

        return new(Coordinate(packageId, pin, framework, rid),
            defaults.ToImmutable(), occurrence);
    }

    private static DefinitionMemberCoordinate.PackageCoordinate Coordinate(
        string id, string? version, string? framework, string? rid) =>
        new(id, version, framework, rid);

    private static string RequiredText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(value.GetString()))
            throw new JsonException($"A nonempty {name} is required.");
        return value.GetString()!;
    }

    private static string? OptionalText(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.Null ? null : value.GetString()
            : null;

    private static WorkspaceComponentExplanationException Failure(
        WorkspaceComponentExplanationFailureKind kind, string detail) => new(kind, detail);

    private static ExplanationSchema CreateSchema()
    {
        var schema = new ExplanationSchemaIdentity(new("workspace"), "components");
        var text = new ExplanationDataShapeIdentity(schema, "text");
        var boolean = new ExplanationDataShapeIdentity(schema, "boolean");
        var package = new ExplanationResourceTypeIdentity(schema, "package");
        ExplanationFactDeclaration Field(string id, string name, string meaning,
            bool required = false) => new(new(package, id), name, meaning, text,
                required ? ExplanationCardinality.RequiredOne : ExplanationCardinality.OptionalOne,
                required ? ExplanationObservationStates.Available
                    : ExplanationObservationStates.Available | ExplanationObservationStates.Absent);
        return new(schema, new(SchemaVersion),
            [new ExplanationDataShapeDeclaration.Scalar(text, "Text",
                "Canonical declared text; no package realization is implied.",
                new(MaxUtf8ByteLength * 6, 1, 1), ExplanationScalarKind.Text),
             new ExplanationDataShapeDeclaration.Scalar(boolean, "Boolean",
                "An owner-validated boolean fact.", new(5, 1, 1), ExplanationScalarKind.Boolean)],
            [new ExplanationResourceTypeDeclaration(package, "Package contribution",
                "Declarative Package content and target configuration for a Workspace.", text,
                [Field("package_id", "Package ID", "NuGet Package identity; comparison is case insensitive.", true),
                 Field("version", "Version", "Exact declared pin; absent means floating, without resolving a version."),
                 Field("framework", "Framework", "Requested acquisition TFM; absent leaves the requested target unspecified."),
                 Field("runtime_identifier", "Runtime identifier", "Requested RID; absent declares no specific runtime target."),
                 new(new(package, "defaulted_fields"), "Defaulted fields",
                    "Optional fields omitted from the submitted declaration; each defaults to null.",
                    text, ExplanationCardinality.OrderedMany,
                    ExplanationObservationStates.Available, maximumValueCount: 3),
                 new(new(package, "component_path"), "Component path",
                    "Canonical path in the supplied parent packet; unavailable without that parent.",
                    text, ExplanationCardinality.RequiredOne,
                    ExplanationObservationStates.Available | ExplanationObservationStates.Unavailable,
                    unavailableDataShape: text),
                 new(new(package, "contexts"), "Contexts",
                    "Ordered context paths in the supplied parent packet; unavailable without it.",
                    text, ExplanationCardinality.OrderedMany,
                    ExplanationObservationStates.Available | ExplanationObservationStates.Unavailable,
                    maximumValueCount: WorkspaceSharePacketCodec.MaxContexts,
                    unavailableDataShape: text),
                 new(new(package, "focused"), "Focused",
                    "Whether this component is focused in the supplied parent packet; unavailable without it.",
                    boolean, ExplanationCardinality.RequiredOne,
                    ExplanationObservationStates.Available | ExplanationObservationStates.Unavailable,
                    unavailableDataShape: text)])]);
    }
}
