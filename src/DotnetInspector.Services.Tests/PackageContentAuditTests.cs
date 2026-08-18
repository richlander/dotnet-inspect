using System.Text;
using DotnetInspector.Services;
using InertText;

namespace DotnetInspector.Services.Tests;

public sealed class PackageContentAuditTests
{
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
