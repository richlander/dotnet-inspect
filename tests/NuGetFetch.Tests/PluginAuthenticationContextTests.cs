using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using NuGetFetch.Plugins;

namespace NuGetFetch.Tests;

public sealed class PluginAuthenticationContextTests
{
    private const string SourceIndex =
        "https://feed.example/v3/index.json";
    private const string Resource =
        "https://feed.example/v3/flat/contoso/index.json";
    private const string ServiceIndex = """
        {
          "version": "3.0.0",
          "resources": [
            {
              "@id": "https://feed.example/v3/flat/",
              "@type": "PackageBaseAddress/3.0.0"
            }
          ]
        }
        """;

    [Fact]
    public async Task AnonymousSourceSharingOriginNeverReceivesPrivateSourceCredential()
    {
        var provider = new RecordingCredentialSource(
            static (_, _, _) =>
                Task.FromResult<PackageSourceCredential?>(
                    new("user", "private-token")));
        PackageSourceAssociation privateAssociation =
            PackageSourceAssociation.Create();
        PackageSourceAssociation anonymousAssociation =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext privateContext =
            Context(
                privateAssociation,
                "https://same.example/private/index.json",
                provider);
        using OwnedAuthenticationContext anonymousContext =
            Context(
                anonymousAssociation,
                "https://same.example/anonymous/index.json",
                provider);
        var privateTransport = ChallengeUntilAuthenticated();
        var anonymousTransport = new RecordingTransport(
            static (_, _, _) =>
                Task.FromResult(Response(HttpStatusCode.OK)));
        using HttpClient privateClient =
            Client(privateContext, privateTransport);
        using HttpClient anonymousClient =
            Client(anonymousContext, anonymousTransport);

        using HttpResponseMessage privateResponse =
            await privateClient.GetAsync(
                "https://same.example/private/index.json",
                TestContext.Current.CancellationToken);
        using HttpResponseMessage anonymousResponse =
            await anonymousClient.GetAsync(
                "https://same.example/anonymous/index.json",
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, privateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);
        Assert.Equal(1, provider.Calls);
        Assert.Single(anonymousTransport.Requests);
        Assert.Null(anonymousTransport.Requests[0].Authorization);
    }

    [Fact]
    public async Task AuthorizedResourceReusesItsSourceContextCredential()
    {
        var provider = FixedCredential("source-token");
        using OwnedAuthenticationContext context =
            Context(PackageSourceAssociation.Create(), SourceIndex, provider);
        var transport = ChallengeUntilAuthenticated();
        using HttpClient client = Client(context, transport);

        using (HttpResponseMessage source = await client.GetAsync(
            SourceIndex,
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        }

        using HttpResponseMessage resource = await client.GetAsync(
            Resource,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resource.StatusCode);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(
            BasicParameter("source-token"),
            transport.Requests[^1].Authorization);
    }

    [Fact]
    public async Task ResourceFirstChallengeUsesConfiguredProviderQuery()
    {
        var provider = FixedCredential("resource-token");
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext context =
            Context(association, SourceIndex, provider);
        var transport = V3ResourceFirstTransport();
        using IPackageSourceClient source = V3(
            association,
            context,
            transport);

        PackageSourceOperationResult<PackageVersionResult> result =
            await source.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken);

        Assert.NotNull(result.Value);
        Assert.Equal(["1.0.0"], result.Value.Candidates.Select(
            candidate => candidate.Coordinate.Version));
        Assert.Equal([SourceIndex], provider.Uris.Select(
            uri => uri.AbsoluteUri));
        Assert.Equal(
            [null, BasicParameter("resource-token")],
            transport.Requests
                .Where(request => request.Uri == Resource)
                .Select(request => request.Authorization));
    }

