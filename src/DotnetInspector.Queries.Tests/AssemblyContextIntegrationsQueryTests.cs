using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextIntegrationsQueryTests
{
    static readonly List<string> s_scanOrder = [];
    static Exception? s_callbackFailure;

    [Fact]
    public void SelectedScan_PublicConsumerRunsOncePerParticipantWithoutCaching()
    {
        var policy = new TestBindingPolicy(new AssemblyBindingPolicyVersion());
        TestAssembly di = TestAssembly.Create(
            "SelectedDi",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy);
        TestAssembly logging = TestAssembly.Create(
            "SelectedLogging",
            "Microsoft.Extensions.Logging.CustomLogger",
            policy);
        TestAssembly empty = TestAssembly.Create(
            "SelectedEmpty",
            "N.Hidden",
            policy,
            publicType: false);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [di.Participant, logging.Participant, empty.Participant]);
        s_scanOrder.Clear();
        var selected = EcosystemIntegrationScannerBinding.Create(SelectDependencyInjection);
        var neighboring = EcosystemIntegrationScannerBinding.Create(UnselectedScanner);
        Assert.NotSame(selected, neighboring);
        Assert.Empty(s_scanOrder);

        AssemblyContextIntegrationScanResult first =
            AssemblyContextIntegrationScanQuery.Execute(group, selected);
        AssemblyContextIntegrationScanResult second =
            AssemblyContextIntegrationScanQuery.Execute(group, selected);

        Assert.True(first.IsComplete);
        Assert.True(second.IsComplete);
        Assert.Same(selected, first.Binding);
        Assert.False(new AssemblyContextIntegrationsResult(first.Assemblies).IsComplete);
        Assert.Equal(
            [
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                "Microsoft.Extensions.Logging.CustomLogger",
                "<empty>",
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                "Microsoft.Extensions.Logging.CustomLogger",
                "<empty>",
            ],
            s_scanOrder);
        var diEntry = Assert.IsType<AssemblyIntegrationsEntry.Selected>(first.Assemblies[0]);
        AssertSubject(di, diEntry.Subject);
        EcosystemIntegrationSignalInfo signal = Assert.Single(diEntry.EcosystemSignals);
        Assert.Same(IntegrationConceptCatalog.DependencyInjection, signal.GetConcept());
        Assert.Same(IntegrationConceptCatalog.EcosystemObserved, signal.GetProducerPolicy());
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            signal.GetTypeDefinition()?.ToMetadataFullName());
        Assert.Empty(Assert.IsType<AssemblyIntegrationsEntry.Selected>(
            first.Assemblies[1]).EcosystemSignals);
        Assert.Empty(Assert.IsType<AssemblyIntegrationsEntry.Selected>(
            first.Assemblies[2]).EcosystemSignals);
        Assert.Equal(1, di.OpenCount);
        Assert.Equal(1, logging.OpenCount);
        Assert.Equal(1, empty.OpenCount);
    }

    [Fact]
    public void SelectedScan_DifferentBindingsAndFullScanKeepTheirOwnScope()
    {
        var policy = new TestBindingPolicy(new AssemblyBindingPolicyVersion());
        TestAssembly source = TestAssembly.Create(
            "SelectedScopes",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy,
            additionalIntegrationTypeName: "Microsoft.Extensions.Logging.CustomLogger");
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([source.Participant]);
        s_scanOrder.Clear();
        var di = EcosystemIntegrationScannerBinding.Create(SelectDependencyInjection);
        var logging = EcosystemIntegrationScannerBinding.Create(SelectLogging);

        var diResult = AssemblyContextIntegrationScanQuery.Execute(group, di);
        var loggingResult = AssemblyContextIntegrationScanQuery.Execute(group, logging);
        AssemblyContextIntegrationsResult full = AssemblyContextIntegrationsQuery.Execute(group);

        Assert.Same(di, diResult.Binding);
        Assert.Same(logging, loggingResult.Binding);
        Assert.Same(
            IntegrationConceptCatalog.DependencyInjection,
            Assert.Single(Assert.IsType<AssemblyIntegrationsEntry.Selected>(
                Assert.Single(diResult.Assemblies)).EcosystemSignals).GetConcept());
        Assert.Same(
            IntegrationConceptCatalog.Logging,
            Assert.Single(Assert.IsType<AssemblyIntegrationsEntry.Selected>(
                Assert.Single(loggingResult.Assemblies)).EcosystemSignals).GetConcept());
        var available = Assert.IsType<AssemblyIntegrationsEntry.Available>(
            Assert.Single(full.Assemblies));
        Assert.Equal(2, available.EcosystemSignals.Length);
        Assert.Equal(2, available.Presence.IntegrationCount);
        Assert.True(available.Presence.HasDependencyInjectionSupport);
        Assert.True(available.Presence.HasLoggingSupport);
        Assert.Single(s_scanOrder);
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public void SelectedScan_CarriesRejectionAndDecodeFailureBesideLaterResults()
    {
        var policy = new TestBindingPolicy(new AssemblyBindingPolicyVersion());
        TestAssembly rejected = TestAssembly.Create(
            "SelectedRejected",
            "N.Rejected",
            policy,
            selectedName: "DifferentIdentity");
        TestAssembly malformed = TestAssembly.Create(
            "SelectedMalformed",
            "N.Malformed",
            policy,
            invalidTypeName: true);
        TestAssembly available = TestAssembly.Create(
            "SelectedAvailable",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [rejected.Participant, malformed.Participant, available.Participant]);
        s_scanOrder.Clear();
        var binding = EcosystemIntegrationScannerBinding.Create(SelectDependencyInjection);

        AssemblyContextIntegrationScanResult result =
            AssemblyContextIntegrationScanQuery.Execute(group, binding);

        Assert.False(result.IsComplete);
        var rejection = Assert.IsType<AssemblyIntegrationsEntry.Rejected>(result.Assemblies[0]);
        AssertSubject(rejected, rejection.Subject);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejection.Failure.Kind);
        var failure = Assert.IsType<AssemblyIntegrationsEntry.Failed>(result.Assemblies[1]);
        AssertSubject(malformed, failure.Subject);
        var success = Assert.IsType<AssemblyIntegrationsEntry.Selected>(result.Assemblies[2]);
        AssertSubject(available, success.Subject);
        Assert.Single(success.EcosystemSignals);
        Assert.Single(s_scanOrder);
    }

    [Fact]
    public void SelectedScan_BudgetRejectionDoesNotInvokeScanner()
    {
        var policy = new TestBindingPolicy(new AssemblyBindingPolicyVersion());
        TestAssembly first = TestAssembly.Create(
            "SelectedBudgetFirst",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy);
        TestAssembly second = TestAssembly.Create(
            "SelectedBudgetSecond",
            "Microsoft.Extensions.Logging.CustomLogger",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [first.Participant, second.Participant],
            new AssemblyContextGroupOptions { MaxRetainedImageBytes = first.Bytes.Length });
        s_scanOrder.Clear();
        var binding = EcosystemIntegrationScannerBinding.Create(SelectDependencyInjection);

        var result = AssemblyContextIntegrationScanQuery.Execute(group, binding);

        Assert.False(result.IsComplete);
        Assert.IsType<AssemblyIntegrationsEntry.Selected>(result.Assemblies[0]);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            Assert.IsType<AssemblyIntegrationsEntry.Rejected>(result.Assemblies[1]).Failure.Kind);
        Assert.Single(s_scanOrder);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("range")]
    [InlineData("overflow")]
    [InlineData("configuration")]
    public void SelectedScan_PropagatesCallbackFaultsWithoutMisclassifyingThem(string kind)
    {
        var policy = new TestBindingPolicy(new AssemblyBindingPolicyVersion());
        TestAssembly source = TestAssembly.Create("CallbackFault", "N.Source", policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([source.Participant]);
        s_callbackFailure = kind switch
        {
            "metadata" => new BadImageFormatException("Scanner callback marker."),
            "range" => new ArgumentOutOfRangeException("scanner"),
            "overflow" => new OverflowException("Scanner callback marker."),
            _ => new InvalidOperationException("Scanner callback marker."),
        };
        var binding = EcosystemIntegrationScannerBinding.Create(FaultingScanner);

        Exception? failure = Record.Exception(() =>
            AssemblyContextIntegrationScanQuery.Execute(group, binding));

        Assert.Same(s_callbackFailure, failure);
    }

    [Fact]
    public void SelectedScan_ParticipantExecutionKeepsTheGroupReusable()
    {
        var policy = new TestBindingPolicy(new AssemblyBindingPolicyVersion());
        TestAssembly source = TestAssembly.Create(
            "SelectedReusable",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([source.Participant]);
        s_scanOrder.Clear();
        var binding = EcosystemIntegrationScannerBinding.Create(SelectDependencyInjection);

        Assert.IsType<AssemblyIntegrationsEntry.Selected>(
            AssemblyContextIntegrationScanQuery.ExecuteParticipant(
                group,
                source.Participant,
                binding));
        Assert.True(AssemblyContextIntegrationScanQuery.Execute(group, binding).IsComplete);
        Assert.True(AssemblyContextIntegrationsQuery.Execute(group).IsComplete);
        Assert.Equal(2, s_scanOrder.Count);
        Assert.Equal(1, source.OpenCount);
    }

    static ImmutableArray<EcosystemIntegrationClassification> SelectDependencyInjection(
        EcosystemIntegrationObservationContext context)
    {
        s_scanOrder.Add(context.Types.IsEmpty ? "<empty>" : context.Types[0].MetadataName);
        return
        [
            .. context.Types
                .Where(type => type.MetadataName ==
                    "Microsoft.Extensions.DependencyInjection.IServiceCollection")
                .Select(type => type.Classify(
                    IntegrationConceptCatalog.DependencyInjection,
                    "Dependency Injection")),
        ];
    }

    static ImmutableArray<EcosystemIntegrationClassification> SelectLogging(
        EcosystemIntegrationObservationContext context) =>
        [
            .. context.Types
                .Where(type => type.MetadataName.StartsWith(
                    "Microsoft.Extensions.Logging.",
                    StringComparison.Ordinal))
                .Select(type => type.Classify(IntegrationConceptCatalog.Logging, "Logging")),
        ];

    static ImmutableArray<EcosystemIntegrationClassification> UnselectedScanner(
        EcosystemIntegrationObservationContext context) =>
        throw new InvalidOperationException("An unselected scanner was invoked.");

    static ImmutableArray<EcosystemIntegrationClassification> FaultingScanner(
        EcosystemIntegrationObservationContext context) =>
        throw s_callbackFailure!;

    [Fact]
    public void RegistryRun_ScansEveryParticipantInOrderAndReusesSnapshots()
    {
        var version = new AssemblyBindingPolicyVersion();
        var policy = new TestBindingPolicy(version);
        List<string> acquisitionOrder = [];
        TestAssembly dependencyInjection = TestAssembly.Create(
            "DependencyInjectionIntegration",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy,
            () => acquisitionOrder.Add("dependency injection"));
        TestAssembly logging = TestAssembly.Create(
            "LoggingIntegration",
            "Microsoft.Extensions.Logging.CustomLogger",
            policy,
            () => acquisitionOrder.Add("logging"),
            additionalIntegrationTypeName:
                "OpenTelemetry.CustomTracer");
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    dependencyInjection.Participant,
                    logging.Participant,
                ]);
        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextIntegrationsQuery.Definition,
                    AssemblyContextIntegrationsQuery.Execute);
        Assert.Equal(
            InspectionCost.Unbounded,
            registry.CostOf(
                AssemblyContextIntegrationsQuery.Definition));

        AssemblyContextIntegrationsResult first =
            registry.Run(
                    [AssemblyContextIntegrationsQuery.Definition],
                    group)
                .Get(AssemblyContextIntegrationsQuery.Definition);
        AssemblyContextIntegrationsResult second =
            registry.Run(
                    [AssemblyContextIntegrationsQuery.Definition],
                    group)
                .Get(AssemblyContextIntegrationsQuery.Definition);

        Assert.True(first.IsComplete);
        Assert.True(second.IsComplete);
        Assert.Equal(
            ["dependency injection", "logging"],
            acquisitionOrder);
        Assert.Equal(1, dependencyInjection.OpenCount);
        Assert.Equal(1, logging.OpenCount);

        var dependencyInjectionResult =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                first.Assemblies[0]);
        AssertSubject(
            dependencyInjection,
            dependencyInjectionResult.Subject);
        Assert.Contains(
            dependencyInjectionResult.EcosystemSignals,
            signal =>
                signal.Integration
                == EcosystemIntegrationNames.DependencyInjection);
        Assert.Empty(
            dependencyInjectionResult.OpenTelemetrySignals);
        Assert.True(
            dependencyInjectionResult.Presence
                .HasDependencyInjectionSupport);
        Assert.Equal(
            1,
            dependencyInjectionResult.Presence.IntegrationCount);

        var loggingResult =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                first.Assemblies[1]);
        AssertSubject(logging, loggingResult.Subject);
        Assert.Contains(
            loggingResult.EcosystemSignals,
            signal =>
                signal.Integration
                == EcosystemIntegrationNames.Logging);
        Assert.Contains(
            loggingResult.OpenTelemetrySignals,
            signal =>
                signal.Name == "OpenTelemetry.CustomTracer");
        Assert.True(loggingResult.Presence.HasLoggingSupport);
        Assert.True(
            loggingResult.Presence.HasOpenTelemetrySupport);
        Assert.Equal(2, loggingResult.Presence.IntegrationCount);
    }

    [Fact]
    public void Execute_CarriesAcquisitionFailureBesideLaterResults()
    {
        var version = new AssemblyBindingPolicyVersion();
        var policy = new TestBindingPolicy(version);
        TestAssembly rejected = TestAssembly.Create(
            "RejectedIntegration",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy,
            selectedName: "DifferentIdentity");
        TestAssembly available = TestAssembly.Create(
            "AvailableIntegration",
            "Microsoft.Extensions.Logging.CustomLogger",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [rejected.Participant, available.Participant]);

        AssemblyContextIntegrationsResult result =
            AssemblyContextIntegrationsQuery.Execute(group);

        Assert.False(result.IsComplete);
        var rejectedResult =
            Assert.IsType<AssemblyIntegrationsEntry.Rejected>(
                result.Assemblies[0]);
        AssertSubject(rejected, rejectedResult.Subject);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejectedResult.Failure.Kind);

        var availableResult =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                result.Assemblies[1]);
        AssertSubject(available, availableResult.Subject);
        Assert.Contains(
            availableResult.EcosystemSignals,
            signal =>
                signal.Integration
                == EcosystemIntegrationNames.Logging);
        Assert.Equal(1, rejected.OpenCount);
        Assert.Equal(1, available.OpenCount);
    }

    [Fact]
    public void Execute_ReportsBudgetExhaustionAsIncompleteEntry()
    {
        var version = new AssemblyBindingPolicyVersion();
        var policy = new TestBindingPolicy(version);
        TestAssembly first = TestAssembly.Create(
            "FirstIntegration",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy);
        TestAssembly second = TestAssembly.Create(
            "SecondIntegration",
            "Microsoft.Extensions.Logging.CustomLogger",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [first.Participant, second.Participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = first.Bytes.Length,
                });

        AssemblyContextIntegrationsResult result =
            AssemblyContextIntegrationsQuery.Execute(group);

        Assert.False(result.IsComplete);
        Assert.IsType<AssemblyIntegrationsEntry.Available>(
            result.Assemblies[0]);
        var rejected =
            Assert.IsType<AssemblyIntegrationsEntry.Rejected>(
                result.Assemblies[1]);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            rejected.Failure.Kind);
    }

    [Fact]
    public void Execute_ComposesOpportunitiesFromTypedIntegrations()
    {
        var version = new AssemblyBindingPolicyVersion();
        var policy = new TestBindingPolicy(version);
        TestAssembly source = TestAssembly.Create(
            "CloudClient",
            "Amazon.S3.AmazonS3Client",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([source.Participant]);
        var scanned = Assert.IsType<AssemblyIntegrationsEntry.Available>(
            AssemblyContextIntegrationsQuery.Execute(group).Assemblies[0]);
        Assert.Empty(scanned.EcosystemSignals);

        var injected = new AssemblyContextIntegrationsResult(
            [
                new AssemblyIntegrationsEntry.Available(
                    scanned.Subject,
                    [
                        new EcosystemIntegrationSignalInfo(
                            EcosystemIntegrationNames.DependencyInjection,
                            "Injected",
                            "Injected.Registration"),
                    ],
                    [],
                    scanned.Presence),
            ]);
        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextIntegrationsQuery.Definition,
                    _ => injected)
                .Add(
                    AssemblyContextIntegrationOpportunitiesQuery.Definition,
                    AssemblyContextIntegrationOpportunitiesQuery.Execute,
                    AssemblyContextIntegrationsQuery.Definition);

        var opportunities =
            Assert.IsType<
                AssemblyIntegrationOpportunitiesEntry.Available>(
                    registry.Run(
                            [AssemblyContextIntegrationOpportunitiesQuery.Definition],
                            group)
                        .Get(
                            AssemblyContextIntegrationOpportunitiesQuery
                                .Definition)
                        .Assemblies[0]);

        Assert.Contains(
            opportunities.Opportunities,
            opportunity =>
                opportunity.Integration == EcosystemIntegrationNames.Aspire);
        IntegrationOpportunityInfo aspireOpportunity = Assert.Single(
            opportunities.Opportunities,
            opportunity =>
                opportunity.Integration == EcosystemIntegrationNames.Aspire);
        Assert.Same(
            IntegrationConceptCatalog.Aspire,
            aspireOpportunity.GetConcept());
        Assert.Same(
            IntegrationConceptCatalog.Opportunity,
            aspireOpportunity.GetProducerPolicy());
        Assert.DoesNotContain(
            opportunities.Opportunities,
            opportunity =>
                opportunity.Integration
                == EcosystemIntegrationNames.DependencyInjection);
    }

    [Fact]
    public void RegistryRun_OpportunityQueryUsesOneImmutableSnapshot()
    {
        var version = new AssemblyBindingPolicyVersion();
        var policy = new TestBindingPolicy(version);
        TestAssembly source = TestAssembly.Create(
            "CloudClient",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            policy,
            additionalIntegrationTypeName:
                "Amazon.S3.AmazonS3Client");
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([source.Participant]);
        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextIntegrationsQuery.Definition,
                    AssemblyContextIntegrationsQuery.Execute)
                .Add(
                    AssemblyContextIntegrationOpportunitiesQuery.Definition,
                    AssemblyContextIntegrationOpportunitiesQuery.Execute,
                    AssemblyContextIntegrationsQuery.Definition);

        Assert.Equal(
            [AssemblyContextIntegrationsQuery.Definition],
            registry.RequirementsOf(
                AssemblyContextIntegrationOpportunitiesQuery.Definition));
        Assert.Equal(
            InspectionCost.Unbounded,
            registry.CostOf(
                AssemblyContextIntegrationOpportunitiesQuery.Definition));

        InspectionQueryResults results = registry.Run(
            [AssemblyContextIntegrationOpportunitiesQuery.Definition],
            group);

        Assert.IsType<AssemblyIntegrationsEntry.Available>(
            results.Get(AssemblyContextIntegrationsQuery.Definition)
                .Assemblies[0]);
        var opportunities =
            Assert.IsType<
                AssemblyIntegrationOpportunitiesEntry.Available>(
                    results.Get(
                        AssemblyContextIntegrationOpportunitiesQuery
                            .Definition)
                    .Assemblies[0]);
        AssertSubject(source, opportunities.Subject);
        Assert.Contains(
            opportunities.Opportunities,
            opportunity =>
                opportunity.Integration == EcosystemIntegrationNames.Aspire);
        Assert.DoesNotContain(
            opportunities.Opportunities,
            opportunity =>
                opportunity.Integration
                == EcosystemIntegrationNames.DependencyInjection);
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public void OpportunityQuery_CarriesPrerequisiteRejectionBesideAvailableEntry()
    {
        var version = new AssemblyBindingPolicyVersion();
        var policy = new TestBindingPolicy(version);
        TestAssembly rejected = TestAssembly.Create(
            "RejectedOpportunity",
            "Npgsql.NpgsqlConnection",
            policy,
            selectedName: "DifferentIdentity");
        TestAssembly available = TestAssembly.Create(
            "AvailableOpportunity",
            "Npgsql.NpgsqlConnection",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [rejected.Participant, available.Participant]);
        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextIntegrationsQuery.Definition,
                    AssemblyContextIntegrationsQuery.Execute)
                .Add(
                    AssemblyContextIntegrationOpportunitiesQuery.Definition,
                    AssemblyContextIntegrationOpportunitiesQuery.Execute,
                    AssemblyContextIntegrationsQuery.Definition);

        AssemblyContextIntegrationOpportunitiesResult result =
            registry.Run(
                    [AssemblyContextIntegrationOpportunitiesQuery.Definition],
                    group)
                .Get(
                    AssemblyContextIntegrationOpportunitiesQuery.Definition);

        var rejectedResult =
            Assert.IsType<
                AssemblyIntegrationOpportunitiesEntry.Rejected>(
                    result.Assemblies[0]);
        AssertSubject(rejected, rejectedResult.Subject);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejectedResult.Failure.Kind);
        var availableResult =
            Assert.IsType<
                AssemblyIntegrationOpportunitiesEntry.Available>(
                    result.Assemblies[1]);
        AssertSubject(available, availableResult.Subject);
        Assert.Contains(
            availableResult.Opportunities,
            opportunity =>
                opportunity.Integration
                == EcosystemIntegrationNames.HealthChecks);
        Assert.Equal(1, rejected.OpenCount);
        Assert.Equal(1, available.OpenCount);
    }

    [Fact]
    public void Execute_CarriesBroadPresenceBeyondEvidenceRows()
    {
        var policy = new TestBindingPolicy(
            new AssemblyBindingPolicyVersion());
        TestAssembly dependencyInjection = TestAssembly.Create(
            "DependencyInjectionPresence",
            "Microsoft.Extensions.DependencyInjection.CustomThing",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [dependencyInjection.Participant]);

        AssemblyContextIntegrationsResult result =
            AssemblyContextIntegrationsQuery.Execute(group);

        var available =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                Assert.Single(result.Assemblies));
        Assert.DoesNotContain(
            available.EcosystemSignals,
            signal =>
                signal.Integration
                == EcosystemIntegrationNames.DependencyInjection);
        Assert.True(
            available.Presence.HasDependencyInjectionSupport);
        Assert.Equal(0, available.Presence.IntegrationCount);
    }

    [Fact]
    public void ExecuteParticipant_DoesNotReleaseTheReusableGroup()
    {
        var policy = new TestBindingPolicy(
            new AssemblyBindingPolicyVersion());
        TestAssembly integration = TestAssembly.Create(
            "ReusableIntegration",
            "Microsoft.Extensions.Logging.CustomLogger",
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([integration.Participant]);

        AssemblyIntegrationsEntry first =
            AssemblyContextIntegrationsQuery.ExecuteParticipant(
                group,
                integration.Participant);
        AssemblyContextIntegrationsResult second =
            AssemblyContextIntegrationsQuery.Execute(group);

        Assert.IsType<AssemblyIntegrationsEntry.Available>(first);
        Assert.True(second.IsComplete);
        Assert.Equal(1, integration.OpenCount);
    }

    [Fact]
    public void OpportunitiesExecuteParticipant_DoesNotReleaseTheReusableGroup()
    {
        var policy = new TestBindingPolicy(
            new AssemblyBindingPolicyVersion());
        TestAssembly source = TestAssembly.Create(
            "ReusableOpportunities",
            "Amazon.S3.AmazonS3Client",
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([source.Participant]);

        AssemblyIntegrationOpportunitiesEntry first =
            AssemblyContextIntegrationOpportunitiesQuery.ExecuteParticipant(
                group,
                source.Participant);
        AssemblyContextIntegrationsResult second =
            AssemblyContextIntegrationsQuery.Execute(group);

        Assert.IsType<AssemblyIntegrationOpportunitiesEntry.Available>(
            first);
        Assert.IsType<AssemblyIntegrationsEntry.Available>(
            Assert.Single(second.Assemblies));
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public void Execute_OpenTelemetryEvidenceDoesNotBroadenLegacyPresence()
    {
        var policy = new TestBindingPolicy(
            new AssemblyBindingPolicyVersion());
        TestAssembly internalTelemetry = TestAssembly.Create(
            "InternalTelemetryPresence",
            "OpenTelemetry.Internal.CustomTracer",
            policy);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [internalTelemetry.Participant]);

        AssemblyContextIntegrationsResult result =
            AssemblyContextIntegrationsQuery.Execute(group);

        var available =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                Assert.Single(result.Assemblies));
        Assert.Contains(
            available.OpenTelemetrySignals,
            signal =>
                signal.Name
                == "OpenTelemetry.Internal.CustomTracer");
        Assert.False(
            available.Presence.HasOpenTelemetrySupport);
        Assert.Equal(0, available.Presence.IntegrationCount);
    }

    static void AssertSubject(
        TestAssembly expected,
        AssemblyContextSubject actual)
    {
        Assert.Same(
            expected.Assembly.Registration,
            actual.Registration);
        Assert.Equal(expected.Assembly.Identity, actual.Identity);
        Assert.Same(
            expected.Assembly.Provenance,
            actual.Provenance);
    }

    sealed class TestAssembly
    {
        int _openCount;
        readonly Action? _onOpen;

        TestAssembly(
            byte[] bytes,
            ResolvedAssemblyReference assembly,
            TestBindingPolicy policy,
            Action? onOpen)
        {
            Bytes = bytes;
            Assembly = assembly;
            Participant =
                new AssemblyContextParticipant(assembly, policy);
            _onOpen = onOpen;
        }

        internal byte[] Bytes { get; }
        internal ResolvedAssemblyReference Assembly { get; }
        internal AssemblyContextParticipant Participant { get; }
        internal int OpenCount =>
            Volatile.Read(ref _openCount);

        internal static TestAssembly Create(
            string assemblyName,
            string integrationTypeName,
            TestBindingPolicy policy,
            Action? onOpen = null,
            string? selectedName = null,
            string? additionalIntegrationTypeName = null,
            bool publicType = true,
            bool invalidTypeName = false)
        {
            byte[] bytes =
                BuildAssembly(
                    assemblyName,
                    integrationTypeName,
                    additionalIntegrationTypeName,
                    publicType);
            AssemblyReferenceIdentity actualIdentity;
            using (var peReader =
                   new PEReader(new MemoryStream(bytes, writable: false)))
            {
                actualIdentity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        peReader.GetMetadataReader());
                if (invalidTypeName)
                {
                    MetadataReader reader = peReader.GetMetadataReader();
                    int nameOffset = peReader.PEHeaders.MetadataStartOffset
                        + reader.GetTableMetadataOffset(TableIndex.TypeDef)
                        + reader.GetTableRowSize(TableIndex.TypeDef)
                        + sizeof(uint);
                    Assert.True(reader.GetHeapSize(HeapIndex.String) < ushort.MaxValue);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(nameOffset, sizeof(ushort)),
                        ushort.MaxValue);
                }
            }

            var selectedIdentity = actualIdentity with
            {
                Name = selectedName ?? actualIdentity.Name,
            };
            TestAssembly? source = null;
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    selectedIdentity,
                    path: null,
                    () =>
                    {
                        Interlocked.Increment(
                            ref source!._openCount);
                        source._onOpen?.Invoke();
                        return new MemoryStream(
                            source.Bytes,
                            writable: false);
                    },
                    AssemblyResolutionProvenance.Local(
                        assemblyName));
            source = new TestAssembly(
                bytes,
                assembly,
                policy,
                onOpen);
            return source;
        }

        static byte[] BuildAssembly(
            string assemblyName,
            string integrationTypeName,
            string? additionalIntegrationTypeName,
            bool publicType)
        {
            var assemblyBuilder = new PersistedAssemblyBuilder(
                new AssemblyName(assemblyName),
                typeof(object).Assembly);
            ModuleBuilder module =
                assemblyBuilder.DefineDynamicModule(assemblyName);
            DefineType(integrationTypeName);
            if (additionalIntegrationTypeName is not null)
                DefineType(additionalIntegrationTypeName);

            using var stream = new MemoryStream();
            assemblyBuilder.Save(stream);
            return stream.ToArray();

            void DefineType(string typeName)
            {
                TypeBuilder type = module.DefineType(
                    typeName,
                    (publicType ? TypeAttributes.Public : TypeAttributes.NotPublic)
                    | TypeAttributes.Class);
                type.DefineDefaultConstructor(
                    MethodAttributes.Public);
                type.CreateType();
            }
        }
    }

    sealed class TestBindingPolicy(
        AssemblyBindingPolicyVersion version)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            version;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }
}
