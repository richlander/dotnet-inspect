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
    /// Matches a severity-prefixed write on <em>any</em> receiver, not just
    /// <c>Console.Error</c>.
    /// </summary>
    /// <remarks>
    /// Naming <c>Console.Error</c> was too narrow twice over. Three product
    /// helpers took a <c>TextWriter error</c> parameter that every caller
    /// filled with <c>Console.Error</c>, so the write was spelled
    /// <c>error.WriteLine($"Warning: ...")</c> and the scan never saw it -- one
    /// of them listed undecodable signatures from the inspected assembly
    /// straight to stderr. The parameter bought no flexibility and cost the
    /// gate its reach, so the receiver is no longer part of the pattern: only
    /// <c>CommandError</c> may spell a severity prefix, whatever it writes to.
    /// </remarks>
    private static readonly Regex ErrorWrite =
        new(@"\.\s*(WriteLine|Write)\s*\(\s*\$?@?""(Error|Warning|Note): ", RegexOptions.Compiled);

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
    private static readonly Regex ComposedPrefixWrite =
        new(@"\.\s*(WriteLine|Write)\s*\(\s*\$@?""\{[^}""]+\}\s*:\s", RegexOptions.Compiled);

    [Fact]
    public void CommandError_IsTheOnlyWriterOfTheErrorPrefix()
    {
        string root = RepositoryRoot();
        string product = Path.Combine(root, "src", "dotnet-inspect");
        string owner = Path.Combine(product, "Output", "CommandError.cs");

        Assert.True(File.Exists(owner), $"Expected the owning writer at {owner}.");

        List<string> offenders = [];
        foreach (string path in Directory.EnumerateFiles(product, "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(path, owner, StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            foreach (Match match in ErrorWrite.Matches(text).Concat(ComposedPrefixWrite.Matches(text)))
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
        Assert.Matches(ErrorWrite, "            Console.Error.WriteLine(\"Error: plain.\");");
        Assert.Matches(ErrorWrite, "Console.Error.WriteLine($\"Error: {value} interpolated.\");");
        Assert.Matches(ErrorWrite, "Console . Error . Write ( $\"Error: spaced.\");");

        // The aliased-writer shape: same diagnostic, different receiver.
        Assert.Matches(ErrorWrite, "            error.WriteLine($\"Warning: {count} signatures.\");");
        Assert.Matches(ErrorWrite, "            writer.WriteLine(\"Note: aliased.\");");
        Assert.Matches(ErrorWrite, "Console.Error.WriteLine(\"Warning: plain.\");");
        Assert.Matches(ErrorWrite, "Console.Error.WriteLine(\"Note: plain.\");");
        Assert.DoesNotMatch(ErrorWrite, "Console.Error.WriteLine(\"Errors: not a prefix.\");");
        // Composed messages that merely quote a prefix are not writes.
        Assert.DoesNotMatch(ErrorWrite, "var text = $\"Error: {value}\";");

        // The wrapped form the line-based version of this scan could not see.
        Assert.Matches(
            ErrorWrite,
            "Console.Error.WriteLine(\n                    $\"Error: wrapped {value}.\");");

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
