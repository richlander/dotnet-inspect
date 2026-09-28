namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemDependencyRecognitionProfileTests
{
    [Fact]
    public void ShippedProfileMatchesTheApprovedProductVocabulary()
    {
        EcosystemDependencyRecognitionProfile profile =
            EcosystemPackCatalog.DependencyRecognitionProfile;

        Assert.Equal(7, profile.Entries.Length);
        Assert.Equal(15, profile.PackageAssociationCount);
        Assert.Equal(20, profile.AssemblyAssociationCount);
        Assert.Equal(
            [
                EcosystemPackIds.Runtime,
                EcosystemPackIds.MicrosoftExtensions,
                EcosystemPackIds.AspNetCore,
                EcosystemPackIds.Aspire,
                EcosystemPackIds.AI,
                EcosystemPackIds.Blazor,
                EcosystemPackIds.Maui,
            ],
            profile.Entries.Select(entry => entry.Ecosystem.Id));

        EcosystemDependencyProfileEntry runtime = profile.Entries[0];
        Assert.Contains(
            runtime.Associations,
            association =>
                association.Domain
                    == EcosystemDependencyIdentityDomain.PackageId
                && association.Kind
                    == EcosystemDependencyAssociationKind.Family
                && association.Value == "System");
        Assert.Contains(
            runtime.Associations,
            association =>
                association.Domain
                    == EcosystemDependencyIdentityDomain.AssemblyName
                && association.Kind
                    == EcosystemDependencyAssociationKind.Exact
                && association.Value == "netstandard");

        // AI recognizes its lab-published roots exactly, so community
        // packages such as Anthropic.SDK stay unrecognized.
        EcosystemDependencyProfileEntry ai = Assert.Single(
            profile.Entries,
            entry => entry.Ecosystem.Id == EcosystemPackIds.AI);
        Assert.Equal(
            [
                "PackageId Family Microsoft.Extensions.AI",
                "AssemblyName Family Microsoft.Extensions.AI",
                "PackageId Exact OpenAI",
                "PackageId Exact Anthropic",
                "PackageId Exact Google.GenAI",
                "PackageId Exact ModelContextProtocol",
                "PackageId Exact Microsoft.Agents.AI",
                "AssemblyName Exact OpenAI",
                "AssemblyName Exact Anthropic",
                "AssemblyName Exact Google.GenAI",
                "AssemblyName Exact ModelContextProtocol",
                "AssemblyName Exact Microsoft.Agents.AI",
            ],
            ai.Associations.Select(
                association =>
                    $"{association.Domain} {association.Kind} {association.Value}"));
    }

    [Fact]
    public void ConstructionSnapshotsAssociationsAndSortsByProductOrder()
    {
        EcosystemDependencyAssociation[] mutable =
        [
            EcosystemDependencyAssociation.PackageIdFamily("Contoso"),
        ];
        var profile = new EcosystemDependencyRecognitionProfile(
            EcosystemPackCatalog.Discover(),
            [
                new(EcosystemPackIds.Blazor, mutable),
                new(
                    EcosystemPackIds.Runtime,
                    [EcosystemDependencyAssociation.ExactAssemblyName("mscorlib")]),
            ]);

        mutable[0] =
            EcosystemDependencyAssociation.PackageIdFamily("Replacement");

        Assert.Equal(EcosystemPackIds.Runtime, profile.Entries[0].Ecosystem.Id);
        Assert.Equal(EcosystemPackIds.Blazor, profile.Entries[1].Ecosystem.Id);
        Assert.Equal("Contoso", profile.Entries[1].Associations[0].Value);
    }

    [Fact]
    public void ConstructionRejectsUnknownDuplicateAndInvalidAssociations()
    {
        Assert.True(EcosystemPackId.TryCreate(
            "ecosystem.unknown",
            out EcosystemPackId? unknown));
        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyRecognitionProfile(
                EcosystemPackCatalog.Discover(),
                [
                    new(
                        unknown,
                        [EcosystemDependencyAssociation.PackageIdFamily("Contoso")]),
                ]));

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyRecognitionProfile(
                EcosystemPackCatalog.Discover(),
                [
                    new(
                        EcosystemPackIds.Blazor,
                        [EcosystemDependencyAssociation.PackageIdFamily("Azure")]),
                    new(
                        EcosystemPackIds.Blazor,
                        [EcosystemDependencyAssociation.AssemblyNameFamily("Azure")]),
                ]));

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyRecognitionProfile(
                EcosystemPackCatalog.Discover(),
                [
                    new(
                        EcosystemPackIds.Blazor,
                        [
                            EcosystemDependencyAssociation.PackageIdFamily(
                                "Azure"),
                            EcosystemDependencyAssociation.PackageIdFamily(
                                "azure"),
                        ]),
                ]));

        Assert.Throws<ArgumentException>(() =>
            EcosystemDependencyAssociation.PackageIdFamily("Azure..AI"));
        Assert.Throws<ArgumentException>(() =>
            EcosystemDependencyAssociation.ExactAssemblyName(" "));
    }
}
