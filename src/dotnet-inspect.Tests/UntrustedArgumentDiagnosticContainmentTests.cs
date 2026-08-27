using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

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
[Collection("Console")]
public class UntrustedArgumentDiagnosticContainmentTests : IDisposable
{
    private const int ChildExitTimeoutMilliseconds = 120_000;
    private const int OutputCaptureTimeoutMilliseconds = 10_000;
    private const int CapturedOutputHeadCharacters = 32 * 1024;
    private const int CapturedOutputTailCharacters = 32 * 1024;
    private const string Bidi = "\u202E";
    private const string VerticalTab = "\u000B";
    private const string Escape = "\u001B";
    private const string LineSeparator = "\u2028";
    private const string ParagraphSeparator = "\u2029";
    private readonly string _cacheDirectory =
        Directory.CreateTempSubdirectory("diagnostic-containment-cache-").FullName;
    private bool _deleteCacheOnDispose = true;

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

        const string InjectedSeverity = "Error: INJECTEDARG";
        HostileOutputAssert.MarkersRendered(injected, "message-line-injection", InjectedSeverity);
        HostileOutputAssert.NoLineSplit(injected, InjectedSeverity);
    }

    [Fact]
    public void ChildTimeoutDiagnostic_ReportsStateWithoutRenderingCapturedText()
    {
        var snapshot = new ChildProcessSnapshot(
            42,
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(3),
            16_384,
            32_768,
            7,
            null);

        string diagnostic = CreateChildFailureDiagnostic(
            "did not exit after 120 seconds",
            "dotnet-inspect",
            ["depends", $"HOSTILE{"\n"}Error: INJECTEDARG"],
            snapshot,
            $"progress{"\n"}Error: FORGEDOUTPUT",
            $"warning{"\n"}Error: FORGEDERROR",
            "diagnostic-containment-cache");

        Assert.Contains("\"ProcessId\":42", diagnostic, StringComparison.Ordinal);
        Assert.Contains(@"HOSTILE\nError: INJECTEDARG", diagnostic, StringComparison.Ordinal);
        Assert.Contains(@"progress\nError: FORGEDOUTPUT", diagnostic, StringComparison.Ordinal);
        Assert.Contains(@"warning\nError: FORGEDERROR", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("HOSTILE\nError: INJECTEDARG", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("progress\nError: FORGEDOUTPUT", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("warning\nError: FORGEDERROR", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildTimeoutDiagnostic_IsWiredToTheProcessTimeout()
    {
        try
        {
            var exception = Assert.Throws<TimeoutException>(() => RunCli(
                ["type", "System.Strng", "--platform", "System.Runtime"],
                childExitTimeoutMilliseconds: 0));

            Assert.Contains(
                "Arguments: [\"type\",\"System.Strng\",\"--platform\",\"System.Runtime\"]",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains("Process state before termination:", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Captured stdout:", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Captured stderr:", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Preserved test cache:", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            _deleteCacheOnDispose = true;
        }
    }

    [Fact]
    public async Task ChildOutputCapture_DrainsWhileRetainingBoundedHeadAndTail()
    {
        string head = new('H', CapturedOutputHeadCharacters);
        string omitted = new('O', 100);
        string tail = new('T', CapturedOutputTailCharacters);

        var capture = await CaptureTextAsync(new StringReader(head + omitted + tail));

        Assert.Equal(100, capture.OmittedCharacterCount);
        Assert.StartsWith(head, capture.Text, StringComparison.Ordinal);
        Assert.EndsWith(tail, capture.Text, StringComparison.Ordinal);
        Assert.Contains("<100 characters omitted>", capture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(omitted, capture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildOutputCapture_FailureIsReportedWithoutEscapingTheDiagnosticPath()
    {
        Task<BoundedTextCapture> stdout =
            Task.FromException<BoundedTextCapture>(new IOException("pipe failed"));
        Task<BoundedTextCapture> stderr =
            Task.FromResult(new BoundedTextCapture("", 0));

        WaitForOutputCapture(stdout, stderr);

        Assert.Equal("<capture failed: pipe failed>", CapturedText(stdout));
    }

    /// <summary>
    /// Keeps out-of-process containment tests off the operator's real cache.
    /// </summary>
    /// <remarks>
    /// This is the non-vacuity gate for the <c>DOTNET_INSPECT_CACHE_DIR</c>
    /// wiring in <see cref="RunCli"/>. Without it, an obsolete category in the
    /// operator cache can emit an unrelated maintenance line and make a
    /// diagnostic assertion order-dependent (#3726).
    /// </remarks>
    [Fact]
    public void ChildCli_UsesThePerTestCacheAndCleansSilently()
    {
        string obsolete = Path.Combine(_cacheDirectory, "package-content-v4");
        Directory.CreateDirectory(obsolete);
        File.WriteAllText(Path.Combine(obsolete, "stale.txt"), "stale");

        var (output, error) = RunCli(["cache", "--json", "-T:q"]);

        Assert.Empty(error);
        Assert.False(Directory.Exists(obsolete));
        using var document = JsonDocument.Parse(output);
        Assert.Equal(
            _cacheDirectory,
            document.RootElement.GetProperty("location").GetString());
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

    /// <summary>
    /// Pins containment for a stderr view that reaches the stream through a
    /// serializer sink rather than through the writer, carrying text taken from
    /// inside a package archive.
    /// </summary>
    /// <remarks>
    /// The Package Info scalar projection reads the declared readme path from
    /// the .nupkg without rendering the package section that normally contains
    /// it, so it independently gates that projection-only sink.
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
    public void HostilePackageReadmePath_IsContainedInTheValueProjection(string hazard)
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

            var (output, error) = RunCli(
                ["package", package, "-S", "Package Info", "--fields", "Readme", "--value", "--info"]);
            string combined = output + error;

            HostileOutputAssert.MarkersRendered(combined, "value-readme", "INJECTEDREADME");
            HostileOutputAssert.NoRenderingHazard(combined, "value-readme");
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

    private (string Output, string Error) RunCli(
        string[] args,
        int childExitTimeoutMilliseconds = ChildExitTimeoutMilliseconds)
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
        psi.Environment["DOTNET_INSPECT_CACHE_DIR"] = _cacheDirectory;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {executable}.");
        var elapsed = Stopwatch.StartNew();

        // Drain both pipes before waiting; a synchronous read of one blocks
        // until EOF and lets the child deadlock filling the other.
        var stdout = CaptureTextAsync(process.StandardOutput);
        var stderr = CaptureTextAsync(process.StandardError);
        if (!process.WaitForExit(childExitTimeoutMilliseconds))
        {
            _deleteCacheOnDispose = false;
            var snapshot = CaptureProcessSnapshot(process, elapsed.Elapsed);
            OutOfProcessCliProcess.KillAndWaitForExit(
                process,
                TimeSpan.FromMilliseconds(OutputCaptureTimeoutMilliseconds));
            WaitForOutputCapture(stdout, stderr);
            throw new TimeoutException(CreateChildFailureDiagnostic(
                $"did not exit after {childExitTimeoutMilliseconds / 1000} seconds",
                executable,
                args,
                snapshot,
                CapturedText(stdout),
                CapturedText(stderr),
                _cacheDirectory));
        }

        WaitForOutputCapture(stdout, stderr);
        if (!stdout.IsCompletedSuccessfully || !stderr.IsCompletedSuccessfully)
        {
            _deleteCacheOnDispose = false;
            throw new TimeoutException(CreateChildFailureDiagnostic(
                "exited but its redirected output did not close after 10 seconds",
                executable,
                args,
                CaptureProcessSnapshot(process, elapsed.Elapsed),
                CapturedText(stdout),
                CapturedText(stderr),
                _cacheDirectory));
        }

        return (stdout.Result.Text, stderr.Result.Text);
    }

    private static ChildProcessSnapshot CaptureProcessSnapshot(Process process, TimeSpan elapsed)
    {
        try
        {
            process.Refresh();
            return new ChildProcessSnapshot(
                process.Id,
                elapsed,
                process.TotalProcessorTime,
                process.WorkingSet64,
                process.PrivateMemorySize64,
                process.Threads.Count,
                null);
        }
        catch (InvalidOperationException ex)
        {
            return SnapshotCaptureFailure(process.Id, elapsed, ex);
        }
        catch (Win32Exception ex)
        {
            return SnapshotCaptureFailure(process.Id, elapsed, ex);
        }
    }

    private static ChildProcessSnapshot SnapshotCaptureFailure(
        int processId,
        TimeSpan elapsed,
        Exception exception)
        => new(
            processId,
            elapsed,
            null,
            null,
            null,
            null,
            $"{exception.GetType().Name}: {exception.Message}");

    private static async Task<BoundedTextCapture> CaptureTextAsync(TextReader reader)
    {
        var head = new StringBuilder(CapturedOutputHeadCharacters);
        var tail = new char[CapturedOutputTailCharacters];
        var buffer = new char[4096];
        int tailCount = 0;
        int tailWriteIndex = 0;
        long totalCharacters = 0;

        while (true)
        {
            int read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            totalCharacters += read;
            int offset = 0;
            if (head.Length < CapturedOutputHeadCharacters)
            {
                int headCount = Math.Min(read, CapturedOutputHeadCharacters - head.Length);
                head.Append(buffer, 0, headCount);
                offset = headCount;
            }

            int remaining = read - offset;
            if (remaining >= tail.Length)
            {
                Array.Copy(buffer, offset + remaining - tail.Length, tail, 0, tail.Length);
                tailCount = tail.Length;
                tailWriteIndex = 0;
            }
            else if (remaining > 0)
            {
                int firstCount = Math.Min(remaining, tail.Length - tailWriteIndex);
                Array.Copy(buffer, offset, tail, tailWriteIndex, firstCount);
                Array.Copy(buffer, offset + firstCount, tail, 0, remaining - firstCount);
                tailWriteIndex = (tailWriteIndex + remaining) % tail.Length;
                tailCount = Math.Min(tailCount + remaining, tail.Length);
            }
        }

        long omittedCharacters = totalCharacters - head.Length - tailCount;
        var text = new StringBuilder(head.Length + tailCount + 64);
        text.Append(head);
        if (omittedCharacters > 0)
        {
            text.Append($"\n<{omittedCharacters} characters omitted>\n");
        }

        if (tailCount > 0)
        {
            int tailStart = tailCount == tail.Length ? tailWriteIndex : 0;
            int firstCount = Math.Min(tailCount, tail.Length - tailStart);
            text.Append(tail, tailStart, firstCount);
            text.Append(tail, 0, tailCount - firstCount);
        }

        return new BoundedTextCapture(text.ToString(), omittedCharacters);
    }

    private static void WaitForOutputCapture(
        Task<BoundedTextCapture> stdout,
        Task<BoundedTextCapture> stderr)
    {
        var allCaptures = Task.WhenAll(stdout, stderr);
        Task.WhenAny(
                allCaptures,
                Task.Delay(OutputCaptureTimeoutMilliseconds))
            .GetAwaiter()
            .GetResult();
        _ = allCaptures.Exception;
    }

    private static string CapturedText(Task<BoundedTextCapture> capture)
    {
        if (capture.IsCompletedSuccessfully)
        {
            return capture.Result.Text;
        }

        if (capture.IsFaulted)
        {
            return $"<capture failed: {capture.Exception.GetBaseException().Message}>";
        }

        return $"<capture incomplete: {capture.Status}>";
    }

    private static string CreateChildFailureDiagnostic(
        string failure,
        string executable,
        string[] args,
        ChildProcessSnapshot snapshot,
        string stdout,
        string stderr,
        string cacheDirectory)
        => $"""
            Child CLI {failure}.
            Executable: {JsonSerializer.Serialize(executable)}
            Arguments: {JsonSerializer.Serialize(args)}
            Process state before termination: {JsonSerializer.Serialize(snapshot)}
            Captured stdout: {JsonSerializer.Serialize(stdout)}
            Captured stderr: {JsonSerializer.Serialize(stderr)}
            Preserved test cache: {JsonSerializer.Serialize(cacheDirectory)}
            """;

    private sealed record ChildProcessSnapshot(
        int ProcessId,
        TimeSpan Elapsed,
        TimeSpan? TotalProcessorTime,
        long? WorkingSetBytes,
        long? PrivateMemoryBytes,
        int? ThreadCount,
        string? CaptureError);

    private sealed record BoundedTextCapture(
        string Text,
        long OmittedCharacterCount);

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

    public void Dispose()
    {
        if (_deleteCacheOnDispose && Directory.Exists(_cacheDirectory))
            Directory.Delete(_cacheDirectory, recursive: true);
    }
}
