namespace DotnetInspector.Queries;

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
