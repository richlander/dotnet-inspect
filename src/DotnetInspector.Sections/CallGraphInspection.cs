using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public static class MemberCallGraphInspection
{
    public static InspectionEnvelope<InspectionGraphDocument> Execute(
        InspectionGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return
        new(
            document,
            new InspectionShare.NonProjectable(
                "member-call-graph/share",
                "Inspect Web cannot yet restore a Member Call Graph inspection."),
            []);
    }
}

public static class CallGraphInspectionJson
{
    public static void Write(
        Utf8JsonWriter writer,
        InspectionGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        JsonSerializer.Serialize(
            writer,
            CallGraphJsonDocument.From(document),
            CallGraphJsonContext.Default.CallGraphJsonDocument);
    }
}

internal sealed record CallGraphJsonDocument(
    string Scope,
    CallGraphJsonModeRequest ModeRequest,
    CallGraphJsonNeighborhoodRequest? NeighborhoodRequest,
    CallGraphJsonInducedSetRequest? InducedSetRequest,
    CallGraphJsonNode[] Nodes,
    CallGraphJsonGroup[] Groups,
    CallGraphJsonEdge[] Edges,
    CallGraphJsonOccurrence[] Occurrences,
    CallGraphJsonCharacteristic[] Characteristics,
    CallGraphJsonSeed[] Seeds,
    CallGraphJsonLimit[] Limits,
    CallGraphJsonFailure[] Failures)
{
    internal static CallGraphJsonDocument From(
        InspectionGraphDocument document) =>
        new(
            document.Scope.ToString(),
            CallGraphJsonModeRequest.From(document.ModeRequest),
            document.NeighborhoodRequest is { } neighborhood
                ? CallGraphJsonNeighborhoodRequest.From(neighborhood)
                : null,
            document.InducedSetRequest is { } inducedSet
                ? CallGraphJsonInducedSetRequest.From(inducedSet)
                : null,
            [.. document.Nodes.Select(CallGraphJsonNode.From)],
            [.. document.Groups.Select(CallGraphJsonGroup.From)],
            [.. document.Edges.Select(CallGraphJsonEdge.From)],
            [.. document.Occurrences.Select(CallGraphJsonOccurrence.From)],
            [.. document.Characteristics.Select(
                CallGraphJsonCharacteristic.From)],
            [.. document.Seeds.Select(CallGraphJsonSeed.From)],
            [.. document.Limits.Select(CallGraphJsonLimit.From)],
            [.. document.Failures.Select(CallGraphJsonFailure.From)]);
}

internal sealed record CallGraphJsonModeRequest(
    string Mode,
    CallGraphJsonSubject[] Seeds,
    string? InducedSetRule)
{
    internal static CallGraphJsonModeRequest From(
        InspectionGraphModeRequest request) =>
        new(
            request.Mode.ToString(),
            [.. request.Seeds.Select(CallGraphJsonSubject.From)],
            request.InducedSetRule?.ToString());
}

internal sealed record CallGraphJsonNeighborhoodRequest(
    CallGraphJsonRelationship[] Relationships,
    string Direction,
    int MaxDepth)
{
    internal static CallGraphJsonNeighborhoodRequest From(
        InspectionGraphNeighborhoodRequest request) =>
        new(
            [.. request.Relationships.Select(
                CallGraphJsonRelationship.From)],
            request.Direction.ToString(),
            request.MaxDepth);
}

internal sealed record CallGraphJsonInducedSetRequest(
    CallGraphJsonSubject[] Subjects,
    CallGraphJsonRelationship[] Relationships,
    string AdmissionRule)
{
    internal static CallGraphJsonInducedSetRequest From(
        InspectionGraphInducedSetRequest request) =>
        new(
            [.. request.Subjects.Select(CallGraphJsonSubject.From)],
            [.. request.Relationships.Select(
                CallGraphJsonRelationship.From)],
            request.AdmissionRule.ToString());
}

internal sealed record CallGraphJsonNode(
    int Id,
    CallGraphJsonSubject Subject,
    string Role,
    int[] GroupIds)
{
    internal static CallGraphJsonNode From(InspectionGraphNode node) =>
        new(
            node.Id,
            CallGraphJsonSubject.From(node.Subject),
            node.Role.ToString(),
            [.. node.GroupIds]);
}

internal sealed record CallGraphJsonGroup(
    int Id,
    CallGraphJsonSubject Subject,
    int? ParentId)
{
    internal static CallGraphJsonGroup From(InspectionGraphGroup group) =>
        new(
            group.Id,
            CallGraphJsonSubject.From(group.Subject),
            group.ParentId);
}

