using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Catalog;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;
using Catalog = DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition(
    "Retained Workspace activation",
    DisableParallelization = true)]
public sealed class BrowserRetainedWorkspaceActivationCollection;

[Collection("Retained Workspace activation")]
[SupportedOSPlatform("browser")]
public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Fact]
    public void PackageSourcePatBindings_AreRequiredBeforeSourceAuthorization()
    {
        const string endpoint =
            "https://nuget.pkg.github.com/example/index.json";
        CompleteRestorationExecutionOptions options =
            BrowserCompleteRestorationOptions.Create();
        WorkspacePackageSourceDefinition[] definitions =
        [
            new(
                endpoint,
                WorkspacePackageSourceAuthentication.AuthenticationRequired),
        ];

        CompleteRestorationExecutionOptions denied =
            BrowserCompleteRestorationOptions.BindPackageSources(
                options,
                definitions,
                new Dictionary<
                    string,
                    PackageSourceCredential>(StringComparer.Ordinal));
        PackageSourceAuthorization deniedAuthorization =
            denied.ContextLoad.SourceAuthorization.AuthorizeSourcesFor(
                "Private.Package");
        Assert.Empty(deniedAuthorization.Sources);
        Assert.Contains(
            "requires an explicit Basic credential",
            deniedAuthorization.DenialReason,
            StringComparison.Ordinal);

        const string secret = "session-only-secret";
        CompleteRestorationExecutionOptions bound =
            BrowserCompleteRestorationOptions.BindPackageSources(
                options,
                definitions,
                new Dictionary<
                    string,
                    PackageSourceCredential>(StringComparer.Ordinal)
                {
                    [endpoint] = new("example-user", secret),
                });
        PackageSource source = Assert.Single(
            bound.ContextLoad.SourceAuthorization
                .AuthorizeSourcesFor("Private.Package")
                .Sources);

        Assert.Equal(endpoint, source.Name);
        Assert.Equal("example-user", source.Credential?.Username);
        Assert.Equal(secret, source.Credential?.Password);
        Assert.DoesNotContain(secret, source.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationRequiredSourceWithoutCredential_IsDeniedBeforeBrowserNetworkWork()
    {
        CompleteRestorationExecutionOptions options =
            BrowserCompleteRestorationOptions.Create();
        CompleteRestorationExecutionOptions denied =
            BrowserCompleteRestorationOptions.BindPackageSources(
                options,
                [
                    new(
                        "https://pkgs.dev.azure.com/example/_packaging/feed/nuget/v3/index.json",
                        WorkspacePackageSourceAuthentication.AuthenticationRequired),
                ],
                new Dictionary<
                    string,
                    PackageSourceCredential>(StringComparer.Ordinal));

        PackageSourceAuthorization authorization =
            denied.ContextLoad.SourceAuthorization.AuthorizeSourcesFor(
                "Private.Package");

        Assert.Empty(authorization.Sources);
        Assert.Contains(
            "unavailable in Browser/Wasm",
            authorization.DenialReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ActivationRequest_ToStringRedactsPackageSourceCredentials()
    {
        const string secret = "session-only-secret";
        var request = new BrowserRetainedWorkspaceActivationRequest(
            "workspace-1",
            "Private",
            "/private",
            "packet",
            new Dictionary<
                string,
                PackageSourceCredential>(StringComparer.Ordinal)
            {
                ["https://nuget.pkg.github.com/example/index.json"] =
                    new("example-user", secret),
            });

        Assert.DoesNotContain(secret, request.ToString(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PackageSourceCredentialJson_BindsEndpointUsernameAndPat()
    {
        const string secret = "session-only-secret";
        Dictionary<string, BrowserRetainedWorkspacePackageSourceCredential>
            wireCredentials = Assert.IsType<Dictionary<
                string,
                BrowserRetainedWorkspacePackageSourceCredential>>(
                    JsonSerializer.Deserialize(
                        $$"""
                        {
                          "https://nuget.pkg.github.com/example/index.json": {
                            "username": "example-user",
                            "pat": "{{secret}}"
                          }
                        }
                        """,
                        BrowserCatalogJsonContext.Default
                            .DictionaryStringBrowserRetainedWorkspacePackageSourceCredential));
        IReadOnlyDictionary<string, PackageSourceCredential> credentials =
            BrowserRetainedWorkspaceActivationService
                .BindPackageSourceCredentials(wireCredentials);

        PackageSourceCredential credential = Assert.Single(credentials).Value;
        Assert.Equal("example-user", credential.Username);
        Assert.Equal(secret, credential.Password);
        Assert.DoesNotContain(secret, credential.ToString());
    }

    public static TheoryData<string, string>
        InvalidPackageSourceCredentialJson =>
        new()
        {
            {
                "must be one JSON object",
                "[]"
            },
            {
                "contain only string 'username' and 'pat' properties",
                """
                {
                  "https://nuget.pkg.github.com/example/index.json": {
                    "username": "example-user",
                    "pat": "secret",
                    "token": "unexpected"
                  }
                }
                """
            },
            {
                "contain only string 'username' and 'pat' properties",
                """
                {
                  "https://nuget.pkg.github.com/example/index.json": {
                    "username": "example-user"
                  }
                }
                """
            },
            {
                "contain only string 'username' and 'pat' properties",
                """
                {
                  "https://nuget.pkg.github.com/example/index.json": {
                    "username": "first",
                    "username": "second",
                    "pat": "secret"
                  }
                }
                """
            },
            {
                "is duplicated",
                """
                {
                  "https://private.example/index.json": {
                    "username": "first",
                    "pat": "secret"
                  },
                  "https://private.example/index.json": {
                    "username": "second",
                    "pat": "secret"
                  }
                }
                """
            },
            {
                "must be one JSON object",
                CredentialJson(
                    pat: new string('[', 8)
                        + "\"secret\""
                        + new string(']', 8),
                    patIsJson: true)
            },
            {
                "browser transport limit",
                CredentialJson(
                    pat: new string(
                        'x',
                        BrowserRetainedWorkspaceActivationService
                            .PackageSourceCredentialsJsonLengthLimit))
            },
            {
                "too many source entries",
                JsonSerializer.Serialize(
                    Enumerable.Range(
                            0,
                            WorkspaceSharePacketCodec.MaxPackageSources + 1)
                        .ToDictionary(
                            index =>
                                $"https://source-{index}.example/index.json",
                            _ =>
                                new BrowserRetainedWorkspacePackageSourceCredential(
                                    "example-user",
                                    "secret"),
                            StringComparer.Ordinal),
                    BrowserCatalogJsonContext.Default
                        .DictionaryStringBrowserRetainedWorkspacePackageSourceCredential)
            },
            {
                "endpoint 'http://private.example/index.json' is invalid",
                CredentialJson(endpoint: "http://private.example/index.json")
            },
            {
                "invalid username",
                CredentialJson(username: " ")
            },
            {
                "invalid PAT length",
                CredentialJson(pat: "")
            },
            {
                "invalid PAT length",
                CredentialJson(pat: new string('x', 64 * 1024 + 1))
            },
            {
                "is duplicated",
                """
                {
                  "https://PRIVATE.EXAMPLE/index.json": {
                    "username": "first",
                    "pat": "secret"
                  },
                  "https://private.example/index.json": {
                    "username": "second",
                    "pat": "secret"
                  }
                }
                """
            },
        };

    [Theory]
    [MemberData(nameof(InvalidPackageSourceCredentialJson))]
    public async Task PackageSourceCredentialJson_RejectsUnauthenticatedShapes(
        string expectedMessage,
        string json)
    {
        Catalog.BrowserRetainedWorkspacePreparationResult preparation =
            Assert.IsType<Catalog.BrowserRetainedWorkspacePreparationResult>(
                JsonSerializer.Deserialize(
                    await CatalogExports
                        .PrepareRetainedWorkspaceDefinitionWithCredentials(
                            "workspace-1",
                            "Private",
                            "/private",
                            "packet",
                            json),
                    BrowserCatalogJsonContext.Default
                        .BrowserRetainedWorkspacePreparationResult));
        Assert.Equal("failed", preparation.Status);
        Assert.Equal("InvalidRequest", preparation.Failure?.Kind);
        Assert.Contains(
            expectedMessage,
            preparation.Failure?.Message,
            StringComparison.Ordinal);

        Catalog.BrowserRetainedWorkspaceActivationResult activation =
            Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
                JsonSerializer.Deserialize(
                    await CatalogExports
                        .ActivateRetainedWorkspaceDefinitionWithCredentials(
                            "workspace-1",
                            "Private",
                            "/private",
                            "packet",
                            json),
                    BrowserCatalogJsonContext.Default
                        .BrowserRetainedWorkspaceActivationResult));
        Assert.Equal("failed", activation.Status);
        Assert.Equal("InvalidRequest", activation.Failure?.Kind);
        Assert.Equal(preparation.Failure, activation.Failure);
    }

    static string CredentialJson(
        string endpoint = "https://private.example/index.json",
        string username = "example-user",
        string pat = "secret",
        bool patIsJson = false)
    {
        if (patIsJson)
        {
            return $$"""
                {
                  "{{endpoint}}": {
                    "username": "{{username}}",
                    "pat": {{pat}}
                  }
                }
                """;
        }

        return JsonSerializer.Serialize(
            new Dictionary<
                string,
                BrowserRetainedWorkspacePackageSourceCredential>(
                    StringComparer.Ordinal)
            {
                [endpoint] = new(username, pat),
            },
            BrowserCatalogJsonContext.Default
                .DictionaryStringBrowserRetainedWorkspacePackageSourceCredential);
    }

    [Fact]
    public async Task A_B_A_RestoresWholeWorkspaceAndSelectedPackage()
    {
        const string foo = "FooPackage";
        const string bar = "BarPackage";
        const string baz = "BazPackage";
        CompleteRestorationExecutionOptions options =
            await MultiPackageOptionsAsync();
        string firstPacket = Packet([foo, bar], selectedIndex: 1);
        string secondPacket = Packet([baz, bar], selectedIndex: 0);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspacePosting first =
            await ActivateAsync(owner, "workspace-1", firstPacket);
        NavigationEffectAuthority firstAuthority =
            Assert.IsType<NavigationEffectAuthority>(
                first.Navigation.Authority);
        using WorkspaceRealizationOperationLease firstOperation =
            await EnterAsync(owner, "workspace-1");
        AssertWorkspace(
            first,
            firstOperation,
            [foo, bar],
            selectedIndex: 1);
        BrowserRetainedWorkspacePosting second =
            await ActivateAsync(owner, "workspace-2", secondPacket);
        using (WorkspaceRealizationOperationLease secondOperation =
            await EnterAsync(owner, "workspace-2"))
        {
            AssertWorkspace(
                second,
                secondOperation,
                [baz, bar],
                selectedIndex: 0);
        }
        Assert.False(
            owner.ValidateNavigationAuthority(
                first.RealizationId,
                first.PublicationOrdinal,
                firstAuthority));

        Assert.NotNull(second.Predecessor);
        Task<BrowserRetainedWorkspaceSettlementResult> observation =
            owner.ObserveSettlementAsync(
                second.Predecessor.SettlementId,
                TestContext.Current.CancellationToken);
        Assert.False(observation.IsCompleted);
        firstOperation.Dispose();
        var settled = Assert.IsType<
            BrowserRetainedWorkspaceSettlementResult.Settled>(
                await observation);
        Assert.True(settled.Settlement.Succeeded);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.Replaced,
            settled.Settlement.Reason);

        BrowserRetainedWorkspacePosting third =
            await ActivateAsync(owner, "workspace-1", firstPacket);
        using WorkspaceRealizationOperationLease thirdOperation =
            await EnterAsync(owner, "workspace-1");
        AssertWorkspace(
            third,
            thirdOperation,
            [foo, bar],
            selectedIndex: 1);
        Assert.NotEqual(first.RealizationId, third.RealizationId);
        Assert.NotEqual(second.RealizationId, third.RealizationId);
        Assert.True(
            first.PublicationOrdinal < second.PublicationOrdinal
            && second.PublicationOrdinal < third.PublicationOrdinal);
        Assert.Equal(
            "workspace-1",
            owner.Active!.RetainedDefinitionId);
    }

    [Fact]
    public async Task ActiveSelection_IsTypedNoEffect()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting first =
            await ActivateAsync(owner, "a", packet);
        NavigationEffectAuthority authority =
            Assert.IsType<NavigationEffectAuthority>(
                first.Navigation.Authority);

        var noEffect = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.NoEffect>(
                await owner.ActivateAsync(
                    Request("a", packet),
                    TestContext.Current.CancellationToken));

        Assert.Equal(first.RealizationId, noEffect.Posting.RealizationId);
        Assert.Equal(
            first.PublicationOrdinal,
            noEffect.Posting.PublicationOrdinal);
        Assert.Same(first.Navigation, noEffect.Posting.Navigation);
        Assert.True(
            owner.ValidateNavigationAuthority(
                first.RealizationId,
                first.PublicationOrdinal,
                authority));
        Assert.Equal(1, owner.Capacity.Charged);
    }

    [Fact]
    public async Task InitialNavigationAuthority_RequiresExactPostingTuple()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(owner, "a", packet);
        NavigationEffectAuthority authority =
            Assert.IsType<NavigationEffectAuthority>(
                posting.Navigation.Authority);

        Assert.False(
            owner.ValidateNavigationAuthority(
                "workspace-realization-other",
                posting.PublicationOrdinal,
                authority));
        Assert.Equal(
            NavigationAuthorityResult.InvalidAuthority,
            owner.RecordConsumerPosting(
                posting.RealizationId,
                posting.PublicationOrdinal + 1,
                authority));
        Assert.True(
            owner.ValidateNavigationAuthority(
                posting.RealizationId,
                posting.PublicationOrdinal,
                authority));
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            owner.RecordConsumerPosting(
                posting.RealizationId,
                posting.PublicationOrdinal,
                authority));
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            owner.Acknowledge(
                posting.RealizationId,
                posting.PublicationOrdinal,
                authority));
        Assert.False(
            owner.ValidateNavigationAuthority(
                posting.RealizationId,
                posting.PublicationOrdinal,
                authority));
    }

    [Fact]
    public async Task ReplacementPublishesPredecessorNavigationCleanupFailure()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting first =
            await ActivateAsync(owner, "a", packet);
        NavigationEffectAuthority authority =
            Assert.IsType<NavigationEffectAuthority>(
                first.Navigation.Authority);
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            owner.RecordConsumerPosting(
                first.RealizationId,
                first.PublicationOrdinal,
                authority));
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            owner.Acknowledge(
                first.RealizationId,
                first.PublicationOrdinal,
                authority));
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreparation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<NavigationPreparation> ThrowDuringRetirement(
            NavigationEvaluationRequest _,
            CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    static () => throw new InvalidOperationException(
                        "retirement cleanup failed"));
            Task cancellation = Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            started.SetResult();
            try
            {
                await cancellation;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await releasePreparation.Task;
                throw;
            }
            throw new InvalidOperationException(
                "Retirement cancellation did not stop preparation.");
        }

        Task<NavigationConsumerResult?> maintenance =
            first.NavigationState.RefreshAsync(
                ThrowDuringRetirement,
                TestContext.Current.CancellationToken).AsTask();
        await started.Task;

        BrowserRetainedWorkspacePosting replacement;
        try
        {
            replacement = await ActivateAsync(owner, "b", packet);
        }
        finally
        {
            releasePreparation.SetResult();
        }

        Assert.Contains(
            "retirement cleanup failed",
            Assert.IsType<BrowserRetainedWorkspaceCleanupEvidence>(
                replacement.Cleanup).Message,
            StringComparison.Ordinal);
        Assert.False(
            owner.ValidateNavigationAuthority(
                first.RealizationId,
                first.PublicationOrdinal,
                authority));
        Assert.Null(await maintenance);
    }

    [Fact]
    public async Task InvalidAndLegacyPackets_PreserveIncumbentWithoutCharge()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting incumbent =
            await ActivateAsync(owner, "a", packet);

        var invalid = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await owner.ActivateAsync(
                    Request("invalid", "not-a-packet"),
                    TestContext.Current.CancellationToken));
        Assert.IsType<CompleteRestorationFailure.InvalidPacket>(
            invalid.Failure);
        Assert.Equal(1, owner.Capacity.Charged);

        var legacy = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await owner.ActivateAsync(
                    Request("legacy", LegacyPacket()),
                    TestContext.Current.CancellationToken));
        Assert.IsType<CompleteRestorationFailure.UnsupportedVersion>(
            legacy.Failure);
        Assert.Equal(1, owner.Capacity.Charged);
        Assert.Equal(
            incumbent.RealizationId,
            owner.Active!.RealizationId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CommittedPacket_RemainsActivatable(int version)
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet(version);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);

        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(owner, $"format-{version}", packet);

        Assert.Equal(
            version,
            WorkspaceSharePacketCodec.Decode(
                packet,
                TestContext.Current.CancellationToken).FormatVersion);
        Assert.Equal(packet, posting.CanonicalPacket);
    }

    [Fact]
    public async Task NewSelection_SupersedesBlockedPreparation()
    {
        CompleteRestorationExecutionOptions baseline = await OptionsAsync();
        int projectionCount = 0;
        BrowserRetainedWorkspaceActivationOwner? owner = null;
        Task<BrowserRetainedWorkspaceActivationResult>? replacement = null;
        string packet = Packet();
        CompleteRestorationExecutionOptions options = baseline with
        {
            Projection = request =>
            {
                if (Interlocked.Increment(ref projectionCount) == 1)
                {
                    replacement = owner!.ActivateAsync(
                        Request("b", packet),
                        TestContext.Current.CancellationToken);
                }
                return CompleteRestorationProjections.Classify(request);
            },
        };
        await using var activationOwner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        owner = activationOwner;

        BrowserRetainedWorkspaceActivationResult first =
            await activationOwner.ActivateAsync(
                Request("a", packet),
                TestContext.Current.CancellationToken);

        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            first);
        Assert.NotNull(replacement);
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await replacement);
        Assert.Equal("b", activated.Posting.RetainedDefinitionId);
        Assert.Equal("b", activationOwner.Active!.RetainedDefinitionId);
    }

    [Fact]
    public async Task PreparedCandidateRequiresConsumerCommitBeforeCutover()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting incumbent =
            await ActivateAsync(owner, "a", packet);

        BrowserRetainedWorkspaceActivationSession session =
            owner.BeginActivation(
                Request("b", packet),
                TestContext.Current.CancellationToken);
        var prepared = Assert.IsType<
            BrowserRetainedWorkspacePreparationResult.Prepared>(
                await session.Preparation);

        Assert.Equal("b", prepared.Posting.RetainedDefinitionId);
        Assert.Same(incumbent, owner.Active);
        Assert.False(session.Activation.IsCompleted);
        Assert.True(session.Commit());
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await session.Activation);
        Assert.Same(activated.Posting, owner.Active);
        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Completed>(
                session.Complete(succeeded: true, failure: null));
    }

    [Fact]
    public async Task RejectedPreparedCandidatePreservesIncumbent()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting incumbent =
            await ActivateAsync(owner, "a", packet);

        BrowserRetainedWorkspaceActivationSession session =
            owner.BeginActivation(
                Request("b", packet),
                TestContext.Current.CancellationToken);
        Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(
            await session.Preparation);
        Assert.True(session.Cancel());
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            await session.Activation);

        Assert.Same(incumbent, owner.Active);
        Assert.Equal(1, owner.Capacity.Charged);
        Assert.False(session.Commit());
        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Unavailable>(
                session.Complete(succeeded: true, failure: null));
    }

    [Fact]
    public async Task CommitCompletionKeepsTransitionOwned()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        using var cancellation = new CancellationTokenSource();

        BrowserRetainedWorkspaceActivationSession session =
            owner.BeginActivation(
                Request("a", packet),
                cancellation.Token);
        bool committed = session.Commit();
        Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(
            await session.Preparation);
        Assert.True(committed || session.Commit());
        cancellation.Cancel();
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Activated>(
            await session.Activation);

        BrowserRetainedWorkspaceActivationSession blocked =
            owner.BeginActivation(
                Request("b", packet),
                TestContext.Current.CancellationToken);
        var failed = Assert.IsType<
            BrowserRetainedWorkspacePreparationResult.Failed>(
                await blocked.Preparation);
        Assert.Contains(
            "awaiting consumer completion",
            failed.Failure.Message,
            StringComparison.Ordinal);
        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Completed>(
                session.Complete(succeeded: true, failure: null));
        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Unavailable>(
                session.Complete(succeeded: true, failure: null));

        BrowserRetainedWorkspacePosting replacement =
            await ActivateAsync(owner, "b", packet);
        Assert.Equal("b", replacement.RetainedDefinitionId);
    }

    [Fact]
    public async Task CompletionFailureCannotRestorePredecessor()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting incumbent =
            await ActivateAsync(owner, "a", packet);
        NavigationEffectAuthority incumbentAuthority =
            Assert.IsType<NavigationEffectAuthority>(
                incumbent.Navigation.Authority);

        BrowserRetainedWorkspaceActivationSession session =
            owner.BeginActivation(
                Request("b", packet),
                TestContext.Current.CancellationToken);
        Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(
            await session.Preparation);
        Assert.True(session.Commit());
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await session.Activation);
        NavigationEffectAuthority successorAuthority =
            Assert.IsType<NavigationEffectAuthority>(
                activated.Posting.Navigation.Authority);
        var completed = Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Completed>(
                session.Complete(
                    succeeded: false,
                    failure: "Injected consumer failure."));

        Assert.False(completed.Succeeded);
        Assert.Equal("Injected consumer failure.", completed.Failure);
        Assert.Same(activated.Posting, owner.Active);
        Assert.Equal("b", owner.Active!.RetainedDefinitionId);
        Assert.False(
            owner.ValidateNavigationAuthority(
                activated.Posting.RealizationId,
                activated.Posting.PublicationOrdinal,
                successorAuthority));
        Assert.False(
            owner.ValidateNavigationAuthority(
                incumbent.RealizationId,
                incumbent.PublicationOrdinal,
                incumbentAuthority));
        Assert.NotNull(activated.Posting.Predecessor);
        Assert.IsType<BrowserRetainedWorkspaceSettlementResult.Settled>(
            await owner.ObserveSettlementAsync(
                activated.Posting.Predecessor.SettlementId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeClosesCommittedActivationAwaitingCompletion()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspaceActivationSession session =
            owner.BeginActivation(
                Request("a", packet),
                TestContext.Current.CancellationToken);
        Assert.IsType<BrowserRetainedWorkspacePreparationResult.Prepared>(
            await session.Preparation);
        Assert.True(session.Commit());
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Activated>(
            await session.Activation);

        await owner.DisposeAsync();

        Assert.Null(owner.Active);
        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Unavailable>(
                session.Complete(succeeded: true, failure: null));
    }

    [Fact]
    public async Task SoleActiveDeactivationRequiresMatchingConsumerCompletion()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateAsync(owner, "a", packet);

        var deactivated = Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.Deactivated>(
                await owner.BeginDeactivationAsync(
                    "a",
                    TestContext.Current.CancellationToken));
        Assert.Null(owner.Active);

        var blocked = Assert.IsType<
            BrowserRetainedWorkspacePreparationResult.Failed>(
                await owner.BeginActivation(
                        Request("b", packet),
                        TestContext.Current.CancellationToken)
                    .Preparation);
        Assert.Contains(
            "still draining",
            blocked.Failure.Message,
            StringComparison.Ordinal);
        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Unavailable>(
                owner.CompleteConsumerDeactivation(
                    "workspace-deactivation-wrong",
                    succeeded: true,
                    failure: null));

        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Completed>(
                owner.CompleteConsumerDeactivation(
                    deactivated.CompletionReceipt,
                    succeeded: true,
                    failure: null));
        Assert.IsType<
            BrowserRetainedWorkspaceConsumerCompletionResult.Unavailable>(
                owner.CompleteConsumerDeactivation(
                    deactivated.CompletionReceipt,
                    succeeded: true,
                    failure: null));

        BrowserRetainedWorkspacePosting replacement =
            await ActivateAsync(owner, "b", packet);
        Assert.Equal("b", replacement.RetainedDefinitionId);
    }

    [Fact]
    public async Task SoleActiveDeactivation_ClosesAndReopensHost()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(owner, "a", packet);
        NavigationEffectAuthority authority =
            Assert.IsType<NavigationEffectAuthority>(
                posting.Navigation.Authority);

        var deactivated = Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.Deactivated>(
                await owner.DeactivateAsync(
                    "a",
                    TestContext.Current.CancellationToken));
        Assert.True(deactivated.Settlement.Succeeded);
        Assert.Null(owner.Active);
        Assert.Equal(0, owner.Capacity.Charged);
        Assert.False(
            owner.ValidateNavigationAuthority(
                posting.RealizationId,
                posting.PublicationOrdinal,
                authority));

        BrowserRetainedWorkspacePosting replacement =
            await ActivateAsync(owner, "b", packet);
        Assert.Equal("b", replacement.RetainedDefinitionId);
    }

    [Fact]
    public async Task CleanupFailedDeactivation_RemovesActiveAuthority()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateAsync(owner, "a", packet);
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, "a"))
        {
            RegisterThrowingResource(operation.Workspace);
        }

        var failed = Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.CleanupFailed>(
                await owner.DeactivateAsync(
                    "a",
                    TestContext.Current.CancellationToken));

        Assert.False(failed.Settlement.Succeeded);
        Assert.Null(owner.Active);
        Assert.Single(owner.Capacity.FailedSettlements);
        Assert.IsType<
            WorkspaceRealizationOperationAdmission.Unavailable>(
                await owner.EnterOperationAsync(
                    "a",
                    TestContext.Current.CancellationToken));
        AggregateException cleanup = await Assert.ThrowsAsync<
            AggregateException>(
                () => owner.DisposeAsync().AsTask());
        Assert.IsType<WorkspaceRealizationSettlementException>(
            Assert.Single(cleanup.InnerExceptions));
    }

    [Fact]
    public async Task DeactivationDrain_BlocksReplacementHostAdmission()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        string packet = Packet();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateAsync(owner, "a", packet);
        WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, "a");

        Task<BrowserRetainedWorkspaceDeactivationResult> deactivation =
            owner.DeactivateAsync(
                "a",
                TestContext.Current.CancellationToken);
        await Task.Yield();
        Assert.False(deactivation.IsCompleted);
        var unavailable = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await owner.ActivateAsync(
                    Request("b", packet),
                    TestContext.Current.CancellationToken));
        Assert.IsType<CompleteRestorationFailure.HostConstructionFailed>(
            unavailable.Failure);
        Assert.Equal(1, owner.Capacity.Charged);

        operation.Dispose();
        Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.Deactivated>(
                await deactivation);
        _ = await ActivateAsync(owner, "b", packet);
    }

    static BrowserRetainedWorkspaceActivationRequest Request(
        string id,
        string packet) =>
        new(id, $"Workspace {id}", $"/workspace/{id}", packet);

    static async Task<BrowserRetainedWorkspacePosting> ActivateAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string id,
        string packet)
    {
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await owner.ActivateAsync(
                    Request(id, packet),
                    TestContext.Current.CancellationToken));
        Assert.Equal(packet, activated.Posting.CanonicalPacket);
        return activated.Posting;
    }

    static void AssertWorkspace(
        BrowserRetainedWorkspacePosting posting,
        WorkspaceRealizationOperationLease operation,
        IReadOnlyList<string> expectedPackages,
        int selectedIndex)
    {
        Assert.Equal(
            [.. expectedPackages.Order(StringComparer.Ordinal)],
            [.. operation.Scope.Packages
                .Select(package => package.Occurrence.Package.PackageId)
                .Order(StringComparer.Ordinal)]);
        Assert.Equal(
            expectedPackages[selectedIndex],
            Assert.Single(
                posting.Navigation.Snapshot.Packages,
                package => package.IsCurrent).PackageId);
        Assert.Equal(
            expectedPackages.Count,
            posting.Packages.Length);
        Assert.Equal(
            posting.Navigation.Snapshot.Packages
                .Select(package => package.Subject.Id),
            posting.Packages
                .Select(package => package.ConsumerPackageSubjectId));
        Assert.Equal(
            expectedPackages[selectedIndex],
            operation.Scope.Packages[selectedIndex]
                .Occurrence.Package.PackageId);
    }

    static async ValueTask<WorkspaceRealizationOperationLease> EnterAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string id)
    {
        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                id,
                TestContext.Current.CancellationToken);
        return Assert.IsType<
            WorkspaceRealizationOperationAdmission.Admitted>(
                admission).Lease;
    }

    static string Packet(
        int schemaVersion = InspectionDefinitionSchema.Version3) =>
        Packet(
            ["System.Text.Json"],
            selectedIndex: 0,
            packageVersion: "9.0.4",
            framework: "net9.0",
            schemaVersion);

    static string Packet(
        IReadOnlyList<string> packageIds,
        int selectedIndex,
        string packageVersion = "11.0.0-preview.7.26381.103",
        string framework = "net10.0",
        int schemaVersion = InspectionDefinitionSchema.Version3)
    {
        if ((uint)selectedIndex >= (uint)packageIds.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        var registry = new InspectionDefinitionRegistry();
        var packages = packageIds
            .Select(packageId =>
                new DefinitionMemberCoordinate.PackageCoordinate(
                    packageId,
                    packageVersion,
                    framework))
            .ToArray();
        string[] tabIds = packages
            .Select((_, index) => $"package-{index}")
            .ToArray();
        registry.Add(new WorkspaceDefinition(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework,
                    members: packages),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            schemaVersion,
            "navigation",
            [.. packages.Select(
                (package, index) => new NavigationTabDefinition(
                    tabIds[index],
                    coordinate: package))],
            focus: tabIds[selectedIndex]));
        registry.Add(new CommittedViewDefinition(
            schemaVersion,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                .. tabIds.Select(tabId =>
                    new CommittedViewStateDefinition(
                        tabId,
                        new PortableSubjectRequest.Package(),
                        new PortableRetainedSubjectContext.Package())),
            ]));
        registry.Add(new ScenarioDefinition(
            schemaVersion,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));
        CommittedScenarioDefinitionSet definitions =
            registry.PrepareScenario("scenario") switch
            {
                InspectionDefinitionScenarioPreparationResult.Version2 version2 =>
                    version2.Definitions,
                InspectionDefinitionScenarioPreparationResult.Version3 version3 =>
                    version3.Definitions,
                InspectionDefinitionScenarioPreparationResult.Version4 version4 =>
                    version4.Definitions,
                _ => throw new InvalidOperationException(
                    "Retained activation tests require schema version 2, 3 or 4."),
            };
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                definitions,
                TestContext.Current.CancellationToken);
        return WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
    }

    static async Task<CompleteRestorationExecutionOptions>
        MultiPackageOptionsAsync()
    {
        const string sourceUrl = "https://api.nuget.org/v3/index.json";
        PackageFixture[] fixtures =
        [
            new(
                "FooPackage",
                "System.Text.Json",
                "Spotlight/package"),
            new(
                "BarPackage",
                "Microsoft.Extensions.Logging",
                "PlatformDemo"),
            new(
                "BazPackage",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                "PlatformDemo"),
        ];
        foreach (PackageFixture fixture in fixtures)
        {
            byte[] assembly = await File.ReadAllBytesAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    fixture.AssetDirectory,
                    $"{fixture.AssemblyName}.dll"),
                TestContext.Current.CancellationToken);
            byte[] package = Archive(
                ($"lib/net10.0/{fixture.AssemblyName}.dll", assembly));
            await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
                new BrowserPackage(
                    fixture.PackageId,
                    "11.0.0-preview.7.26381.103",
                    package,
                    fromCache: false,
                    producerKey: NuGetCache.GetSourceKey(sourceUrl)));
        }

        return await OptionsAsync();
    }

    static string LegacyPacket() =>
        WorkspaceSharePacketCodec.Encode(
            new WorkspaceSharePacket(
                [
                    new WorkspaceShareTab(
                        WorkspaceShareSourceKind.Package,
                        "System.Text.Json",
                        "9.0.4",
                        "net9.0",
                        runtimeIdentifier: null),
                ],
                [new WorkspaceShareContext([0])],
                activeTabIndex: 0,
                selectedContextIndex: 0,
                lens: null,
                type: null,
                memberAnchor: null,
                memberSignature: null,
                section: null,
                libraries: []));

    static async Task<CompleteRestorationExecutionOptions> OptionsAsync()
    {
        const string sourceUrl = "https://api.nuget.org/v3/index.json";
        string packagePath = Path.Combine(
            FindRepositoryRoot(),
            "fixtures",
            "services",
            "signatures",
            "system.text.json.9.0.4.nupkg");
        byte[] package = await File.ReadAllBytesAsync(
            packagePath,
            TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
            new BrowserPackage(
                "System.Text.Json",
                "9.0.4",
                package,
                fromCache: false,
                producerKey: NuGetCache.GetSourceKey(sourceUrl)));
        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        var available = new ViewFacetAvailabilitySnapshot(
            facets.Descriptors.Select(
                static descriptor =>
                    new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        ViewFacetAvailability.Available.Instance)));
        return new CompleteRestorationExecutionOptions
        {
            ContextLoad = new WorkspaceContextLoadOptions
            {
                HttpClient = new HttpClient(new RejectingHandler()),
                SourceAuthorization =
                    new UniformPackageSourceAuthorization(
                        [new PackageSource("nuget.org", sourceUrl)]),
                PackageStore = BrowserPackageWorkspace.SessionPackageStore,
                IncludePackageRootBindings = true,
            },
            ScopeDeadline = DateTimeOffset.UtcNow.AddMinutes(1),
            Facets = facets,
            FacetAvailability = (_, _) => available,
        };
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    static byte[] Archive(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
            buffer,
            System.IO.Compression.ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(path).Open();
                stream.Write(content);
            }
        }
        return buffer.ToArray();
    }

    sealed record PackageFixture(
        string PackageId,
        string AssemblyName,
        string AssetDirectory);

    static void RegisterThrowingResource(InspectionWorkspace workspace)
    {
        System.Reflection.FieldInfo? groupsField =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(groupsField);
        var groups = Assert.IsType<List<AssemblyContextGroup>>(
            groupsField.GetValue(workspace));
        AssemblyContextGroup group = Assert.Single(groups);
        System.Reflection.MethodInfo? register =
            typeof(AssemblyContextGroup).GetMethod(
                "RegisterOwnedResource",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(register);
        _ = register.Invoke(group, [new ThrowingResource()]);
    }

    sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }

    sealed class ThrowingResource : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException("Injected cleanup failure.");
    }
}
