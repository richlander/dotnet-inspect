using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextExtensionCandidatesQueryTests
{
    [Fact]
    public void RequestRequiresCountOrRows()
    {
        string systemLinqPath = typeof(Enumerable).Assembly.Location;
        MetadataExtensionReceiverSelection receiver =
            ReceiverFromReference(
                systemLinqPath,
                "System.Runtime",
                "System.Collections.Generic",
                "IEnumerable`1");

        Assert.Throws<ArgumentException>(() =>
            new AssemblyContextExtensionCandidatePopulationRequest(
                receiver,
                MetadataOperationPolicy.Unbounded));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AssemblyContextExtensionCandidateRowsRequest(
                maximumRows: 0));
    }

    [Fact]
    public async Task ExecuteFindsEnumerableWhereAcrossRuntimeParticipants()
    {
        string coreLibraryPath = typeof(object).Assembly.Location;
        string systemLinqPath = typeof(Enumerable).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, coreLibraryPath, systemLinqPath);
        MetadataExtensionReceiverSelection receiver =
            ReceiverFromReference(
                systemLinqPath,
                "System.Runtime",
                "System.Collections.Generic",
                "IEnumerable`1");

        AssemblyContextResult<
            MetadataExtensionRelationPopulationOutcome> result =
            AssemblyContextExtensionCandidatesQuery.Execute(
                group,
                new(
                    receiver,
                    MetadataOperationPolicy.Unbounded,
                    count: new(),
                    rows: new(maximumRows: 512)),
                TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Assemblies.Length);
        Assert.Equal(
            typeof(object).Assembly.GetName().Name,
            result.Assemblies[0].Subject.Identity.Name);
        Assert.Equal(
            typeof(Enumerable).Assembly.GetName().Name,
            result.Assemblies[1].Subject.Identity.Name);

        MetadataExtensionRelationPopulationResult systemLinq =
            Available(result.Assemblies[1]);
        Assert.True(
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    systemLinq.Count).Value > 0);
        MetadataExtensionRelationPopulationRowsOutcome.Read rows =
            Assert.IsType<
                MetadataExtensionRelationPopulationRowsOutcome.Read>(
                    systemLinq.Rows);
        Assert.Contains(
            rows.Items.SelectMany(static row => row.Occurrences),
            static occurrence =>
                occurrence.DeclaringTypeName.ToMetadataFullName()
                    == "System.Linq.Enumerable"
                && occurrence.Member.CanonicalSignature.StartsWith(
                    "M:System.Linq.Enumerable.Where<",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task CountOnlyPreservesSourceNativeTerminal()
    {
        string systemLinqPath = typeof(Enumerable).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, systemLinqPath);

        AssemblyContextResult<
            MetadataExtensionRelationPopulationOutcome> result =
            AssemblyContextExtensionCandidatesQuery.Execute(
                group,
                new(
                    ReceiverFromReference(
                        systemLinqPath,
                        "System.Runtime",
                        "System.Collections.Generic",
                        "IEnumerable`1"),
                    MetadataOperationPolicy.Unbounded,
                    count: new()),
                TestContext.Current.CancellationToken);

        MetadataExtensionRelationPopulationResult population =
            Available(Assert.Single(result.Assemblies));
        Assert.IsType<
            MetadataExtensionRelationPopulationCountOutcome.Counted>(
                population.Count);
        Assert.Null(population.Rows);
    }

    [Fact]
    public async Task RowsRemainBoundedPerParticipant()
    {
        string systemLinqPath = typeof(Enumerable).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, systemLinqPath);

        AssemblyContextResult<
            MetadataExtensionRelationPopulationOutcome> result =
            AssemblyContextExtensionCandidatesQuery.Execute(
                group,
                new(
                    ReceiverFromReference(
                        systemLinqPath,
                        "System.Runtime",
                        "System.Collections.Generic",
                        "IEnumerable`1"),
                    MetadataOperationPolicy.Unbounded,
                    rows: new(maximumRows: 1)),
                TestContext.Current.CancellationToken);

        MetadataExtensionRelationPopulationResult population =
            Available(Assert.Single(result.Assemblies));
        Assert.Null(population.Count);
        MetadataExtensionRelationPopulationRowsOutcome.Read rows =
            Assert.IsType<
                MetadataExtensionRelationPopulationRowsOutcome.Read>(
                    population.Rows);
        Assert.Single(rows.Items);
        Assert.NotNull(rows.NextOrdinal);
    }

    [Fact]
    public async Task RejectedParticipantRemainsBesideAvailablePopulation()
    {
        string systemLinqPath = typeof(Enumerable).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(systemLinqPath);
        AssemblyReferenceIdentity identity;
        using (var reader = new PEReader(
                   new MemoryStream(bytes, writable: false)))
        {
            identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader.GetMetadataReader());
        }

        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                identity with { Name = "WrongIdentity" },
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local("rejected"));
        ResolvedAssemblyReference available =
            ResolvedAssemblyReference.CreateFromPath(
                systemLinqPath,
                AssemblyResolutionProvenance.Local("available"));
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(rejected, policy),
                    new AssemblyContextParticipant(available, policy),
                ]);

        AssemblyContextResult<
            MetadataExtensionRelationPopulationOutcome> result =
            AssemblyContextExtensionCandidatesQuery.Execute(
                group,
                new(
                    ReceiverFromReference(
                        systemLinqPath,
                        "System.Runtime",
                        "System.Collections.Generic",
                        "IEnumerable`1"),
                    MetadataOperationPolicy.Unbounded,
                    count: new()),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            AssemblyContextEntry<
                MetadataExtensionRelationPopulationOutcome>.Rejected>(
                    result.Assemblies[0]);
        MetadataExtensionRelationPopulationResult population =
            Available(result.Assemblies[1]);
        Assert.IsType<
            MetadataExtensionRelationPopulationCountOutcome.Counted>(
                population.Count);
    }

    private static MetadataExtensionRelationPopulationResult Available(
        AssemblyContextEntry<
            MetadataExtensionRelationPopulationOutcome> entry)
    {
        MetadataExtensionRelationPopulationOutcome outcome =
            Assert.IsType<
                AssemblyContextEntry<
                    MetadataExtensionRelationPopulationOutcome>.Available>(
                        entry).Value;
        return Assert.IsType<
            MetadataExtensionRelationPopulationOutcome.Available>(
                outcome).Result;
    }

    private static MetadataExtensionReceiverSelection ReceiverFromReference(
        string sourcePath,
        string referenceName,
        string @namespace,
        string typeName)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(sourcePath);
        AssemblyReferenceIdentity assembly =
            Assert.Single(
                session.AssemblyReferenceIdentities(),
                candidate => candidate.Name == referenceName);
        MetadataTypeDefinitionName type =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [typeName])).Name;
        return new(assembly, type);
    }

    private static AssemblyContextGroup CreateGroup(
        InspectionWorkspace workspace,
        params string[] paths)
    {
        var policy = new TestBindingPolicy();
        return workspace.CreateAssemblyContextGroup(
            [
                .. paths.Select(path =>
                    new AssemblyContextParticipant(
                        ResolvedAssemblyReference.CreateFromPath(
                            path,
                            AssemblyResolutionProvenance.Local(
                                "extension candidate query tests")),
                        policy)),
            ]);
    }

    private sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