internal sealed record CallGraphJsonEdge(
    int Id,
    int FromNodeId,
    int ToNodeId,
    CallGraphJsonRelationship Relationship,
    int[] OccurrenceIds)
{
    internal static CallGraphJsonEdge From(InspectionGraphEdge edge) =>
        new(
            edge.Id,
            edge.FromNodeId,
            edge.ToNodeId,
            CallGraphJsonRelationship.From(edge.Relationship),
            [.. edge.OccurrenceIds]);
}

internal sealed record CallGraphJsonOccurrence(
    int Id,
    CallGraphJsonRelationship Relationship,
    CallGraphJsonSubject SourceSubject,
    CallGraphJsonSubject TargetSubject,
    CallGraphJsonEvidence Evidence,
    int[] DerivedFromOccurrenceIds)
{
    internal static CallGraphJsonOccurrence From(
        InspectionGraphOccurrence occurrence) =>
        new(
            occurrence.Id,
            CallGraphJsonRelationship.From(occurrence.Relationship),
            CallGraphJsonSubject.From(occurrence.SourceSubject),
            CallGraphJsonSubject.From(occurrence.TargetSubject),
            CallGraphJsonEvidence.From(occurrence.Evidence),
            [.. occurrence.DerivedFromOccurrenceIds]);
}

internal sealed record CallGraphJsonCharacteristic(
    CallGraphJsonCharacteristicDescriptor Descriptor,
    CallGraphJsonTarget Target,
    CallGraphJsonValue Value,
    CallGraphJsonDerivation Derivation)
{
    internal static CallGraphJsonCharacteristic From(
        InspectionGraphCharacteristic characteristic) =>
        new(
            CallGraphJsonCharacteristicDescriptor.From(
                characteristic.Descriptor),
            CallGraphJsonTarget.From(characteristic.Target),
            CallGraphJsonValue.From(characteristic.Value),
            CallGraphJsonDerivation.From(
                characteristic.Derivation));
}

internal sealed record CallGraphJsonSeed(
    CallGraphJsonSubject Subject,
    CallGraphJsonTarget Target,
    string Role)
{
    internal static CallGraphJsonSeed From(InspectionGraphSeed seed) =>
        new(
            CallGraphJsonSubject.From(seed.Subject),
            CallGraphJsonTarget.From(seed.Target),
            seed.Role.ToString());
}

internal sealed record CallGraphJsonLimit(
    CallGraphJsonDiagnosticDescriptor Descriptor,
    CallGraphJsonTarget? Target,
    CallGraphJsonEvidence? Evidence)
{
    internal static CallGraphJsonLimit From(InspectionGraphLimit limit) =>
        new(
            CallGraphJsonDiagnosticDescriptor.From(
                limit.Descriptor.Id,
                limit.Descriptor.Owner,
                limit.Descriptor.Evidence),
            limit.Target is { } target
                ? CallGraphJsonTarget.From(target)
                : null,
            limit.Evidence is { } evidence
                ? CallGraphJsonEvidence.From(evidence)
                : null);
}

internal sealed record CallGraphJsonFailure(
    CallGraphJsonDiagnosticDescriptor Descriptor,
    CallGraphJsonTarget? Target,
    CallGraphJsonEvidence? Evidence)
{
    internal static CallGraphJsonFailure From(
        InspectionGraphFailure failure) =>
        new(
            CallGraphJsonDiagnosticDescriptor.From(
                failure.Descriptor.Id,
                failure.Descriptor.Owner,
                failure.Descriptor.Evidence),
            failure.Target is { } target
                ? CallGraphJsonTarget.From(target)
                : null,
            failure.Evidence is { } evidence
                ? CallGraphJsonEvidence.From(evidence)
                : null);
}

internal sealed record CallGraphJsonSubject(
    string Kind,
    bool IsPortable,
    CallGraphJsonMemberIdentity Member)
{
    internal static CallGraphJsonSubject From(
        InspectionGraphSubject subject)
    {
        if (subject
                is not InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CallGraph identity,
                })
        {
            throw new NotSupportedException(
                $"Call Graph JSON cannot serialize subject kind "
                + $"'{subject.Kind}'.");
        }

        return new(
            subject.Kind.ToString(),
            subject.IsPortable,
            CallGraphJsonMemberIdentity.From(identity));
    }
}

