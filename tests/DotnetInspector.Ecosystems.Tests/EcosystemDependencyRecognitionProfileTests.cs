namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemDependencyRecognitionProfileTests
{
    [Fact]
    public void ShippedProfileMatchesTheApprovedProductVocabulary()
    {
        EcosystemDependencyRecognitionProfile profile =
            EcosystemPackCatalog.DependencyRecognitionProfile;

        Assert.Equal(8, profile.Entries.Length);
        Assert.Equal(18, profile.PackageAssociationCount);
        Assert.Equal(23, profile.AssemblyAssociationCount);
        Assert.Equal(
            [
                EcosystemPackIds.Runtime,
                EcosystemPackIds.MicrosoftExtensions,
                EcosystemPackIds.AspNetCore,
                EcosystemPackIds.Aspire,
                EcosystemPackIds.AI,
                EcosystemPackIds.Azure,
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
                new(EcosystemPackIds.Azure, mutable),
                new(
                    EcosystemPackIds.Runtime,
                    [EcosystemDependencyAssociation.ExactAssemblyName("mscorlib")]),
            ]);

        mutable[0] =
            EcosystemDependencyAssociation.PackageIdFamily("Replacement");

        Assert.Equal(EcosystemPackIds.Runtime, profile.Entries[0].Ecosystem.Id);
        Assert.Equal(EcosystemPackIds.Azure, profile.Entries[1].Ecosystem.Id);
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
                        EcosystemPackIds.Azure,
                        [EcosystemDependencyAssociation.PackageIdFamily("Azure")]),
                    new(
                        EcosystemPackIds.Azure,
                        [EcosystemDependencyAssociation.AssemblyNameFamily("Azure")]),
                ]));

        Assert.Throws<ArgumentException>(() =>
            new EcosystemDependencyRecognitionProfile(
                EcosystemPackCatalog.Discover(),
                [
                    new(
                        EcosystemPackIds.Azure,
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
