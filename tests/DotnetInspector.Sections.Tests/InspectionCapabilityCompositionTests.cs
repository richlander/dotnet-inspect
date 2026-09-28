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

    [Fact]
    public void ConsumerBindingExplanationPreservesExposedFacetSubset()
    {
        ResourceExplanationCatalog readme =
            CreateExplanationForBinding(
                PackageQuery.TermBindingIdentity(
                    PackageQuery.ReadmeTermKey));
        ResourceExplanationCatalog literal =
            CreateExplanationForBinding(
                PackageQuery.TermBindingIdentity(
                    PackageQuery.LibraryLiteralTermKey));
        ResourceExplanationIdentity.Capability bindingIdentity =
            CapabilityIdentity(
                InspectionCapabilityResourceKind.ConsumerBinding,
                "test/binding");

        Assert.Contains(
            readme.Relationships,
            relationship =>
                relationship.Source == bindingIdentity
                && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.Exposes
                && relationship.TargetPath
                    == PackageQueryCapabilityResourcePaths.QueryFacet(
                        PackageQuery.ReadmeTermKey));
        Assert.DoesNotContain(
            readme.Relationships,
            relationship =>
                relationship.Source == bindingIdentity
                && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.Exposes
                && relationship.TargetPath
                    == PackageQueryCapabilityResourcePaths.QueryFacet(
                        PackageQuery.LibraryLiteralTermKey));
        Assert.Contains(
            literal.Relationships,
            relationship =>
                relationship.Source == bindingIdentity
                && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.Exposes
                && relationship.TargetPath
                    == PackageQueryCapabilityResourcePaths.QueryFacet(
                        PackageQuery.LibraryLiteralTermKey));
    }

    [Fact]
    public void SharedQuerySpaceProjectsMembershipOncePerFacet()
    {
        var alternateRoute =
            new InspectionRouteRegistration<
                PackageQueryInspectionRequest,
                PackageQueryDocument>(
                new(
                    "package-query/alternate",
                    "Alternate Package Query",
                    "A second route over the Package Query surface."),
                PackageQueryCapability.Document,
                PackageQuery.QuerySpace,
                PackageQueryCapability.Route.ExecuteAsync,
                PackageQueryCapability.Route.QueryTermRelationships);
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    new(
                        "test/shared-query-space",
                        documents: [PackageQueryCapability.Document],
                        routes:
                        [
                            PackageQueryCapability.Route,
                            alternateRoute,
                        ]),
                ]);

        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                SharedQuerySpacePaths(catalog, alternateRoute));
        ResourceExplanationIdentity.Capability querySpaceIdentity =
            CapabilityIdentity(
                InspectionCapabilityResourceKind.QuerySpace,
                PackageQuery.QuerySpace.Descriptor.Identity);
        ResourceExplanationIdentity.Capability literalIdentity =
            CapabilityIdentity(
                InspectionCapabilityResourceKind.QueryFacet,
                PackageQuery.TermBindingIdentity(
                    PackageQuery.LibraryLiteralTermKey),
                PackageQuery.QuerySpace.Descriptor.Identity);

        Assert.Equal(
            1,
            explanation.Relationships.Count(relationship =>
                relationship.Source == querySpaceIdentity
                && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.QueryFacet
                && relationship.Target == literalIdentity));
        Assert.Equal(
            2,
            explanation.Relationships.Count(relationship =>
                relationship.Source == literalIdentity
                && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.Route));
        Assert.Equal(
            1,
            explanation.Relationships.Count(relationship =>
                relationship.Source == literalIdentity
                && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.RequiredContext));
    }

    private static ResourceExplanationCatalog CreateExplanationForBinding(
        string exposedTerm)
    {
        InspectionConsumerBinding<
            PackageQueryInspectionRequest,
            PackageQueryDocument> binding = Binding(exposedTerm);
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    new("test/consumer", bindings: [binding]),
                ]);
        return ResourceExplanationCatalog.CreateCapabilities(
            catalog,
            PackageQueryCapabilityResourcePaths.Create(catalog));
    }

    private static InspectionConsumerBinding<
        PackageQueryInspectionRequest,
        PackageQueryDocument> Binding(string exposedTerm) =>
        new(
            new(
                "test/binding",
                InspectionConsumerKind.Cli,
                "Test CLI",
                "package query"),
            PackageQueryCapability.Route,
            [exposedTerm]);

    private static IEnumerable<
        InspectionCapabilityResourcePathRegistration> SharedQuerySpacePaths(
        InspectionCapabilityCatalog catalog,
        InspectionRouteRegistration alternateRoute)
    {
        yield return new(
            new(
                InspectionCapabilityResourceKind.Document,
                PackageQueryCapability.Document.Descriptor.Identity),
            PackageQueryCapabilityResourcePaths.Document);
        foreach (InspectionRouteRegistration route in catalog.Routes)
        {
            yield return new(
                new(
                    InspectionCapabilityResourceKind.Route,
                    route.Descriptor.Identity),
                ReferenceEquals(route, alternateRoute)
                    ? PackageQueryCapabilityResourcePaths.Document.Append(
                        "routes",
                        "alternate")
                    : PackageQueryCapabilityResourcePaths.Route);
        }
        yield return new(
            new(
                InspectionCapabilityResourceKind.QuerySpace,
                PackageQuery.QuerySpace.Descriptor.Identity),
            PackageQueryCapabilityResourcePaths.QuerySpace);
        foreach (QuerySpace.Composition.QuerySpaceOperationTermDescriptor term
                 in PackageQuery.QuerySpace.Descriptor.Operation.Terms)
        {
            yield return new(
                new(
                    InspectionCapabilityResourceKind.QueryFacet,
                    term.Identity,
                    PackageQuery.QuerySpace.Descriptor.Identity),
                PackageQueryCapabilityResourcePaths.QueryFacet(term.Key));
        }
    }

    private static ResourceExplanationIdentity.Capability
        CapabilityIdentity(
            InspectionCapabilityResourceKind kind,
            string identity,
            string? parentIdentity = null) =>
        new(new(kind, identity, parentIdentity));

    private static InspectionDocumentRegistration<string> Document(
        string identity) =>
        new(
            new(
                identity,
                "Test document",
                "A test document.",
                PackageQuery.ResultContractIdentity));
}
