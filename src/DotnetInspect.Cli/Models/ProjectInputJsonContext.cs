using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Models;

[JsonSourceGenerationOptions(
    Converters = [typeof(DotnetInspector.Sections.InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProjectInputDocument))]
[JsonSerializable(typeof(AuthoredProjectDependencyFactsResult.Available), TypeInfoPropertyName = "AuthoredFactsAvailable")]
[JsonSerializable(typeof(AuthoredProjectDependencyFactsResult.Incomplete), TypeInfoPropertyName = "AuthoredFactsIncomplete")]
[JsonSerializable(typeof(AuthoredProjectDependencyFactsResult.Failed), TypeInfoPropertyName = "AuthoredFactsFailed")]
[JsonSerializable(typeof(AuthoredProjectDependencyCondition.Unconditional))]
[JsonSerializable(typeof(AuthoredProjectDependencyCondition.TargetFramework))]
[JsonSerializable(typeof(AuthoredProjectDependencyCondition.Unresolved))]
[JsonSerializable(typeof(RestoredProjectDependencyTraversalResult.Available), TypeInfoPropertyName = "RestoredTraversalAvailable")]
[JsonSerializable(typeof(RestoredProjectDependencyTraversalResult.Unavailable), TypeInfoPropertyName = "RestoredTraversalUnavailable")]
[JsonSerializable(typeof(RestoredProjectDependencyTraversalResult.Failed), TypeInfoPropertyName = "RestoredTraversalFailed")]
[JsonSerializable(typeof(RestoredProjectDependencyTraversalFailure.Document))]
[JsonSerializable(typeof(RestoredProjectDependencyTraversalFailure.Graph))]
[JsonSerializable(typeof(RestoredProjectDeclarationResult.Available), TypeInfoPropertyName = "RestoredDeclarationAvailable")]
[JsonSerializable(typeof(RestoredProjectDeclarationResult.Unavailable), TypeInfoPropertyName = "RestoredDeclarationUnavailable")]
[JsonSerializable(typeof(RestoredProjectDeclarationResult.Failed), TypeInfoPropertyName = "RestoredDeclarationFailed")]
[JsonSerializable(typeof(RestoredProjectGraphResult.Available), TypeInfoPropertyName = "RestoredGraphAvailable")]
[JsonSerializable(typeof(RestoredProjectGraphResult.Unavailable), TypeInfoPropertyName = "RestoredGraphUnavailable")]
[JsonSerializable(typeof(RestoredProjectGraphResult.Failed), TypeInfoPropertyName = "RestoredGraphFailed")]
[JsonSerializable(typeof(RestoredProjectPackagePruningResult.Available), TypeInfoPropertyName = "RestoredPruningAvailable")]
[JsonSerializable(typeof(RestoredProjectPackagePruningResult.Unavailable), TypeInfoPropertyName = "RestoredPruningUnavailable")]
[JsonSerializable(typeof(RestoredProjectPackagePruningResult.Failed), TypeInfoPropertyName = "RestoredPruningFailed")]
[JsonSerializable(typeof(PackageDependencyEvidenceRootIdentity.Package), TypeInfoPropertyName = "PackageRootIdentity")]
[JsonSerializable(typeof(PackageDependencyEvidenceRootIdentity.RestoredProject), TypeInfoPropertyName = "RestoredRootIdentity")]
[JsonSerializable(typeof(PackageDependencyEvidenceRootIdentity.AuthoredProject), TypeInfoPropertyName = "AuthoredRootIdentity")]
[JsonSerializable(typeof(PackageDependencyEvidenceGroupIdentity.Package), TypeInfoPropertyName = "PackageGroupIdentity")]
[JsonSerializable(typeof(PackageDependencyEvidenceGroupIdentity.RestoredProject), TypeInfoPropertyName = "RestoredGroupIdentity")]
[JsonSerializable(typeof(PackageDependencyEvidenceGroupIdentity.AuthoredProject), TypeInfoPropertyName = "AuthoredGroupIdentity")]
[JsonSerializable(typeof(PackageDependencyEvidenceGroupOccurrence.Package), TypeInfoPropertyName = "PackageGroupOccurrence")]
[JsonSerializable(typeof(PackageDependencyEvidenceGroupOccurrence.RestoredProject), TypeInfoPropertyName = "RestoredGroupOccurrence")]
[JsonSerializable(typeof(RestoredProjectGraphParentIdentity.Package), TypeInfoPropertyName = "RestoredPackageParentIdentity")]
internal partial class ProjectInputJsonContext : JsonSerializerContext
{
    internal static JsonTypeInfo<ProjectInputDocument> ContentTypeInfo { get; }

    static ProjectInputJsonContext()
    {
        // The generated Default context must be initialized before this metadata.
        ContentTypeInfo = CreateContentTypeInfo();
    }

    private static JsonTypeInfo<ProjectInputDocument> CreateContentTypeInfo()
    {
        var options = new JsonSerializerOptions(Default.Options)
        {
            TypeInfoResolver = Default.WithAddedModifier(ConfigureProviderUnion),
        };
        options.MakeReadOnly();
        return (JsonTypeInfo<ProjectInputDocument>)
            options.GetTypeInfo(typeof(ProjectInputDocument));
    }

    private static void ConfigureProviderUnion(JsonTypeInfo info)
    {
        // Provider unions remain wire-neutral; this host supplies their JSON tags.
        JsonDerivedType[]? variants = info.Type switch
        {
            var type when type == typeof(AuthoredProjectDependencyFactsResult) =>
            [
                new(typeof(AuthoredProjectDependencyFactsResult.Available), "available"),
                new(typeof(AuthoredProjectDependencyFactsResult.Incomplete), "incomplete"),
                new(typeof(AuthoredProjectDependencyFactsResult.Failed), "failed"),
            ],
            var type when type == typeof(AuthoredProjectDependencyCondition) =>
            [
                new(typeof(AuthoredProjectDependencyCondition.Unconditional), "unconditional"),
                new(typeof(AuthoredProjectDependencyCondition.TargetFramework), "target-framework"),
                new(typeof(AuthoredProjectDependencyCondition.Unresolved), "unresolved"),
            ],
            var type when type == typeof(RestoredProjectDependencyTraversalResult) =>
            [
                new(typeof(RestoredProjectDependencyTraversalResult.Available), "available"),
                new(typeof(RestoredProjectDependencyTraversalResult.Unavailable), "unavailable"),
                new(typeof(RestoredProjectDependencyTraversalResult.Failed), "failed"),
            ],
            var type when type == typeof(RestoredProjectDependencyTraversalFailure) =>
            [
                new(typeof(RestoredProjectDependencyTraversalFailure.Document), "document"),
                new(typeof(RestoredProjectDependencyTraversalFailure.Graph), "graph"),
            ],
            var type when type == typeof(RestoredProjectDeclarationResult) =>
            [
                new(typeof(RestoredProjectDeclarationResult.Available), "available"),
                new(typeof(RestoredProjectDeclarationResult.Unavailable), "unavailable"),
                new(typeof(RestoredProjectDeclarationResult.Failed), "failed"),
            ],
            var type when type == typeof(RestoredProjectGraphResult) =>
            [
                new(typeof(RestoredProjectGraphResult.Available), "available"),
                new(typeof(RestoredProjectGraphResult.Unavailable), "unavailable"),
                new(typeof(RestoredProjectGraphResult.Failed), "failed"),
            ],
            var type when type == typeof(RestoredProjectPackagePruningResult) =>
            [
                new(typeof(RestoredProjectPackagePruningResult.Available), "available"),
                new(typeof(RestoredProjectPackagePruningResult.Unavailable), "unavailable"),
                new(typeof(RestoredProjectPackagePruningResult.Failed), "failed"),
            ],
            _ => null,
        };
        if (variants is null)
            return;

        info.PolymorphismOptions = new()
        {
            TypeDiscriminatorPropertyName = "kind",
        };
        foreach (JsonDerivedType variant in variants)
            info.PolymorphismOptions.DerivedTypes.Add(variant);
    }
}
