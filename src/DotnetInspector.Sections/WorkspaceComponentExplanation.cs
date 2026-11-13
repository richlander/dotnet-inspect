using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

public sealed partial class ResourceExplanationCatalog
{
    public const string ComponentContractsCollectionSegment = "component-contracts";

    private static ExplanationResourceKey ComponentContractsKey =>
        ResourceExplanationVocabulary.Key(ResourceExplanationVocabulary.ComponentContractsType,
            "component-contracts");

    internal static ResourcePath? ComponentContractPath(ExplanationResourceTypeIdentity type) =>
        type == WorkspacePackageComponentExplanation.Schema.ResourceTypes.Single().Identity
            ? new(WorkspacePackageComponentExplanation.Contract) : null;

    public static InspectionCapabilityModule ComponentContractsRoutingModule { get; } = new(
        "resource-explanation.component-contracts.routing",
        routingDeclarations: static () => ComponentRoutingCache.Declarations);

    private static class ComponentRoutingCache
    {
        internal static readonly ImmutableArray<ResourceExplanationRoutingDeclaration>
            Declarations = [ComponentContractsRoutingDeclaration];
    }

    public static ResourceExplanationRoutingDeclaration ComponentContractsRoutingDeclaration =>
        new(ResourceExplanationRoutingRole.Reference, ComponentContractsCollectionSegment,
            "Component contracts", "Resource Explanation",
            "Owner-issued contracts for explaining extracted declarations without realizing their content.",
            ["Which declaration kinds can be explained, and what do their fields mean?"],
            [ComponentContractsKey]);

    /// <summary>Adapts the installed Workspace contract, without realizing a Workspace.</summary>
    public static ResourceExplanationCatalog CreateWorkspaceComponents()
    {
        ExplanationSchema schema = WorkspacePackageComponentExplanation.Schema;
        ExplanationResourceTypeDeclaration package = schema.ResourceTypes.Single();
        ExplanationResourceTypeIdentity type = ResourceExplanationVocabulary.ComponentContractType;
        ResourcePath path = new(WorkspacePackageComponentExplanation.Contract);
        ExplanationResourceKey key = ResourceExplanationVocabulary.Key(type, path.Value);
        ExplanationValue.Record Field(ExplanationFactDeclaration field) => new(
        [
            Part(ResourceExplanationVocabulary.ComponentFieldShape, "id", field.Identity.Value),
            Part(ResourceExplanationVocabulary.ComponentFieldShape, "name", field.DisplayName),
            Part(ResourceExplanationVocabulary.ComponentFieldShape, "meaning", field.Meaning),
            Part(ResourceExplanationVocabulary.ComponentFieldShape, "shape", field.ValueShape.ToString()),
            Part(ResourceExplanationVocabulary.ComponentFieldShape, "cardinality", field.Cardinality.ToString()),
            new(new(ResourceExplanationVocabulary.ComponentFieldShape, "states"),
                Enum.GetValues<ExplanationObservationState>()
                    .Where(state => field.AdmittedStates.HasFlag(
                        (ExplanationObservationStates)(1 << (int)state)))
                    .Select(state => ExplanationValue.Text(state.ToString()))),
        ]);
        ExplanationResourceSnapshot contract = Snapshot(path, key,
        [
            ResourceExplanationVocabulary.TextFact(type, "name", package.DisplayName),
            ResourceExplanationVocabulary.TextFact(type, "purpose", package.Meaning),
            ResourceExplanationVocabulary.TextFact(type, "contract", path.Value),
            ResourceExplanationVocabulary.TextFact(type, "owner", schema.Identity.Owner.Value),
            ResourceExplanationVocabulary.TextFact(type, "schema", schema.Identity.Value),
            ResourceExplanationVocabulary.IntegerFact(type, "version", schema.Version.Value),
            ResourceExplanationVocabulary.TextFact(type, "resource_type", package.Identity.Value),
            ResourceExplanationVocabulary.ValuesFact(type, "fields", package.Facts.Select(Field)),
            ResourceExplanationVocabulary.ValuesFact(type, "field_limits",
                package.Facts.Where(field => field.MaximumValueCount is not null).Select(field =>
                    new ExplanationValue.Record(
                    [
                        Part(ResourceExplanationVocabulary.ComponentLimitShape, "field", field.Identity.Value),
                        new(new(ResourceExplanationVocabulary.ComponentLimitShape, "maximum_values"),
                            [ExplanationValue.Integer(field.MaximumValueCount!.Value)]),
                    ]))),
        ], []);
        return Create(ResourceExplanationVocabulary.Schemas,
        [
            Snapshot(new(ComponentContractsCollectionSegment), ComponentContractsKey,
            [
                ResourceExplanationVocabulary.TextFact(ResourceExplanationVocabulary.ComponentContractsType,
                    "name", package.DisplayName),
                ResourceExplanationVocabulary.TextFact(ResourceExplanationVocabulary.ComponentContractsType,
                    "purpose", package.Meaning),
                ResourceExplanationVocabulary.IntegerFact(ResourceExplanationVocabulary.ComponentContractsType,
                    "members", 1),
            ], [ResourceExplanationVocabulary.Targets(ResourceExplanationVocabulary.ComponentContractsType,
                "collection-component-contract", [key])]),
            contract,
        ]);
    }

