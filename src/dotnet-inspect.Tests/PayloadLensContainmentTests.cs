using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins visually encoded payload stdout, exact explicit file export, and the
/// companion-section stream split (issue #3319).
/// </summary>
/// <remarks>
/// Every rendered surface in this tool contains what it shows: line terminators
/// folded, rendering hazards rewritten as visible <c>\uXXXX</c>. A reviewer
/// pointed at the README payload emitting raw VT, ESC, bidi, and LS to
/// stdout and read it as the containment work having missed a channel.
///
/// README and <c>--content</c> payloads are visually encoded when written to
/// stdout. Callers that need exact bytes make that intent explicit with
/// <c>--out</c>. The contract lives in <c>docs/design/output-shapes.md</c>, and
/// this is the gate it names:
///
/// <list type="number">
/// <item><description>
/// stdout carries an invertibly encoded payload with no live rendering hazard,
/// </description></item>
/// <item><description>
/// explicit file export carries the payload <b>byte for byte</b>, and companion
/// headings, tables, and diagnostics go to stderr, contained.
/// </description></item>
/// </list>
///
/// <see cref="ReadmeLens_PutsNoToolFramingOnEncodedStdout"/> runs the hazard
/// payload with <c>--info</c> specifically -- the mode that
/// does put a real <c># Info</c> table in the picture -- and asserts the table
/// landed on the other stream.
/// </remarks>
public class PayloadLensContainmentTests : IDisposable
{
    private const string Bidi = "\u202E";
    private const string Escape = "\u001B";
    private const string LineSeparator = "\u2028";
    private const string ZeroWidthSpace = "\u200B";
    private readonly string _cacheDirectory =
        Directory.CreateTempSubdirectory("payload-containment-cache-").FullName;
    private bool _deleteCacheOnDispose = true;

    /// <summary>
    /// The README payload every test here plants.
    /// </summary>
    /// <remarks>
    /// Each hazard is followed by a distinct marker so a partial or reordered
    /// emission is a failure rather than a coincidence.
    /// </remarks>
    /// <summary>
    /// How a caller asks for the README payload.
    /// </summary>
    /// <remarks>
    /// This was the <c>--readme</c> lens when these tests were written. #3448
    /// retired that lens on the grounds that printing a document is a
    /// projection over a declared section rather than a lens of its own, so
    /// the spelling is now a section selection plus <c>--print</c>. The
    /// contract these tests pin is about the payload stream, not the flag, so
    /// the spelling lives in one place and the assertions did not change.
    /// </remarks>
    private static readonly string[] ReadmeLens = ["-S", "Package README file", "--print"];

    private const string HostileReadme =
        "intro" + Bidi + "MARKERBIDI\n"
        + "line" + Escape + "MARKERESC\n"
        + "line" + LineSeparator + "MARKERLS\n";

    /// <summary>
    /// Keeps payload subprocesses off the operator's real cache.
    /// </summary>
    /// <remarks>
    /// This independently gates the cache environment on this class's
    /// launcher; the diagnostic containment tests use a different launcher.
    /// </remarks>
    [Fact]
    public void ChildCli_UsesThePerTestCache()
    {
        var (output, error) = RunCliCore(["cache", "--json", "-T:q"]);

        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(
            _cacheDirectory,
            document.RootElement.GetProperty("location").GetString());
    }

