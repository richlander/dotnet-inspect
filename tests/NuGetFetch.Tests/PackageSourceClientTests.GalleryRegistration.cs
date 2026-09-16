using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed partial class PackageSourceClientTests
{
    [Fact]
    public async Task GalleryClientUsesKnownEndpointsWithoutServiceIndex()
    {
        var handler = new RecordingHandler
        {
            [GallerySearch] = """
                {
                  "data": [
                    {
                      "id": "Contoso",
                      "version": "1.0.0",
                      "authors": ["Contoso"],
                      "owners": ["Contoso", "Partner"]
                    }
                  ]
                }
                """,
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryManifest] = "<package />",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json#identity",
                      "items": [
                        {
                          "catalogEntry": {
                            "version": "1.0.0"
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
            [GalleryPackage] = "package bytes",
            [GallerySymbols] = "symbol bytes",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        Assert.Equal(
            PackageSourceCapabilities.Search
                | PackageSourceCapabilities.VersionEnumeration
                | PackageSourceCapabilities.Manifest
                | PackageSourceCapabilities.PackagePayload
                | PackageSourceCapabilities.SymbolPayload,
            runtime.Capabilities);
        PackageSearchMatch match = Assert.Single(
            Succeeded(
                await runtime.SearchAsync(
                    "contoso",
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Matches);
        Assert.Equal("Contoso", match.Metadata.Id);
        Assert.Equal("contoso", match.Candidate.Coordinate.PackageId);
        Assert.Equal("1.0.0", match.Candidate.Coordinate.Version);
        Assert.Same(runtime.Source, match.Candidate.Source);
        Assert.Equal(
            PackageDiscoveryContract.KeywordSearch,
            match.Candidate.DiscoveryContract);
        Assert.Equal(
            PackageListingState.Listed,
            match.Candidate.ListingState);
        Assert.Equal(["Contoso", "Partner"], match.Metadata.Owners);
        PackageSearchMatch prefixMatch = Assert.Single(
            Succeeded(
                await runtime.SearchByPrefixAsync(
                    "Contoso",
                    take: 1,
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Matches);
        Assert.Equal(
            match.Candidate.Coordinate,
            prefixMatch.Candidate.Coordinate);
        Assert.Equal(
            match.Candidate.DiscoveryContract,
            prefixMatch.Candidate.DiscoveryContract);
        Assert.Equal(
            match.Candidate.ListingState,
            prefixMatch.Candidate.ListingState);
        Assert.Same(
            match.Candidate.Source,
            prefixMatch.Candidate.Source);
        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "Contoso",
                TestContext.Current.CancellationToken));
        PackageCandidateObservation version =
            Assert.Single(versions.Candidates);
        Assert.Equal("1.0.0", version.Coordinate.Version);
        Assert.Equal(
            PackageListingState.Listed,
            version.ListingState);
        Assert.True(versions.HasAuthoritativeListingState);
        PackageSourceManifest manifest = Succeeded(
            await runtime.GetManifestAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(
            "<package />",
            Encoding.UTF8.GetString(manifest.Content.ToArray()));
        Assert.Equal(match.Candidate.Coordinate, manifest.Coordinate);
        Assert.Same(runtime.Source, manifest.Source);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            manifest.Source.TransportKind);
        PackageSourcePayload packagePayload = Succeeded(
            await runtime.GetPackageAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload symbolPayload = Succeeded(
            await runtime.TryGetSymbolsAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        await using Stream package = packagePayload.Content;
        await using Stream symbols = symbolPayload.Content;

        Assert.Equal("package bytes", await ReadAsync(package));
        Assert.Equal("symbol bytes", await ReadAsync(symbols));
        Assert.Equal(
            PackageSourcePayloadKind.Package,
            packagePayload.Kind);
        Assert.Equal(
            PackageSourcePayloadKind.Symbols,
            symbolPayload.Kind);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            packagePayload.Source.TransportKind);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            symbolPayload.Source.TransportKind);
        Assert.Equal("package bytes".Length, packagePayload.AdvertisedLength);
        Assert.Equal("symbol bytes".Length, symbolPayload.AdvertisedLength);
        Assert.Equal(packagePayload.Coordinate, symbolPayload.Coordinate);
        Assert.Same(runtime.Source, packagePayload.Source);
        Assert.Same(runtime.Source, symbolPayload.Source);
        Assert.DoesNotContain(
            handler.Requested,
            url => url.Contains(
                "api.nuget.org/v3/index.json",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handler.Requested,
            url => url.StartsWith(
                $"{GallerySearch}?",
                StringComparison.Ordinal));
        string[] searchRequests =
        [
            .. handler.Requested.Where(
                url => url.StartsWith(
                    GallerySearch,
                    StringComparison.Ordinal)),
        ];
        Assert.Equal(2, searchRequests.Length);
        Assert.All(
            searchRequests,
            searchRequest =>
            {
                Assert.Contains("q=Contoso", searchRequest, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("prerelease=false", searchRequest);
                Assert.Contains("semVerLevel=2.0.0", searchRequest);
            });
        Assert.Equal(
            [
                GalleryVersions,
                GalleryRegistration,
                GalleryManifest,
                GalleryPackage,
                GallerySymbols,
            ],
            handler.Requested.Where(
                url => !url.StartsWith(
                    GallerySearch,
                    StringComparison.Ordinal)));
        Assert.All(handler.Authentication, Assert.Null);
        Assert.All(
            handler.Headers,
            headers =>
            {
                Assert.DoesNotContain("Authorization", headers.Keys);
                Assert.DoesNotContain("Cookie", headers.Keys);
                Assert.DoesNotContain("X-NuGet-ApiKey", headers.Keys);
            });
    }

    [Fact]
    public async Task GalleryEnumerationJoinsAuthoritativeListingState()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] =
                """{"versions":["1.0.0","1.1.0","2.0.0-beta.1"]}""",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "items": [
                        {
                          "catalogEntry": {
                            "version": "1.0",
                            "listed": true
                          }
                        },
                        {
                          "catalogEntry": {
                            "version": "1.1.0",
                            "listed": false
                          }
                        },
                        {
                          "catalogEntry": {
                            "version": "2.0.0-beta.1"
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(
            [
                ("1.0.0", PackageListingState.Listed),
                ("1.1.0", PackageListingState.Unlisted),
                ("2.0.0-beta.1", PackageListingState.Listed),
            ],
            result.Candidates.Select(candidate => (
                candidate.Coordinate.Version,
                candidate.ListingState)));
        Assert.All(
            result.Candidates,
            candidate => Assert.Equal(
                PackageDiscoveryContract.CompleteVersionEnumeration,
                candidate.DiscoveryContract));
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Fact]
    public async Task GalleryExternalRegistrationPageIsValidatedAndRebased()
    {
        const string externalPage =
            "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/2.0.0.json";
        var handler = new RecordingHandler
        {
            [GalleryVersions] =
                """{"versions":["1.0.0","2.0.0"]}""",
            [GalleryRegistration] = $$"""
                {
                  "items": [
                    {
                      "@id": "{{externalPage}}"
                    }
                  ]
                }
                """,
            [GalleryRegistrationPage] = """
                {
                  "items": [
                    {
                      "catalogEntry": {
                        "version": "1.0.0",
                        "listed": false
                      }
                    },
                    {
                      "catalogEntry": {
                        "version": "2.0.0",
                        "listed": true
                      }
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(
            [
                PackageListingState.Unlisted,
                PackageListingState.Listed,
            ],
            result.Candidates.Select(candidate =>
                candidate.ListingState));
        Assert.Equal(
            [
                GalleryVersions,
                GalleryRegistration,
                GalleryRegistrationPage,
            ],
            handler.Requested);
        Assert.DoesNotContain(externalPage, handler.Requested);
    }

    [Theory]
    [InlineData("http://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json")]
    [InlineData("https://user@api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json?secret=x")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json#fragment")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/other/page/1.0.0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/%63ontoso/page/1.0.0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1%2E0%2E0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0%2F2.0.0/2.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/not-page/1.0.0/1.0.0.json")]
    public async Task GalleryRejectsIneligibleExternalRegistrationPage(
        string externalPage)
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = $$"""
                {
                  "items": [
                    {
                      "@id": "{{externalPage}}"
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Fact]
    public async Task GalleryIncompleteRegistrationIsTypedPartialEnumeration()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] =
                """{"versions":["1.0.0","2.0.0"]}""",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "items": [
                        {
                          "catalogEntry": {
                            "version": "1.0.0",
                            "listed": false
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.All(
            result.Candidates,
            candidate => Assert.Equal(
                PackageListingState.Unknown,
                candidate.ListingState));
    }

    [Theory]
    [InlineData("""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": "false"
                  }
                }
              ]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": true
                  }
                },
                {
                  "catalogEntry": {
                    "version": "1.0",
                    "listed": false
                  }
                }
              ]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": false,
                    "listed": true
                  }
                }
              ]
            }
          ]
        }
        """)]
    public async Task GalleryMalformedRegistrationIsTypedPartialEnumeration(
        string registration)
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task GalleryCorruptEncodedVersionMetadataIsInvalidResponse()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryVersions,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ImmediateReadFailureStream(
                        new InvalidDataException(
                            "The encoded response body is corrupt."))),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
    }

    [Fact]
    public async Task GalleryCorruptEncodedRegistrationIsTypedPartialEnumeration()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
        };
        handler.SetResponse(
            GalleryRegistration,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ImmediateReadFailureStream(
                        new InvalidDataException(
                            "The encoded response body is corrupt."))),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task GalleryMalformedExternalPageIsTypedPartialEnumeration()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
                    }
                  ]
                }
                """,
            [GalleryRegistrationPage] = """{"items":{}}""",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task GalleryRegistrationParserRetainsOnlyFlatCandidates()
    {
        const string page = """
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": false
                  }
                },
                {
                  "catalogEntry": {
                    "version": "2.0.0",
                    "listed": true
                  }
                }
              ]
            }
            """;
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(page));
        var candidates = new HashSet<string>(
            ["1.0.0"],
            StringComparer.OrdinalIgnoreCase);
        var budget =
            new NuGetGalleryRegistrationBudget(
                candidates.Count,
                NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        using var operation = CreateRegistrationParserOperation(
            TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, PackageListingState> listings =
            await NuGetGalleryRegistration.DeserializePageAsync(
                json,
                candidates,
                budget,
                operation,
                TestContext.Current.CancellationToken);

        KeyValuePair<string, PackageListingState> listing =
            Assert.Single(listings);
        Assert.Equal("1.0.0", listing.Key);
        Assert.Equal(PackageListingState.Unlisted, listing.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryRegistrationTraversalHonorsCallerCancellation(
        bool inline)
    {
        const int itemCount = 512;
        string items = RegistrationItems(itemCount);
        using var cancellation = new CancellationTokenSource();
        var candidates = new InterruptingReadOnlySet(
            itemCount,
            cancellation.Cancel);
        var budget =
            new NuGetGalleryRegistrationBudget(
                candidates.Count,
                NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        using var operation =
            CreateRegistrationParserOperation(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DeserializeRegistrationItemsAsync(
                items,
                inline,
                candidates,
                budget,
                operation,
                cancellation.Token));

        Assert.Equal(128, candidates.ContainsCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryRegistrationTraversalUsesMonotonicDeadline(
        bool inline)
    {
        const int itemCount = 512;
        string items = RegistrationItems(itemCount);
        var candidates = new InterruptingReadOnlySet(
            itemCount,
            () => Thread.Sleep(TimeSpan.FromMilliseconds(250)));
        var budget =
            new NuGetGalleryRegistrationBudget(
                candidates.Count,
                NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        using var operation = new NuGetOperationDeadline(
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromMilliseconds(100),
            },
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => DeserializeRegistrationItemsAsync(
                    items,
                    inline,
                    candidates,
                    budget,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(100), error.Timeout);
        Assert.Equal(128, candidates.ContainsCalls);
    }

    [Fact]
    public async Task GalleryRegistrationLeafLimitIsTypedPartialEnumeration()
    {
        string extraItems = string.Join(
            ",",
            Enumerable.Range(
                2,
                NuGetGalleryRegistrationBudget.MinimumLeafCount)
                .Select(version =>
                    $$"""
                      {
                        "catalogEntry": {
                          "version": "{{version}}.0.0"
                        }
                      }
                      """));
        string registration = $$"""
            {
              "items": [
                {
                  "items": [
                    {
                      "catalogEntry": {
                        "version": "1.0.0"
                      }
                    },
                    {{extraItems}}
                  ]
                }
              ]
            }
            """;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Fact]
    public async Task GalleryRegistrationAggregateByteLimitIsTypedPartialEnumeration()
    {
        const int maximumBytes = 512;
        const string firstPage =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json";
        const string secondPage =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.1/1.0.1.json";
        string padding = new('a', 220);
        string registration = $$"""
            {
              "items": [
                {
                  "@id": "{{firstPage}}"
                },
                {
                  "@id": "{{secondPage}}"
                }
              ]
            }
            """;
        string page = $$"""
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "padding": "{{padding}}"
                  }
                }
              ]
            }
            """;
        Assert.InRange(
            Encoding.UTF8.GetByteCount(registration),
            1,
            maximumBytes);
        Assert.InRange(
            Encoding.UTF8.GetByteCount(page),
            1,
            maximumBytes);
        Assert.True(
            Encoding.UTF8.GetByteCount(registration)
            + (2 * Encoding.UTF8.GetByteCount(page))
            > maximumBytes);
        Assert.True(
            2 * Encoding.UTF8.GetByteCount(page)
            < 1_024);
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
            [firstPage] = page,
            [secondPage] = page,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = 1_024,
                    MaxRegistrationMetadataBytes = maximumBytes,
                });

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [
                GalleryVersions,
                GalleryRegistration,
                firstPage,
                secondPage,
            ],
            handler.Requested);
    }

    [Fact]
    public async Task
        GalleryRegistrationDefaultAggregateCoversMeasuredMassTransitCanary()
    {
        const int pageCount = 25;
        const int measuredMassTransitBytes = 18_163_736;
        string padding = new('a', 740_000);
        string page = $$"""
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "padding": "{{padding}}"
                  }
                }
              ]
            }
            """;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
        };
        var indexPages = new string[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            string version = $"1.0.{i}";
            string path =
                "/v3/registration5-gz-semver2/contoso/page/"
                + $"{version}/{version}.json";
            indexPages[i] =
                $$"""{"@id":"https://api.nuget.org{{path}}"}""";
            handler[$"https://globalcdn.nuget.org{path}"] = page;
        }

        string registration =
            $$"""{"items":[{{string.Join(",", indexPages)}}]}""";
        long registrationBytes =
            Encoding.UTF8.GetByteCount(registration)
            + ((long)pageCount * Encoding.UTF8.GetByteCount(page));
        Assert.True(
            registrationBytes
            > NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        Assert.True(registrationBytes >= measuredMassTransitBytes);
        Assert.True(
            registrationBytes
            < NuGetFetchOptions.DefaultMaxRegistrationMetadataBytes);
        handler[GalleryRegistration] = registration;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Listed,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task
        GalleryRegistrationDefaultBatchExceedsPerResponseLimit()
    {
        const int pageCount = 8;
        string padding = new('a', 2_100_000);
        string page = $$"""
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "padding": "{{padding}}"
                  }
                }
              ]
            }
            """;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
        };
        var indexPages = new string[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            string version = $"1.0.{i}";
            string path =
                "/v3/registration5-gz-semver2/contoso/page/"
                + $"{version}/{version}.json";
            indexPages[i] =
                $$"""{"@id":"https://api.nuget.org{{path}}"}""";
            handler[$"https://globalcdn.nuget.org{path}"] = page;
        }

        int pageBytes = Encoding.UTF8.GetByteCount(page);
        long batchBytes = (long)pageCount * pageBytes;
        Assert.True(
            pageBytes
            < NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        Assert.True(
            batchBytes
            > NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        Assert.True(
            batchBytes
            < NuGetFetchOptions.DefaultMaxRegistrationPageBatchBytes);
        handler[GalleryRegistration] =
            $$"""{"items":[{{string.Join(",", indexPages)}}]}""";
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
    }

    [Fact]
    public async Task GalleryRegistrationReservationWaitsForReturnedCapacity()
    {
        var budget = new NuGetGalleryRegistrationBudget(
            candidateCount: 1,
            maximumBytes: 2);
        byte[] buffer = new byte[1];
        using Stream first = budget.LimitBytes(
            new MemoryStream([(byte)'a']));
        Assert.Equal(
            1,
            await first.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken));
        var blockedEof = new BlockingEofStream();
        using Stream eof = budget.LimitBytes(blockedEof);
        Task<int> eofRead = eof.ReadAsync(
            buffer,
            TestContext.Current.CancellationToken).AsTask();
        await blockedEof.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        using Stream final = budget.LimitBytes(
            new MemoryStream([(byte)'b']));
        Task<int> finalRead = final.ReadAsync(
            buffer,
            TestContext.Current.CancellationToken).AsTask();
        Assert.False(finalRead.IsCompleted);

        blockedEof.Release.TrySetResult();

        Assert.Equal(0, await eofRead);
        Assert.Equal(1, await finalRead);
    }

    [Fact]
    public async Task
        GalleryRegistrationMaterializationBudgetReturnsFailedAttemptCapacity()
    {
        var budget = new NuGetGalleryRegistrationByteBudget(
            maximumBytes: 2);

        await Assert.ThrowsAsync<IOException>(
            () => budget.MaterializeAsync(
                new ReadThenFailureStream([(byte)'a', (byte)'b']),
                TestContext.Current.CancellationToken));

        using NuGetGalleryRegistrationByteBudget.Materialization
            materialization = await budget.MaterializeAsync(
            new MemoryStream([(byte)'c', (byte)'d']),
            TestContext.Current.CancellationToken);
        using MemoryStream destination = materialization.Commit();

        Assert.Equal("cd", Encoding.UTF8.GetString(destination.ToArray()));

        await Assert.ThrowsAsync<
            NuGetRegistrationResourceLimitExceededException>(
            () => budget.MaterializeAsync(
                new MemoryStream([(byte)'e']),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        GalleryLatePageDeadlineReturnsMaterializationCapacity(
            bool metadataBodyDeadline)
    {
        const string page = """
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0"
                  }
                }
              ]
            }
            """;
        string registration = $$"""
            {
              "items": [
                {
                  "@id": "{{GalleryRegistrationPage}}"
                }
              ]
            }
            """;
        byte[] pageBytes = Encoding.UTF8.GetBytes(page);
        int pageRequests = 0;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        handler.SetResponse(
            GalleryRegistrationPage,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Interlocked.Increment(ref pageRequests) == 1
                    ? new StreamContent(
                        new LateEofStream(
                            pageBytes,
                            TimeSpan.FromMilliseconds(100)))
                    : new ByteArrayContent(pageBytes),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = Math.Max(
                        pageBytes.Length,
                        Encoding.UTF8.GetByteCount(registration)),
                    MaxRegistrationPageBatchBytes = pageBytes.Length,
                    MaxRegistrationMetadataBytes =
                        Encoding.UTF8.GetByteCount(registration)
                        + (2L * pageBytes.Length),
                    RequestTimeout = metadataBodyDeadline
                        ? TimeSpan.FromSeconds(1)
                        : TimeSpan.FromMilliseconds(40),
                    OperationTimeout = TimeSpan.FromSeconds(3),
                    MetadataBodyTimeout = metadataBodyDeadline
                        ? TimeSpan.FromMilliseconds(40)
                        : Timeout.InfiniteTimeSpan,
                });

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(2, pageRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        GalleryCleanupFailureReturnsMaterializationCapacity(
            bool responseCleanup)
    {
        const string page = """
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0"
                  }
                }
              ]
            }
            """;
        string registration = $$"""
            {
              "items": [
                {
                  "@id": "{{GalleryRegistrationPage}}"
                }
              ]
            }
            """;
        byte[] pageBytes = Encoding.UTF8.GetBytes(page);
        int pageRequests = 0;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        handler.SetResponse(
            GalleryRegistrationPage,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Interlocked.Increment(ref pageRequests) == 1
                    ? new CleanupFailureContent(
                        pageBytes,
                        responseCleanup)
                    : new ByteArrayContent(pageBytes),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = Math.Max(
                        pageBytes.Length,
                        Encoding.UTF8.GetByteCount(registration)),
                    MaxRegistrationPageBatchBytes = pageBytes.Length,
                    MaxRegistrationMetadataBytes =
                        Encoding.UTF8.GetByteCount(registration)
                        + (2L * pageBytes.Length),
                    RequestTimeout = TimeSpan.FromSeconds(1),
                    OperationTimeout = TimeSpan.FromSeconds(3),
                });

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(2, pageRequests);
    }

    [Fact]
    public async Task GalleryRegistrationAggregateCountsFailedAttemptBytes()
    {
        var budget = new NuGetGalleryRegistrationByteBudget(
            maximumBytes: 2);

        using (Stream failedAttempt = budget.LimitBytes(
            new ReadThenFailureStream([(byte)'a'])))
        {
            await Assert.ThrowsAsync<IOException>(
                () => failedAttempt.CopyToAsync(
                    new MemoryStream(),
                    TestContext.Current.CancellationToken));
        }

        using Stream retry = budget.LimitBytes(
            new MemoryStream([(byte)'b', (byte)'c']));
        await Assert.ThrowsAsync<
            NuGetRegistrationResourceLimitExceededException>(
            () => retry.CopyToAsync(
                new MemoryStream(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GalleryRegistrationPageLimitIsTypedPartialEnumeration()
    {
        const string externalPage =
            """
            {
              "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
            }
            """;
        string pages = string.Join(
            ",",
            Enumerable.Repeat(
                externalPage,
                NuGetGalleryRegistrationBudget.MaximumPageCount + 1));
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = $$"""{"items":[{{pages}}]}""",
            [GalleryRegistrationPage] = """
                {
                  "items": [
                    {
                      "catalogEntry": {
                        "version": "1.0.0"
                      }
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrationResourceLimitsMapToResponseRejected(
        bool pageLimit)
    {
        PackageSourceDescriptor descriptor =
            PackageSourceDescriptor.NuGetGallery;
        var budget = new NuGetGalleryRegistrationBudget(
            candidateCount: 1,
            maximumBytes: 1);
        PackageSourceResultFactory results =
            CreateResultFactory(descriptor);

        PackageSourceFailure failure = Failed(
            await PackageSourceOperation.CaptureVersionsAsync(
                results,
                () =>
                {
                    if (pageLimit)
                    {
                        budget.EnsurePageCount(
                            NuGetGalleryRegistrationBudget.MaximumPageCount
                            + 1);
                    }
                    else
                    {
                        for (int i = 0;
                             i <= NuGetGalleryRegistrationBudget
                                 .MinimumLeafCount;
                             i++)
                        {
                            budget.ObserveLeaf();
                        }
                    }

                    return Task.FromResult(
                        results.Versions(
                            [],
                            hasAuthoritativeListingState: false));
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            failure.Kind);
    }

    [Fact]
    public async Task GalleryExternalPagesUseBoundedConcurrency()
    {
        var handler = new ConcurrentRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(9, result.Candidates.Count);
        Assert.Equal(9, handler.PageRequests);
        Assert.Equal(8, handler.MaxActivePageRequests);
    }

    [Fact]
    public async Task GalleryCallerCancellationDuringRegistrationRemainsCancellation()
    {
        var handler = new CancelableRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        using var cancellation = new CancellationTokenSource();
        Task<PackageSourceOperationResult<PackageVersionResult>> operation =
            runtime.GetVersionsAsync(
                "contoso",
                cancellation.Token);
        await handler.RegistrationStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
    }

    [Fact]
    public async Task GallerySharedContextCallerCancellationDuringRegistrationRemainsCancellation()
    {
        var handler = new CancelableRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        using var cancellation = new CancellationTokenSource();
        using var context = new NuGetOperationContext(cancellation.Token);
#pragma warning disable xUnit1051 // The default invocation token is the contract under test.
        Task<PackageSourceOperationResult<PackageVersionResult>> operation =
            runtime.GetVersionsAsync(
                "contoso",
                operationContext: context);
#pragma warning restore xUnit1051
        await handler.RegistrationStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => operation);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task GalleryCallerCancellationOutranksConcurrentRegistrationFault()
    {
        var handler = new FaultAndCancelRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        using var cancellation = new CancellationTokenSource();
        Task<PackageSourceOperationResult<PackageVersionResult>> operation =
            runtime.GetVersionsAsync(
                "contoso",
                cancellation.Token);
        await handler.BothPagesStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();
        handler.ReleaseFault.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryConcurrentTransportFaultCannotHideTimeout(
        bool operationExpires)
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(60),
            OperationTimeout = operationExpires
                ? TimeSpan.FromMilliseconds(800)
                : TimeSpan.FromSeconds(5),
        };
        var handler = new FaultAndTimeoutRegistrationHandler();
        using var context = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: context));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.True(handler.FastTransportRequests > 0);
        Assert.True(handler.StallingRequests > 0);
    }

    [Fact]
    public async Task GalleryConcurrentTransportFaultCannotHideTransportTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromSeconds(5),
        };
        var handler = new FaultAndTimeoutRegistrationHandler(
            transportTimeout: true);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.True(handler.FastTransportRequests > 0);
        Assert.True(handler.StallingRequests > 0);
    }

    [Fact]
    public async Task GalleryConcurrentTransportFaultCannotHideCanceledTransportTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromSeconds(5),
        };
        var handler = new FaultAndTimeoutRegistrationHandler(
            canceledTransportTimeout: true);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.True(handler.FastTransportRequests > 0);
        Assert.True(handler.StallingRequests > 0);
    }

    [Fact]
    public async Task GalleryLateProtocolFailureCannotBecomePartial()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateMalformedRegistrationHandler(),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
    }

    [Fact]
    public async Task GalleryLateMetadataProtocolFailurePreservesBodyDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromSeconds(2),
            MetadataBodyTimeout = TimeSpan.FromMilliseconds(20),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateMalformedRegistrationHandler(),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
    }

    [Fact]
    public async Task GalleryLateInvalidDataPreservesRequestDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(5),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateInvalidDataRegistrationHandler(),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GalleryLateStreamingTimeoutPreservesDeadline(
        bool operationExpires,
        bool canceledTransportTimeout)
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = operationExpires
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromMilliseconds(40),
            OperationTimeout = operationExpires
                ? TimeSpan.FromMilliseconds(40)
                : TimeSpan.FromSeconds(2),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateStreamingTimeoutHandler(
                    canceledTransportTimeout),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
    }

    [Fact]
    public void GalleryFinalListingProjectionPreservesOperationTimeout()
    {
        PackageSourceResultFactory results = CreateResultFactory();
        PackageCandidateObservation candidate = results.Candidate(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Unknown);
        PackageVersionResult partial = results.Versions(
            [candidate],
            hasAuthoritativeListingState: false);
        var listings =
            new Dictionary<string, PackageListingState>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["1.0.0"] = PackageListingState.Listed,
            };
        using var operation = new NuGetOperationDeadline(
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(1),
                OperationTimeout = TimeSpan.FromMilliseconds(20),
            },
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);
        Thread.Sleep(100);

        NuGetOperationTimeoutException error =
            Assert.Throws<NuGetOperationTimeoutException>(
                () => NuGetGalleryPackageSourceClient
                    .ApplyRegistrationListingsOrPartial(
                        results,
                        partial,
                        listings,
                        operation,
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(20), error.Timeout);
    }
}
