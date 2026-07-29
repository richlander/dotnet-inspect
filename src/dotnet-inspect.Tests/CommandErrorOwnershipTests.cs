using System.Text.RegularExpressions;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins <see cref="DotnetInspector.Output.CommandError"/> as the sole writer of
/// the CLI's severity-prefixed stderr lines (<c>Error:</c>, <c>Warning:</c>,
/// <c>Note:</c>).
/// </summary>
/// <remarks>
/// This is the gate for the containment property that
/// <c>UntrustedErrorChannelContainmentTests</c> demonstrates end to end. That
/// test proves one <c>Error:</c> line is contained; it cannot prove the next
/// call site someone adds will be, and there are ~200 of them. The rule this
/// issue keeps rediscovering is that a containment obligation restated at every
/// call site disagrees with itself as soon as one more is added, so the
/// enforcement has to be structural rather than per-site.
///
/// The check is a source scan because that is where the property lives: it is a
/// statement about which code may spell the prefix, not about any one runtime
/// value. A message composed from nothing but literals is still in scope --
/// exempting it would put the author back in the business of judging, per site,
/// whether an interpolated fragment is trusted, which is the judgement that has
/// been wrong repeatedly here.
///
/// The scan reads whole file text rather than lines. Its first version matched
/// per line and so was blind to the wrapped form, where
/// <c>Console.Error.WriteLine(</c> and the string sit on different lines --
/// fourteen real call sites in this repository. A gate that a reformatter can
/// switch off is not a gate, and it reads green while doing it.
/// </remarks>
public class CommandErrorOwnershipTests
{
    /// <summary>
    /// Matches a severity-prefixed string literal anywhere outside the owner --
    /// no receiver, no method name, no call shape.
    /// </summary>
    /// <remarks>
    /// Every narrower spelling of this rule was defeated by the next call site.
    /// Naming <c>Console.Error</c> missed three helpers that took a
    /// <c>TextWriter error</c> parameter every caller filled with
    /// <c>Console.Error</c>; one listed undecodable signatures from the
    /// inspected assembly straight to stderr. Naming a write method then missed
    /// forty sites that spelled <c>logger.Log($"Warning: ...")</c>, where the
    /// prefix travels as an argument to something that is not a writer at all
    /// and the untrusted path or exception text reached stderr raw under
    /// <c>--verbose</c>.
    ///
    /// So the rule is now the smallest one that covers all of them: outside
    /// <c>CommandError</c>, no source line may spell a severity prefix. That is
    /// checkable without understanding the call, and it cannot be sidestepped
    /// by choosing a different sink. It is also newline-immune by construction
    /// -- the literal lives on one line however the call is wrapped -- which the
    /// receiver-based version was not.
    ///
    /// The match is case-insensitive because one site wrote a lowercase
    /// <c>"warning: "</c>, which is the same forgeable line to a reader and was
    /// invisible to a case-sensitive scan.
    /// </remarks>
    private static readonly Regex SeverityLiteral =
        new(@"""(Error|Warning|Note): ", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Catches a diagnostic whose severity is interpolated rather than spelled,
    /// such as <c>$"{prefix}: Select value '{value}' not found."</c>.
    /// </summary>
    /// <remarks>
    /// This is not a hypothetical bypass. <c>SelectOutput</c> chose
    /// <c>"Error"</c> or <c>"Warning"</c> into a local and interpolated it, so
    /// it emitted a real <c>Error:</c> line that neither
    /// <see cref="ErrorWrite"/> nor <c>CommandError</c> ever saw, and the
    /// argument it quoted reached stderr uncontained. Severity now belongs to
    /// the writer, and a call site that reaches for the old shape fails here.
    /// </remarks>
    /// <summary>
    /// Matches stderr reaching code as anything other than a direct write --
    /// aliased into a local, passed as an argument, or taken as a raw handle.
    /// </summary>
    /// <remarks>
    /// The first version matched only the argument position,
    /// <c>[(,]\s*Console\s*\.\s*Error\s*[,)]</c>, and a reviewer defeated it in
    /// one line: <c>var sink = Console.Error;</c> followed by
    /// <c>Serialize(view, sink, ...)</c> added a fifth uncontained sink and the
    /// test stayed green, because the count it asserts never moved.
    /// <c>writer: Console.Error</c>, <c>Console.OpenStandardError()</c> and
    /// <c>Console.SetError</c> were invisible the same way.
    ///
    /// So the rule is the complement of the one next to it: every mention of
    /// the stream that is not <c>Console.Error.Write</c> hands it to something
    /// else, and that is precisely the shape
    /// <see cref="CommandError_IsTheOnlyWriterOfStderr"/> cannot judge.
    /// </remarks>
    private static readonly Regex StderrSink =
        new(@"Console\s*\.\s*(Error\b(?!\s*\.\s*\w+\s*\()|OpenStandardError|SetError)", RegexOptions.Compiled);

    /// <summary>
    /// A call on the stderr stream -- any member, not a named few.
    /// </summary>
    /// <remarks>
    /// Spelling the two method names was the same mistake this file keeps
    /// making at a smaller scale: a reviewer wrote
    /// <c>Console.Error.WriteAsync(value).GetAwaiter().GetResult()</c> and
    /// every test here stayed green, because the rule matched
    /// <c>Write</c> and <c>WriteLine</c> and <c>TextWriter</c> has neither of
    /// those exclusively. <c>WriteLineAsync</c>, <c>Flush</c>, and
    /// <c>Write(char[])</c> were open the same way. The member name is not the
    /// property; reaching the stream is.
    /// </remarks>
    private static readonly Regex StderrWrite =
        new(@"Console\s*\.\s*Error\s*\.\s*\w+\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// A using directive that imports <c>System.Console</c>, making
    /// <c>Error</c> nameable without the receiver every other rule here keys on.
    /// </summary>
    /// <remarks>
    /// <c>using static System.Console;</c> followed by a bare
    /// <c>Error.WriteLine(untrusted)</c> defeated every regex in this file at
    /// once -- an uncontained write with all five tests green -- because each
    /// of them requires the literal <c>Console.</c> to be present. An alias,
    /// <c>using C = System.Console;</c>, does the same thing.
    ///
    /// Rather than chase the spellings that a static import makes possible,
    /// this forbids the import. After it, the only way to name the type is
    /// <c>Console</c> or <c>System.Console</c>, which every other rule here
    /// sees. That closes the set instead of enumerating it -- and the set is
    /// what the previous eighteen rounds kept failing to enumerate.
    ///
    /// The optional <c>global</c> prefix matters: one <c>global using static</c>
    /// anywhere in a project would apply the import to every file in it.
    /// </remarks>
    private static readonly Regex ConsoleImport =
        new(
            @"(?:^|\n)\s*(?:global\s+)?using\s+(?:static\s+|[A-Za-z_]\w*\s*=\s*)(?:global\s*::\s*)?(?:System\s*\.\s*)?Console\s*;",
            RegexOptions.Compiled);

    /// <summary>
    /// The MSBuild spelling of the same import, which no <c>.cs</c> scan sees.
    /// </summary>
    private static readonly Regex MsBuildConsoleImport =
        new(
            @"<Using\s[^>]*Include\s*=\s*""(?:System\.)?Console""[^>]*/?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProjectReference =
        new(@"<ProjectReference\s+Include=""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ComposedPrefixWrite =
        new(@"\.\s*(WriteLine|Write)\s*\(\s*\$@?""\{[^}""]+\}\s*:\s", RegexOptions.Compiled);

    /// <summary>
    /// Every project reachable from <paramref name="projectPath"/> through
    /// ProjectReference, including itself.
    /// </summary>
    private static HashSet<string> ProjectClosure(string projectPath)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Queue<string> queue = new();
        queue.Enqueue(Path.GetFullPath(projectPath));

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (!seen.Add(current) || !File.Exists(current))
            {
                continue;
            }

            string? directory = Path.GetDirectoryName(current);
            foreach (Match match in ProjectReference.Matches(File.ReadAllText(current)))
            {
                string relative = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                queue.Enqueue(Path.GetFullPath(Path.Combine(directory!, relative)));
            }
        }

        return seen;
    }

    /// <summary>
    /// Every C# file belonging to code that runs inside the CLI process.
    /// </summary>
    /// <remarks>
    /// Derived from the CLI's transitive ProjectReference closure rather than a
    /// directory. Scoping the stream rule to <c>src/dotnet-inspect</c> left its
    /// sibling <c>CommandError_IsTheOnlyWriterOfTheErrorPrefix</c> correct and
    /// this one blind: a reviewer added <c>Console.Error.WriteLine(untrusted)</c>
    /// to <c>DotnetInspector.Services</c> -- in-process, on a hostile-nuspec
    /// path -- and the suite stayed green. The closure is the exact set, and it
    /// found a real uncontained sink in <c>DotnetInspector.Core</c> the moment
    /// it was applied.
    /// </remarks>
    private static IEnumerable<string> CliSourceFiles(string root) =>
        ProjectClosure(Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj"))
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories));

    /// <summary>
    /// True when the match at <paramref name="index"/> sits inside a comment.
    /// </summary>
    /// <remarks>
    /// These rules are about code, and both of them describe their own subject
    /// in prose directly above it, so a scan that cannot tell the two apart
    /// reports its own documentation.
    /// </remarks>
    private static bool IsInComment(string text, int index)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;
        string before = text[lineStart..index];
        return before.Contains("//", StringComparison.Ordinal)
            || before.TrimStart().StartsWith('*');
    }

    /// <summary>
    /// Pins the rule that actually closes this class of defect: outside the
    /// owner, no code in the CLI process writes text to stderr.
    /// </summary>
    /// <remarks>
    /// The severity-literal scan below is a spelling rule, and a spelling rule
    /// is evadable by construction -- <c>"Error" + ": "</c>,
    /// <c>string.Format("Error: {0}", m)</c>, or <c>$"{severity}: {m}"</c> all
    /// produce the same forged line without ever spelling it. Worse, it only
    /// describes lines that carry a severity, and stderr also carries
    /// suggestion lists, TFM lists, and progress text. Thirty-four such sites
    /// wrote untrusted text raw; <c>depends</c> printed a hostile package's
    /// <c>targetFramework</c> attribute unindented, forging a diagnostic with
    /// no severity literal anywhere in the source.
    ///
    /// Owning the stream subsumes all of it: if every line comes from the
    /// writer, every line is contained, whatever the caller composed and
    /// however it spelled it. That is a property of the code that runs, not of
    /// the text that appears in it.
    ///
    /// Handing <c>Console.Error</c> to a renderer as a sink stays allowed,
    /// because this scan cannot tell a containing renderer from a
    /// non-containing one. That allowance is the rule's known blind spot and it
    /// has already been exploited once: <c>--trace-mermaid</c> passed the
    /// stream to a bespoke writer that escaped only the two Mermaid
    /// metacharacters, so a line terminator in a package name forged an
    /// unindented stderr line without a single <c>Console.Error.Write</c> in
    /// the source. Two reviewers found it independently.
    ///
    /// Each sink is therefore accounted for by name rather than by category,
    /// and a new one is a change this test cannot catch:
    /// <list type="bullet">
    /// <item><c>Output/Hints.cs</c> x2 -- Markout views whose untrusted field
    /// is contained when the row is built.</item>
    /// <item><c>Program.cs</c> --info -- a Markout view of counts, durations,
    /// and the readme path from inside the .nupkg.</item>
    /// <item><c>Program.cs</c> --trace-mermaid -- contained at composition,
    /// with containment a required parameter so no caller can omit it, and
    /// gated end to end by the trace-mermaid channel.</item>
    /// <item><c>DotnetInspector.Core/HttpClientFactory.cs</c> -- the DEBUG-only
    /// network traffic log, whose URL carries the package id from argv;
    /// contained through a required constructor parameter.</item>
    /// </list>
    /// <see cref="StderrSinks_AreStillTheOnesAccountedFor"/> fails when this
    /// list goes stale.
    /// </remarks>
    [Fact]
    public void CommandError_IsTheOnlyWriterOfStderr()
    {
        string root = RepositoryRoot();
        string owner = Path.Combine(root, "src", "dotnet-inspect", "Output", "CommandError.cs");

        List<string> offenders = [];
        foreach (string path in CliSourceFiles(root))
        {
            if (string.Equals(path, owner, StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            foreach (Match match in StderrWrite.Matches(text).Concat(ConsoleImport.Matches(text)))
            {
                if (IsInComment(text, match.Index))
                {
                    continue;
                }

                int line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(root, path)}:{line}: {match.Value.Trim()}");
            }
        }

        // The same import can be declared per-project in MSBuild, where no .cs
        // file mentions it and every source scan above is blind to it.
        foreach (string project in ProjectClosure(Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj")))
        {
            string text = File.ReadAllText(project);
            foreach (Match match in MsBuildConsoleImport.Matches(text))
            {
                int line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(root, project)}:{line}: {match.Value.Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Only CommandError may write text to stderr, so that every line on the stream is "
                + $"contained. Use CommandError.Write/WriteWarning/WriteNote/WriteLine/WriteDetail. "
                + $"A `using static System.Console` is reported too: it makes `Error.WriteLine` "
                + $"reachable without the receiver this rule keys on.{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Pins the set of places that hand stderr to something other than a direct
    /// write, which is the one shape
    /// <see cref="CommandError_IsTheOnlyWriterOfStderr"/> cannot check. A new
    /// sink is a real risk -- two of these five were live forgeries -- so adding
    /// one must fail here and force a decision about how its text is contained.
    /// </summary>
    /// <remarks>
    /// The assertion is the set of sites, not their number. Asserting
    /// <c>sinks.Count == 4</c> made the test blind in exactly the direction it
    /// exists to watch: a reviewer aliased <c>Console.Error</c> into a local,
    /// added a fifth uncontained sink alongside the four real ones, and the
    /// count-based version passed. A per-file tally moves with any addition,
    /// including one that replaces a site it also removes.
    /// </remarks>
    [Fact]
    public void StderrSinks_AreStillTheOnesAccountedFor()
    {
        string root = RepositoryRoot();
        Dictionary<string, int> sinks = new(StringComparer.Ordinal);

        foreach (string path in CliSourceFiles(root))
        {
            string text = File.ReadAllText(path);
            foreach (Match match in StderrSink.Matches(text))
            {
                if (IsInComment(text, match.Index))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                sinks[relative] = sinks.TryGetValue(relative, out int count) ? count + 1 : 1;
            }
        }

        Dictionary<string, int> accounted = new(StringComparer.Ordinal)
        {
            // Markout views of the tips and legend; every field of both rows is
            // contained where the row is built.
            ["src/dotnet-inspect/Output/Hints.cs"] = 2,

            // --info (a view of counts and durations, plus the readme path from
            // inside the .nupkg, contained at row construction) and
            // --trace-mermaid (contained at composition, containment a required
            // parameter so no caller can omit it).
            ["src/dotnet-inspect/Program.cs"] = 2,

            // DEBUG-only network traffic log. Not in the shipped build, but the
            // logged URL carries the package id from argv, so its consumer takes
            // containment as a required constructor parameter.
            ["src/DotnetInspector.Core/HttpClientFactory.cs"] = 1,
        };

        Assert.Equal(accounted, sinks);
    }

    /// <summary>
    /// Guards the stream rule against becoming vacuous if the pattern or the
    /// scanned root stops matching real code.
    /// </summary>
    [Fact]
    public void StderrScan_MatchesTheShapeItIsMeantToCatch()
    {
        Assert.Matches(StderrWrite, "        Console.Error.WriteLine($\"{a}\");");
        Assert.Matches(StderrWrite, "Console.Error.Write(x);");
        Assert.DoesNotMatch(StderrWrite, "MarkoutSerializer.Serialize(view, Console.Error, ctx);");

        // The sink scan has to see the stream however it is handed over, not
        // only in argument position: each of these was green under the earlier
        // pattern, and the first is the one a reviewer used to smuggle a fifth
        // sink past a passing test.
        Assert.Matches(StderrSink, "var sink = Console.Error;");
        Assert.Matches(StderrSink, "Serialize(view, writer: Console.Error, ctx);");
        Assert.Matches(StderrSink, "using var s = Console.OpenStandardError();");
        Assert.Matches(StderrSink, "Console.SetError(w);");
        Assert.DoesNotMatch(StderrSink, "Console.Error.WriteLine(x);");

        // Naming a method rather than the stream left every other member of
        // TextWriter open; each of these reaches stderr and none is a Write or
        // a WriteLine.
        Assert.Matches(StderrWrite, "Console.Error.WriteAsync(value).GetAwaiter().GetResult();");
        Assert.Matches(StderrWrite, "await Console.Error.WriteLineAsync(value);");
        Assert.Matches(StderrWrite, "Console.Error.Flush();");
        Assert.Matches(StderrWrite, "System.Console.Error.WriteLine(x);");

        // An import of the type makes `Error` nameable with no receiver, which
        // is invisible to every other pattern in this file.
        Assert.Matches(ConsoleImport, "using static System.Console;\n");
        Assert.Matches(ConsoleImport, "using static Console;\n");
        Assert.Matches(ConsoleImport, "global using static System.Console;\n");
        Assert.Matches(ConsoleImport, "using C = System.Console;\n");
        Assert.Matches(ConsoleImport, "using C = global::System.Console;\n");
        Assert.DoesNotMatch(ConsoleImport, "using System;\n");
        Assert.DoesNotMatch(ConsoleImport, "using static System.Math;\n");
        Assert.DoesNotMatch(ConsoleImport, "using DotnetInspector.Output.ConsoleTheme;\n");
        Assert.Matches(MsBuildConsoleImport, "<Using Include=\"System.Console\" Static=\"true\" />");
        Assert.DoesNotMatch(MsBuildConsoleImport, "<Using Include=\"System.Linq\" />");

        // The comment filter must exempt prose without exempting code that
        // merely follows a comment on an earlier line.
        Assert.True(IsInComment("// see Console.Error for why\n", 10));
        Assert.False(IsInComment("// prose\nvar sink = Console.Error;\n", 20));

        // The owner still writes, so the rule is about who, not about whether.
        string owner = Path.Combine(RepositoryRoot(), "src", "dotnet-inspect", "Output", "CommandError.cs");
        Assert.Matches(StderrWrite, File.ReadAllText(owner));
    }

    [Fact]
    public void CommandError_IsTheOnlyWriterOfTheErrorPrefix()
    {
        string root = RepositoryRoot();
        string owner = Path.Combine(root, "src", "dotnet-inspect", "Output", "CommandError.cs");

        Assert.True(File.Exists(owner), $"Expected the owning writer at {owner}.");

        // Every project whose code runs inside this CLI process, derived from
        // the CLI's own transitive ProjectReference closure rather than a
        // hand-kept list. Scoping this to src/dotnet-inspect let
        // ILInspector.Metadata keep composing its own "Error: " into a returned
        // message, which the CLI then prefixed again: "member String -m
        // ToString --index 99" printed "Error: Error: ...". Naming the
        // exclusions instead went the other way and flagged sibling tools
        // (runfaster, mdi, ILInspector.Analysis.App) that have their own entry
        // points and cannot reach this writer at all. The closure is the exact
        // set: a project newly referenced by the CLI is covered automatically,
        // and a separate tool never is.
        //
        // Excluding mdi is mechanically right and substantively a gap: it reads
        // the same untrusted metadata and renders it uncontained. That is
        // tracked as its own issue (#3444), not silently inherited from here.
        string[] products = [.. ProjectClosure(Path.Combine(root, "src", "dotnet-inspect", "dotnet-inspect.csproj"))
            .Select(Path.GetDirectoryName)
            .OfType<string>()];

        // A broken closure would shrink to the CLI alone and pass vacuously,
        // which is exactly the failure this rule already had once.
        string[] names = [.. products.Select(Path.GetFileName).OfType<string>()];
        Assert.Contains("dotnet-inspect", names);
        Assert.Contains("ILInspector.Metadata", names);
        Assert.Contains("DotnetInspector.Services", names);
        Assert.DoesNotContain("mdi", names);
        Assert.DoesNotContain("runfaster", names);

        List<string> offenders = [];
        foreach (string path in products.SelectMany(p => Directory.EnumerateFiles(p, "*.cs", SearchOption.AllDirectories)))
        {
            if (string.Equals(path, owner, StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                // A comment may quote the prefix while describing this very
                // rule, which is not a write.
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (SeverityLiteral.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetRelativePath(root, path)}:{i + 1}: {lines[i].Trim()}");
                }
            }

            foreach (Match match in ComposedPrefixWrite.Matches(text))
            {
                int line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(root, path)}:{line}: {match.Value.Replace('\n', ' ').Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A severity prefix must only be written by CommandError, which contains the message. "
                + $"Replace these with CommandError.Write/WriteWarning/WriteNote:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Guards the scan itself: a regex that matched nothing anywhere would let
    /// the test above pass vacuously forever.
    /// </summary>
    [Fact]
    public void Scan_MatchesTheShapeItIsMeantToCatch()
    {
        Assert.Matches(SeverityLiteral, "            Console.Error.WriteLine(\"Error: plain.\");");
        Assert.Matches(SeverityLiteral, "Console.Error.WriteLine($\"Error: {value} interpolated.\");");

        // The wrapped call: the literal is still on one line.
        Assert.Matches(SeverityLiteral, "                    $\"Error: wrapped {value}.\");");

        // The aliased writer, and the sink that is not a writer at all.
        Assert.Matches(SeverityLiteral, "            error.WriteLine($\"Warning: {count} signatures.\");");
        Assert.Matches(SeverityLiteral, "                logger.Log($\"Warning: Could not read {skippedPath}\");");

        // A returned message, not a write, and a lowercase prefix: both real.
        Assert.Matches(SeverityLiteral, "                $\"Error: No members matched selector '{text}'.\",");
        Assert.Matches(SeverityLiteral, "                var msg = $\"warning: {kind} '{name}' not found\";");

        Assert.DoesNotMatch(SeverityLiteral, "Console.Error.WriteLine(\"Errors: not a prefix.\");");
        Assert.DoesNotMatch(SeverityLiteral, "CommandError.Write($\"{message}\");");

        Assert.Matches(ComposedPrefixWrite, "Console.Error.WriteLine($\"{prefix}: Select value '{v}' not found.\");");
        Assert.DoesNotMatch(ComposedPrefixWrite, "Console.Error.WriteLine($\"  {suggestion}\");");
        Assert.DoesNotMatch(ComposedPrefixWrite, "Console.Error.WriteLine($\"{count} rows.\");");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find repository root containing dotnet-inspect.slnx.");
    }
}
