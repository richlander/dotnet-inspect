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
}
