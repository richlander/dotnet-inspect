using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using InertText;
using NuGetFetch.CustomClientFixture;

namespace NuGetFetch.Tests;

public sealed class PackageSourceResultIdentityTests
{
    [Theory]
    [InlineData(
        "https://feed.example/v3/index.json",
        "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAAAxmZWVkLmV4YW1wbGUAAAAAAAAAAzQ0MwAAAA4vdjMvaW5kZXguanNvbg")]
    [InlineData(
        "https://bücher.example/feed",
        "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAABV4bi0tYmNoZXIta3ZhLmV4YW1wbGUAAAAAAAAAAzQ0MwAAAAUvZmVlZA")]
    [InlineData(
        "http://127.0.0.1:8080/root",
        "nfs-http-1.AAAABGh0dHAAAAAEaXB2NAAAAAR_AAABAAAAAAAAAAQ4MDgwAAAABS9yb290")]
    [InlineData(
        "https://[fe80::1%25ETH0]/v3/%7e/",
        "nfs-http-1.AAAABWh0dHBzAAAABGlwdjYAAAAQ_oAAAAAAAAAAAAAAAAAAAQAAAARFVEgwAAAAAzQ0MwAAAAcvdjMvJTdF")]
    [InlineData(
        "https://feed.example/a/%2f/b",
        "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAAAxmZWVkLmV4YW1wbGUAAAAAAAAAAzQ0MwAAAAgvYS8lMkYvYg")]
    [InlineData(
        "https://feed.example/",
        "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAAAxmZWVkLmV4YW1wbGUAAAAAAAAAAzQ0MwAAAAA")]
    [InlineData(
        "https://feed.example//",
        "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAAAxmZWVkLmV4YW1wbGUAAAAAAAAAAzQ0MwAAAAEv")]
    [InlineData(
        "https://feed.example/F/auth/secret/api",
        "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAAAxmZWVkLmV4YW1wbGUAAAAAAAAAAzQ0MwAAABQvRi9hdXRoL1JFREFDVEVEL2FwaQ")]
    public void HttpProducerKeyHasStableUtf8Framing(
        string endpoint,
        string expected)
    {
        Assert.Equal(expected, Producer(endpoint).Key);
    }

    [Theory]
    [InlineData(
        "HTTPS://FEED.EXAMPLE/v3/index.json",
        "https://feed.example:443/v3/index.json")]
    [InlineData(
        "https://bücher.example/feed",
        "https://xn--bcher-kva.example:443/feed")]
    [InlineData(
        "http://127.0.0.1:8080/root",
        "http://127.0.0.1:8080/root")]
    [InlineData(
        "https://[fe80::1%25ETH0]/v3/%7e/",
        "https://[fe80::1%25ETH0]:443/v3/%7E")]
    [InlineData(
        "https://feed.example",
        "https://feed.example:443")]
    [InlineData(
        "https://feed.example//",
        "https://feed.example:443/")]
    [InlineData(
        "https://feed.example/a/%2f/b/",
        "https://feed.example:443/a/%2F/b")]
    [InlineData(
        "http://feed.example:80/root/",
        "http://feed.example:80/root")]
    [InlineData(
        "http://feed.example:8080/root/",
        "http://feed.example:8080/root")]
    public void HttpProducerDisplayHasStableCanonicalVectors(
        string endpoint,
        string expected)
    {
        Assert.Equal(expected, Producer(endpoint).Display.ToString());
    }

    [Fact]
    public void ProducerIdentityConsumesNormalizedEndpointProjection()
    {
        MethodInfo factory = Assert.Single(
            typeof(PackageSourceClientFactory)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic),
            method => method.Name == "CreateHttpProducer");
        ParameterInfo parameter = Assert.Single(factory.GetParameters());

