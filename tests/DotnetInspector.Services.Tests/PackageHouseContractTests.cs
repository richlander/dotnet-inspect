using System.Reflection;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageHouseContractTests
{
    private static readonly PackageSourceCoordinate Coordinate =
        PackageSourceCoordinate.Create("contoso.json", "4.0.0");

    [Fact]
    public void RequestFloorAcceptsOnlyAnExactDemand()
    {
        var demand = new PackageHouseDemand.Exact(Coordinate);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("NET10.0", "linux-x64");
        PackageHouseRequestAssociation association =
            PackageHouseRequestAssociation.Create();

        var request = new PackageHouseRequest(
            demand,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            target,
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries,
            association);

        Assert.Same(Coordinate, demand.Coordinate);
        Assert.Equal("net10.0", request.TargetContext!.RequestedFramework);
        Assert.Equal("linux-x64", request.TargetContext.RuntimeIdentifier);
        Assert.Same(association, request.Association);
        Assert.Equal(
            ["Exact", "Selecting"],
            typeof(PackageHouseDemand)
                .GetNestedTypes(BindingFlags.Public)
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void VersionSelectionRequestsPreserveDistinctPolicy()
    {
        var stable = new PackageVersionSelectionRequest.LatestStable(
            "Contoso.Json");
        var prerelease =
            new PackageVersionSelectionRequest.LatestPrerelease(
                "Contoso.Json");
        var always = new PackageVersionSelectionRequest.AlwaysLatest(
            "Contoso.Json",
            includePrerelease: true);
        var wildcard = new PackageVersionSelectionRequest.Wildcard(
            "Contoso.Json",
            "4.0.");
        Assert.True(PackageVersionRange.TryParse(
            "Contoso.Json@4.0.0..5.0.0",
            out PackageVersionRange? versionRange,
            out string? error),
            error);
        var range = new PackageVersionSelectionRequest.Range(
            versionRange!,
            new PackageVersionRangeSelection.Ordinal(2));

        Assert.Equal("contoso.json", stable.PackageId);
        Assert.False(stable.Discovery.IncludePrerelease);
        Assert.Equal(
            PackageVersionDiscoveryFreshness.Current,
            stable.Discovery.Freshness);
        Assert.True(prerelease.Discovery.IncludePrerelease);
        Assert.Equal(
            PackageVersionDiscoveryFreshness.RefreshedForRequest,
            always.Discovery.Freshness);
        Assert.True(always.Discovery.IncludePrerelease);
        Assert.Equal("4.0.", wildcard.VersionPrefix);
        Assert.True(wildcard.Discovery.IncludePrerelease);
        Assert.Same(versionRange, range.VersionRange);
        Assert.Equal(
            2,
            Assert.IsType<PackageVersionRangeSelection.Ordinal>(
                range.Selection).Value);
        Assert.True(range.Discovery.RequiresListedVersions);
        Assert.True(range.Discovery.RequiresEveryConfiguredAuthority);
        Assert.True(range.Discovery.RequiresCompleteCandidateSet);
    }

    [Fact]
    public void VersionResolutionSelectsEachRequestForm()
    {
        PackageVersionDiscoveryResult allVersions = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: true,
            hasAnyCandidate: true,
            "3.0.0-preview.1",
            "2.0.0",
            "1.5.0",
            "1.0.0");
        PackageVersionResolutionReceipt.Resolved stable = Assert.IsType<
            PackageVersionResolutionReceipt.Resolved>(
            PackageVersionSelectionResolver.Resolve(
                new PackageVersionSelectionRequest.LatestStable(
                    "contoso.json"),
                allVersions,
                PackageVersionDiscoveryFreshness.Current));
        PackageVersionResolutionReceipt.Resolved prerelease = Assert.IsType<
            PackageVersionResolutionReceipt.Resolved>(
            PackageVersionSelectionResolver.Resolve(
                new PackageVersionSelectionRequest.LatestPrerelease(
                    "contoso.json"),
                allVersions,
                PackageVersionDiscoveryFreshness.Current));
        PackageVersionResolutionReceipt.Resolved always = Assert.IsType<
            PackageVersionResolutionReceipt.Resolved>(
            PackageVersionSelectionResolver.Resolve(
                new PackageVersionSelectionRequest.AlwaysLatest(
                    "contoso.json",
                    includePrerelease: true),
                allVersions,
                PackageVersionDiscoveryFreshness.RefreshedForRequest));
        PackageVersionResolutionReceipt.Resolved wildcard = Assert.IsType<
            PackageVersionResolutionReceipt.Resolved>(
            PackageVersionSelectionResolver.Resolve(
                new PackageVersionSelectionRequest.Wildcard(
                    "contoso.json",
                    "1."),
                allVersions,
                PackageVersionDiscoveryFreshness.Current));
        Assert.True(PackageVersionRange.TryParse(
            "contoso.json@1.0.0..2.0.0",
            out PackageVersionRange? versionRange,
            out string? error),
            error);
        PackageVersionResolutionReceipt.Resolved range = Assert.IsType<
            PackageVersionResolutionReceipt.Resolved>(
            PackageVersionSelectionResolver.Resolve(
                new PackageVersionSelectionRequest.Range(
                    versionRange!,
                    new PackageVersionRangeSelection.Ordinal(2)),
                allVersions,
                PackageVersionDiscoveryFreshness.Current));

        Assert.Equal("2.0.0", stable.Coordinate.Version);
        Assert.Equal("3.0.0-preview.1", prerelease.Coordinate.Version);
        Assert.Equal("3.0.0-preview.1", always.Coordinate.Version);
        Assert.Equal("1.5.0", wildcard.Coordinate.Version);
        Assert.Equal("1.5.0", range.Coordinate.Version);
        Assert.All(
            new[] { stable, prerelease, always, wildcard, range },
            receipt => Assert.Same(
                receipt.Discovery,
                allVersions));
        Assert.All(
            new[] { stable, prerelease, always, wildcard, range },
            receipt => Assert.Equal(
                PackageAcquisitionCandidateKind.Discovered,
                receipt.Candidate.Kind));
    }

    [Fact]
    public void RangeSelectionPreservesDirectionAndTypedAddresses()
    {
        PackageVersionDiscoveryResult discovery = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: true,
            hasAnyCandidate: true,
            "2.0.0",
            "1.5.0",
            "1.0.0");
        Assert.True(PackageVersionRange.TryParse(
            "contoso.json@2.0.0..1.0.0",
            out PackageVersionRange? versionRange,
            out string? error),
            error);

        string Resolve(PackageVersionRangeSelection selection) =>
            new PackageVersionResolutionReceipt.Resolved(
                new PackageVersionSelectionRequest.Range(
                    versionRange!,
                    selection),
                discovery,
                PackageVersionDiscoveryFreshness.Current)
            .Coordinate.Version;

        Assert.Equal(
            "2.0.0",
            Resolve(new PackageVersionRangeSelection.First()));
        Assert.Equal(
            "1.0.0",
            Resolve(new PackageVersionRangeSelection.Last()));
        Assert.Equal(
            "1.5.0",
            Resolve(new PackageVersionRangeSelection.Ordinal(2)));
        Assert.Equal(
            "1.5.0",
            Resolve(new PackageVersionRangeSelection.Exact("1.5.0")));
    }

    [Fact]
    public void AlwaysLatestRequiresDiscoveryRefreshedForTheExactRequest()
    {
        var request = new PackageVersionSelectionRequest.AlwaysLatest(
            "contoso.json");
        PackageVersionDiscoveryResult discovery = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: true,
            "4.0.0");

        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.Resolved(
                request,
                discovery,
                PackageVersionDiscoveryFreshness.Current));

        var resolved = new PackageVersionResolutionReceipt.Resolved(
            request,
            discovery,
            PackageVersionDiscoveryFreshness.RefreshedForRequest);
        Assert.Same(request, resolved.Request);
        Assert.Equal("4.0.0", resolved.Coordinate.Version);
    }

    [Fact]
    public void VersionResolutionRequiresCompleteCompatibleDiscovery()
    {
        var stable = new PackageVersionSelectionRequest.LatestStable(
            "contoso.json");
        var prerelease =
            new PackageVersionSelectionRequest.LatestPrerelease(
                "contoso.json");
        PackageVersionDiscoveryResult limited = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            includeUnlisted: false,
            limit: 1,
            hasAnyCandidate: true,
            "4.0.0");
        PackageVersionDiscoveryResult unlisted = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            includeUnlisted: true,
            limit: null,
            hasAnyCandidate: true,
            "4.0.0");
        PackageVersionDiscoveryResult stableOnly = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: true,
            "4.0.0");

        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.Resolved(
                stable,
                limited,
                PackageVersionDiscoveryFreshness.Current));
        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.Resolved(
                stable,
                unlisted,
                PackageVersionDiscoveryFreshness.Current));
        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.Resolved(
                prerelease,
                stableOnly,
                PackageVersionDiscoveryFreshness.Current));
    }

    [Fact]
    public void VersionResolutionDistinguishesAbsenceFromNoMatch()
    {
        var request = new PackageVersionSelectionRequest.LatestStable(
            "contoso.json");
        PackageVersionDiscoveryResult absent = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: false);
        PackageVersionDiscoveryResult prereleaseOnly = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: true);

        var notFound = new PackageVersionResolutionReceipt.NotFound(
            request,
            absent,
            PackageVersionDiscoveryFreshness.Current,
            Reason("No configured authority reported the package."));
        var noMatch = new PackageVersionResolutionReceipt.NoMatch(
            request,
            prereleaseOnly,
            PackageVersionDiscoveryFreshness.Current,
            Reason("Only prerelease versions were reported."));

        Assert.Same(absent, notFound.Discovery);
        Assert.Same(prereleaseOnly, noMatch.Discovery);
        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.NotFound(
                request,
                prereleaseOnly,
                PackageVersionDiscoveryFreshness.Current,
                Reason("Wrong outcome.")));
        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.NoMatch(
                request,
                absent,
                PackageVersionDiscoveryFreshness.Current,
                Reason("Wrong outcome.")));
    }

    [Fact]
    public void VersionDiscoveryRetainsPackageIdentityWithoutCandidates()
    {
        PackageVersionDiscoveryResult absent = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: false);
        PackageVersionDiscoveryResult failed = VersionDiscovery(
            PackageVersionDiscoveryState.Failed,
            includePrerelease: false,
            hasAnyCandidate: false);

        Assert.Equal("contoso.json", absent.PackageId);
        Assert.Equal("contoso.json", failed.PackageId);
    }

    [Fact]
    public void VersionDiscoveryReservesMissingIdentityForInputFailure()
    {
        var inputFailure = new PackageVersionDiscoveryResult(
            packageId: null,
            PackageVersionDiscoveryState.Failed,
            sourceListings: [],
            failures:
            [
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Input,
                    "The package ID is invalid."),
            ],
            hasAnyCandidate: false);

        Assert.Null(inputFailure.PackageId);
        Assert.Throws<ArgumentException>(
            () => new PackageVersionDiscoveryResult(
                packageId: null,
                PackageVersionDiscoveryState.Authoritative,
                sourceListings: [],
                failures: [],
                hasAnyCandidate: false));
    }

    [Fact]
    public void VersionDiscoveryRejectsCandidateForAnotherPackage()
    {
        var authority = new ConfiguredPackageAuthority(
            new PackageSource(
                "version-selection",
                "https://versions.example/v3/index.json"));
        PackageSourceResultFactory factory = ResultFactory(
            authority.Association);
        var candidate = new ConfiguredPackageCandidateObservation(
            authority,
            factory.Candidate(
                PackageSourceCoordinate.Create(
                    "other.package",
                    "4.0.0"),
                PackageDiscoveryContract.CompleteVersionEnumeration,
                PackageListingState.Listed));

        Assert.Throws<ArgumentException>(
            () => new PackageVersionDiscoveryResult(
                "contoso.json",
                PackageVersionDiscoveryState.Authoritative,
                [
                    new PackageVersionSourceInfo(
                        "4.0.0",
                        "version-selection",
                        Listed: true),
                ],
                failures: [],
                hasAnyCandidate: true,
                candidates: [candidate],
                contract: PackageVersionDiscoveryContract.Create(
                    includePrerelease: false,
                    includeUnlisted: false,
                    limit: null),
                candidateIssuer: new object()));
    }

    [Fact]
    public void PartialDiscoveryCannotProduceASelectedCoordinate()
    {
        var request = new PackageVersionSelectionRequest.LatestStable(
            "contoso.json");
        PackageVersionDiscoveryResult partial = VersionDiscovery(
            PackageVersionDiscoveryState.Partial,
            includePrerelease: false,
            hasAnyCandidate: true,
            "4.0.0");

        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.Resolved(
                request,
                partial,
                PackageVersionDiscoveryFreshness.Current));

        var incomplete = new PackageVersionResolutionReceipt.Incomplete(
            request,
            partial,
            PackageVersionDiscoveryFreshness.Current,
            Reason("One required authority timed out."));
        Assert.Equal(
            "version-selection",
            Assert.Single(incomplete.Discovery.SourceListings).Feed);
        Assert.Equal(
            "timeout-authority",
            Assert.Single(incomplete.Discovery.Failures)
                .Authority.ToString());
        Assert.Equal(
            PackageAuthorityFailureKind.Timeout,
            Assert.Single(incomplete.Discovery.Failures).Kind);
    }

    [Fact]
    public void VersionResolutionTerminalFamilyRetainsTypedEvidence()
    {
        var request = new PackageVersionSelectionRequest.LatestStable(
            "contoso.json");
        PackageVersionDiscoveryResult authoritative = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: true);
        PackageVersionDiscoveryResult failed = VersionDiscovery(
            PackageVersionDiscoveryState.Failed,
            includePrerelease: false,
            hasAnyCandidate: false);
        InertString reason = Reason("Version resolution stopped.");
        PackageVersionResolutionReceipt[] receipts =
        [
            new PackageVersionResolutionReceipt.NoMatch(
                request,
                authoritative,
                PackageVersionDiscoveryFreshness.Current,
                reason),
            new PackageVersionResolutionReceipt.Ambiguous(
                request,
                authoritative,
                PackageVersionDiscoveryFreshness.Current,
                reason),
            new PackageVersionResolutionReceipt.Rejected(
                request,
                authoritative,
                PackageVersionDiscoveryFreshness.Current,
                reason),
            new PackageVersionResolutionReceipt.Unavailable(
                request,
                failed,
                PackageVersionDiscoveryFreshness.NotEstablished,
                reason),
            new PackageVersionResolutionReceipt.Failed(
                request,
                failed,
                PackageVersionDiscoveryFreshness.NotEstablished,
                reason),
        ];

        Assert.All(receipts, receipt =>
        {
            Assert.Same(request, receipt.Request);
            Assert.Contains(
                receipt.Discovery,
                new[] { authoritative, failed });
        });
    }

    [Fact]
    public void VersionSelectionResolverMapsTerminalEvidence()
    {
        var request = new PackageVersionSelectionRequest.LatestStable(
            "contoso.json");
        PackageVersionDiscoveryResult absent = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: false);
        PackageVersionDiscoveryResult noMatch = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: true);
        PackageVersionDiscoveryResult partial = VersionDiscovery(
            PackageVersionDiscoveryState.Partial,
            includePrerelease: false,
            hasAnyCandidate: true,
            "4.0.0");
        PackageVersionDiscoveryResult unavailable =
            VersionDiscoveryWithFailure(
                PackageAuthorityFailureKind.Unsupported);
        PackageVersionDiscoveryResult rejected =
            VersionDiscoveryWithFailure(
                PackageAuthorityFailureKind.InvalidResponse);
        PackageVersionDiscoveryResult failed =
            VersionDiscoveryWithFailure(
                PackageAuthorityFailureKind.Transport);
        PackageVersionDiscoveryResult mixedFailure =
            VersionDiscoveryWithFailure(
                PackageAuthorityFailureKind.IncompleteMetadata,
                PackageAuthorityFailureKind.Timeout);

        PackageVersionResolutionReceipt.NotFound notFoundResult =
            Assert.IsType<PackageVersionResolutionReceipt.NotFound>(
            PackageVersionSelectionResolver.Resolve(
                request,
                absent,
                PackageVersionDiscoveryFreshness.Current));
        Assert.IsType<PackageVersionResolutionReceipt.NoMatch>(
            PackageVersionSelectionResolver.Resolve(
                request,
                noMatch,
                PackageVersionDiscoveryFreshness.Current));
        Assert.IsType<PackageVersionResolutionReceipt.Incomplete>(
            PackageVersionSelectionResolver.Resolve(
                request,
                partial,
                PackageVersionDiscoveryFreshness.Current));
        Assert.IsType<PackageVersionResolutionReceipt.Unavailable>(
            PackageVersionSelectionResolver.Resolve(
                request,
                unavailable,
                PackageVersionDiscoveryFreshness.NotEstablished));
        Assert.IsType<PackageVersionResolutionReceipt.Rejected>(
            PackageVersionSelectionResolver.Resolve(
                request,
                rejected,
                PackageVersionDiscoveryFreshness.NotEstablished));
        Assert.IsType<PackageVersionResolutionReceipt.Failed>(
            PackageVersionSelectionResolver.Resolve(
                request,
                failed,
                PackageVersionDiscoveryFreshness.NotEstablished));
        Assert.IsType<PackageVersionResolutionReceipt.Failed>(
            PackageVersionSelectionResolver.Resolve(
                request,
                mixedFailure,
                PackageVersionDiscoveryFreshness.NotEstablished));
        Assert.Same(request, notFoundResult.Request);
        Assert.Same(absent, notFoundResult.Discovery);
    }

    [Fact]
    public void VersionSelectionResolverRejectsIncompatibleDiscovery()
    {
        var request = new PackageVersionSelectionRequest.AlwaysLatest(
            "contoso.json",
            includePrerelease: true);
        PackageVersionDiscoveryResult stale = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: true,
            hasAnyCandidate: true,
            "4.0.0-preview.1");
        PackageVersionDiscoveryResult limited = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: true,
            includeUnlisted: false,
            limit: 1,
            hasAnyCandidate: true,
            "4.0.0-preview.1");

        PackageVersionResolutionReceipt.Rejected staleResult = Assert.IsType<
            PackageVersionResolutionReceipt.Rejected>(
            PackageVersionSelectionResolver.Resolve(
                request,
                stale,
                PackageVersionDiscoveryFreshness.Current));
        PackageVersionResolutionReceipt.Rejected limitedResult =
            Assert.IsType<PackageVersionResolutionReceipt.Rejected>(
                PackageVersionSelectionResolver.Resolve(
                    request,
                    limited,
                    PackageVersionDiscoveryFreshness.RefreshedForRequest));

        Assert.Same(request, staleResult.Request);
        Assert.Same(stale, staleResult.Discovery);
        Assert.Same(limited, limitedResult.Discovery);
    }

    [Fact]
    public void VersionSelectionResolverRejectsMissingOrMismatchedDiscoveryIdentity()
    {
        var request = new PackageVersionSelectionRequest.LatestStable(
            "contoso.json");
        var mismatchedAuthoritative = new PackageVersionDiscoveryResult(
            "other.package",
            PackageVersionDiscoveryState.Authoritative,
            sourceListings: [],
            failures: [],
            hasAnyCandidate: false,
            contract: PackageVersionDiscoveryContract.Create(
                includePrerelease: false,
                includeUnlisted: false,
                limit: null));
        PackageVersionDiscoveryResult mismatchedFailure =
            VersionDiscoveryWithFailureForPackage(
                "other.package",
                PackageAuthorityFailureKind.Transport);
        var missingIdentity = new PackageVersionDiscoveryResult(
            packageId: null,
            PackageVersionDiscoveryState.Failed,
            sourceListings: [],
            failures:
            [
                new PackageAuthorityFailure(
                    InertString.Empty,
                    PackageAuthorityFailureKind.Input,
                    "The package ID is invalid."),
            ],
            hasAnyCandidate: false);

        PackageVersionDiscoveryResult[] incompatible =
        [
            mismatchedAuthoritative,
            mismatchedFailure,
            missingIdentity,
        ];
        Assert.All(incompatible, discovery =>
        {
            PackageVersionResolutionReceipt.Rejected rejected =
                Assert.IsType<PackageVersionResolutionReceipt.Rejected>(
                    PackageVersionSelectionResolver.Resolve(
                        request,
                        discovery,
                        PackageVersionDiscoveryFreshness.NotEstablished));
            Assert.Same(request, rejected.Request);
            Assert.Same(discovery, rejected.Discovery);
            Assert.Contains(
                "requested package",
                rejected.Reason.ToString(),
                StringComparison.Ordinal);
        });
        Assert.Throws<ArgumentException>(
            () => new PackageVersionResolutionReceipt.NotFound(
                request,
                mismatchedAuthoritative,
                PackageVersionDiscoveryFreshness.Current,
                Reason("Wrong package.")));
    }

    [Fact]
    public void SelectingHouseDemandRequiresItsExactResolutionReceipt()
    {
        var selection =
            new PackageVersionSelectionRequest.LatestStable(
                "contoso.json");
        PackageVersionDiscoveryResult discovery = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: true,
            "4.0.0");
        var resolution = new PackageVersionResolutionReceipt.Resolved(
            selection,
            discovery,
            PackageVersionDiscoveryFreshness.Current);
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(selection),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));

        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.RetainSelectedPackage(
                request,
                resolution);

        Assert.Same(resolution, decision.VersionResolution);
        Assert.Same(resolution.Coordinate, decision.Coordinate);
        Assert.Same(resolution.Candidate, decision.Candidate);
        Assert.Throws<ArgumentException>(
            () => PackageHouseDecisionReceipt.RetainPackage(
                request,
                resolution.Coordinate,
                resolution.Candidate));

        var otherSelection =
            new PackageVersionSelectionRequest.LatestStable(
                "contoso.json");
        var otherRequest = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(otherSelection),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));
        Assert.Throws<ArgumentException>(
            () => PackageHouseDecisionReceipt.RetainSelectedPackage(
                otherRequest,
                resolution));
    }

    [Fact]
    public void SelectingHouseDemandRetainsTypedNonSuccessWithoutCoordinate()
    {
        var selection =
            new PackageVersionSelectionRequest.LatestStable(
                "contoso.json");
        PackageVersionDiscoveryResult discovery = VersionDiscovery(
            PackageVersionDiscoveryState.Authoritative,
            includePrerelease: false,
            hasAnyCandidate: true);
        var resolution = new PackageVersionResolutionReceipt.NoMatch(
            selection,
            discovery,
            PackageVersionDiscoveryFreshness.Current,
            Reason("Only prerelease versions were reported."));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Selecting(selection),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle));

        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.Stop(
                request,
                versionResolution: resolution);

        Assert.Same(resolution, decision.VersionResolution);
        Assert.Null(decision.Coordinate);
        Assert.Null(decision.Candidate);
        var evidence = new PackageHouseEvidence(request, decision);
        var result = new PackageHouseResult.NoMatch(
            evidence,
            Reason("No selected version matched."));
        Assert.Same(resolution, result.Decision!.VersionResolution);
        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.NotFound(
                evidence,
                Reason("Wrong terminal outcome.")));
        Assert.Throws<ArgumentException>(
            () => PackageHouseDecisionReceipt.Stop(
                request,
                Coordinate,
                versionResolution: resolution));
    }

    [Fact]
    public void DecisionBindsSettlementToTheExactDemand()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Settle);

        Assert.Throws<ArgumentException>(
            () => PackageHouseDecisionReceipt.RetainPackage(
                request,
                PackageSourceCoordinate.Create("other", "4.0.0")));
        Assert.Throws<ArgumentException>(
            () => PackageHouseDecisionReceipt.RetainPackage(
                request,
                PackageSourceCoordinate.Create(
                    "contoso.json",
                    "5.0.0")));

        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.RetainPackage(request, Coordinate);
        Assert.Same(Coordinate, decision.Coordinate);
    }

    [Fact]
    public void RequestRequiresRealizeForSelectionAndLibraryHandoff()
    {
        PackageHouseDemand demand = new PackageHouseDemand.Exact(Coordinate);

        Assert.Throws<ArgumentException>(
            () => new PackageHouseRequest(
                demand,
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Acquire),
                assetSelection: PackageHouseAssetSelectionKind.Compile));

        Assert.Throws<ArgumentException>(
            () => new PackageHouseRequest(
                demand,
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Realize)));

        Assert.Throws<ArgumentException>(
            () => new PackageHouseRequest(
                demand,
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Settle),
                libraryHandoff:
                    PackageHouseLibraryHandoffMode.SelectedLibraries));
    }

    [Fact]
    public void OperationPreservesIdentityAndDeadlineDurations()
    {
        PackageHouseOperation operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Acquire,
            TimeSpan.FromSeconds(7),
            TimeSpan.FromSeconds(31));

        Assert.Equal(PackageHouseOperationProfile.Acquire, operation.Profile);
        Assert.Equal(TimeSpan.FromSeconds(7), operation.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(31), operation.OperationTimeout);
        Assert.NotSame(
            operation.Identity,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire).Identity);
    }

    [Fact]
    public void AcquireRequiresAuthorityBoundAcquisitionReceipt()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire);
        AcquisitionBundle bundle = Acquisition(request);

        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Settled(
                new PackageHouseEvidence(request, bundle.Decision)));

        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition);
        var result = new PackageHouseResult.Settled(evidence);

        Assert.Same(bundle.Candidate, evidence.Acquisition!.Candidate);
        Assert.Same(bundle.Authority, evidence.Acquisition.Authority);
        Assert.Same(bundle.Source.Producer, evidence.Acquisition.Producer);
        Assert.Same(bundle.Generation, evidence.Acquisition.Generation);
        Assert.Null(evidence.Realization);
        Assert.Same(evidence, result.Evidence);
    }

    [Fact]
    public void AcquisitionRejectsSourceFromAnotherAuthority()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire);
        var authority = new ConfiguredPackageAuthority(PackageSource.NuGetOrg);
        var otherAuthority = new ConfiguredPackageAuthority(
            new PackageSource(
                "other",
                "https://packages.example.test/v3/index.json"));
        PackageAcquisitionCandidate candidate =
            PackageAcquisitionCandidate.CreatePinned(
                new object(),
                Coordinate,
                [authority]);
        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.RetainPackage(
                request,
                Coordinate,
                candidate);
        PackageSourceResultIdentity otherSource;
        using (IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                otherAuthority.Source,
                otherAuthority.Association))
        {
            otherSource = client.Source;
        }

        Assert.Throws<ArgumentException>(
            () => new PackageHouseAcquisitionReceipt(
                decision,
                candidate,
                authority,
                otherSource,
                PackagePayloadOrigin.Cache,
                new PackageContentGenerationIdentity()));
    }

    [Fact]
    public void AcquisitionRejectsASettleOperation()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Settle);

        Assert.Throws<ArgumentException>(() => Acquisition(request));
    }

    [Fact]
    public void TerminalFailureRetainsCompletedAcquisitionAndTypedFailure()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize);
        AcquisitionBundle bundle = Acquisition(request);
        var failure = new PackageHouseFailure.Stage(
            PackageHouseFailureStage.Selection,
            Reason("No compatible compile assets."));
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            failures: [failure]);

        var result = new PackageHouseResult.NoMatch(
            evidence,
            Reason("Package acquisition completed but selection did not."));

        Assert.Same(bundle.Decision, result.Decision);
        Assert.Same(bundle.Acquisition, result.Evidence.Acquisition);
        Assert.Same(failure, Assert.Single(result.Evidence.Failures));
        Assert.NotNull(result.Evidence.Identity);
    }

    [Fact]
    public void SettledResultRetainsEarlierFallbackFailures()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire);
        AcquisitionBundle bundle = Acquisition(request);
        var timeout = new PackageHouseFailure.Timeout(
            request.Operation.Identity,
            PackageHouseTimeoutKind.Request,
            request.Operation.RequestTimeout);
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            failures: [timeout]);

        var result = new PackageHouseResult.Settled(evidence);

        Assert.Same(timeout, Assert.Single(result.Evidence.Failures));
        Assert.Same(
            request.Operation.Identity,
            timeout.Operation);
    }

    [Fact]
    public void TimeoutEvidenceMustMatchOperationAndRemainTerminal()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire);
        AcquisitionBundle bundle = Acquisition(request);
        PackageHouseRequest other = Request(
            PackageHouseOperationProfile.Acquire);
        var wrongOperation = new PackageHouseFailure.Timeout(
            other.Operation.Identity,
            PackageHouseTimeoutKind.Request,
            request.Operation.RequestTimeout);

        Assert.Throws<ArgumentException>(
            () => new PackageHouseEvidence(
                request,
                bundle.Decision,
                bundle.Acquisition,
                failures: [wrongOperation]));

        var operationTimeout = new PackageHouseFailure.Timeout(
            request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            request.Operation.OperationTimeout);
        var timedOut = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            failures: [operationTimeout]);

        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Settled(timedOut));
        var failed = new PackageHouseResult.Failed(
            timedOut,
            Reason("The PackageHouse operation timed out."));
        Assert.Same(operationTimeout, Assert.Single(failed.Evidence.Failures));
    }

    [Fact]
    public void OwnerOperationTimeoutRemainsTerminalAfterHouseAdaptation()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire);
        AcquisitionBundle bundle = Acquisition(request);
        var ownerFailure = new PackageAuthorityFailure(
            Reason("nuget.org"),
            PackageAuthorityFailureKind.Timeout,
            "The package source operation timed out.")
        {
            Timeout = new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                request.Operation.OperationTimeout),
        };
        var adapted = new PackageHouseFailure.Authority(
            request.Operation.Identity,
            ownerFailure);
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            failures: [adapted]);

        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Settled(evidence));
        var failed = new PackageHouseResult.Failed(
            evidence,
            Reason("The PackageHouse operation timed out."));
        Assert.Same(ownerFailure, adapted.Failure);
        Assert.Same(adapted, Assert.Single(failed.Evidence.Failures));
    }

    [Fact]
    public void DirectOperationTimeoutRetainsCompletedSelectedRealization()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize);
        IPackageContent content = Content(
            "ref/net10.0/Contoso.Json.dll",
            "lib/net10.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        var selection = new PackageHouseAssetSelectionReceipt.Compile(
            bundle.Acquisition,
            PackageCompileAssetSelector.Evaluate(
                content,
                "contoso.json",
                "net10.0"));
        var realization = new PackageHouseRealizationReceipt(selection);
        var timeout = new PackageHouseFailure.Timeout(
            request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            request.Operation.OperationTimeout);
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            realization,
            [timeout]);

        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Settled(evidence));
        var failed = new PackageHouseResult.Failed(
            evidence,
            Reason("The PackageHouse operation timed out."));
        Assert.Same(realization, failed.Evidence.Realization);
        Assert.Same(timeout, Assert.Single(failed.Evidence.Failures));
    }

    [Fact]
    public void OwnerOperationTimeoutRetainsCompletedEmptyRealization()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize);
        IPackageContent content = Content(
            "lib/net8.0/Contoso.Json.dll",
            "ref/net10.0/_._");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        var selection = new PackageHouseAssetSelectionReceipt.Compile(
            bundle.Acquisition,
            PackageCompileAssetSelector.Evaluate(
                content,
                "contoso.json",
                "net10.0"));
        var realization = new PackageHouseRealizationReceipt(selection);
        var ownerFailure = new PackageAuthorityFailure(
            Reason("nuget.org"),
            PackageAuthorityFailureKind.Timeout,
            "The package source operation timed out.")
        {
            Timeout = new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                request.Operation.OperationTimeout),
        };
        var adapted = new PackageHouseFailure.Authority(
            request.Operation.Identity,
            ownerFailure);
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            realization,
            [adapted]);

        var failed = new PackageHouseResult.Failed(
            evidence,
            Reason("The PackageHouse operation timed out."));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            selection.Selection.Status);
        Assert.Same(realization, failed.Evidence.Realization);
        Assert.Same(adapted, Assert.Single(failed.Evidence.Failures));
    }

    [Fact]
    public void SettleRejectsAcquisitionAndRealizeRequiresSelection()
    {
        PackageHouseRequest settle = Request(
            PackageHouseOperationProfile.Settle);
        PackageHouseDecisionReceipt settleDecision =
            PackageHouseDecisionReceipt.RetainPackage(settle, Coordinate);
        PackageHouseRequest realize = Request(
            PackageHouseOperationProfile.Realize);
        AcquisitionBundle realization = Acquisition(realize);

        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Settled(
                new PackageHouseEvidence(
                    settle,
                    settleDecision,
                    realization.Acquisition)));
        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Settled(
                new PackageHouseEvidence(
                    realize,
                    realization.Decision,
                    realization.Acquisition)));
    }

    [Fact]
    public void SelectionReceiptRejectsAnotherRequestedKind()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize,
            selection: PackageHouseAssetSelectionKind.Runtime);
        IPackageContent content = Content(
            "ref/net10.0/Contoso.Json.dll",
            "lib/net10.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        PackageCompileAssetSelectionReceipt receipt =
            PackageCompileAssetSelector.Evaluate(
                content,
                "contoso.json",
                "net10.0");

        Assert.Throws<ArgumentException>(
            () => new PackageHouseAssetSelectionReceipt.Compile(
                bundle.Acquisition,
                receipt));
    }

    [Fact]
    public void RealizeKeepsRequestedAndSelectedFrameworksDistinct()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize,
            requestedFramework: "net10.0",
            selection: PackageHouseAssetSelectionKind.Runtime);
        IPackageContent content = Content(
            "lib/net8.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        PackageAssetSelectionReceipt ownerReceipt =
            PackageAssetSelector.Evaluate(content, "net10.0");
        var selection = new PackageHouseAssetSelectionReceipt.Runtime(
            bundle.Acquisition,
            ownerReceipt);
        var realization = new PackageHouseRealizationReceipt(selection);
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            realization);
        var result = new PackageHouseResult.Settled(evidence);

        Assert.Equal("net10.0", request.TargetContext!.RequestedFramework);
        Assert.Equal(
            "net8.0",
            Assert.IsType<PackageAssetSelection.Selected>(
                selection.Selection).Universe.TargetFramework);
        Assert.Same(ownerReceipt, selection.Receipt);
        Assert.Same(realization, result.Evidence.Realization);
    }

    [Fact]
    public void SelectionReceiptRejectsAnotherContentGeneration()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize);
        IPackageContent acquired = Content(
            "ref/net10.0/Contoso.Json.dll");
        IPackageContent other = Content(
            "ref/net10.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            acquired.GenerationIdentity);
        PackageCompileAssetSelectionReceipt receipt =
            PackageCompileAssetSelector.Evaluate(
                other,
                "contoso.json",
                "net10.0");

        Assert.Throws<ArgumentException>(
            () => new PackageHouseAssetSelectionReceipt.Compile(
                bundle.Acquisition,
                receipt));
    }

    [Fact]
    public void SameCoordinateWithDifferentTargetsHasDistinctSettlements()
    {
        AcquisitionBundle netEight = Acquisition(
            Request(
                PackageHouseOperationProfile.Realize,
                "net8.0"));
        AcquisitionBundle netTen = Acquisition(
            Request(
                PackageHouseOperationProfile.Realize,
                "net10.0"));
        var first = new PackageHouseEvidence(
            netEight.Decision.Request,
            netEight.Decision,
            netEight.Acquisition);
        var second = new PackageHouseEvidence(
            netTen.Decision.Request,
            netTen.Decision,
            netTen.Acquisition);

        Assert.NotSame(first.Identity, second.Identity);
        Assert.NotSame(netEight.Generation, netTen.Generation);
        Assert.Same(Coordinate, netEight.Decision.Coordinate);
        Assert.Same(Coordinate, netTen.Decision.Coordinate);
    }

    [Fact]
    public void CompileHandoffRetainsSelectionAcquisitionAndCounterpart()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize,
            handoff: PackageHouseLibraryHandoffMode.SelectedLibraries);
        IPackageContent content = Content(
            "ref/net10.0/Contoso.Json.dll",
            "lib/net10.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        PackageCompileAssetSelectionReceipt ownerReceipt =
            PackageCompileAssetSelector.Evaluate(
                content,
                "contoso.json",
                "net10.0");
        var selection = new PackageHouseAssetSelectionReceipt.Compile(
            bundle.Acquisition,
            ownerReceipt);
        var realization = new PackageHouseRealizationReceipt(selection);

        PackageHouseLibraryHandoff.Compile handoff =
            Assert.IsType<PackageHouseLibraryHandoff.Compile>(
                Assert.Single(realization.LibraryHandoffs));
        Assert.Same(selection, handoff.Selection);
        Assert.Same(bundle.Acquisition, handoff.Acquisition);
        Assert.Same(bundle.Decision, handoff.Decision);
        Assert.Same(Coordinate, handoff.Coordinate);
        Assert.Same(ownerReceipt.Selection.Assets[0], handoff.Asset);
        Assert.Same(
            ownerReceipt.Selection.ImplementationAssets[0],
            handoff.ImplementationAsset);
        Assert.Same(bundle.Authority, handoff.Acquisition.Authority);
        Assert.Same(bundle.Source, handoff.Acquisition.Source);
        Assert.Same(bundle.Generation, handoff.Acquisition.Generation);
    }

    [Fact]
    public void PackageOnlyRealizationDoesNotUnwrapSelectedLibraries()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize);
        IPackageContent content = Content(
            "ref/net10.0/Contoso.Json.dll",
            "lib/net10.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        var selection = new PackageHouseAssetSelectionReceipt.Compile(
            bundle.Acquisition,
            PackageCompileAssetSelector.Evaluate(
                content,
                "contoso.json",
                "net10.0"));
        var realization = new PackageHouseRealizationReceipt(selection);

        Assert.True(selection.Selection.IsSelected);
        Assert.Empty(realization.LibraryHandoffs);
    }

    [Fact]
    public void RuntimeHandoffWrapsOwnerSelectedAsset()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize,
            runtimeIdentifier: "linux-x64",
            handoff: PackageHouseLibraryHandoffMode.SelectedLibraries,
            selection: PackageHouseAssetSelectionKind.Runtime);
        IPackageContent content = Content(
            "runtimes/linux-x64/lib/net10.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        PackageAssetSelectionReceipt ownerReceipt =
            PackageAssetSelector.Evaluate(
                content,
                "net10.0",
                "linux-x64");
        var selection = new PackageHouseAssetSelectionReceipt.Runtime(
            bundle.Acquisition,
            ownerReceipt);
        var realization = new PackageHouseRealizationReceipt(selection);

        PackageHouseLibraryHandoff.Runtime handoff =
            Assert.IsType<PackageHouseLibraryHandoff.Runtime>(
                Assert.Single(realization.LibraryHandoffs));
        Assert.Same(ownerReceipt, selection.Receipt);
        Assert.Same(
            Assert.IsType<PackageAssetSelection.Selected>(
                ownerReceipt.Selection).Universe.Assets[0],
            handoff.Asset);
        Assert.Same(selection, handoff.Selection);
    }

    [Fact]
    public void OwnerNoMatchSelectionProducesNoLibraryHandoffs()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize,
            handoff: PackageHouseLibraryHandoffMode.SelectedLibraries);
        IPackageContent content = Content(
            "ref/net8.0/Contoso.Json.dll");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        PackageCompileAssetSelectionReceipt ownerReceipt =
            PackageCompileAssetSelector.Evaluate(
                content,
                "contoso.json",
                "net10.0");
        var selection = new PackageHouseAssetSelectionReceipt.Compile(
            bundle.Acquisition,
            ownerReceipt);

        var realization = new PackageHouseRealizationReceipt(selection);
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            realization);

        Assert.Same(ownerReceipt, selection.Receipt);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
            selection.Selection.Status);
        Assert.Empty(realization.LibraryHandoffs);
        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Settled(evidence));
        var result = new PackageHouseResult.NoMatch(
            evidence,
            Reason("No compile assets match the requested framework."));
        Assert.Same(realization, result.Evidence.Realization);
    }

    [Fact]
    public void ExplicitEmptyCompileGroupIsASettledZeroLibraryOutcome()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Realize,
            handoff: PackageHouseLibraryHandoffMode.SelectedLibraries);
        IPackageContent content = Content(
            "lib/net8.0/Contoso.Json.dll",
            "ref/net10.0/_._");
        AcquisitionBundle bundle = Acquisition(
            request,
            content.GenerationIdentity);
        PackageCompileAssetSelectionReceipt ownerReceipt =
            PackageCompileAssetSelector.Evaluate(
                content,
                "contoso.json",
                "net10.0");
        var selection = new PackageHouseAssetSelectionReceipt.Compile(
            bundle.Acquisition,
            ownerReceipt);
        var realization = new PackageHouseRealizationReceipt(selection);
        var evidence = new PackageHouseEvidence(
            request,
            bundle.Decision,
            bundle.Acquisition,
            realization);

        var result = new PackageHouseResult.Settled(evidence);

        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            selection.Selection.Status);
        Assert.Empty(result.Evidence.Realization!.LibraryHandoffs);
    }

    [Fact]
    public void PlatformDelegationConsumesPolicyIssuedCorrespondence()
    {
        PlatformFamilyTarget target = PlatformTarget("net11.0", "11.0.0");
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire,
            requestedFramework: "net11.0",
            platformTarget: target);
        PlatformPruneInventory inventory = PlatformInventory(
            "net11.0",
            "11.0.0");
        PackageCoordinate policyCoordinate = new(
            "contoso.json",
            "4.0.0",
            "net11.0");
        PlatformSupplyReceipt policy =
            PlatformPrunePolicy.Evaluate(inventory, policyCoordinate);
        var pruning = new PackageHousePruningReceipt(
            request,
            target,
            policy);
        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.DelegateToPlatform(
                request,
                Coordinate,
                candidate: null,
                pruning);
        var delegation = new PlatformDelegation(decision);
        var evidence = new PackageHouseEvidence(request, decision);
        var result = new PackageHouseResult.Delegated(
            evidence,
            delegation);

        Assert.Same(policy, delegation.Policy);
        Assert.Same(inventory, delegation.Policy.Inventory);
        Assert.Same(policyCoordinate, delegation.Policy.Coordinate);
        Assert.Same(target, delegation.Target);
        Assert.Same(Coordinate, delegation.Coordinate);
        Assert.True(delegation.Supply.DelegatesToPlatform);
        Assert.Same(evidence, result.Evidence);
    }

    [Fact]
    public void PruningUsesPackageOwnerExactCoordinateNormalization()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(
                "contoso.json",
                "4.0.0-RC.1");
        PlatformFamilyTarget target = PlatformTarget(
            "net11.0",
            "11.0.0");
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(coordinate),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire),
            PackageHouseTargetContext.Exact(
                "net11.0",
                platformTarget: target));
        PlatformSupplyReceipt policy = PlatformPrunePolicy.Evaluate(
            PlatformInventory("net11.0", "11.0.0"),
            new PackageCoordinate(
                "contoso.json",
                "4.0.0-RC.1",
                "net11.0"));

        var pruning = new PackageHousePruningReceipt(
            request,
            target,
            policy);

        Assert.Equal("4.0.0-rc.1", coordinate.Version);
        Assert.Same(policy, pruning.Policy);
    }

    [Fact]
    public void PruningRejectsAnotherPolicyCoordinateOrPlatformVersion()
    {
        PlatformFamilyTarget target = PlatformTarget("net11.0", "11.0.0");
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire,
            requestedFramework: "net11.0",
            platformTarget: target);
        PlatformPruneInventory inventory = PlatformInventory(
            "net11.0",
            "11.0.0");

        Assert.Throws<ArgumentException>(
            () => new PackageHousePruningReceipt(
                request,
                target,
                PlatformPrunePolicy.Evaluate(
                    inventory,
                    new PackageCoordinate(
                        "contoso.json",
                        "5.0.0",
                        "net11.0"))));
        Assert.Throws<ArgumentException>(
            () => new PackageHousePruningReceipt(
                request,
                target,
                PlatformPrunePolicy.Evaluate(
                    PlatformInventory("net11.0", "11.0.1"),
                    new PackageCoordinate(
                        "contoso.json",
                        "4.0.0",
                        "net11.0"))));
    }

    [Fact]
    public void PruningRejectsADelegatingSupplyFromAnotherFamily()
    {
        PlatformFamilyTarget target = PlatformTarget("net11.0", "11.0.0");
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire,
            requestedFramework: "net11.0",
            platformTarget: target);
        PlatformPruneInventory runtime = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(
                "Microsoft.NETCore.App",
                "net11.0",
                NuGetVersion.Parse("11.0.0")),
            ["system.runtime|11.0.0"]);
        PlatformPruneInventory aspnet = PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(
                "Microsoft.AspNetCore.App",
                "net11.0",
                NuGetVersion.Parse("11.0.0")),
            ["contoso.json|11.0.0"]);
        PlatformSupplyReceipt policy = PlatformPrunePolicy.Evaluate(
            PlatformPruneInventory.Compose([runtime, aspnet]),
            new PackageCoordinate(
                "contoso.json",
                "4.0.0",
                "net11.0"));

        Assert.True(policy.Supply.DelegatesToPlatform);
        Assert.Equal("Microsoft.AspNetCore.App", policy.Supply.Family);
        Assert.Throws<ArgumentException>(
            () => new PackageHousePruningReceipt(
                request,
                target,
                policy));
    }

    [Fact]
    public void PlatformDelegationRejectsNonSubsumedPruning()
    {
        PlatformFamilyTarget target = PlatformTarget("net11.0", "11.0.0");
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Acquire,
            requestedFramework: "net11.0",
            platformTarget: target);
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net11.0",
                    NuGetVersion.Parse("11.0.0")),
                ["other|11.0.0"]);
        PlatformSupplyReceipt policy = PlatformPrunePolicy.Evaluate(
            inventory,
            new PackageCoordinate(
                "contoso.json",
                "4.0.0",
                "net11.0"));
        var pruning = new PackageHousePruningReceipt(
            request,
            target,
            policy);

        Assert.Throws<ArgumentException>(
            () => PackageHouseDecisionReceipt.DelegateToPlatform(
                request,
                Coordinate,
                candidate: null,
                pruning));
    }

    [Fact]
    public void ContractFamiliesAreClosedClasses()
    {
        Type[] closedFamilies =
        [
            typeof(PackageVersionSelectionRequest),
            typeof(PackageVersionRangeSelection),
            typeof(PackageVersionResolutionReceipt),
            typeof(PackageHouseDemand),
            typeof(PackageHouseAssetSelectionReceipt),
            typeof(PackageHouseLibraryHandoff),
            typeof(PackageHouseFailure),
            typeof(PackageHouseResult),
        ];

        Assert.All(
            closedFamilies,
            family => Assert.All(
                family.GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic),
                constructor => Assert.True(constructor.IsPrivate)));
        Assert.All(
            closedFamilies.SelectMany(family =>
                family.GetNestedTypes(BindingFlags.Public)),
            arm => Assert.True(arm.IsSealed));
    }

    [Fact]
    public void NonSuccessRejectsAnEmptyReason()
    {
        var evidence = new PackageHouseEvidence(
            Request(PackageHouseOperationProfile.Settle));

        Assert.Throws<ArgumentException>(
            () => new PackageHouseResult.Failed(evidence, default));
    }

    [Fact]
    public void TerminalResultFamilyIsClosedAndResourceFree()
    {
        Type result = typeof(PackageHouseResult);
        string[] expected =
        [
            "Ambiguous",
            "Delegated",
            "Failed",
            "Incomplete",
            "NoMatch",
            "NotFound",
            "Rejected",
            "Settled",
            "Unavailable",
        ];

        Assert.Equal(
            expected,
            result.GetNestedTypes(BindingFlags.Public)
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        Type[] contractTypes =
        [
            typeof(PackageHouseRequest),
            typeof(PackageHouseOperation),
            typeof(PackageVersionDiscoveryRequirement),
            typeof(PackageVersionSelectionRequest),
            typeof(PackageVersionRangeSelection),
            typeof(PackageVersionResolutionReceipt),
            .. typeof(PackageVersionSelectionRequest)
                .GetNestedTypes(BindingFlags.Public),
            .. typeof(PackageVersionRangeSelection)
                .GetNestedTypes(BindingFlags.Public),
            .. typeof(PackageVersionResolutionReceipt)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageHouseDecisionReceipt),
            typeof(PackageHousePruningReceipt),
            typeof(PackageHouseAcquisitionReceipt),
            typeof(PackageHouseAssetSelectionReceipt),
            typeof(PackageAssetSelectionReceipt),
            typeof(PackageCompileAssetSelectionReceipt),
            typeof(PackageHouseRealizationReceipt),
            typeof(PackageHouseLibraryHandoff),
            typeof(PackageHouseFailure),
            typeof(PackageHouseEvidence),
            typeof(PlatformDelegation),
            .. result.GetNestedTypes(BindingFlags.Public),
        ];
        Assert.DoesNotContain(
            contractTypes.SelectMany(type => type.GetProperties()),
            property =>
                typeof(Stream).IsAssignableFrom(property.PropertyType)
                || typeof(IPackageContent).IsAssignableFrom(
                    property.PropertyType)
                || property.PropertyType.Name.Contains(
                    "Workspace",
                    StringComparison.Ordinal)
                || property.PropertyType.Namespace
                    ?.Equals(
                        "DotnetInspector.Queries",
                        StringComparison.Ordinal)
                    is true);
    }

    [Fact]
    public void EveryTerminalArmRetainsTheExactEvidenceEnvelope()
    {
        PackageHouseRequest request = Request(
            PackageHouseOperationProfile.Settle);
        var evidence = new PackageHouseEvidence(request);
        InertString reason = Reason("Terminal outcome.");
        PackageHouseResult[] results =
        [
            new PackageHouseResult.NotFound(evidence, reason),
            new PackageHouseResult.NoMatch(evidence, reason),
            new PackageHouseResult.Ambiguous(evidence, reason),
            new PackageHouseResult.Rejected(evidence, reason),
            new PackageHouseResult.Unavailable(evidence, reason),
            new PackageHouseResult.Incomplete(evidence, reason),
            new PackageHouseResult.Failed(evidence, reason),
        ];

        Assert.All(results, result => Assert.Same(evidence, result.Evidence));
        Assert.All(results, result => Assert.Same(request, result.Request));
    }

    [Fact]
    public async Task PackageSourceSettlementLeaseSettlesManifestAndRetiresWithoutDisposingClient()
    {
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [
                    new PackageSource(
                        "browser",
                        "https://browser.example/v3/index.json"),
                ]);
        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.Authorities);
        TrackingPackageSourceClient? tracking = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                authority.Association,
                factory =>
                {
                    tracking = new TrackingPackageSourceClient(factory);
                    return tracking;
                });
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(_ => client);
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(
                TestContext.Current.CancellationToken,
                requestTimeout: TimeSpan.FromSeconds(7),
                operationTimeout: TimeSpan.FromSeconds(31));

        PackageAcquisitionCandidateResult resolution =
            operation.ResolvePinnedCandidate(
                authorization,
                Coordinate);
        PackageAcquisitionCandidate candidate =
            Assert.IsType<PackageAcquisitionCandidate>(
                resolution.Candidate);
        ConfiguredPackageManifestResult manifest =
            await operation.AcquireCandidateManifestAsync(candidate);

        Assert.Null(manifest.Manifest);
        Assert.Equal(
            PackageAuthorityFailureKind.ResponseRejected,
            Assert.Single(manifest.Failures).Kind);
        Assert.Equal(TimeSpan.FromSeconds(7), tracking!.ObservedOperationContext!.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(31), tracking.ObservedOperationContext.OperationTimeout);

        operation.Dispose();
        await lease.DisposeAsync();

        Assert.False(tracking!.IsDisposed);
        Assert.Same(candidate, resolution.Candidate);
        Assert.Throws<ObjectDisposedException>(
            () => operation.ResolvePinnedCandidate(
                authorization,
                Coordinate));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await operation.AcquireCandidateManifestAsync(candidate));
    }

    [Fact]
    public async Task PackageSourceSettlementLeaseAcquiresPayloadAndRetiresWithoutDisposingClient()
    {
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [
                    new PackageSource(
                        "browser",
                        "https://browser.example/v3/index.json"),
                ]);
        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.Authorities);
        TrackingPackageSourceClient? tracking = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                authority.Association,
                factory =>
                {
                    tracking = new TrackingPackageSourceClient(factory);
                    return tracking;
                });
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(_ => client);
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(TestContext.Current.CancellationToken);
        PackageAcquisitionCandidate candidate =
            Assert.IsType<PackageAcquisitionCandidate>(
                operation.ResolvePinnedCandidate(
                    authorization,
                    Coordinate).Candidate);
        var store = new InMemoryPackageStore();
        byte[] nupkg = TestPackageArchive.Create(
            (
                "contoso.json.nuspec",
                "<package><metadata><id>contoso.json</id><version>1.0.0</version></metadata></package>"u8.ToArray()),
            ("lib/net10.0/contoso.json.dll", [1, 2, 3]));
        IPackageContent committed = await store.CommitAsync(
            Coordinate.PackageId,
            Coordinate.Version,
            client.Source.Producer.Key,
            new MemoryStream(nupkg, writable: false),
            TestContext.Current.CancellationToken);

        ConfiguredPackagePayloadResult payload =
            await operation.AcquireCandidatePayloadAsync(
                candidate,
                (_, producer) =>
                {
                    Assert.Equal(client.Source.Producer, producer);
                    return store;
                });

        Assert.Same(authority, payload.Authority);
        Assert.Same(client.Source, payload.Source);
        AcquiredPackageSourcePayload acquired =
            Assert.IsType<AcquiredPackageSourcePayload>(payload.Payload);
        Assert.Same(committed, acquired.Content);
        Assert.Same(
            committed.GenerationIdentity,
            acquired.Content.GenerationIdentity);
        Assert.Equal(client.Source.Producer.Key, acquired.ProducerKey);
        Assert.Equal(PackagePayloadOrigin.Cache, acquired.Origin);
        Assert.Empty(payload.Failures);

        operation.Dispose();
        await lease.DisposeAsync();

        Assert.False(tracking!.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await operation.AcquireCandidatePayloadAsync(
                candidate,
                (_, _) => store));
    }

    [Fact]
    public async Task PackageSourceSettlementLeaseRejectsForeignCandidateAndClientAssociation()
    {
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.Authorities);
        ConfiguredPackageAuthority foreignAuthority =
            new(new PackageSource(
                "foreign",
                "https://foreign.example/v3/index.json"));
        using IPackageSourceClient foreignClient =
            PackageSourceClientFactory.Create(
                foreignAuthority.Source,
                foreignAuthority.Association);
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(_ => foreignClient);
        await using PackageSourceSettlementLease foreignLease =
            PackageSourceSettlementService.IssueLease(_ => foreignClient);
        using PackageSourceOperationLease operation =
            lease.IssueOperationLease(TestContext.Current.CancellationToken);
        using PackageSourceOperationLease foreignOperation =
            foreignLease.IssueOperationLease(TestContext.Current.CancellationToken);
        PackageAcquisitionCandidate candidate =
            Assert.IsType<PackageAcquisitionCandidate>(
                operation.ResolvePinnedCandidate(
                    authorization,
                    Coordinate).Candidate);
        PackageAcquisitionCandidate foreignCandidate =
            Assert.IsType<PackageAcquisitionCandidate>(
                foreignOperation.ResolvePinnedCandidate(
                    authorization,
                    Coordinate).Candidate);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await operation.AcquireCandidateManifestAsync(foreignCandidate));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await operation.AcquireCandidateManifestAsync(candidate));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await operation.AcquireCandidatePayloadAsync(
                foreignCandidate,
                (_, _) => new InMemoryPackageStore()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await operation.AcquireCandidatePayloadAsync(
                candidate,
                (_, _) => new InMemoryPackageStore()));
    }

    private static PackageHouseRequest Request(
        PackageHouseOperationProfile profile,
        string? requestedFramework = "net10.0",
        string? runtimeIdentifier = null,
        PackageHouseLibraryHandoffMode handoff =
            PackageHouseLibraryHandoffMode.PackageOnly,
        PlatformFamilyTarget? platformTarget = null,
        PackageHouseAssetSelectionKind selection =
            PackageHouseAssetSelectionKind.Compile) =>
        new(
            new PackageHouseDemand.Exact(Coordinate),
            PackageHouseOperation.Create(profile),
            requestedFramework is null
                ? null
                : PackageHouseTargetContext.Exact(
                    requestedFramework,
                    runtimeIdentifier,
                    platformTarget),
            profile == PackageHouseOperationProfile.Realize
                ? selection
                : null,
            handoff);

    private static AcquisitionBundle Acquisition(
        PackageHouseRequest request,
        PackageContentGenerationIdentity? generation = null)
    {
        var authority = new ConfiguredPackageAuthority(PackageSource.NuGetOrg);
        PackageAcquisitionCandidate candidate =
            PackageAcquisitionCandidate.CreatePinned(
                new object(),
                Coordinate,
                [authority]);
        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.RetainPackage(
                request,
                Coordinate,
                candidate);
        PackageSourceResultIdentity source;
        using (IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association))
        {
            source = client.Source;
        }

        generation ??= new PackageContentGenerationIdentity();
        var acquisition = new PackageHouseAcquisitionReceipt(
            decision,
            candidate,
            authority,
            source,
            PackagePayloadOrigin.Cache,
            generation);
        return new AcquisitionBundle(
            decision,
            candidate,
            authority,
            source,
            generation,
            acquisition);
    }

    private static IPackageContent Content(params string[] entries) =>
        new InMemoryPackageContent(
            TestPackageArchive.Create(entries),
            fromCache: true,
            producerKey: "test-source");

    private static PlatformPruneInventory PlatformInventory(
        string framework,
        string version) =>
        PlatformPruneInventory.FromExactFamily(
            new PlatformPruneTarget(
                "Microsoft.NETCore.App",
                framework,
                NuGetVersion.Parse(version)),
            ["contoso.json|11.0.0"]);

    private static PlatformFamilyTarget PlatformTarget(
        string framework,
        string version) =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse(framework),
            PlatformVersion.Parse(version));

    private static InertString Reason(string text) =>
        new(TextPolicy.Field, text);

    private static PackageVersionDiscoveryResult VersionDiscovery(
        PackageVersionDiscoveryState state,
        bool includePrerelease,
        bool hasAnyCandidate,
        params string[] versions) =>
        VersionDiscovery(
            state,
            includePrerelease,
            includeUnlisted: false,
            limit: null,
            hasAnyCandidate: hasAnyCandidate,
            versions: versions);

    private static PackageVersionDiscoveryResult VersionDiscovery(
        PackageVersionDiscoveryState state,
        bool includePrerelease,
        bool includeUnlisted,
        int? limit,
        bool hasAnyCandidate,
        params string[] versions)
    {
        var authority = new ConfiguredPackageAuthority(
            new PackageSource(
                "version-selection",
                "https://versions.example/v3/index.json"));
        PackageSourceResultFactory factory = ResultFactory(
            authority.Association);
        var candidates = versions.Select(version =>
            new ConfiguredPackageCandidateObservation(
                authority,
                factory.Candidate(
                    PackageSourceCoordinate.Create(
                        "contoso.json",
                        version),
                    PackageDiscoveryContract
                        .CompleteVersionEnumeration,
                    PackageListingState.Listed)))
            .ToArray();
        PackageAuthorityFailure[] failures =
            state == PackageVersionDiscoveryState.Authoritative
                ? []
                : [
                    new PackageAuthorityFailure(
                        Reason("timeout-authority"),
                        PackageAuthorityFailureKind.Timeout,
                        "The required authority timed out."),
                ];

        return new PackageVersionDiscoveryResult(
            "contoso.json",
            state,
            [
                .. versions.Select(version =>
                    new PackageVersionSourceInfo(
                        version,
                        "version-selection",
                        Listed: true)),
            ],
            failures,
            hasAnyCandidate,
            candidates,
            PackageVersionDiscoveryContract.Create(
                includePrerelease,
                includeUnlisted,
                limit),
            candidateIssuer: new object());
    }

    private static PackageVersionDiscoveryResult
        VersionDiscoveryWithFailure(
            params PackageAuthorityFailureKind[] kinds) =>
        VersionDiscoveryWithFailureForPackage(
            "contoso.json",
            kinds);

    private static PackageVersionDiscoveryResult
        VersionDiscoveryWithFailureForPackage(
            string packageId,
            params PackageAuthorityFailureKind[] kinds) =>
        new(
            packageId,
            PackageVersionDiscoveryState.Failed,
            sourceListings: [],
            failures:
            [
                .. kinds.Select(kind => new PackageAuthorityFailure(
                    Reason("version-authority"),
                    kind,
                    "Version discovery did not settle.")),
            ],
            hasAnyCandidate: false,
            candidates: [],
            contract: PackageVersionDiscoveryContract.Create(
                includePrerelease: false,
                includeUnlisted: false,
                limit: null),
            candidateIssuer: new object());

    private static PackageSourceResultFactory ResultFactory(
        PackageSourceAssociation association)
    {
        TrackingPackageSourceClient? tracking = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                association,
                factory =>
                {
                    tracking = new TrackingPackageSourceClient(factory);
                    return tracking;
                });
        return tracking!.Factory;
    }

    private sealed record AcquisitionBundle(
        PackageHouseDecisionReceipt Decision,
        PackageAcquisitionCandidate Candidate,
        ConfiguredPackageAuthority Authority,
        PackageSourceResultIdentity Source,
        PackageContentGenerationIdentity Generation,
        PackageHouseAcquisitionReceipt Acquisition);

    private sealed class TrackingPackageSourceClient(
        PackageSourceResultFactory factory) : IPackageSourceClient
    {
        public PackageSourceResultFactory Factory { get; } = factory;

        public PackageSourceResultIdentity Source => Factory.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Manifest;

        public bool IsDisposed { get; private set; }

        public NuGetOperationContext? ObservedOperationContext { get; private set; }

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
            NuGetOperationContext? operationContext = null)
        {
            ObservedOperationContext = operationContext;
            return Task.FromResult(
                Factory.FailedManifest(
                    PackageSourceCoordinate.Create(packageId, version),
                    PackageSourceFailureKind.NotFound));
        }

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

        public void Dispose() =>
            IsDisposed = true;
    }
}
