using System.Collections.Immutable;
using System.Reflection;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace DotnetInspector.EcosystemLoading.Tests;

public sealed class EcosystemPopulationLoadingTests
{
    [Theory]
    [InlineData("ecosystem-loader.dotnet")]
    [InlineData("ecosystem-loader.aspnetcore")]
    [InlineData("ecosystem-loader.a1-b2")]
    public void LoaderIdsAcceptCanonicalValues(string value)
    {
        EcosystemPopulationLoaderId id =
            EcosystemPopulationLoaderId.Create(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(id, EcosystemPopulationLoaderId.Create(value));
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ecosystem.dotnet")]
    [InlineData("ecosystem-loader.Dotnet")]
    [InlineData("ecosystem-loader.-dotnet")]
    [InlineData("ecosystem-loader.dotnet-")]
    [InlineData("ecosystem-loader.dotnet--runtime")]
    [InlineData("ecosystem-loader.dotnet.runtime")]
    public void LoaderIdsRejectNonCanonicalValues(string value)
    {
        Assert.False(
            EcosystemPopulationLoaderId.TryCreate(value, out _));
        Assert.Throws<ArgumentException>(
            () => EcosystemPopulationLoaderId.Create(value));
    }

    [Fact]
    public void BindingRequiresOneTargetFreeStaticMethod()
    {
        EcosystemPopulationLoaderId id =
            EcosystemPopulationLoaderId.Create("ecosystem-loader.test");
        EcosystemPopulationLoaderBinding<TestInputs> binding =
            EcosystemPopulationLoaderBinding.Create<TestInputs>(
                id,
                LoadAsync);

        Assert.Same(id, binding.Id);

        int captured = 0;
        Assert.Throws<ArgumentException>(
            () => EcosystemPopulationLoaderBinding.Create<TestInputs>(
                id,
                request =>
                {
                    captured++;
                    return LoadAsync(request);
                }));
        Assert.Equal(0, captured);

        Func<
            EcosystemPopulationLoadRequest<TestInputs>,
            ValueTask<EcosystemPopulationLoaderReply>> combined = LoadAsync;
        combined += LoadAsync;
        Assert.Throws<ArgumentException>(
            () => EcosystemPopulationLoaderBinding.Create(id, combined));
    }

    [Fact]
    public async Task SelectionRequiresExactRegistrationCorrespondence()
    {
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        WorkspaceEcosystemRegistrationDeclaration retained =
            Declaration("ecosystem.test");
        WorkspaceEcosystemRegistrationDeclaration equalButDistinct =
            Declaration("ecosystem.test");
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                ImmutableArray.Create<WorkspaceRegistration>(
                    new WorkspaceRegistration.Ecosystem(retained))));
        WorkspaceRegistrationRevision revision = Read(workspace);

        var exact =
            new EcosystemPopulationLoaderCorrespondence<TestInputs>(
                retained,
                binding);
        var mismatch =
            new EcosystemPopulationLoaderCorrespondence<TestInputs>(
                equalButDistinct,
                binding);

        Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<TestInputs>>(
                exact.Select(
                    revision,
                    retained,
                    EcosystemPopulationDemand.WholePopulation.Instance));
        var rejected =
            Assert.IsType<EcosystemPopulationLoaderSelection.Rejected>(
                mismatch.Select(
                    revision,
                    retained,
                    EcosystemPopulationDemand.WholePopulation.Instance));
        Assert.Same(retained, rejected.Registration);
        Assert.Equal(
            "ecosystem-loader.registration-mismatch",
            Assert.Single(rejected.Diagnostics).Code);

        var unavailable =
            EcosystemPopulationLoaderSelection.UnavailableFor(
                revision,
                retained,
                EcosystemPopulationDemand.WholePopulation.Instance,
                [Diagnostic("ecosystem-loader.unavailable")]);
        Assert.Same(revision, unavailable.Revision);
        Assert.Same(retained, unavailable.Registration);
    }

    [Fact]
    public async Task BoundRequestInvokesOnceAndRetainsExactAssociation()
    {
        TestInputs inputs = new(LoadMode.CompletedNoMembers);
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture fixture =
            WorkspaceFixture.Create(binding);
        EcosystemPopulationLoadRequest<TestInputs> request =
            fixture.Request(
                inputs,
                TestContext.Current.CancellationToken);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(request));

