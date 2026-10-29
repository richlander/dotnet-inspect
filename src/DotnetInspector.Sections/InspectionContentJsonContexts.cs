using System.Text.Json.Serialization;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    Converters =
    [
        typeof(InertStringJsonConverter),
        typeof(PackageRootReacquisitionRequestJsonConverter),
    ],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(
    typeof(PackageManifestDependencyFactsResult.Available),
    TypeInfoPropertyName = "PackageManifestDependencyFactsAvailable")]
[JsonSerializable(
    typeof(PackageManifestDependencyFactsResult.Failed),
    TypeInfoPropertyName = "PackageManifestDependencyFactsFailed")]
[JsonSerializable(
    typeof(PackageManifestFrameworkReferenceFactsResult.Available),
    TypeInfoPropertyName = "PackageManifestFrameworkReferenceFactsAvailable")]
[JsonSerializable(
    typeof(PackageManifestFrameworkReferenceFactsResult.Failed),
    TypeInfoPropertyName = "PackageManifestFrameworkReferenceFactsFailed")]
[JsonSerializable(typeof(PackageQueryDocument))]
public partial class PackageQueryJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(
    typeof(InspectionEnvelope<PackageDependencyInspectionOutcome>))]
[JsonSerializable(
    typeof(PackageDependencyInspectionOutcome.Available),
    TypeInfoPropertyName = "PackageDependencyInspectionAvailable")]
[JsonSerializable(
    typeof(PackageDependencyInspectionOutcome.NoManifest),
    TypeInfoPropertyName = "PackageDependencyInspectionNoManifest")]
[JsonSerializable(
    typeof(PackageDependencyInspectionOutcome.Failed),
    TypeInfoPropertyName = "PackageDependencyInspectionFailed")]
[JsonSerializable(
    typeof(PackageManifestDependencyFactsResult.Available),
    TypeInfoPropertyName = "PackageManifestDependencyFactsAvailable")]
[JsonSerializable(
    typeof(PackageManifestDependencyFactsResult.Failed),
    TypeInfoPropertyName = "PackageManifestDependencyFactsFailed")]
[JsonSerializable(
    typeof(PackageManifestFrameworkReferenceFactsResult.Available),
    TypeInfoPropertyName = "PackageManifestFrameworkReferenceFactsAvailable")]
[JsonSerializable(
    typeof(PackageManifestFrameworkReferenceFactsResult.Failed),
    TypeInfoPropertyName = "PackageManifestFrameworkReferenceFactsFailed")]
[JsonSerializable(
    typeof(PackageManifestFacts),
    TypeInfoPropertyName = "PackageManifestFactsModel")]
[JsonSerializable(
    typeof(PackageDependencyGroups),
    TypeInfoPropertyName = "PackageDependencyGroupsModel")]
public partial class PackageDependencyInspectionJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LibraryQueryDocument))]
public partial class LibraryQueryJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PackageAssemblySemanticQueryDocument))]
[JsonSerializable(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.Matched),
    TypeInfoPropertyName = "PackageAssemblySemanticQueryCandidateMatched")]
[JsonSerializable(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.NoMatch),
    TypeInfoPropertyName = "PackageAssemblySemanticQueryCandidateNoMatch")]
[JsonSerializable(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.NotApplicable),
    TypeInfoPropertyName = "PackageAssemblySemanticQueryCandidateNotApplicable")]
[JsonSerializable(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.Failure),
    TypeInfoPropertyName = "PackageAssemblySemanticQueryCandidateFailure")]
[JsonSerializable(
    typeof(PackageAssemblySemanticQueryFailureReason.AggregateOccurrenceLimit),
    TypeInfoPropertyName = "PackageAssemblySemanticQueryAggregateOccurrenceLimit")]
[JsonSerializable(
    typeof(PackageAssemblyEvaluationOutcome.Matched),
    TypeInfoPropertyName = "PackageAssemblyEvaluationMatched")]
[JsonSerializable(
    typeof(PackageAssemblyEvaluationOutcome.NoMatch),
    TypeInfoPropertyName = "PackageAssemblyEvaluationNoMatch")]
[JsonSerializable(
    typeof(PackageAssemblyEvaluationOutcome.NotApplicable),
    TypeInfoPropertyName = "PackageAssemblyEvaluationNotApplicable")]
[JsonSerializable(
    typeof(PackageAssemblyEvaluationOutcome.Failure),
    TypeInfoPropertyName = "PackageAssemblyEvaluationFailure")]
public partial class PackageAssemblySemanticQueryJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExactTypeInspectionResult))]
public partial class ExactTypeInspectionJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExactLibraryApiInspectionResult))]
public partial class ExactLibraryApiInspectionJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TypeApiDeclarationResult))]
[JsonSerializable(typeof(InspectionEnvelope<TypeApiDeclarationResult>))]
public partial class TypeApiDeclarationInspectionJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TypeDeclaredMethodPopulationOutcome))]
[JsonSerializable(typeof(
    InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>))]
public partial class TypeDeclaredMethodPopulationInspectionJsonContext
    : JsonSerializerContext;
