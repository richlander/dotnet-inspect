using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    UseStringEnumConverter = true,
    Converters = [typeof(InertStringJsonConverter)])]
[JsonSerializable(typeof(DependencyInspectionContent))]
[JsonSerializable(typeof(DependencyInspectionEvidenceDocument))]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.Package),
    TypeInfoPropertyName = "EvidenceRootFailurePackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.RestoredProject),
    TypeInfoPropertyName = "EvidenceRootFailureRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.AuthoredProject),
    TypeInfoPropertyName = "EvidenceRootFailureAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootFailure.RuntimeDependencyManifest),
    TypeInfoPropertyName = "EvidenceRootFailureRuntimeDependencyManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.Package),
    TypeInfoPropertyName = "EvidenceRootIdentityPackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.RestoredProject),
    TypeInfoPropertyName = "EvidenceRootIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.AuthoredProject),
    TypeInfoPropertyName = "EvidenceRootIdentityAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootIdentity.RuntimeDependencyManifest),
    TypeInfoPropertyName = "EvidenceRootIdentityRuntimeDependencyManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.Package),
    TypeInfoPropertyName = "EvidenceRootProvenancePackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.RestoredProject),
    TypeInfoPropertyName = "EvidenceRootProvenanceRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.AuthoredProject),
    TypeInfoPropertyName = "EvidenceRootProvenanceAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRootProvenance.RuntimeDependencyManifest),
    TypeInfoPropertyName = "EvidenceRootProvenanceRuntimeDependencyManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupIdentity.Package),
    TypeInfoPropertyName = "EvidenceGroupIdentityPackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupIdentity.RestoredProject),
    TypeInfoPropertyName = "EvidenceGroupIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupIdentity.AuthoredProject),
    TypeInfoPropertyName = "EvidenceGroupIdentityAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupOccurrence.Package),
    TypeInfoPropertyName = "EvidenceGroupOccurrencePackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceGroupOccurrence.RestoredProject),
    TypeInfoPropertyName = "EvidenceGroupOccurrenceRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationFailure.RestoredProject),
    TypeInfoPropertyName = "EvidenceDeclarationFailureRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationFailure.AuthoredProject),
    TypeInfoPropertyName = "EvidenceDeclarationFailureAuthoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.Available),
    TypeInfoPropertyName = "EvidenceDeclarationAvailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.NotApplicable),
    TypeInfoPropertyName = "EvidenceDeclarationNotApplicable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.Unavailable),
    TypeInfoPropertyName = "EvidenceDeclarationUnavailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceDeclarationResult.Failed),
    TypeInfoPropertyName = "EvidenceDeclarationFailed")]
[JsonSerializable(
    typeof(PackageDependencyEvidencePackageIdentity.RestoredProject),
    TypeInfoPropertyName = "EvidencePackageIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidencePackageIdentity.RuntimeDependencyManifest),
    TypeInfoPropertyName = "EvidencePackageIdentityRuntimeDependencyManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipParentIdentity.Package),
    TypeInfoPropertyName = "EvidenceRelationshipParentPackage")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipIdentity.RestoredProject),
    TypeInfoPropertyName = "EvidenceRelationshipIdentityRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipIdentity
        .RuntimeDependencyManifest),
    TypeInfoPropertyName = "EvidenceRelationshipIdentityRuntimeDependencyManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipFailure.RestoredProject),
    TypeInfoPropertyName = "EvidenceRelationshipFailureRestoredProject")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipFailure
        .RuntimeDependencyManifest),
    TypeInfoPropertyName = "EvidenceRelationshipFailureRuntimeDependencyManifest")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.Available),
    TypeInfoPropertyName = "EvidenceRelationshipAvailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.NotApplicable),
    TypeInfoPropertyName = "EvidenceRelationshipNotApplicable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.Unavailable),
    TypeInfoPropertyName = "EvidenceRelationshipUnavailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceRelationshipResult.Failed),
    TypeInfoPropertyName = "EvidenceRelationshipFailed")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.Available),
    TypeInfoPropertyName = "EvidenceProcessingAvailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.NotApplicable),
    TypeInfoPropertyName = "EvidenceProcessingNotApplicable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.Unavailable),
    TypeInfoPropertyName = "EvidenceProcessingUnavailable")]
[JsonSerializable(
    typeof(PackageDependencyEvidenceProcessingResult.Failed),
    TypeInfoPropertyName = "EvidenceProcessingFailed")]
[JsonSerializable(
    typeof(DependencyGraphNodeIdentity.Package),
    TypeInfoPropertyName = "DependencyGraphNodePackage")]
[JsonSerializable(
    typeof(DependencyGraphNodeIdentity.RestoredProject),
    TypeInfoPropertyName = "DependencyGraphNodeRestoredProject")]
[JsonSerializable(
    typeof(DependencyGraphEvidenceIdentity.AssemblyReference),
    TypeInfoPropertyName = "DependencyGraphEvidenceAssemblyReference")]
[JsonSerializable(
    typeof(DependencyGraphEvidenceIdentity.PackageVersionConstraint),
    TypeInfoPropertyName = "DependencyGraphEvidencePackageVersionConstraint")]
[JsonSerializable(
    typeof(DependencyGraphEvidenceIdentity.PackageDeclaration),
    TypeInfoPropertyName = "DependencyGraphEvidencePackageDeclaration")]
