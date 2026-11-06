using System.Text.Json;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    [Fact]
    public async Task ResolvedAssembly_MatchesPackageTypeAndRetainsAuthorizedPolicy()
    {
        string typeName = typeof(ExactTypeInspectionOperation).FullName!;
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.CreateFromPath(
            typeof(ExactTypeInspectionOperation).Assembly.Location,
            AssemblyResolutionProvenance.Local("resolved exact Type test"));
        IAssemblyBindingPolicy policy = NoResolverAssemblyBindingPolicy.Instance;
        InspectionEnvelope<ExactTypeInspectionResult> resolved =
            await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly,
                policy,
                typeName,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                (envelope, target, _, _) =>
                {
                    Assert.NotNull(target);
                    Assert.Same(policy, target.BindingPolicy);
                    Assert.Equal(assembly.Identity, target.Assembly.Identity);
                    Assert.Equal(typeName, target.Type.FullName);
                    return ValueTask.FromResult(envelope);
                },
                TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        InspectionEnvelope<ExactTypeInspectionResult> package =
            await ExecuteHouseAsync(
                new(PackageId, Version, Framework, typeName),
                LoadOptions(client, await CachedStoreAsync()),
                TestContext.Current.CancellationToken);

        Assert.True(resolved.Content.IsAvailable);
        AssertAvailable(package);
        Assert.Equal(package.Content.MatchedType, resolved.Content.MatchedType);
        Assert.Equal(package.Content.RequestedAssembly, resolved.Content.RequestedAssembly);
        Assert.Equal(package.Content.SupplierAssembly, resolved.Content.SupplierAssembly);
        Assert.Equal(
            JsonSerializer.Serialize(package.Content.Type),
            JsonSerializer.Serialize(resolved.Content.Type));
        Assert.IsType<InspectionShare.NonProjectable>(resolved.Share);
    }

    [Fact]
    public async Task ResolvedAssembly_FollowsAuthorizedForwarderAndPreservesOccurrence()
    {
        const string facadeName = "Resolved.Facade";
        const string supplierName = "Resolved.Supplier";
        var supplierIdentity = new AssemblyReferenceIdentity(
            supplierName, new Version(1, 0, 0, 0), null, null);
        ResolvedAssemblyReference supplier = Image(
            supplierIdentity,
            BuildMetadataAssembly(
                supplierName, Guid.NewGuid(), true, "Exact", "Widget"));
        ResolvedAssemblyReference facade = Image(
            new(facadeName, new Version(1, 0, 0, 0), null, null),
            BuildMetadataAssembly(
                facadeName, Guid.NewGuid(), false, "Exact", "Widget",
                supplierIdentity));
        IAcquisitionFreeAssemblyBindingPolicy policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                new[] { facade, supplier }.Select(
                    assembly => (
                        assembly,
                        (IAcquisitionFreeAssemblyBindingPolicy)
                            NoResolverAssemblyBindingPolicy.Instance)));

        InspectionEnvelope<ExactTypeInspectionResult> result =
            await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                facade,
                policy,
                "Exact.Widget",
                ApiSurfaceScope.Public,
                (envelope, target, _, _) =>
                {
                    Assert.NotNull(target);
                    Assert.Same(policy, target.BindingPolicy);
                    Assert.Equal(supplier.Identity, target.Assembly.Identity);
                    Assert.Equal(
                        supplier.Identity,
                        target.Occurrence.Assembly.Identity);
                    return ValueTask.FromResult(envelope);
                },
                TestContext.Current.CancellationToken);

        Assert.True(result.Content.IsAvailable);
        Assert.True(result.Content.Type!.IsForwarded);
        Assert.Equal(facadeName, result.Content.RequestedAssembly!.Identity.Name);
        Assert.Equal(supplierName, result.Content.SupplierAssembly!.Identity.Name);
        ExactTypeForwardingHop hop = Assert.Single(result.Content.ForwardingHops);
        Assert.Equal(facadeName, hop.Source.Name);
        Assert.Equal(supplierName, hop.Target.Name);

        static ResolvedAssemblyReference Image(
            AssemblyReferenceIdentity identity, byte[] bytes) =>
            ResolvedAssemblyReference.Create(
                identity, null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local("resolved forwarding test"));
    }

    [Theory]
    [InlineData("Missing.Type", ExactTypeInspectionOutcome.NotFound)]
    [InlineData("Missing.Type.More", ExactTypeInspectionOutcome.NotFound)]
    [InlineData("*TypeInspectionOperation", ExactTypeInspectionOutcome.Ambiguous)]
    [InlineData("Available", ExactTypeInspectionOutcome.Ambiguous)]
    public async Task ResolvedAssembly_NonUniqueSelectionDoesNotLendATarget(
        string query, ExactTypeInspectionOutcome expected)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.CreateFromPath(
            typeof(ExactTypeInspectionOperation).Assembly.Location,
            AssemblyResolutionProvenance.Local("resolved selection test"));
        InspectionEnvelope<ExactTypeInspectionResult> result =
            await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance,
                query,
                ApiSurfaceScope.Public,
                (envelope, target, catalog, _) =>
                {
                    Assert.Null(target);
                    Assert.NotNull(catalog);
                    return ValueTask.FromResult(envelope);
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Content.Outcome);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == InspectionDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("Exact.Healthy", ExactTypeInspectionOutcome.Available)]
    [InlineData("Exact.Forwarded", ExactTypeInspectionOutcome.Unavailable)]
    [InlineData("Exact.Missing", ExactTypeInspectionOutcome.NotFound)]
    public async Task ResolvedAssembly_ScopesUnresolvedForwarderToItsSubject(
        string query, ExactTypeInspectionOutcome expected)
    {
        bool expectedAvailable = expected == ExactTypeInspectionOutcome.Available;
        var identity = new AssemblyReferenceIdentity(
            "Resolved.Mixed", new Version(1, 0, 0, 0), null, null);
        byte[] image = BuildMetadataAssembly(
            identity.Name,
            Guid.NewGuid(),
            true,
            "Exact",
            "Forwarded",
            new("Missing.Supplier", new Version(1, 0, 0, 0), null, null),
            definedTypeName: "Healthy");
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            identity, null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("forwarded failure scope test"));

        InspectionEnvelope<ExactTypeInspectionResult> result =
            await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance,
                query,
                ApiSurfaceScope.Public,
                (envelope, target, _, _) =>
                {
                    Assert.Equal(expectedAvailable, target is not null);
                    return ValueTask.FromResult(envelope);
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(expectedAvailable, result.Content.IsAvailable);
        Assert.Equal(expected, result.Content.Outcome);
    }

    [Theory]
    [InlineData("Exact.Type.")]
    [InlineData("Exact.Missing")]
    public async Task ResolvedAssembly_MalformedForwarderPreventsConclusiveAbsence(
        string query)
    {
        byte[] image = BuildMalformedForwarderAssembly();
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new("MalformedForwarder", new Version(1, 0, 0, 0), null, null),
            null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("malformed forwarder selection test"));

        InspectionEnvelope<ExactTypeInspectionResult> result =
            await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance,
                query,
                ApiSurfaceScope.Public,
                (envelope, target, _, _) =>
                {
                    Assert.Null(target);
                    return ValueTask.FromResult(envelope);
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(ExactTypeInspectionOutcome.Unavailable, result.Content.Outcome);
        Assert.Contains(
            result.Content.InspectionFailures,
            failure => failure.Operation
                == ApiSurfaceInspectionFailure.TypeForwarderIdentityOperation);
        Assert.Contains(
            result.Content.Failures,
            failure => failure.Kind == ExactTypeInspectionFailureKind.InspectionIncomplete);
    }
}
