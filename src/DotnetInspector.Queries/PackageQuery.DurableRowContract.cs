using QuerySpace.Vocabulary;

namespace DotnetInspector.Queries;

public static class PackageQueryDurableRowContract
{
    public const int SlotCount = 13;
    public const string ContractIdentity = "package-query.durable-row";
    public const string Direction = "serialize";
    public const string VocabularyCatalog = "dotnet-inspect.product";
    public const string VocabularySnapshotIdentity =
        "sha256:f0527bd80f85c7683fcf3797da980ef38b3266116f375cc44323796775d884df";
    public const string Vocabulary = "package-query.durable-row";
    public const string VocabularyLabel = "Package Query Durable Row";

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
    public const string EcosystemAdmission = "ecosystem-admission";

    public static VocabularyDefinition DeclareVocabulary(
        VocabularyCatalogIdentity catalog)
    {
        var identity = new VocabularyIdentity(catalog, Vocabulary);
        return new(
            identity,
            VocabularyLabel,
            "Stable semantic fields in the compact Package Query durable row.",
            maps: null,
            [
                Term(
                    identity,
                    PackageId,
                    "Package",
                    "Exact NuGet package identity."),
                Term(
                    identity,
                    Version,
                    "Version",
                    "Exact package version."),
                Term(
                    identity,
                    Tier,
                    "Acquisition Tier",
                    "Highest acquisition tier used to establish the match."),
                Term(
                    identity,
                    Answers,
                    "Answers",
                    "Semantic values that satisfied selected query terms."),
                Term(
                    identity,
                    Evidence,
                    "Evidence",
                    "Structured package- and query-scoped supporting facts."),
                Term(
                    identity,
                    TotalDownloads,
                    "Lifetime Downloads",
                    "Reported lifetime package downloads when available."),
                Term(
                    identity,
                    Verified,
                    "Verified",
                    "Reported package-owner verification when available."),
                Term(
                    identity,
                    Producer,
                    "Source",
                    "Package source that produced the result."),
                Term(
                    identity,
                    Description,
                    "Description",
                    "Package description when available."),
                Term(
                    identity,
                    RootRequest,
                    "Root Request",
                    "Reacquisition request for a semantic package result."),
                Term(
                    identity,
                    Owners,
                    "Owners",
                    "Reported package owners."),
                Term(
                    identity,
                    Manifest,
                    "Manifest",
                    "Acquired package-manifest facts when available."),
                Term(
                    identity,
                    EcosystemAdmission,
                    "Ecosystem Admission",
                    "Exact ecosystem registration evidence that admitted the package result."),
            ]);
    }

    private static VocabularyTerm Term(
        VocabularyIdentity vocabulary,
        string identity,
        string displayLabel,
        string summary) =>
        new(
            new(vocabulary, identity),
            displayLabel,
            summary);
}