[JsonSerializable(
    typeof(DependencyGraphEvidenceIdentity.RestoredProjectRelationship),
    TypeInfoPropertyName = "DependencyGraphEvidenceRestoredProjectRelationship")]
[JsonSerializable(
    typeof(DependencyGraphEvidenceIdentity.RestoredPackageRelationship),
    TypeInfoPropertyName = "DependencyGraphEvidenceRestoredPackageRelationship")]
[JsonSerializable(
    typeof(PackageDependencyCandidateFailure.AuthorizationDenied),
    TypeInfoPropertyName = "PackageCandidateFailureAuthorizationDenied")]
[JsonSerializable(
    typeof(PackageDependencyCandidateFailure.NoMatchingVersion),
    TypeInfoPropertyName = "PackageCandidateFailureNoMatchingVersion")]
[JsonSerializable(
    typeof(PackageDependencyCandidateFailure.ResolvedCoordinateMismatch),
    TypeInfoPropertyName = "PackageCandidateFailureResolvedCoordinateMismatch")]
[JsonSerializable(
    typeof(PackageDependencyCandidateIncomplete.PinnedAuthorization),
    TypeInfoPropertyName = "PackageCandidateIncompletePinnedAuthorization")]
[JsonSerializable(
    typeof(PackageDependencyCandidateIncomplete.VersionDiscovery),
    TypeInfoPropertyName = "PackageCandidateIncompleteVersionDiscovery")]
[JsonSerializable(
    typeof(PackageDependencyCandidateResult.Resolved),
    TypeInfoPropertyName = "PackageCandidateResultResolved")]
[JsonSerializable(
    typeof(PackageDependencyCandidateResult.Failed),
    TypeInfoPropertyName = "PackageCandidateResultFailed")]
[JsonSerializable(
    typeof(PackageDependencyCandidateResult.Incomplete),
    TypeInfoPropertyName = "PackageCandidateResultIncomplete")]
[JsonSerializable(
    typeof(PackageHouseDependencySubject.Declaration),
    TypeInfoPropertyName = "PackageHouseSubjectDeclaration")]
[JsonSerializable(
    typeof(PackageHouseDependencySubject.Relationship),
    TypeInfoPropertyName = "PackageHouseSubjectRelationship")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.Evaluated),
    TypeInfoPropertyName = "PackageHousePruningEvaluated")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult
        .ApplicationAuthoredExemption),
    TypeInfoPropertyName = "PackageHousePruningApplicationAuthoredExemption")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.UnattributedAuthorship),
    TypeInfoPropertyName = "PackageHousePruningUnattributedAuthorship")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.ProcessingIncomplete),
    TypeInfoPropertyName = "PackageHousePruningProcessingIncomplete")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.ProcessingUnavailable),
    TypeInfoPropertyName = "PackageHousePruningProcessingUnavailable")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.ProcessingFailed),
    TypeInfoPropertyName = "PackageHousePruningProcessingFailed")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.RuntimeProjected),
    TypeInfoPropertyName = "PackageHousePruningRuntimeProjected")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.PreviouslyEvaluated),
    TypeInfoPropertyName = "PackageHousePruningPreviouslyEvaluated")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.ProcessingNotEvidenced),
    TypeInfoPropertyName = "PackageHousePruningProcessingNotEvidenced")]
[JsonSerializable(
    typeof(PackageHouseDependencyPruningResult.TargetUnavailable),
    TypeInfoPropertyName = "PackageHousePruningTargetUnavailable")]
[JsonSerializable(
    typeof(PackageDependencyTraversalCandidateFailure.AuthorizationDenied),
    TypeInfoPropertyName = "TraversalCandidateFailureAuthorizationDenied")]
[JsonSerializable(
    typeof(PackageDependencyTraversalCandidateFailure.NoMatchingVersion),
    TypeInfoPropertyName = "TraversalCandidateFailureNoMatchingVersion")]
[JsonSerializable(
    typeof(PackageDependencyTraversalCandidateIncomplete.PinnedAuthorization),
    TypeInfoPropertyName = "TraversalCandidateIncompletePinnedAuthorization")]
[JsonSerializable(
    typeof(PackageDependencyTraversalCandidateIncomplete.VersionDiscovery),
    TypeInfoPropertyName = "TraversalCandidateIncompleteVersionDiscovery")]
[JsonSerializable(
    typeof(PackageDependencyTraversalCandidateResult.Resolved),
    TypeInfoPropertyName = "TraversalCandidateResultResolved")]
[JsonSerializable(
    typeof(PackageDependencyTraversalCandidateResult.Failed),
    TypeInfoPropertyName = "TraversalCandidateResultFailed")]
[JsonSerializable(
    typeof(PackageDependencyTraversalCandidateResult.Incomplete),
    TypeInfoPropertyName = "TraversalCandidateResultIncomplete")]
[JsonSerializable(
    typeof(RestoredProjectDependencyTraversalFailure.Document),
    TypeInfoPropertyName = "RestoredTraversalFailureDocument")]
[JsonSerializable(
    typeof(RestoredProjectDependencyTraversalFailure.Graph),
    TypeInfoPropertyName = "RestoredTraversalFailureGraph")]
public partial class DependencyInspectionJsonContext : JsonSerializerContext;
