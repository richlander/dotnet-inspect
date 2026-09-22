using DotnetInspector.Queries.Definitions;
using QuerySpace;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionDefinitionV3Tests
{
    private const string RegistrationOnlyWorkspaceJson =
        """
        {
          "schemaVersion": 3,
          "kind": "workspace",
          "id": "serializer-discovery",
          "contexts": [],
          "registrations": [
            {
              "kind": "packagePrefix",
              "prefix": "Microsoft.Extensions."
            }
          ]
        }
        """;

    private const string CompositeRegistrationWorkspaceJson =
        """
        {
          "schemaVersion": 3,
          "kind": "workspace",
          "id": "portable-registrations",
          "contexts": [],
          "registrations": [
            {
              "kind": "exactLibrary",
              "coordinate": {
                "kind": "package",
                "id": "system.text.json",
                "version": "10.0.0",
                "library": {
                  "name": "System.Text.Json",
                  "version": "10.0.0.0",
                  "culture": null,
                  "publicKeyToken": "cc7b13ffcd2ddd51"
                }
              }
            },
            {
              "kind": "exactLibrary",
              "coordinate": {
                "kind": "platform",
                "family": "DotNetRuntime",
                "library": {
                  "name": "System.Runtime",
                  "version": "11.0.0.0",
                  "culture": null,
                  "publicKeyToken": "b03f5f7f11d50a3a"
                }
              }
            },
            {
              "kind": "ecosystem",
              "declaration": {
                "id": "ecosystem.platform",
                "namespaceRoots": ["System"],
                "corePackages": ["system.runtime"],
                "populations": [
                  {
                    "kind": "exactLibrary",
                    "coordinate": {
                      "kind": "package",
                      "id": "system.text.json",
                      "version": "10.0.0",
                      "library": {
                        "name": "System.Text.Json",
                        "version": "10.0.0.0",
                        "culture": null,
                        "publicKeyToken": "cc7b13ffcd2ddd51"
                      }
                    }
                  },
                  {
                    "kind": "platform",
                    "family": "AspNetCore"
                  },
                  {
                    "kind": "packagePrefix",
                    "prefix": "Microsoft.Extensions."
                  }
                ]
              }
            }
          ]
        }
        """;

    [Fact]
    public void Workspace_RegistrationOnly_RoundTripsPortableJson()
    {
        var workspace = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(RegistrationOnlyWorkspaceJson));

        Assert.Equal(InspectionDefinitionSchema.Version3, workspace.SchemaVersion);
        Assert.Empty(workspace.Contexts);
        var prefix = Assert.IsType<WorkspaceRegistration.PackagePrefix>(
            Assert.Single(workspace.Registrations));
        Assert.Equal("Microsoft.Extensions.", prefix.Prefix.Prefix);

        string canonical = InspectionDefinitionJson.Serialize(workspace);
        var roundTripped = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(canonical));
        Assert.Empty(roundTripped.Contexts);
        Assert.IsType<WorkspaceRegistration.PackagePrefix>(
            Assert.Single(roundTripped.Registrations));
        Assert.True(
            canonical.IndexOf("\"contexts\"", StringComparison.Ordinal)
                < canonical.IndexOf(
                    "\"registrations\"",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Workspace_AllPortableRegistrationArms_RoundTripJson()
    {
        var workspace = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(
                CompositeRegistrationWorkspaceJson));

        Assert.Collection(
            workspace.Registrations,
            registration => Assert.IsType<
                ExactLibrarySourceCoordinate.Package>(
                    Assert.IsType<WorkspaceRegistration.ExactLibrary>(
                        registration).Coordinate),
            registration => Assert.IsType<
                ExactLibrarySourceCoordinate.Platform>(
                    Assert.IsType<WorkspaceRegistration.ExactLibrary>(
                        registration).Coordinate),
            registration =>
            {
                WorkspaceEcosystemRegistrationDeclaration ecosystem =
                    Assert.IsType<WorkspaceRegistration.Ecosystem>(
                        registration).Declaration;
                Assert.Equal(["System"], ecosystem.NamespaceRoots);
                Assert.Equal(
                    ["system.runtime"],
                    ecosystem.CorePackages.Select(package =>
                        package.PackageId));
                Assert.Collection(
                    ecosystem.Populations,
                    population => Assert.IsType<
                        WorkspaceEcosystemPopulationDeclaration.ExactLibrary>(
                            population),
                    population => Assert.IsType<
                        WorkspaceEcosystemPopulationDeclaration.Platform>(
                            population),
                    population => Assert.IsType<
                        WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
                            population));
            });

        string canonical = InspectionDefinitionJson.Serialize(workspace);
        var roundTripped = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(canonical));
        Assert.Equal(3, roundTripped.Registrations.Count);
    }

    [Theory]
    [InlineData(
        """{"schemaVersion":2,"kind":"workspace","id":"w","contexts":[{"name":"g","members":[{"kind":"package","id":"P","version":"1.0.0"}]}],"registrations":[{"kind":"packagePrefix","prefix":"P."}]}""")]
    [InlineData(
        """{"schemaVersion":3,"kind":"workspace","id":"w","contexts":[],"registrations":[{"kind":"packagePrefix","prefix":"P."},{"kind":"packagePrefix","prefix":"P."}]}""")]
    public void Workspace_RejectsInvalidVersion3RegistrationComposition(
        string json)
    {
        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Parse(json));
    }

    [Fact]
    public void Workspace_EmptyVersion3_IsAdmittedForPeerComposition()
    {
        var workspace = Assert.IsType<WorkspaceDefinition>(
            InspectionDefinitionJson.Parse(
                """{"schemaVersion":3,"kind":"workspace","id":"w","contexts":[],"registrations":[]}"""));

        Assert.Empty(workspace.Contexts);
        Assert.Empty(workspace.Registrations);
    }

    [Fact]
    public void Query_CanonicalizesReadablePayload()
    {
        var query = Assert.IsType<CommittedQueryDefinition>(
            InspectionDefinitionJson.Parse(
                """
                {
                  "schemaVersion": 3,
                  "kind": "query",
                  "id": "extensions-with-di",
                  "queryId": "package-query/v1",
                  "payload": {
                    "b": [["candidates", 200]],
                    "t": [
                      ["prerelease", "eq", "stable"],
                      ["prefix", "eq", "Microsoft.Extensions."],
                      ["depends", "eq", "Microsoft.Extensions.DependencyInjection"]
                    ]
                  }
                }
                """));

        Assert.Equal(
            """{"t":[["depends","eq","Microsoft.Extensions.DependencyInjection"],["prefix","eq","Microsoft.Extensions."],["prerelease","eq","stable"]],"b":[["candidates",200]]}""",
            query.Payload);
        var roundTripped = Assert.IsType<CommittedQueryDefinition>(
            InspectionDefinitionJson.Parse(
                InspectionDefinitionJson.Serialize(query)));
        Assert.Equal(query.Identity, roundTripped.Identity);
    }

    [Fact]
    public void Query_RejectsBlankProgrammaticVocabulary()
    {
        PortableQueryIdentity identity =
            PortableQueryIdentity.FromCanonicalPayload(
                " ",
                "{}",
                TestContext.Current.CancellationToken);

        Assert.Throws<ArgumentException>(
            () => new CommittedQueryDefinition(
                InspectionDefinitionSchema.Version3,
                "query",
                identity));
    }

    [Fact]
    public void Workspace_RejectsNonPortableRegistrationCapabilities()
    {
        ManagedMetadataIdentity.Assembly library = Library("Local.Library");
        WorkspaceDefinition local = Workspace(
            new WorkspaceRegistration.ExactLibrary(
                new ExactLibrarySourceCoordinate.Local(library)));
        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Serialize(local));

        WorkspaceDefinition scanner = Workspace(
            new WorkspaceRegistration.Ecosystem(
                new WorkspaceEcosystemRegistrationDeclaration(
                    WorkspaceEcosystemRegistrationId.Create(
                        "ecosystem.scanner"),
                    ["Scanner"],
                    [],
                    [],
                    EcosystemIntegrationScanner.AspireBinding)));
        Assert.Throws<InspectionDefinitionException>(
            () => InspectionDefinitionJson.Serialize(scanner));
    }

    [Fact]
    public void Registry_PreparesRegistrationOnlyVersion3Plan()
    {
        WorkspaceDefinition workspace = Workspace(
            new WorkspaceRegistration.PackagePrefix(
                new PackagePrefixDeclaration("Microsoft.Extensions.")));
        var navigation = new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version3,
            "navigation",
            [],
            focus: null);
        var view = new CommittedViewDefinition(
            InspectionDefinitionSchema.Version3,
            "view",
            [
                new CommittedViewStateDefinition(
                    navigation: null,
                    subject: new PortableSubjectRequest.Workspace()),
            ]);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version3,
            "scenario",
            workspace: workspace.Id,
            context: null,
            view: view.Id,
            navigation: navigation.Id);
        var registry = new InspectionDefinitionRegistry();
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(view);
        registry.Add(scenario);

        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version3>(
                registry.PrepareScenario(scenario.Id));

        Assert.Same(workspace, prepared.Definitions.Workspace);
        WorkspacePlan plan =
            InspectionDefinitionRegistry.CreateWorkspacePlan(workspace);
        Assert.Empty(plan.Contexts);
        Assert.Single(plan.Registrations);
    }

    private static WorkspaceDefinition Workspace(
        WorkspaceRegistration registration) =>
        new(
            InspectionDefinitionSchema.Version3,
            "workspace",
            [],
            registrations: [registration]);

    private static ManagedMetadataIdentity.Assembly Library(string name) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
}
