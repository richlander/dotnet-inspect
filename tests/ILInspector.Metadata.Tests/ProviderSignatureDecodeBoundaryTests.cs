using System.Text.RegularExpressions;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Freezes the signature-provider closure contract for issue #2575 across both
/// SRM-only assemblies. SRM's <c>DecodeSignature</c> recurses on the native
/// stack for every nested element <em>before</em> the first provider callback,
/// so a single over-deep blob overflows the stack in a way no managed
/// <c>try/catch</c> can contain. Every top-level provider decode must therefore
/// be prescanned with <c>SignatureBlobGuard</c>, and every nested cross-handle
/// TypeSpec re-entry must be bounded by <c>TypeSpecGuard</c>.
///
/// The assertions below are a deny-list, not an allow-list: any raw
/// <c>Decode*Signature</c> call that is not one of the sanctioned guarded forms
/// is a violation. A newly added provider, a decode routed through a local
/// alias, or an un-gated site fails this test rather than shipping an unguarded
/// same-mechanism hole. This is the anti-ratchet completeness proof.
/// </summary>
public class ProviderSignatureDecodeBoundaryTests
{
    static readonly string[] AssemblyRoots =
    {
        Path.Combine("src", "ILInspector.Metadata"),
        Path.Combine("src", "ILInspector.MetadataPrimitives"),
    };

    // Files that perform a top-level provider decode behind an inline
    // SignatureBlobGuard prescan. Each is asserted to actually contain the
    // prescan by GatewayFiles_PrescanWithSignatureBlobGuard.
    static readonly string[] PrescanGatewayFiles =
    {
        "GuardedProviderDecode.cs",
        "GuardedSignatureText.cs",
        "AttributeDecoder.cs",
    };

    // Whitespace-tolerant so a `.DecodeSignature (` mutation cannot slip past.
    static readonly Regex RawDecode = new(
        @"\.Decode(Method|Field)?Signature\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void EveryProviderDecode_IsGuarded()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!RawDecode.IsMatch(line))
                    continue;

                // Sanctioned form 1: an inline-prescanned gateway file.
                if (PrescanGatewayFiles.Contains(fileName))
                    continue;

                // Sanctioned form 2: a nested cross-handle TypeSpec re-entry,
                // bounded by TypeSpecGuard in its enclosing provider file.
                if (line.Contains("GetTypeSpecification(", StringComparison.Ordinal))
                    continue;

                violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every provider signature decode must be prescanned (GuardedProviderDecode / "
            + "GuardedSignatureText / AttributeDecoder) or a TypeSpecGuard-bounded nested "
            + "re-entry. Unguarded raw decodes:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void GatewayFiles_PrescanWithSignatureBlobGuard()
    {
        var root = FindRepoRoot();
        var present = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var fileName = Path.GetFileName(file);
            if (!PrescanGatewayFiles.Contains(fileName))
                continue;

            present.Add(fileName);
            if (!File.ReadAllText(file).Contains("SignatureBlobGuard.IsSafeToDecode", StringComparison.Ordinal))
                missing.Add(Path.GetRelativePath(root, file));
        }

        // Every declared prescan gateway must exist and actually prescan.
        Assert.Equal(PrescanGatewayFiles.Length, present.Count);
        Assert.True(
            missing.Count == 0,
            "Declared prescan gateways must call SignatureBlobGuard.IsSafeToDecode:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void NestedTypeSpecReentry_IsBoundedByTypeSpecGuard()
    {
        var root = FindRepoRoot();
        var unguarded = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var lines = File.ReadAllLines(file);
            bool hasNestedReentry = lines.Any(line =>
                RawDecode.IsMatch(line)
                && line.Contains("GetTypeSpecification(", StringComparison.Ordinal));

            if (hasNestedReentry
                && !File.ReadAllText(file).Contains("TypeSpecGuard.TryEnter", StringComparison.Ordinal))
                unguarded.Add(Path.GetRelativePath(root, file));
        }

        Assert.True(
            unguarded.Count == 0,
            "Every file that re-enters a nested TypeSpec decode must bound it with "
            + "TypeSpecGuard.TryEnter:\n  " + string.Join("\n  ", unguarded));
    }

    static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        foreach (var assemblyRoot in AssemblyRoots)
        {
            var dir = Path.Combine(root, assemblyRoot);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                yield return file;
        }
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
        return directory!.FullName;
    }
}