internal sealed record CallGraphJsonMemberIdentity(
    string IdentityKind,
    bool IsPortable,
    CallGraphJsonArtifactMemberAddress? ArtifactMember,
    CallGraphJsonMember Member)
{
    internal static CallGraphJsonMemberIdentity From(
        InspectionGraphMemberIdentity.CallGraph identity)
    {
        GraphArtifactMemberAddress? artifactMember =
            identity.Identity.ArtifactMemberAddress;
        if (identity.Identity.Kind == GraphNodeIdentityKind.ArtifactMember
            && artifactMember is null)
        {
            throw new NotSupportedException(
                "Call Graph JSON cannot serialize an ArtifactMember "
                + "identity without its artifact address.");
        }

        return new(
            identity.Identity.Kind.ToString(),
            identity.IsPortable,
            artifactMember is not null
                ? CallGraphJsonArtifactMemberAddress.From(artifactMember)
                : null,
            CallGraphJsonMember.From(identity.Member));
    }
}

internal sealed record CallGraphJsonArtifactMemberAddress(
    CallGraphJsonAssemblyIdentity Assembly,
    Guid ModuleVersionId,
    int MethodToken)
{
    internal static CallGraphJsonArtifactMemberAddress From(
        GraphArtifactMemberAddress address) =>
        new(
            CallGraphJsonAssemblyIdentity.From(address.AssemblyIdentity),
            address.ModuleVersionId,
            address.MethodToken);
}

internal sealed record CallGraphJsonMember(
    CallGraphJsonType DeclaringType,
    string Name,
    CallGraphJsonType[] ParameterTypes,
    CallGraphJsonType ReturnType,
    string Kind,
    CallGraphJsonType[] TypeArguments,
    bool HasThis,
    byte SignatureHeader,
    int RequiredParameterCount,
    int GenericArity,
    CallGraphJsonType[] OpenParameterTypes,
    CallGraphJsonType? OpenReturnType)
{
    internal static CallGraphJsonMember From(MemberRef member) =>
        new(
            CallGraphJsonType.From(member.DeclaringType),
            member.Name,
            [.. member.ParameterTypes.Select(CallGraphJsonType.From)],
            CallGraphJsonType.From(member.ReturnType),
            member.Kind.ToString(),
            [.. member.TypeArguments.Select(CallGraphJsonType.From)],
            member.HasThis,
            member.SignatureHeader,
            member.RequiredParameterCount,
            member.GenericArity,
            [.. member.OpenParameterTypes.Select(CallGraphJsonType.From)],
            member.OpenReturnType is { } openReturn
                ? CallGraphJsonType.From(openReturn)
                : null);
}

internal sealed record CallGraphJsonType(
    string Kind,
    string Assembly,
    string Namespace,
    string Name,
    CallGraphJsonType? ElementType,
    CallGraphJsonType[] TypeArguments,
    int Rank,
    int GenericParameterIndex,
    string GenericParameterName,
    string UnsupportedReason,
    CallGraphJsonType? UnmodifiedType,
    CallGraphJsonType? ModifierType,
    bool IsRequiredModifier,
    CallGraphJsonFunctionPointer? FunctionPointer,
    int[] ArraySizes,
    int[] ArrayLowerBounds,
    byte RawTypeKind,
    CallGraphJsonMetadataNameFailure? MetadataNameFailure,
    CallGraphJsonTypeResolution? Resolution,
    bool TrustedFrameworkAssembly,
    bool TrustedProtobufAssembly)
{
    internal static CallGraphJsonType From(TypeRef type) =>
        new(
            type.Kind.ToString(),
            type.Assembly,
            type.Namespace,
            type.Name,
            type.ElementType is { } elementType
                ? From(elementType)
                : null,
            [.. type.TypeArguments.Select(From)],
            type.Rank,
            type.GenericParameterIndex,
            type.GenericParameterName,
            type.UnsupportedReason,
            type.UnmodifiedType is { } unmodifiedType
                ? From(unmodifiedType)
                : null,
            type.ModifierType is { } modifierType
                ? From(modifierType)
                : null,
            type.IsRequiredModifier,
            type.FunctionPointerSignature is { } functionPointer
                ? CallGraphJsonFunctionPointer.From(functionPointer)
                : null,
            [.. type.ArraySizes],
            [.. type.ArrayLowerBounds],
            type.RawTypeKind,
            type.MetadataNameFailure is { } failure
                ? CallGraphJsonMetadataNameFailure.From(failure)
                : null,
            type.Resolution is { } resolution
                ? CallGraphJsonTypeResolution.From(resolution)
                : null,
            type.TrustedFrameworkAssembly,
            type.TrustedProtobufAssembly);
}

