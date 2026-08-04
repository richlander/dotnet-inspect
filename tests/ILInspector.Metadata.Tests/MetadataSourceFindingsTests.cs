using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataSourceFindingsTests
{
    const int LargeBlobLength = 4 * 1024 * 1024;
    static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    static readonly FindingSubject Subject = new("assembly", "Test assembly");

    static int SourceMappedMethod(int value)
    {
        int adjusted = value + 1;
        return adjusted * 2;
    }

    sealed class NestedTypeSourceProbe
    {
        public static int SourceMappedMethod() => 42;
    }

#line 100 "Generated/MetadataSourceFindings.g.cs"
    static int MultiDocumentMappedMethod(int value)
    {
        int adjusted = value + 1;
#line default
        return adjusted * 2;
    }

    sealed class OrderedTypeSourceProbe
    {
#line 100 "Generated/OrderedType.Z.cs"
        public static int First() => 1;
#line 100 "Generated/OrderedType.A.cs"
        public static int Second() => 2;
#line default
    }

    [Fact]
    public void SourceDocuments_ReturnCompleteFilteredCensus()
    {
        SourceDocument[] documents =
        [
            new(
                "/_/src/System.Text.Json/JsonSerializer.cs",
                IsEmbedded: false,
                "https://example.test/repository/commit/src/System.Text.Json/JsonSerializer.cs",
                [0x01, 0xA2],
                "SHA256",
                DocumentRowId: 7,
                CanonicalPath: "src/System.Text.Json/JsonSerializer.cs"),
            new(
                "/_/src/System.Net.Http/HttpClient.cs",
                IsEmbedded: true,
                ResolvedUrl: null,
                DocumentRowId: 9,
                CanonicalPath: "src/System.Net.Http/HttpClient.cs"),
            new(
                "/_/src/Components/App.razor",
                IsEmbedded: false,
                ResolvedUrl: "https://example.test/repository/commit/src/Components/App.razor",
                DocumentRowId: 10,
                CanonicalPath: "src/Components/App.razor"),
        ];

        var all = Findings(SourceLinkFindings.InspectSourceDocuments(documents, Subject));
        var filtered = Findings(SourceLinkFindings.InspectSourceDocuments(
            documents,
            Subject,
            new SourceDocumentQuery("text.json")));
        var missing = Findings(SourceLinkFindings.InspectSourceDocuments(
            documents,
            Subject,
            new SourceDocumentQuery("does-not-exist")));

        Assert.Equal(3, all.Length);
        Assert.Contains(all, finding =>
            finding.Payload.CanonicalPath == "src/Components/App.razor"
            && !finding.Payload.IsCompilerLanguageSource);
        var json = Assert.Single(filtered);
        Assert.Equal("metadata.source-document", json.Descriptor.Id);
        Assert.Equal("src/System.Text.Json/JsonSerializer.cs", json.Key.IdentityKey);
        Assert.Equal("01A2", json.Payload.Checksum);
        Assert.Equal(SourceDocumentStorage.SourceLink, json.Payload.Storage);
        Assert.Null(json.Ordinal);
        Assert.Empty(missing);
    }

    [Fact]
    public void SourceDocumentComparison_IgnoresPdbRowMovementButReportsContentChanges()
    {
        var oldDocument = new SourceDocument(
            "/_/src/Widget.cs",
            IsEmbedded: false,
            "https://example.test/old/src/Widget.cs",
            [0x01],
            "SHA256",
            DocumentRowId: 1,
            CanonicalPath: "src/Widget.cs");
        var movedRow = oldDocument with { DocumentRowId = 19 };
        var changedContent = movedRow with
        {
            ResolvedUrl = "https://example.test/new/src/Widget.cs",
            Checksum = [0x02],
        };

        var exact = Assert.Single(Pairs(SourceLinkFindings.CompareSourceDocuments(
            [oldDocument],
            [movedRow],
            Subject)));
        var changed = Assert.Single(Pairs(SourceLinkFindings.CompareSourceDocuments(
            [oldDocument],
            [changedContent],
            Subject)));

        Assert.Equal(PairKind.Present, exact.Kind);
        Assert.Equal(PairKind.Changed, changed.Kind);
    }

    [Fact]
    public void SourceDocumentIdentity_UsesMostSpecificSourceLinkMapping()
    {
        const string sourceLink = """
            {
              "documents": {
                "/repo/*": "https://example.test/repository/*",
                "/repo/submodule/*": "https://example.test/submodule/*"
              }
            }
            """;

        Assert.Equal(
            "Widget.cs",
            SourceDocumentPath.Canonicalize("/repo/submodule/Widget.cs", sourceLink));
    }

    [Fact]
    public void SourceDocumentIdentity_MatchesSourceLinkPathsCaseInsensitively()
    {
        const string sourceLink = """
            {
              "documents": {
                "c:/repo/*": "https://example.test/repository/*"
              }
            }
            """;

        Assert.Equal(
            "src/Widget.cs",
            SourceDocumentPath.Canonicalize("C:/Repo/src/Widget.cs", sourceLink));
    }

    [Fact]
    public void SourceDocumentIdentity_AndResolvedUrlUseSameSourceLinkMatch()
    {
        const string sourceLink = """
            {
              "documents": {
                "c:/repo/*": "https://example.test/repository/*",
                "c:/repo/src/generated/*": "https://example.test/generated/*"
              }
            }
            """;

        var resolved = SourceDocumentPath.Resolve(
            "C:/Repo/src/Generated/Widget.cs",
            sourceLink);

        Assert.True(resolved.IsMapped);
        Assert.Equal("Widget.cs", resolved.CanonicalPath);
        Assert.Equal("https://example.test/generated/Widget.cs", resolved.ResolvedUrl);
    }

    [Fact]
    public void SourceDocumentIdentity_EscapesSourceLinkWildcardUrlSuffixBySegment()
    {
        const string sourceLink = """
            {
              "documents": {
                "/repo/*": "https://example.test/repository/*"
              }
            }
            """;

        var resolved = SourceDocumentPath.Resolve(
            "/repo/src/C# Features/My Class.cs",
            sourceLink);

        Assert.Equal("src/C# Features/My Class.cs", resolved.CanonicalPath);
        Assert.Equal(
            "https://example.test/repository/src/C%23%20Features/My%20Class.cs",
            resolved.ResolvedUrl);
    }

    [Fact]
    public void SourceDocumentIdentity_IgnoresMalformedSourceLinkUrlValues()
    {
        const string sourceLink = """
            {
              "documents": {
                "/repo/*": 123,
                "/exact/Widget.cs": true
              }
            }
            """;

        var wildcard = SourceDocumentPath.Resolve("/repo/src/Widget.cs", sourceLink);
        var exact = SourceDocumentPath.Resolve("/exact/Widget.cs", sourceLink);

        // A malformed entry is rejected by the map's owner rather than kept as a key that matches
        // and yields no URL: such an entry outranked valid, less specific entries and swallowed
        // the URLs they would have produced. The document is therefore unmapped, and
        // canonicalization falls back to the document's own path -- a visible degradation of a
        // cosmetic value in exchange for not silently losing a real one.
        Assert.False(wildcard.IsMapped);
        Assert.Equal("/repo/src/Widget.cs", wildcard.CanonicalPath);
        Assert.Null(wildcard.ResolvedUrl);
        Assert.False(exact.IsMapped);
        Assert.Equal("/exact/Widget.cs", exact.CanonicalPath);
        Assert.Null(exact.ResolvedUrl);
    }

    [Fact]
    public void SourceDocumentIdentity_IgnoresMalformedSourceLinkDocumentMappings()
    {
        const string sourceLink = """
            {
              "documents": []
            }
            """;

        var resolved = SourceDocumentPath.Resolve("/repo/src/Widget.cs", sourceLink);

        Assert.False(resolved.IsMapped);
        Assert.Equal("/repo/src/Widget.cs", resolved.CanonicalPath);
        Assert.Null(resolved.ResolvedUrl);
    }

    [Fact]
    public void TypeSourceResolution_RequiresATypeDefinedByTheAssembly()
    {
        using var context = PdbContext.Open(
            typeof(MetadataSourceFindingsTests).Assembly.Location);
        var map = SourceLinkFetch.SourceLinkResolver.Parse(
            """{"documents":{"*":"https://example.test/*"}}""");
        var resolver = new SourceLinkResolver(context, map);

        Assert.NotNull(resolver.ResolveTypeSource(
            typeof(MetadataSourceFindingsTests).FullName!));
        Assert.NotNull(resolver.ResolveTypeSource(nameof(NestedTypeSourceProbe)));
        Assert.Null(resolver.ResolveTypeSource(
            $"Not.This.Assembly.{nameof(MetadataSourceFindingsTests)}"));
    }

    [Fact]
    public void TypeSourceResolution_PreservesPdbDiscoveryOrder()
    {
        using var context = PdbContext.Open(
            typeof(MetadataSourceFindingsTests).Assembly.Location);
        var map = SourceLinkFetch.SourceLinkResolver.Parse(
            """{"documents":{"*":"https://example.test/*"}}""");
        var resolver = new SourceLinkResolver(context, map);

        var source = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
            resolver.ResolveTypeSource(nameof(OrderedTypeSourceProbe)));

        Assert.EndsWith(
            Path.Combine("Generated", "OrderedType.Z.cs"),
            source.SourceFilePath,
            StringComparison.Ordinal);
        var additional = Assert.Single(source.AdditionalSourceFiles);
        Assert.EndsWith(
            Path.Combine("Generated", "OrderedType.A.cs"),
            additional.FilePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPortablePdbDocumentPath_IsMalformedMetadata()
    {
        var exception = Assert.Throws<BadImageFormatException>(
            () => SourceDocumentPath.Canonicalize("", sourceLinkJson: null));

        Assert.Contains("empty path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberSourceComparison_UsesCanonicalMemberIdentity()
    {
        var anchor = new MemberAnchor(
            "Run~1234567890",
            "M:Sample.Widget.Run(System.Int32)",
            "1234567890",
            "Sample.Widget",
            "Run");
        var oldMapping = new MemberSourceInfo(
            anchor,
            MetadataToken: 0x06000001,
            DocumentRowId: 1,
            "/_/src/Widget.cs",
            "src/Widget.cs",
            ResolvedUrl: null,
            StartLine: 10,
            EndLine: 12);
        var renumbered = oldMapping with
        {
            MetadataToken = 0x0600000A,
            DocumentRowId = 8,
        };
        var movedLine = renumbered with { StartLine = 20, EndLine = 22 };

        var exact = Assert.Single(Pairs(SourceLinkFindings.CompareMemberSources(
            [oldMapping],
            [renumbered],
            Subject)));
        var changed = Assert.Single(Pairs(SourceLinkFindings.CompareMemberSources(
            [oldMapping],
            [movedLine],
            Subject)));

        Assert.Equal(
            anchor.CanonicalSignature,
            Assert.IsType<PairFinding<MemberSourceObservation>.Present>(exact.Value).Old.Key.IdentityKey);
        Assert.Equal(PairKind.Present, exact.Kind);
        Assert.Equal(PairKind.Changed, changed.Kind);
    }

    [Fact]
    public void MemberSourceQuery_FiltersByMetadataToken()
    {
        var anchor = new MemberAnchor(
            "Run~1234567890",
            "M:Sample.Widget.Run(System.Int32)",
            "1234567890",
            "Sample.Widget",
            "Run");
        MemberSourceInfo Mapping(int token) => new(
            anchor,
            token,
            DocumentRowId: token,
            "/_/src/Widget.cs",
            "src/Widget.cs",
            ResolvedUrl: null,
            StartLine: 10,
            EndLine: 12);

        var findings = Findings(SourceLinkFindings.InspectMemberSources(
            [Mapping(1), Mapping(2)],
            Subject,
            new MemberSourceQuery(new HashSet<int> { 2 })));

        Assert.Equal(2, Assert.Single(findings).Payload.MetadataToken);
    }

    [Fact]
    public void MemberSourceProducer_UsesFirstVisibleDocumentWhenRootIsOmitted()
    {
        using var source = SourceLinkService.Open(typeof(MetadataSourceFindingsTests).Assembly.Location);
        int token = typeof(MetadataSourceFindingsTests)
            .GetMethod(
                nameof(MultiDocumentMappedMethod),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .MetadataToken;

        var mappings = Findings(SourceLinkFindings.InspectMemberSources(
                source,
                Subject,
                new MemberSourceQuery(new HashSet<int> { token })))
            .Select(static finding => finding.Payload)
            .ToArray();

        // CanonicalPath is separator-agnostic (always forward slashes); OriginalPath
        // carries the OS separator and so fails these suffix checks on Windows (#3018).
        var authored = Assert.Single(mappings, mapping =>
            mapping.CanonicalPath.EndsWith(
                "tests/ILInspector.Metadata.Tests/MetadataSourceFindingsTests.cs",
                StringComparison.Ordinal));
        Assert.False(authored.IsPrimaryDocument);
        var primary = Assert.Single(mappings, static mapping => mapping.IsPrimaryDocument);
        Assert.EndsWith(
            "Generated/MetadataSourceFindings.g.cs",
            primary.CanonicalPath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MemberSourceQuery_ResolvesMultipleRealTokens()
    {
        using var source = SourceLinkService.Open(typeof(MetadataSourceFindingsTests).Assembly.Location);
        int[] tokens =
        [
            typeof(MetadataSourceFindingsTests)
                .GetMethod(
                    nameof(SourceMappedMethod),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .MetadataToken,
            typeof(MetadataSourceFindingsTests)
                .GetMethod(
                    nameof(MultiDocumentMappedMethod),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .MetadataToken,
        ];

        var actualTokens = Findings(SourceLinkFindings.InspectMemberSources(
                source,
                Subject,
                new MemberSourceQuery(tokens.ToHashSet())))
            .Select(static finding => finding.Payload.MetadataToken)
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(tokens.Order(), actualTokens);
    }

    [Fact]
    public void MemberSourceProducer_UsesSameSourceLinkMatchForCanonicalPathAndUrl()
    {
        using var source = SourceLinkService.Open(typeof(MetadataSourceFindingsTests).Assembly.Location);
        int token = typeof(MetadataSourceFindingsTests)
            .GetMethod(
                nameof(SourceMappedMethod),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .MetadataToken;

        var mapping = Assert.Single(Findings(SourceLinkFindings.InspectMemberSources(
                source,
                Subject,
                new MemberSourceQuery(new HashSet<int> { token })))
            .Select(static finding => finding.Payload));
        var expected = SourceDocumentPath.Resolve(mapping.OriginalPath, source.SourceLinkJson);

        Assert.Equal(expected.CanonicalPath, mapping.CanonicalPath);
        Assert.Equal(expected.ResolvedUrl, mapping.ResolvedUrl);
    }

    [Fact]
    public void BuildContextComparisons_PromoteOptionAndReferenceChanges()
    {
        var option = Assert.Single(Pairs(MetadataFindings.CompareCompilationOptions(
            [new CompilationOptionInfo("optimization", "debug")],
            [new CompilationOptionInfo("optimization", "release")],
            Subject)));
        var oldReference = new CompilationReferenceInfo(
            "System.Runtime.dll",
            "",
            CompilationReferenceImageKind.Assembly,
            EmbedInteropTypes: false,
            Timestamp: 1,
            ImageSize: 1024,
            ModuleVersionId: Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var reference = Assert.Single(Pairs(MetadataFindings.CompareCompilationReferences(
            [oldReference],
            [oldReference with
            {
                ModuleVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            }],
            Subject)));

        Assert.Equal(PairKind.Changed, option.Kind);
        Assert.Equal(PairKind.Changed, reference.Kind);
    }

    [Fact]
    public void RealPortablePdb_ProducesAllSourceAndBuildContextFamilies()
    {
        using var source = SourceLinkService.Open(typeof(MetadataSourceFindingsTests).Assembly.Location);

        var documents = Findings(SourceLinkFindings.InspectSourceDocuments(
            source,
            Subject,
            new SourceDocumentQuery(nameof(MetadataSourceFindingsTests))));
        var members = Findings(SourceLinkFindings.InspectMemberSources(source, Subject));
        var options = Findings(MetadataFindings.InspectCompilationOptions(source.Context, Subject));
        var references = Findings(MetadataFindings.InspectCompilationReferences(source.Context, Subject));

        Assert.Contains(documents, finding =>
            finding.Payload.CanonicalPath.EndsWith(
                "tests/ILInspector.Metadata.Tests/MetadataSourceFindingsTests.cs",
                StringComparison.Ordinal));
        Assert.Contains(members, finding =>
            finding.Payload.Anchor.MemberName == nameof(SourceMappedMethod)
            && finding.Payload.StartLine < finding.Payload.EndLine);
        Assert.Contains(options, finding => finding.Payload.Name == "language");
        Assert.Contains(references, finding =>
            finding.Payload.Name == "ILInspector.Metadata.dll"
            && finding.Payload.ImageKind == CompilationReferenceImageKind.Assembly);
    }

    [Fact]
    public void MissingPortablePdb_IsAbsentForEveryPdbProducer()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"metadata-source-findings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Probe.dll");
        File.Copy(typeof(MetadataSourceFindingsTests).Assembly.Location, assemblyPath);

        try
        {
            using var source = SourceLinkService.Open(assemblyPath);

            Assert.IsType<FindingInspection<SourceDocumentObservation>.Absent>(
                SourceLinkFindings.InspectSourceDocuments(source, Subject).Value);
            Assert.IsType<FindingInspection<MemberSourceObservation>.Absent>(
                SourceLinkFindings.InspectMemberSources(source, Subject).Value);
            Assert.IsType<FindingInspection<CompilationOptionInfo>.Absent>(
                MetadataFindings.InspectCompilationOptions(source.Context, Subject).Value);
            Assert.IsType<FindingInspection<CompilationReferenceInfo>.Absent>(
                MetadataFindings.InspectCompilationReferences(source.Context, Subject).Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MalformedPortablePdb_ProducesFailedInspection()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"metadata-source-findings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Probe.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        File.Copy(typeof(MetadataSourceFindingsTests).Assembly.Location, assemblyPath);
        WriteMalformedCompilationReferencesPdb(assemblyPath, pdbPath);

        try
        {
            using var source = SourceLinkService.Open(assemblyPath);

            Assert.True(source.HasPdb);
            var failed = Assert.IsType<FindingInspection<CompilationReferenceInfo>.Failed>(
                MetadataFindings.InspectCompilationReferences(source.Context, Subject).Value);
            Assert.Contains("truncated", failed.Error.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MalformedSourceLinkKind_DoesNotEscapeTheSourceLinkBoundary()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"metadata-source-findings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Probe.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        File.Copy(typeof(MetadataSourceFindingsTests).Assembly.Location, assemblyPath);
        WriteMalformedSourceLinkKindPdb(assemblyPath, pdbPath);

        try
        {
            List<string> log = [];
            using (var source = SourceLinkService.Open(assemblyPath, log.Add))
            {
                Assert.True(source.HasPdb);
                Assert.False(source.HasSourceLink);
                Assert.Contains(
                    "could not be read",
                    source.Provenance().Reason,
                    StringComparison.Ordinal);
                Assert.Contains(
                    log,
                    message => message.Contains(
                        "SourceLink unavailable",
                        StringComparison.Ordinal));
            }

            string lateAssemblyPath = Path.Combine(directory, "Late.dll");
            File.Copy(typeof(MetadataSourceFindingsTests).Assembly.Location, lateAssemblyPath);
            using (var source = SourceLinkService.Open(lateAssemblyPath))
            {
                Assert.False(source.HasPdb);
                source.LoadPdb(pdbPath);
                Assert.True(source.HasPdb);
                Assert.False(source.HasSourceLink);
            }

            string contextAssemblyPath = Path.Combine(directory, "Context.dll");
            File.Copy(typeof(MetadataSourceFindingsTests).Assembly.Location, contextAssemblyPath);
            using var contextSource = SourceLinkService.Open(contextAssemblyPath);
            contextSource.Context.LoadPdbFromFile(pdbPath);
            Assert.False(contextSource.HasSourceLink);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DuplicateCustomDebugInformation_IsRejectedWithoutCopyingItsBlobs()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"metadata-source-findings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Probe.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        File.Copy(typeof(MetadataSourceFindingsTests).Assembly.Location, assemblyPath);
        WriteDuplicateSourceLinkPdb(assemblyPath, pdbPath);

        try
        {
            using (var context = PdbContext.Open(assemblyPath))
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var result = context.ReadModuleCustomDebugInformation(SourceLinkKind);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.Equal(PdbCustomDebugInformationStatus.Duplicate, result.Status);
                Assert.Null(result.Value);
                Assert.True(
                    allocated < LargeBlobLength / 4,
                    $"Duplicate scan allocated {allocated:N0} bytes.");
            }

            using var source = SourceLinkService.Open(assemblyPath);
            Assert.True(source.HasSourceLink);
            Assert.Null(source.SourceLinkJson);
            Assert.Contains(
                "multiple SourceLink",
                source.Provenance().Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DocumentPathEnumeration_DoesNotCopyChecksums()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"metadata-source-findings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Probe.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        File.Copy(typeof(MetadataSourceFindingsTests).Assembly.Location, assemblyPath);
        WriteLargeChecksumPdb(assemblyPath, pdbPath);

        try
        {
            using var context = PdbContext.Open(assemblyPath);

            long beforePaths = GC.GetAllocatedBytesForCurrentThread();
            string path = Assert.Single(context.EnumeratePdbDocumentPaths());
            long pathBytes = GC.GetAllocatedBytesForCurrentThread() - beforePaths;

            long beforeDocuments = GC.GetAllocatedBytesForCurrentThread();
            var document = Assert.Single(context.EnumeratePdbDocuments());
            long documentBytes =
                GC.GetAllocatedBytesForCurrentThread() - beforeDocuments;

            Assert.Equal("/_/src/Widget.cs", path);
            Assert.Equal(LargeBlobLength, document.Checksum?.Length);
            Assert.True(
                pathBytes < LargeBlobLength / 4,
                $"Path-only enumeration allocated {pathBytes:N0} bytes.");
            Assert.True(
                documentBytes >= LargeBlobLength,
                $"Full document enumeration allocated only {documentBytes:N0} bytes.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void WriteMalformedCompilationReferencesPdb(string assemblyPath, string pdbPath)
    {
        WritePortablePdb(assemblyPath, pdbPath, pdbMetadata =>
        {
            var malformedReference = new BlobBuilder();
            malformedReference.WriteUTF8("Broken.dll");
            malformedReference.WriteByte(0);
            malformedReference.WriteByte(0);
            pdbMetadata.AddCustomDebugInformation(
                EntityHandle.ModuleDefinition,
                pdbMetadata.GetOrAddGuid(
                    new Guid("7E4D4708-096E-4C5C-AEDA-CB10BA6A740D")),
                pdbMetadata.GetOrAddBlob(malformedReference));
        });
    }

    static void WriteMalformedSourceLinkKindPdb(string assemblyPath, string pdbPath)
    {
        byte[] image = WritePortablePdb(assemblyPath, pdbPath, pdbMetadata =>
        {
            var blob = new BlobBuilder();
            blob.WriteUTF8("""{"documents":{"/_/*":"https://example.test/*"}}""");
            pdbMetadata.AddCustomDebugInformation(
                EntityHandle.ModuleDefinition,
                pdbMetadata.GetOrAddGuid(SourceLinkKind),
                pdbMetadata.GetOrAddBlob(blob));
        });
        PatchStreamSize(image, "#GUID", 0);
        File.WriteAllBytes(pdbPath, image);
    }

    static void WriteDuplicateSourceLinkPdb(string assemblyPath, string pdbPath)
    {
        WritePortablePdb(assemblyPath, pdbPath, pdbMetadata =>
        {
            var blob = new BlobBuilder();
            blob.WriteBytes(new byte[LargeBlobLength]);
            var value = pdbMetadata.GetOrAddBlob(blob);
            var kind = pdbMetadata.GetOrAddGuid(SourceLinkKind);
            pdbMetadata.AddCustomDebugInformation(
                EntityHandle.ModuleDefinition,
                kind,
                value);
            pdbMetadata.AddCustomDebugInformation(
                EntityHandle.ModuleDefinition,
                kind,
                value);
        });
    }

    static void WriteLargeChecksumPdb(string assemblyPath, string pdbPath)
    {
        WritePortablePdb(assemblyPath, pdbPath, pdbMetadata =>
        {
            var checksum = new BlobBuilder();
            checksum.WriteBytes(new byte[LargeBlobLength]);
            pdbMetadata.AddDocument(
                pdbMetadata.GetOrAddDocumentName("/_/src/Widget.cs"),
                pdbMetadata.GetOrAddGuid(
                    new Guid("8829D00F-11B8-4213-878B-770E8597AC16")),
                pdbMetadata.GetOrAddBlob(checksum),
                default);
        });
    }

    static byte[] WritePortablePdb(
        string assemblyPath,
        string pdbPath,
        Action<MetadataBuilder> addRows)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var codeView = pe.ReadDebugDirectory()
            .Where(static entry => entry.Type == DebugDirectoryEntryType.CodeView)
            .Select(pe.ReadCodeViewDebugDirectoryData)
            .First();

        int[] rowCounts = new int[64];
        foreach (var table in Enum.GetValues<TableIndex>())
        {
            int index = (int)table;
            if ((uint)index < (uint)rowCounts.Length)
                rowCounts[index] = metadata.GetTableRowCount(table);
        }

        var pdbMetadata = new MetadataBuilder();
        addRows(pdbMetadata);
        var builder = new PortablePdbBuilder(
            pdbMetadata,
            ImmutableArray.Create(rowCounts),
            default,
            _ => new BlobContentId(codeView.Guid, stamp: 0));
        var image = new BlobBuilder();
        builder.Serialize(image);
        byte[] bytes = image.ToArray();
        File.WriteAllBytes(pdbPath, bytes);
        return bytes;
    }

    static void PatchStreamSize(byte[] image, string streamName, uint newSize)
    {
        int position = 0;
        int versionLength = BitConverter.ToInt32(image, position + 12);
        position += 16 + versionLength;
        position += 2;
        ushort streams = BitConverter.ToUInt16(image, position);
        position += 2;
        for (int i = 0; i < streams; i++)
        {
            int sizeOffset = position + 4;
            int nameStart = position + 8;
            int nameEnd = nameStart;
            while (image[nameEnd] != 0)
                nameEnd++;
            string name = System.Text.Encoding.ASCII.GetString(
                image,
                nameStart,
                nameEnd - nameStart);
            int nameLength = ((nameEnd - nameStart) + 4) & ~3;
            if (name == streamName)
            {
                BitConverter.GetBytes(newSize).CopyTo(image, sizeOffset);
                return;
            }

            position = nameStart + nameLength;
        }

        throw new InvalidOperationException($"Metadata stream '{streamName}' was not found.");
    }

    static ImmutableArray<Finding<T>> Findings<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings
            : throw new Xunit.Sdk.XunitException("Expected a complete PDB inspection.");

    static ImmutableArray<PairFinding<T>> Pairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison is FindingComparison<T>.Complete complete
            ? complete.Pairs
            : throw new Xunit.Sdk.XunitException($"Expected a complete comparison: {comparison.Failure}");
}
