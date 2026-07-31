using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace DotnetInspector.Tests;

/// <summary>
/// Gate for the diagnostics the CLI composes out of the user's own command
/// line (issue #3319).
/// </summary>
/// <remarks>
/// Argv looks like trusted input and is not. An agent builds these option
/// values out of names it just read from a package or an assembly, so a type
/// name carrying <c>U+202E</c> or an ANSI escape arrives as <c>-S</c>,
/// <c>--where</c>, or a bare argument and is quoted straight back. The line
/// that results is indistinguishable from a genuine diagnostic, which is the
/// whole attack.
///
/// A hostile-argv sweep over these options found every case below leaking, and
/// two of them show why the earlier gates missed the family:
///
/// <list type="bullet">
/// <item><description>
/// <c>SelectOutput</c> wrote <c>$"{prefix}: Select value '{value}' not
/// found."</c>, choosing <c>"Error"</c> or <c>"Warning"</c> at the call site.
/// It never spells the literal <c>Error:</c>, so it escaped both
/// <c>CommandError</c> and the source scan in
/// <see cref="CommandErrorOwnershipTests"/>. Severity now belongs to the
/// writer.
/// </description></item>
/// <item><description>
/// Parse errors are formatted in <c>Program.cs</c>, above the root command, so
/// no in-process gate that invokes the parsed command can reach them. These
/// cases therefore run the real executable.
/// </description></item>
/// </list>
/// </remarks>
public class UntrustedArgumentDiagnosticContainmentTests
{
    private const string Bidi = "\u202E";
    private const string VerticalTab = "\u000B";
    private const string Escape = "\u001B";
    private const string LineSeparator = "\u2028";
    private const string ParagraphSeparator = "\u2029";

    /// <remarks>
    /// A raw line feed and carriage return are the most direct forgery of all --
    /// they need no rendering hazard, just a new line -- and the first version
    /// of this gate omitted both, testing only the exotic terminators. A
    /// reviewer pointed out that the one character the whole issue is about was
    /// the one character never sent.
    /// </remarks>
    private const string LineFeed = "\n";
    private const string CarriageReturn = "\r";

    public static TheoryData<string, string[]> HostileArgumentChannels()
    {
        var data = new TheoryData<string, string[]>();
        string library = ProductAssemblyPath();

        foreach (string hazard in new[]
        {
            Bidi, VerticalTab, Escape, LineSeparator, ParagraphSeparator, LineFeed, CarriageReturn,
        })

        {
            string hostile = $"HOSTILE{hazard}INJECTEDARG";

            // Parse-time failures, formatted above the root command.
            data.Add("parse-integer", ["type", "Object", "-n", hostile]);
            data.Add("parse-unrecognized", ["library", library, "--rows", hostile]);
            data.Add("parse-row", ["library", library, "--row", hostile]);

            // Command-time failures that quote the offending argument.
            data.Add("select-miss", ["library", library, "-S", hostile]);
            data.Add("il-offset", ["library", library, "--il-offset", hostile]);
            data.Add("order-by", ["library", library, "--order-by", hostile]);
            data.Add("where", ["library", library, "--where", hostile]);

            // A validator that composes its own message and hands it back as a
            // value, rather than writing it. The message travelled to stderr
            // through a bare Console.Error.WriteLine(error.Message) and so was
            // never contained by anything.
            data.Add("version-error", ["type", "mypackage", $"1.2.3{hazard}INJECTEDARG"]);

            // Verbose progress quotes the thing being worked on, so it carries
            // untrusted text on every line. It went to stderr raw: a hundred
            // and sixteen call sites, none of them a "writer" by name.
            data.Add("verbose-progress", ["depends", hostile, "--platform", "System.Runtime", "--verbose"]);

            // A diagram written to stderr as a TextWriter sink rather than
            // through the writer. Its node labels carry the request URL and the
            // cache key, both built from the package reference, and it escaped
            // only the two Mermaid metacharacters -- so a line terminator in a
            // package name ended the label's line and forged a diagnostic under
            // it.
            data.Add("trace-mermaid", ["package", hostile, "--trace-mermaid"]);

            // The --trace report is a composed multi-line diagnostic whose head
            // line interpolates the target name. It reached stderr as one
            // terminated string, so its writer had to recover line boundaries by
            // splitting -- and a splitter cannot tell the composer's newline from
            // the attacker's, which is how a target named "ev\nError: FORGED"
            // printed a forged unindented line of its own. The trace now yields
            // lines, so each is a unit the writer contains.
            data.Add("trace", ["library", hostile, "--trace"]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HostileArgumentChannels))]
    public void HostileArgument_IsContainedInDiagnostics(string channel, string[] args)
    {
        var (output, error) = RunCli(args);
        string combined = output + error;

        // Non-vacuity: the diagnostic must actually have quoted the argument.
        // Without this a command that silently succeeded, or failed for an
        // unrelated reason, would pass the hazard scan below having proved
        // nothing about this channel.
        HostileOutputAssert.MarkersRendered(combined, channel, "INJECTEDARG");
        HostileOutputAssert.NoRenderingHazard(combined, channel);
        HostileOutputAssert.NoLineSplit(combined, "INJECTEDARG");
    }