internal sealed record CallGraphJsonFunctionPointer(
    string CallingConvention,
    byte SignatureHeader,
    bool IsInstance,
    bool HasExplicitThis,
    int GenericParameterCount,
    int RequiredParameterCount,
    CallGraphJsonType[] ParameterTypes,
    CallGraphJsonType ReturnType)
{
    internal static CallGraphJsonFunctionPointer From(
        System.Reflection.Metadata.MethodSignature<TypeRef> signature) =>
        new(
            signature.Header.CallingConvention.ToString(),
            signature.Header.RawValue,
            signature.Header.IsInstance,
            signature.Header.HasExplicitThis,
            signature.GenericParameterCount,
            signature.RequiredParameterCount,
            [.. signature.ParameterTypes.Select(CallGraphJsonType.From)],
            CallGraphJsonType.From(signature.ReturnType));
}

internal sealed record CallGraphJsonMetadataNameFailure(
    string Mechanism,
    string Detail,
    int? SubjectToken,
    int ConsumedNodes,
    string Kind)
{
    internal static CallGraphJsonMetadataNameFailure From(
        MetadataTypeNameFailure failure) =>
        new(
            failure.Mechanism.ToString(),
            failure.Detail,
            failure.SubjectToken,
            failure.ConsumedNodes,
            failure.Kind);
}

internal sealed record CallGraphJsonTypeResolution(
    CallGraphJsonTypeOrigin Origin,
    CallGraphJsonMetadataTypeName Type)
{
    internal static CallGraphJsonTypeResolution From(
        ResolvableTypeReference resolution) =>
        new(
            CallGraphJsonTypeOrigin.From(resolution.Origin),
            CallGraphJsonMetadataTypeName.From(resolution.Type));
}

internal sealed record CallGraphJsonTypeOrigin(
    string Kind,
    CallGraphJsonAssemblyIdentity? Assembly,
    string? ModuleName)
{
    internal static CallGraphJsonTypeOrigin From(
        TypeReferenceOrigin origin) =>
        origin switch
        {
            TypeReferenceOrigin.AssemblyReference assembly =>
                new(
                    nameof(TypeReferenceOrigin.AssemblyReference),
                    CallGraphJsonAssemblyIdentity.From(
                        assembly.Assembly),
                    null),
            TypeReferenceOrigin.CurrentAssembly current =>
                new(
                    nameof(TypeReferenceOrigin.CurrentAssembly),
                    current.Assembly is { } identity
                        ? CallGraphJsonAssemblyIdentity.From(identity)
                        : null,
                    null),
            TypeReferenceOrigin.IntrinsicCoreLibrary =>
                new(
                    nameof(TypeReferenceOrigin.IntrinsicCoreLibrary),
                    null,
                    null),
            TypeReferenceOrigin.ModuleReference module =>
                new(
                    nameof(TypeReferenceOrigin.ModuleReference),
                    null,
                    module.ModuleName),
            _ => throw new NotSupportedException(
                $"Analysis identity JSON cannot serialize type origin "
                + $"'{origin.GetType().FullName}'."),
        };
}

internal sealed record CallGraphJsonAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken)
{
    internal static CallGraphJsonAssemblyIdentity From(
        AssemblyReferenceIdentity assembly) =>
        new(
            assembly.Name,
            assembly.Version?.ToString(),
            assembly.Culture,
            assembly.PublicKeyToken);
}

internal sealed record CallGraphJsonMetadataTypeName(
    string Namespace,
    string[] Segments)
{
    internal static CallGraphJsonMetadataTypeName From(
        MetadataTypeDefinitionName type) =>
        new(type.Namespace, [.. type.Segments]);
}

