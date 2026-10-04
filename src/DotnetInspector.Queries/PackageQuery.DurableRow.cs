using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using QuerySpace;
using TsJsExport;

namespace DotnetInspector.Queries;

[JsonConverter(typeof(JsonStringEnumConverter<PackageQueryDurableRowTier>))]
public enum PackageQueryDurableRowTier
{
    SearchMetadata,
    Nuspec,
    PackageContent,
    Assembly,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<PackageQueryDurableEvidenceScope>))]
public enum PackageQueryDurableEvidenceScope
{
    Package,
    Query,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<
        PackageQueryDurableManifestIdentityProvenance>))]
public enum PackageQueryDurableManifestIdentityProvenance
{
    ExpectedCoordinate,
    SelfAttested,
}

public sealed record PackageQueryDurableTerm(
    string Key,
    string Operator,
    string Value);

public sealed record PackageQueryDurableAnswer(
    string Id,
    string Value,
    PackageQueryDurableTerm? Term = null);

public sealed record PackageQueryDurableEvidenceSummary(
    int Count,
    string[] Preview);

public sealed record PackageQueryDurableEvidenceProperty(
    string Name,
    string Value);

public sealed record PackageQueryDurableEvidence(
    string Id,
    PackageQueryDurableEvidenceScope Scope,
    PackageQueryDurableEvidenceSummary? Summary,
    PackageQueryDurableEvidenceProperty[] Properties,
    long? Number,
    PackageQueryDurableTerm? Term = null);

public sealed record PackageQueryDurableDeclaredDependency(
    string Id,
    string VersionRange);

public sealed record PackageQueryDurableDeclaredDependencyGroup(
    string TargetFramework,
    PackageQueryDurableDeclaredDependency[] Dependencies,
    bool IsImplicitManifestGroup);

public sealed record PackageQueryDurableManifest(
    string PackageId,
    string Version,
    string ManifestVersion,
    string? Description,
    string? Authors,
    string? Repository,
    string? RepositoryType,
    string? RepositoryCommit,
    string? License,
    string? LicenseUrl,
    string[] PackageTypes,
    bool IsToolPackage,
    string? ReadmeFile,
    PackageQueryDurableDeclaredDependencyGroup[] DependencyGroups,
    string? IconFile,
    string? IconUrl,
    PackageQueryDurableManifestIdentityProvenance IdentityProvenance);

[JsExportJsonSchema(
    PackageQueryDurableRowContract.ContractIdentity,
    PackageQueryDurableRowContract.Direction,
    PackageQueryDurableRowContract.VocabularyCatalog,
    PackageQueryDurableRowContract.VocabularySnapshotIdentity)]
