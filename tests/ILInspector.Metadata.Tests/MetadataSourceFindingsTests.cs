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
    static readonly FindingSubject Subject = new("assembly", "Test assembly");

    static int SourceMappedMethod(int value)
    {
        int adjusted = value + 1;
        return adjusted * 2;
    }

#line 100 "Generated/MetadataSourceFindings.g.cs"
    static int MultiDocumentMappedMethod(int value)
    {
        int adjusted = value + 1;
#line default
        return adjusted * 2;
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

        var all = Findings(MetadataFindings.InspectSourceDocuments(documents, Subject));
        var filtered = Findings(MetadataFindings.InspectSourceDocuments(
            documents,
            Subject,
            new SourceDocumentQuery("text.json")));
        var missing = Findings(MetadataFindings.InspectSourceDocuments(
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

        var exact = Assert.Single(Pairs(MetadataFindings.CompareSourceDocuments(
            [oldDocument],
            [movedRow],
            Subject)));
        var changed = Assert.Single(Pairs(MetadataFindings.CompareSourceDocuments(
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

        Assert.True(wildcard.IsMapped);
        Assert.Equal("src/Widget.cs", wildcard.CanonicalPath);
        Assert.Null(wildcard.ResolvedUrl);
        Assert.True(exact.IsMapped);
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

        var exact = Assert.Single(Pairs(MetadataFindings.CompareMemberSources(
            [oldMapping],
            [renumbered],
            Subject)));
        var changed = Assert.Single(Pairs(MetadataFindings.CompareMemberSources(
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

        var findings = Findings(MetadataFindings.InspectMemberSources(
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

        var mappings = Findings(MetadataFindings.InspectMemberSources(
                source,
                Subject,
                new MemberSourceQuery(new HashSet<int> { token })))
            .Select(static finding => finding.Payload)
            .ToArray();

        var authored = Assert.Single(mappings, mapping =>
            mapping.OriginalPath.EndsWith(
                "tests/ILInspector.Metadata.Tests/MetadataSourceFindingsTests.cs",
                StringComparison.Ordinal));
        Assert.False(authored.IsPrimaryDocument);
        var primary = Assert.Single(mappings, static mapping => mapping.IsPrimaryDocument);
        Assert.EndsWith(
            "Generated/MetadataSourceFindings.g.cs",
            primary.OriginalPath,
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

        var actualTokens = Findings(MetadataFindings.InspectMemberSources(
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

        var mapping = Assert.Single(Findings(MetadataFindings.InspectMemberSources(
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

        var documents = Findings(MetadataFindings.InspectSourceDocuments(
            source,
            Subject,
            new SourceDocumentQuery(nameof(MetadataSourceFindingsTests))));
        var members = Findings(MetadataFindings.InspectMemberSources(source, Subject));
        var options = Findings(MetadataFindings.InspectCompilationOptions(source, Subject));
        var references = Findings(MetadataFindings.InspectCompilationReferences(source, Subject));

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
                MetadataFindings.InspectSourceDocuments(source, Subject).Value);
            Assert.IsType<FindingInspection<MemberSourceObservation>.Absent>(
                MetadataFindings.InspectMemberSources(source, Subject).Value);
            Assert.IsType<FindingInspection<CompilationOptionInfo>.Absent>(
                MetadataFindings.InspectCompilationOptions(source, Subject).Value);
            Assert.IsType<FindingInspection<CompilationReferenceInfo>.Absent>(
                MetadataFindings.InspectCompilationReferences(source, Subject).Value);
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
                MetadataFindings.InspectCompilationReferences(source, Subject).Value);
            Assert.Contains("truncated", failed.Error.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void WriteMalformedCompilationReferencesPdb(string assemblyPath, string pdbPath)
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
        var malformedReference = new BlobBuilder();
        malformedReference.WriteUTF8("Broken.dll");
        malformedReference.WriteByte(0);
        malformedReference.WriteByte(0);
        pdbMetadata.AddCustomDebugInformation(
            EntityHandle.ModuleDefinition,
            pdbMetadata.GetOrAddGuid(new Guid("7E4D4708-096E-4C5C-AEDA-CB10BA6A740D")),
            pdbMetadata.GetOrAddBlob(malformedReference));

        var builder = new PortablePdbBuilder(
            pdbMetadata,
            ImmutableArray.Create(rowCounts),
            default,
            _ => new BlobContentId(codeView.Guid, stamp: 0));
        var image = new BlobBuilder();
        builder.Serialize(image);
        File.WriteAllBytes(pdbPath, image.ToArray());
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
