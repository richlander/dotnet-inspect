using System.Text.Json;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    [Fact]
    public async Task ResolvedDefinition_DoesNotOpenUnrelatedForwarderSupplier()
    {
        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Exact", ["Healthy"])).Name;
        var complete = await ReadAsync(structured: false);
        var selected = await ReadAsync(structured: true);

        Assert.Equal(complete.Json, selected.Json);
        Assert.True(complete.SupplierReads > 0);
        Assert.Equal(0, selected.SupplierReads);
        Assert.Equal(2, complete.Types);
        Assert.Equal(1, selected.Types);

        async Task<(string Json, int Types, int SupplierReads)> ReadAsync(
            bool structured)
        {
            int supplierReads = 0;
            var supplierIdentity = new AssemblyReferenceIdentity(
                "Unrelated.Supplier", new Version(1, 0, 0, 0), null, null);
            byte[] supplierImage = BuildMetadataAssembly(
                supplierIdentity.Name, Guid.NewGuid(), true, "Exact", "Forwarded");
            ResolvedAssemblyReference supplier = ResolvedAssemblyReference.Create(
                supplierIdentity, null,
                () =>
                {
                    supplierReads++;
                    return new MemoryStream(supplierImage, writable: false);
                },
                AssemblyResolutionProvenance.Local("unrelated supplier test"));
            byte[] rootImage = BuildMetadataAssembly(
                "Resolved.Root", Guid.NewGuid(), true, "Exact", "Forwarded",
                supplierIdentity, definedTypeName: "Healthy");
            ResolvedAssemblyReference root = ResolvedAssemblyReference.Create(
                new("Resolved.Root", new Version(1, 0, 0, 0), null, null), null,
                () => new MemoryStream(rootImage, writable: false),
                AssemblyResolutionProvenance.Local("exact root test"));
            IAcquisitionFreeAssemblyBindingPolicy policy =
                SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                    new[] { root, supplier }.Select(
                        assembly => (
                            assembly,
                            (IAcquisitionFreeAssemblyBindingPolicy)
                                NoResolverAssemblyBindingPolicy.Instance)));

            ValueTask<(string Json, int Types, int SupplierReads)> Read(
                InspectionEnvelope<ExactTypeInspectionResult> envelope,
                SelectedContextExactTypeLiveTarget? target,
                ApiSurface? catalog,
                CancellationToken cancellationToken)
            {
                Assert.True(envelope.Content.IsAvailable);
                Assert.NotNull(target);
                return ValueTask.FromResult((
                    JsonSerializer.Serialize(envelope.Content.Type),
                    target.Surface.Types.Count,
                    supplierReads));
            }

            return structured
                ? await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                    root, policy, type, ApiSurfaceScope.Public, Read,
                    TestContext.Current.CancellationToken)
                : await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                    root, policy, "Exact.Healthy", ApiSurfaceScope.Public, Read,
                    TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(ApiSurfaceScope.Public)]
    [InlineData(ApiSurfaceScope.IncludeAll)]
    public async Task ResolvedDefinition_NarrowsProjectionAndPreservesExtensions(
        ApiSurfaceScope scope)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.CreateFromPath(
            typeof(ResolvedTypeProjectionFixture).Assembly.Location,
            AssemblyResolutionProvenance.Local("resolved projection test"));
        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                typeof(ResolvedTypeProjectionFixture).Namespace!,
                [nameof(ResolvedTypeProjectionFixture)])).Name;
        var complete = await ReadAsync(structured: false);
        var selected = await ReadAsync(structured: true);

        Assert.Equal(complete.Json, selected.Json);
        Assert.Contains("extension-method", selected.Json);
        Assert.Contains(nameof(ResolvedTypeProjectionExtensions.Extended), selected.Json);
        Assert.True(selected.Types < complete.Types);

        async Task<(string Json, int Types)> ReadAsync(bool structured)
        {
            ValueTask<(string Json, int Types)> Read(
                InspectionEnvelope<ExactTypeInspectionResult> envelope,
                SelectedContextExactTypeLiveTarget? target,
                ApiSurface? catalog,
                CancellationToken cancellationToken)
            {
                Assert.True(envelope.Content.IsAvailable);
                Assert.NotNull(target);
                Assert.Null(catalog);
                return ValueTask.FromResult((
                    JsonSerializer.Serialize(envelope.Content.Type),
                    target.Surface.Types.Count));
            }

            return structured
                ? await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                    assembly, NoResolverAssemblyBindingPolicy.Instance,
                    type, scope, Read, TestContext.Current.CancellationToken)
                : await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                    assembly, NoResolverAssemblyBindingPolicy.Instance,
                    typeof(ResolvedTypeProjectionFixture).FullName!,
                    scope, Read, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("DotnetInspector.Sections", ExactTypeInspectionOutcome.Available)]
    [InlineData("dotnetinspector.sections", ExactTypeInspectionOutcome.NotFound)]
    public async Task ResolvedDefinitionIsNotReinterpretedAsATextQuery(
        string selectedNamespace, ExactTypeInspectionOutcome expected)
    {
        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                selectedNamespace, ["ExactTypeInspectionOperation"])).Name;
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.CreateFromPath(
            typeof(ExactTypeInspectionOperation).Assembly.Location,
            AssemblyResolutionProvenance.Local("resolved definition test"));
        var result = await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
            assembly, NoResolverAssemblyBindingPolicy.Instance,
            type, ApiSurfaceScope.Public,
            (envelope, target, _, _) =>
            {
                if (expected == ExactTypeInspectionOutcome.Available)
                    Assert.Equal(type, Assert.IsType<MetadataTypeDefinitionName>(target?.Type.DefinitionName));
                else
                    Assert.Null(target);
                return ValueTask.FromResult(envelope.Content);
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolvedAssembly_FollowsAuthorizedForwarderAndPreservesOccurrence(
        bool structured)
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

        ValueTask<InspectionEnvelope<ExactTypeInspectionResult>> Read(
            InspectionEnvelope<ExactTypeInspectionResult> envelope,
            SelectedContextExactTypeLiveTarget? target,
            ApiSurface? catalog,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(target);
            Assert.Same(policy, target.BindingPolicy);
            Assert.Equal(supplier.Identity, target.Assembly.Identity);
            Assert.Equal(
                supplier.Identity,
                target.Occurrence.Assembly.Identity);
            return ValueTask.FromResult(envelope);
        }

        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("Exact", ["Widget"])).Name;
        InspectionEnvelope<ExactTypeInspectionResult> result = structured
            ? await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                facade, policy, type, ApiSurfaceScope.Public, Read,
                TestContext.Current.CancellationToken)
            : await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                facade, policy, "Exact.Widget", ApiSurfaceScope.Public, Read,
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
    [InlineData("Exact.Healthy", ExactTypeInspectionOutcome.Available, false)]
    [InlineData("Exact.Healthy", ExactTypeInspectionOutcome.Available, true)]
    [InlineData("Exact.Forwarded", ExactTypeInspectionOutcome.Unavailable, false)]
    [InlineData("Exact.Forwarded", ExactTypeInspectionOutcome.Unavailable, true)]
    [InlineData("Exact.Missing", ExactTypeInspectionOutcome.NotFound, false)]
    [InlineData("Exact.Missing", ExactTypeInspectionOutcome.NotFound, true)]
    public async Task ResolvedAssembly_ScopesUnresolvedForwarderToItsSubject(
        string query, ExactTypeInspectionOutcome expected, bool structured)
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

        ValueTask<InspectionEnvelope<ExactTypeInspectionResult>> Read(
            InspectionEnvelope<ExactTypeInspectionResult> envelope,
            SelectedContextExactTypeLiveTarget? target,
            ApiSurface? catalog,
            CancellationToken cancellationToken)
        {
            Assert.Equal(expectedAvailable, target is not null);
            return ValueTask.FromResult(envelope);
        }

        var type = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "Exact", [query["Exact.".Length..]])).Name;
        InspectionEnvelope<ExactTypeInspectionResult> result = structured
            ? await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly, NoResolverAssemblyBindingPolicy.Instance,
                type, ApiSurfaceScope.Public, Read,
                TestContext.Current.CancellationToken)
            : await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly, NoResolverAssemblyBindingPolicy.Instance,
                query, ApiSurfaceScope.Public, Read,
                TestContext.Current.CancellationToken);

        Assert.Equal(expectedAvailable, result.Content.IsAvailable);
        Assert.Equal(expected, result.Content.Outcome);
    }

    [Theory]
    [InlineData("Exact.Type.", false)]
    [InlineData("Exact.Missing", false)]
    [InlineData("Exact.Missing", true)]
    public async Task ResolvedAssembly_MalformedForwarderPreventsConclusiveAbsence(
        string query, bool structured)
    {
        byte[] image = BuildMalformedForwarderAssembly();
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new("MalformedForwarder", new Version(1, 0, 0, 0), null, null),
            null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("malformed forwarder selection test"));

        ValueTask<InspectionEnvelope<ExactTypeInspectionResult>> Read(
            InspectionEnvelope<ExactTypeInspectionResult> envelope,
            SelectedContextExactTypeLiveTarget? target,
            ApiSurface? catalog,
            CancellationToken cancellationToken)
        {
            Assert.Null(target);
            return ValueTask.FromResult(envelope);
        }

        InspectionEnvelope<ExactTypeInspectionResult> result = structured
            ? await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly, NoResolverAssemblyBindingPolicy.Instance,
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create("Exact", ["Missing"])).Name,
                ApiSurfaceScope.Public, Read,
                TestContext.Current.CancellationToken)
            : await ExactTypeInspectionOperation.ExecuteWithLiveTargetAsync(
                assembly, NoResolverAssemblyBindingPolicy.Instance,
                query, ApiSurfaceScope.Public, Read,
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

public sealed class ResolvedTypeProjectionFixture
{
    public int Call(int value) => value;
    private int Hidden() => 1;
}

public static class ResolvedTypeProjectionExtensions
{
    public static int Extended(this ResolvedTypeProjectionFixture receiver) =>
        receiver.Call(7);
}