        Assert.Equal(1, inputs.InvocationCount);
        Assert.Same(request.Snapshot, outcome.Receipt.Request);
        Assert.Same(
            fixture.Workspace.Identity,
            outcome.Receipt.Request.Workspace);
        Assert.Same(
            fixture.Revision,
            outcome.Receipt.Request.RegistrationRevision);
        Assert.Same(
            fixture.Declaration,
            outcome.Receipt.Request.Registration);
        Assert.Same(binding.Id, outcome.Receipt.Request.Loader);
        Assert.Same(inputs.Snapshot, outcome.Receipt.Request.Inputs);
        Assert.Equal(
            EcosystemPopulationLoadSettlementKind.Completed,
            outcome.Receipt.SettlementKind);
        Assert.Equal(
            EcosystemPopulationCompletionKind.NoMembers,
            outcome.Receipt.Completion!.Kind);
        Assert.False(
            outcome.Receipt.Children
                is EcosystemPopulationChildSettlement[]);
        Assert.False(
            outcome.Receipt.Diagnostics
                is EcosystemPopulationLoadDiagnostic[]);
        Assert.Empty(outcome.Owners.Libraries);
        await outcome.Owners.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(request)
                .AsTask());
        Assert.Equal(1, inputs.InvocationCount);
    }

    [Fact]
    public async Task CancellationBeforeInvocationDoesNotCallLoader()
    {
        TestInputs inputs = new(LoadMode.CompletedNoMembers);
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture fixture =
            WorkspaceFixture.Create(binding);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        EcosystemPopulationLoadRequest<TestInputs> request =
            fixture.Request(inputs, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(request)
                .AsTask());
        Assert.Equal(0, inputs.InvocationCount);
    }

    [Fact]
    public async Task CompletedOwnerTransfersExactlyOnce()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner = artifacts.CreateOwner("Contoso.Focus");
        TestInputs inputs = new(LoadMode.CompletedMembers)
        {
            Owner = owner,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        EcosystemPopulationLoadedLibraryReference reference =
            Assert.Single(outcome.Owners.Libraries);

        var transferred =
            Assert.IsType<EcosystemPopulationOwnerTakeOutcome.Transferred>(
                outcome.Owners.Take(reference.Reference));
        Assert.Same(owner, transferred.Owner);
        Assert.Equal(
            EcosystemPopulationLibraryRole.Focus,
            transferred.Library.Roles);
        Assert.IsType<
            EcosystemPopulationOwnerTakeOutcome.AlreadyTransferred>(
                outcome.Owners.Take(reference.Reference));

        await outcome.Owners.DisposeAsync();
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
        Assert.Equal(LibraryContentOwnerState.Active, owner.State);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task OwnerBatchRetirementFailureRemainsVisible()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        OwnerWithChild owned =
            artifacts.CreateOwnerWithChild("Contoso.BatchFailure");
        TestInputs inputs = new(LoadMode.CompletedMembers)
        {
            Owner = owned.Owner,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Task retirement = WhileBorrowed(
            owned.Child,
            () => outcome.Owners.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<AggregateException>(() => retirement);

        Assert.Equal(
            EcosystemPopulationOwnerBatchState.RetirementFailed,
            outcome.Owners.State);
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            owned.Owner.State);
        owned.Child.Dispose();
    }

    [Fact]
    public async Task CancellationAfterReplyRetiresUntransferredOwners()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Cancelled");
        using var cancellation = new CancellationTokenSource();
        TestInputs inputs = new(LoadMode.CancelAfterCompletedReply)
        {
            Owner = owner,
            Cancellation = cancellation,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(inputs, cancellation.Token))
                .AsTask());

        Assert.Equal(1, inputs.InvocationCount);
        Assert.Equal(LibraryContentOwnerState.Released, owner.State);
    }

    [Fact]
    public async Task CancellationCleanupFailureRemainsVisible()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        OwnerWithChild owned =
            artifacts.CreateOwnerWithChild("Contoso.CancelledFailure");
        using var cancellation = new CancellationTokenSource();
        TestInputs inputs = new(LoadMode.CancelAfterCompletedReply)
        {
            Owner = owned.Owner,
            Cancellation = cancellation,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        Task operation = WhileBorrowed(
            owned.Child,
            () => EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(inputs, cancellation.Token))
                .AsTask());
        OperationCanceledException failure =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => operation);

        Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            owned.Owner.State);
        owned.Child.Dispose();
    }

    [Fact]
    public async Task IncompleteMayTransferOnlyCompletedChildOwners()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Partial");
        TestInputs inputs = new(LoadMode.Incomplete)
        {
            Owner = owner,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Incomplete>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Equal(2, outcome.Receipt.Children.Count);
        Assert.Single(outcome.Owners.Libraries);
        await outcome.Owners.DisposeAsync();
        Assert.Equal(LibraryContentOwnerState.Released, owner.State);
    }

    [Fact]
    public async Task NonCompletedChildCannotCarryLibraryOwnership()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Invalid");
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        ChildEvidence child = Child(request, "child");
        EcosystemPopulationChildSettlement incomplete =
            request.ChildIncomplete(
                child.Request,
                child.Receipt);

        Assert.Throws<ArgumentException>(
            () => new EcosystemPopulationCompletedChild(
                incomplete,
                [
                    new(
                        owner,
                        EcosystemPopulationLibraryRole.Focus),
                ]));

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateOwnerCannotEnterOneLoadReply()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Duplicate");
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        var ownership = new EcosystemPopulationLibraryOwnership(
            owner,
            EcosystemPopulationLibraryRole.Focus);
        ChildEvidence evidence = Child(request, "child");
        EcosystemPopulationChildSettlement child =
            request.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.Members);

        Assert.Throws<ArgumentException>(
            () => new EcosystemPopulationCompletedChild(
                child,
                [ownership, ownership]));

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateChildIdentityCannotEnterOneLoadReply()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.DuplicateChild");
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        ChildEvidence evidence = Child(request, "child");
        EcosystemPopulationChildSettlement completed =
            request.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.Members);
        EcosystemPopulationChildSettlement incomplete =
            request.ChildIncomplete(
                evidence.Request,
                evidence.Receipt);
        var completedChild = new EcosystemPopulationCompletedChild(
            completed,
            [
                new(
                    owner,
                    EcosystemPopulationLibraryRole.Focus),
            ]);

        Assert.Throws<ArgumentException>(
            () => request.Incomplete(
                [completed, incomplete],
                [completedChild],
                [Diagnostic("duplicate-child")]));

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task ReplyFromAnotherRequestIsRejected()
    {
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        TestInputs firstInputs = new(LoadMode.ForeignReply);
        TestInputs secondInputs = new(LoadMode.CompletedNoMembers);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                firstInputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                secondInputs,
                TestContext.Current.CancellationToken);
        firstInputs.ForeignReply = second.Completed(
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign"),
                EcosystemPopulationCompletionKind.NoMembers),
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(first)
                .AsTask());
    }

    [Fact]
    public async Task ForeignPopulatedReplyRetiresItsLibraryOwner()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Foreign");
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        TestInputs firstInputs = new(LoadMode.ForeignReply);
        TestInputs secondInputs = new(LoadMode.CompletedMembers);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                firstInputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                secondInputs,
                TestContext.Current.CancellationToken);
        firstInputs.ForeignReply = second.Completed(
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign-members"),
                EcosystemPopulationCompletionKind.Satisfied),
            [CompletedChild(second, owner)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(first)
                .AsTask());

        Assert.Equal(LibraryContentOwnerState.Released, owner.State);
    }

    [Fact]
    public async Task ForeignReplyCleanupFailureRemainsVisible()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        OwnerWithChild owned =
            artifacts.CreateOwnerWithChild("Contoso.ForeignFailure");
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        TestInputs firstInputs = new(LoadMode.ForeignReply);
        TestInputs secondInputs = new(LoadMode.CompletedMembers);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                firstInputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                secondInputs,
                TestContext.Current.CancellationToken);
        firstInputs.ForeignReply = second.Completed(
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign-failure"),
                EcosystemPopulationCompletionKind.Satisfied),
            [CompletedChild(second, owned.Owner)]);

        Task operation = WhileBorrowed(
            owned.Child,
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(first)
                .AsTask());
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => operation);

        Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            owned.Owner.State);
        owned.Child.Dispose();
    }

    [Fact]
    public async Task CompletionEvidenceIsBoundToExactRequest()
    {
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationCompletionWitness foreign =
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign-completion"),
                EcosystemPopulationCompletionKind.NoMembers);

        Assert.Throws<ArgumentException>(
            () => first.Completed(foreign, []));

        ChildEvidence evidence = Child(second, "foreign");
        EcosystemPopulationChildSettlement foreignChild =
            second.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.NoMembers);
        var completedForeignChild =
            new EcosystemPopulationCompletedChild(foreignChild, []);
        Assert.Throws<ArgumentException>(
            () => first.Completed(
                first.Completion(
                    EcosystemPopulationCompletionIdentity.Create(
                        "test.local-completion"),
                    EcosystemPopulationCompletionKind.NoMembers),
                [completedForeignChild]));
    }

    [Fact]
    public void OwningResultsDeclareResourceOwnership()
    {
        Assert.NotNull(
            typeof(EcosystemPopulationOwnerBatch)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoadOutcome.Completed)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoadOutcome.Incomplete)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLibraryOwnership)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationCompletedChild)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationOwnerTakeOutcome.Transferred)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoaderReply.Completed)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoaderReply.Incomplete)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
    }

    [Fact]
    public async Task RetainedCollectionsAreNotMutableArrays()
    {
        EcosystemPopulationLoadDiagnostic diagnostic =
            Diagnostic("ecosystem-loader.test");
        WorkspaceEcosystemRegistrationDeclaration declaration =
            Declaration("ecosystem.test");
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                ImmutableArray.Create<WorkspaceRegistration>(
                    new WorkspaceRegistration.Ecosystem(declaration))));
        WorkspaceRegistrationRevision revision = Read(workspace);

        var unavailable =
            EcosystemPopulationLoaderSelection.UnavailableFor(
                revision,
                declaration,
                EcosystemPopulationDemand.WholePopulation.Instance,
                [diagnostic]);

        Assert.False(
            unavailable.Diagnostics
                is EcosystemPopulationLoadDiagnostic[]);
    }

    [Fact]
    public void PublicRequestSurfaceHasNoWorkspaceMutationAuthority()
    {
        Type request = typeof(EcosystemPopulationLoadRequest<TestInputs>);

        Assert.DoesNotContain(
            request.GetProperties(),
            property =>
                property.PropertyType == typeof(InspectionWorkspace));
        Assert.DoesNotContain(
            request.GetMethods(),
            method => method.GetParameters().Any(
                parameter =>
                    parameter.ParameterType == typeof(InspectionWorkspace)));
        Assert.DoesNotContain(
            typeof(EcosystemPopulationLoaderBinding<TestInputs>)
                .GetProperties(),
            property => typeof(Delegate).IsAssignableFrom(
                property.PropertyType));
    }

    [Fact]
    public async Task ChildEvidenceSurfaceIsResourceFreeAndExactlyBound()
    {
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> foreign =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationChildRequestIdentity childRequest =
            request.ChildRequest("child.request");
        EcosystemPopulationChildRequestIdentity foreignRequest =
            foreign.ChildRequest("foreign.request");
        EcosystemPopulationChildReceiptIdentity foreignReceipt =
            foreign.ChildReceipt(
                foreignRequest,
                "foreign.receipt");

        Assert.Throws<ArgumentException>(
            () => request.ChildIncomplete(
                childRequest,
                foreignReceipt));
        Assert.Throws<ArgumentException>(
            () => request.ChildReceipt(
                foreignRequest,
                "cross-request.receipt"));
        Assert.Throws<ArgumentException>(
            () => request.ChildIncomplete(
                foreignRequest,
                foreignReceipt));

        Type[] evidenceTypes =
        [
            typeof(EcosystemPopulationChildRequestIdentity),
            typeof(EcosystemPopulationChildReceiptIdentity),
            typeof(EcosystemPopulationChildSettlement),
        ];
        Assert.All(
            evidenceTypes,
            type =>
            {
                Assert.False(typeof(IDisposable).IsAssignableFrom(type));
                Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
                Assert.Null(
                    type.GetCustomAttribute<ResourceOwnershipAttribute>());
                Assert.DoesNotContain(
                    type.GetFields(
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic),
                    field =>
                        typeof(Delegate).IsAssignableFrom(field.FieldType)
                        || typeof(InspectionWorkspace).IsAssignableFrom(
                            field.FieldType)
                        || typeof(LibraryContentOwner).IsAssignableFrom(
                            field.FieldType));
            });
    }

    static EcosystemPopulationLoaderBinding<TestInputs> Binding() =>
        EcosystemPopulationLoaderBinding.Create<TestInputs>(
            EcosystemPopulationLoaderId.Create(
                "ecosystem-loader.test"),
            LoadAsync);

    static ValueTask<EcosystemPopulationLoaderReply> LoadAsync(
        EcosystemPopulationLoadRequest<TestInputs> request)
    {
        TestInputs inputs = request.Inputs;
        inputs.InvocationCount++;
        EcosystemPopulationLoaderReply reply = inputs.Mode switch
        {
            LoadMode.CompletedNoMembers =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.NoMembers),
                    []),
            LoadMode.CompletedMembers =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.Satisfied),
                    [CompletedChild(request, inputs.Owner!)]),
            LoadMode.CancelAfterCompletedReply =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.Satisfied),
                    [CompletedChild(request, inputs.Owner!)]),
            LoadMode.Incomplete => Incomplete(request),
            LoadMode.ForeignReply =>
                inputs.ForeignReply
                ?? throw new InvalidOperationException(
                    "The foreign reply was not configured."),
            _ => throw new InvalidOperationException(
                "Unknown test load mode."),
        };

        inputs.Cancellation?.Cancel();
        return ValueTask.FromResult(reply);
    }

    static EcosystemPopulationLoaderReply Incomplete(
        EcosystemPopulationLoadRequest<TestInputs> request)
    {
        EcosystemPopulationCompletedChild completed =
            CompletedChild(request, request.Inputs.Owner!);
        ChildEvidence evidence = Child(request, "second");
        EcosystemPopulationChildSettlement incomplete =
            request.ChildIncomplete(
                evidence.Request,
                evidence.Receipt);
        return request.Incomplete(
            [completed.Settlement, incomplete],
            [completed],
            [Diagnostic("ecosystem-loader.incomplete")]);
    }

    static EcosystemPopulationCompletedChild CompletedChild(
        EcosystemPopulationLoadRequest<TestInputs> request,
        LibraryContentOwner owner)
    {
        ChildEvidence evidence = Child(request, "child");
        EcosystemPopulationChildSettlement settlement =
            request.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.Members);
        return new EcosystemPopulationCompletedChild(
            settlement,
            [
                new(
                    owner,
                    EcosystemPopulationLibraryRole.Focus),
            ]);
    }

    static EcosystemPopulationCompletionWitness Completion(
        EcosystemPopulationLoadRequest<TestInputs> request,
        EcosystemPopulationCompletionKind kind) =>
        request.Completion(
            EcosystemPopulationCompletionIdentity.Create(
                kind == EcosystemPopulationCompletionKind.NoMembers
                    ? "test.no-members"
                    : "test.completed"),
            kind);

    static EcosystemPopulationLoadDiagnostic Diagnostic(string code) =>
        new(code, "Test diagnostic.");

    static ChildEvidence Child(
        EcosystemPopulationLoadRequest<TestInputs> parent,
        string name)
    {
        EcosystemPopulationChildRequestIdentity request =
            parent.ChildRequest($"{name}.request");
        return new(
            request,
            parent.ChildReceipt(
                request,
                $"{name}.receipt"));
    }

    static Task WhileBorrowed(
        ArtifactContentLease child,
        Func<Task> operation) =>
        Assert.IsType<ArtifactContentAccessOutcome<Task>.Accessed>(
            child.WithContent(
                (_, _) => operation(),
                TestContext.Current.CancellationToken))
            .Value;

    static WorkspaceEcosystemRegistrationDeclaration Declaration(
        string id) =>
        new(
            WorkspaceEcosystemRegistrationId.Create(id),
            ["Contoso"],
            [],
            []);

    static WorkspaceRegistrationRevision Read(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    enum LoadMode
    {
        CompletedNoMembers,
        CompletedMembers,
        CancelAfterCompletedReply,
        Incomplete,
        ForeignReply,
    }

    sealed class TestInputs(LoadMode mode) :
        IEcosystemPopulationLoadInputs
    {
        public LoadMode Mode { get; } = mode;
        public int InvocationCount { get; set; }
        public LibraryContentOwner? Owner { get; init; }
        public CancellationTokenSource? Cancellation { get; init; }
        public EcosystemPopulationLoaderReply? ForeignReply { get; set; }
        public EcosystemPopulationLoadInputSnapshot Snapshot { get; } =
            new(
                EcosystemPopulationOperationPolicyIdentity.Create(
                    "test-policy"),
                EcosystemPopulationCapabilityPlanIdentity.Create(
                    "test-capabilities"),
                EcosystemPopulationWorkIdentity.Create("test-work"));
    }

    sealed class WorkspaceFixture : IAsyncDisposable
    {
        readonly EcosystemPopulationLoaderSelection.Known<TestInputs> _known;

        WorkspaceFixture(
            InspectionWorkspace workspace,
            WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration declaration,
            EcosystemPopulationLoaderSelection.Known<TestInputs> known)
        {
            Workspace = workspace;
            Revision = revision;
            Declaration = declaration;
            _known = known;
        }

        public InspectionWorkspace Workspace { get; }
        public WorkspaceRegistrationRevision Revision { get; }
        public WorkspaceEcosystemRegistrationDeclaration Declaration { get; }

        public static WorkspaceFixture Create(
            EcosystemPopulationLoaderBinding<TestInputs> binding)
        {
            WorkspaceEcosystemRegistrationDeclaration declaration =
                EcosystemPopulationLoadingTests.Declaration(
                    "ecosystem.test");
            var workspace = new InspectionWorkspace(
                new WorkspacePlan(
                    ImmutableArray.Create<WorkspaceRegistration>(
                        new WorkspaceRegistration.Ecosystem(
                            declaration))));
            WorkspaceRegistrationRevision revision = Read(workspace);
            var correspondence =
                new EcosystemPopulationLoaderCorrespondence<TestInputs>(
                    declaration,
                    binding);
            var known =
                Assert.IsType<
                    EcosystemPopulationLoaderSelection.Known<TestInputs>>(
                        correspondence.Select(
                            revision,
                            declaration,
                            EcosystemPopulationDemand
                                .WholePopulation.Instance));
            return new WorkspaceFixture(
                workspace,
                revision,
                declaration,
                known);
        }

        public EcosystemPopulationLoadRequest<TestInputs> Request(
            TestInputs inputs,
            CancellationToken cancellationToken = default) =>
            _known.CreateRequest(inputs, cancellationToken);

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }

    sealed class ArtifactFixture : IAsyncDisposable
    {
        readonly ArtifactSetSession _session;
        readonly ArtifactQueryLease _queryLease;
        readonly ArtifactContentReference _reference;

        ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            ArtifactContentReference reference)
        {
            _session = session;
            _queryLease = queryLease;
            _reference = reference;
        }

        public static async Task<ArtifactFixture> CreateAsync()
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        ArtifactContribution contribution =
                            scope.Register(
                                new Provenance("test"),
                                _ => new MemoryStream(
                                    [42],
                                    writable: false));
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    [contribution],
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken: cancellationToken);
                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
                ArtifactQueryAuthorization authorization =
                    session.CreateQueryAuthorization();
                ArtifactQueryLease lease =
                    session.IssueLease(authorization);
                ArtifactContentReference reference =
                    session.GetCatalog(lease)
                        .Select(
                            descriptor =>
                                session.GetContentReference(
                                    descriptor.Identity,
                                    lease))
                        .Single();
                return new ArtifactFixture(session, lease, reference);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public LibraryContentOwner CreateOwner(string name)
            => CreateOwnerWithChild(name).Owner;

        public OwnerWithChild CreateOwnerWithChild(string name)
        {
            ManagedMetadataIdentity.Assembly identity =
                new(
                    new AssemblyReferenceIdentity(
                        name,
                        new Version(1, 0, 0, 0),
                        Culture: null,
                        PublicKeyToken: null));
            LibraryReference library = LibraryReference.CreateDirect(
                new LibraryAssemblyCorrespondence(
                    _reference,
                    identity,
                    _reference,
                    identity));
            ArtifactContentLease child =
                _session.IssueContentLease(_reference, _queryLease);
            return new OwnerWithChild(
                new LibraryContentOwner(
                    library,
                    [child]),
                child);
        }

        public async ValueTask DisposeAsync()
        {
            _queryLease.Dispose();
            await _session.DisposeAsync();
        }
    }

    sealed record Provenance(string Name) : IArtifactProvenance;
    readonly record struct OwnerWithChild(
        LibraryContentOwner Owner,
        ArtifactContentLease Child);
    readonly record struct ChildEvidence(
        EcosystemPopulationChildRequestIdentity Request,
        EcosystemPopulationChildReceiptIdentity Receipt);
}
