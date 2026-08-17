using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionGraphIntegrationsQueryTests
{
    [Fact]
    public void Execute_DefaultsToWorkspaceInducedSetWithoutSeeds()
    {
        using var fixture = IntegrationFixture.Create();

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Equal(
            InspectionGraphMode.InducedSet,
            document.ModeRequest.Mode);
        Assert.Equal(
            InspectionGraphInducedSetRule.WorkspaceParticipants,
            document.ModeRequest.InducedSetRule);
        Assert.Empty(document.ModeRequest.Seeds);
        Assert.Empty(document.Seeds);
        Assert.NotEmpty(document.Groups);
    }

    [Fact]
    public void Execute_BindsTypeSeedToExactNode()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            induced,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");
        InspectionGraphModeRequest request =
            InspectionGraphModeRequest.SingleSeed(hub);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                request);

        InspectionGraphSeed seed = Assert.Single(document.Seeds);
        Assert.Same(request, document.ModeRequest);
        Assert.Equal(InspectionGraphSeedRole.Primary, seed.Role);
        Assert.Equal(hub, seed.Subject);
        Assert.Equal(InspectionGraphTargetKind.Node, seed.Target.Kind);
        Assert.Equal(
            hub,
            document.Nodes[seed.Target.Id].Subject);
        Assert.All(
            document.Groups,
            group => Assert.NotEqual(hub, group.Subject));
    }

    [Fact]
    public void Execute_BindsPackageSeedToDetailedLensGroup()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.PackageSubject package =
            PackageSubject(induced, "microsoft.extensions.ai.openai");

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                InspectionGraphModeRequest.SingleSeed(package));

        InspectionGraphSeed seed = Assert.Single(document.Seeds);
        Assert.Equal(package, seed.Subject);
        Assert.Equal(InspectionGraphTargetKind.Group, seed.Target.Kind);
        Assert.Equal(
            package,
            document.Groups[seed.Target.Id].Subject);
        Assert.DoesNotContain(
            document.Nodes,
            node => node.Subject == package);
    }

    [Fact]
    public void Execute_BindsAssemblySeedToExactNode()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.AssemblySubject assembly =
            Assert.IsType<InspectionGraphSubject.AssemblySubject>(
                Assert.Single(
                    induced.Nodes,
                    node =>
                        node.Subject
                            is InspectionGraphSubject.AssemblySubject
                        && AssemblyName(node.Subject)
                            == "Azure.AI.OpenAI")
                    .Subject);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                InspectionGraphModeRequest.SingleSeed(assembly));

        InspectionGraphSeed seed = Assert.Single(document.Seeds);
        Assert.Equal(assembly, seed.Subject);
        Assert.Equal(InspectionGraphTargetKind.Node, seed.Target.Kind);
        Assert.Equal(
            assembly,
            document.Nodes[seed.Target.Id].Subject);
    }

    [Fact]
    public void Execute_BindsPeerSeedsWithoutChoosingPrimary()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            induced,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");
        InspectionGraphSubject.PackageSubject openAi =
            PackageSubject(induced, "microsoft.extensions.ai.openai");
        InspectionGraphSubject.PackageSubject bedrock =
            PackageSubject(induced, "awssdk.extensions.bedrock.meai");
        InspectionGraphSubject[] peers = [hub, openAi, bedrock];

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                InspectionGraphModeRequest.PeerSeeds(peers));

        Assert.Equal(
            InspectionGraphMode.PeerSeeds,
            document.ModeRequest.Mode);
        Assert.Equal(
            peers,
            document.Seeds.Select(static seed => seed.Subject));
        Assert.All(
            document.Seeds,
            seed => Assert.Equal(
                InspectionGraphSeedRole.Peer,
                seed.Role));
        Assert.DoesNotContain(
            document.Seeds,
            seed => seed.Role == InspectionGraphSeedRole.Primary);
        Assert.Equal(
            [
                InspectionGraphTargetKind.Node,
                InspectionGraphTargetKind.Group,
                InspectionGraphTargetKind.Group,
            ],
            document.Seeds.Select(static seed => seed.Target.Kind));
    }

    [Fact]
    public void Execute_RejectsSeedOutsideWorkspaceWithGuidance()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphSubject missing =
            InspectionGraphSubject.ForRealizedPackage(
                new RealizedMemberCoordinate.Package(
                    "missing.package",
                    "1.0.0",
                    "feed",
                    "net11.0",
                    null));

        InspectionQueryException exception = Assert.Throws<
            InspectionQueryException>(
                () => InspectionGraphIntegrationsQuery.Execute(
                    fixture.Context,
                    InspectionGraphModeRequest.SingleSeed(missing)));

        Assert.Contains("not present", exception.Message);
        Assert.Contains("workspace scope", exception.Message);
    }

    [Fact]
    public void Execute_BoundsMixedRelationshipNeighborhoodByDepth()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            induced,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");
        InspectionGraphRelationshipDescriptor[] relationships =
        [
            InspectionGraphIntegrationsCatalog.IntegrationObserved,
            InspectionGraphIntegrationsCatalog.Extension,
        ];
        InspectionGraphNeighborhoodRequest depthOne =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                hub,
                relationships,
                InspectionGraphTraversalDirection.Both,
                maxDepth: 1);
        InspectionGraphNeighborhoodRequest depthTwo =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                hub,
                relationships,
                InspectionGraphTraversalDirection.Both,
                maxDepth: 2);

        InspectionGraphDocument one =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                depthOne);
        InspectionGraphDocument two =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                depthTwo);

        Assert.Same(depthOne, one.NeighborhoodRequest);
        Assert.Same(depthOne.ModeRequest, one.ModeRequest);
        Assert.Equal(2, one.Edges.Length);
        Assert.All(
            one.Edges,
            edge => Assert.Same(
                InspectionGraphIntegrationsCatalog.IntegrationObserved,
                edge.Relationship));
        Assert.Equal(4, two.Edges.Length);
        Assert.Equal(
            2,
            two.Edges.Count(edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog.Extension));
        Assert.Equal(
            2,
            two.Edges.Count(edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationObserved));
        Assert.All(
            one.Edges,
            edge => Assert.Equal(
                hub,
                one.Nodes[edge.ToNodeId].Subject));
        Assert.Equal(one.Edges.Length, one.Occurrences.Length);
        Assert.Equal(
            induced.Occurrences
                .Where(occurrence =>
                    occurrence.Relationship
                        == InspectionGraphIntegrationsCatalog
                            .IntegrationObserved)
                .Select(occurrence =>
                    occurrence.Relationship.OccurrenceIdentity.Project(
                        occurrence)),
            one.Occurrences.Select(occurrence =>
                occurrence.Relationship.OccurrenceIdentity.Project(
                    occurrence)));
        Assert.Equal(
            induced.Occurrences
                .Where(occurrence =>
                    occurrence.Relationship
                        == InspectionGraphIntegrationsCatalog
                            .IntegrationObserved)
                .Select(occurrence =>
                    (
                        occurrence.SourceSubject,
                        occurrence.TargetSubject)),
            one.Occurrences.Select(occurrence =>
                (
                    occurrence.SourceSubject,
                    occurrence.TargetSubject)));
        var bound = Assert.IsType<
            InspectionGraphNeighborhoodDepthBoundEvidence>(
                Assert.Single(
                    one.Limits,
                    limit =>
                        limit.Descriptor
                            == InspectionGraphNeighborhoodCatalog
                                .DepthBound)
                    .Evidence);
        Assert.Equal(1, bound.MaxDepth);
    }

    [Fact]
    public void Execute_PackageSeedExpandsThroughOwnedSourceSubjects()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.PackageSubject openAi =
            PackageSubject(
                induced,
                "microsoft.extensions.ai.openai");
        InspectionGraphNeighborhoodRequest request =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                openAi,
                [
                    InspectionGraphIntegrationsCatalog
                        .IntegrationObserved,
                ],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                request);

        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal(
            "microsoft.extensions.ai.openai",
            PackageId(
                document,
                document.Nodes[edge.FromNodeId]));
        Assert.Equal(
            "Microsoft.Extensions.AI.IChatClient",
            TypeName(document.Nodes[edge.ToNodeId].Subject));
        InspectionGraphSeed seed = Assert.Single(document.Seeds);
        Assert.Equal(InspectionGraphTargetKind.Group, seed.Target.Kind);
        Assert.Equal(
            openAi,
            document.Groups[seed.Target.Id].Subject);
    }

    [Fact]
    public void Execute_OpportunitySourceTypeUsesOccurrenceAdmission()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphOccurrence inducedOpportunity = Assert.Single(
            induced.Occurrences,
            occurrence =>
                occurrence.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationOpportunity);
        InspectionGraphSubject.TypeSubject source =
            Assert.IsType<InspectionGraphSubject.TypeSubject>(
                inducedOpportunity.SourceSubject);
        InspectionGraphNeighborhoodRequest request =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                source,
                [
                    InspectionGraphIntegrationsCatalog
                        .IntegrationOpportunity,
                ],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                request);

        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal(
            "Azure.AI.OpenAI",
            AssemblyName(document.Nodes[edge.FromNodeId].Subject));
        Assert.NotEqual(
            source,
            document.Nodes[edge.FromNodeId].Subject);
        InspectionGraphOccurrence occurrence =
            document.Occurrences[
                Assert.Single(edge.OccurrenceIds)];
        Assert.Equal(source, occurrence.SourceSubject);
        Assert.Equal(
            source,
            document.Nodes[
                Assert.Single(document.Seeds).Target.Id]
                .Subject);
    }

    [Fact]
    public void Execute_ZeroDepthRetainsSeedWithoutEdges()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            induced,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                InspectionGraphNeighborhoodRequest.SingleSeed(
                    hub,
                    [
                        InspectionGraphIntegrationsCatalog
                            .IntegrationObserved,
                    ],
                    InspectionGraphTraversalDirection.Incoming,
                    maxDepth: 0));

        Assert.Empty(document.Edges);
        Assert.Empty(document.Occurrences);
        Assert.Single(document.Seeds);
        Assert.Contains(
            document.Nodes,
            node => node.Subject == hub);
        var evidence = Assert.IsType<
            InspectionGraphNeighborhoodDepthBoundEvidence>(
                Assert.Single(document.Limits).Evidence);
        Assert.Equal(0, evidence.MaxDepth);
    }

    [Fact]
    public void Execute_SelectedRelationshipsControlProducerDemand()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            induced,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");
        InspectionGraphSubject.TypeSubject opportunitySource =
            Assert.IsType<InspectionGraphSubject.TypeSubject>(
                Assert.Single(
                    induced.Occurrences,
                    occurrence =>
                        occurrence.Relationship
                            == InspectionGraphIntegrationsCatalog
                                .IntegrationOpportunity)
                    .SourceSubject);
        InspectionGraphSubject.MemberSubject extensionSource =
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                induced.Occurrences.First(occurrence =>
                    occurrence.Relationship
                        == InspectionGraphIntegrationsCatalog.Extension)
                    .SourceSubject);
        InspectionGraphSubject.AssemblySubject referenceSource =
            Assert.IsType<InspectionGraphSubject.AssemblySubject>(
                induced.Nodes[
                    Assert.Single(
                        induced.Edges,
                        edge =>
                            edge.Relationship
                                == InspectionGraphIntegrationsCatalog
                                    .MetadataReference
                            && AssemblyName(
                                induced.Nodes[edge.FromNodeId].Subject)
                                == "Azure.AI.OpenAI")
                    .FromNodeId]
                .Subject);
        var extensionExecutions =
            new List<InspectionQueryDefinition>();
        var referenceExecutions =
            new List<InspectionQueryDefinition>();
        var observedExecutions =
            new List<InspectionQueryDefinition>();
        var opportunityExecutions =
            new List<InspectionQueryDefinition>();

        InspectionGraphIntegrationsQuery.Execute(
            fixture.Context,
            InspectionGraphNeighborhoodRequest.SingleSeed(
                extensionSource,
                [InspectionGraphIntegrationsCatalog.Extension],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1),
            (query, _) => extensionExecutions.Add(query));
        InspectionGraphIntegrationsQuery.Execute(
            fixture.Context,
            InspectionGraphNeighborhoodRequest.SingleSeed(
                referenceSource,
                [
                    InspectionGraphIntegrationsCatalog
                        .MetadataReference,
                ],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1),
            (query, _) => referenceExecutions.Add(query));
        InspectionGraphIntegrationsQuery.Execute(
            fixture.Context,
            InspectionGraphNeighborhoodRequest.SingleSeed(
                hub,
                [
                    InspectionGraphIntegrationsCatalog
                        .IntegrationObserved,
                ],
                InspectionGraphTraversalDirection.Incoming,
                maxDepth: 1),
            (query, _) => observedExecutions.Add(query));
        InspectionGraphIntegrationsQuery.Execute(
            fixture.Context,
            InspectionGraphNeighborhoodRequest.SingleSeed(
                opportunitySource,
                [
                    InspectionGraphIntegrationsCatalog
                        .IntegrationOpportunity,
                ],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1),
            (query, _) => opportunityExecutions.Add(query));

        Assert.Equal(
            [AssemblyContextExtensionMethodsQuery.Definition],
            extensionExecutions);
        Assert.Equal(
            [AssemblyContextReferencesQuery.Definition],
            referenceExecutions);
        Assert.Equal(
            [AssemblyContextIntegrationsQuery.Definition],
            observedExecutions);
        Assert.Equal(
            [
                AssemblyContextIntegrationsQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
            ],
            opportunityExecutions);
    }

    [Fact]
    public void Execute_RejectsForeignRelationshipBeforeProducerExecution()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphOccurrence integration =
            induced.Occurrences.First(occurrence =>
                occurrence.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationObserved);
        InspectionGraphSubject.MemberSubject member =
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                integration.SourceSubject);
        InspectionGraphNeighborhoodRequest request =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                member,
                [CallGraphInspectionGraphCatalog.Call],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth: 1);
        bool producerRan = false;

        InspectionQueryException exception = Assert.Throws<
            InspectionQueryException>(
                () => InspectionGraphIntegrationsQuery.Execute(
                    fixture.Context,
                    request,
                    (_, _) => producerRan = true));

        Assert.Contains("not supported", exception.Message);
        Assert.False(producerRan);
    }

    [Fact]
    public void Execute_NeighborhoodRetainsSelectedProducerFailures()
    {
        using var fixture = IntegrationFixture.Create(
            includeRejectedParticipant: true);
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            induced,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                InspectionGraphNeighborhoodRequest.SingleSeed(
                    hub,
                    [
                        InspectionGraphIntegrationsCatalog
                            .IntegrationObserved,
                    ],
                    InspectionGraphTraversalDirection.Incoming,
                    maxDepth: 1));

        InspectionGraphFailure failure = Assert.Single(document.Failures);
        Assert.Equal(
            InspectionGraphTargetKind.Node,
            failure.Target!.Value.Kind);
        Assert.Equal(
            "Rejected.Integration",
            AssemblyName(
                document.Nodes[failure.Target.Value.Id].Subject));
        Assert.All(
            document.Edges,
            edge => Assert.Same(
                InspectionGraphIntegrationsCatalog.IntegrationObserved,
                edge.Relationship));
    }

    [Fact]
    public void Execute_ProjectsLockedIChatClientEvidenceAcrossPackageGroups()
    {
        using var fixture = IntegrationFixture.Create();

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Empty(document.Failures);
        Assert.Equal(6, document.Groups.Length);

        InspectionGraphSubject.TypeSubject hub = FindType(
            document,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");
        InspectionGraphOccurrence[] integrations =
        [
            .. document.Occurrences.Where(occurrence =>
                occurrence.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationObserved
                && occurrence.TargetSubject == hub),
        ];
        Assert.Equal(2, integrations.Length);
        Assert.Equal(
            [
                "awssdk.extensions.bedrock.meai",
                "microsoft.extensions.ai.openai",
            ],
            integrations
                .Select(occurrence => PackageId(
                    document,
                    Node(document, occurrence.SourceSubject)))
                .Order(StringComparer.Ordinal));
        Assert.All(
            integrations,
            occurrence =>
            {
                var evidence =
                    Assert.IsType<InspectionGraphIntegrationEvidence>(
                        occurrence.Evidence);
                Assert.Equal(
                    EcosystemIntegrationNames.AI,
                    evidence.Integration);
                Assert.Equal(
                    "AsIChatClient",
                    evidence.Member.MemberName);
            });

        InspectionGraphEdge[] extensions =
        [
            .. document.Edges.Where(edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog.Extension),
        ];
        Assert.Equal(2, extensions.Length);
        Assert.Contains(
            extensions,
            edge =>
                TypeName(document.Nodes[edge.ToNodeId].Subject)
                == "OpenAI.Chat.ChatClient");
        Assert.Contains(
            extensions,
            edge =>
                TypeName(document.Nodes[edge.ToNodeId].Subject)
                == "Amazon.BedrockRuntime.IAmazonBedrockRuntime");

        InspectionGraphEdge azureReference = Assert.Single(
            document.Edges.Where(edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog.MetadataReference
                && AssemblyName(document.Nodes[edge.FromNodeId].Subject)
                    == "Azure.AI.OpenAI"
                && AssemblyName(document.Nodes[edge.ToNodeId].Subject)
                    == "OpenAI"));
        Assert.Single(azureReference.OccurrenceIds);

        InspectionGraphEdge opportunity = Assert.Single(
            document.Edges.Where(edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationOpportunity));
        Assert.Equal(
            "Azure.AI.OpenAI",
            AssemblyName(document.Nodes[opportunity.FromNodeId].Subject));
        Assert.Equal(
            "Microsoft.Extensions.AI.IChatClient",
            TypeName(document.Nodes[opportunity.ToNodeId].Subject));
        var opportunityOccurrence = document.Occurrences[
            Assert.Single(opportunity.OccurrenceIds)];
        Assert.Equal(
            "Azure.AI.OpenAI.AzureOpenAIClient",
            TypeName(opportunityOccurrence.SourceSubject));
        Assert.DoesNotContain(
            document.Edges,
            edge => edge.Relationship.Id == "call");
    }

    [Fact]
    public void PackageAndTypeModesShareSemanticIntegrationOccurrences()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument induced =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            induced,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");
        InspectionGraphSubject.PackageSubject openAi =
            PackageSubject(induced, "microsoft.extensions.ai.openai");
        InspectionGraphSubject.PackageSubject bedrock =
            PackageSubject(induced, "awssdk.extensions.bedrock.meai");
        InspectionGraphDocument typeOutward =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                InspectionGraphModeRequest.SingleSeed(hub));
        InspectionGraphDocument packageInward =
            InspectionGraphIntegrationsQuery.Execute(
                fixture.Context,
                InspectionGraphModeRequest.PeerSeeds(
                    [openAi, bedrock]));

        InspectionGraphOccurrence[] typeOccurrences =
        [
            .. typeOutward.Occurrences
                .Where(occurrence =>
                    occurrence.Relationship
                        == InspectionGraphIntegrationsCatalog
                            .IntegrationObserved
                    && occurrence.TargetSubject == hub),
        ];
        InspectionGraphOccurrence[] packageOccurrences =
        [
            .. packageInward.Edges
                .Where(edge =>
                    edge.Relationship
                        == InspectionGraphIntegrationsCatalog
                            .IntegrationObserved
                    && PackageId(
                        packageInward,
                        packageInward.Nodes[edge.FromNodeId])
                        is "microsoft.extensions.ai.openai"
                            or "awssdk.extensions.bedrock.meai")
                .SelectMany(static edge => edge.OccurrenceIds)
                .Order()
                .Select(id => packageInward.Occurrences[id]),
        ];

        Assert.Equal(
            typeOccurrences.Select(occurrence =>
                occurrence.Relationship.OccurrenceIdentity.Project(
                    occurrence)),
            packageOccurrences.Select(occurrence =>
                occurrence.Relationship.OccurrenceIdentity.Project(
                    occurrence)));
        Assert.Equal(
            typeOccurrences.Select(occurrence =>
                (occurrence.SourceSubject, occurrence.TargetSubject)),
            packageOccurrences.Select(occurrence =>
                (occurrence.SourceSubject, occurrence.TargetSubject)));
        Assert.Equal(2, typeOccurrences.Length);
    }

    [Fact]
    public void Execute_DoesNotJoinAmbiguousMatchingAssemblyIdentities()
    {
        using var fixture = IntegrationFixture.Create(
            duplicateHubAssembly: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Equal(
            2,
            document.Nodes.Count(node =>
                node.Subject
                    is InspectionGraphSubject.TypeSubject
                    {
                        Identity:
                            InspectionGraphTypeIdentity.AcquiredDefinition
                            identity,
                    }
                && identity.Type.ToMetadataFullName()
                    == "Microsoft.Extensions.AI.IChatClient"));
        Assert.DoesNotContain(
            document.Edges,
            edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationObserved);
        Assert.Contains(
            document.Failures,
            failure =>
                Assert.IsType<InspectionGraphIntegrationFailureEvidence>(
                    failure.Evidence).Details.Any(detail =>
                        detail.Kind
                            == InspectionGraphIntegrationFailureKind
                                .BindingAmbiguous));
        Assert.Contains(
            document.Failures,
            failure =>
                Assert.IsType<InspectionGraphIntegrationFailureEvidence>(
                    failure.Evidence).Details.Any(detail =>
                        detail.Producer == "references"
                        && detail.Kind
                            == InspectionGraphIntegrationFailureKind
                                .BindingAmbiguous));
    }

    [Fact]
    public void Execute_ReportsApiWhoseStructuredEvidenceIsUnavailable()
    {
        using var fixture = IntegrationFixture.Create(
            overBudgetAdapterTypeName: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Contains(
            document.Failures,
            failure =>
            {
                var evidence =
                    Assert.IsType<InspectionGraphIntegrationFailureEvidence>(
                        failure.Evidence);
                return evidence.Details.Any(detail =>
                    detail.Producer == "integrations"
                    && detail.Kind
                        == InspectionGraphIntegrationFailureKind
                            .StructuredEvidenceUnavailable);
            });
    }

    [Fact]
    public void Execute_DifferentAiTargetDoesNotFulfillChatOpportunity()
    {
        using var fixture = IntegrationFixture.Create(
            openAiAdapterReturnsDifferentAiType: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Contains(
            document.Edges,
            edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationOpportunity
                && AssemblyName(document.Nodes[edge.FromNodeId].Subject)
                    == "OpenAI"
                && TypeName(document.Nodes[edge.ToNodeId].Subject)
                    == "Microsoft.Extensions.AI.IChatClient");
    }

    [Fact]
    public void Execute_AggregatesRejectedParticipantFailuresByTarget()
    {
        using var fixture = IntegrationFixture.Create(
            includeRejectedParticipant: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        InspectionGraphFailure rejected = Assert.Single(
            document.Failures,
            failure =>
                failure.Target is
                    {
                        Kind: InspectionGraphTargetKind.Node,
                    } target
                && AssemblyName(document.Nodes[target.Id].Subject)
                    == "Rejected.Integration");
        var evidence =
            Assert.IsType<InspectionGraphIntegrationFailureEvidence>(
                rejected.Evidence);
        Assert.Equal(
            ["extensions", "integrations", "opportunities", "references"],
            evidence.Details
                .Select(static detail => detail.Producer)
                .Order(StringComparer.Ordinal));
        Assert.All(
            evidence.Details,
            detail => Assert.Equal(
                InspectionGraphIntegrationFailureKind.ParticipantRejected,
                detail.Kind));
    }

    [Fact]
    public void Execute_DeduplicatesRepeatedAssemblyReferenceRows()
    {
        using var fixture = IntegrationFixture.Create(
            duplicateOpenAiReference: true);
        var references = AssemblyContextReferencesQuery.Execute(
            fixture.Context.Group);
        var azureReferences = Assert.IsType<
            AssemblyContextEntry<
                System.Collections.Immutable.ImmutableArray<
                    AssemblyReferenceIdentity>>.Available>(
            Assert.Single(
                references.Assemblies,
                entry => entry.Subject.Identity.Name
                    == "Azure.AI.OpenAI"));
        Assert.Equal(
            2,
            azureReferences.Value.Count(reference =>
                reference.Name == "OpenAI"));

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        InspectionGraphEdge reference = Assert.Single(
            document.Edges.Where(edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog.MetadataReference
                && AssemblyName(document.Nodes[edge.FromNodeId].Subject)
                    == "Azure.AI.OpenAI"
                && AssemblyName(document.Nodes[edge.ToNodeId].Subject)
                    == "OpenAI"));
        Assert.Single(reference.OccurrenceIds);
    }

    [Fact]
    public void Execute_DeduplicatesEquivalentAssemblyReferenceRows()
    {
        using var fixture = IntegrationFixture.Create(
            equivalentReferenceVariants: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        InspectionGraphEdge reference = Assert.Single(
            document.Edges.Where(edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog.MetadataReference
                && AssemblyName(document.Nodes[edge.FromNodeId].Subject)
                    == "ReferenceVariants"
                && AssemblyName(document.Nodes[edge.ToNodeId].Subject)
                    == "Foo"));
        Assert.Single(reference.OccurrenceIds);
    }

    [Fact]
    public void Execute_ReportsUnavailableReferenceBinding()
    {
        using var fixture = IntegrationFixture.Create(
            unavailableOpenAiBinding: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Contains(
            document.Failures,
            failure =>
                Assert.IsType<InspectionGraphIntegrationFailureEvidence>(
                    failure.Evidence).Details.Any(detail =>
                        detail.Producer == "references"
                        && detail.Kind
                            == InspectionGraphIntegrationFailureKind
                                .BindingUnavailable));
    }

    [Fact]
    public void Execute_RetainsEachUnavailableReferenceIdentity()
    {
        using var fixture = IntegrationFixture.Create(
            multipleUnavailableReferenceBindings: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        InspectionGraphFailure failure = Assert.Single(
            document.Failures,
            failure =>
                failure.Target is
                    {
                        Kind: InspectionGraphTargetKind.Node,
                    } target
                && AssemblyName(document.Nodes[target.Id].Subject)
                    == "UnavailableReferences");
        var evidence =
            Assert.IsType<InspectionGraphIntegrationFailureEvidence>(
                failure.Evidence);
        Assert.Equal(
            ["Bar", "Foo"],
            evidence.Details
                .Where(detail =>
                    detail.Producer == "references"
                    && detail.Kind
                        == InspectionGraphIntegrationFailureKind
                            .BindingUnavailable)
                .Select(detail => Assert.IsType<
                    AssemblyReferenceIdentity>(detail.Reference).Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Execute_RetainsEquivalentUnavailableReferenceSpellings()
    {
        using var fixture = IntegrationFixture.Create(
            equivalentReferenceVariants: true,
            multipleUnavailableReferenceBindings: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        InspectionGraphFailure failure = Assert.Single(
            document.Failures,
            failure =>
                failure.Target is
                    {
                        Kind: InspectionGraphTargetKind.Node,
                    } target
                && AssemblyName(document.Nodes[target.Id].Subject)
                    == "ReferenceVariants");
        AssemblyReferenceIdentity[] references =
        [
            .. Assert.IsType<
                    InspectionGraphIntegrationFailureEvidence>(
                        failure.Evidence)
                .Details
                .Where(detail =>
                    detail.Producer == "references"
                    && detail.Kind
                        == InspectionGraphIntegrationFailureKind
                            .BindingUnavailable)
                .Select(detail => Assert.IsType<
                    AssemblyReferenceIdentity>(detail.Reference)),
        ];
        Assert.Equal(2, references.Length);
        Assert.Contains(
            references,
            reference =>
                reference.Name == "Foo"
                && reference.Culture is null);
        Assert.Contains(
            references,
            reference =>
                reference.Name == "foo"
                && reference.Culture == "neutral");
    }

    [Fact]
    public void Execute_DeduplicatesEquivalentExtensionMethodRows()
    {
        using var fixture = IntegrationFixture.Create(
            duplicateExtensionMethodRows: true);
        var extensions = AssemblyContextExtensionMethodsQuery.Execute(
            fixture.Context.Group);
        var duplicateExtensions = Assert.IsType<
            AssemblyContextEntry<
                System.Collections.Immutable.ImmutableArray<
                    ExtensionMethodInfo>>.Available>(
            Assert.Single(
                extensions.Assemblies,
                entry => entry.Subject.Identity.Name == "Dup.Ext"));
        Assert.Equal(2, duplicateExtensions.Value.Length);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        InspectionGraphOccurrence occurrence = Assert.Single(
            document.Occurrences,
            occurrence =>
                occurrence.Relationship
                    == InspectionGraphIntegrationsCatalog.Extension
                && Assert.IsType<InspectionGraphExtensionEvidence>(
                    occurrence.Evidence).Registration
                    is { } registration
                && ReferenceEquals(
                    registration,
                    duplicateExtensions.Subject.Registration));
        Assert.IsType<InspectionGraphExtensionEvidence>(
            occurrence.Evidence);
    }

    [Fact]
    public void ExtensionOccurrenceIdentity_NormalizesEquivalentScopes()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphOccurrence occurrence = Assert.Single(
            document.Occurrences,
            occurrence =>
                occurrence.Relationship
                    == InspectionGraphIntegrationsCatalog.Extension
                && TypeName(occurrence.TargetSubject)
                    == "OpenAI.Chat.ChatClient");
        var evidence =
            Assert.IsType<InspectionGraphExtensionEvidence>(
                occurrence.Evidence);
        var scope =
            Assert.IsType<
                MetadataTypeReferenceScope.AssemblyReference>(
                    evidence.ExtendedType.Scope);
        var equivalentAssembly = new AssemblyReferenceIdentity(
            scope.Assembly.Name.ToLowerInvariant(),
            scope.Assembly.Version,
            "neutral",
            scope.Assembly.PublicKeyToken?.ToUpperInvariant());
        var equivalentEvidence = evidence with
        {
            ExtendedType = new MetadataNamedTypeReference(
                new MetadataTypeReferenceScope.AssemblyReference(
                    equivalentAssembly),
                evidence.ExtendedType.Type),
        };
        var equivalentOccurrence = new InspectionGraphOccurrence(
            occurrence.Id,
            occurrence.Relationship,
            occurrence.SourceSubject,
            occurrence.TargetSubject,
            equivalentEvidence,
            occurrence.DerivedFromOccurrenceIds);

        Assert.Equal(
            occurrence.Relationship.OccurrenceIdentity.Project(
                occurrence),
            occurrence.Relationship.OccurrenceIdentity.Project(
                equivalentOccurrence));
    }

    [Fact]
    public void Execute_RetainsOverloadedAdapterEvidence()
    {
        using var fixture = IntegrationFixture.Create(
            overloadedOpenAiAdapter: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Equal(
            2,
            document.Occurrences.Count(occurrence =>
                occurrence.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationObserved
                && Assert.IsType<InspectionGraphIntegrationEvidence>(
                    occurrence.Evidence).Registration
                    is { } registration
                && AcquiredAssemblyName(document, registration)
                    == "Microsoft.Extensions.AI.OpenAI"));
        Assert.DoesNotContain(
            document.Edges,
            edge =>
                edge.Relationship
                    == InspectionGraphIntegrationsCatalog
                        .IntegrationOpportunity
                && AssemblyName(document.Nodes[edge.FromNodeId].Subject)
                    == "OpenAI"
                && TypeName(document.Nodes[edge.ToNodeId].Subject)
                    == "Microsoft.Extensions.AI.IChatClient");
    }

    [Fact]
    public void Execute_ReportsTypeWhoseStructuredEvidenceIsUnavailable()
    {
        using var fixture = IntegrationFixture.Create(
            overBudgetIntegrationTypeName: true);

        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);

        Assert.Contains(
            document.Failures,
            failure =>
                failure.Target is
                    {
                        Kind: InspectionGraphTargetKind.Node,
                    } target
                && AssemblyName(document.Nodes[target.Id].Subject)
                    == "Oversized.Logging"
                && Assert.IsType<
                    InspectionGraphIntegrationFailureEvidence>(
                        failure.Evidence).Details.Any(detail =>
                            detail.Producer == "integrations"
                            && detail.Kind
                                == InspectionGraphIntegrationFailureKind
                                    .StructuredEvidenceUnavailable));
    }

    static InspectionGraphNode Node(
        InspectionGraphDocument document,
        InspectionGraphSubject subject) =>
        Assert.Single(document.Nodes, node => node.Subject == subject);

    static InspectionGraphSubject.TypeSubject FindType(
        InspectionGraphDocument document,
        string assemblyName,
        string @namespace,
        string name) =>
        Assert.IsType<InspectionGraphSubject.TypeSubject>(
            Assert.Single(
                document.Nodes,
                node =>
                    node.Subject
                        is InspectionGraphSubject.TypeSubject
                        {
                            Identity:
                                InspectionGraphTypeIdentity
                                    .AcquiredDefinition identity,
                        }
                    && AcquiredAssemblyName(
                        document,
                        identity.Registration) == assemblyName
                    && identity.Type.Namespace == @namespace
                    && identity.Type.ToNestedMetadataName() == name)
                .Subject);

    static string TypeName(InspectionGraphSubject subject)
    {
        var type = Assert.IsType<InspectionGraphSubject.TypeSubject>(
            subject);
        var identity =
            Assert.IsType<
                InspectionGraphTypeIdentity.AcquiredDefinition>(
                    type.Identity);
        return identity.Type.ToMetadataFullName();
    }

    static string AssemblyName(InspectionGraphSubject subject)
    {
        var assembly =
            Assert.IsType<InspectionGraphSubject.AssemblySubject>(
                subject);
        return Assert.IsType<InspectionGraphAssemblyIdentity.Acquired>(
            assembly.Identity).Assembly.Name;
    }

    static string AcquiredAssemblyName(
        InspectionGraphDocument document,
        AssemblyAcquisitionRegistration registration) =>
        Assert.IsType<InspectionGraphAssemblyIdentity.Acquired>(
            Assert.IsType<InspectionGraphSubject.AssemblySubject>(
                Assert.Single(
                    document.Nodes,
                    node =>
                        node.Subject
                            is InspectionGraphSubject.AssemblySubject
                            {
                                Identity:
                                    InspectionGraphAssemblyIdentity.Acquired
                                    identity,
                            }
                        && ReferenceEquals(
                            identity.Registration,
                            registration))
                    .Subject)
                .Identity)
            .Assembly.Name;

    static string PackageId(
        InspectionGraphDocument document,
        InspectionGraphNode node)
    {
        InspectionGraphGroup group = Assert.Single(
            document.Groups,
            group => node.GroupIds.Contains(group.Id));
        var package =
            Assert.IsType<InspectionGraphSubject.PackageSubject>(
                group.Subject);
        return Assert.IsType<InspectionGraphPackageIdentity.Realized>(
            package.Identity).Package.PackageId;
    }

    static InspectionGraphSubject.PackageSubject PackageSubject(
        InspectionGraphDocument document,
        string packageId) =>
        Assert.IsType<InspectionGraphSubject.PackageSubject>(
            Assert.Single(
                document.Groups,
                group =>
                    Assert.IsType<
                        InspectionGraphPackageIdentity.Realized>(
                            Assert.IsType<
                                InspectionGraphSubject.PackageSubject>(
                                    group.Subject).Identity)
                        .Package.PackageId == packageId)
                .Subject);

    sealed class IntegrationFixture : IDisposable
    {
        readonly InspectionWorkspace _workspace;

        IntegrationFixture(
            InspectionWorkspace workspace,
            WorkspaceContextLoadOutcome.Loaded context)
        {
            _workspace = workspace;
            Context = context;
        }

        internal WorkspaceContextLoadOutcome.Loaded Context { get; }

        public void Dispose() => _workspace.Dispose();

        internal static IntegrationFixture Create(
            bool duplicateHubAssembly = false,
            bool overBudgetAdapterTypeName = false,
            bool duplicateOpenAiReference = false,
            bool openAiAdapterReturnsDifferentAiType = false,
            bool includeRejectedParticipant = false,
            bool equivalentReferenceVariants = false,
            bool unavailableOpenAiBinding = false,
            bool duplicateExtensionMethodRows = false,
            bool multipleUnavailableReferenceBindings = false,
            bool overloadedOpenAiAdapter = false,
            bool overBudgetIntegrationTypeName = false)
        {
            (
                PersistedAssemblyBuilder abstractions,
                Type iChatClient,
                Type iEmbeddingGenerator) =
                Abstractions();

            var openAi = new PersistedAssemblyBuilder(
                new AssemblyName("OpenAI"),
                typeof(object).Assembly);
            ModuleBuilder openAiModule =
                openAi.DefineDynamicModule("OpenAI");
            Type chatClient = DefineClass(
                openAiModule,
                "OpenAI.Chat.ChatClient");
            Type otherClient = DefineClass(
                openAiModule,
                "OpenAI.Chat.OtherClient");

            var bedrock = new PersistedAssemblyBuilder(
                new AssemblyName("AWSSDK.BedrockRuntime"),
                typeof(object).Assembly);
            Type bedrockClient = bedrock
                .DefineDynamicModule("AWSSDK.BedrockRuntime")
                .DefineType(
                    "Amazon.BedrockRuntime.IAmazonBedrockRuntime",
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract)
                .CreateType();

            var openAiAdapter = Adapter(
                "Microsoft.Extensions.AI.OpenAI",
                overBudgetAdapterTypeName
                    ? "Microsoft.Extensions.AI.OpenAI."
                        + new string('x', 5000)
                    : "Microsoft.Extensions.AI.OpenAI.OpenAIClientExtensions",
                chatClient,
                openAiAdapterReturnsDifferentAiType
                    ? iEmbeddingGenerator
                    : iChatClient,
                overloadedOpenAiAdapter ? otherClient : null);
            var bedrockAdapter = Adapter(
                "AWSSDK.Extensions.Bedrock.MEAI",
                "Microsoft.Extensions.AI.AmazonBedrockRuntimeExtensions",
                bedrockClient,
                iChatClient);

            var azure = new PersistedAssemblyBuilder(
                new AssemblyName("Azure.AI.OpenAI"),
                typeof(object).Assembly);
            ModuleBuilder azureModule =
                azure.DefineDynamicModule("Azure.AI.OpenAI");
            TypeBuilder azureClient = azureModule.DefineType(
                "Azure.AI.OpenAI.AzureOpenAIClient",
                TypeAttributes.Public | TypeAttributes.Class);
            azureClient.DefineDefaultConstructor(MethodAttributes.Public);
            MethodBuilder openAiReceipt = azureClient.DefineMethod(
                "AsOpenAIClient",
                MethodAttributes.Public,
                chatClient,
                Type.EmptyTypes);
            ILGenerator receiptBody = openAiReceipt.GetILGenerator();
            receiptBody.Emit(OpCodes.Ldnull);
            receiptBody.Emit(OpCodes.Ret);
            if (duplicateOpenAiReference)
            {
                var duplicateOpenAi = new PersistedAssemblyBuilder(
                    new AssemblyName("OpenAI"),
                    typeof(object).Assembly);
                Type duplicateType = DefineClass(
                    duplicateOpenAi.DefineDynamicModule("OpenAI"),
                    "OpenAI.Chat.DuplicateReference");
                azureClient.DefineField(
                    "DuplicateReference",
                    duplicateType,
                    FieldAttributes.Public);
            }
            azureClient.CreateType();

            List<PersistedAssemblyBuilder> builders =
            [
                abstractions,
                openAi,
                bedrock,
                openAiAdapter,
                bedrockAdapter,
                azure,
            ];
            List<string> packageIds =
            [
                "microsoft.extensions.ai.abstractions",
                "openai",
                "awssdk.bedrockruntime",
                "microsoft.extensions.ai.openai",
                "awssdk.extensions.bedrock.meai",
                "azure.ai.openai",
            ];
            if (duplicateHubAssembly)
            {
                builders.Add(Abstractions().Builder);
                packageIds.Add(
                    "microsoft.extensions.ai.abstractions.copy");
            }
            var assemblies = builders.Select((builder, index) =>
                Assembly(builder, packageIds[index])).ToList();
            if (includeRejectedParticipant)
            {
                assemblies.Add(RejectedAssembly());
                packageIds.Add("rejected.integration");
            }
            if (equivalentReferenceVariants)
            {
                var fooName = new AssemblyName("Foo")
                {
                    Version = new Version(1, 0, 0, 0),
                };
                var foo = new PersistedAssemblyBuilder(
                    fooName,
                    typeof(object).Assembly);
                foo.DefineDynamicModule("Foo");
                assemblies.Add(Assembly(foo, "foo"));
                packageIds.Add("foo");
                assemblies.Add(EquivalentReferencesAssembly());
                packageIds.Add("reference.variants");
            }
            if (duplicateExtensionMethodRows)
            {
                assemblies.Add(DuplicateExtensionsAssembly());
                packageIds.Add("dup.ext");
            }
            if (multipleUnavailableReferenceBindings)
            {
                assemblies.Add(UnavailableReferencesAssembly());
                packageIds.Add("unavailable.references");
            }
            if (overBudgetIntegrationTypeName)
            {
                var oversized = new PersistedAssemblyBuilder(
                    new AssemblyName("Oversized.Logging"),
                    typeof(object).Assembly);
                oversized
                    .DefineDynamicModule("Oversized.Logging")
                    .DefineType(
                        "Microsoft.Extensions.Logging."
                            + new string('x', 5000),
                        TypeAttributes.Public | TypeAttributes.Class)
                    .CreateType();
                assemblies.Add(
                    Assembly(oversized, "oversized.logging"));
                packageIds.Add("oversized.logging");
            }
            var policy = new FixtureBindingPolicy(
                assemblies,
                unavailableOpenAiBinding,
                multipleUnavailableReferenceBindings);
            WorkspaceContextMember[] members =
            [
                .. assemblies.Select((assembly, index) =>
                    Member(
                        assembly,
                        packageIds[index],
                        policy)),
            ];
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [.. members.Select(
                        static member => member.Participant)]);
            var loaded = new WorkspaceContextLoadOutcome.Loaded(
                group,
                [.. members],
                "net11.0",
                null);
            return new IntegrationFixture(workspace, loaded);
        }

        static (
            PersistedAssemblyBuilder Builder,
            Type Chat,
            Type Embedding) Abstractions()
        {
            var builder = new PersistedAssemblyBuilder(
                new AssemblyName("Microsoft.Extensions.AI.Abstractions"),
                typeof(object).Assembly);
            ModuleBuilder module = builder.DefineDynamicModule(
                "Microsoft.Extensions.AI.Abstractions");
            Type chat = module
                .DefineType(
                    "Microsoft.Extensions.AI.IChatClient",
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract)
                .CreateType();
            Type embedding = module
                .DefineType(
                    "Microsoft.Extensions.AI.IEmbeddingGenerator",
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract)
                .CreateType();
            return (builder, chat, embedding);
        }

        static PersistedAssemblyBuilder Adapter(
            string assemblyName,
            string typeName,
            Type receiver,
            Type returnType,
            Type? priorReceiver = null)
        {
            var builder = new PersistedAssemblyBuilder(
                new AssemblyName(assemblyName),
                typeof(object).Assembly);
            TypeBuilder extensions = builder
                .DefineDynamicModule(assemblyName)
                .DefineType(
                    typeName,
                    TypeAttributes.Public
                        | TypeAttributes.Abstract
                        | TypeAttributes.Sealed);
            var extensionAttribute = new CustomAttributeBuilder(
                typeof(ExtensionAttribute).GetConstructor(
                    Type.EmptyTypes)!,
                []);
            extensions.SetCustomAttribute(extensionAttribute);
            Type[] receiverTypes = priorReceiver is null
                ? [receiver]
                : [priorReceiver, receiver];
            foreach (Type receiverType in receiverTypes)
            {
                MethodBuilder method = extensions.DefineMethod(
                    "AsIChatClient",
                    MethodAttributes.Public | MethodAttributes.Static,
                    returnType,
                    [receiverType]);
                method.SetCustomAttribute(extensionAttribute);
                ILGenerator body = method.GetILGenerator();
                body.Emit(OpCodes.Ldnull);
                body.Emit(OpCodes.Ret);
            }
            extensions.CreateType();
            return builder;
        }

        static Type DefineClass(
            ModuleBuilder module,
            string name)
        {
            TypeBuilder type = module.DefineType(
                name,
                TypeAttributes.Public | TypeAttributes.Class);
            type.DefineDefaultConstructor(MethodAttributes.Public);
            return type.CreateType();
        }

        static ResolvedAssemblyReference Assembly(
            PersistedAssemblyBuilder builder,
            string packageId)
        {
            using var stream = new MemoryStream();
            builder.Save(stream);
            byte[] bytes = stream.ToArray();
            AssemblyReferenceIdentity identity;
            using (var reader = new PEReader(
                       new MemoryStream(bytes, writable: false)))
            {
                identity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        reader.GetMetadataReader());
            }

            return ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Package(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    null));
        }

        static ResolvedAssemblyReference RejectedAssembly() =>
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "Rejected.Integration",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(
                    [0x00, 0x01, 0x02],
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "rejected.integration",
                    "1.0.0",
                    "net11.0",
                    null));

        static ResolvedAssemblyReference EquivalentReferencesAssembly()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("ReferenceVariants.dll"),
                metadata.GetOrAddGuid(
                    new Guid(
                        "84112b51-cad1-4c5d-b499-bbbc52ab47b8")),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("ReferenceVariants"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Foo"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("foo"),
                new Version(1, 0, 0, 0),
                metadata.GetOrAddString("neutral"),
                default,
                default,
                default);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var pe = new ManagedPEBuilder(
                new PEHeaderBuilder(
                    imageCharacteristics:
                        Characteristics.Dll
                        | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                new BlobBuilder());
            var output = new BlobBuilder();
            pe.Serialize(output);
            byte[] bytes = output.ToArray();
            return ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "ReferenceVariants",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Package(
                    "reference.variants",
                    "1.0.0",
                    "net11.0",
                    null));
        }

        static ResolvedAssemblyReference DuplicateExtensionsAssembly()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString("Dup.Ext.dll"),
                metadata.GetOrAddGuid(
                    new Guid(
                        "2c6f7d38-6b1e-4a1f-9f0e-6f8a1a2b3c4d")),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("Dup.Ext"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            AssemblyReferenceHandle coreLibrary =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("System.Runtime"),
                    new Version(11, 0, 0, 0),
                    default,
                    default,
                    default,
                    default);
            TypeReferenceHandle extensionAttribute =
                metadata.AddTypeReference(
                    coreLibrary,
                    metadata.GetOrAddString(
                        "System.Runtime.CompilerServices"),
                    metadata.GetOrAddString("ExtensionAttribute"));
            var constructorSignature = new BlobBuilder();
            constructorSignature.WriteByte(0x20);
            constructorSignature.WriteCompressedInteger(0);
            constructorSignature.WriteByte(0x01);
            MemberReferenceHandle extensionConstructor =
                metadata.AddMemberReference(
                    extensionAttribute,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(constructorSignature));
            int serviceCollectionIndex = (2 << 2) | 0;
            var methodSignature = new BlobBuilder();
            methodSignature.WriteByte(0x00);
            methodSignature.WriteCompressedInteger(1);
            methodSignature.WriteByte(0x12);
            methodSignature.WriteCompressedInteger(
                serviceCollectionIndex);
            methodSignature.WriteByte(0x12);
            methodSignature.WriteCompressedInteger(
                serviceCollectionIndex);
            BlobHandle sharedSignature =
                metadata.GetOrAddBlob(methodSignature);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString(
                    "Microsoft.Extensions.DependencyInjection"),
                metadata.GetOrAddString("IServiceCollection"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle extensionType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public
                        | TypeAttributes.Sealed
                        | TypeAttributes.Abstract,
                    metadata.GetOrAddString(
                        "Microsoft.Extensions.DependencyInjection"),
                    metadata.GetOrAddString("ProbeExtensions"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            var attributeValue = new BlobBuilder();
            attributeValue.WriteUInt16(1);
            attributeValue.WriteUInt16(0);
            BlobHandle attributeBlob =
                metadata.GetOrAddBlob(attributeValue);
            metadata.AddCustomAttribute(
                extensionType,
                extensionConstructor,
                attributeBlob);
            for (int index = 0; index < 2; index++)
            {
                MethodDefinitionHandle method =
                    metadata.AddMethodDefinition(
                        MethodAttributes.Public
                            | MethodAttributes.Static,
                        MethodImplAttributes.IL,
                        metadata.GetOrAddString("AddProbeThing"),
                        sharedSignature,
                        bodyOffset: -1,
                        MetadataTokens.ParameterHandle(1));
                metadata.AddCustomAttribute(
                    method,
                    extensionConstructor,
                    attributeBlob);
            }
            var pe = new ManagedPEBuilder(
                new PEHeaderBuilder(
                    imageCharacteristics:
                        Characteristics.Dll
                        | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                new BlobBuilder());
            var output = new BlobBuilder();
            pe.Serialize(output);
            byte[] bytes = output.ToArray();
            return ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "Dup.Ext",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Package(
                    "dup.ext",
                    "1.0.0",
                    "net11.0",
                    null));
        }

        static ResolvedAssemblyReference UnavailableReferencesAssembly()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString(
                    "UnavailableReferences.dll"),
                metadata.GetOrAddGuid(
                    new Guid(
                        "cb43032a-a2e5-481d-ab5b-da59a92460b9")),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("UnavailableReferences"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            foreach (string name in new[] { "Foo", "Bar" })
            {
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString(name),
                    new Version(1, 0, 0, 0),
                    metadata.GetOrAddString("neutral"),
                    default,
                    default,
                    default);
            }
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var pe = new ManagedPEBuilder(
                new PEHeaderBuilder(
                    imageCharacteristics:
                        Characteristics.Dll
                        | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                new BlobBuilder());
            var output = new BlobBuilder();
            pe.Serialize(output);
            byte[] bytes = output.ToArray();
            return ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "UnavailableReferences",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Package(
                    "unavailable.references",
                    "1.0.0",
                    "net11.0",
                    null));
        }

        static WorkspaceContextMember Member(
            ResolvedAssemblyReference assembly,
            string packageId,
            IAssemblyBindingPolicy policy)
        {
            var package = new RealizedMemberCoordinate.Package(
                packageId,
                "1.0.0",
                "fixture",
                "net11.0",
                null);
            return new WorkspaceContextMember(
                WorkspaceMemberCoordinate.Package(
                    packageId,
                    "1.0.0",
                    "net11.0"),
                package,
                new AssemblyContextParticipant(
                    assembly,
                    policy));
        }
    }

    sealed class FixtureBindingPolicy : IAssemblyBindingPolicy
    {
        readonly IReadOnlyList<ResolvedAssemblyReference> _assemblies;
        readonly bool _unavailableOpenAiBinding;
        readonly bool _multipleUnavailableReferenceBindings;

        internal FixtureBindingPolicy(
            IReadOnlyList<ResolvedAssemblyReference> assemblies,
            bool unavailableOpenAiBinding,
            bool multipleUnavailableReferenceBindings)
        {
            _assemblies = assemblies;
            _unavailableOpenAiBinding = unavailableOpenAiBinding;
            _multipleUnavailableReferenceBindings =
                multipleUnavailableReferenceBindings;
        }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            if (request.Target
                is not AssemblyBindingTarget.AssemblyReference reference)
            {
                return AssemblyBindingSelection.NotFound();
            }
            if (_unavailableOpenAiBinding
                && reference.Identity.Name.Equals(
                    "OpenAI",
                    StringComparison.OrdinalIgnoreCase))
            {
                return AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .IdentityPolicyRequired));
            }
            if (_multipleUnavailableReferenceBindings
                && (reference.Identity.Name.Equals(
                        "Foo",
                        StringComparison.OrdinalIgnoreCase)
                    || reference.Identity.Name.Equals(
                        "Bar",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .IdentityPolicyRequired));
            }

            ResolvedAssemblyReference[] candidates =
            [
                .. _assemblies.Where(assembly =>
                    assembly.Identity.IsEquivalentTo(
                        reference.Identity)),
            ];
            return candidates.Length switch
            {
                0 => AssemblyBindingSelection.NotFound(),
                1 => AssemblyBindingSelection.Found(candidates[0]),
                _ => AssemblyBindingSelection.Multiple(
                    [.. candidates]),
            };
        }
    }
}
