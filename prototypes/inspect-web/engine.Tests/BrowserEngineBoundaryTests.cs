using System.IO.Compression;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Xml;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserEngineBoundaryTests
{
    const int MiB = 1024 * 1024;

    [Fact]
    public void WorkspaceOwnership_AccountsArchivesAndCarriesSelectedFailures()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);

        BrowserPackageWorkspace.OpenScope(
            [Coordinate("Large.A", Package(image, "lib/net11.0/Large.A.dll", 60 * MiB))]);
        foreach (string id in new[] { "Small.B", "Small.C", "Small.D" })
        {
            BrowserPackageWorkspace.OpenScope(
                [Coordinate(id, Package(image, $"lib/net11.0/{id}.dll", 25 * MiB))]);
        }

        BrowserPackageCacheStats stats = BrowserPackageWorkspace.Stats();
        Assert.Equal(3, stats.Workspaces);
        Assert.Equal(3, stats.Resident);
        Assert.InRange(stats.ResidentBytes, 75L * MiB, 76L * MiB);

        using (BrowserPackageWorkspace.ReservePackageDownload(
            "pending.package@1.0.0",
            80L * MiB))
        {
            BrowserPackageCacheStats reserved = BrowserPackageWorkspace.Stats();
            Assert.InRange(reserved.ResidentBytes, 80L * MiB, 128L * MiB);
            Assert.Equal(1, reserved.Workspaces);
        }

        BrowserInspectionScope malformed = BrowserPackageWorkspace.OpenScope(
            [Coordinate(
                "Malformed",
                Package([0x01, 0x02, 0x03], "lib/net11.0/Malformed.dll"))]);
        AssemblyContextApiSurfaceResult malformedResult = malformed.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(malformedResult.Assemblies.Assemblies));

        byte[] largeReferenceImage = new byte[40 * MiB];
        image.CopyTo(largeReferenceImage, 0);
        BrowserInspectionScope referenceOnly = BrowserPackageWorkspace.OpenScope(
            [Coordinate(
                "Reference.Only",
                Package(
                    largeReferenceImage,
                    "ref/net11.0/Reference.Only.dll"))]);
        AssemblyContextApiSurfaceResult referenceResult = referenceOnly.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(referenceResult.Assemblies.Assemblies));

        BrowserPackageCoordinate oversized = Coordinate(
            "Oversized.Role",
            PackageRole(
                image,
                "Oversized.Role",
                assemblyCount: 4,
                expandedAssemblyBytes: 20 * MiB));
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => BrowserPackageWorkspace.OpenScope([oversized]));
        Assert.Contains(
            "before assembly identity decoding",
            failure.Message,
            StringComparison.Ordinal);

        BrowserPackageCoordinate tooManyAssemblies = Coordinate(
            "Too.Many.Assemblies",
            PackageRole(
                [0x01],
                "Too.Many.Assemblies",
                BrowserInspectionScope.MaxAssembliesPerRole + 1,
                expandedAssemblyBytes: 1));
        InvalidOperationException countFailure = Assert.Throws<InvalidOperationException>(
            () => BrowserPackageWorkspace.OpenScope([tooManyAssemblies]));
        Assert.Contains(
            "assembly-count limit",
            countFailure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReusedCompositeScope_PreservesTheCurrentRequestedRoot()
    {
        byte[] firstImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        _ = Coordinate(
            "Root.Order.A",
            Package(firstImage, "lib/net11.0/Root.Order.A.dll"));
        _ = Coordinate(
            "Root.Order.B",
            Package(secondImage, "lib/net11.0/Root.Order.B.dll"));

        BrowserScopeResolution first = await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
        [
            new BrowserPackageRequest("Root.Order.A", "1.0.0", "net11.0"),
            new BrowserPackageRequest("Root.Order.B", "1.0.0", "net11.0"),
        ]);
        BrowserScopeResolution second = await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
        [
            new BrowserPackageRequest("Root.Order.B", "1.0.0", "net11.0"),
            new BrowserPackageRequest("Root.Order.A", "1.0.0", "net11.0"),
        ]);

        Assert.Same(first.Scope, second.Scope);
        BrowserPackageCoordinate requestedRoot = second.RequestedCoordinates[0];
        Assert.Equal("Root.Order.B", requestedRoot.PackageId);
        Assert.Equal("Root.Order.B", second.Scope.Coordinate(requestedRoot).PackageId);
    }

    [Fact]
    public void PackageArchiveEntryFlood_IsRejectedBeforeArchiveEnumeration()
    {
        const int maxEntries = 4_096;
        _ = new BrowserPackage(
            "Entry.Limit",
            "1.0.0",
            PackageEntries(maxEntries),
            fromCache: false);
        byte[] nupkg = PackageEntries(maxEntries + 1);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new BrowserPackage("Entry.Flood", "1.0.0", nupkg, fromCache: false));

        Assert.Contains("more than 4096 entries", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageDocumentDiscovery_UsesOneCachedEntryManifestAtTheLimit()
    {
        const int maxEntries = 4_096;
        var package = new BrowserPackage(
            "Document.Limit",
            "1.0.0",
            PackageDocuments(maxEntries),
            fromCache: false);

        IReadOnlyList<BrowserPackageDocument> documents = package.Documents();

        Assert.Equal(maxEntries, documents.Count);
        Assert.Same(
            package.Content.EnumerateEntriesWithLengths(),
            package.Content.EnumerateEntriesWithLengths());
    }

    [Fact]
    public void XmlDocumentation_DuplicateParametersUseTheLastCompilerEntry()
    {
        const string xml = """
            <doc>
              <members>
                <member name="M:Example.M(System.Int32)">
                  <summary>Summary</summary>
                  <param name="value">first</param>
                  <param name="value">second</param>
                </member>
              </members>
            </doc>
            """;

        BrowserMemberDocumentation documentation = BrowserXmlDocumentation.Read(
            System.Text.Encoding.UTF8.GetBytes(xml),
            "M:Example.M(System.Int32)");

        Assert.Equal("Summary", documentation.Summary);
        Assert.Equal("second", Assert.Single(documentation.Parameters).Value);
    }

    [Fact]
    public void XmlDocumentation_AcceptsTheDepthLimitAndRejectsTheNextElement()
    {
        BrowserMemberDocumentation accepted = BrowserXmlDocumentation.Read(
            System.Text.Encoding.UTF8.GetBytes(
                NestedDocumentation(CSharpText.XmlDocText.MaxElementDepth)),
            "M:Example.M");

        Assert.Equal("x", accepted.Summary);

        XmlException failure = Assert.Throws<XmlException>(
            () => BrowserXmlDocumentation.Read(
                System.Text.Encoding.UTF8.GetBytes(
                    NestedDocumentation(CSharpText.XmlDocText.MaxElementDepth + 1)),
                "M:Example.M"));

        Assert.Contains("supported element depth", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnconstrainedDependencyNavigation_SelectsLatestStableVersion()
    {
        Assert.Equal(
            "3.0.0",
            BrowserPackageWorkspace.SelectDependencyVersion(
                ["1.0.0", "3.1.0-preview.1", "3.0.0"],
                declaredRange: ""));
        Assert.Equal(
            "2.1.0",
            BrowserPackageWorkspace.SelectDependencyVersion(
                ["1.0.0", "2.0.0", "2.1.0", "3.0.0"],
                declaredRange: "2.*"));
    }

    [Fact]
    public void DependencyCoordinateMatch_PreservesProductOwnedProvenanceAndCardinality()
    {
        var platform = new BrowserDependencyCoordinateCandidate(
            "platform",
            BrowserDependencyCoordinateProvenance.PlatformRuntime,
            "Microsoft.NETCore.App",
            "10.0.10",
            "net10.0");
        var package = new BrowserDependencyCoordinateCandidate(
            "package",
            BrowserDependencyCoordinateProvenance.NuGetPackage,
            "Microsoft.NETCore.App",
            "2.2.8",
            "netcoreapp1.0");

        BrowserDependencyCoordinateMatch noMatch = MatchDependencyCoordinate(
            [platform],
            "Microsoft.NETCore.App",
            "1.0.5");
        BrowserDependencyCoordinateMatch unique = MatchDependencyCoordinate(
            [platform, package],
            "Microsoft.NETCore.App",
            "1.0.5");
        BrowserDependencyCoordinateMatch ambiguous = MatchDependencyCoordinate(
            [
                platform,
                package,
                package with { Key = "package-other-framework", TargetFramework = "net8.0" },
            ],
            "Microsoft.NETCore.App",
            "1.0.5");

        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.NoMatch, noMatch.Outcome);
        Assert.Null(noMatch.CandidateKey);
        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.Unique, unique.Outcome);
        Assert.Equal("package", unique.CandidateKey);
        Assert.Equal(BrowserDependencyCoordinateMatchOutcome.Ambiguous, ambiguous.Outcome);
        Assert.Null(ambiguous.CandidateKey);
    }

    [Fact]
    public void BuildIdentity_UsesVersionedRepositoryProvenance()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";

        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Create(
            "0.18.0",
            commit,
            "https://github.com/richlander/dotnet-inspect",
            "2026-08-14T23:30:22Z");

        Assert.Equal("0.18.0", identity.Version);
        Assert.Equal(commit, identity.Commit);
        Assert.Equal("2026-08-14T23:30:22.0000000+00:00", identity.BuiltAtUtc);
        Assert.Equal(
            $"https://github.com/richlander/dotnet-inspect/commit/{commit}",
            identity.CommitUrl);
    }

    [Fact]
    public void BuildIdentity_DropsInvalidOptionalProvenance()
    {
        BrowserBuildIdentity identity = BrowserBuildIdentityReader.Create(
            "0.18.0",
            "not-a-commit",
            "javascript:alert(1)",
            "not-a-time");

        Assert.Null(identity.Commit);
        Assert.Null(identity.BuiltAtUtc);
        Assert.Null(identity.CommitUrl);
    }

    [Fact]
    public void WorkspaceBinding_RejectsPackageParticipantsForPlatformScope()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = Coordinate(
            "Platform.Confusable",
            Package(image, "lib/net11.0/Platform.Confusable.dll"));
        PackageCompileAsset asset = Assert.Single(coordinate.Selection.Assets);
        using var workspace = new InspectionWorkspace();
        using var group = new BrowserWorkspaceGroup(
            workspace,
            [(coordinate, asset)],
            BrowserInspectionScope.MaxRetainedImageBytes);
        AssemblyReferenceIdentity identity = Assert.Single(group.Participants).Assembly.Identity;

        Assert.NotNull(group.Resolve(identity, AssemblyResolutionScope.Any));
        Assert.Null(group.Resolve(identity, AssemblyResolutionScope.Platform));
    }

    [Fact]
    public void WorkspaceBinding_RejectsEquivalentAssemblyIdentities()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate first = Coordinate(
            "Identity.Collision.A",
            Package(image, "lib/net11.0/Identity.Collision.A.dll"));
        BrowserPackageCoordinate second = Coordinate(
            "Identity.Collision.B",
            Package(image, "lib/net11.0/Identity.Collision.B.dll"));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new BrowserInspectionScope([first, second]));

        Assert.Contains(
            "same assembly identity",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementationPairing_RequiresEquivalentAssemblyIdentity()
    {
        byte[] surfaceImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] differentImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        BrowserPackageCoordinate mismatched = Coordinate(
            "Identity.Mismatch",
            PackagePair(surfaceImage, differentImage, "Identity.Pair.dll"));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => BrowserPackageWorkspace.OpenScope([mismatched]));

        Assert.Contains(
            "different assembly identities",
            failure.Message,
            StringComparison.Ordinal);

        BrowserPackageCoordinate equivalent = Coordinate(
            "Identity.Equivalent",
            PackagePair(surfaceImage, surfaceImage, "Identity.Pair.dll"));
        using BrowserInspectionScope equivalentScope =
            BrowserPackageWorkspace.OpenScope([equivalent]);
        BrowserWorkspaceParticipant equivalentSurface =
            Assert.Single(equivalentScope.SurfaceParticipants);

        Assert.NotNull(
            equivalentScope.ImplementationParticipant(equivalentSurface));
    }

    [Fact]
    public void CallGraphDiagnostics_PreserveIncompleteProductEvidence()
    {
        BrowserCallGraphDiagnostics diagnostics = BrowserInspectionEngine.Diagnostics(
            new CatalogCallGraphDiagnostics(2, 3, 4),
            hasUnexploredTraversalBoundary: true,
            hasAnalysisFailureBoundary: true);

        Assert.True(diagnostics.IsIncomplete);
        Assert.Equal(2, diagnostics.IncompleteNodes);
        Assert.Equal(3, diagnostics.IncompleteEdges);
        Assert.Equal(4, diagnostics.BindingIdentityConflicts);
        Assert.True(diagnostics.HasUnexploredTraversalBoundary);
        Assert.True(diagnostics.HasAnalysisFailureBoundary);
    }

    [Fact]
    public void SurfaceProjection_UsesExactMetadataTypeIdentityForBrowserKeys()
    {
        MetadataTypeDefinitionName nestedName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer", "Inner"]))
            .Name;
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer.Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = nestedName,
            Kind = "class",
        };

        BrowserTypeSurface projected = BrowserSurfaceProjection.Type(
            type,
            "Physical.dll",
            "asset:physical",
            "Sample");

        Assert.Equal("Sample.Outer+Inner", projected.Id);
        Assert.Equal("Physical.dll", projected.Assembly);
        Assert.Equal("asset:physical", projected.AssemblyId);
        Assert.Equal("Sample", projected.AssemblyName);
        Assert.Equal(projected.Id, projected.DefinitionId);
        Assert.Equal("Sample.Outer.Inner", projected.QueryId);
        Assert.Equal(projected.Id, projected.MetadataId);

        var literalPlus = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer+Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer+Inner"]))
                .Name,
            Kind = "class",
        };
        BrowserTypeSurface projectedLiteral =
            BrowserSurfaceProjection.Type(
                literalPlus,
                "Physical.dll",
                "asset:physical",
                "Sample");

        Assert.Equal(@"Sample.Outer\+Inner", projectedLiteral.Id);
        Assert.Equal(projectedLiteral.Id, projectedLiteral.DefinitionId);
        Assert.NotEqual(projected.Id, projectedLiteral.Id);
        Assert.Equal("Sample.Outer+Inner", projectedLiteral.QueryId);
        Assert.Equal(projected.MetadataId, projectedLiteral.MetadataId);

        BrowserTypeSurface qualified = projected with { Id = $"Sample.dll:{projected.Id}" };
        Assert.NotEqual(qualified.Id, qualified.DefinitionId);
        Assert.Equal(projected.DefinitionId, qualified.DefinitionId);
    }

    [Fact]
    public void SurfaceProjection_LongDeclaringTypeStopsIncrementally()
    {
        var type = new ApiType
        {
            Namespace = new string('N', 4_000),
            Name = "Amplifier",
            Kind = "class",
            Members =
            [
                .. Enumerable.Range(0, 10_000).Select(index => new ApiMember
                {
                    Name = $"M{index}",
                    Kind = "method",
                    Signature = $"void M{index}()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "void",
                        MemberName = $"M{index}",
                    },
                }),
            ],
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(8_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 64L * MiB,
            $"bounded Browser projection allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeTypeStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = new string('N', 4_000_000),
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_OneHugeMemberStopsBeforeDerivedIdentities()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Amplifier",
            MetadataName = "Amplifier",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "M",
                    Kind = "method",
                    Signature = new string('S', 4_000_000),
                },
            ],
        };
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(32_000_000);
        budget.BeginParticipant();
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                type,
                "Amplifier.dll",
                "asset:amplifier",
                "Amplifier",
                budget));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(
            allocated < 4L * MiB,
            $"Browser projection preflight allocated {allocated:N0} bytes");
    }

    [Fact]
    public void SurfaceProjection_PreflightUsesTheRemainingSharedBudget()
    {
        var budget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(1_000_000);
        budget.BeginParticipant();
        _ = BrowserSurfaceProjection.Type(
            new ApiType
            {
                Namespace = new string('C', 10_000),
                Name = "Committed",
                MetadataName = "Committed",
                Kind = "class",
            },
            "Committed.dll",
            "asset:committed",
            "Committed",
            budget);
        budget.CommitParticipant();
        Assert.True(budget.CommittedCharacters > 40_000);

        budget.BeginParticipant();
        Assert.Throws<BrowserSurfaceProjection.BrowserSurfaceTextBoundExceededException>(
            () => BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = new string('P', 80_000),
                    Name = "Pending",
                    MetadataName = "Pending",
                    Kind = "class",
                },
                "Pending.dll",
                "asset:pending",
                "Pending",
                budget));
    }

    [Fact]
    public async Task QueryPackage_FirstTransportTruncationReturnsTypedNotice()
    {
        const string packageId = "First.Transport.Truncation";
        byte[] image = BuildTransportAmplificationImage(
            packageId,
            typeCount: 10_000,
            namespaceLength: 1_000);
        _ = Coordinate(
            packageId,
            Package(image, $"lib/net11.0/{packageId}.dll"));

        string json = await BrowserInspectionEngine.QueryPackage(
            packageId,
            "1.0.0",
            "net11.0");
        BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
            JsonSerializer.Deserialize(
                json,
                BrowserJsonContext.Default.BrowserPackageSurface));

        Assert.Empty(surface.Assemblies);
        Assert.Contains(
            "truncated",
            surface.InspectionError,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SurfaceProjection_QualifiedCollisionIdIsAccountedBeforeCommit()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Value",
            MetadataName = "Value",
            Kind = "class",
        };
        const string assembly = "Collision.Assembly.dll";
        var unqualifiedBudget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(10_000);
        unqualifiedBudget.BeginParticipant();
        _ = BrowserSurfaceProjection.Type(
            type,
            assembly,
            "asset:collision",
            "Collision.Assembly",
            unqualifiedBudget);
        unqualifiedBudget.CommitParticipant();

        var qualifiedBudget =
            new BrowserSurfaceProjection.BrowserSurfaceTextBudget(10_000);
        qualifiedBudget.BeginParticipant();
        BrowserTypeSurface qualified = BrowserSurfaceProjection.Type(
            type,
            assembly,
            "asset:collision",
            "Collision.Assembly",
            qualifiedBudget,
            qualifyId: true);
        qualifiedBudget.CommitParticipant();

        Assert.Equal($"{assembly}:{qualified.DefinitionId}", qualified.Id);
        Assert.Equal(
            unqualifiedBudget.CommittedCharacters + assembly.Length + 1,
            qualifiedBudget.CommittedCharacters);
    }

    [Fact]
    public void ApiSurfacePolicy_AcceptsCoreLibraryAtEveryBrowserScope()
    {
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var reader = new PEReader(stream);

        foreach (ApiSurfaceExtractionScope scope in
            new[]
            {
                ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
                ApiSurfaceExtractionScope.IncludeAll,
            })
        {
            stream.Position = 0;
            var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
                ApiSurfaceExtractor.ExtractBounded(
                    reader,
                    scope,
                    new ApiSurfaceExtractionBounds(
                        BrowserApiSurfacePolicy.MaxTypes,
                        BrowserApiSurfacePolicy.MaxMembers,
                        BrowserApiSurfacePolicy.MaxInspectionFailures,
                        BrowserApiSurfacePolicy.MaxTypeForwarders,
                        BrowserApiSurfacePolicy.MaxMetadataRows,
                        BrowserApiSurfacePolicy.MaxRetainedTextCharacters)));
            if (scope == ApiSurfaceExtractionScope.PublicWithNonPublicTypes)
            {
                var transportBudget =
                    new BrowserSurfaceProjection.BrowserSurfaceTextBudget(
                        BrowserApiSurfacePolicy.MaxRetainedTextCharacters);
                transportBudget.BeginParticipant();
                foreach (ApiType type in extracted.Surface.Types)
                {
                    BrowserSurfaceProjection.Type(
                        type,
                        "System.Private.CoreLib.dll",
                        "runtime:corelib",
                        "System.Private.CoreLib",
                        transportBudget);
                }
                transportBudget.CommitParticipant();
            }
        }
    }

    static string NestedDocumentation(int depth)
    {
        string nested = string.Concat(Enumerable.Repeat("<b>", depth));
        string close = string.Concat(Enumerable.Repeat("</b>", depth));
        return $"<doc><members><member name=\"M:Example.M\"><summary>{nested}x{close}</summary>"
            + "</member></members></doc>";
    }

    static byte[] BuildTransportAmplificationImage(
        string assemblyName,
        int typeCount,
        int namespaceLength)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StringHandle @namespace =
            metadata.GetOrAddString(new string('N', namespaceLength));
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                @namespace,
                metadata.GetOrAddString($"T{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public async Task PackageDependencies_UsesProductQueriesForManifestAndReferences()
    {
        const string packageId = "Browser.Dependency.Root";
        byte[] image = File.ReadAllBytes(
            typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] nupkg = PackageWithManifest(
            image,
            $"lib/net11.0/{packageId}.dll",
            $"""
             <package>
               <metadata>
                 <id>{packageId}</id>
                 <version>1.0.0</version>
                 <dependencies>
                   <group targetFramework=".NETCoreApp,Version=v11.0">
                     <dependency id="Browser.Dependency.Child" version="[2.0.0]" />
                   </group>
                 </dependencies>
               </metadata>
             </package>
             """);
        BrowserPackageWorkspace.RegisterAcquiredPackage(
            new BrowserPackage(
                packageId,
                "1.0.0",
                nupkg,
                fromCache: false));

        string json = await BrowserInspectionEngine.QueryPackageDependencies(
            packageId,
            "1.0.0",
            "net11.0",
            $"{packageId}.dll");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(packageId, root.GetProperty("package").GetString());
        Assert.Equal("net11.0", root.GetProperty("activeFramework").GetString());
        JsonElement group = Assert.Single(
            root.GetProperty("dependencyGroups").EnumerateArray());
        Assert.Equal(0, group.GetProperty("index").GetInt32());
        Assert.True(group.GetProperty("isActive").GetBoolean());
        JsonElement dependency = Assert.Single(
            group.GetProperty("dependencies").EnumerateArray());
        Assert.Equal(
            "Browser.Dependency.Child",
            dependency.GetProperty("id").GetString());
        JsonElement reference = Assert.Single(
            root.GetProperty("assemblyReferences").EnumerateArray(),
            reference =>
                reference.GetProperty("name").GetString() == "System.Runtime");
        Assert.Equal("11.0.0.0", reference.GetProperty("version").GetString());
        Assert.True(reference.TryGetProperty("culture", out JsonElement culture));
        Assert.True(
            culture.ValueKind is JsonValueKind.Null or JsonValueKind.String);
        Assert.False(string.IsNullOrWhiteSpace(
            reference.GetProperty("publicKeyToken").GetString()));
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("dependencyGroupError").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("assemblyReferenceError").ValueKind);
    }

    [Fact]
    public void MermaidLabel_ContainsGrammarSignificantArtifactText()
    {
        string encoded = BrowserInspectionEngine.MermaidLabel(
            "A\"B\n<x>&\\\u2028");

        Assert.Equal(
            "A&quot;B&#92;u000A&lt;x&gt;&amp;&#92;&#92;u2028",
            encoded);
        Assert.DoesNotContain('"', encoded);
        Assert.DoesNotContain('\n', encoded);
        Assert.DoesNotContain('<', encoded);
        Assert.DoesNotContain('>', encoded);
        Assert.DoesNotContain('\\', encoded);
        Assert.DoesNotContain('\u2028', encoded);
    }

    [Fact]
    public void CallGraphMermaid_DerivesLoopEdgesFromTypedProjectionState()
    {
        TypeRef type = TypeRef.Definition(
            "Example",
            "Example",
            "Worker");
        TypeRef returnType = TypeRef.CoreLib("System", "Void");
        var caller = new MemberRef(
            type,
            "Run",
            [],
            returnType,
            MemberKind.Method);
        var callee = new MemberRef(
            type,
            "Tick",
            [],
            returnType,
            MemberKind.Method);
        var calleeNode = new CallTreeNode(
            callee,
            null,
            CallTreeStatus.Leaf,
            [],
            new CallTreePerf(0, 0, 1, true, "loop"));
        var root = new CallTreeNode(
            caller,
            null,
            CallTreeStatus.Expanded,
            [calleeNode],
            new CallTreePerf(0, 0, 1, false));

        string mermaid = BrowserInspectionEngine.Mermaid(
            CallGraphProjection.FromCallees(root));

        Assert.Contains("n0 -- loop --> n1", mermaid);
    }

    [Fact]
    public void CallGraphTargets_CarryEveryNavigableNodeWithNormalizedKinds()
    {
        TypeRef declaringTypeDefinition = TypeRef.Definition(
            "Example",
            "Example",
            "Outer`1+Widget`1");
        TypeRef declaringType = TypeRef.GenericInstance(
            declaringTypeDefinition,
            [TypeRef.CoreLib("System", "String"), TypeRef.CoreLib("System", "Int32")]);
        TypeRef returnType = TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void");
        var member = new MemberRef(
            declaringType,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        var arrayMember = new MemberRef(
            TypeRef.MdArray(declaringTypeDefinition, rank: 2),
            "Get",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(member),
                member,
                "focus",
                CallGraphNodeKind.Focus),
            new(
                1,
                GraphNodeIdentity.FromMember(member),
                member,
                "normal",
                CallGraphNodeKind.Normal),
            new(
                2,
                GraphNodeIdentity.FromMember(member),
                member,
                "external",
                CallGraphNodeKind.External),
            new(
                3,
                GraphNodeIdentity.FromMember(arrayMember),
                arrayMember,
                "array",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTarget[] targets = BrowserInspectionEngine.Targets(
            nodes,
            [new AssemblyReferenceIdentity(
                "Example",
                new Version(1, 2, 3, 4),
                "neutral",
                "0011223344556677")]);

        Assert.Equal(["n0", "n1", "n2", "n3"], targets.Select(target => target.Id));
        Assert.Equal(
            ["focus", "normal", "external", "normal"],
            targets.Select(target => target.Kind));
        Assert.All(
            targets[..3],
            target =>
            {
                Assert.Equal("Example", target.Assembly);
                Assert.Equal("1.2.3.4", target.AssemblyVersion);
                Assert.Equal("neutral", target.AssemblyCulture);
                Assert.Equal("0011223344556677", target.AssemblyPublicKeyToken);
                Assert.Equal("Example.Outer.Widget<int>", target.TypeFullName);
                Assert.Equal("Example.Outer`1+Widget`1", target.TypeMetadataId);
            });
        Assert.Null(targets[3].TypeMetadataId);
    }

    // A package coordinate becomes a flat-container path segment and a cache key. Both halves are
    // validated before either use, so a segment-breaking coordinate never reaches the cache or
    // the network — the failing handler below proves no request was attempted.
    [Theory]
    [InlineData("evil/../other", "1.0.0")]
    [InlineData("..", "1.0.0")]
    [InlineData("Example", "1.0.0/../9.9.9")]
    [InlineData("Example", "1.0.0?x=1")]
    [InlineData("Example", "1.0.0 ")]
    [InlineData("Example", "notaversion")]
    public async Task PackageCoordinates_AreRejectedBeforeAnyCacheOrNetworkAccess(
        string packageId,
        string version)
    {
        BrowserPackageCacheStats before = BrowserPackageWorkspace.Stats();

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(packageId, version));

        Assert.Contains("package coordinate", failure.Message, StringComparison.OrdinalIgnoreCase);
        BrowserPackageCacheStats after = BrowserPackageWorkspace.Stats();
        Assert.Equal(before.Packages, after.Packages);
        Assert.Equal(before.Resident, after.Resident);
        Assert.Equal(before.ResidentBytes, after.ResidentBytes);
    }

    [Fact]
    public async Task PackageAcquisition_StallBecomesVisibleOperationTimeout()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> acquisition = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            TimeSpan.FromMilliseconds(200));
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(() => acquisition);

        Assert.Contains(
            "Browser package operation",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task PackageAcquisition_ExactPinUsesGalleryCdnWithoutServiceIndex()
    {
        string packageId = $"gallery.exact.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        byte[] archive = PackageDocuments(1);
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            archive);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version,
            source,
            TimeSpan.FromSeconds(5));

        Assert.Equal(version, package.Version);
        Assert.Equal(archive, package.RetainedBytes);
        Assert.False(package.Content.FromCache);
        Assert.Equal(
            NuGetCache.GetSourceKey(PackageSourceIdentity.NuGetOrg.Value),
            package.Content.ProducerKey);
        Assert.Equal(
            [$"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg"],
            handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.Contains(
                "api.nuget.org",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageAcquisition_GalleryFailureRemainsVisible()
    {
        string packageId = $"gallery.failure.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            PackageDocuments(1),
            packageStatus: System.Net.HttpStatusCode.BadGateway);
        using IPackageSourceClient source = Gallery(handler);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(
                    packageId,
                    "1.0.0",
                    source,
                    TimeSpan.FromSeconds(5)));

        Assert.Contains(
            "transport failed",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "globalcdn.nuget.org",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackageAcquisition_RejectedReservationDisposesGalleryPayload()
    {
        string packageId = $"gallery.no-length.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(
            packageId,
            "1.0.0",
            PackageDocuments(1),
            omitContentLength: true);
        using IPackageSourceClient source = Gallery(handler);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.AcquireAsync(
                    packageId,
                    "1.0.0",
                    source,
                    TimeSpan.FromSeconds(5)));

        Assert.Contains(
            "did not declare its byte length",
            failure.Message,
            StringComparison.Ordinal);
        Assert.True(handler.PayloadDisposed);
    }

    [Fact]
    public async Task PackageAcquisition_FloatingRootUsesGallerySearchAndCdn()
    {
        string packageId = $"gallery.floating.{Guid.NewGuid():N}";
        const string version = "4.5.6";
        var handler = new GalleryPackageHandler(
            packageId,
            version,
            PackageDocuments(1),
            provideSearchResult: true);
        using IPackageSourceClient source = Gallery(handler);

        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version: null,
            source,
            TimeSpan.FromSeconds(5));

        Assert.Equal(version, package.Version);
        Assert.Equal(2, handler.Requested.Count);
        Assert.StartsWith(
            "https://azuresearch-usnc.nuget.org/query?",
            handler.Requested[0],
            StringComparison.Ordinal);
        Assert.Contains(
            $"q=packageid%3A{packageId}",
            handler.Requested[0],
            StringComparison.Ordinal);
        Assert.Equal(
            $"https://globalcdn.nuget.org/packages/{packageId}.{version}.nupkg",
            handler.Requested[1]);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.Contains(
                "api.nuget.org",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageResolution_StallBecomesVisibleOperationTimeout()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"resolution.timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> acquisition = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            version: null,
            source,
            TimeSpan.FromSeconds(5));
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(() => acquisition);

        Assert.Contains(
            "Browser package operation",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task PackageAcquisition_SharedStallIsAVisibleTimeoutForEveryCaller()
    {
        var handler = new StallingPackageHandler();
        using IPackageSourceClient source = Gallery(handler);
        string packageId =
            $"shared.timeout.package.{Guid.NewGuid():N}";

        Task<BrowserPackage> first = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            TimeSpan.FromMilliseconds(500));
        await handler.RequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        Task<BrowserPackage> second = BrowserPackageWorkspace.AcquireAsync(
            packageId,
            "1.0.0",
            source,
            TimeSpan.FromMilliseconds(100));

        TimeoutException secondFailure =
            await Assert.ThrowsAsync<TimeoutException>(() => second);
        Assert.Contains(
            "0.1-second deadline",
            secondFailure.Message,
            StringComparison.Ordinal);
        Assert.False(first.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(() => first);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public void PackageAcquisition_ExpiredDeadlineCannotPublishReservedContent()
    {
        using var deadline =
            new BrowserPackageWorkspace.BrowserPackageOperationDeadline(
                TimeSpan.FromMilliseconds(10));
        var inner = new RecordingTransferPolicy();
        var policy =
            new BrowserPackageWorkspace.BrowserPackageOperationTransferPolicy(
                inner,
                deadline);
        using IPackagePayloadReservation reservation =
            policy.ApplyDeadline(inner.Reservation);
        while (!deadline.HasExpired)
            Thread.SpinWait(100);

        Assert.Throws<TimeoutException>(() => reservation.Complete());
        Assert.False(inner.Reservation.Completed);
    }

    [Fact]
    public async Task PackageOperation_LateFailureBecomesVisibleTimeout()
    {
        TimeoutException failure =
            await Assert.ThrowsAsync<TimeoutException>(
                () => BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                    deadline =>
                    {
                        while (!deadline.HasExpired)
                            Thread.SpinWait(100);
                        return Task.FromException<int>(
                            new InvalidOperationException(
                                "Synchronous work failed after the deadline."));
                    },
                    TimeSpan.FromMilliseconds(10)));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
    }

    [Fact]
    public async Task PackageVersionIndex_ValidatesTheIdBeforeRequestingIt()
    {
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageWorkspace.GetVersionsAsync("evil/../other"));

        Assert.Contains("package coordinate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactDependencyNavigation_DoesNotRequireAListedVersion()
    {
        string resolved =
            await BrowserPackageWorkspace.ResolveDependencyVersionAsync(
                "Example.Package",
                "[999999.0]");

        Assert.Equal("999999.0.0", resolved);
    }

    private static BrowserDependencyCoordinateMatch MatchDependencyCoordinate(
        BrowserDependencyCoordinateCandidate[] candidates,
        string packageId,
        string declaredRange)
    {
        string candidatesJson = JsonSerializer.Serialize(
            candidates,
            BrowserJsonContext.Default.BrowserDependencyCoordinateCandidateArray);
        string resultJson = BrowserInspectionEngine.MatchPackageDependencyCoordinate(
            packageId,
            declaredRange,
            candidatesJson);
        return JsonSerializer.Deserialize(
            resultJson,
            BrowserJsonContext.Default.BrowserDependencyCoordinateMatch)
            ?? throw new InvalidOperationException("The dependency-coordinate result is absent.");
    }

    // The default package load runs under explicit bounds and says so when it stops early. Both
    // halves matter: an ordinary projection must be untouched, and the bound must be reachable.
    [Fact]
    public void ApiSurfaceProjection_IsBoundedAndReportsTruncation()
    {
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserInspectionScope scope = BrowserPackageWorkspace.OpenScope(
            [Coordinate("Bounded.Surface", Package(image, "lib/net11.0/Bounded.Surface.dll"))]);

        AssemblyContextApiSurfaceResult complete = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits));

        Assert.Null(complete.Truncation);
        Assert.True(complete.IsComplete);
        Assert.Null(BrowserApiSurfacePolicy.TruncationNotice(complete.Truncation));
        int projectedTypes = complete.Assemblies.Assemblies
            .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
            .Sum(entry => entry.Value.Surface.Types.Count);
        Assert.True(projectedTypes > 0);
        Assert.True(projectedTypes < BrowserApiSurfacePolicy.MaxTypes);

        AssemblyContextApiSurfaceResult truncated = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(1, 1, 1, 1, 1, int.MaxValue)));

        Assert.NotNull(truncated.Truncation);
        Assert.False(truncated.IsComplete);
        string notice = Assert.IsType<string>(
            BrowserApiSurfacePolicy.TruncationNotice(truncated.Truncation));
        Assert.Contains("API surface truncated", notice, StringComparison.Ordinal);

        // A truncation is carried beside participant failures, never instead of them.
        Assert.Equal(
            notice,
            BrowserSurfaceProjection.Notice(truncated.Assemblies.Assemblies, notice));

        AssemblyContextApiSurfaceResult textTruncated = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(
                    1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    1)));
        Assert.Equal(
            ApiSurfaceProjectionLimit.RetainedTextCharacters,
            textTruncated.Truncation!.Limit);
        Assert.Contains(
            "retained text character",
            BrowserApiSurfacePolicy.TruncationNotice(textTruncated.Truncation),
            StringComparison.Ordinal);
    }

    // A nested Outer+Inner and a type whose own metadata name is literally "Outer+Inner" share a
    // flattened spelling. The browser must carry an identity that tells them apart, and must not
    // publish the flattened one where it names both.
    [Fact]
    public void CallGraphTargets_DistinguishNestedFromLiteralPlusDeclaringTypes()
    {
        TypeRef nested = ResolvedDefinition("Example", ["Outer", "Inner"]);
        TypeRef literalPlus = ResolvedDefinition("Example", ["Outer+Inner"]);
        TypeRef returnType = TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void");
        var nestedMember = new MemberRef(
            nested,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        var literalPlusMember = new MemberRef(
            literalPlus,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(nestedMember),
                nestedMember,
                "nested",
                CallGraphNodeKind.Normal),
            new(
                1,
                GraphNodeIdentity.FromMember(literalPlusMember),
                literalPlusMember,
                "literal",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTarget[] targets = BrowserInspectionEngine.Targets(nodes);

        // Both declaring types flatten to the same metadata spelling. That spelling genuinely
        // names the nested type, so it is still published for it; for the literal-plus type it
        // names the other one, so it is withheld rather than published as if it named this one.
        Assert.Equal("Outer+Inner", nested.Name);
        Assert.Equal("Outer+Inner", literalPlus.Name);
        Assert.Equal("Example.Outer+Inner", targets[0].TypeMetadataId);
        Assert.Null(targets[1].TypeMetadataId);

        // The escaped structured identity resolves each target uniquely, and is the same
        // projection a browsable type row carries as its id.
        Assert.Equal("Example.Outer+Inner", targets[0].TypeDefinitionId);
        Assert.Equal(@"Example.Outer\+Inner", targets[1].TypeDefinitionId);
        Assert.NotEqual(targets[0].TypeDefinitionId, targets[1].TypeDefinitionId);
        Assert.Equal(
            targets[0].TypeDefinitionId,
            BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = "Example",
                    Name = "Outer.Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = DefinitionName("Example", ["Outer", "Inner"]),
                    Kind = "class",
                },
                "Example.dll",
                "asset:example",
                "Example").DefinitionId);
        Assert.Equal(
            targets[1].TypeDefinitionId,
            BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = "Example",
                    Name = "Outer+Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = DefinitionName("Example", ["Outer+Inner"]),
                    Kind = "class",
                },
                "Example.dll",
                "asset:example",
                "Example").DefinitionId);
    }

    [Fact]
    public void CallGraphTargets_KeepTheLegacyIdentityWhereItIsUnambiguous()
    {
        TypeRef declaring = ResolvedDefinition("Example", ["Outer`1", "Widget`1"]);
        var member = new MemberRef(
            declaring,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void"),
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(member),
                member,
                "nested",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTarget[] targets = BrowserInspectionEngine.Targets(nodes);

        Assert.Equal("Example.Outer`1+Widget`1", targets[0].TypeMetadataId);
        Assert.Equal("Example.Outer`1+Widget`1", targets[0].TypeDefinitionId);
    }

    static TypeRef ResolvedDefinition(string @namespace, string[] segments)
        => TypeRef.Definition(
            "Example",
            @namespace,
            string.Join('+', segments),
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                DefinitionName(@namespace, segments)));

    static MetadataTypeDefinitionName DefinitionName(string @namespace, string[] segments)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(@namespace, [.. segments]))
            .Name;

    static BrowserPackageCoordinate Coordinate(string id, byte[] nupkg)
    {
        var package = new BrowserPackage(id, "1.0.0", nupkg, fromCache: false);
        BrowserPackageWorkspace.RegisterAcquiredPackage(package);
        PackageCompileAssetSelection selection = PackageCompileAssetSelector.Select(
            package.Content,
            id,
            "net11.0");
        Assert.True(selection.IsSelected);
        return new BrowserPackageCoordinate(package, selection);
    }

    static byte[] Package(
        byte[] assembly,
        string assemblyPath,
        int paddingBytes = 0)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry(assemblyPath, CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(assembly);
            }

            if (paddingBytes > 0)
            {
                using Stream padding = archive
                    .CreateEntry("content/padding.bin", CompressionLevel.NoCompression)
                    .Open();
                byte[] block = new byte[64 * 1024];
                int remaining = paddingBytes;
                while (remaining > 0)
                {
                    int count = Math.Min(remaining, block.Length);
                    padding.Write(block, 0, count);
                    remaining -= count;
                }
            }
        }

        return content.ToArray();
    }

    static byte[] PackagePair(
        byte[] surfaceAssembly,
        byte[] implementationAssembly,
        string assemblyFileName)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry(
                    $"ref/net11.0/{assemblyFileName}",
                    CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(surfaceAssembly);
            }

            using (Stream entry = archive
                .CreateEntry(
                    $"lib/net11.0/{assemblyFileName}",
                    CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(implementationAssembly);
            }
        }

        return content.ToArray();
    }

    static byte[] PackageWithManifest(
        byte[] assembly,
        string assemblyPath,
        string manifest)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(
            content,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry(assemblyPath, CompressionLevel.NoCompression)
                .Open())
            {
                entry.Write(assembly);
            }

            using Stream nuspec = archive
                .CreateEntry(
                    "Browser.Dependency.Root.nuspec",
                    CompressionLevel.NoCompression)
                .Open();
            using var writer = new StreamWriter(
                nuspec,
                System.Text.Encoding.UTF8,
                leaveOpen: true);
            writer.Write(manifest);
        }

        return content.ToArray();
    }

    static byte[] PackageRole(
        byte[] assembly,
        string assemblyName,
        int assemblyCount,
        int expandedAssemblyBytes)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            byte[] expanded = new byte[expandedAssemblyBytes];
            assembly.CopyTo(expanded, 0);
            for (int index = 0; index < assemblyCount; index++)
            {
                using Stream entry = archive
                    .CreateEntry(
                        $"lib/net11.0/{assemblyName}.{index}.dll",
                        CompressionLevel.SmallestSize)
                    .Open();
                entry.Write(expanded);
            }
        }

        return content.ToArray();
    }

    static byte[] PackageEntries(int entryCount)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int index = 0; index < entryCount; index++)
                archive.CreateEntry($"content/{index:D5}.txt", CompressionLevel.NoCompression);
        }

        return content.ToArray();
    }

    static byte[] PackageDocuments(int entryCount)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int index = 0; index < entryCount; index++)
            {
                archive.CreateEntry(
                    $"skills/skill-{index:D5}.md",
                    CompressionLevel.NoCompression);
            }
        }

        return content.ToArray();
    }

    static IPackageSourceClient Gallery(HttpMessageHandler handler) =>
        PackageSourceClientFactory.CreateGallery(
            handler,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromMinutes(1),
                OperationTimeout = TimeSpan.FromMinutes(1),
            });

    sealed class StallingPackageHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            RequestStarted.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The stalling handler completed without cancellation.");
        }
    }

    sealed class GalleryPackageHandler(
        string packageId,
        string version,
        byte[] archive,
        bool provideSearchResult = false,
        System.Net.HttpStatusCode packageStatus =
            System.Net.HttpStatusCode.OK,
        bool omitContentLength = false)
        : HttpMessageHandler
    {
        readonly string _packageUrl =
            $"https://globalcdn.nuget.org/packages/{packageId.ToLowerInvariant()}.{version}.nupkg";

        public List<string> Requested { get; } = [];
        public bool PayloadDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            if (provideSearchResult
                && url.StartsWith(
                    "https://azuresearch-usnc.nuget.org/query?",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""{"data":[{"id":"{{packageId}}","version":"{{version}}"}]}"""),
                    });
            }

            if (!url.Equals(_packageUrl, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(
                        System.Net.HttpStatusCode.NotFound));
            }

            var response = new HttpResponseMessage(packageStatus);
            if (packageStatus == System.Net.HttpStatusCode.OK)
            {
                response.Content = omitContentLength
                    ? new StreamContent(
                        new TrackingPayloadStream(
                            archive,
                            () => PayloadDisposed = true))
                    : new ByteArrayContent(archive);
            }

            return Task.FromResult(response);
        }
    }

    sealed class TrackingPayloadStream(byte[] bytes, Action onDispose)
        : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                onDispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            onDispose();
            return base.DisposeAsync();
        }
    }

    sealed class RecordingTransferPolicy : IPackagePayloadTransferPolicy
    {
        internal RecordingReservation Reservation { get; } = new();

        public IPackagePayloadReservation Reserve(
            PackagePayloadTransfer transfer) =>
            Reservation;
    }

    sealed class RecordingReservation : IPackagePayloadReservation
    {
        internal bool Completed { get; private set; }

        public void Complete() => Completed = true;

        public void Dispose()
        {
        }
    }

}
