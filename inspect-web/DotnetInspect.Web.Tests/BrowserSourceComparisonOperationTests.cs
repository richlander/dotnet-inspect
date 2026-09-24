using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Sections;
using DotnetInspector.SourceHouse;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspect.Web.Interop.Source;
using InspectWeb.MethodBodyFixtures;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Managed outcome cases for the authored Source comparison export (#6076).
/// </summary>
/// <remarks>
/// <para>
/// Every case starts from the compiler-produced version pair in
/// <c>fixtures/inspect-web/InspectWeb.SourceComparisonFixtures.V1</c> and
/// <c>fixtures/inspect-web/InspectWeb.SourceComparisonFixtures.V2</c>, registered as two versions
/// of one browser package. That pair compiles the same <c>Counter.cs</c> inputs as the queries
/// source-diff fixtures, but in the shape a browser participant can actually reach: an embedded
/// PDB, because the browser reads no adjacent file and an unpublished package has no symbol
/// transport, and a SourceLink map on a host the production
/// <see cref="BrowserSourceFetchPolicy"/> admits. Nothing here builds a Source endpoint, a line
/// pair, or a comparison result: the product query resolves each image independently, extracts and
/// checksum-verifies the PDB source, and compares the authored declarations, and the browser
/// projection is asserted on the result of that work.
/// </para>
/// <para>
/// Through the public export the Source context is the production browser one, whose HTTP client
/// reaches the real internet, so the source fetch for this unpublished fixture pair ends
/// non-success. Those export cases gate the envelope, independent endpoint resolution, and visible
/// non-success. The compared-declaration cases run the same browser scopes and the same paired
/// query with only the HTTP transport substituted — the production fetch policy still governs
/// every source request — which is the only offline route to real extraction, checksum
/// verification, and comparison; published-Wasm acceptance covers the production transport end to
/// end.
/// </para>
/// </remarks>
[Collection("Type source operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserSourceComparisonOperationTests(ITestOutputHelper output)
{
    const string Framework = "net11.0";
    const string SourcePackageId = "InspectWeb.SourceComparisonFixture";
    const string AssemblyName = "InspectWebSourceComparisonFixture.dll";
    const string BeforeVersion = "1.0.0";
    const string AfterVersion = "2.0.0";

    [Theory]
    [InlineData("Count", "public int Count => field + 1;")]
    [InlineData("AutomaticCount", "public int AutomaticCount { get; }")]
    [InlineData("ChangingCount", "get_ChangingCount()")]
    [InlineData("WriteOnly", "public int WriteOnly")]
    public async Task MemberSourceExport_PreservesSelectedAccessor(
        string propertyName, string expected)
    {
        await using Pair pair = await Pair.OpenAsync();
        MemberSelection selection =
            await pair.Selection("FieldGetter", propertyName);
        string json = await SourceExports.QueryMemberSource(
            selection.PackageId, BeforeVersion, selection.Framework, selection.Assembly,
            selection.TypeIdentity, selection.MemberName, selection.SelectorKey,
            selection.MetadataToken, "[]");
        using var document = JsonDocument.Parse(json);
        var source = document.RootElement.GetProperty("source");
        Assert.Equal("decompiled", source.GetProperty("provider").GetString());
        Assert.Contains(expected, source.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("InitializedFieldGetter", true)]
    [InlineData("CalculatedFieldGetter", false)]
    public async Task MemberSourceExport_PreservesProvenInitializerContext(string typeName, bool initialized)
    {
        await using Pair pair = await Pair.OpenAsync();
        MemberSelection selection =
            await pair.Selection(typeName, "Count");
        string json = await SourceExports.QueryMemberSource(
            selection.PackageId, BeforeVersion, selection.Framework, selection.Assembly,
            selection.TypeIdentity, selection.MemberName, selection.SelectorKey,
            selection.MetadataToken, "[]");
        using var document = JsonDocument.Parse(json);
        var source = document.RootElement.GetProperty("source");
        Assert.Equal("decompiled", source.GetProperty("provider").GetString());
        string text = source.GetProperty("text").GetString()!;
        Assert.Contains("field + 1", text);
        if (initialized)
        {
            Assert.Contains("readonly struct InitializedFieldGetter(int value)", text);
            Assert.Contains("} = value;", text);
        }
        else
        {
            Assert.DoesNotContain("struct ", text);
            Assert.DoesNotContain("} = ", text);
        }
    }

    [Theory]
    [InlineData("authored")]
    [InlineData("missing")]
    [InlineData("deadline")]
    public async Task TypeSourcePdbHedgeEnvelope_PreservesBrowserPreferenceAndFallback(string scenario)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            SourceExports.BrowserTypeSourcePdbLatencyHedge.PortablePdbPreferenceWindow);
        await using Pair pair = await Pair.OpenAsync();
        using var host = new SourcePairHost(
            scenario == "missing" ? null : FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.Old),
            FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.New));
        MemberSelection selection =
            await pair.Selection("Counter", "Value");
        await using BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                selection.PackageId, BeforeVersion, selection.Framework,
                selection.Assembly, selection.TypeIdentity, selection.MemberName,
                selection.SelectorKey, selection.MetadataToken,
                TestContext.Current.CancellationToken);
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            TypeSourceTimeout = scenario == "deadline" ? TimeSpan.FromTicks(1) : TimeSpan.FromMinutes(5),
        };

        var inspection = await resolved.Scope.UseImplementationParticipant(
            resolved.ImplementationParticipant,
            (group, participant) => TypeSourceInspection.ExecuteWithPdbLatencyHedgeAsync(
                group, participant, AssemblyTypeSourceRequest.From(resolved.Member.Type),
                context, SourceExports.BrowserTypeSourcePdbLatencyHedge,
                TestContext.Current.CancellationToken));
        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        TypeSourceLatencyHedgeEvidence evidence =
            Assert.IsType<TypeSourceLatencyHedgeEvidence>(available.LatencyHedgeEvidence);
        BrowserSource source = SourceExports.Adapt(inspection.Content, resolved.ImplementationParticipant);

        Assert.IsType<InspectionShare.NonProjectable>(inspection.Share);
        Assert.True(evidence.PdbReadyBeforeDecompilation);
        Assert.Equal(scenario == "authored" ? "pdb" : "decompiled", source.Provider);
        Assert.Contains("Counter", source.Text);
        if (scenario == "authored")
        {
            Assert.Equal(
                TypeSourceLatencyHedgeSelection.AuthoredBeforeDecompilation,
                evidence.Selection);
            Assert.False(evidence.DecompilationStarted);
            SourceHouseOutcome.Available outcome = Assert.IsType<SourceHouseOutcome.Available>(
                available.HouseOutcome);
            Assert.Equal(SourceHouseSourceUnitScope.PrimaryTypeDocument,
                Assert.IsType<SourceHouseAuthoredMapping.Type>(outcome.AuthoredAttempt.Mapping).Scope);
            Assert.Null(source.PdbSourceLimitation);
            Assert.NotNull(source.Url);
        }
        else
        {
            Assert.True(evidence.DecompilationStarted);
            Assert.True(evidence.DecompilationUsedPdb);
            Assert.Equal(
                TypeSourceLatencyHedgeSelection.DecompiledAfterAuthoredUnavailable,
                evidence.Selection);
            Assert.True(Assert.IsType<AssemblyTypeSource.Decompiled>(available.Source)
                .Decompilation.PdbSupplied);
            var decompilationHouse =
                Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                    available.DecompilationHouseOutcome);
            Assert.IsType<SourceHouseTarget.TypeTarget>(
                decompilationHouse.Request.Target);
            Assert.Equal(
                SourceHousePdbContributionKind.Embedded,
                decompilationHouse.PdbContribution.Kind);
            Assert.NotNull(source.PdbSourceLimitation);
            Assert.Null(source.Url);
            if (scenario == "deadline")
            {
                Assert.Equal(SourceHouseIncompleteBoundary.Deadline,
                    Assert.IsType<SourceHouseOutcome.Incomplete>(available.HouseOutcome).Boundary);
                Assert.Contains("Deadline", source.PdbSourceLimitation);
                Assert.Empty(host.SourceRequests);
            }
        }
        Assert.Empty(host.SymbolRequests);
    }

    [Theory]
    [InlineData("authored")]
    [InlineData("missing")]
    [InlineData("deadline")]
    public async Task MemberSourceEnvelope_PreservesBrowserPreferenceAndFallback(string scenario)
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = new SourcePairHost(
            scenario == "missing" ? null : FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.Old),
            FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.New));
        MemberSelection selection =
            await pair.Selection("Counter", "Value");
        await using BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                selection.PackageId, BeforeVersion, selection.Framework,
                selection.Assembly, selection.TypeIdentity, selection.MemberName,
                selection.SelectorKey, selection.MetadataToken,
                TestContext.Current.CancellationToken);
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            MemberSourceTimeout = scenario == "deadline" ? TimeSpan.FromTicks(1) : TimeSpan.FromMinutes(5),
        };

        var inspection = await resolved.Scope.UseImplementationParticipant(
            resolved.ImplementationParticipant,
            (group, participant) => MemberSourceInspection.ExecuteAsync(
                group, participant,
                AssemblyMemberSourceRequest.From(resolved.Member.Type, resolved.Member.Member),
                context, TestContext.Current.CancellationToken));
        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(inspection.Content);
        BrowserSource source = SourceExports.Adapt(inspection.Content, resolved.ImplementationParticipant);

        Assert.Equal(scenario == "authored" ? "pdb" : "decompiled", source.Provider);
        Assert.Contains("Value", source.Text);
        if (scenario == "authored")
        {
            Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
            Assert.Null(source.PdbSourceLimitation);
            Assert.NotNull(source.Url);
        }
        else
        {
            Assert.True(Assert.IsType<AssemblyMemberSource.Decompiled>(available.Source)
                .Decompilation.PdbSupplied);
            Assert.NotNull(source.PdbSourceLimitation);
            Assert.Null(source.Url);
            if (scenario == "deadline")
            {
                Assert.Equal(SourceHouseIncompleteBoundary.Deadline,
                    Assert.IsType<SourceHouseOutcome.Incomplete>(available.HouseOutcome).Boundary);
                Assert.Contains("Deadline", source.PdbSourceLimitation);
                Assert.Empty(host.SourceRequests);
            }
        }
        Assert.Empty(host.SymbolRequests);
    }

    [Theory]
    [InlineData("Counter", true)]
    [InlineData("MovedCounter", false)]
    public async Task ExportResolvesEachEndpointIndependentlyAndKeepsMissingSourceVisible(
        string typeName,
        bool sameToken)
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest request = await pair.Request(typeName, "Value");

        BrowserSourceComparisonResult result = await Compare(request);

        Assert.Equal(1, result.Version);
        Assert.Equal(BrowserSourceComparisonResultKind.Succeeded, result.Kind);
        Assert.Null(result.FailureKind);
        Assert.Null(result.Reason);
        BrowserSourceComparison value = Assert.IsType<BrowserSourceComparison>(result.Value);
        Assert.Equal(request, value.Request);
        Assert.Equal(BeforeVersion, value.Before.Version);
        Assert.Equal(AfterVersion, value.After.Version);
        Assert.Equal(value.Before.Assembly, value.After.Assembly);
        Assert.Equal(value.Before.Framework, value.After.Framework);

        // Independent resolution: the same logical member, two images, two module identities.
        Assert.Equal(value.Before.MemberIdentity, value.After.MemberIdentity);
        Assert.Contains(typeName, value.Before.MemberIdentity!);
        Assert.NotEqual(value.Before.AssemblyIdentity, value.After.AssemblyIdentity);
        Assert.NotEqual(value.Before.ModuleVersionId, value.After.ModuleVersionId);
        Assert.NotNull(value.Before.ModuleVersionId);
        Assert.NotNull(value.After.ModuleVersionId);
        Assert.NotNull(value.Before.MetadataToken);
        Assert.NotNull(value.After.MetadataToken);
        Assert.Equal(sameToken, value.Before.MetadataToken == value.After.MetadataToken);

        // Refused Source acquisition is visible non-success, never an empty successful diff.
        Assert.Equal("Unavailable", value.Status);
        Assert.False(value.IsExact);
        Assert.Null(value.Diff);
        output.WriteLine($"Before: {value.Before.State}: {value.Before.Detail}");
        output.WriteLine($"After: {value.After.State}: {value.After.Detail}");
        AssertUnresolvedSource(value.Before);
        AssertUnresolvedSource(value.After);
        await pair.AssertScopesReleased();
    }

    [Fact]
    public async Task ExportPreservesDistinctEndpointSelectors()
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest value =
            await pair.Request("Counter", "Value");
        BrowserSourceComparisonRequest unchanged =
            await pair.Request("Counter", "Unchanged");
        BrowserSourceComparisonRequest request =
            value with { After = unchanged.After };

        BrowserSourceComparison result = Assert.IsType<BrowserSourceComparison>(
            (await Compare(request)).Value);

        Assert.Contains("::Value", result.Before.MemberIdentity);
        Assert.Contains("::Unchanged", result.After.MemberIdentity);
        Assert.NotEqual(
            result.Before.MemberIdentity,
            result.After.MemberIdentity);
        Assert.NotNull(result.Before.MetadataToken);
        Assert.NotNull(result.After.MetadataToken);
        await pair.AssertScopesReleased();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExportRetainsOneRequestedEndpointWithoutClaimingAbsence(
        bool requestBefore)
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest both =
            await pair.Request("Counter", "Value");
        BrowserSourceComparisonRequest request = both with
        {
            Before = requestBefore ? both.Before : null,
            After = requestBefore ? null : both.After,
        };

        BrowserSourceComparison result = Assert.IsType<BrowserSourceComparison>(
            (await Compare(request)).Value);

        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.Diff);
        BrowserSourceComparisonEndpoint requested =
            requestBefore ? result.Before : result.After;
        BrowserSourceComparisonEndpoint unrequested =
            requestBefore ? result.After : result.Before;
        Assert.NotEqual("Unrequested", requested.State);
        Assert.NotNull(requested.MemberIdentity);
        Assert.Equal("Unrequested", unrequested.State);
        Assert.Null(unrequested.MemberIdentity);
        Assert.Null(unrequested.MetadataToken);
        Assert.Null(unrequested.Detail);
        await pair.AssertScopesReleased();
    }

    [Fact]
    public async Task MemberMissingFromAfterIsNotFoundWithoutHidingTheBeforeEndpoint()
    {
        await using Pair pair = await Pair.OpenAsync();

        BrowserSourceComparisonResult result = await Compare(
            await pair.Request("Counter", "BeforeOnly"));

        BrowserSourceComparison value = Assert.IsType<BrowserSourceComparison>(result.Value);
        Assert.Equal("Unavailable", value.Status);
        Assert.False(value.IsExact);
        Assert.Null(value.Diff);
        Assert.Equal("NotFound", value.After.State);
        Assert.Contains("TargetNotFound", value.After.Detail);
        Assert.Null(value.After.MemberIdentity);
        Assert.Null(value.After.MetadataToken);
        Assert.Equal(AfterVersion, value.After.Version);
        Assert.NotNull(value.After.AssemblyIdentity);

        // The launching endpoint keeps its own resolved identity: a member that is absent in the
        // other version is not a comparison failure and does not retract Before.
        Assert.NotNull(value.Before.MemberIdentity);
        Assert.NotNull(value.Before.MetadataToken);
        await pair.AssertScopesReleased();
    }

    [Fact]
    public async Task SameVersionPairStaysTwoIndependentlyResolvedEndpoints()
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest request =
            await pair.Request("Counter", "Value", afterVersion: BeforeVersion);

        BrowserSourceComparison value = Assert.IsType<BrowserSourceComparison>(
            (await Compare(request)).Value);

        Assert.Equal(BeforeVersion, value.Before.Version);
        Assert.Equal(BeforeVersion, value.After.Version);
        Assert.Equal(value.Before.ModuleVersionId, value.After.ModuleVersionId);
        Assert.Equal(value.Before.MetadataToken, value.After.MetadataToken);
        Assert.Equal(value.Before.MemberIdentity, value.After.MemberIdentity);
        await pair.AssertScopesReleased(BeforeVersion);
    }

    [Theory]
    [InlineData("{", "JsonException")]
    [InlineData("null", "request is required")]
    public async Task MalformedRequestIsAnExpectedFailureThatReleasesTheOperation(
        string requestJson,
        string diagnostic)
    {
        string id = Guid.NewGuid().ToString();
        BrowserSourceComparisonResult result = Read(
            await DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSourceComparison(id, requestJson));

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains(diagnostic, result.Diagnostic);
        Assert.Equal(
            BrowserTypeSourceCancellationKind.NotActive,
            Cancel(id, "user").Kind);
    }

    [Fact]
    public async Task OversizedMalformedRequestRetainsExpectedFailure()
    {
        string requestJson = $"{{\"{new string('x', 8_180)}\":0}}";
        Assert.True(requestJson.Length <= BrowserSourceDiffProjection.MaximumRequestBytes);
        string id = Guid.NewGuid().ToString();

        BrowserSourceComparisonResult result = Read(
            await DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSourceComparison(
                id,
                requestJson));

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Equal(BrowserSourceDiffJson.BoundedFailureError, result.Error);
        Assert.Equal(BrowserSourceDiffJson.BoundedFailureDiagnostic, result.Diagnostic);
        Assert.Null(result.Capacity);
        Assert.Equal(
            BrowserTypeSourceCancellationKind.NotActive,
            Cancel(id, "user").Kind);
    }

    [Fact]
    public void SourceDiffAdmissionAcceptsEveryExactFirstProfileLimit()
    {
        BrowserSourceDiffProjection.AdmitRequest(
            new string('x', BrowserSourceDiffProjection.MaximumRequestBytes));
        BrowserSourceDiffProjection.AdmitEndpointText(
            new string('x', BrowserSourceDiffProjection.MaximumRawEndpointBytes),
            BrowserSourceDiffCapacityDimension.RawBeforeBytes,
            BrowserSourceDiffCapacityDimension.RawBeforeLines);
        BrowserSourceDiffProjection.AdmitEndpointText(
            string.Join('\n', Enumerable.Repeat("x",
                BrowserSourceDiffProjection.MaximumRawEndpointLines)),
            BrowserSourceDiffCapacityDimension.RawAfterBytes,
            BrowserSourceDiffCapacityDimension.RawAfterLines);
        BrowserSourceDiffProjection.AdmitProjectedShape(
            BrowserSourceDiffProjection.MaximumRelations,
            BrowserSourceDiffProjection.MaximumCoordinateOccurrences,
            BrowserSourceDiffProjection.MaximumMappedChanges,
            BrowserSourceDiffProjection.MaximumInnerMappings,
            BrowserSourceDiffProjection.MaximumAnnotations,
            BrowserSourceDiffProjection.MaximumAnnotationTextBytes);
        BrowserSourceDiffProjection.AdmitAuxiliaryText(
            [new string('x', BrowserSourceDiffProjection.MaximumAuxiliaryTextBytes)]);
    }

    [Theory]
    [InlineData(BrowserSourceDiffCapacityDimension.Relations, 0)]
    [InlineData(BrowserSourceDiffCapacityDimension.CoordinateOccurrences, 1)]
    [InlineData(BrowserSourceDiffCapacityDimension.MappedChanges, 2)]
    [InlineData(BrowserSourceDiffCapacityDimension.InnerMappings, 3)]
    [InlineData(BrowserSourceDiffCapacityDimension.Annotations, 4)]
    [InlineData(BrowserSourceDiffCapacityDimension.AnnotationTextBytes, 5)]
    public void ProjectedShapeOverLimitProducesTypedCapacity(
        BrowserSourceDiffCapacityDimension dimension,
        int selected)
    {
        int[] values =
        [
            BrowserSourceDiffProjection.MaximumRelations,
            BrowserSourceDiffProjection.MaximumCoordinateOccurrences,
            BrowserSourceDiffProjection.MaximumMappedChanges,
            BrowserSourceDiffProjection.MaximumInnerMappings,
            BrowserSourceDiffProjection.MaximumAnnotations,
            BrowserSourceDiffProjection.MaximumAnnotationTextBytes,
        ];
        values[selected]++;

        BrowserSourceDiffCapacityException error = Assert.Throws<
            BrowserSourceDiffCapacityException>(() =>
                BrowserSourceDiffProjection.AdmitProjectedShape(
                    values[0], values[1], values[2],
                    values[3], values[4], values[5]));

        Assert.Equal(dimension, error.Capacity.Dimension);
        Assert.Equal(values[selected] - 1, error.Capacity.Limit);
        Assert.Equal(values[selected], error.Capacity.Actual);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndpointOverLimitProducesTypedByteOrLineCapacity(bool bytes)
    {
        BrowserSourceDiffCapacityException error = Assert.Throws<
            BrowserSourceDiffCapacityException>(() =>
                BrowserSourceDiffProjection.AdmitEndpointText(
                    bytes
                        ? new string('x',
                            BrowserSourceDiffProjection.MaximumRawEndpointBytes + 1)
                        : new string('\n',
                            BrowserSourceDiffProjection.MaximumRawEndpointLines),
                    BrowserSourceDiffCapacityDimension.RawBeforeBytes,
                    BrowserSourceDiffCapacityDimension.RawBeforeLines));

        Assert.Equal(
            bytes
                ? BrowserSourceDiffCapacityDimension.RawBeforeBytes
                : BrowserSourceDiffCapacityDimension.RawBeforeLines,
            error.Capacity.Dimension);
        Assert.Equal(error.Capacity.Limit + 1, error.Capacity.Actual);
    }

    [Fact]
    public void RequestAndAuxiliaryOverLimitProduceTypedCapacity()
    {
        BrowserSourceDiffCapacityException request = Assert.Throws<
            BrowserSourceDiffCapacityException>(() =>
                BrowserSourceDiffProjection.AdmitRequest(
                    new string('x',
                        BrowserSourceDiffProjection.MaximumRequestBytes + 1)));
        Assert.Equal(
            BrowserSourceDiffCapacityDimension.RequestBytes,
            request.Capacity.Dimension);

        BrowserSourceDiffCapacityException auxiliary = Assert.Throws<
            BrowserSourceDiffCapacityException>(() =>
                BrowserSourceDiffProjection.AdmitAuxiliaryText(
                [
                    new string('x',
                        BrowserSourceDiffProjection.MaximumAuxiliaryTextBytes + 1),
                ]));
        Assert.Equal(
            BrowserSourceDiffCapacityDimension.AuxiliaryTextBytes,
            auxiliary.Capacity.Dimension);
    }

    [Fact]
    public void DiagnosticExpansionRetainsUnexpectedFailure()
    {
        var oversized = new BrowserSourceComparisonResult(
            Version: 1,
            BrowserSourceComparisonResultKind.Failed,
            Value: null,
            BrowserTypeSourceFailureKind.Unexpected,
            Error: new string('x',
                BrowserSourceDiffProjection.MaximumAuxiliaryTextBytes + 1),
            Diagnostic: null,
            Reason: null,
            Capacity: null);

        BrowserSourceComparisonResult result = Read(
            BrowserSourceDiffJson.Serialize(oversized));

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Unexpected, result.FailureKind);
        Assert.Equal(BrowserSourceDiffJson.BoundedFailureError, result.Error);
        Assert.Equal(BrowserSourceDiffJson.BoundedFailureDiagnostic, result.Diagnostic);
        Assert.Null(result.Capacity);
    }

    [Fact]
    public void EscapingExpansionReturnsTypedEncodedResultCapacity()
    {
        string[] lines = Enumerable.Repeat(new string('"', 127),
            BrowserSourceDiffProjection.MaximumRawEndpointLines).ToArray();
        BrowserSourceDiffRelation[] relations =
        [
            .. Enumerable.Range(0, lines.Length).Select(index =>
                new BrowserSourceDiffRelation(
                    BrowserSourceDiffRelationKind.Removal,
                    [index], [], null, null)),
            .. Enumerable.Range(0, lines.Length).Select(index =>
                new BrowserSourceDiffRelation(
                    BrowserSourceDiffRelationKind.Addition,
                    [], [index], null, null)),
        ];
        BrowserSourceDiffInnerMapping[] innerMappings =
            Enumerable.Range(0, BrowserSourceDiffProjection.MaximumInnerMappings)
                .Select(_ => new BrowserSourceDiffInnerMapping(
                    new(0, 0, 1), new(0, 0, 1)))
                .ToArray();
        BrowserSourceDiffAnnotation[] annotations =
            Enumerable.Range(0, BrowserSourceDiffProjection.MaximumAnnotations)
                .Select(_ => new BrowserSourceDiffAnnotation(
                    "", BrowserSourceDiffSeverity.Note,
                    BrowserSourceDiffAnnotationTargetKind.Change,
                    null, null, null))
                .ToArray();
        BrowserSourceDiffChange[] changes =
            Enumerable.Range(0, BrowserSourceDiffProjection.MaximumMappedChanges)
                .Select(index => new BrowserSourceDiffChange(
                    new(index % lines.Length, 1),
                    new(index % lines.Length, 1),
                    index == 0 ? innerMappings : [],
                    index == 0 ? annotations : []))
                .ToArray();
        var diff = new BrowserSourceDiff(
            1,
            new(null, lines, BrowserSourceDiffLineTerminator.Absent),
            new(null, lines, BrowserSourceDiffLineTerminator.Absent),
            relations,
            new(0, 0, 0, 0, 0, 0),
            changes);
        var endpointRequest = new BrowserSourceComparisonEndpointRequest(
            "T", "M()", "void T.M()", "0123456789", "T", "M");
        var request = new BrowserSourceComparisonRequest(
            "P", "1.0.0", "2.0.0", "net11.0", "A",
            endpointRequest, endpointRequest);
        static BrowserSourceComparisonEndpoint Endpoint(string version) =>
            new("P", version, "net11.0", "A", "A.dll", null, "A", "T::M()",
                0x06000001, "Available", null, null, null, null, null);
        var result = new BrowserSourceComparisonResult(
            1,
            BrowserSourceComparisonResultKind.Succeeded,
            new(request, "Compared", false, Endpoint("1.0.0"), Endpoint("2.0.0"),
                diff, null),
            null, null, null, null, null);

        BrowserSourceComparisonResult encoded = Read(
            BrowserSourceDiffJson.Serialize(result));

        Assert.Equal(BrowserSourceComparisonResultKind.TooComplex, encoded.Kind);
        BrowserSourceDiffCapacity capacity =
            Assert.IsType<BrowserSourceDiffCapacity>(encoded.Capacity);
        Assert.Equal(
            BrowserSourceDiffCapacityDimension.EncodedResultBytes,
            capacity.Dimension);
        Assert.Equal(
            BrowserSourceDiffProjection.MaximumEncodedResultBytes,
            capacity.Limit);
        Assert.True(capacity.Actual > capacity.Limit);
    }

    [Theory]
    [InlineData("", BeforeVersion, AfterVersion, "Value()", "PackageId")]
    [InlineData("Source.Comparison.Unused", "not a version", AfterVersion, "Value()",
        "two exact package versions")]
    [InlineData("Source.Comparison.Unused", BeforeVersion, "", "Value()",
        "two exact package versions")]
    [InlineData("Source.Comparison.Unused", BeforeVersion, AfterVersion, "",
        "StableSelector")]
    public async Task InvalidPairOrSelectionIsRejectedBeforeAnyAcquisition(
        string packageId,
        string beforeVersion,
        string afterVersion,
        string stableSelector,
        string message)
    {
        var request = new BrowserSourceComparisonRequest(
            packageId,
            beforeVersion,
            afterVersion,
            Framework,
            AssemblyName,
            new(
                "SourceDiffFixture.Counter",
                stableSelector,
                "int SourceDiffFixture.Counter.Value()",
                "0123456789",
                "SourceDiffFixture.Counter",
                "Value"),
            new(
                "SourceDiffFixture.Counter",
                stableSelector,
                "int SourceDiffFixture.Counter.Value()",
                "0123456789",
                "SourceDiffFixture.Counter",
                "Value"));

        BrowserSourceComparisonResult result = await Compare(request);

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains(message, result.Error);
    }

    [Fact]
    public async Task PairWithoutARequestedEndpointIsRejectedBeforeAcquisition()
    {
        var request = new BrowserSourceComparisonRequest(
            "Source.Comparison.Unused",
            BeforeVersion,
            AfterVersion,
            Framework,
            AssemblyName,
            null,
            null);

        BrowserSourceComparisonResult result = await Compare(request);

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains("at least one endpoint member", result.Error);
    }

    [Fact]
    public async Task NonMethodAnchorDoesNotSilentlyBecomeAnAccessorDeclaration()
    {
        string packageId = "Source.Comparison.Accessor." + Guid.NewGuid().ToString("N");
        await RegisterAsync(FixtureCatalog.InspectWebMethodBodies, BeforeVersion, packageId);
        await using BrowserScopeLease<BrowserInspectionScope> lease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId, BeforeVersion, Framework, TestContext.Current.CancellationToken);
        ApiType type = Assert.Single(
            Surface(lease.Scope).Types,
            candidate => candidate.FullName == typeof(Left).FullName);
        ApiMember property = Assert.Single(
            type.Members,
            member => member.Name == nameof(Left.Value));
        MemberAnchor anchor = ApiMemberIdentity.GetMemberAnchor(type, property);
        var endpoint = new BrowserSourceComparisonEndpointRequest(
            type.DefinitionName!.ToEscapedFullName(),
            anchor.StableSelector,
            anchor.CanonicalSignature,
            anchor.Fingerprint,
            anchor.TypeFullName,
            anchor.MemberName);

        BrowserSourceComparisonResult result = await Compare(new(
            packageId,
            BeforeVersion,
            BeforeVersion,
            Framework,
            "InspectWeb.MethodBodyFixtures.dll",
            endpoint,
            endpoint));

        Assert.Equal(BrowserSourceComparisonResultKind.Succeeded, result.Kind);
        BrowserSourceComparison value =
            Assert.IsType<BrowserSourceComparison>(result.Value);
        Assert.Equal("Unavailable", value.Status);
        Assert.Equal("NotFound", value.Before.State);
        Assert.Equal("NotFound", value.After.State);
        Assert.Null(value.Diff);
        await BrowserPackageWorkspace.RemoveScopeAsync(lease.Scope);
    }

    [Fact]
    public async Task CancellationPublishesNoPartialPairAndSettlesEveryProtectedScope()
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest request = await pair.Request("Counter", "Value");
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();

        Task<string> pending = DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSourceComparison(id, Serialize(request));
        Assert.False(pending.IsCompleted);
        Assert.Equal(
            BrowserTypeSourceCancellationKind.Requested,
            Cancel(id, "superseded").Kind);

        BrowserSourceComparisonResult result = Read(await pending);
        Assert.Equal(BrowserSourceComparisonResultKind.Canceled, result.Kind);
        Assert.Equal("superseded", result.Reason);
        Assert.Null(result.Value);
        Assert.Null(result.Error);
        Assert.Null(result.FailureKind);
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, Cancel(id, "user").Kind);

        Task<BrowserSourceOperationLease> successor =
            BrowserSourceOperationCoordinator.BeginAsync().AsTask();
        Assert.False(successor.IsCompleted);
        holder.Dispose();
        using BrowserSourceOperationLease next = await successor;
        Assert.False(next.CancellationToken.IsCancellationRequested);
        await pair.AssertScopesReleased();
    }

    [Fact]
    public async Task AuthoredSourceOnlyChangeComparesVerifiedDeclarations()
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host();

        BrowserSourceComparison value = await pair.CompareThrough(host, "Counter", "Value");

        Assert.Equal("Compared", value.Status);
        Assert.False(value.IsExact);
        Assert.Equal("Available", value.Before.State);
        Assert.Equal("Available", value.After.State);
        BrowserSourceDiff diff = Assert.IsType<BrowserSourceDiff>(value.Diff);
        Assert.Contains("1 + 2", string.Join('\n', diff.Before.Lines));
        Assert.Contains("=> 3", string.Join('\n', diff.After.Lines));
        Assert.DoesNotContain("=> 3", string.Join('\n', diff.Before.Lines));
        Assert.Null(value.Before.Text);
        Assert.Null(value.After.Text);
        Assert.Null(value.Before.BrowseUrl);
        Assert.Null(value.After.BrowseUrl);
        Assert.NotEqual(value.Before.ModuleVersionId, value.After.ModuleVersionId);

        // The debug information travelled inside each image, so no symbol server was consulted,
        // and both texts arrived over a fetch the production browser policy admitted rather than
        // from the compiler inputs still sitting on this machine.
        Assert.Empty(host.SymbolRequests);
        Assert.Equal(2, host.SourceRequests.Count);
        Assert.All(host.SourceRequests, uri =>
            Assert.Equal("raw.githubusercontent.com", uri.IdnHost));

        // The authored declaration is one line in each version. The native producer polarity
        // survives as distinct analytical relations over the shared endpoint arrays.
        Assert.Equal(2, diff.Relations.Length);
        BrowserSourceDiffRelation removed = Assert.Single(
            diff.Relations,
            relation => relation.Kind == BrowserSourceDiffRelationKind.Removal);
        Assert.Equal([0], removed.BeforeCoordinates);
        Assert.Empty(removed.AfterCoordinates);
        BrowserSourceDiffRelation added = Assert.Single(
            diff.Relations,
            relation => relation.Kind == BrowserSourceDiffRelationKind.Addition);
        Assert.Empty(added.BeforeCoordinates);
        Assert.Equal([0], added.AfterCoordinates);
        Assert.Equal(1, diff.Statistics.Added);
        Assert.Equal(1, diff.Statistics.Removed);
        Assert.Single(diff.Changes);
    }

    [Fact]
    public async Task PairAdmissionRunsBeforeBrowserProjection()
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host();
        bool admitted = false;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pair.CompareThrough(
                host,
                "Counter",
                "Value",
                admitEndpoints: (before, after) =>
                {
                    admitted = true;
                    Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(before);
                    Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(after);
                    throw new InvalidOperationException("Admission stopped comparison.");
                }));

        Assert.True(admitted);
        Assert.Equal("Admission stopped comparison.", error.Message);
    }

    // PR-fast: bounded public projection over the existing embedded-PDB pair.
    [Theory]
    [InlineData(true, "SourceDeadlineExceeded")]
    [InlineData(false, "SourceLimitExceeded")]
    public async Task SettlementBoundsKeepTruthfulPublicOutcomes(bool deadline, string expected)
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host();
        SourceHouseLimits limits = host.Context.MemberSourcePairLimits;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            MemberSourcePairTimeout = deadline
                ? TimeSpan.FromTicks(1) : host.Context.MemberSourcePairTimeout,
            MemberSourcePairLimits = deadline ? limits : new(
                limits.MaximumAssemblyBytes, limits.MaximumPortablePdbBytes,
                limits.TargetBounds, limits.SourceLinkReadLimits,
                limits.MaximumDocuments, limits.MaximumTargetMappings,
                limits.MaximumCandidateAttempts, 1, limits.MaximumSourceTextCharacters),
        };

        BrowserSourceComparison value = await pair.CompareThrough(host, "Counter", "Value", context);

        Assert.Equal("Unavailable", value.Status);
        Assert.Null(value.Diff);
        foreach (var endpoint in new[] { value.Before, value.After })
        {
            Assert.Equal("Failed", endpoint.State);
            Assert.StartsWith($"{expected}:", endpoint.Detail);
            Assert.Null(endpoint.Text);
        }
        await pair.AssertScopesReleased();
    }

    [Theory]
    [InlineData("Unchanged", true, false)]
    [InlineData("SameSource", true, false)]
    [InlineData("Reordered", false, false)]
    [InlineData("MovedBlock", false, true)]
    [InlineData("MovedBlockAndEdit", false, true)]
    public async Task NativeLinePolarityAndMovementSurviveTheBrowserProjection(
        string memberName,
        bool exact,
        bool moved)
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host();

        BrowserSourceComparison value = await pair.CompareThrough(host, "Counter", memberName);

        Assert.Equal("Compared", value.Status);
        Assert.Equal(exact, value.IsExact);
        BrowserSourceDiff diff = Assert.IsType<BrowserSourceDiff>(value.Diff);
        Assert.NotEmpty(diff.Relations);
        Assert.Equal(
            moved,
            diff.Relations.Any(relation =>
                relation.Placement == BrowserSourceDiffPlacementKind.Moved));
        Assert.All(diff.Relations, relation =>
        {
            Assert.All(
                relation.BeforeCoordinates,
                coordinate => Assert.InRange(
                    coordinate,
                    0,
                    diff.Before.Lines.Length - 1));
            Assert.All(
                relation.AfterCoordinates,
                coordinate => Assert.InRange(
                    coordinate,
                    0,
                    diff.After.Lines.Length - 1));
        });
        if (exact)
        {
            Assert.All(diff.Relations, relation =>
            {
                Assert.Equal(
                    BrowserSourceDiffRelationKind.Correspondence,
                    relation.Kind);
                Assert.Equal(
                    BrowserSourceDiffContentKind.Unchanged,
                    relation.Content);
                Assert.Equal(
                    BrowserSourceDiffPlacementKind.Stable,
                    relation.Placement);
                Assert.Equal(
                    relation.BeforeCoordinates,
                    relation.AfterCoordinates);
            });
            Assert.Empty(diff.Changes);
        }

        if (moved)
        {
            BrowserSourceDiffRelation movedRelation = diff.Relations.First(
                relation =>
                    relation.Placement == BrowserSourceDiffPlacementKind.Moved);
            Assert.NotEqual(
                movedRelation.BeforeCoordinates,
                movedRelation.AfterCoordinates);
            Assert.Equal(
                diff.Before.Lines[movedRelation.BeforeCoordinates[0]],
                diff.After.Lines[movedRelation.AfterCoordinates[0]]);
        }

        if (memberName == "MovedBlockAndEdit")
        {
            Assert.Contains(diff.After.Lines, line => line.Contains("+ 1"));
            Assert.True(diff.Statistics.Added > 0
                || diff.Statistics.ChangedAfter > 0);
        }
    }

    [Fact]
    public async Task UnavailableAfterSourceLeavesTheAvailableDeclarationInspectable()
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host(afterSource: false);

        BrowserSourceComparison value = await pair.CompareThrough(host, "Counter", "Value");

        Assert.Equal("Unavailable", value.Status);
        Assert.False(value.IsExact);
        Assert.Null(value.Diff);
        Assert.Equal("Available", value.Before.State);
        Assert.Contains("1 + 2", value.Before.Text);
        Assert.Null(value.Before.BrowseUrl);
        AssertUnresolvedSource(value.After);
        Assert.Contains(
            host.SourceRequests,
            uri => uri.AbsolutePath.Contains("/source-comparison/v2/", StringComparison.Ordinal));
        Assert.NotNull(value.After.MemberIdentity);
        Assert.NotNull(value.After.MetadataToken);
    }

    [Fact]
    public async Task ChecksumMismatchDoesNotAuthorizeBrowseDestination()
    {
        await using Pair pair = await Pair.OpenAsync();
        byte[] mismatched = FixtureSource(
            FixtureCatalog.InspectWebSourceComparisonPair.New);
        mismatched[^1] ^= 1;
        using var host = new SourcePairHost(
            FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.Old),
            mismatched);

        BrowserSourceComparison value =
            await pair.CompareThrough(host, "Counter", "Value");

        Assert.Equal("Unavailable", value.Status);
        Assert.Equal("Failed", value.After.State);
        Assert.Contains("ChecksumMismatch", value.After.Detail);
        Assert.Null(value.After.Text);
        Assert.Null(value.After.BrowseUrl);
    }

    static void AssertUnresolvedSource(BrowserSourceComparisonEndpoint endpoint)
    {
        Assert.Contains(endpoint.State, (string[])["Unavailable", "Failed"]);
        Assert.NotEmpty(endpoint.Detail!);
        Assert.Null(endpoint.Text);
        Assert.Null(endpoint.BrowseUrl);
    }

    [Theory]
    [InlineData(
        "https://raw.githubusercontent.com/example/repository/0123456789abcdef0123456789abcdef01234567/src/Widget.cs",
        "https://github.com/example/repository/blob/0123456789abcdef0123456789abcdef01234567/src/Widget.cs")]
    [InlineData(
        "https://raw.githubusercontent.com/example/repository/v1/src/Widget.cs",
        null)]
    [InlineData("https://example.test/src/Widget.cs", null)]
    public void BrowseUrlRequiresAttributedGitHubProvenance(
        string resolvedUrl,
        string? expected)
    {
        Assert.Equal(expected, BrowserSourceDiffProjection.BrowseUrl(resolvedUrl));
    }

    static async Task<BrowserSourceComparisonResult> Compare(
        BrowserSourceComparisonRequest request) =>
        Read(await DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSourceComparison(
            Guid.NewGuid().ToString(), Serialize(request)));

    static string Serialize(BrowserSourceComparisonRequest request) =>
        JsonSerializer.Serialize(
            request, BrowserSourceJsonContext.Default.BrowserSourceComparisonRequest);

    static BrowserSourceComparisonResult Read(string json) =>
        JsonSerializer.Deserialize(
            json, BrowserSourceJsonContext.Default.BrowserSourceComparisonResult)!;

    static BrowserTypeSourceCancellation Cancel(string id, string reason) =>
        JsonSerializer.Deserialize(
            DotnetInspect.Web.Interop.Source.SourceExports.CancelMemberSourceComparison(id, reason),
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!;

    static ApiSurface Surface(BrowserInspectionScope scope) =>
        scope.UseSurface(group =>
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    group,
                    ApiSurfaceScope.IncludeAll,
                    BrowserApiSurfacePolicy.Limits).Assemblies.Assemblies.Single())
                .Value.Surface);

    /// <summary>
    /// Registers the fixture's own built package, so the browser workspace reads the same
    /// <c>.nupkg</c> a browser acceptance run acquires instead of a test-shaped archive.
    /// </summary>
    static async Task RegisterAsync(
        FixtureDefinition fixture, string version, string? packageId = null) =>
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId ?? SourcePackageId,
                version,
                File.ReadAllBytes(fixture.AssetPath("package")),
                fromCache: false));

    static SourcePairHost Host(bool afterSource = true) =>
        new(
            FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.Old),
            afterSource ? FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.New) : null);

    static byte[] FixtureSource(FixtureDefinition fixture) =>
        File.ReadAllBytes(Assert.Single(
            fixture.SourcePaths(), path => Path.GetFileName(path) == "Counter.cs"));

    static AssemblyMemberSourcePairEndpointRequest ProductEndpoint(
        BrowserSourceComparisonEndpointRequest request) =>
        new(
            MetadataTypeDefinitionName.ParseSerialized(request.TypeIdentity)
                is MetadataTypeDefinitionNameResult.Valid valid
                    ? valid.Name
                    : throw new InvalidOperationException(
                        "The test endpoint Type identity is invalid."),
            new(
                request.StableSelector,
                request.CanonicalSignature,
                request.Fingerprint,
                request.TypeFullName,
                request.MemberName));

    sealed record MemberSelection(
        string PackageId,
        string Framework,
        string Assembly,
        string TypeIdentity,
        string MemberName,
        string SelectorKey,
        int MetadataToken);

    sealed class Pair(string packageId) : IAsyncDisposable
    {
        internal string PackageId => packageId;

        internal static async Task<Pair> OpenAsync()
        {
            await RegisterAsync(
                FixtureCatalog.InspectWebSourceComparisonPair.Old, BeforeVersion);
            await RegisterAsync(
                FixtureCatalog.InspectWebSourceComparisonPair.New, AfterVersion);
            return new(SourcePackageId);
        }

        internal async Task<BrowserSourceComparisonRequest> Request(
            string typeName,
            string memberName,
            string afterVersion = AfterVersion)
        {
            await using BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId, BeforeVersion, Framework);
            ApiType type = Assert.Single(
                Surface(lease.Scope).Types,
                candidate => candidate.FullName == $"SourceDiffFixture.{typeName}");
            ApiMember member = Assert.Single(
                type.Members, candidate => candidate.Name == memberName);
            MemberAnchor anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
            var endpoint = new BrowserSourceComparisonEndpointRequest(
                type.DefinitionName!.ToEscapedFullName(),
                anchor.StableSelector,
                anchor.CanonicalSignature,
                anchor.Fingerprint,
                anchor.TypeFullName,
                anchor.MemberName);
            return new(
                packageId,
                BeforeVersion,
                afterVersion,
                Framework,
                AssemblyName,
                endpoint,
                endpoint);
        }

        internal async Task<MemberSelection> Selection(
            string typeName,
            string memberName)
        {
            await using BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId, BeforeVersion, Framework);
            ApiType type = Assert.Single(
                Surface(lease.Scope).Types,
                candidate => candidate.FullName == $"SourceDiffFixture.{typeName}");
            ApiMember member = Assert.Single(
                type.Members, candidate => candidate.Name == memberName);
            CallGraphMemberBodySelector body = Assert.Single(
                CallGraphMemberResolver.CreateBodySelectors(type, member));
            return new(
                packageId,
                Framework,
                AssemblyName,
                type.DefinitionName!.ToEscapedFullName(),
                body.MemberName,
                body.SelectorKey,
                body.BodyToken);
        }

        /// <summary>
        /// Runs the export's own resolution, leasing, and paired query over both registered
        /// versions, substituting only the Source transport so the fixtures' controlled SourceLink
        /// map is reachable offline.
        /// </summary>
        internal async Task<BrowserSourceComparison> CompareThrough(
            SourcePairHost host,
            string typeName,
            string memberName,
            AssemblyContextSourceQueryContext? sourceContext = null,
            Action<AssemblyMemberSourcePairEndpoint, AssemblyMemberSourcePairEndpoint>?
                admitEndpoints = null)
        {
            BrowserSourceComparisonRequest request = await Request(typeName, memberName);
            AssemblyMemberSourcePairEndpointRequest selectedBefore =
                ProductEndpoint(request.Before!);
            AssemblyMemberSourcePairEndpointRequest selectedAfter =
                ProductEndpoint(request.After!);
            await using BrowserScopeLease<BrowserInspectionScope> beforeLease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    request.PackageId,
                    request.BeforeVersion,
                    request.Framework,
                    TestContext.Current.CancellationToken);
            await using BrowserScopeLease<BrowserInspectionScope> afterLease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    request.PackageId, request.AfterVersion, request.Framework,
                    TestContext.Current.CancellationToken);
            BrowserInspectionScope afterScope = afterLease.Scope;
            BrowserInspectionScope beforeScope = beforeLease.Scope;
            BrowserPackageCoordinate beforeCoordinate =
                beforeScope.Coordinates[0];
            BrowserWorkspaceParticipant before =
                beforeScope.ImplementationParticipant(
                    beforeScope.SurfaceParticipant(
                        beforeCoordinate,
                        beforeCoordinate.CompileAsset(request.Assembly)));
            BrowserPackageCoordinate afterCoordinate = afterScope.Coordinates[0];
            BrowserWorkspaceParticipant after = afterScope.ImplementationParticipant(
                afterScope.SurfaceParticipant(
                    afterCoordinate, afterCoordinate.CompileAsset(request.Assembly)));
            InspectionEnvelope<AssemblyMemberSourcePairResult> inspection =
                await beforeScope.UseImplementationParticipant(
                before,
                (beforeGroup, beforeParticipant) => afterScope.UseImplementationParticipant(
                    after,
                    (afterGroup, afterParticipant) =>
                        MemberSourcePairInspection.ExecuteAsync(
                            beforeGroup, beforeParticipant, afterGroup, afterParticipant,
                            new(selectedBefore, selectedAfter),
                            sourceContext ?? host.Context,
                            TestContext.Current.CancellationToken,
                            admitEndpoints)));
            AssemblyMemberSourcePairResult pair = inspection.Content;
            foreach (var endpoint in new[] { pair.Before, pair.After })
            {
                var resolved = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(endpoint);
                Assert.NotNull(resolved.HouseOutcome);
                if (resolved.HouseOutcome is not SourceHouseOutcome.Incomplete
                    { Boundary: SourceHouseIncompleteBoundary.Deadline })
                {
                    Assert.Equal(SourceHousePdbContributionKind.Embedded,
                        resolved.HouseOutcome.PdbContribution.Kind);
                }
            }
            return BrowserSourceComparisonProjection.Project(
                request, pair, before, after);
        }

        internal async Task AssertScopesReleased(params string[] versions)
        {
            foreach (string version in versions.Length == 0
                ? [BeforeVersion, AfterVersion]
                : versions)
            {
                await using BrowserScopeLease<BrowserInspectionScope> lease =
                    await BrowserPackageWorkspace.OpenScopeAsync(
                        packageId, version, Framework);
                BrowserInspectionScope scope = lease.Scope;
                await BrowserPackageWorkspace.RemoveScopeAsync(scope);
                await lease.DisposeAsync();
                Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (string version in (string[])[BeforeVersion, AfterVersion])
            {
                await using BrowserScopeLease<BrowserInspectionScope> lease =
                    await BrowserPackageWorkspace.OpenScopeAsync(
                        packageId, version, Framework);
                await BrowserPackageWorkspace.RemoveScopeAsync(lease.Scope);
            }
        }
    }

    /// <summary>
    /// Substitutes only the HTTP transport. The production browser fetch policy still decides
    /// which source requests may leave, and the PDB still comes from the inspected image.
    /// </summary>
    sealed class SourcePairHost : IDisposable
    {
        internal const string BeforeSourcePrefix =
            "https://raw.githubusercontent.com/dotnet-inspect-fixtures/source-comparison/v1/";

        internal const string AfterSourcePrefix =
            "https://raw.githubusercontent.com/dotnet-inspect-fixtures/source-comparison/v2/";

        readonly HttpClient _symbolClient;
        readonly HttpClient _sourceClient;
        readonly List<Uri> _symbolRequests = [];
        readonly List<Uri> _sourceRequests = [];

        internal SourcePairHost(byte[]? beforeSource, byte[]? afterSource)
        {
            _symbolClient = new HttpClient(new ContentHandler(uri =>
            {
                lock (_symbolRequests)
                    _symbolRequests.Add(uri);
                return null;
            }));
            _sourceClient = new HttpClient(new ContentHandler(uri =>
            {
                lock (_sourceRequests)
                    _sourceRequests.Add(uri);
                return uri.AbsolutePath.Contains("/source-comparison/v2/", StringComparison.Ordinal)
                    ? afterSource
                    : beforeSource;
            }));
            Context = new AssemblyContextSourceQueryContext(
                _symbolClient,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
                new SourceFetch(
                    _sourceClient,
                    new InMemorySourceContentStore(),
                    BrowserSourceFetchPolicy.Instance));
        }

        internal AssemblyContextSourceQueryContext Context { get; }

        /// <summary>Every symbol-server request, which an embedded PDB should never need.</summary>
        internal IReadOnlyList<Uri> SymbolRequests
        {
            get
            {
                lock (_symbolRequests)
                    return [.. _symbolRequests];
            }
        }

        /// <summary>Every source request the production fetch policy allowed to leave.</summary>
        internal IReadOnlyList<Uri> SourceRequests
        {
            get
            {
                lock (_sourceRequests)
                    return [.. _sourceRequests];
            }
        }

        public void Dispose()
        {
            _symbolClient.Dispose();
            _sourceClient.Dispose();
        }

        sealed class ContentHandler(Func<Uri, byte[]?> response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? content = response(request.RequestUri!);
                return Task.FromResult(new HttpResponseMessage(
                    content is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                {
                    Content = content is null ? null : new ByteArrayContent(content),
                    RequestMessage = request,
                });
            }
        }
    }
}
