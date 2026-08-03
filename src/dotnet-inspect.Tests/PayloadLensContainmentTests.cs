using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the one place the CLI deliberately emits untrusted bytes verbatim, and
/// the stream split that makes doing so safe (issue #3319).
/// </summary>
/// <remarks>
/// Every rendered surface in this tool contains what it shows: line terminators
/// folded, rendering hazards rewritten as visible <c>\uXXXX</c>. A reviewer
/// pointed at the README payload emitting raw VT, ESC, bidi, and LS to
/// stdout and read it as the containment work having missed a channel.
///
/// It had not. The README payload and <c>--content</c> are payload lenses: their
/// job is to hand over the bytes of a file the way <c>cat</c> does, and
/// escaping those bytes would corrupt the payload and defeat the flag. Piping
/// that payload to a file has to reproduce the README.
///
/// But "defensible" and "declared" are different things, and the gap the
/// reviewer actually found was the second one: the behavior was correct,
/// undocumented, and ungated, which reads exactly like an oversight and would
/// have been indistinguishable from one to the next reader. The contract now
/// lives in <c>docs/design/output-shapes.md</c>, and this is the gate it names.
///
/// The contract is not "the payload is raw" on its own -- that alone would be a
/// leak. It is a conjunction, and this class asserts both halves because either
/// one without the other is unsafe:
///
/// <list type="number">
/// <item><description>
/// stdout carries the payload <b>byte for byte</b>, and
/// </description></item>
/// <item><description>
/// stdout carries <b>no tool-authored framing</b> -- every heading, table, and
/// diagnostic the tool composes goes to stderr, contained.
/// </description></item>
/// </list>
///
/// A test that checked only the first half would keep passing if someone added
/// a heading to stdout, which is the change that would turn a defensible
/// <c>cat</c> into a forgeable report. That is the regression this exists to
/// catch, so <see cref="ReadmeLens_PutsNoToolFramingOnTheStreamItEmitsRawBytesOn"/>
/// runs the hazard payload with <c>--info</c> specifically -- the mode that
/// does put a real <c># Info</c> table in the picture -- and asserts the table
/// landed on the other stream.
/// </remarks>
public class PayloadLensContainmentTests : IDisposable
{
    private const string Bidi = "\u202E";
    private const string Escape = "\u001B";
    private const string LineSeparator = "\u2028";
    private readonly string _cacheDirectory =
        Directory.CreateTempSubdirectory("payload-containment-cache-").FullName;

    /// <summary>
    /// The README bytes every test here plants and expects back unchanged.
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
    public void ReadmeLens_ReproducesThePayloadByteForByte()
    {
        using var package = HostilePackage.Create();

        var (output, _) = RunCli([package.Path, ..ReadmeLens]);

        AssertIsThePayload(output);
    }

    /// <summary>
    /// Asserts stdout is the README bytes, unescaped and unaltered.
    /// </summary>
    /// <remarks>
    /// Equality, not "contains a marker": a lens that escaped one of the three
    /// hazards, or normalized line endings, would still satisfy a
    /// containment-style assertion while failing at the job the flag exists for.
    ///
    /// The retired <c>--readme</c> lens appended one line terminator, which was
    /// measured rather than assumed. The <c>--print</c> path it was replaced by
    /// appends nothing: re-measured against payloads ending in zero, one, and
    /// two newlines, each comes back with exactly the count it went in with, so
    /// the payload is now byte-identical to <c>cat</c> with no departure at
    /// all. Equality is asserted rather than a looser check so that a future
    /// change to how the payload is written has to come back through this test.
    /// </remarks>
    private static void AssertIsThePayload(string output)
    {
        Assert.Equal(
            HostileReadme,
            output.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// The half that keeps the raw half honest: raw bytes are only safe on a
    /// stream that never speaks for the tool.
    /// </summary>
    [Fact]
    public void ReadmeLens_PutsNoToolFramingOnTheStreamItEmitsRawBytesOn()
    {
        using var package = HostilePackage.Create();

        // --info is the mode that composes a real section alongside the lens,
        // so it is the one that can put framing and payload on one stream.
        var (output, error) = RunCli([package.Path, ..ReadmeLens, "--info"]);

        AssertIsThePayload(output);

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
    /// controlled, so the banner is a rendered surface even though the bytes
    /// beneath it are not -- and it is covered by the containment rule rather
    /// than by the lens exemption.
    ///
    /// The banner's *shape* remains forgeable by the payload: a file whose
    /// content includes that exact line makes one file look like two. That is
    /// pre-existing (it reproduces identically on the pre-fix baseline), is
    /// outside the metadata-containment scope of #3319, and is deliberately not
    /// claimed here. This test pins the half that is fixed -- the fields -- so
    /// that the unfixed half stays a known, written-down limitation rather than
    /// an assumption.
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
        HostileOutputAssert.MarkersRendered(banner, "content-banner", "ENTRY", "MARKERPATH");
        Assert.Contains(@"\u202E", banner, StringComparison.Ordinal);
        HostileOutputAssert.NoRenderingHazard(banner, "content-banner");
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

        private string Directory { get; }

        public string Path { get; }

        public static HostilePackage Create()
        {
            string directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "lens-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);

            string path = System.IO.Path.Combine(directory, "Hostile.Lens.1.0.0.nupkg");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "Hostile.Lens.nuspec", """
                    <?xml version="1.0" encoding="utf-8"?>
                    <package><metadata><id>Hostile.Lens</id><version>1.0.0</version>
                    <description>d</description><authors>a</authors></metadata></package>
                    """);

                // --readme selects by name, so the hazard payload has to live
                // under a recognized one; a hazard in the *name* would simply
                // not be found, and the lens would emit nothing at all.
                WriteEntry(archive, "README.md", HostileReadme);

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
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} did not exit.");
        }

        return (stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
            Directory.Delete(_cacheDirectory, recursive: true);
    }
}
