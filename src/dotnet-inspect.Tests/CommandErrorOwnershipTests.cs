using System.Text.RegularExpressions;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins <see cref="DotnetInspector.Output.CommandError"/> as the sole writer of
/// the CLI's <c>Error:</c> line.
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
/// </remarks>
public class CommandErrorOwnershipTests
{
    private static readonly Regex ErrorWrite =
        new(@"Console\s*\.\s*Error\s*\.\s*(WriteLine|Write)\s*\(\s*\$?@?""Error: ", RegexOptions.Compiled);

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
        new(@"Console\s*\.\s*Error\s*\.\s*(WriteLine|Write)\s*\(\s*\$@?""\{[^}""]+\}\s*:\s", RegexOptions.Compiled);

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

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                if (ErrorWrite.IsMatch(lines[i]) || ComposedPrefixWrite.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetRelativePath(root, path)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The 'Error:' prefix must only be written by CommandError, which contains the message. "
                + $"Replace these with CommandError.Write(...):{Environment.NewLine}"
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
        Assert.DoesNotMatch(ErrorWrite, "Console.Error.WriteLine(\"Note: not an error line.\");");
        Assert.DoesNotMatch(ErrorWrite, "CommandError.Write($\"Error: already routed.\");");

        Assert.Matches(ComposedPrefixWrite, "Console.Error.WriteLine($\"{prefix}: Select value '{v}' not found.\");");
        Assert.DoesNotMatch(ComposedPrefixWrite, "Console.Error.WriteLine($\"  {suggestion}\");");
        Assert.DoesNotMatch(ComposedPrefixWrite, "CommandError.WriteWarning($\"{prefix}: routed.\");");
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
