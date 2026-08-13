using System.Buffers.Binary;
using System.IO.Compression;
using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Xml;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;

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
        byte[] image = File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        _ = Coordinate("Root.Order.A", Package(image, "lib/net11.0/Root.Order.A.dll"));
        _ = Coordinate("Root.Order.B", Package(image, "lib/net11.0/Root.Order.B.dll"));

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
        _ = new BrowserPackage(
            "Entry.Limit",
            "1.0.0",
            PackageEntries(BrowserPackageArchiveValidator.MaxEntries),
            fromCache: false);
        byte[] nupkg = PackageEntries(BrowserPackageArchiveValidator.MaxEntries + 1);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new BrowserPackage("Entry.Flood", "1.0.0", nupkg, fromCache: false));

        Assert.Contains("entry-count limit", failure.Message, StringComparison.Ordinal);

        InvalidOperationException zip64Failure = Assert.Throws<InvalidOperationException>(
            () => BrowserPackageArchiveValidator.Validate(
                Zip64WithDeclaredEntryCount(BrowserPackageArchiveValidator.MaxEntries + 1)));
        Assert.Contains("entry-count limit", zip64Failure.Message, StringComparison.Ordinal);

        InvalidOperationException ambiguousFailure = Assert.Throws<InvalidOperationException>(
            () => BrowserPackageArchiveValidator.Validate(
                ArchiveWithShadowedEndRecord(
                    PackageEntries(BrowserPackageArchiveValidator.MaxEntries + 1))));
        Assert.Contains("entry-count limit", ambiguousFailure.Message, StringComparison.Ordinal);

        byte[] divergent = ArchiveWithIgnoredZip64SizeSentinel(nupkg);
        using (var archive = new ZipArchive(
            new MemoryStream(divergent, writable: false),
            ZipArchiveMode.Read))
        {
            Assert.Equal(
                BrowserPackageArchiveValidator.MaxEntries + 1,
                archive.Entries.Count);
        }

        InvalidOperationException divergentFailure = Assert.Throws<InvalidOperationException>(
            () => BrowserPackageArchiveValidator.Validate(divergent));
        Assert.Contains("entry-count limit", divergentFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageDocumentDiscovery_UsesOneCachedEntryManifestAtTheLimit()
    {
        var package = new BrowserPackage(
            "Document.Limit",
            "1.0.0",
            PackageDocuments(BrowserPackageArchiveValidator.MaxEntries),
            fromCache: false);

        IReadOnlyList<BrowserPackageDocument> documents = package.Documents();

        Assert.Equal(BrowserPackageArchiveValidator.MaxEntries, documents.Count);
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
    public void PackageVersionIndex_RejectsSuccessShapedMalformedEntries()
    {
        Assert.Equal(
            ["1.0.0", "2.0.0-preview.1"],
            BrowserPackageWorkspace.ParseVersions(
                """{"versions":["1.0.0","2.0.0-preview.1"]}"""u8.ToArray(),
                "example"));

        InvalidDataException failure = Assert.Throws<InvalidDataException>(
            () => BrowserPackageWorkspace.ParseVersions(
                """{"versions":["1.0.0",null]}"""u8.ToArray(),
                "example"));

        Assert.Contains("invalid version", failure.Message, StringComparison.Ordinal);
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

    static string NestedDocumentation(int depth)
    {
        string nested = string.Concat(Enumerable.Repeat("<b>", depth));
        string close = string.Concat(Enumerable.Repeat("</b>", depth));
        return $"<doc><members><member name=\"M:Example.M\"><summary>{nested}x{close}</summary>"
            + "</member></members></doc>";
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
            new(0, member, "focus", CallGraphNodeKind.Focus),
            new(1, member, "normal", CallGraphNodeKind.Normal),
            new(2, member, "external", CallGraphNodeKind.External),
            new(3, arrayMember, "array", CallGraphNodeKind.Normal),
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

    static byte[] Zip64WithDeclaredEntryCount(int entryCount)
    {
        byte[] archive = new byte[56 + 20 + 22];
        Span<byte> bytes = archive;

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[4..], 44);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..], 45);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[14..], 45);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], (ulong)entryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[32..], (ulong)entryCount);

        int locator = 56;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[locator..], 0x07064b50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[(locator + 16)..], 1);

        int end = locator + 20;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[end..], 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(end + 8)..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(end + 10)..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[(end + 12)..], uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[(end + 16)..], uint.MaxValue);
        return archive;
    }

    static byte[] ArchiveWithShadowedEndRecord(byte[] canonical)
    {
        int realEnd = -1;
        for (int offset = canonical.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(canonical.AsSpan(offset)) == 0x06054b50)
            {
                realEnd = offset;
                break;
            }
        }
        Assert.True(realEnd >= 0);

        byte[] ambiguous = new byte[canonical.Length + 23];
        canonical.AsSpan(0, realEnd).CopyTo(ambiguous);
        Span<byte> shadow = ambiguous.AsSpan(realEnd, 22);
        BinaryPrimitives.WriteUInt32LittleEndian(shadow, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(shadow[20..], 23);
        canonical.AsSpan(realEnd).CopyTo(ambiguous.AsSpan(realEnd + 22));
        return ambiguous;
    }

    static byte[] ArchiveWithIgnoredZip64SizeSentinel(byte[] canonical)
    {
        int realEnd = -1;
        for (int offset = canonical.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(canonical.AsSpan(offset)) == 0x06054b50)
            {
                realEnd = offset;
                break;
            }
        }
        Assert.True(realEnd >= 0);

        ReadOnlySpan<byte> originalEnd = canonical.AsSpan(realEnd, 22);
        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(originalEnd[10..]);
        uint directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(originalEnd[16..]);
        ReadOnlySpan<byte> firstHeader = canonical.AsSpan((int)directoryOffset);
        int firstHeaderLength = 46
            + BinaryPrimitives.ReadUInt16LittleEndian(firstHeader[28..])
            + BinaryPrimitives.ReadUInt16LittleEndian(firstHeader[30..])
            + BinaryPrimitives.ReadUInt16LittleEndian(firstHeader[32..]);

        byte[] divergent = new byte[realEnd + 56 + 20 + 22];
        canonical.AsSpan(0, realEnd).CopyTo(divergent);
        Span<byte> bytes = divergent;

        int zip64 = realEnd;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[zip64..], 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[(zip64 + 4)..], 44);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(zip64 + 12)..], 45);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(zip64 + 14)..], 45);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[(zip64 + 24)..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[(zip64 + 32)..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[(zip64 + 40)..], (ulong)firstHeaderLength);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[(zip64 + 48)..], directoryOffset);

        int locator = zip64 + 56;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[locator..], 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[(locator + 8)..], (ulong)zip64);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[(locator + 16)..], 1);

        int end = locator + 20;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[end..], 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(end + 8)..], totalEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(end + 10)..], totalEntries);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[(end + 12)..], uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[(end + 16)..], directoryOffset);
        return divergent;
    }
}
