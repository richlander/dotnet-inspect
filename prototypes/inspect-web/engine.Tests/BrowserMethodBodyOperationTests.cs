using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using InspectWeb.Engine.SourceFacade;
using InspectWeb.MethodBodyFixtures;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

[Collection("Type source operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserMethodBodyOperationTests
{
    const string AssemblyName = "InspectWeb.MethodBodyFixtures.dll";
    const string Framework = "net11.0";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportPreservesDifferentAndSamePhysicalPair(bool same)
    {
        using Fixture fixture = await Fixture.Open();
        BrowserMethodBodyTargets targets = await fixture.Targets();
        BrowserMethodBodySelection after = same ? targets.Before
            : Assert.Single(targets.Methods, method => method.MemberName == nameof(Right.Transform));
        BrowserMethodBodyComparisonResult result = await Compare(Request(targets, after));
        Assert.Equal(BrowserMethodBodyResultKind.Succeeded, result.Kind);
        BrowserMethodBodyComparison value = Assert.IsType<BrowserMethodBodyComparison>(result.Value);
        Assert.Equal("Research", value.Stage);
        Assert.Equal("Completed", value.Outcome);
        Assert.Equal(after, value.Request.After);
        Assert.Equal(targets.Before, value.Request.Before);
        Assert.Equal(2, value.Producers.Length);
        foreach (BrowserMethodBodyProducer producer in value.Producers)
        {
            Assert.Equal(targets.ModuleVersionId, producer.Before.ModuleVersionId);
            Assert.Equal(targets.ModuleVersionId, producer.After.ModuleVersionId);
            Assert.Equal(targets.Before.MetadataToken, producer.Before.MetadataToken);
            Assert.Equal(after.MetadataToken, producer.After.MetadataToken);
            Assert.Equal("Complete", producer.Before.State);
            Assert.Equal("Complete", producer.After.State);
        }
        BrowserCSharpBodyEvidence csharp = Assert.IsType<BrowserCSharpBodyEvidence>(value.Producers[0].CSharp);
        BrowserIlBodyEvidence il = Assert.IsType<BrowserIlBodyEvidence>(value.Producers[1].Il);
        Assert.Equal(same, csharp.IsExact);
        Assert.Equal(same, il.IsExact);
        if (!same)
        {
            Assert.NotEmpty(csharp.Rows);
            Assert.Contains(il.Rows, row => row.Operation.Operand is not null);
        }
    }

    [Fact]
    public async Task BodylessMethodRemainsSelectableAndNativeNotApplicable()
    {
        using Fixture fixture = await Fixture.Open();
        BrowserMethodBodyTargets targets = await fixture.Targets();
        BrowserMethodBodySelection bodyless = Assert.Single(targets.Methods,
            method => method.MemberName == nameof(IBodyless.WithoutBody));
        var result = await Compare(Request(targets, bodyless));
        Assert.Equal(BrowserMethodBodyResultKind.Succeeded, result.Kind);
        Assert.Equal("Completed", result.Value!.Outcome);
        Assert.All(result.Value.Producers, producer =>
        {
            Assert.Equal("NoApplicableInput", producer.After.State);
            Assert.NotEmpty(producer.After.Detail!);
            Assert.Null(producer.CSharp);
            Assert.Null(producer.Il);
            Assert.Equal("NotApplicable", producer.NativeVerdict);
        });
    }

    [Fact]
    public async Task ReferenceTokenDriftAndExplicitAccessorsUseImplementationAddress()
    {
        using Fixture fixture = await Fixture.Open(reference: true);
        BrowserMethodBodyTargets targets = await fixture.Targets();
        int implementation = typeof(Left).GetMethod(nameof(Left.Compute), [typeof(int)])!.MetadataToken;
        Assert.NotEqual(fixture.Launch.MetadataToken, implementation);
        Assert.Equal(implementation, targets.Before.MetadataToken);
        Assert.Equal(typeof(Left).Module.ModuleVersionId.ToString("D"), targets.ModuleVersionId);
        BrowserMethodBodySelection getter = Assert.Single(targets.Methods, method => method.MemberName == "get_Value");
        BrowserMethodBodySelection setter = Assert.Single(targets.Methods, method => method.MemberName == "set_Value");
        var result = await Compare(Request(targets, setter) with { Before = getter });
        Assert.Equal(BrowserMethodBodyResultKind.Succeeded, result.Kind);
        Assert.All(result.Value!.Producers, producer =>
        {
            Assert.Equal(getter.MetadataToken, producer.Before.MetadataToken);
            Assert.Equal(setter.MetadataToken, producer.After.MetadataToken);
        });
    }

    [Fact]
    public async Task WrongImageAndMissingContextFailWithoutReacquisition()
    {
        using Fixture fixture = await Fixture.Open();
        BrowserMethodBodyTargets targets = await fixture.Targets();
        var wrong = await Compare(Request(targets, targets.Before) with { ModuleVersionId = Guid.NewGuid().ToString() });
        Assert.Equal(BrowserMethodBodyResultKind.Failed, wrong.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, wrong.FailureKind);
        Assert.Contains("WrongImage", wrong.Error);
        Assert.Null(wrong.Value);

        BrowserPackageWorkspace.RemoveScope(fixture.Scope);
        var missing = await Compare(Request(targets, targets.Before));
        Assert.Equal(BrowserMethodBodyResultKind.Failed, missing.Kind);
        Assert.Contains("ContextUnavailable", missing.Error);
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(fixture.Scope));
        BrowserMethodBodyTargetsResult missingTargets = await fixture.TargetsResult();
        Assert.Equal(BrowserMethodBodyResultKind.Failed, missingTargets.Kind);
        Assert.Contains("ContextUnavailable", missingTargets.Error);
    }

    [Fact]
    public async Task InvalidSelectionDoesNotSubstituteAnotherOverloadOrAccessor()
    {
        using Fixture fixture = await Fixture.Open();
        BrowserMethodBodyTargets targets = await fixture.Targets();
        BrowserMethodBodySelection other = Assert.Single(targets.Methods,
            method => method.MemberName == nameof(Right.Transform));
        var bad = await Compare(Request(targets, other with { MetadataToken = targets.Before.MetadataToken }));
        Assert.Equal(BrowserMethodBodyResultKind.Failed, bad.Kind);
        Assert.Contains("SelectionUnavailable", bad.Error);
        var labels = await Compare(Request(targets, targets.Before with { Label = "Untrusted display label" }));
        Assert.Equal(targets.Before.Label, labels.Value!.Request.After.Label);
    }

    [Fact]
    public async Task DisposedRetainedContextDoesNotBecomeAnEmptyInventory()
    {
        using Fixture fixture = await Fixture.Open();
        fixture.Scope.Dispose();
        BrowserMethodBodyTargetsResult result = await fixture.TargetsResult();
        Assert.Equal(BrowserMethodBodyResultKind.Failed, result.Kind);
        Assert.NotEmpty(result.Error!);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task NativeBodyFailureSurvivesSuccessfulResearchAccounting()
    {
        using Fixture fixture = await Fixture.Open(brokenBody: true);
        BrowserMethodBodyTargets targets = await fixture.Targets();
        var result = await Compare(Request(targets,
            Assert.Single(targets.Methods, method => method.MemberName == nameof(Right.Transform))));
        Assert.Equal(BrowserMethodBodyResultKind.Succeeded, result.Kind);
        Assert.Equal("Research", result.Value!.Stage);
        Assert.Equal("Completed", result.Value.Outcome);
        Assert.All(result.Value.Producers, producer =>
        {
            Assert.Equal("Failed", producer.Before.State);
            Assert.Equal("Complete", producer.After.State);
            Assert.Null(producer.CSharp);
            Assert.Null(producer.Il);
            Assert.NotEmpty(producer.Diagnostics);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalQueryWrongImageAndCancellationRemainQueryOutcomes(bool canceled)
    {
        using Fixture fixture = await Fixture.Open();
        BrowserMethodBodyTargets targets = await fixture.Targets();
        using var cancellation = new CancellationTokenSource();
        if (canceled)
            cancellation.Cancel();
        LocalComparisonQueryResult original = fixture.Scope.UseImplementation(group =>
        {
            AssemblyContextParticipant participant = Assert.Single(group.Participants);
            MetadataMethodAddress address = Assert.IsType<AssemblyContextEntry<MetadataMethodAddress>.Available>(
                AssemblyContextMethodAddressQuery.ExecuteParticipant(group, participant, targets.Before.MetadataToken)).Value;
            return DirectMemberComparisonQuery.Execute(group,
                new(new(participant, canceled ? address : address with { ModuleVersionId = Guid.NewGuid() }),
                    new(participant, address), ResearchProducerCatalog.Kinds), cancellation.Token);
        });
        BrowserMethodBodyComparison projected = BrowserMethodBodyProjection.Project(
            Request(targets, targets.Before), original);
        Assert.Equal("Query", projected.Stage);
        Assert.Equal(canceled ? "Cancelled" : "DesignationUnavailable", projected.Outcome);
        Assert.Empty(projected.Producers);
        Assert.Contains(projected.Diagnostics,
            diagnostic => diagnostic.Kind == (canceled ? "Cancelled" : "AddressEvidenceMismatch"));
    }

    [Theory]
    [InlineData(false, "user")]
    [InlineData(true, "disposed")]
    public async Task BothExportsPreserveSourceAcquisitionAndReleaseOwnOperation(bool compare, string reason)
    {
        using Fixture fixture = await Fixture.Open();
        BrowserMethodBodyTargets targets = await fixture.Targets();
        using BrowserSourceOperationLease holder = await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();
        Task<string> pending = compare
            ? SourceExports.QueryMethodBodyComparison(id,
                JsonSerializer.Serialize(Request(targets, targets.Before),
                    BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonRequest))
            : SourceExports.QueryMethodBodyComparisonTargets(
                id, targets.PackageId, targets.Version, targets.Framework, targets.Assembly,
                targets.Before.TypeIdentity, targets.Before.MemberName,
                targets.Before.SelectorKey, targets.Before.MetadataToken);
        using JsonDocument result = JsonDocument.Parse(await pending);
        Assert.Equal("Succeeded", result.RootElement.GetProperty("kind").GetString());
        Assert.False(holder.CancellationToken.IsCancellationRequested);
        BrowserTypeSourceCancellation cancel = JsonSerializer.Deserialize(
            SourceExports.CancelMethodBodyComparison(id, reason),
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!;
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, cancel.Kind);
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, JsonSerializer.Deserialize(
            SourceExports.CancelTypeSourceQuery(id, "user"),
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!.Kind);
        Task<BrowserSourceOperationLease> successor = BrowserSourceOperationCoordinator.BeginAsync().AsTask();
        Assert.False(successor.IsCompleted);
        holder.Dispose();
        using BrowserSourceOperationLease next = await successor;
    }

    [Fact]
    public async Task MalformedRequestIsVisibleExpectedFailureAndReleasesOperation()
    {
        string id = Guid.NewGuid().ToString();
        var result = JsonSerializer.Deserialize(await SourceExports.QueryMethodBodyComparison(id, "{"),
            BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonResult)!;
        Assert.Equal(BrowserMethodBodyResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Contains("JsonException", result.Diagnostic);
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, JsonSerializer.Deserialize(
            SourceExports.CancelMethodBodyComparison(id, "user"),
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!.Kind);
    }

    [Fact]
    public async Task RetainedPlatformSelectionComparesWithoutAcquisitionAndRejectsMissingContext()
    {
        const string framework = "net11.0-method-body-platform";
        const string version = "11.0.963";
        using var archiveBytes = new MemoryStream();
        using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Create, leaveOpen: true))
        using (Stream entry = archive.CreateEntry($"runtimes/linux-x64/lib/net11.0/{AssemblyName}").Open())
            entry.Write(File.ReadAllBytes(FixtureCatalog.InspectWebMethodBodies.AssemblyPath()));
        using var handler = new PlatformHandler(version, archiveBytes.ToArray());
        using var client = new HttpClient(handler);
        using BrowserPlatformScopeResolution resolution = await BrowserPlatformWorkspace.OpenAssemblyAsync(
            framework, AssemblyName, "netcore.app", client,
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        ApiSurface surface = resolution.Scope.UseParticipant(resolution.Participant,
            BrowserMemberResolution.ImplementationSurface);
        ApiType type = Assert.Single(surface.Types, type => type.FullName == typeof(Right).FullName);
        ApiMember member = Assert.Single(type.Members, member => member.Name == nameof(Right.Transform));
        CallGraphMemberBodySelector body = Assert.Single(CallGraphMemberResolver.CreateBodySelectors(type, member));
        int acquisitionRequests = handler.Requests;
        BrowserMethodBodyTargetsResult targets = JsonSerializer.Deserialize(
            await SourceExports.QueryMethodBodyComparisonTargets(Guid.NewGuid().ToString(),
                "", version, resolution.Scope.Framework, AssemblyName, type.DefinitionName!.ToEscapedFullName(),
                body.MemberName, body.SelectorKey, body.BodyToken),
            BrowserSourceJsonContext.Default.BrowserMethodBodyTargetsResult)!;
        Assert.True(targets.Kind == BrowserMethodBodyResultKind.Succeeded, targets.Diagnostic);
        var result = await Compare(Request(targets.Value!, targets.Value!.Before));
        Assert.Equal(BrowserMethodBodyResultKind.Succeeded, result.Kind);
        Assert.Equal("Completed", result.Value!.Outcome);
        Assert.Equal(acquisitionRequests, handler.Requests);
        resolution.Dispose();
        BrowserPackageWorkspace.RemoveScope(resolution.Scope);
        var missing = await Compare(Request(targets.Value!, targets.Value!.Before));
        Assert.Equal(BrowserMethodBodyResultKind.Failed, missing.Kind);
        Assert.Contains("ContextUnavailable", missing.Error);
        Assert.Equal(acquisitionRequests, handler.Requests);
    }

    static BrowserMethodBodyComparisonRequest Request(BrowserMethodBodyTargets targets, BrowserMethodBodySelection after) =>
        new(targets.PackageId, targets.Version, targets.Framework, targets.Assembly,
            targets.ModuleVersionId, targets.Before, after);

    static async Task<BrowserMethodBodyComparisonResult> Compare(BrowserMethodBodyComparisonRequest request) =>
        JsonSerializer.Deserialize(await SourceExports.QueryMethodBodyComparison(
            Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(request, BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonRequest)),
            BrowserSourceJsonContext.Default.BrowserMethodBodyComparisonResult)!;

    sealed class PlatformHandler(string version, byte[] archive) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            string url = request.RequestUri!.AbsoluteUri;
            const string package = "microsoft.netcore.app.runtime.linux-x64";
            HttpContent? content = url switch
            {
                $"https://api.nuget.org/v3-flatcontainer/{package}/index.json" =>
                    new StringContent($$"""{"versions":["{{version}}"]}"""),
                $"https://api.nuget.org/v3/registration5-gz-semver2/{package}/index.json" =>
                    new StringContent($$$"""{"items":[{"items":[{"catalogEntry":{"version":"{{{version}}}","listed":true}}]}]}"""),
                _ when url == $"https://api.nuget.org/v3-flatcontainer/{package}/{version}/{package}.{version}.nupkg" =>
                    new ByteArrayContent(archive),
                _ => null,
            };
            return Task.FromResult(new HttpResponseMessage(content is null
                ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    sealed class Fixture(string packageId, BrowserInspectionScope scope, BrowserMethodBodySelection launch) : IDisposable
    {
        internal BrowserInspectionScope Scope => scope;
        internal BrowserMethodBodySelection Launch => launch;

        internal static async Task<Fixture> Open(bool reference = false, bool brokenBody = false)
        {
            byte[] implementation = File.ReadAllBytes(FixtureCatalog.InspectWebMethodBodies.AssemblyPath());
            if (brokenBody)
            {
                using var pe = new PEReader(new MemoryStream(implementation, writable: false));
                var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                    typeof(Left).GetMethod(nameof(Left.Compute), [typeof(int)])!.MetadataToken);
                int rva = pe.GetMetadataReader().GetMethodDefinition(handle).RelativeVirtualAddress;
                SectionHeader section = pe.PEHeaders.SectionHeaders.Single(section =>
                    rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData);
                implementation[section.PointerToRawData + rva - section.VirtualAddress] = 0;
            }
            string id = "Method.Body." + Guid.NewGuid().ToString("N");
            using var bytes = new MemoryStream();
            using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (Stream entry = archive.CreateEntry($"lib/{Framework}/{AssemblyName}").Open())
                    entry.Write(implementation);
                if (reference)
                {
                    using Stream entry = archive.CreateEntry($"ref/{Framework}/{AssemblyName}").Open();
                    entry.Write(File.ReadAllBytes(FixtureCatalog.InspectWebMethodBodies.AssetPath("reference")));
                }
            }
            BrowserPackageWorkspace.RegisterAcquiredPackage(
                new BrowserPackage(id, "1.0.0",
                    reference && !brokenBody
                        ? File.ReadAllBytes(FixtureCatalog.InspectWebMethodBodies.AssetPath("package"))
                        : bytes.ToArray(),
                    fromCache: false));
            BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(id, "1.0.0", Framework);
            ApiSurface surface = scope.UseSurface(group => Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                AssemblyContextApiSurfaceQuery.ExecuteBounded(group, ApiSurfaceScope.IncludeAll,
                    BrowserApiSurfacePolicy.Limits).Assemblies.Assemblies.Single()).Value.Surface);
            ApiType type = Assert.Single(surface.Types, type => type.FullName == typeof(Left).FullName);
            ApiMember method = Assert.Single(type.Members,
                member => member.Name == nameof(Left.Compute) && member.SignatureModel?.Parameters.Count == 1);
            CallGraphMemberBodySelector body = Assert.Single(CallGraphMemberResolver.CreateBodySelectors(type, method));
            return new(id, scope, new(type.DefinitionName!.ToEscapedFullName(),
                body.MemberName, body.SelectorKey, body.BodyToken, "Launch"));
        }

        internal async Task<BrowserMethodBodyTargetsResult> TargetsResult() =>
            JsonSerializer.Deserialize(await SourceExports.QueryMethodBodyComparisonTargets(
                Guid.NewGuid().ToString(), packageId, "1.0.0", Framework, AssemblyName,
                launch.TypeIdentity, launch.MemberName, launch.SelectorKey, launch.MetadataToken),
                BrowserSourceJsonContext.Default.BrowserMethodBodyTargetsResult)!;

        internal async Task<BrowserMethodBodyTargets> Targets()
        {
            BrowserMethodBodyTargetsResult result = await TargetsResult();
            Assert.True(result.Kind == BrowserMethodBodyResultKind.Succeeded, result.Diagnostic);
            return Assert.IsType<BrowserMethodBodyTargets>(result.Value);
        }

        public void Dispose() => BrowserPackageWorkspace.RemoveScope(scope);
    }
}