    [Fact]
    public void ChildCli_LineLimitIgnoresRowsLiteralAfterOptionTerminator()
    {
        var (output, error) = RunCliCore(
            ["package", "--help", "-n1", "--", "--rows"]);

        Assert.Empty(error);
        Assert.Single(
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void ReadmeLens_VisuallyEncodesThePayloadOnStdout()
    {
        using var package = HostilePackage.Create();

        var (output, _) = RunCli([package.Path, ..ReadmeLens]);

        AssertIsEncodedPayload(output);
    }

    private static void AssertIsEncodedPayload(string output)
    {
        HostileOutputAssert.MarkersRendered(
            output,
            "readme-stdout",
            "MARKERBIDI",
            "MARKERESC",
            "MARKERLS");
        HostileOutputAssert.NoRenderingHazard(output, "readme-stdout");
        Assert.Contains(@"\u202E", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeLens_ExplicitFileExportReproducesThePayloadByteForByte()
    {
        using var package = HostilePackage.Create();
        string outputPath = System.IO.Path.Combine(
            package.Directory,
            "README.export.md");

        var (output, error) = RunCli(
            [package.Path, ..ReadmeLens, "--out", outputPath]);

        Assert.Empty(output);
        Assert.Empty(error);
        Assert.Equal(HostileReadme, File.ReadAllText(outputPath));
    }

    [Fact]
    public void ReadmeLens_ScopedFileExportVisuallyEncodesThePayload()
    {
        using var package = HostilePackage.Create();
        string outputPath = System.IO.Path.Combine(
            package.Directory,
            "README.body.md");

        var (output, error) = RunCli(
            [
                package.Path,
                ..ReadmeLens,
                "--body",
                "--bare",
                "--out",
                outputPath,
            ]);

        Assert.Empty(output);
        Assert.Empty(error);
        AssertIsEncodedPayload(File.ReadAllText(outputPath));
    }

    [Fact]
    public void ContentLens_ScopedFileExportVisuallyEncodesThePayload()
    {
        using var package = HostilePackage.Create();
        string outputPath = System.IO.Path.Combine(
            package.Directory,
            "README.content-body.md");

        var (output, error) = RunCli(
            [
                package.Path,
                "--content",
                "--path",
                "README.md",
                "--body",
                "--out",
                outputPath,
            ]);

        Assert.Empty(output);
        Assert.Empty(error);
        AssertIsEncodedPayload(File.ReadAllText(outputPath));
    }

    /// <summary>
    /// Companion tool-authored framing remains on the contained diagnostic stream.
    /// </summary>
    [Fact]
    public void ReadmeLens_PutsNoToolFramingOnEncodedStdout()
    {
        using var package = HostilePackage.Create();

        // --info is the mode that composes a real section alongside the lens,
        // so it is the one that can put framing and payload on one stream.
        var (output, error) = RunCli([package.Path, ..ReadmeLens, "--info"]);

        AssertIsEncodedPayload(output);

        // The framing exists -- otherwise this test would pass by the section
        // having been silently dropped, which is a different bug that would
        // look identical here.
        Assert.Contains("# Info", error, StringComparison.Ordinal);

        // ... and it is on the other stream. No line of stdout is a heading, a
        // table row, or a diagnostic.
        foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();
            Assert.False(
                trimmed.StartsWith("# ", StringComparison.Ordinal)
                    || trimmed.StartsWith("| ", StringComparison.Ordinal)
                    || trimmed.StartsWith("Error: ", StringComparison.Ordinal)
                    || trimmed.StartsWith("Warning: ", StringComparison.Ordinal)
                    || trimmed.StartsWith("Note: ", StringComparison.Ordinal),
                $"stdout is a payload stream and must carry no tool framing, but got: {line}");
        }

        // The tool's own stream stays contained even while the payload beside
        // it does not, which is the whole basis for the split.
        HostileOutputAssert.NoRenderingHazard(error, "readme-lens-info");
    }

    /// <summary>
    /// Structured output is not a payload lens, so the same bytes are escaped.
    /// </summary>
    /// <remarks>
    /// This is the contrast that shows the rule is about framing rather than
    /// about the flag: <c>--jsonl</c> wraps the identical content in a
    /// structure a caller parses, so the content becomes JSON-escaped text and
    /// <c>U+202E</c> arrives as the six characters <c>\u202E</c>.
    /// </remarks>
    [Fact]
    public void StructuredOutput_IsNotAPayloadLensAndEscapesTheSameBytes()
    {
        using var package = HostilePackage.Create();

        var (output, _) = RunCli([package.Path, ..ReadmeLens, "--jsonl"]);

        HostileOutputAssert.NoRenderingHazard(output, "readme-jsonl");
        HostileOutputAssert.MarkersRendered(output, "readme-jsonl", "MARKERBIDI", "MARKERESC", "MARKERLS");
        Assert.Contains(@"\u202E", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--content</c> is the one lens that frames on stdout, and the framing
    /// it emits quotes untrusted text.
    /// </summary>
    /// <remarks>
    /// A multi-file payload needs a separator, so <c>--content</c> delimits each
    /// match with <c>------------ &lt;package&gt; :: &lt;path&gt;
    /// ------------</c>. That path is a zip entry name, which is attacker
    /// controlled, so both the banner fields and payload beneath it use the
    /// terminal-facing containment policy.
    /// </remarks>
    [Fact]
    public void ContentLens_ContainsTheFieldsOfItsOwnBanner()
    {
        using var package = HostilePackage.Create();

        // Scoped to the hazard-named entry so the banner is unambiguous;
        // "*.md" would also match README.md and yield two.
        var (output, _) = RunCli([package.Path, "--content", "--path", "ENTRY*"]);

        string banner = Assert.Single(
            output.ReplaceLineEndings("\n").Split('\n'),
            l => l.StartsWith("---", StringComparison.Ordinal));

        // The marker survives -- the entry name is still identifiable, so the
        // banner was not simply dropped -- while the hazard between its halves
        // is now visible text rather than an active override.
        HostileOutputAssert.MarkersRendered(
            banner,
            "content-banner",
            "HOSTILE",
            "MARKERPACKAGE",
            "ENTRY",
            "MARKERPATH");
        Assert.Contains(@"\u202E", banner, StringComparison.Ordinal);
        HostileOutputAssert.NoRenderingHazard(banner, "content-banner");
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public void MultiPackageFileRows_ContainPackageAndPathMetadata(string format)
    {
        using var first = HostilePackage.Create();
        using var second = HostilePackage.Create();

        var (output, _) = RunCli(
            [first.Path, second.Path, "--path", "ENTRY*", format, "--tips", "q"]);

        HostileOutputAssert.MarkersRendered(
            output,
            $"multi-package-files-{format}",
            "HOSTILE",
            "MARKERPACKAGE",
            "ENTRY",
            "MARKERPATH");
        HostileOutputAssert.NoRenderingHazard(output, $"multi-package-files-{format}");
    }

    [Fact]
    public void MultiPackageInfoRows_ContainPackageMetadata()
    {
        using var first = HostilePackage.Create();
        using var second = HostilePackage.Create();

        var (output, _) = RunCli(
            [first.Path, second.Path, "-S", "Package Info", "--table", "--tips", "q"]);

        HostileOutputAssert.MarkersRendered(
            output,
            "multi-package-info",
            "HOSTILE",
            "MARKERPACKAGE");
        HostileOutputAssert.NoRenderingHazard(output, "multi-package-info");
    }

    [Fact]
    public void SinglePackageFileJsonl_ContainsPathMetadata()
    {
        using var package = HostilePackage.Create();

        var (output, _) = RunCli(
            [package.Path, "--path", "ENTRY*", "--jsonl", "--tips", "q"]);

        HostileOutputAssert.MarkersRendered(
            output,
            "single-package-files-jsonl",
            "ENTRY",
            "MARKERPATH");
        HostileOutputAssert.NoRenderingHazard(output, "single-package-files-jsonl");
    }

    [Fact]
    public void ContentJsonl_ContainsMetadataAndPreservesThePayloadValue()
    {
        using var package = HostilePackage.Create();

        var (output, _) = RunCli(
            [package.Path, "--path", "README.md", "--content", "--jsonl", "--tips", "q"]);

        HostileOutputAssert.MarkersRendered(
            output,
            "content-jsonl",
            "HOSTILE",
            "MARKERPACKAGE",
            "MARKERBIDI",
            "MARKERESC",
            "MARKERLS");
        HostileOutputAssert.NoRenderingHazard(output, "content-jsonl");

        using var document = JsonDocument.Parse(output);
        Assert.Equal(HostileReadme, document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void EmptyDependencyTree_ContainsItsPackageTitle()
    {
        using var package = HostilePackage.Create(emptyDependencyGroup: true);

        var (output, _) = RunCli([package.Path, "--dependencies", "--tips", "q"]);

        HostileOutputAssert.MarkersRendered(
            output,
            "empty-dependency-tree",
            "HOSTILE",
            "MARKERPACKAGE");
        HostileOutputAssert.NoRenderingHazard(output, "empty-dependency-tree");
    }

    [Fact]
    public void PackageInfoValue_UsesThePackagePresentationBoundary()
    {
        using var package = HostilePackage.Create();

        var (output, error) = RunCli(
            [package.Path, "-S", "Package Info", "--fields", "Repository", "--value", "--tips", "q"]);

        Assert.Empty(error);
        Assert.Contains("MARKERREPOSITORY", output, StringComparison.Ordinal);
        Assert.Contains(@"\u200B", output, StringComparison.Ordinal);
        Assert.DoesNotContain(ZeroWidthSpace, output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void SkillPrint_UsesRawPathForAcquisition()
    {
        using var package = HostilePackage.Create();

        var (output, error) = RunCli(
            [package.Path, "-S", "Package skill files", "--print", "--bare", "--tips", "q"]);

        Assert.Empty(error);
        Assert.Equal("skill payload\n", output.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// A package whose README carries rendering hazards and whose Markdown entry
    /// name carries one too.
    /// </summary>
    private sealed class HostilePackage : IDisposable
    {
        private HostilePackage(string directory, string path)
        {
            Directory = directory;
            Path = path;
        }

        public string Directory { get; }

        public string Path { get; }

        public static HostilePackage Create(bool emptyDependencyGroup = false)
        {
            string directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "lens-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);

            string path = System.IO.Path.Combine(directory, "Hostile.Lens.1.0.0.nupkg");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                string dependencies = emptyDependencyGroup
                    ? """<dependencies><group targetFramework="net8.0" /></dependencies>"""
                    : "";
                WriteEntry(archive, "Hostile.Lens.nuspec", $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package><metadata><id>HOSTILE{Bidi}MARKERPACKAGE</id><version>1.0.0</version>
                    <description>d</description><authors>a</authors>
                    <repository type="git" url="https://example.test/HOSTILE{ZeroWidthSpace}MARKERREPOSITORY" />
                    {dependencies}</metadata></package>
                    """);

                // --readme selects by name, so the hazard payload has to live
                // under a recognized one; a hazard in the *name* would simply
                // not be found, and the lens would emit nothing at all.
                WriteEntry(archive, "README.md", HostileReadme);
                WriteEntry(
                    archive,
                    "skills/ENTRY" + ZeroWidthSpace + "MARKERSKILL/SKILL.md",
                    "skill payload\n");

                // A second entry whose *name* carries the hazard, for the
                // --content banner. Its body is benign so that a failure there
                // is unambiguously about the banner's fields.
                WriteEntry(archive, "ENTRY" + Bidi + "MARKERPATH.md", "benign\n");
                WriteEntry(archive, "lib/net8.0/Hostile.Lens.dll", "MZ");
            }

            return new HostilePackage(directory, path);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            using var stream = archive.CreateEntry(name).Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
    }

    private (string Output, string Error) RunCli(string[] args) =>
        RunCliCore(["package", .. args]);

    private (string Output, string Error) RunCliCore(string[] args)
    {
        // The product copy in *this* project's output, matching
        // UntrustedArgumentDiagnosticContainmentTests: rebuilding the product
        // alone leaves this stale, so a tamper check must rebuild the tests.
        string executable = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["DOTNET_INSPECT_OFFLINE"] = "1";
        psi.Environment["DOTNET_INSPECT_CACHE_DIR"] = _cacheDirectory;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        // Drain both pipes before waiting; reading one to EOF first lets the
        // child deadlock filling the other.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            _deleteCacheOnDispose = false;
            OutOfProcessCliProcess.KillAndWaitForExit(process, TimeSpan.FromSeconds(10));
            throw new TimeoutException(
                $"{executable} did not exit; preserved its test cache at {_cacheDirectory}.");
        }

        return (stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    public void Dispose()
    {
        if (_deleteCacheOnDispose && Directory.Exists(_cacheDirectory))
            Directory.Delete(_cacheDirectory, recursive: true);
    }
}
