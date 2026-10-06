using DotnetInspector.InspectionContracts;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Queries;

public static class PackageQueryDurableRowVocabulary
{
    public static VocabularyDefinition Declare(
        VocabularyCatalogIdentity catalog)
    {
        var identity = new VocabularyIdentity(
            catalog,
            PackageQueryDurableRowContract.Vocabulary);
        return new(
            identity,
            "Package Query Durable Row",
            "Stable semantic fields in the compact Package Query durable row.",
            maps: null,
            [
                Term(
                    identity,
                    PackageQueryDurableRowContract.PackageId,
                    "Package",
                    "Exact NuGet package identity."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Version,
                    "Version",
                    "Exact package version."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Tier,
                    "Acquisition Tier",
                    "Highest acquisition tier used to establish the match."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Answers,
                    "Answers",
                    "Semantic values that satisfied selected query terms."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Evidence,
                    "Evidence",
                    "Structured package- and query-scoped supporting facts."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.TotalDownloads,
                    "Lifetime Downloads",
                    "Reported lifetime package downloads when available."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Verified,
                    "Verified",
                    "Reported package-owner verification when available."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Producer,
                    "Source",
                    "Package source that produced the result."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Description,
                    "Description",
                    "Package description when available."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.RootRequest,
                    "Root Request",
                    "Reacquisition request for a semantic package result."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Owners,
                    "Owners",
                    "Reported package owners."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.Manifest,
                    "Manifest",
                    "Acquired package-manifest facts when available."),
                Term(
                    identity,
                    PackageQueryDurableRowContract.EcosystemAdmission,
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