    /// <summary>
    /// Pins the two halves of the diagnostic contract: structure passed as
    /// details survives and is indented, and text passed as the message never
    /// produces a second line at all.
    /// </summary>
    /// <remarks>
    /// Folding everything onto one line was safe but destroyed the messages
    /// that list alternatives underneath. Honoring line breaks inside the
    /// message brought the block back and kept injected text indented, but the
    /// injected text still became a real line, because no writer can tell the
    /// composer's newline from the attacker's. Structure therefore arrives as a
    /// separate argument, and the message is always folded -- so a diagnostic
    /// has exactly one unindented line no matter what reaches it.
    /// </remarks>
    [Fact]
    public void Diagnostic_KeepsItsDetailBlockAndNeverGrowsALineFromItsMessage()
    {
        // A diagnostic with genuine detail lines.
        var (_, block) = RunCli(["type", "System.Strng", "--platform", "System.Runtime"]);
        string[] blockLines = Lines(block);

        Assert.StartsWith("Error: ", blockLines[0], StringComparison.Ordinal);
        Assert.Contains(blockLines, l => l.Contains("Did you mean:", StringComparison.Ordinal));
        AssertOneUnindentedLine(blockLines, block);

        // The same writer, with a line terminator injected into the message.
        var (_, injected) = RunCli(["depends", $"HOSTILE{"\n"}Error: INJECTEDARG"]);
        string[] injectedLines = Lines(injected);

        HostileOutputAssert.MarkersRendered(injected, "message-line-injection", "INJECTEDARG");

        // A run may emit several genuine diagnostics; what the injection must
        // not do is add a line that is neither indented nor prefixed by this
        // writer. The forged "Error: INJECTEDARG" is folded into its message.
        AssertEveryLineIsOwned(injectedLines, injected);
    }

    /// <summary>
    /// <c>CommandError.Writer</c> contains what is written through it, and does
    /// so per line rather than per call.
    /// </summary>
    /// <remarks>
    /// The metadata lens takes its caveat sink as a <see cref="TextWriter"/>
    /// parameter and was handed <c>Console.Error</c> directly, so a caveat
    /// naming a hostile heap entry went out raw. Routing it through the owner
    /// is only worth as much as the seam: a writer that forwarded each
    /// <c>Write</c> call would contain <c>"a\nError: FORGED"</c> written whole
    /// but not the same text written a character at a time, and
    /// <c>TextWriter.Write(string)</c> is free to do either. So both spellings
    /// are asserted to produce the same contained lines.
    /// </remarks>
    [Fact]
    public async Task ContainedWriter_FoldsInjectedLinesWhateverTheCallShape()
    {
        const string Hostile = "caveat\nError: FORGEDCAVEAT";

        var (_, whole) = await ConsoleCapture.RunAsync(() =>
        {
            Output.CommandError.Writer.WriteLine(Hostile);
            Output.CommandError.Writer.Flush();
        });

        var (_, piecemeal) = await ConsoleCapture.RunAsync(() =>
        {
            foreach (char c in Hostile)
                Output.CommandError.Writer.Write(c);
            Output.CommandError.Writer.WriteLine();
            Output.CommandError.Writer.Flush();
        });

        var (_, split) = await ConsoleCapture.RunAsync(() =>
        {
            Output.CommandError.Writer.Write(Hostile);
            Output.CommandError.Writer.WriteLine();
            Output.CommandError.Writer.Flush();
        });

        var (_, boxed) = await ConsoleCapture.RunAsync(() =>
        {
            Output.CommandError.Writer.WriteLine((object)Hostile);
            Output.CommandError.Writer.Flush();
        });

        var (_, unterminated) = await ConsoleCapture.RunAsync(() =>
        {
            Output.CommandError.Writer.Write(Hostile);
            Output.CommandError.Writer.Flush();
        });

        Assert.Equal(whole, piecemeal);
        Assert.Equal(whole, split);
        Assert.Equal(whole, boxed);
        Assert.Equal(whole, unterminated);

        string[] lines = Lines(whole);
        Assert.Single(lines);
        Assert.DoesNotContain("\n", lines[0], StringComparison.Ordinal);
        Assert.Contains("FORGEDCAVEAT", lines[0], StringComparison.Ordinal);
        Assert.False(
            lines[0].StartsWith("Error: ", StringComparison.Ordinal),
            $"The injected severity line must not survive as a line of its own: {whole}");
    }

