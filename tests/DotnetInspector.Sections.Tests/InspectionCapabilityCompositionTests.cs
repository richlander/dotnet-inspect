using DotnetInspector.Queries;
using QuerySpace;

namespace DotnetInspector.Sections.Tests;

public sealed class InspectionCapabilityCompositionTests
{
    [Fact]
    public void PackageQueryProductModuleComposesWithoutExecutingRoute()
    {
        int executions = 0;
        InspectionDocumentRegistration<string> document =
            Document("test/document");
        var route =
            new InspectionRouteRegistration<object, string>(
                new(
                    "test/route",
                    "Test route",
                    "A test route."),
                document,
                PackageQuery.QuerySpace,
                (_, _) =>
                {
                    executions++;
                    return ValueTask.FromResult(
                        new InspectionEnvelope<string>(
                            "executed",
                            new InspectionShare.NonProjectable(
                                "test/share",
                                "Test content is not shareable.")));
                });
        var module =
            new InspectionCapabilityModule(
                "test/product",
                documents: [document],
                routes: [route]);

        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create([module]);

        Assert.Equal(0, executions);
        Assert.Same(document, Assert.Single(catalog.Documents));
        Assert.Same(route, Assert.Single(catalog.Routes));
    }

    [Fact]
    public void PackageQueryProductModuleReportsBothHostAdoptionGaps()
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [PackageQueryCapability.ProductModule]);

        Assert.Same(
            PackageQueryCapability.Document,
            Assert.Single(catalog.Documents));
        Assert.Same(
            PackageQueryCapability.Route,
            Assert.Single(catalog.Routes));
        Assert.Same(
            PackageQuery.QuerySpace,
            PackageQueryCapability.Route.QuerySpace);
        Assert.Equal(
            [
                InspectionConsumerKind.Cli,
                InspectionConsumerKind.Browser,
            ],
            catalog.AdoptionGaps.Select(static gap =>
                gap.ConsumerKind));
    }

    [Fact]
    public void PackageQueryRouteProjectsExecutableLibraryLiteralFacet()
    {
        QuerySpace.Composition.QuerySpaceOperationTermDescriptor facet =
            Assert.Single(
                PackageQueryCapability.Route.QuerySpace
                    .Descriptor.Operation.Terms,
                static term =>
                    term.Key == PackageQuery.LibraryLiteralTermKey);

        Assert.Equal(
            "package-query.term.library-literal",
            facet.Identity);
        Assert.Equal("decoded UTF-16 text", facet.ValueKind);
        Assert.Equal(
            [PortableQueryOperator.Equal],
            facet.Operators);
        Assert.Contains(
            facet.Effects,
            static effect =>
                effect.Kind
                    == QuerySpace.Operations
                        .QueryOperationEffectKind.AcquisitionTier
                && effect.Identity == "package-content");
    }

    [Fact]
    public void CatalogRejectsDuplicateMissingAndOutOfProfileRegistrations()
    {
        InspectionDocumentRegistration<string> first =
            Document("test/document");
        InspectionDocumentRegistration<string> duplicate =
            Document("test/document");
        Assert.Throws<ArgumentException>(() =>
            InspectionCapabilityCatalog.Create(
                [
                    new("test/one", documents: [first]),
                    new("test/two", documents: [duplicate]),
                ]));

        InspectionDocumentRegistration<string> incompatible =
            new(
                new(
                    "test/incompatible",
                    "Incompatible document",
                    "A document with another result contract.",
                    "test/another-result/v1"));
        Assert.Throws<ArgumentException>(() =>
            new InspectionRouteRegistration<object, string>(
                new(
                    "test/incompatible-route",
                    "Incompatible route",
                    "A route with an incompatible document contract."),
                incompatible,
                PackageQuery.QuerySpace,
                static (_, _) =>
                    ValueTask.FromResult(
                        new InspectionEnvelope<string>(
                            "content",
                            new InspectionShare.NonProjectable(
                                "test/share",
                                "Test content is not shareable.")))));

        var unregisteredRoute =
            new InspectionRouteRegistration<object, string>(
                new(
                    "test/route",
                    "Test route",
                    "A test route."),
                duplicate,
                PackageQuery.QuerySpace,
                static (_, _) =>
                    ValueTask.FromResult(
                        new InspectionEnvelope<string>(
                            "content",
                            new InspectionShare.NonProjectable(
                                "test/share",
                                "Test content is not shareable."))));
        Assert.Throws<ArgumentException>(() =>
            InspectionCapabilityCatalog.Create(
                [
                    new(
                        "test/product",
                        documents: [first],
                        routes: [unregisteredRoute]),
                ]));

        var invalidBinding =
            new InspectionConsumerBinding<object, string>(
                new(
                    "test/binding",
                    InspectionConsumerKind.Cli,
                    "Test CLI",
                    "test"),
                unregisteredRoute,
                ["query.term.not-registered"]);
        Assert.Throws<ArgumentException>(() =>
            InspectionCapabilityCatalog.Create(
                [
                    new(
                        "test/product",
                        documents: [duplicate],
                        routes: [unregisteredRoute]),
                    new(
                        "test/consumer",
                        bindings: [invalidBinding]),
                ]));
    }

    [Fact]
    public void CatalogConstructionIsDeterministicAcrossModuleOrder()
    {
        var cli =
            new InspectionConsumerBinding<
                PackageQueryInspectionRequest,
                PackageQueryDocument>(
                new(
                    "test/cli",
                    InspectionConsumerKind.Cli,
                    "Test CLI",
                    "package query"),
                PackageQueryCapability.Route,
                PackageQuery.InspectionTermBindingIdentities);
        var browser =
            new InspectionConsumerBinding<
                PackageQueryInspectionRequest,
                PackageQueryDocument>(
                new(
                    "test/browser",
                    InspectionConsumerKind.Browser,
                    "Test Browser",
                    "Package Query"),
                PackageQueryCapability.Route,
                PackageQuery.InspectionTermBindingIdentities);
        var cliModule =
            new InspectionCapabilityModule(
                "test/cli-module",
                bindings: [cli]);
        var browserModule =
            new InspectionCapabilityModule(
                "test/browser-module",
                bindings: [browser]);

        InspectionCapabilityCatalog first =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    cliModule,
                    browserModule,
                ]);
        InspectionCapabilityCatalog second =
            InspectionCapabilityCatalog.Create(
                [
                    browserModule,
                    cliModule,
                    PackageQueryCapability.ProductModule,
                ]);

        Assert.Equal(
            first.Modules.Select(static module => module.Identity),
            second.Modules.Select(static module => module.Identity));
        Assert.Equal(
            first.Bindings.Select(static binding =>
                binding.Descriptor.Identity),
            second.Bindings.Select(static binding =>
                binding.Descriptor.Identity));
        Assert.Empty(first.AdoptionGaps);
        Assert.Empty(second.AdoptionGaps);
    }

    [Fact]
    public void PackageQueryCapabilityExplanationUsesCanonicalFacetPath()
    {
        InspectionCapabilityCatalog capabilityCatalog =
            InspectionCapabilityCatalog.Create(
                [PackageQueryCapability.ProductModule]);
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateCapabilities(
                capabilityCatalog,
                PackageQueryCapabilityResourcePaths.Create(
                    capabilityCatalog));
        ResourcePath path =
            PackageQueryCapabilityResourcePaths.QueryFacet(
                PackageQuery.LibraryLiteralTermKey);

        var resolved =
            Assert.IsType<ResourcePathResolution.Resolved>(
                explanation.Resolve(path.Value));
        InspectionEnvelope<ResourceExplanationDocument> envelope =
            explanation.Explain(
                resolved,
                new(
                    depth: 1,
                    resourceLimit: 32,
                    relationshipLimit: 64));
        ResourceExplanationResource root =
            Assert.Single(
                envelope.Content.Resources,
                resource => resource.Path == path);
        var details =
            Assert.IsType<
                ResourceExplanationDetail.QueryFacetDetails>(
                root.Details);

        Assert.Equal(PackageQuery.LibraryLiteralTermKey, details.Key);
        Assert.Equal("decoded UTF-16 text", details.ValueKind);
        Assert.Equal(["eq"], details.Operators);
        Assert.Contains(
            envelope.Content.Relationships,
            relationship =>
                relationship.Source == root.Identity
                && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.Route
                && relationship.TargetPath
                    == PackageQueryCapabilityResourcePaths.Route);
    }

    private static InspectionDocumentRegistration<string> Document(
        string identity) =>
        new(
            new(
                identity,
                "Test document",
                "A test document.",
                PackageQuery.ResultContractIdentity));
}
