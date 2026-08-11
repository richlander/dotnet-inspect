using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextIntegrationsQueryTests
{
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
            string? additionalIntegrationTypeName = null)
        {
            byte[] bytes =
                BuildAssembly(
                    assemblyName,
                    integrationTypeName,
                    additionalIntegrationTypeName);
            AssemblyReferenceIdentity actualIdentity;
            using (var peReader =
                   new PEReader(new MemoryStream(bytes, writable: false)))
            {
                actualIdentity =
                    AssemblyReferenceIdentity.FromAssemblyDefinition(
                        peReader.GetMetadataReader());
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
            string? additionalIntegrationTypeName)
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
                    TypeAttributes.Public | TypeAttributes.Class);
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

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
    }
}
