using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DependencyInspectionContent))]
[JsonSerializable(typeof(DependencyInspectionEvidenceDocument))]
[JsonSerializable(typeof(InspectionEnvelope<DependencyInspectionContent>))]
[JsonSerializable(
    typeof(
        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument>))]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.Package),
    TypeInfoPropertyName = "PackageEvidenceRootFailurePackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.Package),
    TypeInfoPropertyName = "PackageEvidenceRootIdentityPackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.Package),
    TypeInfoPropertyName = "PackageEvidenceRootProvenancePackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupIdentity.Package),
    TypeInfoPropertyName = "PackageEvidenceGroupIdentityPackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupOccurrence.Package),
    TypeInfoPropertyName = "PackageEvidenceGroupOccurrencePackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipParentIdentity.Package),
    TypeInfoPropertyName = "PackageEvidenceRelationshipParentPackage")]
[JsonSerializable(
    typeof(DependencyGraphNodeIdentity.Package),
    TypeInfoPropertyName = "DependencyGraphNodeIdentityPackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceRootFailureRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceRootIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceRootProvenanceRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupIdentity.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceGroupIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupOccurrence.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceGroupOccurrenceRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationFailure.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceDeclarationFailureRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidencePackageIdentity.RestoredProject),
    TypeInfoPropertyName = "PackageEvidencePackageIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipIdentity.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceRelationshipIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipFailure.RestoredProject),
    TypeInfoPropertyName = "PackageEvidenceRelationshipFailureRestoredProject")]
[JsonSerializable(
    typeof(DependencyGraphNodeIdentity.RestoredProject),
    TypeInfoPropertyName = "DependencyGraphNodeIdentityRestoredProject")]
[JsonSerializable(
    typeof(RestoredProjectGraphParentIdentity.Root),
    TypeInfoPropertyName = "RestoredProjectGraphParentRoot")]
[JsonSerializable(
    typeof(RestoredProjectGraphParentIdentity.Package),
    TypeInfoPropertyName = "RestoredProjectGraphParentPackage")]
[JsonSerializable(
    typeof(RestoredProjectGraphParentIdentity.Project),
    TypeInfoPropertyName = "RestoredProjectGraphParentProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.AuthoredProject),
    TypeInfoPropertyName = "PackageEvidenceRootFailureAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.AuthoredProject),
    TypeInfoPropertyName = "PackageEvidenceRootIdentityAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.AuthoredProject),
    TypeInfoPropertyName = "PackageEvidenceRootProvenanceAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupIdentity.AuthoredProject),
    TypeInfoPropertyName = "PackageEvidenceGroupIdentityAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationFailure.AuthoredProject),
    TypeInfoPropertyName = "PackageEvidenceDeclarationFailureAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.RuntimeDependencyManifest),
    TypeInfoPropertyName = "PackageEvidenceRootFailureRuntimeManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.RuntimeDependencyManifest),
    TypeInfoPropertyName = "PackageEvidenceRootIdentityRuntimeManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.RuntimeDependencyManifest),
    TypeInfoPropertyName = "PackageEvidenceRootProvenanceRuntimeManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidencePackageIdentity.RuntimeDependencyManifest),
    TypeInfoPropertyName = "PackageEvidencePackageIdentityRuntimeManifest")]
[JsonSerializable(
    typeof(
        PackageDependencyEvidenceRelationshipIdentity
            .RuntimeDependencyManifest),
    TypeInfoPropertyName = "PackageEvidenceRelationshipIdentityRuntimeManifest")]
[JsonSerializable(
    typeof(
        PackageDependencyEvidenceRelationshipFailure
            .RuntimeDependencyManifest),
    TypeInfoPropertyName = "PackageEvidenceRelationshipFailureRuntimeManifest")]
[JsonSerializable(
    typeof(RuntimeDependencyGraphParentIdentity.Package),
    TypeInfoPropertyName = "RuntimeDependencyGraphParentPackage")]
[JsonSerializable(
    typeof(RuntimeDependencyGraphParentIdentity.Library),
    TypeInfoPropertyName = "RuntimeDependencyGraphParentLibrary")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.Available),
    TypeInfoPropertyName = "PackageEvidenceDeclarationAvailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.Available),
    TypeInfoPropertyName = "PackageEvidenceRelationshipAvailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.Available),
    TypeInfoPropertyName = "PackageEvidenceProcessingAvailable")]
[JsonSerializable(
    typeof(InspectionPortableProjection.Available),
    TypeInfoPropertyName = "InspectionPortableProjectionAvailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.NotApplicable),
    TypeInfoPropertyName = "PackageEvidenceDeclarationNotApplicable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.NotApplicable),
    TypeInfoPropertyName = "PackageEvidenceRelationshipNotApplicable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.NotApplicable),
    TypeInfoPropertyName = "PackageEvidenceProcessingNotApplicable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.Unavailable),
    TypeInfoPropertyName = "PackageEvidenceDeclarationUnavailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.Unavailable),
    TypeInfoPropertyName = "PackageEvidenceRelationshipUnavailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.Unavailable),
    TypeInfoPropertyName = "PackageEvidenceProcessingUnavailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.Failed),
    TypeInfoPropertyName = "PackageEvidenceDeclarationFailed")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.Failed),
    TypeInfoPropertyName = "PackageEvidenceRelationshipFailed")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.Failed),
    TypeInfoPropertyName = "PackageEvidenceProcessingFailed")]
[JsonSerializable(
    typeof(DependencyInspectionPackageManifestFailure.Acquisition),
    TypeInfoPropertyName = "DependencyInspectionManifestAcquisitionFailure")]
[JsonSerializable(
    typeof(DependencyInspectionRestoredTraversalOutcomeFailure.Graph),
    TypeInfoPropertyName = "DependencyInspectionRestoredOutcomeGraphFailure")]
public partial class DependencyInspectionJsonContext : JsonSerializerContext;
