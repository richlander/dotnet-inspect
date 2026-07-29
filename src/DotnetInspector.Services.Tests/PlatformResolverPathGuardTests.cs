using DotnetInspector.Packages;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// <see cref="PlatformResolver.ResolveAssembly"/> is the single entry point through which an
/// assembly name reaches <c>Path.Combine</c> and <c>File.Exists</c> against a framework directory.
/// Callers hand it names read straight out of untrusted assembly metadata — the transitive
/// reference walk forwards <c>AssemblyRef.Name</c> from the inspected PE — so a hostile name once
/// resolved to a file outside the framework directory and was reported as a platform assembly.
/// </summary>
public class PlatformResolverPathGuardTests
{
    /// <summary>
    /// The name that motivated the guard. The escape needs no existing framework layout: the
    /// resolver combined the name with the shared runtime directory and returned it if the target
    /// existed, so the assertion is that no path is returned and the refusal is visible.
    /// </summary>
    [Fact]
    public void ResolveAssembly_WithTraversingName_RefusesAndReportsAnError()
    {
        var payloadDirectory = Path.Combine(Path.GetTempPath(), $"platform-traversal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(payloadDirectory);
        var payload = Path.Combine(payloadDirectory, "payload.dll");
        File.WriteAllText(payload, "payload");

        try
        {
            // The payload really exists, so a null result is the guard refusing the name rather
            // than the resolver simply failing to find anything.
            Assert.True(File.Exists(payload));

            var traversal = Path.Combine("..", "..", "..", "..", "..", "..", "..", "..")
                + Path.DirectorySeparatorChar
                + Path.GetRelativePath("/", payloadDirectory)
                + Path.DirectorySeparatorChar
                + "payload";

            var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly(traversal);

            Assert.Null(assemblyPath);
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(payloadDirectory, recursive: true);
        }
    }

    /// <summary>
    /// The positive control: an ordinary platform assembly still resolves, so the guard is
    /// refusing hostile names rather than refusing every name.
    /// </summary>
    [Fact]
    public void ResolveAssembly_WithOrdinaryName_StillResolves()
    {
        var (assemblyPath, _, _, _) = PlatformResolver.ResolveAssembly("System.Text.Json");

        Assert.NotNull(assemblyPath);
        Assert.True(File.Exists(assemblyPath));
    }
}

/// <summary>
/// <see cref="StorePath.IsSafeSegment"/> was a separate implementation of the path-component rule
/// and accepted values every other copy refused. An untrusted CodeView PDB file name reaches it on
/// the way to filesystem-backed symbol storage.
/// </summary>
public class StorePathSegmentTests
{
    /// <summary>
    /// The non-drift gate. Rather than restating the rule, this asserts that
    /// <see cref="StorePath.IsSafeSegment"/> and <see cref="HardenedPath.IsSafePathComponent"/>
    /// return the same answer for every probe, so reintroducing a local implementation fails here
    /// the moment it disagrees with the owner on anything.
    /// </summary>
    [Fact]
    public void IsSafeSegment_AgreesWithHardenedPathOnEveryProbe()
    {
        string[] probes =
        [
            // Accepted by the old local implementation, refused by the owner.
            "CON", "con", "NUL", "COM1", "CONIN$", "CONOUT$", "LPT1", "CLOCK$",
            "name.", "name ", " name", "na\u200bme", "na\u0007me", new string('a', 300),
            // Refused by both, all along.
            "", ".", "..", "a/b", "a\\b", "C:", "/rooted",
            // Ordinary store segments.
            "System.Text.Json.pdb", "Newtonsoft.Json", "13.0.3", "lib", "net8.0",
        ];

        var disagreements = probes
            .Where(p => StorePath.IsSafeSegment(p) != HardenedPath.IsSafePathComponent(p))
            .ToArray();

        Assert.Empty(disagreements);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("CONIN$")]
    [InlineData("LPT1")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData(" name")]
    [InlineData("na\u200bme")]
    [InlineData("na\u0007me")]
    public void IsSafeSegment_RefusesWhatTheOtherCopiesRefused(string segment)
        => Assert.False(StorePath.IsSafeSegment(segment));

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    public void IsSafeSegment_StillRefusesWhatItAlwaysRefused(string segment)
        => Assert.False(StorePath.IsSafeSegment(segment));

    [Theory]
    [InlineData("System.Text.Json.pdb")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("13.0.3")]
    [InlineData("lib")]
    public void IsSafeSegment_AcceptsOrdinaryStoreSegments(string segment)
        => Assert.True(StorePath.IsSafeSegment(segment));
}
