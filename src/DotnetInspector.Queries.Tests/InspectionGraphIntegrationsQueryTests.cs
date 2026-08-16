using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionGraphIntegrationsQueryTests
{
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
    public void PackageAndTypeReadingsShareTheSameIntegrationOccurrences()
    {
        using var fixture = IntegrationFixture.Create();
        InspectionGraphDocument document =
            InspectionGraphIntegrationsQuery.Execute(fixture.Context);
        InspectionGraphSubject.TypeSubject hub = FindType(
            document,
            "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.AI",
            "IChatClient");

        int[] typeOutwardOccurrenceIds =
        [
            .. document.Occurrences
                .Where(occurrence =>
                    occurrence.Relationship
                        == InspectionGraphIntegrationsCatalog
                            .IntegrationObserved
                    && occurrence.TargetSubject == hub)
                .Select(static occurrence => occurrence.Id)
                .Order(),
        ];
        int[] packageInwardOccurrenceIds =
        [
            .. document.Edges
                .Where(edge =>
                    edge.Relationship
                        == InspectionGraphIntegrationsCatalog
                            .IntegrationObserved
                    && PackageId(
                        document,
                        document.Nodes[edge.FromNodeId])
                        is "microsoft.extensions.ai.openai"
                            or "awssdk.extensions.bedrock.meai")
                .SelectMany(static edge => edge.OccurrenceIds)
                .Order(),
        ];

        Assert.Equal(
            typeOutwardOccurrenceIds,
            packageInwardOccurrenceIds);
        Assert.Equal(2, typeOutwardOccurrenceIds.Length);
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
                    failure.Evidence).Kind
                == InspectionGraphIntegrationFailureKind
                    .BindingAmbiguous);
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
            bool duplicateHubAssembly = false)
        {
            (PersistedAssemblyBuilder abstractions, Type iChatClient) =
                Abstractions();

            var openAi = new PersistedAssemblyBuilder(
                new AssemblyName("OpenAI"),
                typeof(object).Assembly);
            Type chatClient = DefineClass(
                openAi.DefineDynamicModule("OpenAI"),
                "OpenAI.Chat.ChatClient");

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
                "Microsoft.Extensions.AI.OpenAI.OpenAIClientExtensions",
                chatClient,
                iChatClient);
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
            ResolvedAssemblyReference[] assemblies =
            [
                .. builders.Select((builder, index) =>
                    Assembly(builder, packageIds[index])),
            ];
            var policy = new FixtureBindingPolicy(assemblies);
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

        static (PersistedAssemblyBuilder Builder, Type Type) Abstractions()
        {
            var builder = new PersistedAssemblyBuilder(
                new AssemblyName("Microsoft.Extensions.AI.Abstractions"),
                typeof(object).Assembly);
            Type type = builder
                .DefineDynamicModule(
                    "Microsoft.Extensions.AI.Abstractions")
                .DefineType(
                    "Microsoft.Extensions.AI.IChatClient",
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract)
                .CreateType();
            return (builder, type);
        }

        static PersistedAssemblyBuilder Adapter(
            string assemblyName,
            string typeName,
            Type receiver,
            Type returnType)
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
            MethodBuilder method = extensions.DefineMethod(
                "AsIChatClient",
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                [receiver]);
            method.SetCustomAttribute(extensionAttribute);
            ILGenerator body = method.GetILGenerator();
            body.Emit(OpCodes.Ldnull);
            body.Emit(OpCodes.Ret);
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
        readonly ResolvedAssemblyReference[] _assemblies;

        internal FixtureBindingPolicy(
            ResolvedAssemblyReference[] assemblies) =>
            _assemblies = assemblies;

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            if (request.Target
                is not AssemblyBindingTarget.AssemblyReference reference)
            {
                return AssemblyBindingSelection.NotFound();
            }

            ResolvedAssemblyReference[] candidates =
            [
                .. _assemblies.Where(assembly =>
                    assembly.Identity == reference.Identity),
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
