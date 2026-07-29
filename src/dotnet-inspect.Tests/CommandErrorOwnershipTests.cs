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
    private static readonly Regex StderrWrite =
        new(@"Console\s*\.\s*Error\s*\.\s*Write(Line)?\s*\(", RegexOptions.Compiled);

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
    /// Passing <c>Console.Error</c> to a serializer as a sink is a different
    /// path and stays allowed: it renders a view through the same Markout
    /// containment that this suite gates for stdout, rather than writing
    /// caller-composed text.
    /// </remarks>
    [Fact]
    public void CommandError_IsTheOnlyWriterOfStderr()
    {
        string root = RepositoryRoot();
        string owner = Path.Combine(root, "src", "dotnet-inspect", "Output", "CommandError.cs");

        List<string> offenders = [];
        foreach (string path in Directory.EnumerateFiles(
            Path.Combine(root, "src", "dotnet-inspect"), "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(path, owner, StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            foreach (Match match in StderrWrite.Matches(text))
            {
                int line = text.Take(match.Index).Count(c => c == '\n') + 1;
                string source = text[match.Index..];
                if (source.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{Path.GetRelativePath(root, path)}:{line}: {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Only CommandError may write text to stderr, so that every line on the stream is "
                + $"contained. Use CommandError.Write/WriteWarning/WriteNote/WriteLine/WriteDetail:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offenders));
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