internal sealed record CallGraphJsonRelationship(
    string Id,
    string Owner,
    string Semantics,
    string[] EdgeSourceKinds,
    string[] EdgeTargetKinds,
    string[] OccurrenceSourceKinds,
    string[] OccurrenceTargetKinds,
    CallGraphJsonSeedAdmission[] SeedAdmissions,
    string EndpointProjection,
    string OccurrenceIdentity,
    CallGraphJsonEvidenceDescriptor[] Evidence)
{
    internal static CallGraphJsonRelationship From(
        InspectionGraphRelationshipDescriptor relationship)
    {
        if (!ReferenceEquals(
                relationship,
                CallGraphInspectionGraphCatalog.Call))
        {
            throw new NotSupportedException(
                $"Call Graph JSON cannot serialize relationship "
                + $"'{relationship.Id}'.");
        }

        return new(
            relationship.Id,
            relationship.Owner.ToString(),
            relationship.Semantics.ToString(),
            [.. relationship.EdgeSourceKinds.Select(
                static kind => kind.ToString())],
            [.. relationship.EdgeTargetKinds.Select(
                static kind => kind.ToString())],
            [.. relationship.OccurrenceSourceKinds.Select(
                static kind => kind.ToString())],
            [.. relationship.OccurrenceTargetKinds.Select(
                static kind => kind.ToString())],
            [.. relationship.SeedAdmissions.Select(
                CallGraphJsonSeedAdmission.From)],
            "Exact",
            "CallOccurrence",
            [.. relationship.Evidence.Select(
                CallGraphJsonEvidenceDescriptor.From)]);
    }
}

internal sealed record CallGraphJsonSeedAdmission(
    string SubjectKind,
    string Kind,
    string Role)
{
    internal static CallGraphJsonSeedAdmission From(
        InspectionGraphSeedAdmission admission) =>
        new(
            admission.SubjectKind.ToString(),
            admission.Kind.ToString(),
            admission.Role.ToString());
}

internal sealed record CallGraphJsonEvidenceDescriptor(
    string Id,
    string Owner)
{
    internal static CallGraphJsonEvidenceDescriptor From(
        InspectionGraphEvidenceDescriptor descriptor) =>
        new(descriptor.Id, descriptor.Owner.ToString());
}

internal sealed record CallGraphJsonCharacteristicDescriptor(
    string Id,
    string Owner,
    CallGraphJsonValueDescriptor Value,
    string[] Targets,
    CallGraphJsonQuery[] Prerequisites,
    string[] AdmittedDerivations,
    string Aggregation)
{
    internal static CallGraphJsonCharacteristicDescriptor From(
        InspectionGraphCharacteristicDescriptor descriptor) =>
        new(
            descriptor.Id,
            descriptor.Owner.ToString(),
            CallGraphJsonValueDescriptor.From(descriptor.Value),
            [.. descriptor.Targets.Select(
                static target => target.ToString())],
            [.. descriptor.Prerequisites.Select(
                CallGraphJsonQuery.From)],
            [.. descriptor.AdmittedDerivations.Select(
                static derivation => derivation.ToString())],
            descriptor.Aggregation.ToString());
}

internal sealed record CallGraphJsonValueDescriptor(
    string Id,
    string Shape)
{
    internal static CallGraphJsonValueDescriptor From(
        InspectionGraphValueDescriptor descriptor) =>
        new(descriptor.Id, descriptor.Shape.ToString());
}

internal sealed record CallGraphJsonQuery(string Name, string Cost)
{
    internal static CallGraphJsonQuery From(
        InspectionQueryDefinition query) =>
        new(query.Name, query.Cost.ToString());
}

internal sealed record CallGraphJsonTarget(string Kind, int Id)
{
    internal static CallGraphJsonTarget From(
        InspectionGraphTarget target) =>
        new(target.Kind.ToString(), target.Id);
}

internal sealed record CallGraphJsonValue(
    CallGraphJsonValueDescriptor Descriptor,
    bool? Boolean,
    long? Integer,
    string? Token,
    string[]? Tokens)
{
    internal static CallGraphJsonValue From(
        InspectionGraphValue value) =>
        value switch
        {
            InspectionGraphValue.Boolean boolean =>
                new(
                    CallGraphJsonValueDescriptor.From(
                        boolean.Descriptor),
                    boolean.Value,
                    null,
                    null,
                    null),
            InspectionGraphValue.Integer integer =>
                new(
                    CallGraphJsonValueDescriptor.From(
                        integer.Descriptor),
                    null,
                    integer.Value,
                    null,
                    null),
            InspectionGraphValue.Token token =>
                new(
                    CallGraphJsonValueDescriptor.From(token.Descriptor),
                    null,
                    null,
                    token.Value,
                    null),
            InspectionGraphValue.TokenSet tokens =>
                new(
                    CallGraphJsonValueDescriptor.From(tokens.Descriptor),
                    null,
                    null,
                    null,
                    [.. tokens.Values]),
            _ => throw new NotSupportedException(
                $"Call Graph JSON cannot serialize value "
                + $"'{value.GetType().FullName}'."),
        };
}