public sealed record PackageQueryDurableRow(
    [property: JsExportJsonSchemaSlot(
        0,
        PackageQueryDurableRowContract.PackageId,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.PackageId)]
    string PackageId,
    [property: JsExportJsonSchemaSlot(
        1,
        PackageQueryDurableRowContract.Version,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Version)]
    string Version,
    [property: JsExportJsonSchemaSlot(
        2,
        PackageQueryDurableRowContract.Tier,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Tier)]
    PackageQueryDurableRowTier Tier,
    [property: JsExportJsonSchemaSlot(
        3,
        PackageQueryDurableRowContract.Answers,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Answers)]
    PackageQueryDurableAnswer[] Answers,
    [property: JsExportJsonSchemaSlot(
        4,
        PackageQueryDurableRowContract.Evidence,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Evidence)]
    PackageQueryDurableEvidence[] Evidence,
    [property: JsExportJsonSchemaSlot(
        5,
        PackageQueryDurableRowContract.TotalDownloads,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.TotalDownloads)]
    long? TotalDownloads,
    [property: JsExportJsonSchemaSlot(
        6,
        PackageQueryDurableRowContract.Verified,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Verified)]
    bool? Verified,
    [property: JsExportJsonSchemaSlot(
        7,
        PackageQueryDurableRowContract.Producer,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Producer)]
    string Producer,
    [property: JsExportJsonSchemaSlot(
        8,
        PackageQueryDurableRowContract.Description,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Description)]
    string? Description,
    [property: JsExportJsonSchemaSlot(
        9,
        PackageQueryDurableRowContract.RootRequest,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.RootRequest)]
    string? RootRequest,
    [property: JsExportJsonSchemaSlot(
        10,
        PackageQueryDurableRowContract.Owners,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Owners)]
    string[] Owners,
    [property: JsExportJsonSchemaSlot(
        11,
        PackageQueryDurableRowContract.Manifest,
        PackageQueryDurableRowContract.Vocabulary,
        PackageQueryDurableRowContract.Manifest)]
    PackageQueryDurableManifest? Manifest)
{
    public static PackageQueryDurableRow Create(PackageQueryMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new(
            match.Package.PackageId,
            match.Package.Version,
            match.Tier switch
            {
                PackageQueryAcquisitionTier.SearchMetadata =>
                    PackageQueryDurableRowTier.SearchMetadata,
                PackageQueryAcquisitionTier.Nuspec =>
                    PackageQueryDurableRowTier.Nuspec,
                PackageQueryAcquisitionTier.PackageContent =>
                    PackageQueryDurableRowTier.PackageContent,
                _ => throw new InvalidOperationException(
                    "Unknown package-query match tier."),
            },
            [
                .. match.Answers.Select(answer =>
                    new PackageQueryDurableAnswer(
                        answer.Id,
                        answer.Value,
                        Project(answer.Term))),
            ],
            [
                .. match.Evidence.Select(evidence =>
                    new PackageQueryDurableEvidence(
                        evidence.Id,
                        evidence.Scope switch
                        {
                            PackageQueryEvidenceScope.Package =>
                                PackageQueryDurableEvidenceScope.Package,
                            PackageQueryEvidenceScope.Query =>
                                PackageQueryDurableEvidenceScope.Query,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query evidence scope."),
                        },
                        evidence.Summary is { } summary
                            ? new(
                                summary.Count,
                                [
                                    .. summary.Preview.Select(
                                        value => value.ToString()),
                                ])
                            : null,
                        [
                            .. evidence.Properties.Select(property =>
                                new PackageQueryDurableEvidenceProperty(
                                    property.Name,
                                    property.Value)),
                        ],
                        evidence.Number,
                        Project(evidence.Term))),
            ],
            match.Package.TotalDownloads,
            match.Package.Verified,
            match.Package.Source.Producer.Display.ToString(),
            match.Package.Description,
            match.LibraryLiteral?.RootRequest.Encode(),
            [.. match.Package.Owners],
            match.Package.Manifest is { } manifest
                ? Project(manifest)
                : null);
    }

    static PackageQueryDurableTerm? Project(PortableQueryTerm? term) =>
        term is null
            ? null
            : new(
                term.Key,
                PortableQueryModel.TextOf(term.Operator),
                term.Value);

    static PackageQueryDurableManifest Project(
        PackageManifestFacts manifest) =>
        new(
            manifest.Coordinate.PackageId,
            manifest.Coordinate.Version,
            manifest.ManifestVersion,
            manifest.Description?.ToString(),
            manifest.Authors,
            manifest.Repository,
            manifest.RepositoryType,
            manifest.RepositoryCommit,
            manifest.License,
            manifest.LicenseUrl,
            [.. manifest.PackageTypes],
            manifest.IsToolPackage,
            manifest.ReadmeFile,
            [
                .. manifest.DependencyGroups.Select(group =>
                    new PackageQueryDurableDeclaredDependencyGroup(
                        group.TargetFramework,
                        [
                            .. group.Dependencies.Select(dependency =>
                                new PackageQueryDurableDeclaredDependency(
                                    dependency.Id,
                                    dependency.VersionRange)),
                        ],
                        group.IsImplicitManifestGroup)),
            ],
            manifest.IconFile,
            manifest.IconUrl,
            manifest.IdentityProvenance switch
            {
                PackageManifestIdentityProvenance.ExpectedCoordinate =>
                    PackageQueryDurableManifestIdentityProvenance
                        .ExpectedCoordinate,
                PackageManifestIdentityProvenance.SelfAttested =>
                    PackageQueryDurableManifestIdentityProvenance.SelfAttested,
                _ => throw new InvalidOperationException(
                    "Unknown package-manifest identity provenance."),
            });
}

public static class PackageQueryDurableRowContract
{
    public const string ContractIdentity = "package-query.durable-row";
    public const string Direction = "serialize";
    public const string VocabularyCatalog = "dotnet-inspect.product";
    public const string VocabularySnapshotIdentity =
        "sha256:738fb458557d93aa6681947f43cc9ba9315260177f7bbe8af83c02d49b437f36";
    public const string Vocabulary = "package-query.durable-row";

    public const string PackageId = "package-id";
    public const string Version = "version";
    public const string Tier = "tier";
    public const string Answers = "answers";
    public const string Evidence = "evidence";
    public const string TotalDownloads = "total-downloads";
    public const string Verified = "verified";
    public const string Producer = "producer";
    public const string Description = "description";
    public const string RootRequest = "root-request";
    public const string Owners = "owners";
    public const string Manifest = "manifest";
}
