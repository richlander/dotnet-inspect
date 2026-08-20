using System.Text;
using DotnetInspector.Services;
using InertText;

namespace DotnetInspector.Services.Tests;

public sealed class PackageContentAuditTests
{
    [Theory]
    [InlineData(".cjs")]
    [InlineData(".cshtml")]
    [InlineData(".css")]
    [InlineData(".html")]
    [InlineData(".js")]
    [InlineData(".jsx")]
    [InlineData(".mjs")]
    [InlineData(".razor")]
    [InlineData(".scss")]
    [InlineData(".svg")]
    [InlineData(".ts")]
    [InlineData(".tsx")]
    public void CommonWebTextFiles_AreAuditedOutsideContentDirectories(string extension)
    {
        string root = CreateRoot();
        try
        {
            string relativePath = $"wwwroot/app{extension}";
            Write(root, relativePath, "prefix\u202Esuffix");

            PackageContentAuditResult result = PackageContentAudit.Scan(root, [relativePath]);

            Assert.True(result.Complete);
            Assert.Equal(1, result.EligibleFiles);
            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(PackageContentFindingKind.NonGraphicText, finding.Kind);
            Assert.Contains("\\u202E", finding.EncodedText.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void KnownBinaryOutsideContentDirectories_IsNotTreatedAsText()
    {
        string root = CreateRoot();
        try
        {
            WriteBytes(root, "wwwroot/image.png", [0x1B, 0x07]);

            PackageContentAuditResult result =
                PackageContentAudit.Scan(root, ["wwwroot/image.png"]);

            Assert.True(result.Complete);
            Assert.Equal(0, result.EligibleFiles);
            Assert.Empty(result.Findings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../", true)]
    [InlineData("https://example.test/repository-a/../repository-b/file.cs", true)]
    [InlineData("prefix../suffix", true)]
    [InlineData("https://example.test/repository-a/..", false)]
    [InlineData("https://example.test/repository-a/%2E%2E/repository-b/file.cs", false)]
    [InlineData("https://example.test/repository-a/.. /repository-b/file.cs", false)]
    [InlineData(null, false)]
    public void ParentPathRule_IsLiteralAndReviewOriented(string? value, bool expected)
    {
        Assert.Equal(expected, PackageContentAudit.ContainsParentPathReference(value));
    }

    [Fact]
    public void AdversarialPackageShape_ReportsEncodedTextAndRestoreConfiguration()
    {
        const string HostileSource = "https://api.\u202Etegun\u202C.org/v3/index.json";
        string root = CreateRoot();
        try
        {
            Write(root, "README.md", HostileSource + "\n");
            Write(root, "content/INSTRUCTIONS.md", "setup\n\u001B]52;c;WW91IHRvb2sgYSB3cm9uZyB0dXJuLgo=\u0007\n");
            Write(
                root,
                "content/nuget.config",
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="nuget.org" value="{{HostileSource}}" />
                  </packageSources>
                </configuration>
                """);
            WriteUtf8Bom(root, "Package.nuspec", "<package />");
            WriteBytes(root, "lib/net11.0/Package.dll", [0x1B, 0x07]);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                [
                    "README.md",
                    "content/INSTRUCTIONS.md",
                    "content/nuget.config",
                    "Package.nuspec",
                    "lib/net11.0/Package.dll",
                ]);

            Assert.True(result.Complete);
            Assert.Equal(4, result.EligibleFiles);
            Assert.Equal(4, result.ScannedFiles);
            Assert.Equal(5, result.Findings.Count);

            var textFindings = result.Findings
                .Where(finding => finding.Kind == PackageContentFindingKind.NonGraphicText)
                .ToList();
            Assert.Equal(3, textFindings.Count);
            Assert.Contains(textFindings, finding => finding.Path == "README.md"
                && finding.Concerns == TextConcern.Format
                && finding.EncodedText.ToString().Contains("\\u202E", StringComparison.Ordinal));
            Assert.Contains(textFindings, finding => finding.Path == "content/INSTRUCTIONS.md"
                && finding.Concerns == TextConcern.Control
                && finding.EncodedText.ToString().Contains("\\^[", StringComparison.Ordinal)
                && finding.EncodedText.ToString().Contains("\\^G", StringComparison.Ordinal));
            Assert.Contains(result.Findings, finding =>
                finding.Kind == PackageContentFindingKind.RestoreSourcesCleared);
            Assert.Contains(result.Findings, finding =>
                finding.Kind == PackageContentFindingKind.PackageSourceDeclared);
            Assert.DoesNotContain(result.Findings, finding => finding.Path == "Package.nuspec");
            Assert.DoesNotContain(result.Findings, finding => finding.Path.EndsWith(".dll", StringComparison.Ordinal));

            foreach (PackageContentAuditFinding finding in result.Findings)
            {
                string encoded = finding.EncodedText.ToString();
                Assert.DoesNotContain('\u202E', encoded);
                Assert.DoesNotContain('\u202C', encoded);
                Assert.DoesNotContain('\u001B', encoded);
                Assert.DoesNotContain('\u0007', encoded);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OneLineWithSeveralConcernKinds_ProducesOneFinding()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "README.md", "prefix\u202Esuffix\u001B");

            PackageContentAuditResult result = PackageContentAudit.Scan(root, ["README.md"]);

            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(PackageContentFindingKind.NonGraphicText, finding.Kind);
            Assert.Equal(TextConcern.Format | TextConcern.Control, finding.Concerns);
            Assert.Equal("prefix\\u202Esuffix\\^[", finding.EncodedText.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidTextEncoding_RemainsVisibleAndMarksAuditIncomplete()
    {
        string root = CreateRoot();
        try
        {
            WriteBytes(root, "README.md", [0xC3, 0x28]);

            PackageContentAuditResult result = PackageContentAudit.Scan(root, ["README.md"]);

            Assert.False(result.Complete);
            Assert.Equal(1, result.EligibleFiles);
            Assert.Equal(0, result.ScannedFiles);
            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(PackageContentFindingKind.InvalidTextEncoding, finding.Kind);
            Assert.Equal(TextConcern.None, finding.Concerns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TruncatedPdb_RemainsVisibleAndMarksAuditIncomplete()
    {
        string root = CreateRoot();
        try
        {
            WriteBytes(root, "symbols/broken.pdb", [0x42, 0x53, 0x4A]);

            PackageContentAuditResult result =
                PackageContentAudit.Scan(root, ["symbols/broken.pdb"]);

            Assert.False(result.Complete);
            Assert.Equal(0, result.ScannedSourceLinkMaps);
            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(PackageContentFindingKind.InvalidSourceLinkMap, finding.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Microsoft C/C++ MSF 7.00\r\n\u001ADS\0\0\0")]
    [InlineData("Microsoft C/C++ program database 2.00\r\n\u001AJG\0\0")]
    public void WindowsPdb_IsRecognizedAsUnsupportedRatherThanMalformed(string signature)
    {
        string root = CreateRoot();
        try
        {
            WriteBytes(
                root,
                "symbols/windows.pdb",
                Encoding.ASCII.GetBytes(signature));

            PackageContentAuditResult result =
                PackageContentAudit.Scan(root, ["symbols/windows.pdb"]);

            Assert.True(result.Complete);
            Assert.Equal(0, result.ScannedSourceLinkMaps);
            Assert.Empty(result.Findings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OversizedCandidate_RemainsVisibleWithoutBeingRead()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "README.md");
            using (FileStream stream = File.Create(path))
                stream.SetLength(PackageContentAudit.MaxFileBytes + 1L);

            PackageContentAuditResult result = PackageContentAudit.Scan(root, ["README.md"]);

            Assert.False(result.Complete);
            Assert.Equal(0, result.ScannedBytes);
            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(PackageContentFindingKind.ScanLimit, finding.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LongHostileLine_IsBoundedAroundTheFirstEncoding()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "README.md", new string('a', 800) + '\u202E' + new string('b', 800));

            PackageContentAuditResult result = PackageContentAudit.Scan(root, ["README.md"]);

            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.True(finding.EncodedText.IsTruncated);
            Assert.Contains("\\u202E", finding.EncodedText.ToString(), StringComparison.Ordinal);
            Assert.True(finding.EncodedText.Length <= 514);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CaseDistinctPackagePaths_AreNotDeduplicated()
    {
        string[] paths = PackageContentAudit.NormalizePackagePaths(
            ["content/NOTES.md", "content/notes.md"]);

        Assert.Equal(["content/NOTES.md", "content/notes.md"], paths);
    }

    [Fact]
    public void CandidatePathLimit_BoundsRepeatedInputBeforeMaterialization()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "README.md", string.Empty);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                Enumerable.Repeat(
                    "README.md",
                    PackageContentAudit.MaxCandidatePaths + 1));

            Assert.False(result.Complete);
            Assert.Equal(1, result.EligibleFiles);
            Assert.Contains(
                result.Findings,
                finding =>
                    finding.Path == "<package>"
                    && finding.Kind == PackageContentFindingKind.ScanLimit);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceLinkCarrierLimit_BoundsZeroByteWork()
    {
        string root = CreateRoot();
        try
        {
            WriteBytes(root, "symbols/broken.pdb", [0]);
            string[] paths =
            [
                .. Enumerable.Range(
                        0,
                        PackageContentAudit.MaxSourceLinkCarriers + 1)
                    .Select(index => $"lib/net11.0/carrier-{index:D3}.dll"),
                "symbols/broken.pdb",
            ];

            PackageContentAuditResult result =
                PackageContentAudit.Scan(root, paths);

            Assert.False(result.Complete);
            Assert.Contains(
                result.Findings,
                finding =>
                    finding.Kind == PackageContentFindingKind.ScanLimit
                    && finding.EncodedText.ToString().Contains(
                        "carrier limit",
                        StringComparison.Ordinal));
            Assert.Contains(
                result.Findings,
                finding =>
                    finding.Path == "symbols/broken.pdb"
                    && finding.Kind
                    == PackageContentFindingKind.InvalidSourceLinkMap);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TextFileLimit_BoundsZeroByteReads()
    {
        string root = CreateRoot();
        try
        {
            Write(root, "content/README.md", string.Empty);
            string[] paths =
            [
                .. Enumerable.Range(
                        0,
                        PackageContentAudit.MaxTextFiles + 1)
                    .Select(index =>
                        $"content/directory-{index}/../README.md"),
            ];

            PackageContentAuditResult result =
                PackageContentAudit.Scan(root, paths);

            Assert.False(result.Complete);
            Assert.Equal(
                PackageContentAudit.MaxTextFiles,
                result.EligibleFiles);
            Assert.Equal(
                PackageContentAudit.MaxTextFiles,
                result.ScannedFiles);
            Assert.Contains(
                result.Findings,
                finding =>
                    finding.Kind == PackageContentFindingKind.ScanLimit
                    && finding.EncodedText.ToString().Contains(
                        "text-file limit",
                        StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindingLimit_StopsPathologicalLineCardinalityAndMarksPartial()
    {
        string root = CreateRoot();
        try
        {
            Write(
                root,
                "README.md",
                string.Join('\n', Enumerable.Repeat("\u001B", PackageContentAudit.MaxFindings * 2)));

            PackageContentAuditResult result = PackageContentAudit.Scan(root, ["README.md"]);

            Assert.False(result.Complete);
            Assert.Equal(PackageContentAudit.MaxFindings, result.Findings.Count);
            Assert.Equal(
                PackageContentFindingKind.ScanLimit,
                result.Findings[^1].Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MinifiedNuGetConfiguration_BoundsEverySemanticRow()
    {
        string root = CreateRoot();
        try
        {
            string sources = string.Concat(
                Enumerable.Range(0, 100).Select(index =>
                    $"<add key=\"source-{index}\" value=\"https://example.test/{new string('a', 600)}\"/>"));
            Write(
                root,
                "build/nuget.config",
                $"<configuration><packageSources><clear/>{sources}</packageSources></configuration>");

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config"]);

            Assert.True(result.Complete);
            Assert.Equal(101, result.Findings.Count);
            Assert.All(
                result.Findings,
                finding => Assert.True(finding.EncodedText.Length <= 514));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NestedNuGetConfiguration_EvidenceIsElementLocal()
    {
        string root = CreateRoot();
        try
        {
            const int Depth = 16;
            string content =
                "<configuration><packageSources><add>"
                + string.Concat(Enumerable.Repeat("<packageSources><add>", Depth))
                + "deep-marker"
                + string.Concat(Enumerable.Repeat("</add></packageSources>", Depth))
                + "</add></packageSources></configuration>";
            Write(root, "build/nuget.config", content);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config"]);

            Assert.True(result.Complete);
            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal("<add />", finding.EncodedText.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NuGetConfiguration_DeepXmlReportsScanLimit()
    {
        string root = CreateRoot();
        try
        {
            const int Depth = 128;
            string content =
                "<configuration>"
                + string.Concat(Enumerable.Repeat("<wrapper>", Depth))
                + string.Concat(Enumerable.Repeat("</wrapper>", Depth))
                + "</configuration>";
            Write(root, "build/nuget.config", content);
            Write(
                root,
                "zzz/nuget.config",
                """
                <configuration>
                  <packageSources>
                    <add key="later" value="https://later.example/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config", "zzz/nuget.config"]);

            Assert.False(result.Complete);
            Assert.Equal(2, result.EligibleFiles);
            Assert.Equal(2, result.ScannedFiles);
            PackageContentAuditFinding limit = Assert.Single(
                result.Findings,
                finding => finding.Kind == PackageContentFindingKind.ScanLimit);
            Assert.Contains(
                "XML depth limit",
                limit.EncodedText.ToString(),
                StringComparison.Ordinal);
            Assert.Contains(
                result.Findings,
                finding => finding.Kind == PackageContentFindingKind.PackageSourceDeclared
                    && finding.Path == "zzz/nuget.config");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NuGetConfiguration_EscapesAttributeStructure()
    {
        string root = CreateRoot();
        try
        {
            Write(
                root,
                "build/nuget.config",
                """
                <configuration>
                  <packageSources>
                    <add key="a&quot; value=&quot;https://spoof.example" />
                    <add key="a" value="https://spoof.example" />
                  </packageSources>
                </configuration>
                """);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config"]);

            Assert.True(result.Complete);
            Assert.Equal(2, result.Findings.Count);
            Assert.NotEqual(
                result.Findings[0].EncodedText.ToString(),
                result.Findings[1].EncodedText.ToString());
            Assert.Contains(
                "&quot;",
                result.Findings[0].EncodedText.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "&quot;",
                result.Findings[1].EncodedText.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NuGetConfiguration_NamespaceAttributesCannotForgeSemanticEvidence()
    {
        string root = CreateRoot();
        try
        {
            Write(
                root,
                "build/nuget.config",
                """
                <configuration>
                  <packageSources>
                    <add xmlns:key="corp" xmlns:value="https://evil.example/v3/index.json" />
                    <add key="corp" value="https://evil.example/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config"]);

            Assert.True(result.Complete);
            Assert.Collection(
                result.Findings,
                finding => Assert.Equal("<add />", finding.EncodedText.ToString()),
                finding => Assert.Equal(
                    "<add key=\"corp\" value=\"https://evil.example/v3/index.json\" />",
                    finding.EncodedText.ToString()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NuGetConfiguration_QualifiedElementsMatchNuGetSemantics()
    {
        string root = CreateRoot();
        try
        {
            Write(
                root,
                "build/nuget.config",
                """
                <configuration xmlns="urn:default" xmlns:f="urn:foreign">
                  <packageSources>
                    <clear />
                    <add key="default" value="https://default.example/v3/index.json" />
                  </packageSources>
                  <f:packageSources>
                    <f:add key="prefixed" value="https://prefixed.example/v3/index.json" />
                  </f:packageSources>
                </configuration>
                """);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config"]);

            Assert.True(result.Complete);
            Assert.Collection(
                result.Findings,
                finding => Assert.Equal("<clear />", finding.EncodedText.ToString()),
                finding => Assert.Equal(
                    "<add key=\"default\" value=\"https://default.example/v3/index.json\" />",
                    finding.EncodedText.ToString()),
                finding => Assert.Equal(
                    "<add key=\"prefixed\" value=\"https://prefixed.example/v3/index.json\" />",
                    finding.EncodedText.ToString()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NuGetConfiguration_IgnoresNestedAndMiscasedSections()
    {
        string root = CreateRoot();
        try
        {
            Write(
                root,
                "build/nuget.config",
                """
                <Configuration>
                  <packageSources>
                    <add key="active" value="https://active.example/v3/index.json" />
                  </packageSources>
                  <wrapper>
                    <packageSources>
                      <clear />
                      <add key="nested" value="https://nested.example/v3/index.json" />
                    </packageSources>
                  </wrapper>
                  <PackageSources>
                    <add key="miscased" value="https://miscased.example/v3/index.json" />
                  </PackageSources>
                </Configuration>
                """);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config"]);

            Assert.True(result.Complete);
            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(
                "<add key=\"active\" value=\"https://active.example/v3/index.json\" />",
                finding.EncodedText.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NuGetConfiguration_TruncationKeepsSurrogatePairsWhole()
    {
        string root = CreateRoot();
        try
        {
            string value = new string('a', 498) + "\U0001F600" + "b";
            Write(
                root,
                "build/nuget.config",
                $"<configuration><packageSources><add value=\"{value}\" /></packageSources></configuration>");

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["build/nuget.config"]);

            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(TextConcern.None, finding.EncodedText.Concerns);
            Assert.StartsWith("<add", finding.EncodedText.ToString(), StringComparison.Ordinal);
            Assert.EndsWith("…", finding.EncodedText.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LongHostileLine_AfterLiteralBackslashesKeepsTheConcernInEvidence()
    {
        string root = CreateRoot();
        try
        {
            Write(
                root,
                "README.md",
                @"C:\build\obj\" + new string('a', 900) + "\u202Ehidden");

            PackageContentAuditResult result = PackageContentAudit.Scan(root, ["README.md"]);

            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Contains(@"\u202E", finding.EncodedText.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceLinkEvidence_UsesSingleFieldControlPolicy()
    {
        InertString encoded = PackageContentAudit.EncodeSourceLinkEvidence(
            "document\nkey => https://example.test/\tpath");

        Assert.Equal(TextConcern.Control, encoded.Concerns);
        Assert.Contains(@"\^J", encoded.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\^I", encoded.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidPortablePdb_RemainsVisibleAndMarksAuditIncomplete()
    {
        string root = CreateRoot();
        try
        {
            WriteBytes(root, "lib/net11.0/broken.pdb", [.. "BSJB"u8, 0, 0, 0, 0]);

            PackageContentAuditResult result = PackageContentAudit.Scan(
                root,
                ["lib/net11.0/broken.pdb"]);

            Assert.False(result.Complete);
            PackageContentAuditFinding finding = Assert.Single(result.Findings);
            Assert.Equal(PackageContentFindingKind.InvalidSourceLinkMap, finding.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"package-content-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relativePath, string content)
        => WriteBytes(root, relativePath, Encoding.UTF8.GetBytes(content));

    private static void WriteUtf8Bom(string root, string relativePath, string content)
        => WriteBytes(
            root,
            relativePath,
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)]);

    private static void WriteBytes(string root, string relativePath, byte[] content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
