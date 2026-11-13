using QuerySpace.Vocabulary;

namespace DotnetInspector.InspectionContracts;

public static class PackageQueryDurableRowContract
{
    public const int SlotCount = 13;
    public const string ContractIdentity = "package-query.durable-row";
    public const string Direction = "serialize";
    public const string VocabularyCatalog = "dotnet-inspect.product";
    public const string VocabularySnapshotIdentity =
        "sha256:1c48720af80cda1e0c37e0759f6b3c987c35b6358db7a44a1eadd1671e47ec73";
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

    public static VocabularySnapshotReference CreateVocabularySnapshotReference()
    {
        var catalog = new VocabularyCatalogIdentity(VocabularyCatalog);
        var vocabulary = new VocabularyIdentity(catalog, Vocabulary);
        return new(
            catalog,
            new QuerySpace.Vocabulary.VocabularySnapshotIdentity(
                VocabularySnapshotIdentity),
            [
                new(vocabulary, PackageId),
                new(vocabulary, Version),
                new(vocabulary, Tier),
                new(vocabulary, Answers),
                new(vocabulary, Evidence),
                new(vocabulary, TotalDownloads),
                new(vocabulary, Verified),
                new(vocabulary, Producer),
                new(vocabulary, Description),
                new(vocabulary, RootRequest),
                new(vocabulary, Owners),
                new(vocabulary, Manifest),
                new(vocabulary, EcosystemAdmission),
            ]);
    }
}
