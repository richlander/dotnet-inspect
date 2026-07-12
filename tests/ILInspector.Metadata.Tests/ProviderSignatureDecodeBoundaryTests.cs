using System.Text.RegularExpressions;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Freezes the provider-closure contract for issue #2575. Every
/// <see cref="System.Reflection.Metadata.ISignatureTypeProvider{TType,TContext}"/>
/// in <c>ILInspector.Metadata</c> is guarded on both crash vectors:
///
/// 1. Top-level decodes route through <c>GuardedProviderDecode</c>, which prescans
///    the blob with <c>SignatureBlobGuard</c> (SRM recurses on the native stack
///    before the first callback, so only a prescan stops a deep single blob).
/// 2. Each provider's own <c>GetTypeFromSpecification</c> bounds cross-handle
///    TypeSpec re-entry through <c>TypeSpecGuard</c>.
///
/// These assertions are the anti-ratchet completeness proof: a newly added
/// provider or an un-gated top-level decode fails this test rather than shipping
/// an unguarded same-mechanism hole.
/// </summary>
public class ProviderSignatureDecodeBoundaryTests
{
    static readonly string[] Providers =
    {
        "PointerDetector.Instance",
        "ILSignatureTypeProvider.Instance",
        "TypeNodeProvider.Instance",
        "AnchorSignatureTypeProvider.Instance",
        "new InaccessibleTypeDetector",
    };

    // The provider files whose nested TypeSpec re-entry must be bounded by TypeSpecGuard.
    static readonly string[] ProviderFiles =
    {
        "PointerDetector.cs",
        "CanonicalIL.cs",              // ILSignatureTypeProvider
        "TypeNodeProvider.cs",
        "ApiMemberIdentity.cs",        // AnchorSignatureTypeProvider
        "SignatureSpellability.cs",    // InaccessibleTypeDetector
    };

    static readonly Regex RawDecode = new(
        @"\.Decode(Method|Field)?Signature\(",
        RegexOptions.Compiled);

    [Fact]
    public void ProviderTopLevelDecodes_OnlyThroughGuardedGateway()
    {
        var metadataRoot = Path.Combine(FindRepoRoot(), "src", "ILInspector.Metadata");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(metadataRoot, "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (fileName == "GuardedProviderDecode.cs")
                continue; // the gateway is the single sanctioned raw-decode site

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!RawDecode.IsMatch(line))
                    continue;

                // A provider's own guarded GetTypeFromSpecification re-enters via `this`
                // inside TypeSpecGuard.TryEnter/Exit — that is allowed.
                if (line.Contains(".DecodeSignature(this,", StringComparison.Ordinal))
                    continue;

                foreach (var provider in Providers)
                {
                    if (line.Contains(provider, StringComparison.Ordinal))
                        violations.Add($"{Path.GetRelativePath(FindRepoRoot(), file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Provider signatures must enter through GuardedProviderDecode. Raw top-level decodes:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void EveryProvider_BoundsNestedTypeSpecReentry()
    {
        var metadataRoot = Path.Combine(FindRepoRoot(), "src", "ILInspector.Metadata");
        var missing = new List<string>();

        foreach (var providerFile in ProviderFiles)
        {
            var path = Path.Combine(metadataRoot, providerFile);
            Assert.True(File.Exists(path), $"Expected provider file {providerFile} to exist.");
            var text = File.ReadAllText(path);
            if (!text.Contains("TypeSpecGuard.TryEnter", StringComparison.Ordinal))
                missing.Add(providerFile);
        }

        Assert.True(
            missing.Count == 0,
            "Every provider's GetTypeFromSpecification must bound re-entry with TypeSpecGuard. Unguarded:\n  "
            + string.Join("\n  ", missing));
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