    private static string[] Lines(string text)
        => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    private static void AssertOneUnindentedLine(string[] lines, string raw)
    {
        string[] unindented = [.. lines.Skip(1).Where(l => l.Length > 0 && !l.StartsWith("  ", StringComparison.Ordinal))];
        Assert.True(
            unindented.Length == 0,
            "A diagnostic must have exactly one unindented line, or injected text can forge one. "
                + $"Extra: {string.Join(" | ", unindented)} in: {raw}");
    }

    private static void AssertEveryLineIsOwned(string[] lines, string raw)
    {
        string[] orphans =
        [
            .. lines.Where(l =>
                l.Length > 0
                && !l.StartsWith("  ", StringComparison.Ordinal)
                && !l.StartsWith("Error: ", StringComparison.Ordinal)
                && !l.StartsWith("Warning: ", StringComparison.Ordinal)
                && !l.StartsWith("Note: ", StringComparison.Ordinal)),
        ];

        Assert.True(
            orphans.Length == 0,
            "Every stderr line must be indented or written by CommandError. "
                + $"Orphans: {string.Join(" | ", orphans)} in: {raw}");
    }

    /// <summary>
    /// Pins containment for a stderr view that reaches the stream through a
    /// serializer sink rather than through the writer, carrying text taken from
    /// inside a package archive.
    /// </summary>
    /// <remarks>
    /// The --info view was cleared by one reviewer as carrying only counts and
    /// durations. It also carries the readme path, which comes out of the
    /// .nupkg, so a bidi override in an entry name reached stderr as raw bytes.
    /// Two sink leaks in one round is why the sink set is now pinned by name
    /// rather than reasoned about per review.
    /// </remarks>
    /// <remarks>
    /// Only the hazards XML 1.0 can carry are exercised. A nuspec holding
    /// U+000B or U+001B does not parse, so the readme is never found and the
    /// non-vacuity assertion below correctly refuses to call that a pass.
    /// </remarks>
    [Theory]
    [InlineData(Bidi)]
    [InlineData(LineSeparator)]
    [InlineData(ParagraphSeparator)]
    public void HostilePackageReadmePath_IsContainedInTheInfoView(string hazard)
    {
        string directory = Path.Combine(Path.GetTempPath(), "readme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string entry = $"READ{hazard}INJECTEDREADME.md";
            string package = Path.Combine(directory, "Hostile.Readme.1.0.0.nupkg");

            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "Hostile.Readme.nuspec", $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package><metadata><id>Hostile.Readme</id><version>1.0.0</version>
                    <description>d</description><authors>a</authors>
                    <readme>{System.Security.SecurityElement.Escape(entry)}</readme></metadata></package>
                    """);
                WriteEntry(archive, entry, "readme!!");
                WriteEntry(archive, "lib/net8.0/Hostile.Readme.dll", "MZ");
            }

            var (output, error) = RunCli(["package", package, "-S", "Package README file", "--print", "--info"]);
            string combined = output + error;

            HostileOutputAssert.MarkersRendered(combined, "info-readme", "INJECTEDREADME");
            HostileOutputAssert.NoRenderingHazard(combined, "info-readme");
            HostileOutputAssert.NoLineSplit(combined, "INJECTEDREADME");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Pins the writer nobody in this repository owns: the one that prints an
    /// exception nothing caught.
    /// </summary>
    /// <remarks>
    /// System.CommandLine's default exception handler printed
    /// <c>Unhandled exception: </c> and the raw exception to stderr at column 0.
    /// That made it a second writer of this stream, outside every gate here --
    /// the source scans see no <c>Console.Error.Write</c> and no severity
    /// literal, because the code doing the writing is not in this repository.
    /// An exception message routinely quotes attacker-reachable text, so
    /// <c>--out "&lt;missing&gt;/x\nError: ..."</c> forged a complete diagnostic
    /// with no product code involved, and a hostile .nupkg reached the same
    /// printer through a zip-traversal or nuspec-parse throw. A plain user
    /// mistake -- writing to a directory that does not exist -- was enough to
    /// trigger it, so this was not an exotic path.
    ///
    /// The default handler is now off and the throw lands in the CLI's error
    /// contract instead: one contained <c>Error:</c> line, with the full
    /// exception detail preserved underneath as indented lines.
    ///
    /// Only path-legal hazards are exercised. U+000B, U+001B, LF, and CR are
    /// invalid in a Windows path, so there the failure would come from path
    /// validation without ever quoting the argument, and the non-vacuity
    /// assertion would correctly refuse to call that a pass.
    /// </remarks>
    [Theory]
    [InlineData(Bidi)]
    [InlineData(LineSeparator)]
    [InlineData(ParagraphSeparator)]
    public void EscapingException_IsContainedRatherThanPrintedByTheRuntime(string hazard)
    {
        string directory = Path.Combine(Path.GetTempPath(), "escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string package = Path.Combine(directory, "Benign.Out.1.0.0.nupkg");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "Benign.Out.nuspec", """
                    <?xml version="1.0" encoding="utf-8"?>
                    <package><metadata><id>Benign.Out</id><version>1.0.0</version>
                    <description>d</description><authors>a</authors></metadata></package>
                    """);
                WriteEntry(archive, "lib/net8.0/Benign.Out.dll", "MZ");
            }

            // The parent directory is absent, so writing the output throws and
            // the exception message quotes the whole path back.
            string destination = Path.Combine(
                directory, "missing", $"HOSTILE{hazard}INJECTEDARG", "out.md");

            var (output, error) = RunCli(["package", package, "--out", destination]);
            string combined = output + error;

            HostileOutputAssert.MarkersRendered(combined, "escaping-exception", "INJECTEDARG");
            HostileOutputAssert.NoRenderingHazard(combined, "escaping-exception");
            HostileOutputAssert.NoLineSplit(combined, "INJECTEDARG");

            // The runtime's printer is gone, not merely quieter: its banner is
            // the tell, and its absence is what makes the line count meaningful.
            Assert.DoesNotContain("Unhandled exception", combined, StringComparison.Ordinal);

            // Every line the reader could mistake for a diagnostic must have
            // come from the writer, so the exception detail is indented and the
            // severity line is the only unindented one.
            string[] unindented = [.. error.ReplaceLineEndings("\n").Split('\n')
                .Where(l => l.Length > 0 && !l.StartsWith(' '))];
            Assert.StartsWith("Error: ", Assert.Single(unindented), StringComparison.Ordinal);

            // Nothing is dropped in exchange: the stack still reaches the
            // reader, as indented detail.
            Assert.Contains("DirectoryNotFoundException", error, StringComparison.Ordinal);
            Assert.Contains("   at ", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)    {
        using var stream = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static (string Output, string Error) RunCli(string[] args)
    {
        string executable = Path.Combine(
            Path.GetDirectoryName(ProductAssemblyPath())!,
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

        // Out-of-process, so the offline switch has to travel as environment
        // rather than as the process-wide static the in-process helper sets.
        psi.Environment["DOTNET_INSPECT_OFFLINE"] = "1";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        // Drain both pipes before waiting; a synchronous read of one blocks
        // until EOF and lets the child deadlock filling the other.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{executable} did not exit.");
        }

        Task.WaitAll([stdout, stderr], 10_000);
        return (stdout.Result, stderr.Result);
    }

    private static string ProductAssemblyPath()
    {
        // Deliberately the product copy in *this* test project's output, not
        // the one under artifacts/bin/dotnet-inspect. Anyone checking that
        // these cases are non-vacuous by breaking a product line must rebuild
        // the test project, not just the product: building the product alone
        // leaves this copy stale and the tamper appears to change nothing.
        string path = Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
        if (File.Exists(path))
        {
            return path;
        }

        // The test assembly copies the product next to itself; fall back to the
        // loaded location so a layout change fails loudly here rather than
        // turning every case above into a vacuous pass.
        var located = Assembly.Load("dotnet-inspect").Location;
        return string.IsNullOrEmpty(located)
            ? throw new FileNotFoundException("Could not locate the dotnet-inspect product assembly.")
            : located;
    }
}
