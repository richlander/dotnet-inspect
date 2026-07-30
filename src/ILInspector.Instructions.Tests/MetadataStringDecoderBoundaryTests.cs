namespace ILInspector.Instructions.Tests;

/// <summary>
/// Gate for the premise the compiler-generated ordinal correspondence rests on.
/// </summary>
/// <remarks>
/// <see cref="CompilerGeneratedOrdinalCorrespondence.OrdinalPlaceholder"/> and
/// <see cref="CompilerGeneratedOrdinalCorrespondence.KeySeparator"/> are safe because a
/// metadata name cannot contain NUL. That follows from the <c>#Strings</c> heap being
/// NUL-terminated — but only for a <see cref="MetadataReader"/> built with the default
/// string decoder. A custom <c>MetadataStringDecoder</c> may return any string it likes,
/// including one containing NUL, which would let a raw name impersonate a folded one.
/// <para>
/// The decoder is chosen by the code that constructs the reader, never by the assembly
/// being read, so this is a constraint on this repository rather than an attack an
/// untrusted input can mount. This test makes that constraint enforced instead of
/// assumed: the product does not construct a custom decoder anywhere, and cannot start
/// doing so without failing here. It follows the source-scanning boundary pattern already
/// used by <c>StringSignatureDecodeBoundaryTests</c>.
/// </para>
/// <para>
/// A caller outside this repository can still hand the public comparison seam a reader
/// with a custom decoder. That is a documented precondition on
/// <c>CompilerGeneratedOrdinalCorrespondence.Build</c>, not something this test covers.
/// </para>
/// </remarks>
public class MetadataStringDecoderBoundaryTests
{
    [Fact]
    public void Product_DoesNotSupplyACustomMetadataStringDecoder()
    {
        string root = FindRepoRoot();
        string[] sourceRoots = [Path.Combine(root, "src"), Path.Combine(root, "tools")];
        var violations = new List<string>();

        foreach (string sourceRoot in sourceRoots)
        {
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                // The gate itself names the type it forbids.
                if (Path.GetFileName(file) == "MetadataStringDecoderBoundaryTests.cs")
                    continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    // Prose may name the type — the rationale on the correspondence does.
                    // Only a real reference to it can defeat the unspellability argument.
                    string text = lines[i].TrimStart();
                    if (text.StartsWith("//", StringComparison.Ordinal)
                        || text.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (lines[i].Contains("MetadataStringDecoder", StringComparison.Ordinal))
                        violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "A custom MetadataStringDecoder can return names containing NUL, which breaks the "
            + "unspellability argument behind OrdinalPlaceholder and KeySeparator. If one is "
            + "genuinely needed, the ordinal correspondence must stop relying on that argument "
            + "first. Uses:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>
    /// The scan only means something if it is looking at real sources, so pin that it found
    /// a substantial number of files. Otherwise a wrong root would make the gate vacuous.
    /// </summary>
    [Fact]
    public void Scan_ActuallyReachesProductSources()
    {
        string root = FindRepoRoot();
        int count = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Count(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        Assert.True(count > 100, $"expected the scan to reach the product sources, saw {count} files");
    }

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
