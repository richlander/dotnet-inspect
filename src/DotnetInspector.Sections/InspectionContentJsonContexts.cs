using System.Text.Json.Serialization;

using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PackageQueryDocument))]
public partial class PackageQueryJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PackageAssemblySemanticQueryDocument))]
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
