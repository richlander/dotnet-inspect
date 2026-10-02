using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextDeclaredMethodPopulationQueryTests
{
    [Fact]
    public async Task PreparationIsSettledPerParticipantAndBinding()
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;
        MetadataTypeDefinitionBinding binding = Binding(path);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, path);
        AssemblyContextParticipant participant = group.Participants[0];

        var first = Assert.IsType<
            AssemblyContextDeclaredMethodPopulationPreparation.Ready>(
                AssemblyContextDeclaredMethodPopulationQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        binding,
                        TestContext.Current.CancellationToken));
        var second = Assert.IsType<
            AssemblyContextDeclaredMethodPopulationPreparation.Ready>(
                AssemblyContextDeclaredMethodPopulationQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        binding,
                        TestContext.Current.CancellationToken));
        MetadataDeclaredMethodPopulationResult count = first.Count();
        MetadataDeclaredMethodPopulationResult rows = first.Rows();

        Assert.Same(first, second);
        Assert.Equal(
            MetadataDeclaredMethodPopulationResultKind.Counted,
            count.Kind);
        Assert.Equal(
            MetadataDeclaredMethodPopulationResultKind.Read,
            rows.Kind);
        Assert.Equal(count.Count, rows.Count);
        Assert.Equal(count.Count, rows.Rows.Length);
        Assert.Same(first.Subject, second.Subject);
    }

    [Fact]
    public async Task BindingRejectionIsSettledOnce()
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;
        MetadataTypeDefinitionBinding binding = Binding(path);
        var foreign =
            new MetadataTypeDefinitionBinding(
                Guid.NewGuid(),
                binding.Definition);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, path);
        AssemblyContextParticipant participant = group.Participants[0];

        var first = Assert.IsType<
            AssemblyContextDeclaredMethodPopulationPreparation
                .BindingRejected>(
                AssemblyContextDeclaredMethodPopulationQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        foreign,
                        TestContext.Current.CancellationToken));
        AssemblyContextDeclaredMethodPopulationPreparation second =
            AssemblyContextDeclaredMethodPopulationQuery
                .PrepareParticipant(
                    group,
                    participant,
                    foreign,
                    TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(
            MetadataDeclaredMethodPopulationRejection
                .ModuleVersionIdMismatch,
            first.Reason);
    }

    [Fact]
    public async Task PreparedSourceClosesWithOwningGroup()
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;
        MetadataTypeDefinitionBinding binding = Binding(path);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = Group(workspace, path);
        var ready = Assert.IsType<
            AssemblyContextDeclaredMethodPopulationPreparation.Ready>(
                AssemblyContextDeclaredMethodPopulationQuery
                    .PrepareParticipant(
                        group,
                        group.Participants[0],
                        binding,
                        TestContext.Current.CancellationToken));

        group.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ready.Count());
        Assert.Throws<ObjectDisposedException>(() => ready.Rows());
    }

    [Fact]
    public async Task PreparedExecutionRetainsSourceUntilReleased()
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;
        MetadataTypeDefinitionBinding binding = Binding(path);
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group = Group(workspace, path);
        var ready = Assert.IsType<
            AssemblyContextDeclaredMethodPopulationPreparation.Ready>(
                AssemblyContextDeclaredMethodPopulationQuery
                    .PrepareParticipant(
                        group,
                        group.Participants[0],
                        binding,
                        TestContext.Current.CancellationToken));
        using AssemblyContextDeclaredMethodPopulationExecution execution =
            ready.OpenExecution();

        group.Dispose();

        MetadataDeclaredMethodPopulationResult count = execution.Count();
        Assert.Equal(
            MetadataDeclaredMethodPopulationResultKind.Counted,
            count.Kind);

        execution.Dispose();

        Assert.Throws<ObjectDisposedException>(() => execution.Count());
        Assert.Throws<ObjectDisposedException>(() => ready.Count());
    }

    [Fact]
    public async Task ParticipantFailureIsSettledOnce()
    {
        string path = typeof(System.Text.Json.JsonSerializer)
            .Assembly.Location;
        MetadataTypeDefinitionBinding binding = Binding(path);
        ResolvedAssemblyReference valid =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "declared-method preparation test"));
        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                valid.Identity,
                path: null,
                () => throw new IOException(
                    "Prepared participant failed."),
                AssemblyResolutionProvenance.Local(
                    "declared-method preparation failure"));
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(
            rejected,
            NoResolverAssemblyBindingPolicy.Instance);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var first = Assert.IsType<
            AssemblyContextDeclaredMethodPopulationPreparation
                .ParticipantRejected>(
                AssemblyContextDeclaredMethodPopulationQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        binding,
                        TestContext.Current.CancellationToken));
        AssemblyContextDeclaredMethodPopulationPreparation second =
            AssemblyContextDeclaredMethodPopulationQuery
                .PrepareParticipant(
                    group,
                    participant,
                    binding,
                    TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(
            CandidateOpenFailureKind.Unreadable,
            first.Failure.Kind);
    }

    private static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path) =>
        workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "declared-method preparation test")),
                    NoResolverAssemblyBindingPolicy.Instance),
            ]);

    private static MetadataTypeDefinitionBinding Binding(string path)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var defined = Assert.IsType<TypeDeclarationResult.Defined>(
            session.ProbeDeclaration(
                Assert.IsType<
                        MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.Create(
                            "System.Text.Json",
                            ["JsonSerializer"]))
                    .Name));
        return new(session.ModuleVersionId(), defined.Definition);
    }
}