        Assert.Equal(
            "NuGetSourceRequest+EndpointProjection",
            parameter.ParameterType.Name
                == "EndpointProjection"
                    ? $"{parameter.ParameterType.DeclaringType!.Name}+{parameter.ParameterType.Name}"
                    : parameter.ParameterType.Name);
        Assert.Equal(
            typeof(PackageProducerIdentity),
            factory.ReturnType);
        Assert.DoesNotContain(
            typeof(PackageProducerIdentity)
                .GetConstructors(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters()),
            candidate => candidate.ParameterType == typeof(Uri));
        Assert.Equal(
            "https://[fe80::1%25ETH0]:443/v3/%7E",
            Producer(
                "https://[fe80::1%25ETH0]/v3/%7e/")
                .Display.ToString());
    }

    [Fact]
    public void ProducerIdentityIgnoresAmbientCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");
            PackageProducerIdentity first = Producer(
                "HTTPS://BÜCHER.EXAMPLE:443/V3/%7e/");

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
            PackageProducerIdentity second = Producer(
                "https://xn--bcher-kva.example/V3/%7E");

            Assert.Equal(first, second);
            Assert.Equal(first.Key, second.Key);
            Assert.Equal(first.Display, second.Display);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ProducerIdentityFoldsOnlyDeclaredEndpointEquivalences()
    {
        AssertProducerEqual(
            "HTTPS://FEED.EXAMPLE/v3/index.json",
            "https://feed.example:443/v3/index.json");
        AssertProducerEqual(
            "https://feed.example/a/%2f",
            "https://feed.example/a/%2F/");
        AssertProducerEqual(
            "https://feed.example/path?tenant=a",
            "https://feed.example/path?tenant=b");
        AssertProducerEqual(
            "https://feed.example/path#one",
            "https://feed.example/path#two");
        AssertProducerEqual(
            "https://feed.example/F/auth/alpha/api",
            "https://feed.example/F/auth/beta/api");

        AssertProducerDifferent(
            "https://feed.example/Path",
            "https://feed.example/path");
        AssertProducerDifferent(
            "https://feed.example/a/../b",
            "https://feed.example/b");
        AssertProducerDifferent(
            "https://feed.example/%7E",
            "https://feed.example/~");
        AssertProducerDifferent(
            "https://feed.example/",
            "https://feed.example//");
        AssertProducerDifferent(
            "https://[fe80::1%25ETH0]/path",
            "https://[fe80::1%25eth0]/path");
        AssertProducerDifferent(
            "https://feed.example/F/token/alpha/api",
            "https://feed.example/F/token/beta/api");
    }

    [Fact]
    public void ProducerKeyVersionPinsPathRedactionOutput()
    {
        Assert.Equal(1, UrlRedaction.PathComponentContractVersion);
        Assert.Equal(
            "/F/auth/REDACTED/api",
            UrlRedaction.ForPathComponent(
                "/F/auth/secret/api").ToString());
        Assert.Equal(
            "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAAAxmZWVkLmV4YW1wbGUAAAAAAAAAAzQ0MwAAABQvRi9hdXRoL1JFREFDVEVEL2FwaQ",
            Producer(
                "https://feed.example/F/auth/secret/api").Key);
        Assert.Equal(
            "nfs-http-1.AAAABWh0dHBzAAAAA2RucwAAAAxmZWVkLmV4YW1wbGUAAAAAAAAAAzQ0MwAAABAvcHJveHkvaHR0cHM6Ly9h",
            Producer(
                "https://feed.example/proxy/https://a/").Key);
    }

    [Fact]
    public void ProducerIdentityRedactsPathBeforeKeyAndDisplay()
    {
        const string firstSecret = "alpha-secret";
        const string secondSecret = "beta-secret";
        PackageProducerIdentity first = Producer(
            $"https://feed.example/F/auth/{firstSecret}/api");
        PackageProducerIdentity second = Producer(
            $"https://feed.example/F/auth/{secondSecret}/api");

        Assert.Equal(first, second);
        Assert.DoesNotContain(firstSecret, first.Key, StringComparison.Ordinal);
        Assert.DoesNotContain(secondSecret, first.Key, StringComparison.Ordinal);
        Assert.DoesNotContain(
            firstSecret,
            first.Display.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            secondSecret,
            second.Display.ToString(),
            StringComparison.Ordinal);
        AssertProducerDifferent(
            $"https://feed.example/F/token/{firstSecret}/api",
            $"https://feed.example/F/token/{secondSecret}/api");
    }

    [Fact]
    public void AuthorityShapedPathsRemainDistinctProducerIdentities()
    {
        AssertProducerDifferent(
            "https://feed.example/proxy/https://a/",
            "https://feed.example/proxy/https://b/");
    }

    [Fact]
    public void QueryDistinctAuthoritiesRequireDistinctAssociations()
    {
        PackageSourceAssociation firstAssociation =
            PackageSourceAssociation.Create();
        PackageSourceAssociation secondAssociation =
            PackageSourceAssociation.Create();
        using IPackageSourceClient first = V3(
            "https://feed.example/v3/index.json?tenant=a",
            firstAssociation);
        using IPackageSourceClient second = V3(
            "https://feed.example/v3/index.json?tenant=b",
            secondAssociation);
        var authorities =
            new Dictionary<PackageSourceAssociation, string>(
                ReferenceEqualityComparer.Instance)
            {
                [firstAssociation] = "tenant-a",
                [secondAssociation] = "tenant-b",
            };

        Assert.Equal(first.Source.Producer, second.Source.Producer);
        Assert.NotSame(
            first.Source.Association,
            second.Source.Association);
        Assert.Equal(
            "tenant-a",
            authorities[first.Source.Association]);
        Assert.Equal(
            "tenant-b",
            authorities[second.Source.Association]);
    }

    [Fact]
    public void SourceResultIdentityEqualityUsesAllRoles()
    {
        PackageSourceAssociation shared =
            PackageSourceAssociation.Create();
        using IPackageSourceClient first = V3(
            "https://feed.example/v3/index.json",
            shared);
        using IPackageSourceClient equal = V3(
            "https://FEED.EXAMPLE:443/v3/index.json/",
            shared);
        using IPackageSourceClient differentProducer = V3(
            "https://other.example/v3/index.json",
            shared);
        using IPackageSourceClient differentAssociation = V3(
            "https://feed.example/v3/index.json",
            PackageSourceAssociation.Create());
        using IPackageSourceClient differentTransport =
            PackageSourceClientFactory.CreateGallery(
                shared,
                new PassiveHandler());
        using IPackageSourceClient canonicalV3 = V3(
            "https://api.nuget.org/v3/index.json",
            shared);

        Assert.Equal(first.Source, equal.Source);
        Assert.Equal(
            first.Source.GetHashCode(),
            equal.Source.GetHashCode());
        Assert.NotEqual(first.Source, differentProducer.Source);
        Assert.NotEqual(first.Source, differentAssociation.Source);
        Assert.NotEqual(canonicalV3.Source, differentTransport.Source);
    }

    [Fact]
    public void EverySourceResultCarriesTheIssuingIdentity()
    {
        PackageSourceResultFactory factory = CreateFactory();
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");
        PackageCandidateObservation candidate = factory.Candidate(
            coordinate,
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Listed);
        PackageSearchResult search = factory.Search(
            [new SearchResult("Contoso", "1.0.0")]);
        PackageSearchResult emptySearch = factory.Search([]);
        PackageVersionResult versions = factory.Versions(
            [candidate],
            hasAuthoritativeListingState: true);
        PackageVersionResult emptyVersions = factory.Versions(
            [],
            hasAuthoritativeListingState: true);
        PackageSourceManifest manifest = factory.Manifest(
            coordinate,
            "<package />"u8.ToArray());
        PackageSourcePayload package = factory.Payload(
            coordinate,
            PackageSourcePayloadKind.Package,
            new MemoryStream([1], writable: false));
        PackageSourcePayload symbols = factory.Payload(
            coordinate,
            PackageSourcePayloadKind.Symbols,
            new MemoryStream([2], writable: false));

        Assert.Same(factory.Source, candidate.Source);
        Assert.Same(factory.Source, search.Source);
        Assert.Same(
            factory.Source,
            Assert.Single(search.Matches).Candidate.Source);
        Assert.Same(factory.Source, emptySearch.Source);
        Assert.Same(factory.Source, versions.Source);
        Assert.Same(factory.Source, emptyVersions.Source);
        Assert.Same(factory.Source, manifest.Source);
        Assert.Same(factory.Source, package.Source);
        Assert.Same(factory.Source, symbols.Source);

        PackageSourceOperationResult<PackageSearchResult>[] searchOutcomes =
        [
            factory.SucceededSearch(search),
            factory.FailedSearch(PackageSourceFailureKind.Unsupported),
            factory.FailedSearch(
                PackageSourceFailureKind.AuthenticationRequired),
            factory.FailedSearch(PackageSourceFailureKind.Timeout),
            factory.FailedSearch(PackageSourceFailureKind.InvalidResponse),
            factory.FailedSearch(PackageSourceFailureKind.ResponseRejected),
            factory.FailedSearch(PackageSourceFailureKind.Transport),
        ];
        foreach (PackageSourceOperationResult<PackageSearchResult> outcome
            in searchOutcomes)
        {
            Assert.Same(
                factory.Source,
                outcome.Value?.Source ?? outcome.Failure!.Source);
        }

        Assert.Same(
            factory.Source,
            factory.FailedVersions(
                PackageSourceFailureKind.Transport).Failure!.Source);
        Assert.Same(
            factory.Source,
            factory.FailedManifest(
                coordinate,
                PackageSourceFailureKind.NotFound).Failure!.Source);
        Assert.Same(
            factory.Source,
            factory.FailedPackage(
                coordinate,
                PackageSourceFailureKind.NotFound).Failure!.Source);
        Assert.Same(
            factory.Source,
            factory.FailedSymbols(
                coordinate,
                PackageSourceFailureKind.NotFound).Failure!.Source);

        package.Content.Dispose();
        symbols.Content.Dispose();
    }

    [Fact]
    public void GalleryAndV3ClientsShareCanonicalNuGetOrgProducer()
    {
        PackageSourceAssociation galleryAssociation =
            PackageSourceAssociation.Create();
        PackageSourceAssociation v3Association =
            PackageSourceAssociation.Create();
        using IPackageSourceClient gallery =
            PackageSourceClientFactory.CreateGallery(
                galleryAssociation,
                new PassiveHandler());
        using IPackageSourceClient v3 = V3(
            "HTTPS://API.NUGET.ORG:443/v3/index.json/",
            v3Association);

        Assert.Same(gallery.Source.Producer, v3.Source.Producer);
        Assert.Equal(
            PackageProducerIdentity.NuGetOrg,
            gallery.Source.Producer);
        Assert.NotSame(
            gallery.Source.Association,
            v3.Source.Association);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            gallery.Source.TransportKind);
        Assert.Equal(
            PackageSourceKind.NuGetV3,
            v3.Source.TransportKind);
    }

    [Fact]
    public void SharedAssociationAcrossTransportsFlowsThroughAllResultShapes()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PackageSourceResultFactory gallery = CreateFactory(
            PackageSourceDescriptor.NuGetGallery,
            association);
        PackageSourceResultFactory v3 = CreateFactory(
            PackageSourceDescriptor.NuGetV3(
                "nuget-v3",
                "NuGet.org v3",
                new Uri("https://api.nuget.org/v3/index.json")),
            association);

        Assert.Equal(gallery.Source.Producer, v3.Source.Producer);
        Assert.NotEqual(
            gallery.Source.TransportKind,
            v3.Source.TransportKind);
        AssertFactoryAssociation(gallery, association);
        AssertFactoryAssociation(v3, association);
    }

    [Fact]
    public void SourceResultFactoryBindsIssuingIdentity()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PackageSourceResultFactory first = CreateFactory(
            association: association);
        PackageSourceResultFactory second = CreateFactory(
            association: association);
        PackageSearchResult firstValue = first.Search(
            [new SearchResult("Contoso", "1.0.0")]);

        Assert.Equal(first.Source, second.Source);
        Assert.NotSame(first.Source, second.Source);
        Assert.Throws<InvalidOperationException>(
            () => second.SucceededSearch(firstValue));
        Assert.Same(
            firstValue,
            first.SucceededSearch(firstValue).Value);
    }

    [Fact]
    public void SourceConstructionRequiresOwnerCapability()
    {
        Type[] constructedTypes =
        [
            typeof(PackageProducerIdentity),
            typeof(PackageSourceResultIdentity),
            typeof(PackageSourceResultFactory),
            typeof(PackageCandidateObservation),
            typeof(PackageSearchMatch),
            typeof(PackageSearchResult),
            typeof(PackageVersionResult),
            typeof(PackageSourceManifestContent),
            typeof(PackageSourceManifest),
            typeof(PackageSourcePayload),
            typeof(PackageSourceFailure),
            typeof(PackageSourceOperationResult<PackageSearchResult>),
        ];

        foreach (Type type in constructedTypes)
        {
            ConstructorInfo constructor = Assert.Single(
                type.GetConstructors(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic));
            Assert.False(constructor.IsPublic);
            Assert.Equal(
                typeof(object),
                constructor.GetParameters()[0].ParameterType);
            AssertCapabilityRejected(constructor, null);
            AssertCapabilityRejected(constructor, new object());
        }

        Assert.All(
            typeof(PackageSourceClientFactory).GetFields(
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic),
            field =>
            {
                if (field.Name.Contains(
                        "Capability",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Assert.True(field.IsPrivate);
                }
            });
    }

    [Fact]
    public void RuntimeClientFactoriesRequireCallerAssociation()
    {
        MethodInfo[] runtimeFactories =
        [
            .. typeof(PackageSourceClientFactory)
                .GetMethods(
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic)
                .Where(method =>
                    method.ReturnType == typeof(IPackageSourceClient)),
        ];

        Assert.NotEmpty(runtimeFactories);
        Assert.All(
            runtimeFactories,
            method =>
            {
                ParameterInfo association = Assert.Single(
                    method.GetParameters(),
                    parameter =>
                        parameter.ParameterType
                            == typeof(PackageSourceAssociation));
                Assert.False(association.IsOptional);
            });

        PackageSourceAssociation shared =
            PackageSourceAssociation.Create();
        using IPackageSourceClient gallery =
            PackageSourceClientFactory.CreateGallery(
                shared,
                new PassiveHandler());
        using IPackageSourceClient v3 = V3(
            "https://api.nuget.org/v3/index.json",
            shared);
        Assert.Same(shared, gallery.Source.Association);
        Assert.Same(shared, v3.Source.Association);
    }

    [Fact]
    public async Task CustomClientRegistrationReceivesBoundFactory()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using IPackageSourceClient client =
            ExternalCustomPackageSource.Create(
                PackageSourceDescriptor.NuGetGallery,
                association);

        Assert.Same(association, client.Source.Association);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            client.Source.TransportKind);
        Assert.NotNull(
            (await client.SearchAsync(
                "external",
                cancellationToken:
                    TestContext.Current.CancellationToken)).Value);
        Assert.NotNull(
            (await client.SearchByPrefixAsync(
                "External",
                cancellationToken:
                    TestContext.Current.CancellationToken)).Value);
        Assert.NotNull(
            (await client.GetVersionsAsync(
                "External.Package",
                TestContext.Current.CancellationToken)).Value);
        Assert.NotNull(
            (await client.GetManifestAsync(
                "External.Package",
                "1.0.0",
                TestContext.Current.CancellationToken)).Value);
        PackageSourcePayload package = Assert.IsType<PackageSourcePayload>(
            (await client.GetPackageAsync(
                "External.Package",
                "1.0.0",
                TestContext.Current.CancellationToken)).Value);
        PackageSourcePayload symbols = Assert.IsType<PackageSourcePayload>(
            (await client.TryGetSymbolsAsync(
                "External.Package",
                "1.0.0",
                TestContext.Current.CancellationToken)).Value);
        await package.Content.DisposeAsync();
        await symbols.Content.DisposeAsync();

        Assert.Throws<ArgumentNullException>(
            () => PackageSourceClientFactory.CreateCustom(
                null!,
                association,
                _ => new FactoryOnlyClient(null!)));
        Assert.Throws<ArgumentNullException>(
            () => PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                null!,
                _ => new FactoryOnlyClient(null!)));
        Assert.Throws<ArgumentNullException>(
            () => PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                association,
                null!));
    }

    [Fact]
    public void SourceOperationFactoryMatchesClientOperations()
    {
        string[] operations =
        [
            .. typeof(IPackageSourceClient)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method =>
                    method.ReturnType.IsGenericType
                    && method.ReturnType.GetGenericTypeDefinition()
                        == typeof(Task<>))
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(
            [
                "GetManifestAsync",
                "GetPackageAsync",
                "GetVersionsAsync",
                "SearchAsync",
                "SearchByPrefixAsync",
                "TryGetSymbolsAsync",
            ],
            operations);

        string[] finiteMethods =
        [
            .. typeof(PackageSourceResultFactory)
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.DeclaredOnly)
                .Where(method =>
                    method.Name.StartsWith(
                        "Succeeded",
                        StringComparison.Ordinal)
                    || method.Name.StartsWith(
                        "Failed",
                        StringComparison.Ordinal))
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(
            [
                "FailedManifest",
                "FailedPackage",
                "FailedSearch",
                "FailedSymbols",
                "FailedVersions",
                "SucceededManifest",
                "SucceededPackage",
                "SucceededSearch",
                "SucceededSymbols",
                "SucceededVersions",
            ],
            finiteMethods);
        Assert.DoesNotContain(
            typeof(PackageSourceResultFactory).GetMethods(),
            method => method.IsGenericMethodDefinition);
    }

    [Fact]
    public void ExactOperationCoordinatesMatchInvocation()
    {
        PackageSourceResultFactory factory = CreateFactory();
        PackageSourceCoordinate requested =
            PackageSourceCoordinate.Create("Contoso", "1.0");
        PackageSourceCoordinate other =
            PackageSourceCoordinate.Create("Other", "1.0.0");
        PackageSourceManifest manifest = factory.Manifest(
            requested,
            ReadOnlyMemory<byte>.Empty);
        PackageSourcePayload package = factory.Payload(
            requested,
            PackageSourcePayloadKind.Package,
            new MemoryStream());

        Assert.Same(
            manifest,
            factory.SucceededManifest(requested, manifest).Value);
        Assert.Same(
            package,
            factory.SucceededPackage(requested, package).Value);
        Assert.Throws<InvalidOperationException>(
            () => factory.SucceededManifest(other, manifest));
        Assert.Throws<InvalidOperationException>(
            () => factory.SucceededPackage(other, package));
        Assert.Equal(
            requested,
            factory.FailedManifest(
                requested,
                PackageSourceFailureKind.NotFound).Failure!.Coordinate);
        Assert.Equal(
            requested,
            factory.FailedPackage(
                requested,
                PackageSourceFailureKind.NotFound).Failure!.Coordinate);
        Assert.Equal(
            requested,
            factory.FailedSymbols(
                requested,
                PackageSourceFailureKind.NotFound).Failure!.Coordinate);
        package.Content.Dispose();
    }

    [Fact]
    public void SourceResultIssuerCoversEveryConstructibleShape()
    {
        Type[] issuerTypes =
        [
            typeof(PackageCandidateObservation),
            typeof(PackageSearchMatch),
            typeof(PackageSearchResult),
            typeof(PackageVersionResult),
            typeof(PackageSourceManifest),
            typeof(PackageSourcePayload),
            typeof(PackageSourceFailure),
            typeof(PackageSourceOperationResult<PackageSearchResult>),
        ];
        Assert.All(
            issuerTypes,
            type =>
            {
                FieldInfo issuer = Assert.Single(
                    type.GetFields(
                        BindingFlags.Instance
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly),
                    field => field.Name == "_issuer");
                Assert.True(issuer.IsPrivate);
                Assert.Equal(typeof(object), issuer.FieldType);
            });

        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PackageSourceResultFactory first = CreateFactory(
            association: association);
        PackageSourceResultFactory second = CreateFactory(
            association: association);
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");
        Assert.Throws<InvalidOperationException>(
            () => second.Versions(
                [
                    first.Candidate(
                        coordinate,
                        PackageDiscoveryContract.ExactCoordinate,
                        PackageListingState.NotApplicable),
                ],
                hasAuthoritativeListingState: false));
    }

    [Fact]
    public void SourceOperationOutcomesBindIssuingIdentity()
    {
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        PackageSourceResultFactory first = CreateFactory(
            association: association);
        PackageSourceResultFactory second = CreateFactory(
            association: association);
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");
        PackageSearchResult foreignSearch = first.Search([]);
        PackageSourcePayload symbols = first.Payload(
            coordinate,
            PackageSourcePayloadKind.Symbols,
            new MemoryStream());

        Assert.Throws<InvalidOperationException>(
            () => second.SucceededSearch(foreignSearch));
        Assert.Throws<InvalidOperationException>(
            () => first.SucceededPackage(coordinate, symbols));
        PackageSourceOperationResult<PackageSearchResult> succeeded =
            first.SucceededSearch(foreignSearch);
        PackageSourceOperationResult<PackageSearchResult> failed =
            first.FailedSearch(PackageSourceFailureKind.Transport);
        Assert.NotNull(succeeded.Value);
        Assert.Null(succeeded.Failure);
        Assert.Null(failed.Value);
        Assert.NotNull(failed.Failure);
        symbols.Content.Dispose();
    }

    [Fact]
    public void SourceResultCollectionsAndBuffersAreImmutableSnapshots()
    {
        PackageSourceResultFactory factory = CreateFactory();
        SearchVersion[] suppliedVersions =
        [
            new("1.0.0", 10),
        ];
        string[] suppliedOwners = ["Contoso"];
        SearchResult[] suppliedSearch =
        [
            new(
                "Contoso",
                "1.0.0",
                Versions: suppliedVersions,
                Owners: suppliedOwners),
        ];
        PackageSearchResult search = factory.Search(suppliedSearch);
        suppliedVersions[0] = new SearchVersion("9.0.0", 90);
        suppliedOwners[0] = "Mutated";
        suppliedSearch[0] = new SearchResult("Other", "9.0.0");

        PackageSearchMatch match = Assert.Single(search.Matches);
        Assert.Equal("Contoso", match.Metadata.Id);
        Assert.Equal(
            "1.0.0",
            Assert.Single(match.Metadata.Versions!).Version);
        Assert.Equal("Contoso", Assert.Single(match.Metadata.Owners!));
        Assert.False(search.Matches is Array);
        Assert.False(search.Matches is List<PackageSearchMatch>);

        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");
        PackageCandidateObservation first = factory.Candidate(
            coordinate,
            PackageDiscoveryContract.ExactCoordinate,
            PackageListingState.NotApplicable);
        PackageCandidateObservation[] suppliedCandidates = [first];
        PackageVersionResult versions = factory.Versions(
            suppliedCandidates,
            hasAuthoritativeListingState: false);
        suppliedCandidates[0] = factory.Candidate(
            PackageSourceCoordinate.Create("Other", "2.0.0"),
            PackageDiscoveryContract.ExactCoordinate,
            PackageListingState.NotApplicable);
        Assert.Same(first, Assert.Single(versions.Candidates));

        byte[] suppliedManifest = [1, 2, 3];
        PackageSourceManifest manifest = factory.Manifest(
            coordinate,
            suppliedManifest);
        suppliedManifest[0] = 9;
        Assert.Equal([1, 2, 3], manifest.Content.ToArray());
    }

    [Fact]
    public void ManifestContentIsByteAccurateCopyOutStorage()
    {
        PackageSourceResultFactory factory = CreateFactory();
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");
        PackageSourceManifestContent content = factory.Manifest(
            coordinate,
            new byte[] { 1, 2, 3 }).Content;

        Assert.Equal(3, content.Length);
        Assert.Equal(1, content[0]);
        Assert.Equal(2, content[1]);
        Assert.Equal(3, content[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => content[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => content[3]);

        byte[] exact = new byte[3];
        content.CopyTo(exact);
        Assert.Equal([1, 2, 3], exact);
        byte[] oversized = [9, 9, 9, 7, 8];
        content.CopyTo(oversized);
        Assert.Equal([1, 2, 3, 7, 8], oversized);
        byte[] undersized = [9, 9];
        Assert.Throws<ArgumentException>(
            () => content.CopyTo(undersized));
        Assert.Equal([9, 9], undersized);

        byte[] first = content.ToArray();
        byte[] second = content.ToArray();
        Assert.NotSame(first, second);
        first[0] = 9;
        Assert.Equal(1, content[0]);

        PackageSourceManifestContent empty = factory.Manifest(
            coordinate,
            ReadOnlyMemory<byte>.Empty).Content;
        byte[] emptyFirst = empty.ToArray();
        byte[] emptySecond = empty.ToArray();
        Assert.Empty(emptyFirst);
        Assert.NotSame(emptyFirst, emptySecond);
        empty.CopyTo([]);
        Assert.Empty(typeof(PackageSourceManifestContent).GetInterfaces());
    }

    [Fact]
    public void IdentityBearingResultShapesAreClosed()
    {
        Type[] closedTypes =
        [
            typeof(PackageProducerIdentity),
            typeof(PackageSourceResultIdentity),
            typeof(PackageCandidateObservation),
            typeof(PackageSearchMatch),
            typeof(PackageSearchResult),
            typeof(PackageVersionResult),
            typeof(PackageSourceManifest),
            typeof(PackageSourcePayload),
            typeof(PackageSourceFailure),
            typeof(PackageSourceOperationResult<PackageSearchResult>),
        ];

        Assert.All(
            closedTypes,
            type =>
            {
                Assert.True(type.IsSealed);
                Assert.Empty(type.GetConstructors());
                Assert.DoesNotContain(
                    type.GetProperties(
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.DeclaredOnly),
                    property => property.SetMethod?.IsPublic == true);
                Assert.DoesNotContain(
                    type.GetMethods(
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.DeclaredOnly),
                    method => method.Name is "Clone"
                        or "<Clone>$");
            });
    }

    [Fact]
    public void SourceResultIssuerIsPrivateConstructionEvidence()
    {
        Assert.DoesNotContain(
            PublicSurface(typeof(PackageCandidateObservation))
                .Concat(PublicSurface(typeof(PackageSearchMatch)))
                .Concat(PublicSurface(typeof(PackageSearchResult)))
                .Concat(PublicSurface(typeof(PackageVersionResult)))
                .Concat(PublicSurface(typeof(PackageSourceManifest)))
                .Concat(PublicSurface(typeof(PackageSourcePayload)))
                .Concat(PublicSurface(typeof(PackageSourceFailure)))
                .Concat(PublicSurface(
                    typeof(PackageSourceOperationResult<PackageSearchResult>))),
            member => member.Name.Contains(
                "Issuer",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageSourceAssociationHasOpaqueReferenceSurface()
    {
        Type type = typeof(PackageSourceAssociation);
        PackageSourceAssociation first =
            PackageSourceAssociation.Create();
        PackageSourceAssociation second =
            PackageSourceAssociation.Create();

        Assert.NotSame(first, second);
        Assert.False(first.Equals(second));
        Assert.Empty(
            type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly));
        Assert.Empty(
            type.GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly));
        Assert.Empty(type.GetInterfaces());
        MethodInfo create = Assert.Single(
            type.GetMethods(
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(PackageSourceAssociation.Create), create.Name);
        Assert.DoesNotContain(
            type.GetMethods(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly),
            method => method.Name is nameof(object.Equals)
                or nameof(object.GetHashCode)
                or nameof(object.ToString));
    }

    [Fact]
    public void FailureFactoryAcceptsNoArbitraryRetainedText()
    {
        MethodInfo[] failureMethods =
        [
            .. typeof(PackageSourceResultFactory)
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.DeclaredOnly)
                .Where(method => method.Name.StartsWith(
                    "Failed",
                    StringComparison.Ordinal)),
        ];

        Assert.Equal(5, failureMethods.Length);
        Assert.DoesNotContain(
            failureMethods.SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(string)
                || parameter.ParameterType
                    == typeof(PackageSourceCapabilities));
        Assert.All(
            Enum.GetValues<PackageSourceFailureKind>()
                .Where(kind =>
                    kind != PackageSourceFailureKind.NotFound),
            kind =>
            {
                PackageSourceFailure failure =
                    CreateFactory().FailedSearch(kind).Failure!;
                Assert.False(string.IsNullOrWhiteSpace(failure.Message));
            });
        Assert.Throws<ArgumentException>(
            () => CreateFactory().FailedSearch(
                PackageSourceFailureKind.NotFound));
    }

    [Fact]
    public void RetainedFailureStorageMatchesAllowList()
    {
        Type[] allowedFailureFields =
        [
            typeof(object),
            typeof(PackageSourceResultIdentity),
            typeof(PackageSourceCapabilities),
            typeof(PackageSourceCoordinate),
            typeof(PackageSourceFailureKind),
            typeof(string),
        ];
        FieldInfo[] failureFields =
            typeof(PackageSourceFailure).GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);
        Assert.Equal(6, failureFields.Length);
        Assert.All(
            failureFields,
            field => Assert.Contains(
                Nullable.GetUnderlyingType(field.FieldType)
                    ?? field.FieldType,
                allowedFailureFields));

        FieldInfo[] outcomeFields =
            typeof(PackageSourceOperationResult<PackageSearchResult>)
                .GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
        Assert.Equal(3, outcomeFields.Length);
        Assert.Contains(
            outcomeFields,
            field => field.FieldType == typeof(object));
        Assert.Contains(
            outcomeFields,
            field => field.FieldType == typeof(PackageSearchResult));
        Assert.Contains(
            outcomeFields,
            field => field.FieldType == typeof(PackageSourceFailure));
    }

    [Fact]
    public async Task RetainedFailureHasNoConfiguredEndpointOrRecognizedCredentialText()
    {
        const string pathSecret = "path-secret";
        const string querySecret = "query-secret";
        const string responseSecret = "response-secret";
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "signed",
                    $"https://feed.example/F/auth/{pathSecret}/api"
                    + $"?sig={querySecret}"),
                PackageSourceAssociation.Create(),
                new ThrowingHandler(
                    new HttpRequestException(
                        $"transport {responseSecret}")));
        PackageSourceFailure failure =
            (await client.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken)).Failure!;

        string retained =
            $"{failure.Message}|{failure.Source.Producer.Key}|"
            + failure.Source.Producer.Display.ToString();
        Assert.DoesNotContain(pathSecret, retained, StringComparison.Ordinal);
        Assert.DoesNotContain(querySecret, retained, StringComparison.Ordinal);
        Assert.DoesNotContain(
            responseSecret,
            retained,
            StringComparison.Ordinal);
        Assert.Equal(
            "The package source transport failed.",
            failure.Message);
    }

    [Fact]
    public void LegacyPackageSourceIdentityBehaviorRemainsStable()
    {
        PackageSourceIdentity first =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri(
                    "HTTPS://BÜCHER.EXAMPLE:443/feed/%2f/?sig=%2f#frag"));
        PackageSourceIdentity equal =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri(
                    "https://xn--bcher-kva.example/feed/%2F?sig=%2F#frag"));
        PackageSourceIdentity queryDifferent =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri(
                    "https://xn--bcher-kva.example/feed/%2F?sig=other#frag"));

        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, queryDifferent);
        Assert.Equal(first.Value, first.ToString());
        Assert.Equal(
            "https://api.nuget.org:443/v3/index.json",
            PackageSourceIdentity.NuGetOrg.Value);
        Assert.Equal(
            PackageSourceIdentity.NuGetOrg,
            PackageSourceDescriptor.NuGetGallery.Identity);
        Assert.Equal(
            PackageSourceIdentity.NuGetOrg,
            PackageSourceDescriptor.NuGetV3(
                "nuget",
                "NuGet",
                new Uri(
                    "https://api.nuget.org/v3/index.json/")).Identity);
    }

    [Fact]
    public void NuGetFetchCachesDoNotConsumeProducerIdentity()
    {
        Type[] cacheTypes =
        [
            typeof(ResponseCache),
            typeof(PackageCache),
        ];
        Type[] forbidden =
        [
            typeof(PackageProducerIdentity),
            typeof(PackageSourceResultIdentity),
            typeof(PackageSourceAssociation),
        ];

        foreach (Type cacheType in cacheTypes)
        {
            Assert.DoesNotContain(
                cacheType.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field => forbidden.Contains(field.FieldType));
            Assert.DoesNotContain(
                cacheType.GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic)
                    .SelectMany(method => method.GetParameters()),
                parameter => forbidden.Contains(
                    parameter.ParameterType));
        }
    }

    private static PackageProducerIdentity Producer(string endpoint)
    {
        using IPackageSourceClient client = V3(
            endpoint,
            PackageSourceAssociation.Create());
        return client.Source.Producer;
    }

    private static IPackageSourceClient V3(
        string endpoint,
        PackageSourceAssociation association) =>
        PackageSourceClientFactory.Create(
            new PackageSource("test", endpoint),
            association,
            new PassiveHandler());

    private static PackageSourceResultFactory CreateFactory(
        PackageSourceDescriptor? descriptor = null,
        PackageSourceAssociation? association = null)
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                descriptor ?? PackageSourceDescriptor.NuGetGallery,
                association ?? PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyClient(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private static void AssertProducerEqual(
        string first,
        string second)
    {
        PackageProducerIdentity firstProducer = Producer(first);
        PackageProducerIdentity secondProducer = Producer(second);
        Assert.Equal(firstProducer, secondProducer);
        Assert.Equal(firstProducer.Key, secondProducer.Key);
        Assert.Equal(firstProducer.Display, secondProducer.Display);
    }

    private static void AssertProducerDifferent(
        string first,
        string second)
    {
        PackageProducerIdentity firstProducer = Producer(first);
        PackageProducerIdentity secondProducer = Producer(second);
        Assert.NotEqual(firstProducer, secondProducer);
        Assert.NotEqual(firstProducer.Key, secondProducer.Key);
        Assert.NotEqual(firstProducer.Display, secondProducer.Display);
    }

    private static void AssertFactoryAssociation(
        PackageSourceResultFactory factory,
        PackageSourceAssociation association)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Contoso", "1.0.0");
        PackageCandidateObservation candidate = factory.Candidate(
            coordinate,
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Listed);
        PackageSearchResult search = factory.Search(
            [new SearchResult("Contoso", "1.0.0")]);
        PackageVersionResult versions = factory.Versions(
            [candidate],
            hasAuthoritativeListingState: true);
        PackageSourceManifest manifest = factory.Manifest(
            coordinate,
            ReadOnlyMemory<byte>.Empty);
        PackageSourcePayload payload = factory.Payload(
            coordinate,
            PackageSourcePayloadKind.Package,
            new MemoryStream());
        PackageSourceOperationResult<PackageSourcePayload> failed =
            factory.FailedPackage(
                coordinate,
                PackageSourceFailureKind.Transport);

        Assert.Same(association, factory.Source.Association);
        Assert.Same(association, candidate.Source.Association);
        Assert.Same(association, search.Source.Association);
        Assert.Same(
            association,
            Assert.Single(search.Matches).Candidate.Source.Association);
        Assert.Same(association, versions.Source.Association);
        Assert.Same(association, manifest.Source.Association);
        Assert.Same(association, payload.Source.Association);
        Assert.Same(
            association,
            failed.Failure!.Source.Association);
        payload.Content.Dispose();
    }

    private static void AssertCapabilityRejected(
        ConstructorInfo constructor,
        object? capability)
    {
        object?[] arguments =
        [
            .. constructor.GetParameters().Select((parameter, index) =>
                index == 0
                    ? capability
                    : parameter.ParameterType.IsValueType
                        ? Activator.CreateInstance(parameter.ParameterType)
                        : null),
        ];
        TargetInvocationException exception =
            Assert.Throws<TargetInvocationException>(
                () => constructor.Invoke(arguments));
        Assert.IsType<InvalidOperationException>(
            exception.InnerException);
    }

    private static IEnumerable<MemberInfo> PublicSurface(Type type) =>
        type.GetMembers(
            BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.DeclaredOnly);

    private sealed class PassiveHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.NotFound));
    }

    private sealed class ThrowingHandler(Exception failure)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(failure);
    }

    private sealed class FactoryOnlyClient(
        PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