internal sealed record CallGraphJsonDerivation(
    string Kind,
    CallGraphJsonTarget[] Sources)
{
    internal static CallGraphJsonDerivation From(
        InspectionGraphCharacteristicDerivation derivation) =>
        new(
            derivation.Kind.ToString(),
            [.. derivation.Sources.Select(CallGraphJsonTarget.From)]);
}

internal sealed record CallGraphJsonDiagnosticDescriptor(
    string Id,
    string Owner,
    CallGraphJsonEvidenceDescriptor[] Evidence)
{
    internal static CallGraphJsonDiagnosticDescriptor From(
        string id,
        InspectionGraphOwner owner,
        IEnumerable<InspectionGraphEvidenceDescriptor> evidence) =>
        new(
            id,
            owner.ToString(),
            [.. evidence.Select(
                CallGraphJsonEvidenceDescriptor.From)]);
}

internal sealed record CallGraphJsonEvidence(
    CallGraphJsonEvidenceDescriptor Descriptor,
    string Kind,
    int? RowNumber,
    bool? IdentityPortable,
    string? SourceReceiptEvidence,
    Guid? CallerModuleVersionId,
    int? CallerMethodToken,
    int? IlOffset,
    int? OperandToken,
    string? CallKind,
    string? DispatchKind,
    bool? InLoop,
    int? MaxNodes,
    int? IncompleteNodeCount,
    int? IncompleteEdgeCount,
    int? BindingIdentityConflictCount)
{
    internal static CallGraphJsonEvidence From(
        IInspectionGraphEvidence evidence) =>
        evidence switch
        {
            CallGraphLogicalEdgeEvidence logical =>
                Create(evidence, "LogicalEdge", rowNumber: logical.RowNumber),
            CallGraphCallSiteEvidence callSite =>
                Create(
                    evidence,
                    "CallSite",
                    identityPortable: callSite.Identity.IsPortable,
                    sourceReceiptEvidence:
                        callSite.Identity.SourceReceiptEvidence,
                    callerModuleVersionId:
                        callSite.CallerModuleVersionId,
                    callerMethodToken: callSite.CallerMethodToken,
                    ilOffset: callSite.ILOffset,
                    operandToken: callSite.OperandToken,
                    callKind: callSite.CallKind.ToString(),
                    dispatchKind: callSite.DispatchKind.ToString(),
                    inLoop: callSite.InLoop),
            CallGraphTraversalNodeBoundEvidence bound =>
                Create(evidence, "TraversalNodeBound",
                    maxNodes: bound.MaxNodes),
            CallGraphCorrespondenceIncompleteEvidence incomplete =>
                Create(
                    evidence,
                    "CorrespondenceIncomplete",
                    incompleteNodeCount:
                        incomplete.IncompleteNodeCount,
                    incompleteEdgeCount:
                        incomplete.IncompleteEdgeCount,
                    bindingIdentityConflictCount:
                        incomplete.BindingIdentityConflictCount),
            _ => throw new NotSupportedException(
                $"Call Graph JSON cannot serialize evidence "
                + $"'{evidence.GetType().FullName}'."),
        };

    static CallGraphJsonEvidence Create(
        IInspectionGraphEvidence evidence,
        string kind,
        int? rowNumber = null,
        bool? identityPortable = null,
        string? sourceReceiptEvidence = null,
        Guid? callerModuleVersionId = null,
        int? callerMethodToken = null,
        int? ilOffset = null,
        int? operandToken = null,
        string? callKind = null,
        string? dispatchKind = null,
        bool? inLoop = null,
        int? maxNodes = null,
        int? incompleteNodeCount = null,
        int? incompleteEdgeCount = null,
        int? bindingIdentityConflictCount = null) =>
        new(
            CallGraphJsonEvidenceDescriptor.From(evidence.Descriptor),
            kind,
            rowNumber,
            identityPortable,
            sourceReceiptEvidence,
            callerModuleVersionId,
            callerMethodToken,
            ilOffset,
            operandToken,
            callKind,
            dispatchKind,
            inLoop,
            maxNodes,
            incompleteNodeCount,
            incompleteEdgeCount,
            bindingIdentityConflictCount);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CallGraphJsonDocument))]
[JsonSerializable(typeof(CallGraphJsonType))]
internal partial class CallGraphJsonContext : JsonSerializerContext;
