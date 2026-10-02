using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemHierarchyTests
{
    [Fact]
    public void ShippedPacksFormTheDocumentedHierarchy()
    {
        Dictionary<string, string?> parents = EcosystemPackCatalog.Discover()
            .ToDictionary(pack => pack.Id.Value, pack => pack.DependsOn?.Value);

        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["ecosystem.runtime"] = null,
                ["ecosystem.microsoft-extensions"] = "ecosystem.runtime",
                ["ecosystem.aspnetcore"] = "ecosystem.microsoft-extensions",
                ["ecosystem.aspire"] = "ecosystem.aspnetcore",
                ["ecosystem.ai"] = "ecosystem.microsoft-extensions",
                ["ecosystem.blazor"] = "ecosystem.aspnetcore",
                ["ecosystem.maui"] = "ecosystem.microsoft-extensions",
            },
            parents);
        Assert.Equal(
            ["ecosystem.runtime", "ecosystem.microsoft-extensions", "ecosystem.aspnetcore", "ecosystem.aspire"],
            Lookup(EcosystemPackIds.Aspire).Lineage.Select(id => id.Value));
        Assert.Equal(
            ["ecosystem.runtime"],
            Lookup(EcosystemPackIds.Runtime).Lineage.Select(id => id.Value));
    }

    [Fact]
    public void PlatformPlanIsAspNetCoreLineage()
    {
        Assert.Equal(
            Lookup(EcosystemPackIds.AspNetCore).Lineage.Select(id => id.Value),
            Ids(EcosystemPackCatalog.CreatePlatformWorkspacePlan()));
    }

    [Fact]
    public void AllKnownPlanRegistersEveryPackAfterItsParent()
    {
        string[] ids = Ids(EcosystemPackCatalog.CreateWorkspacePlan());

        Assert.Equal(EcosystemPackCatalog.Discover().Length, ids.Length);
        foreach (EcosystemPackDescriptor pack in EcosystemPackCatalog.Discover())
        {
            if (pack.DependsOn is { } parent)
                Assert.True(Array.IndexOf(ids, parent.Value) < Array.IndexOf(ids, pack.Id.Value));
        }
    }

    [Theory]
    [InlineData("aspire,ai", "runtime,microsoft-extensions,aspnetcore,aspire,ai")]
    [InlineData("ai,aspire", "runtime,microsoft-extensions,ai,aspnetcore,aspire")]
    [InlineData("aspire,aspnetcore", "runtime,microsoft-extensions,aspnetcore,aspire")]
    public void SelectedLineagesAppendRootFirstInCallerOrder(string selection, string expected)
    {
        Assert.Equal(
            Qualified(expected),
            ProductEcosystemPacks.Registry
                .ExpandLineages(Qualified(selection).Select(EcosystemPackId.Create))
                .Select(id => id.Value));
    }

    [Fact]
    public void LineageSelectionRejectsDuplicateAndUnknownPacks()
    {
        EcosystemPackRegistry registry = ProductEcosystemPacks.Registry;
        Assert.Throws<ArgumentException>(() =>
            registry.ExpandLineages([EcosystemPackIds.Aspire, EcosystemPackIds.Aspire]));
        Assert.Throws<ArgumentException>(() =>
            registry.ExpandLineages([EcosystemPackId.Create("ecosystem.unknown")]));
    }

    [Fact]
    public void ParentMayBeRegisteredAfterItsChild()
    {
        var registry = new EcosystemPackRegistry(
        [
            Pack("ecosystem.child", 100, dependsOn: "ecosystem.parent"),
            Pack("ecosystem.parent", 200),
        ]);

        Assert.Equal(
            ["ecosystem.parent", "ecosystem.child"],
            registry.ExpandLineages([EcosystemPackId.Create("ecosystem.child")])
                .Select(id => id.Value));
    }

    [Fact]
    public void InvalidDependsOnFailsRegistryConstruction()
    {
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry(
            [Pack("ecosystem.child", 100, dependsOn: "ecosystem.missing")]));
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry(
            [Pack("ecosystem.self", 100, dependsOn: "ecosystem.self")]));
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry(
        [
            Pack("ecosystem.a", 100, dependsOn: "ecosystem.b"),
            Pack("ecosystem.b", 200, dependsOn: "ecosystem.a"),
        ]));
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry(
        [
            Pack("ecosystem.parent", 100, projected: false),
            Pack("ecosystem.child", 200, dependsOn: "ecosystem.parent"),
        ]));
    }

    private static EcosystemPackDescriptor Lookup(EcosystemPackId id) =>
        Assert.IsType<EcosystemPackLookupResult.Known>(EcosystemPackCatalog.Lookup(id)).Descriptor;

    private static string[] Ids(WorkspacePlan plan) =>
    [
        .. plan.Registrations.Select(registration =>
            Assert.IsType<WorkspaceRegistration.Ecosystem>(registration).Declaration.Id.Value),
    ];

    private static string[] Qualified(string shortNames) =>
        [.. shortNames.Split(',').Select(name => "ecosystem." + name)];

    private static EcosystemPackRegistration Pack(
        string id,
        int order,
        string? dependsOn = null,
        bool projected = true) =>
        new(
            EcosystemPackId.Create(id),
            "Test pack",
            "Hierarchy fixture.",
            order,
            projected ? null : PackageSetIds.Aspire,
            [])
        {
            DependsOn = dependsOn is null ? null : EcosystemPackId.Create(dependsOn),
            WorkspaceRegistration = projected
                ? new(
                    WorkspaceEcosystemRegistrationId.Create(id),
                    [],
                    [],
                    [new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                        new PackagePrefixDeclaration("Example."))])
                : null,
        };
}