    [Fact]
    public async Task SharedAssociationPipelinesShareAuthenticationContext()
    {
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new RecordingCredentialSource(
            async (_, _, cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken);
                return new PackageSourceCredential(
                    "user",
                    "shared-token");
            });
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext ownedContext =
            Context(association, SourceIndex, provider);
        Assert.Throws<InvalidOperationException>(
            () => PluginAuthenticationContextOwner.Create(
                association,
                new Uri(SourceIndex),
                provider));
        {
            using IPackageSourceClient first = V3(
                association,
                ownedContext,
                ChallengeV3Transport());
            using IPackageSourceClient second = V3(
                association,
                ownedContext,
                ChallengeV3Transport());

            Task<PackageSourceOperationResult<PackageVersionResult>>
                firstRequest = first.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken);
            Task<PackageSourceOperationResult<PackageVersionResult>>
                secondRequest = second.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken);
            await provider.FirstStarted;
            release.SetResult();
            PackageSourceOperationResult<PackageVersionResult>[] responses =
                await Task.WhenAll(firstRequest, secondRequest);
            foreach (PackageSourceOperationResult<PackageVersionResult>
                response in responses)
            {
                Assert.NotNull(response.Value);
            }
        }

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void AuthenticationContextRequiresMatchingAssociationAndEndpoint()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext context =
            Context(
                association,
                SourceIndex,
                FixedCredential("token"));
        PackageSourceDescriptor descriptor =
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri(SourceIndex));

        Assert.Throws<InvalidOperationException>(
            () => PackageSourceClientFactory.CreateWithPluginAuthentication(
                descriptor,
                PackageSourceAssociation.Create(),
                context));
        Assert.Throws<InvalidOperationException>(
            () => PackageSourceClientFactory.CreateWithPluginAuthentication(
                PackageSourceDescriptor.NuGetV3(
                    "foreign",
                    "Foreign",
                    new Uri(
                        "https://foreign.example/v3/index.json")),
                association,
                context));
        Assert.Throws<InvalidOperationException>(
            () => PackageSourceClientFactory.CreateWithPluginAuthentication(
                new PackageSource(
                    "feed",
                    SourceIndex),
                PackageSourceAssociation.Create(),
                context));
        Assert.Throws<InvalidOperationException>(
            () => PackageSourceClientFactory.CreateWithPluginAuthentication(
                new PackageSource(
                    "foreign",
                    "https://foreign.example/v3/index.json"),
                association,
                context));

        using IPackageSourceClient valid =
            PackageSourceClientFactory.CreateWithPluginAuthentication(
                new PackageSource(
                    "feed",
                    SourceIndex),
                association,
                context);
        Assert.Same(
            association,
            valid.Source.Association);

        context.Dispose();
        Assert.Throws<InvalidOperationException>(
            () => PackageSourceClientFactory.CreateWithPluginAuthentication(
                descriptor,
                association,
                context));
    }

    [Fact]
    public void LegacyCreateNullOptionsRemainUnambiguous()
    {
        using IPackageSourceClient legacySource =
            PackageSourceClientFactory.Create(
                new PackageSource("feed", SourceIndex),
                PackageSourceAssociation.Create(),
                null);
        using IPackageSourceClient descriptor =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetV3(
                    "feed",
                    "Feed",
                    new Uri(SourceIndex)),
                PackageSourceAssociation.Create(),
                options: default);
    }

    [Fact]
    public async Task SharedContextSurvivesIndividualPipelineDisposal()
    {
        var provider = FixedCredential("shared-token");
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext context =
            Context(association, SourceIndex, provider);
        var firstTransport = ChallengeV3Transport();
        var survivorTransport = ChallengeV3Transport();
        IPackageSourceClient first =
            V3(association, context, firstTransport);
        using IPackageSourceClient survivor =
            V3(association, context, survivorTransport);

        Assert.NotNull((await first.GetVersionsAsync(
            "contoso",
            TestContext.Current.CancellationToken)).Value);

        first.Dispose();
        Assert.NotNull((await survivor.GetVersionsAsync(
            "contoso",
            TestContext.Current.CancellationToken)).Value);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(
            BasicParameter("shared-token"),
            survivorTransport.Requests[0].Authorization);

        var activeRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activeProvider = new RecordingCredentialSource(
            async (_, _, cancellationToken) =>
            {
                await activeRelease.Task.WaitAsync(cancellationToken);
                return new PackageSourceCredential(
                    "user",
                    "active-token");
            });
        PackageSourceAssociation activeAssociation =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext activeContext =
            Context(activeAssociation, SourceIndex, activeProvider);
        IPackageSourceClient disposable = V3(
            activeAssociation,
            activeContext,
            ChallengeV3Transport());
        using IPackageSourceClient activeSurvivor = V3(
            activeAssociation,
            activeContext,
            ChallengeV3Transport());
        Task<PackageSourceOperationResult<PackageVersionResult>>
            disposableRequest = disposable.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken);
        await activeProvider.FirstStarted;

        disposable.Dispose();
        Task<PackageSourceOperationResult<PackageVersionResult>>
            survivorRequest = activeSurvivor.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken);
        activeRelease.SetResult();
        Assert.NotNull((await survivorRequest).Value);
        Assert.Equal(1, activeProvider.Calls);
        _ = await disposableRequest;
    }

    [Fact]
    public async Task CrossContextResourceCannotReadAcquireOrReplayCredential()
    {
        var provider = new RecordingCredentialSource(
            static (uri, _, _) =>
                Task.FromResult<PackageSourceCredential?>(
                    uri.AbsolutePath.Contains(
                        "/first/",
                        StringComparison.Ordinal)
                        ? new("user", "first-token")
                        : null));
        PackageSourceAssociation firstAssociation =
            PackageSourceAssociation.Create();
        PackageSourceAssociation secondAssociation =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext firstContext =
            Context(
                firstAssociation,
                "https://same.example/first/index.json",
                provider);
        using OwnedAuthenticationContext secondContext =
            Context(
                secondAssociation,
                "https://same.example/second/index.json",
                provider);
        using HttpClient first =
            Client(firstContext, ChallengeUntilAuthenticated());
        var secondTransport = ChallengeUntilAuthenticated();
        using HttpClient second =
            Client(secondContext, secondTransport);

        using (HttpResponseMessage populated = await first.GetAsync(
            "https://same.example/resource",
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, populated.StatusCode);
        }

        using HttpResponseMessage isolated = await second.GetAsync(
            "https://same.example/resource",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, isolated.StatusCode);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(
            [
                "https://same.example/first/index.json",
                "https://same.example/second/index.json",
            ],
            provider.Uris.Select(uri => uri.AbsoluteUri));
        Assert.Single(secondTransport.Requests);
        Assert.Null(secondTransport.Requests[0].Authorization);
    }

    [Fact]
    public async Task OutOfScopeResourceCannotReadAcquireOrReplayCredential()
    {
        var provider = FixedCredential("private-token");
        using OwnedAuthenticationContext context =
            Context(PackageSourceAssociation.Create(), SourceIndex, provider);
        var transport = ChallengeUntilAuthenticated();
        using HttpClient client = Client(context, transport);

        using (HttpResponseMessage populated = await client.GetAsync(
            SourceIndex,
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, populated.StatusCode);
        }

        using HttpResponseMessage foreign = await client.GetAsync(
            "https://foreign.example/resource",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
        Assert.Equal(1, provider.Calls);
        RequestRecord request = Assert.Single(
            transport.Requests,
            request => request.Uri
                == "https://foreign.example/resource");
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task OrdinaryResourceScopeUsesCanonicalOrigin()
    {
        const string UnicodeSource =
            "https://bücher.example/v3/index.json";
        var provider = FixedCredential("idn-token");
        using OwnedAuthenticationContext context =
            Context(
                PackageSourceAssociation.Create(),
                UnicodeSource,
                provider);
        var transport = ChallengeUntilAuthenticated();
        using var invoker = new HttpMessageInvoker(
            new NuGetCredentialRedirectHandler(
                context.Reference.Bind(transport)));

        using (HttpResponseMessage populated = await SendAsync(
            invoker,
            UnicodeSource))
        {
            Assert.Equal(HttpStatusCode.OK, populated.StatusCode);
        }

        string[] authorized =
        [
            "https://xn--bcher-kva.example/other/path?x=1#fragment",
            "https://BÜCHER.example:443/another",
            "https://user@xn--bcher-kva.example/resource",
        ];
        string[] rejected =
        [
            "http://xn--bcher-kva.example/resource",
            "https://other.example/resource",
            "https://xn--bcher-kva.example:444/resource",
            "file:///tmp/package",
        ];

        foreach (string target in authorized)
        {
            using HttpResponseMessage response =
                await SendAsync(invoker, target);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        foreach (string target in rejected)
        {
            using HttpResponseMessage response =
                await SendAsync(invoker, target);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }

        foreach (string target in authorized)
        {
            Assert.NotNull(Assert.Single(
                transport.Requests,
                request => request.Uri == CanonicalUri(target))
                .Authorization);
        }

        foreach (string target in rejected)
        {
            Assert.Null(Assert.Single(
                transport.Requests,
                request => request.Uri == CanonicalUri(target))
                .Authorization);
        }

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task AzureResourceScopeIncludesOrganizationButAllowsNameGuidAliases()
    {
        const string AzureSource =
            "https://pkgs.dev.azure.com/org/project/_packaging/feed/nuget/v3/index.json";
        var provider = FixedCredential("azure-token");
        using OwnedAuthenticationContext context =
            Context(
                PackageSourceAssociation.Create(),
                AzureSource,
                provider);
        var transport = ChallengeUntilAuthenticated();
        using HttpClient client = Client(context, transport);

        using (HttpResponseMessage populated = await client.GetAsync(
            AzureSource,
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, populated.StatusCode);
        }

        const string Alias =
            "https://pkgs.dev.azure.com/org/11111111-1111-1111-1111-111111111111"
            + "/_packaging/22222222-2222-2222-2222-222222222222/nuget/v3/flat2/a";
        const string Foreign =
            "https://pkgs.dev.azure.com/other/project/_packaging/feed/nuget/v3/flat2/a";
        using (HttpResponseMessage alias = await client.GetAsync(
            Alias,
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, alias.StatusCode);
        }

        using HttpResponseMessage foreign = await client.GetAsync(
            Foreign,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
        Assert.NotNull(Assert.Single(
            transport.Requests,
            request => request.Uri == Alias).Authorization);
        Assert.Null(Assert.Single(
            transport.Requests,
            request => request.Uri == Foreign).Authorization);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ConcurrentAcquisitionIsSingleFlightPerContextAndIndependentAcrossContexts()
    {
        var provider = new ConcurrentCredentialSource();
        PackageSourceAssociation firstAssociation =
            PackageSourceAssociation.Create();
        PackageSourceAssociation secondAssociation =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext firstContext =
            Context(
                firstAssociation,
                "https://same.example/first/index.json",
                provider);
        using OwnedAuthenticationContext secondContext =
            Context(
                secondAssociation,
                "https://same.example/second/index.json",
                provider);
        using HttpClient firstA =
            Client(firstContext, ChallengeUntilAuthenticated());
        using HttpClient firstB =
            Client(firstContext, ChallengeUntilAuthenticated());
        using HttpClient secondA =
            Client(secondContext, ChallengeUntilAuthenticated());
        using HttpClient secondB =
            Client(secondContext, ChallengeUntilAuthenticated());

        Task<HttpResponseMessage>[] requests =
        [
            firstA.GetAsync(
                "https://same.example/first/a",
                TestContext.Current.CancellationToken),
            firstB.GetAsync(
                "https://same.example/first/b",
                TestContext.Current.CancellationToken),
            secondA.GetAsync(
                "https://same.example/second/a",
                TestContext.Current.CancellationToken),
            secondB.GetAsync(
                "https://same.example/second/b",
                TestContext.Current.CancellationToken),
        ];
        await provider.TwoContextsStarted;
        provider.Release();
        HttpResponseMessage[] responses =
            await Task.WhenAll(requests);
        foreach (HttpResponseMessage response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, provider.MaximumConcurrency);
    }

    [Fact]
    public async Task RetiredContextRejectsLateCredentialPublication()
    {
        var provider = new LateCredentialSource();
        using OwnedAuthenticationContext context =
            Context(
                PackageSourceAssociation.Create(),
                SourceIndex,
                provider);
        var transport = ChallengeUntilAuthenticated();
        using HttpClient client = Client(context, transport);
        Task<HttpResponseMessage> pending =
            client.GetAsync(
                SourceIndex,
                TestContext.Current.CancellationToken);
        await provider.Started;

        context.Dispose();
        using HttpResponseMessage retired = await pending;
        Assert.Equal(HttpStatusCode.Unauthorized, retired.StatusCode);
        provider.Release();
        await provider.Completed;

        using HttpResponseMessage later = await client.GetAsync(
            Resource,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, later.StatusCode);
        Assert.Equal(1, provider.Calls);
        Assert.Null(transport.Requests[^1].Authorization);
    }

    [Fact]
    public async Task RetiredContextRejectsPendingChallengeJoinAndLaterRequest()
    {
        var provider = new LateCredentialSource();
        using OwnedAuthenticationContext context =
            Context(
                PackageSourceAssociation.Create(),
                SourceIndex,
                provider);
        var firstTransport = ChallengeUntilAuthenticated();
        var secondTransport = new PendingChallengeTransport();
        using HttpClient first = Client(context, firstTransport);
        using HttpClient second = Client(context, secondTransport);
        Task<HttpResponseMessage> active =
            first.GetAsync(
                SourceIndex,
                TestContext.Current.CancellationToken);
        await provider.Started;
        Task<HttpResponseMessage> challenged =
            second.GetAsync(
                Resource,
                TestContext.Current.CancellationToken);
        await secondTransport.ChallengeObserved;

        context.Dispose();
        secondTransport.ReleaseChallenge();
        using HttpResponseMessage challengedResponse =
            await challenged;
        using HttpResponseMessage activeResponse =
            await active;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            challengedResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            activeResponse.StatusCode);

        using HttpResponseMessage later = await second.GetAsync(
            "https://feed.example/later",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, later.StatusCode);
        Assert.Equal(1, provider.Calls);
        provider.Release();
        await provider.Completed;
    }

    [Fact]
    public async Task ConcurrentRejectedCredentialRefreshesPublishOneNewVersion()
    {
        var provider = new RefreshCredentialSource();
        var transport = new RefreshTransport();
        using OwnedAuthenticationContext context =
            Context(
                PackageSourceAssociation.Create(),
                SourceIndex,
                provider);
        using HttpClient client = Client(context, transport);

        using (HttpResponseMessage populated = await client.GetAsync(
            SourceIndex,
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, populated.StatusCode);
        }

        transport.RejectOldCredential = true;
        Task<HttpResponseMessage>[] requests =
        [
            client.GetAsync(
                "https://feed.example/a",
                TestContext.Current.CancellationToken),
            client.GetAsync(
                "https://feed.example/b",
                TestContext.Current.CancellationToken),
        ];
        await provider.RefreshStarted;
        provider.ReleaseRefresh();
        HttpResponseMessage[] responses =
            await Task.WhenAll(requests);
        foreach (HttpResponseMessage response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        Assert.Equal(2, provider.Calls);
        Assert.Equal([false, true], provider.RetryFlags);
        Assert.Equal(2, transport.NewCredentialUses);
    }

    [Fact]
    public async Task ExplicitAuthorizationBypassesPluginContext()
    {
        var provider = FixedCredential("plugin-token");
        using OwnedAuthenticationContext context =
            Context(
                PackageSourceAssociation.Create(),
                SourceIndex,
                provider);
        var transport = new RecordingTransport(
            static (request, _, _) =>
                Task.FromResult(Response(
                    request.Headers.Authorization?.Parameter
                        == BasicParameter("plugin-token")
                            ? HttpStatusCode.OK
                            : HttpStatusCode.Unauthorized)));
        using HttpClient client = Client(context, transport);
        using var explicitRequest = new HttpRequestMessage(
            HttpMethod.Get,
            SourceIndex);
        explicitRequest.Headers.Authorization =
            Basic("configured-token");

        using HttpResponseMessage explicitResponse =
            await client.SendAsync(
                explicitRequest,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            explicitResponse.StatusCode);
        Assert.Equal(0, provider.Calls);
        Assert.Single(transport.Requests);

        using HttpResponseMessage pluginResponse =
            await client.GetAsync(
                Resource,
                TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, pluginResponse.StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ForbiddenChallengeRequiresContextOptIn()
    {
        var provider = FixedCredential("plugin-token");
        PackageSourceAssociation defaultAssociation =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext defaultContext =
            Context(defaultAssociation, SourceIndex, provider);
        var defaultTransport = new RecordingTransport(
            static (request, _, _) =>
                Task.FromResult(Response(
                    request.Headers.Authorization is null
                        ? HttpStatusCode.Forbidden
                        : HttpStatusCode.OK)));
        using HttpClient defaultClient =
            Client(defaultContext, defaultTransport);

        using HttpResponseMessage defaultResponse =
            await defaultClient.GetAsync(
                SourceIndex,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            defaultResponse.StatusCode);
        Assert.Equal(0, provider.Calls);

        PackageSourceAssociation optedInAssociation =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext optedInContext =
            Context(
                optedInAssociation,
                SourceIndex,
                provider,
                promptOn403: true);
        using HttpClient optedInClient =
            Client(
                optedInContext,
                new RecordingTransport(
                    static (request, _, _) =>
                        Task.FromResult(Response(
                            request.Headers.Authorization is null
                                ? HttpStatusCode.Forbidden
                                : HttpStatusCode.OK))));

        using HttpResponseMessage optedInResponse =
            await optedInClient.GetAsync(
                SourceIndex,
                TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, optedInResponse.StatusCode);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task RequestClonePropagationPreservesContextAndRejection()
    {
        const string AzureSource =
            "https://pkgs.dev.azure.com/org/project/_packaging/feed/nuget/v3/index.json";
        var provider = FixedCredential("azure-token");
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext context =
            Context(
                association,
                AzureSource,
                provider);
        var transport = new RedirectReentryTransport();
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetV3(
                    "azure",
                    "Azure",
                    new Uri(AzureSource)),
                association,
                transport,
                authenticationContext: context);

        PackageSourceOperationResult<PackageVersionResult> response =
            await client.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken);

        Assert.NotNull(response.Value);
        Assert.Equal(1, provider.Calls);
        Assert.NotNull(Assert.Single(
            transport.Requests,
            request => request.Uri.Contains(
                "/org/start/",
                StringComparison.Ordinal)).Authorization);
        Assert.Null(Assert.Single(
            transport.Requests,
            request => request.Uri.Contains(
                "/other/",
                StringComparison.Ordinal)).Authorization);
        Assert.Null(Assert.Single(
            transport.Requests,
            request => request.Uri.EndsWith(
                "/org/return",
                StringComparison.Ordinal)).Authorization);
    }

    [Fact]
    public void AuthenticationContextReferenceIsOpaque()
    {
        Type type = typeof(PluginAuthenticationContext);

        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(
            BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(type.GetProperties(
            BindingFlags.Instance | BindingFlags.Public));
        Assert.DoesNotContain(
            type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.IsPublic);
        Assert.Null(type.GetMethod(
            nameof(ToString),
            BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public async Task NuGetGalleryTransportCannotReachPluginAuthentication()
    {
        Assert.All(
            typeof(PackageSourceClientFactory)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == nameof(
                    PackageSourceClientFactory.CreateGallery)),
            method => Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.ParameterType
                    == typeof(PluginAuthenticationContext)));

        var provider = FixedCredential("must-not-run");
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using OwnedAuthenticationContext context =
            Context(association, SourceIndex, provider);
        Assert.Throws<InvalidOperationException>(
            () => PackageSourceClientFactory.CreateWithPluginAuthentication(
                PackageSourceDescriptor.NuGetGallery,
                association,
                context));

        var transport = new RecordingTransport(
            static (_, _, _) =>
                Task.FromResult(Response(
                    HttpStatusCode.OK,
                    """{"data":[]}""")));
        using IPackageSourceClient gallery =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                transport);
        PackageSourceOperationResult<PackageSearchResult> search =
            await gallery.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(search.Value);
        Assert.Equal(0, provider.Calls);
    }

    private static OwnedAuthenticationContext Context(
        PackageSourceAssociation association,
        string providerQueryUri,
        ICredentialSource provider,
        bool promptOn403 = false) =>
        new(
            PluginAuthenticationContextOwner.Create(
                association,
                new Uri(providerQueryUri),
                provider,
                promptOn403));

    private static HttpClient Client(
        PluginAuthenticationContext context,
        HttpMessageHandler transport) =>
        new(
            new NuGetCredentialRedirectHandler(
                context.Bind(transport)));

    private static IPackageSourceClient V3(
        PackageSourceAssociation association,
        PluginAuthenticationContext context,
        HttpMessageHandler transport) =>
        PackageSourceClientFactory.Create(
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri(SourceIndex)),
            association,
            transport,
            authenticationContext: context);

    private static RecordingCredentialSource FixedCredential(
        string password) =>
        new(
            (_, _, _) =>
                Task.FromResult<PackageSourceCredential?>(
                    new("user", password)));

    private static RecordingTransport ChallengeUntilAuthenticated() =>
        new(
            static (request, _, _) =>
                Task.FromResult(Response(
                    request.Headers.Authorization is null
                        ? HttpStatusCode.Unauthorized
                        : HttpStatusCode.OK)));

    private static RecordingTransport V3ResourceFirstTransport() =>
        new(
            static (request, _, _) =>
            {
                if (request.RequestUri!.AbsoluteUri == SourceIndex)
                {
                    return Task.FromResult(Response(
                        HttpStatusCode.OK,
                        ServiceIndex));
                }

                return Task.FromResult(
                    request.Headers.Authorization is null
                        ? Response(HttpStatusCode.Unauthorized)
                        : Response(
                            HttpStatusCode.OK,
                            """{"versions":["1.0.0"]}"""));
            });

    private static RecordingTransport ChallengeV3Transport() =>
        new(
            static (request, _, _) =>
            {
                if (request.Headers.Authorization is null)
                {
                    return Task.FromResult(
                        Response(HttpStatusCode.Unauthorized));
                }

                return Task.FromResult(
                    request.RequestUri!.AbsoluteUri == SourceIndex
                        ? Response(
                            HttpStatusCode.OK,
                            ServiceIndex)
                        : Response(
                            HttpStatusCode.OK,
                            """{"versions":["1.0.0"]}"""));
            });

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string? body = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new StringContent(body);
        }

        return response;
    }

    private static AuthenticationHeaderValue Basic(string password) =>
        new("Basic", BasicParameter(password));

    private static string BasicParameter(string password) =>
        Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"user:{password}"));

    private static string CanonicalUri(string uri) =>
        new Uri(uri).AbsoluteUri;

    private static async Task<HttpResponseMessage> SendAsync(
        HttpMessageInvoker invoker,
        string target)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(target, UriKind.RelativeOrAbsolute));
        return await invoker.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private sealed class RecordingCredentialSource(
        Func<
            Uri,
            bool,
            CancellationToken,
            Task<PackageSourceCredential?>> acquire)
        : ICredentialSource
    {
        private readonly TaskCompletionSource _firstStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public bool HasCredentialSources => true;

        public int Calls => _calls;

        public Task FirstStarted => _firstStarted.Task;

        public List<Uri> Uris { get; } = [];

        public List<bool> RetryFlags { get; } = [];

        public async Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            lock (Uris)
            {
                Uris.Add(uri);
                RetryFlags.Add(isRetry);
            }

            _firstStarted.TrySetResult();
            return await acquire(
                uri,
                isRetry,
                cancellationToken);
        }
    }

    private sealed class RecordingTransport(
        Func<
            HttpRequestMessage,
            int,
            CancellationToken,
            Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        private int _attempts;

        public List<RequestRecord> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref _attempts);
            lock (Requests)
            {
                Requests.Add(new RequestRecord(
                    request.RequestUri?.AbsoluteUri
                        ?? request.RequestUri?.OriginalString
                        ?? "",
                    request.Headers.Authorization?.Parameter));
            }

            HttpResponseMessage response = await respond(
                request,
                attempt,
                cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private sealed record RequestRecord(
        string Uri,
        string? Authorization);

    private sealed class OwnedAuthenticationContext(
        PluginAuthenticationContextOwner owner)
        : IDisposable
    {
        public static implicit operator PluginAuthenticationContext(
            OwnedAuthenticationContext context) =>
            context.Reference;

        public PluginAuthenticationContext Reference =>
            owner.Context;

        public void Dispose() => owner.Dispose();
    }

    private sealed class ConcurrentCredentialSource
        : ICredentialSource
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _twoContextsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _calls;
        private int _maximumConcurrency;

        public bool HasCredentialSources => true;

        public int Calls => _calls;

        public int MaximumConcurrency => _maximumConcurrency;

        public Task TwoContextsStarted =>
            _twoContextsStarted.Task;

        public void Release() => _release.TrySetResult();

        public async Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            int active = Interlocked.Increment(ref _active);
            InterlockedExtensions.Max(
                ref _maximumConcurrency,
                active);
            if (active == 2)
            {
                _twoContextsStarted.TrySetResult();
            }

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return new PackageSourceCredential(
                    "user",
                    uri.AbsolutePath.Contains(
                        "/first/",
                        StringComparison.Ordinal)
                        ? "first"
                        : "second");
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class LateCredentialSource
        : ICredentialSource
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public bool HasCredentialSources => true;

        public int Calls => _calls;

        public Task Started => _started.Task;

        public Task Completed => _completed.Task;

        public void Release() => _release.TrySetResult();

        public async Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            _started.TrySetResult();
            await _release.Task;
            _completed.TrySetResult();
            return new PackageSourceCredential(
                "user",
                "late-token");
        }
    }

    private sealed class PendingChallengeTransport
        : HttpMessageHandler
    {
        private readonly TaskCompletionSource _challengeObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ChallengeObserved =>
            _challengeObserved.Task;

        public void ReleaseChallenge() =>
            _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _challengeObserved.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return Response(HttpStatusCode.Unauthorized);
        }
    }

    private sealed class RefreshCredentialSource
        : ICredentialSource
    {
        private readonly TaskCompletionSource _refreshStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public bool HasCredentialSources => true;

        public int Calls => _calls;

        public Task RefreshStarted =>
            _refreshStarted.Task;

        public List<bool> RetryFlags { get; } = [];

        public void ReleaseRefresh() => _release.TrySetResult();

        public async Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);
            lock (RetryFlags)
            {
                RetryFlags.Add(isRetry);
            }

            if (call == 1)
            {
                return new PackageSourceCredential(
                    "user",
                    "old");
            }

            _refreshStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new PackageSourceCredential(
                "user",
                "new");
        }
    }

    private sealed class RefreshTransport
        : HttpMessageHandler
    {
        private int _newCredentialUses;

        public bool RejectOldCredential { get; set; }

        public int NewCredentialUses => _newCredentialUses;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? authorization =
                request.Headers.Authorization?.Parameter;
            if (authorization == BasicParameter("new"))
            {
                Interlocked.Increment(
                    ref _newCredentialUses);
                return Task.FromResult(
                    Response(HttpStatusCode.OK));
            }

            if (authorization == BasicParameter("old")
                && !RejectOldCredential)
            {
                return Task.FromResult(
                    Response(HttpStatusCode.OK));
            }

            return Task.FromResult(
                Response(HttpStatusCode.Unauthorized));
        }
    }

    private sealed class RedirectReentryTransport
        : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string uri = request.RequestUri!.AbsoluteUri;
            Requests.Add(
                new RequestRecord(
                    uri,
                    request.Headers.Authorization?.Parameter));
            if (uri.EndsWith(
                    "/org/project/_packaging/feed/nuget/v3/index.json",
                    StringComparison.Ordinal))
            {
                if (request.Headers.Authorization is null)
                {
                    return Task.FromResult(
                        Response(HttpStatusCode.Unauthorized));
                }

                return Task.FromResult(
                    Response(
                        HttpStatusCode.OK,
                        """
                        {
                          "version": "3.0.0",
                          "resources": [
                            {
                              "@id": "https://pkgs.dev.azure.com/org/start/",
                              "@type": "PackageBaseAddress/3.0.0"
                            }
                          ]
                        }
                        """));
            }

            if (uri.Contains(
                    "/org/start/",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Redirect(
                        "https://pkgs.dev.azure.com/other/redirect"));
            }

            if (uri.Contains(
                    "/other/",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Redirect(
                        "https://pkgs.dev.azure.com/org/return"));
            }

            return Task.FromResult(
                Response(
                    HttpStatusCode.OK,
                    """{"versions":["1.0.0"]}"""));
        }

        private static HttpResponseMessage Redirect(string target)
        {
            var response = Response(
                HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(target);
            return response;
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(
            ref int target,
            int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                ref target,
                value,
                current) != current);
        }
    }
}