    private static ExplanationRecordFieldValue Part(
        ExplanationDataShapeIdentity shape, string field, string value) =>
        new(new(shape, field), [ExplanationValue.Text(value)]);
}

/// <summary>Projects owner-validated Package input into the existing detached Document.</summary>
public static class WorkspaceComponentExplanationOperation
{
    public static InspectionEnvelope<ResourceExplanationDocument> Explain(
        WorkspacePackageComponentExplanationInput input,
        ResourceExplanationRequest? request = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ExplanationSchema schema = WorkspacePackageComponentExplanation.Schema;
        ExplanationResourceTypeDeclaration type = schema.ResourceTypes.Single();
        var coordinate = input.Declaration;
        // This bounded, detached-input key is not a public component/packet identity.
        // The supplied canonical packet qualifies observations without serializing it
        // or inventing parent authority for a standalone declaration.
        string parent = input.Occurrence is { } occurrence
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                WorkspaceSharePacketCodec.Encode(occurrence.Packet)))) : "standalone";
        string local = JsonSerializer.Serialize(new[]
            { coordinate.Id.ToLowerInvariant(), coordinate.Version ?? "", coordinate.Framework ?? "",
                coordinate.RuntimeIdentifier ?? "", string.Join(",", input.DefaultedFields), parent },
            WorkspaceComponentExplanationJsonContext.Default.StringArray);
        var key = new ExplanationResourceKey(schema.Identity.Owner, type.Identity, ExplanationValue.Text(local));
        ExplanationFactObservation Fact(string id, string? value) => new(new(type.Identity, id),
            value is null ? ExplanationObservationState.Absent : ExplanationObservationState.Available,
            value is null ? [] : [ExplanationValue.Text(value)]);
        ExplanationFactObservation ParentFact(string id, IEnumerable<ExplanationValue> values) =>
            input.Occurrence is null
                ? new(new(type.Identity, id), ExplanationObservationState.Unavailable,
                    outcomeData: ExplanationValue.Text("Supply the parent packet to qualify this fact."))
                : new(new(type.Identity, id), ExplanationObservationState.Available, values);
        WorkspacePackageComponentDescriptor? component = input.Occurrence?.Component;
        ExplanationResourceSnapshot snapshot = ExplanationConformance.CreateSnapshot(
            [schema], key, schema.Version, ExplanationSnapshotScope.Detached, [],
        [
            Fact("package_id", coordinate.Id), Fact("version", coordinate.Version),
            Fact("framework", coordinate.Framework), Fact("runtime_identifier", coordinate.RuntimeIdentifier),
            new(new(type.Identity, "defaulted_fields"), ExplanationObservationState.Available,
                input.DefaultedFields.Select(ExplanationValue.Text)),
            ParentFact("component_path", component is null ? [] : [ExplanationValue.Text(component.Path.Value)]),
            ParentFact("contexts", component is null ? [] : component.Contexts.Select(c => ExplanationValue.Text(c.Value))),
            ParentFact("focused", component is null ? [] : [ExplanationValue.Boolean(component.Focused)]),
        ], []);
        return ResourceExplanationCatalog.ExplainDetached([schema], snapshot, [],
            request ?? ResourceExplanationRequest.ForHost(0), "workspace-component-explanation");
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
internal partial class WorkspaceComponentExplanationJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
