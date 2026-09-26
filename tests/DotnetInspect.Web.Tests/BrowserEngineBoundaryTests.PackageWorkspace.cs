using System.IO.Compression;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using InertText;
using Inspector.Findings;
using ILInspector.Metadata;
using NuGetFetch;

using DotnetInspect.Web.Interop.Package;
using BrowserMetadataJsonContext = DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonContext;
using BrowserAnalysisJsonContext = DotnetInspect.Web.Interop.Analysis.BrowserAnalysisJsonContext;
using BrowserSourceJsonContext = DotnetInspect.Web.Interop.Source.BrowserSourceJsonContext;
using BrowserCallGraphJsonContext = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphJsonContext;
using BrowserCatalogJsonContext = DotnetInspect.Web.Interop.Catalog.BrowserCatalogJsonContext;
using BrowserPackageMetadata = DotnetInspect.Web.Interop.Metadata.BrowserPackageMetadata;
using BrowserMetadataCompileLibraryStatus = DotnetInspect.Web.Interop.Metadata.BrowserCompileLibraryStatus;
using BrowserAnalysisCompileLibraryStatus = DotnetInspect.Web.Interop.Analysis.BrowserCompileLibraryStatus;
using BrowserPackageIntegrations = DotnetInspect.Web.Interop.Analysis.BrowserPackageIntegrations;
using BrowserPackageOpportunities = DotnetInspect.Web.Interop.Analysis.BrowserPackageOpportunities;
using BrowserPackagePerformance = DotnetInspect.Web.Interop.Analysis.BrowserPackagePerformance;
using BrowserPerformanceMember = DotnetInspect.Web.Interop.Analysis.BrowserPerformanceMember;
using BrowserOpportunityItem = DotnetInspect.Web.Interop.Analysis.BrowserOpportunityItem;
using BrowserSource = DotnetInspect.Web.Interop.Source.BrowserSource;
using BrowserCallGraph = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraph;
using BrowserCallGraphTarget = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphTarget;
using BrowserCallGraphWireProjection = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphWireProjection;
using BrowserCallGraphDiagnostics = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphDiagnostics;
using BrowserHomeDemoRunResult = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunResult;
using BrowserHomeDemoRunActivation = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunActivation;
using BrowserHomeDemoRunPlan = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunPlan;
using BrowserProductHomeDemos = DotnetInspect.Web.Interop.Catalog.BrowserProductHomeDemos;
using BrowserHomeDemoRunMember = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunMember;
using BrowserHomeDemoRunRequest = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunRequest;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{

    [Fact]
    public void SourceFetchPolicy_OmitsCredentialsAndFollowsRedirects()
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "https://raw.githubusercontent.com/org/repo/commit/A.cs");

        BrowserSourceFetchPolicy.Instance.ConfigureRequest(request);

        Assert.True(request.Options.TryGetValue(
            new HttpRequestOptionsKey<IDictionary<string, object>>(
                "WebAssemblyFetchOptions"),
            out IDictionary<string, object>? options));
        Assert.Equal("omit", options["credentials"]);
        Assert.Equal("follow", options["redirect"]);
    }

    [Fact]
    public async Task TypeSourceParticipant_RefusesReferenceOnlyAssembly()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Reference.Source",
            Package(
                image,
                "ref/net11.0/DotnetInspect.Web.Tests.dll"));

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => coordinate.ImplementationAsset(
                    Assert.IsType<PackageCompileAsset>(
                        coordinate.DefaultAsset).AssemblyName));

        Assert.Contains("reference assembly only", error.Message);
    }

    [Fact]
    public async Task RidSpecificPackage_SeparatesCompileAndImplementationAssets()
    {
        const string packageId = "Rid.Specific";
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] unrelatedImage =
            File.ReadAllBytes(
                typeof(PackageAssemblyContextRealization).Assembly.Location);
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            PackageEntries(
                ("lib/net11.0/Rid.Specific.dll", image),
                ("lib/net11.0/shadow/Rid.Specific.dll", unrelatedImage),
                ("runtimes/linux-x64/lib/net11.0/Rid.Specific.dll", image)),
            fromCache: false);
        var coordinate = new BrowserPackageCoordinate(
            package,
            new PackageRootRealization(
                package.Content,
                packageId,
                package.Version,
                "net11.0",
                "linux-x64"));

        PackageCompileAsset compile =
            coordinate.CompileAsset("Rid.Specific.dll");
        Assert.Equal("lib/net11.0/Rid.Specific.dll", compile.Path);
        Assert.Equal(
            "runtimes/linux-x64/lib/net11.0/Rid.Specific.dll",
            coordinate.ImplementationAsset("Rid.Specific.dll").Path);
        await using BrowserInspectionScope scope = await BrowserInspectionScope.CreateAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserWorkspaceParticipant surface =
            Assert.Single(scope.SurfaceParticipants);
        BrowserWorkspaceParticipant implementation =
            scope.ImplementationParticipants.Single(candidate =>
                candidate.Asset.Path
                    == "runtimes/linux-x64/lib/net11.0/Rid.Specific.dll");
        Assert.Equal(compile.Path, surface.Asset.Path);
        Assert.Equal(
            "runtimes/linux-x64/lib/net11.0/Rid.Specific.dll",
            implementation.Asset.Path);
        Assert.Contains(
            scope.ImplementationParticipants,
            candidate =>
                candidate.Asset.Path
                    == "lib/net11.0/shadow/Rid.Specific.dll");
        Assert.Same(
            implementation,
            scope.ImplementationParticipant(surface));
    }

    [Fact]
    public async Task PackageFrameworkUnavailability_DoesNotEmitArtifactFramework()
    {
        const char bidi = '\u202E';
        const string packageId = "Bidi.Framework.Failure";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                PackageEntries(
                    ($"{packageId}.nuspec", Encoding.UTF8.GetBytes(
                        $"""
                         <?xml version="1.0" encoding="utf-8"?>
                         <package>
                           <metadata>
                             <id>{packageId}</id>
                             <version>1.0.0</version>
                             <dependencies>
                               <group targetFramework="net8.0{bidi}" />
                             </dependencies>
                           </metadata>
                         </package>
                         """)),
                    ($"lib/net8.0{bidi}/{packageId}.dll", [0x01])),
                fromCache: false));

        BrowserPackageSurface surface = await QueryPackageSurface(
            packageId,
            "1.0.0",
            "net11.0");

        Assert.DoesNotContain(
            surface.Frameworks,
            framework => framework.Contains(bidi, StringComparison.Ordinal));
        Assert.Equal(
            BrowserCompileLibraryStatus.NoMatchingTargetFramework,
            surface.CompileLibrary.Status);
        Assert.Empty(surface.Frameworks);
        Assert.DoesNotContain(bidi, surface.ActiveFramework);
        Assert.DoesNotContain(
            bidi,
            surface.CompileLibrary.TargetFramework ?? "");
        InvalidOperationException dependencyFailure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageDependencies(
                    packageId,
                    "1.0.0",
                    "net11.0",
                    assemblyId: ""));
        Assert.Contains(
            "cannot be represented safely",
            dependencyFailure.Message);
        Assert.DoesNotContain(bidi, dependencyFailure.Message);

        var selectedPackage = new BrowserPackage(
            "Bidi.Selected.Framework",
            "1.0.0",
            Package(
                [0x01],
                $"lib/net8.0{bidi}/Selected.dll"),
            fromCache: false);
        var selectedContext = new PackageRootRealization(
            selectedPackage.Content,
            selectedPackage.PackageId,
            selectedPackage.Version);
        var coordinate =
            new BrowserPackageCoordinate(selectedPackage, selectedContext);

        InvalidOperationException compileFailure =
            Assert.Throws<InvalidOperationException>(
                () => coordinate.CompileAsset("Missing.dll"));

        Assert.DoesNotContain(bidi, compileFailure.Message);
    }

    [Fact]
    public void PackageCoordinate_RejectsDifferentContentWithSameIdentity()
    {
        const string packageId = "Exact.Content";
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        var package = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(image, $"lib/net11.0/{packageId}.dll"),
            fromCache: false);
        var differentContent = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(
                image,
                $"lib/net11.0/{packageId}.dll",
                paddingBytes: 1),
            fromCache: false);
        var root = new PackageRootRealization(
            differentContent.Content,
            packageId,
            "1.0.0",
            "net11.0");

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new BrowserPackageCoordinate(package, root));

        Assert.Contains("exact content", error.Message);
    }

    [Fact]
    public async Task PackageScope_DoesNotCollapseDifferentContentAtSameCoordinate()
    {
        string packageId = $"Exact.Scope.{Guid.NewGuid():N}";
        byte[] firstImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealization).Assembly.Location);
        var firstPackage = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(firstImage, $"lib/net11.0/{packageId}.dll"),
            fromCache: false);
        var secondPackage = new BrowserPackage(
            packageId,
            "1.0.0",
            Package(secondImage, $"lib/net11.0/{packageId}.dll"),
            fromCache: false);
        var firstCoordinate = new BrowserPackageCoordinate(
            firstPackage,
            new PackageRootRealization(
                firstPackage.Content,
                packageId,
                "1.0.0",
                "net11.0"));
        var secondCoordinate = new BrowserPackageCoordinate(
            secondPackage,
            new PackageRootRealization(
                secondPackage.Content,
                packageId,
                "1.0.0",
                "net11.0"));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await BrowserInspectionScope.CreateAsync(
                [firstCoordinate, secondCoordinate],
                TestContext.Current.CancellationToken));
        await using BrowserInspectionScope directScope =
            await BrowserInspectionScope.CreateAsync([firstCoordinate], TestContext.Current.CancellationToken);
        Assert.False(
            directScope.ContainsExactCoordinates([secondCoordinate]));
        Assert.Throws<InvalidOperationException>(
            () => directScope.Coordinate(secondCoordinate));

        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(firstPackage);
        await using BrowserScopeLease<BrowserInspectionScope> retainedLease =
            await BrowserPackageWorkspace.OpenScopeAsync([firstCoordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope retained = retainedLease.Scope;
        try
        {
            await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(secondPackage);
            InvalidOperationException cacheFailure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await BrowserPackageWorkspace.OpenScopeAsync(
                        [secondCoordinate],
                        TestContext.Current.CancellationToken));
            Assert.Contains(
                "exact requested package content",
                cacheFailure.Message);
        }
        finally
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(retained);
        }
    }

    [Fact]
    public async Task PackageScope_ValidatesEveryCoordinateAgainstCacheProvenance()
    {
        string provenanceId = $"Exact.Provenance.{Guid.NewGuid():N}";
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] archive =
            Package(image, $"lib/net11.0/{provenanceId}.dll");
        var registered = new BrowserPackage(
            provenanceId,
            "1.0.0",
            archive,
            fromCache: false,
            producerKey: "producer-a");
        var unregisteredProducer = new BrowserPackage(
            provenanceId,
            "1.0.0",
            archive,
            fromCache: false,
            producerKey: "producer-b");
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(registered);
        var provenanceCoordinate = new BrowserPackageCoordinate(
            unregisteredProducer,
            new PackageRootRealization(
                unregisteredProducer.Content,
                provenanceId,
                "1.0.0",
                "net11.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync(
                [provenanceCoordinate],
                TestContext.Current.CancellationToken));

        string frameworksId = $"Exact.Frameworks.{Guid.NewGuid():N}";
        var cachedPackage = new BrowserPackage(
            frameworksId,
            "1.0.0",
            Package(
                image,
                $"lib/net10.0/{frameworksId}.dll"),
            fromCache: false,
            producerKey: "producer-a");
        var unregisteredFramework = new BrowserPackage(
            frameworksId,
            "1.0.0",
            Package(
                image,
                $"lib/net11.0/{frameworksId}.dll"),
            fromCache: false,
            producerKey: "producer-a");
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(cachedPackage);
        var cachedCoordinate = new BrowserPackageCoordinate(
            cachedPackage,
            new PackageRootRealization(
                cachedPackage.Content,
                frameworksId,
                "1.0.0",
                "net10.0"));
        var unregisteredCoordinate = new BrowserPackageCoordinate(
            unregisteredFramework,
            new PackageRootRealization(
                unregisteredFramework.Content,
                frameworksId,
                "1.0.0",
                "net11.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync(
                [cachedCoordinate, unregisteredCoordinate],
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PackageScope_RequestedFrameworkCannotForgeCompositeRegistryKey()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string firstId = $"Scope.Collision.First.{suffix}";
        string secondId = $"Scope.Collision.Second.{suffix}";
        var firstPackage = new BrowserPackage(
            firstId,
            "1.0.0",
            PackageEntries(($"tools/net11.0/any/{firstId}.dll", [0x01])),
            fromCache: false);
        var secondPackage = new BrowserPackage(
            secondId,
            "1.0.0",
            PackageEntries(($"tools/net11.0/any/{secondId}.dll", [0x02])),
            fromCache: false);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(firstPackage);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(secondPackage);

        BrowserPackageCoordinate crafted = new(
            firstPackage,
            new PackageRootRealization(
                firstPackage.Content,
                firstId,
                "1.0.0",
                $"net8.0|{secondId.ToLowerInvariant()}@1.0.0/net9.0"));
        BrowserPackageCoordinate first = new(
            firstPackage,
            new PackageRootRealization(
                firstPackage.Content,
                firstId,
                "1.0.0",
                "net8.0"));
        BrowserPackageCoordinate second = new(
            secondPackage,
            new PackageRootRealization(
                secondPackage.Content,
                secondId,
                "1.0.0",
                "net9.0"));

        await using BrowserScopeLease<BrowserInspectionScope> craftedScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([crafted], TestContext.Current.CancellationToken);
        BrowserInspectionScope craftedScope = craftedScopeLease.Scope;
        await using BrowserScopeLease<BrowserInspectionScope> legitimateScopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([first, second], TestContext.Current.CancellationToken);
        BrowserInspectionScope legitimateScope = legitimateScopeLease.Scope;
        try
        {
            Assert.NotSame(craftedScope, legitimateScope);
            Assert.Single(craftedScope.Coordinates);
            Assert.Equal(2, legitimateScope.Coordinates.Length);
        }
        finally
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(craftedScope);
            await BrowserPackageWorkspace.RemoveScopeAsync(legitimateScope);
        }
    }

    [Fact]
    public async Task MixedPackageScope_RealizesOnlySelectedCoordinates()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        var selectedPackage = new BrowserPackage(
            "Selected.Library",
            "1.0.0",
            Package(image, "lib/net11.0/Selected.Library.dll"),
            fromCache: false);
        var rootOnlyPackage = new BrowserPackage(
            "Tool.Pointer",
            "1.0.0",
            PackageEntries(
                ("tools/net11.0/any/Tool.Pointer.dll", [0x01])),
            fromCache: false);
        var selectedCoordinate = new BrowserPackageCoordinate(
            selectedPackage,
            new PackageRootRealization(
                selectedPackage.Content,
                selectedPackage.PackageId,
                selectedPackage.Version,
                "net11.0"));
        var rootOnlyCoordinate = new BrowserPackageCoordinate(
            rootOnlyPackage,
            new PackageRootRealization(
                rootOnlyPackage.Content,
                rootOnlyPackage.PackageId,
                rootOnlyPackage.Version,
                "net11.0"));

        await using BrowserInspectionScope scope = await BrowserInspectionScope.CreateAsync(
            [selectedCoordinate, rootOnlyCoordinate],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, scope.Coordinates.Length);
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.SurfaceParticipants);
        Assert.Same(selectedCoordinate, participant.Coordinate);
        Assert.Same(
            selectedCoordinate,
            Assert.Single(scope.ImplementationParticipants).Coordinate);
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => rootOnlyCoordinate.CompileAsset("Tool.Pointer"));
        Assert.Contains(
            nameof(PackageCompileAssetSelectionStatus.NoCompileAssets),
            error.Message);
    }

    [Fact]
    public async Task ReferenceOnlyFailures_DoNotEmitArtifactAssemblyNames()
    {
        const char bidi = '\u202E';
        string assemblyName = $"Bidi.Reference{bidi}.dll";
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Bidi.ReferenceOnly",
            Package(
                image,
                $"ref/net11.0/{assemblyName}"));

        InvalidOperationException coordinateFailure =
            Assert.Throws<InvalidOperationException>(
                () => coordinate.ImplementationAsset(assemblyName));

        Assert.Contains("reference assembly only", coordinateFailure.Message);
        Assert.DoesNotContain(bidi, coordinateFailure.Message);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        InvalidOperationException scopeFailure =
            Assert.Throws<InvalidOperationException>(
                () => scope.ImplementationParticipant(
                    Assert.Single(scope.SurfaceParticipants)));

        Assert.Contains("reference assembly only", scopeFailure.Message);
        Assert.DoesNotContain(bidi, scopeFailure.Message);
    }

    [Fact]
    public void MissingPackageEntryFailure_DoesNotEmitArtifactPath()
    {
        const char bidi = '\u202E';
        var package = new BrowserPackage(
            "Bidi.Missing.Entry",
            "1.0.0",
            Package([0x01], "lib/net11.0/Present.dll"),
            fromCache: false);

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => package.OpenEntry(
                    $"lib/net11.0/Missing{bidi}.dll",
                    1_024));

        Assert.DoesNotContain(bidi, failure.Message);
    }

    [Fact]
    public void SourceFailures_PreserveTypedDetailAndCause()
    {
        var cause = new IOException("symbol service failed");
        var failure = new AssemblySourceFailure(
            AssemblySourceFailureKind.InspectionFailed,
            "Source inspection failed.",
            cause);

        InvalidOperationException adapted =
            DotnetInspect.Web.Interop.Source.SourceExports.SourceUnavailable(failure);

        Assert.Contains(
            nameof(AssemblySourceFailureKind.InspectionFailed),
            adapted.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            failure.Detail,
            adapted.Message,
            StringComparison.Ordinal);
        Assert.Same(cause, adapted.InnerException);

        InvalidOperationException withPdbSourceFailure =
            DotnetInspect.Web.Interop.Source.SourceExports.SourceUnavailable(
                failure,
                "The host does not authorize this SourceLink destination.");
        Assert.Contains(
            "PDB source unavailable",
            withPdbSourceFailure.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "does not authorize",
            withPdbSourceFailure.Message,
            StringComparison.Ordinal);
        Assert.Same(cause, withPdbSourceFailure.InnerException);
    }

    [Fact]
    public async Task DecompiledSources_CarryPdbAttemptLimitation()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Source.Limitation",
            Package(
                image,
                "lib/net11.0/DotnetInspect.Web.Tests.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync([coordinate], TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.ImplementationParticipants);
        AssemblyContextApiSurfaceResult result =
            scope.UseImplementation(
                group => AssemblyContextApiSurfaceQuery.Execute(group));
        var available =
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(result.Assemblies.Assemblies));
        ApiType type = available.Value.Surface.Types.First(
            candidate => candidate.Members.Any(
                member => member.MetadataToken is not null));
        ApiMember member = type.Members.First(
            candidate => candidate.MetadataToken is not null);

        const string MemberLimitation = "member PDB source unavailable";
        var memberAttempt = new PdbMemberSourceInspection(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                MemberLimitation),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);
        var memberEntry = new AssemblyMemberSourceEntry.Available(
            available.Subject,
            AssemblyMemberSourceRequest.From(type, member),
            new AssemblyMemberSource.Decompiled(
                "void M() {}",
                new CSharpDecompilationAttempt(
                    CSharpDecompilationStatus.Available,
                    DecompilerResult.Success("void M() {}"),
                    [], [], false, DecompilerSymbolSource.None, 0),
                memberAttempt));

        BrowserSource memberSource =
            DotnetInspect.Web.Interop.Source.SourceExports.Adapt(memberEntry, participant);
        Assert.Equal(MemberLimitation, memberSource.PdbSourceLimitation);

        const string TypeLimitation = "type PDB source unavailable";
        var typeAttempt = new PdbTypeSourceInspection(
            new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                TypeLimitation),
            Text: null,
            Mapping: null,
            Document: null,
            ChecksumVerification: null);
        var typeEntry = new AssemblyTypeSourceEntry.Available(
            available.Subject,
            AssemblyTypeSourceRequest.From(type),
            new AssemblyTypeSource.Decompiled(
                "class C {}",
                new CSharpDecompilationAttempt(
                    CSharpDecompilationStatus.Available,
                    DecompilerResult.Success("class C {}"),
                    [], [], false, DecompilerSymbolSource.None, 0),
                typeAttempt));

        BrowserSource typeSource =
            DotnetInspect.Web.Interop.Source.SourceExports.Adapt(typeEntry, participant);
        Assert.Equal(TypeLimitation, typeSource.PdbSourceLimitation);
    }

    [Fact]
    public void SourceProvenance_RemainsAScalarJsonString()
    {
        var source = new BrowserSource(
            "pdb",
            new InertString(TextPolicy.Field, "source\u202E"),
            null,
            null,
            "class C {}");

        string json = JsonSerializer.Serialize(
            source,
            BrowserSourceJsonContext.Default.BrowserSource);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement provenance = document.RootElement.GetProperty("provenance");

        Assert.Equal(JsonValueKind.String, provenance.ValueKind);
        Assert.Equal(@"source\u202E", provenance.GetString());
    }

    [Fact]
    public async Task SourceUnavailable_PreservesDecompilerDiagnostic()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await Coordinate(
            "Source.Decompiler.Failure",
            Package(
                image,
                "lib/net11.0/DotnetInspect.Web.Tests.dll"));
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.ImplementationParticipants);
        AssemblyContextApiSurfaceResult result =
            scope.UseImplementation(
                group => AssemblyContextApiSurfaceQuery.Execute(group));
        var available =
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(result.Assemblies.Assemblies));
        ApiType type = Assert.Single(
            available.Value.Surface.Types,
            candidate => candidate.FullName
                == typeof(BrowserEngineBoundaryTests).FullName);
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name
                == nameof(SourceUnavailable_PreservesDecompilerDiagnostic));
        const string DecompilerDetail =
            "DEC0016: module memory-safety rules are Unsupported";
        var memberEntry = new AssemblyMemberSourceEntry.Unavailable(
            available.Subject,
            AssemblyMemberSourceRequest.From(type, member),
            new AssemblySourceFailure(
                AssemblySourceFailureKind.PdbAndDecompiledUnavailable,
                "Neither source form is available."),
            DecompiledAttempt: new CSharpDecompilationAttempt(
                CSharpDecompilationStatus.Failed,
                DecompilerResult.Failure(
                    DiagnosticIds.MemorySafetyModeUnavailable,
                    "module memory-safety rules are Unsupported"),
                [], [], false, DecompilerSymbolSource.None, 0));

        var memberError = Assert.Throws<InvalidOperationException>(
            () => DotnetInspect.Web.Interop.Source.SourceExports.Adapt(
                memberEntry,
                participant));
        Assert.Contains(
            DecompilerDetail,
            memberError.Message,
            StringComparison.Ordinal);

        var decompiledAttempt = DecompilerResult.Failure(
            DiagnosticIds.MemorySafetyModeUnavailable,
            "module memory-safety rules are Unsupported");
        var entry = new AssemblyTypeSourceEntry.Unavailable(
            available.Subject,
            AssemblyTypeSourceRequest.From(type),
            new AssemblySourceFailure(
                AssemblySourceFailureKind.PdbAndDecompiledUnavailable,
                "Neither source form is available."),
            DecompiledAttempt: new CSharpDecompilationAttempt(
                CSharpDecompilationStatus.Failed,
                decompiledAttempt,
                [], [], false, DecompilerSymbolSource.None, 0));

        var error = Assert.ThrowsAny<InvalidOperationException>(
            () => DotnetInspect.Web.Interop.Source.SourceExports.Adapt(
                entry,
                participant));

        Assert.Contains(
            DecompilerDetail,
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceOwnership_AccountsArchivesAndCarriesSelectedFailures()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);

        await (await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate("Large.A", Package(image, "lib/net11.0/Large.A.dll", 60 * MiB))],
            TestContext.Current.CancellationToken))
            .DisposeAsync();
        long expectedResidentBytes = 0;
        foreach (string id in new[] { "Small.B", "Small.C", "Small.D" })
        {
            byte[] package = Package(
                image,
                $"lib/net11.0/{id}.dll",
                25 * MiB);
            expectedResidentBytes += package.LongLength;
            await (await BrowserPackageWorkspace.OpenScopeAsync(
                [await Coordinate(id, package)],
                TestContext.Current.CancellationToken))
                .DisposeAsync();
        }

        BrowserPackageCacheSnapshot stats = BrowserPackageWorkspace.Stats();
        Assert.Equal(3, stats.Workspaces);
        Assert.Equal(3, stats.Resident);
        Assert.Equal(expectedResidentBytes, stats.ResidentBytes);

        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            "pending.package@1.0.0",
            80L * MiB))
        {
            BrowserPackageCacheSnapshot reserved = BrowserPackageWorkspace.Stats();
            Assert.InRange(reserved.ResidentBytes, 80L * MiB, 128L * MiB);
            Assert.Equal(1, reserved.Workspaces);
        }

        await using BrowserScopeLease<BrowserInspectionScope> malformedLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Malformed",
                Package([0x01, 0x02, 0x03], "lib/net11.0/Malformed.dll"))],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope malformed = malformedLease.Scope;
        AssemblyContextApiSurfaceResult malformedResult = malformed.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(malformedResult.Assemblies.Assemblies));

        byte[] largeReferenceImage = new byte[40 * MiB];
        image.CopyTo(largeReferenceImage, 0);
        await using BrowserScopeLease<BrowserInspectionScope> referenceOnlyLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
            [await Coordinate(
                "Reference.Only",
                Package(
                    largeReferenceImage,
                    "ref/net11.0/Reference.Only.dll"))],
                    TestContext.Current.CancellationToken);
        BrowserInspectionScope referenceOnly = referenceOnlyLease.Scope;
        AssemblyContextApiSurfaceResult referenceResult = referenceOnly.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(referenceResult.Assemblies.Assemblies));

        BrowserPackageCoordinate oversized = await Coordinate(
            "Oversized.Role",
            PackageRole(
                image,
                "Oversized.Role",
                assemblyCount: 4,
                expandedAssemblyBytes: 20 * MiB));
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync([oversized], TestContext.Current.CancellationToken));
        Assert.Contains(
            "before assembly identity decoding",
            failure.Message,
            StringComparison.Ordinal);

        BrowserPackageCoordinate tooManyAssemblies = await Coordinate(
            "Too.Many.Assemblies",
            PackageRole(
                [0x01],
                "Too.Many.Assemblies",
                BrowserInspectionScope.MaxAssembliesPerRole + 1,
                expandedAssemblyBytes: 1));
        InvalidOperationException countFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync([tooManyAssemblies], TestContext.Current.CancellationToken));
        Assert.Contains(
            "assembly-count limit",
            countFailure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyCallGraph_UsesBrowserAssemblyRealizationPolicy()
    {
        PackageAssemblyContextRealizationOptions policy =
            BrowserPackageWorkspace.DependencyCallGraphRealizationPolicy;

        Assert.Equal(
            BrowserInspectionScope.MaxAssembliesPerRole,
            policy.MaxAssembliesPerRole);
        Assert.Equal(
            BrowserInspectionScope.MaxRetainedImageBytes,
            policy.MaxAggregateRetainedImageBytes);
        Assert.Equal(
            BrowserInspectionScope.MaxRetainedImageBytes,
            policy.MaxAssemblyEntryBytes);
        Assert.True(policy.RequireDeclaredEntryLengths);
    }

    [Fact]
    public async Task QueryPackage_AllSelectedFailuresPreserveKindWithoutArtifactDetail()
    {
        const string packageId = "Malformed.Surface";
        const string version = "1.0.0";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                version,
                Package(
                    [0x01, 0x02, 0x03],
                    $"lib/net11.0/{packageId}.dll"),
                fromCache: false));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DotnetInspect.Web.Interop.Package.PackageExports.QueryPackage(
                    packageId,
                    version,
                    "net11.0"));

        Assert.Contains(
            "Assembly unavailable: InvalidImage.",
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "invalid metadata",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformWorkspace_PinsAndAccumulatesSelectedAssemblies()
    {
        const string packageId =
            "microsoft.netcore.app.runtime.linux-x64";
        const string version = "11.0.0";
        byte[] coreLibrary = File.ReadAllBytes(typeof(object).Assembly.Location);
        byte[] sibling =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PlatformPackage(
            ("System.Private.CoreLib.dll", coreLibrary),
            ("DotnetInspect.Web.Tests.dll", sibling));
        var handler = new PlatformVersionHandler(
            packageId,
            version,
            nupkg);
        using var client = new HttpClient(handler);
        var authorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);

        await using BrowserPlatformScopeResolution initial =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                DotnetInspect.Web.Interop.Package.PackageExports.ProjectPlatformSurface(initial),
                BrowserPackageJsonContext.Default.BrowserPackageSurface));

        Assert.Equal("Microsoft.NETCore.App", surface.Package);
        Assert.Equal(version, surface.Version);
        Assert.Equal("System.Private.CoreLib", surface.DefaultAssemblyId);
        Assert.Single(surface.Assemblies);
        Assert.NotEmpty(surface.Types);
        Assert.Equal(
            "netcore.app",
            Assert.Single(surface.Assemblies).PlatformPack);
        Assert.All(
            surface.Types,
            type => Assert.Equal("netcore.app", type.PlatformPack));
        int requestsAfterInitialLoad = handler.Requests;
        await using BrowserPlatformScopeResolution reused =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Same(initial.Scope, reused.Scope);
        Assert.Equal(requestsAfterInitialLoad, handler.Requests);

        await using BrowserPlatformScopeResolution expanded =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                "net11.0",
                "DotnetInspect.Web.Tests.dll",
                "netcore.app",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

        Assert.Equal(2, expanded.Scope.Members.Length);
        Assert.All(
            expanded.Scope.Coordinates,
            coordinate =>
            {
                Assert.Equal(version, coordinate.Version);
                Assert.Equal(
                    NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
                    coordinate.Producer);
            });
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(initial.Scope));
        Assert.Single(initial.Scope.Members);
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(expanded.Scope));
        Assert.Equal(requestsAfterInitialLoad, handler.Requests);
        await reused.DisposeAsync();
        Assert.True(
            BrowserPackageWorkspace.IsScopeRetained(initial.Scope));
        await initial.DisposeAsync();
        Assert.False(
            BrowserPackageWorkspace.IsScopeRetained(initial.Scope));

        BrowserPackageSurface siblingSurface =
            Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePackAssembly(
                        "net11.0",
                        "DotnetInspect.Web.Tests.dll",
                        "netcore.app"),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
        Assert.Equal(
            "DotnetInspect.Web.Tests",
            siblingSurface.DefaultAssemblyId);
        BrowserPackageIntegrations integrations =
            Assert.IsType<BrowserPackageIntegrations>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPlatformIntegrations(
                        "net11.0",
                        "DotnetInspect.Web.Tests.dll",
                        "netcore.app"),
                    BrowserAnalysisJsonContext.Default.BrowserPackageIntegrations));
        Assert.True(integrations.IsComplete);
        Assert.Equal(
            BrowserAnalysisCompileLibraryStatus.Selected,
            integrations.CompileLibrary.Status);
        BrowserPackageOpportunities opportunities =
            Assert.IsType<BrowserPackageOpportunities>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Analysis.AnalysisExports.QueryPlatformOpportunities(
                        "net11.0",
                        "DotnetInspect.Web.Tests.dll",
                        "netcore.app"),
                    BrowserAnalysisJsonContext.Default.BrowserPackageOpportunities));
        Assert.True(opportunities.IsComplete);
        Assert.Equal(
            BrowserAnalysisCompileLibraryStatus.Selected,
            opportunities.CompileLibrary.Status);
        BrowserPackageMetadata metadata =
            Assert.IsType<BrowserPackageMetadata>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformMetadata(
                        "net11.0",
                        version,
                        "DotnetInspect.Web.Tests.dll",
                        "netcore.app"),
                    BrowserMetadataJsonContext.Default.BrowserPackageMetadata));
        Assert.Equal(
            BrowserMetadataCompileLibraryStatus.Selected,
            metadata.CompileLibrary.Status);

        var selected = siblingSurface.Types
            .SelectMany(type => type.Api.Select(member => (Type: type, Member: member)))
            .First(candidate =>
                candidate.Member.MetadataToken is > 0
                && candidate.Member.BodySelectors.Length > 0);
        BrowserAssemblySurface selectedAssembly =
            Assert.Single(
                siblingSurface.Assemblies,
                assembly => assembly.Id == selected.Type.AssemblyId);
        BrowserCallGraph graph = Assert.IsType<BrowserCallGraph>(
            JsonSerializer.Deserialize(
                await DotnetInspect.Web.Interop.CallGraph.CallGraphExports.ExpandPlatformCallGraph(
                    "net11.0",
                    "DotnetInspect.Web.Tests",
                    "netcore.app",
                    selectedAssembly.Version,
                    selectedAssembly.Culture,
                    selectedAssembly.PublicKeyToken,
                    selected.Type.MetadataId,
                    selected.Member.Name,
                    selected.Member.GraphSelectorKey,
                    selected.Member.MetadataToken!.Value),
                BrowserCallGraphJsonContext.Default.BrowserCallGraph));
        Assert.Equal(0, graph.Scope.Packages);
        Assert.Equal(2, graph.Scope.Assemblies);
        BrowserCallGraphTarget[] attributedTargets =
        [
            .. graph.Targets.Where(target =>
                target.Assembly is "System.Private.CoreLib"
                    or "DotnetInspect.Web.Tests"),
        ];
        Assert.NotEmpty(attributedTargets);
        Assert.All(
            attributedTargets,
            target => Assert.Equal("netcore.app", target.PlatformPack));
        InvalidOperationException identityMismatch =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DotnetInspect.Web.Interop.CallGraph.CallGraphExports.ExpandPlatformCallGraph(
                    "net11.0",
                    "DotnetInspect.Web.Tests",
                    "netcore.app",
                    "0.0.0.0",
                    selectedAssembly.Culture,
                    selectedAssembly.PublicKeyToken,
                    selected.Type.MetadataId,
                    selected.Member.Name,
                    selected.Member.GraphSelectorKey,
                    selected.Member.MetadataToken!.Value));
        Assert.Contains(
            "does not match the acquired assembly identity",
            identityMismatch.Message,
            StringComparison.Ordinal);

        await using BrowserPlatformScopeResolution qualifiedRuntime =
            await BrowserPlatformWorkspace.OpenRuntimeAsync(
                "net11.0-browser",
                client,
                authorization,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Single(qualifiedRuntime.Scope.Members);
        BrowserCallGraph lazySelectorGraph =
            Assert.IsType<BrowserCallGraph>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.CallGraph.CallGraphExports.ExpandPlatformCallGraph(
                        "net11.0-browser",
                        "DotnetInspect.Web.Tests",
                        "netcore.app",
                        selectedAssembly.Version,
                        selectedAssembly.Culture,
                        selectedAssembly.PublicKeyToken,
                        selected.Type.MetadataId,
                        selected.Member.Name,
                        selected.Member.GraphSelectorKey,
                        metadataToken: 0),
                    BrowserCallGraphJsonContext.Default.BrowserCallGraph));
        Assert.Equal(2, lazySelectorGraph.Scope.Assemblies);

        await qualifiedRuntime.DisposeAsync();
        await expanded.DisposeAsync();
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            "platform.eviction@1.0.0",
            128L * MiB))
        {
            Assert.False(
                BrowserPackageWorkspace.IsScopeRetained(expanded.Scope));
        }
        Assert.Throws<ObjectDisposedException>(
            () => expanded.Scope.Members);
    }
}
