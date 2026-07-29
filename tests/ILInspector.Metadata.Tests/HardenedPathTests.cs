using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The shared path-component rule. These theories are the gate for
/// <see cref="HardenedPath.IsSafePathComponent"/> being the single owner: every hostile form that
/// any of the three former copies rejected is listed here, so consolidating cannot quietly drop
/// one of them.
/// </summary>
public class HardenedPathTests
{
    [Theory]
    // Traversal and separators: escape the intended directory.
    [InlineData("..")]
    [InlineData("../payload")]
    [InlineData("..\\payload")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    [InlineData("/etc/passwd")]
    // Empty and whitespace-only.
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    // Control characters. The NuGetCache copy rejected only \0, which is the drift this
    // consolidation removes.
    [InlineData("foo\0bar")]
    [InlineData("foo\nbar")]
    [InlineData("foo\u0007bar")]
    // Reserved device names, with and without an extension. The NuGetCache copy knew none of
    // these, so a package coordinate could name a device.
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("CON.txt")]
    [InlineData("com1.dll")]
    // The console input/output devices, which CreateFile also opens.
    [InlineData("CONIN$")]
    [InlineData("conout$")]
    [InlineData("CONIN$.dll")]
    // Names the host rewrites before opening: Windows strips trailing spaces and dots.
    [InlineData("CON ")]
    [InlineData("NUL   ")]
    [InlineData("Foo.")]
    [InlineData("System.Text.Json ")]
    [InlineData(" System.Text.Json")]
    [InlineData(".")]
    // Windows reserves these exact superscript spellings, so they are refused as literal names --
    // not because a superscript folds to a digit. COM\u2074 is deliberately absent: it is not on
    // the list and is an ordinary name.
    [InlineData("COM\u00b9")]
    [InlineData("COM\u00b2.txt")]
    [InlineData("LPT\u00b3")]
    [InlineData("com\u00b9")]
    // Windows strips trailing dots and spaces from the stem before matching, so the space lands
    // inside the value rather than at its end and the edge-whitespace rule never sees it.
    [InlineData("COM1 .txt")]
    [InlineData("COM1 . .ext")]
    [InlineData("COM\u00b9 .txt")]
    // Absorbed from ResourceExtractor's copy when it was made to delegate: the Windows-invalid
    // filename characters, which Path.GetInvalidFileNameChars misses on Unix, and CLOCK$.
    [InlineData("Cont<oso")]
    [InlineData("Cont>oso")]
    [InlineData("Cont\"oso")]
    [InlineData("Cont|oso")]
    [InlineData("Cont?oso")]
    [InlineData("Cont*oso")]
    [InlineData("CLOCK$")]
    [InlineData("clock$.dll")]
    // Invisible or reordering format characters: the rendered name is not the opened name.
    [InlineData("System.Text.Json\u200b")]
    [InlineData("\u200bSystem.Text.Json")]
    [InlineData("System.\u202eJson")]
    [InlineData("\ufeffSystem.Text.Json")]
    // Non-ASCII edge whitespace renders as padding but denotes something else.
    [InlineData("System.Text.Json\u00a0")]
    [InlineData("\u3000System.Text.Json")]
    public void UnsafeComponent_IsRejected(string value)
    {
        Assert.False(HardenedPath.IsSafePathComponent(value));
    }

    [Theory]
    // Real package ids, versions, assembly simple names and forwarder targets. Over-rejecting here
    // refuses a legitimate artifact, which is why each hostile rule above is narrow.
    [InlineData("System.Text.Json")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("System.Private.CoreLib")]
    [InlineData("mscorlib")]
    [InlineData("13.0.3")]
    [InlineData("9.0.0-preview.1.24080.9")]
    [InlineData("1.0.0+build.meta")]
    [InlineData("My-Assembly_1.0")]
    [InlineData("\u00dcmlaut.Assembly")]
    // Device names only as a prefix, and interior spaces, which are not canonicalized away.
    [InlineData("NULlable.Helpers")]
    [InlineData("CONtoso.Library")]
    [InlineData("COM1Plus")]
    [InlineData("My Assembly.Core")]
    [InlineData("CON Toso.Library")]
    [InlineData("My\u00a0Assembly.Core")]
    // A device name only as a prefix of a longer stem, including the console devices.
    [InlineData("CONIN$Extras")]
    [InlineData("CLOCK$Extras")]
    // A reserved spelling only matches as the whole stem.
    [InlineData("COM\u00b9Plus")]
    [InlineData("Contoso.V\u2074")]
    [InlineData("COM\u00b94")]
    [InlineData("COM\U0001d7cfPlus")]
    [InlineData("Contoso.\U0001d400ssembly")]
    [InlineData("\U0001f600.Assembly")]
    // Windows' device matcher uppercases ASCII letters and strips trailing dots and spaces. It
    // applies no compatibility normalization and no best-fit mapping, so these are ordinary
    // names, and refusing them would refuse valid artifacts. Two earlier revisions of the guard
    // rejected them on a mechanism that does not apply to path parsing.
    [InlineData("COM\u2074")]
    [InlineData("LPT\u2079")]
    [InlineData("COM\uff11")]
    [InlineData("COM\u0661")]
    [InlineData("COM\U0001d7cf")]
    [InlineData("LPT\U0001d7e3")]
    [InlineData("\uff23\uff2f\uff2d1")]
    [InlineData("\uff23\uff2f\uff2e")]
    [InlineData("\uff21\uff35\uff38")]
    [InlineData("\uff23\uff2f\uff2d\u0664")]
    [InlineData("\uff23\uff2f\uff2d1Plus")]
    [InlineData("\uff2c\uff29\uff22.Assembly")]
    [InlineData("\u30c6\u30b9\u30c8.Library")]
    [InlineData("Contoso.\uff26\uff4f\uff4f")]
    public void LegitimateComponent_IsAccepted(string value)
    {
        Assert.True(HardenedPath.IsSafePathComponent(value));
    }

    /// <summary>
    /// Malformed UTF-16 is rejected. Built in code rather than as theory data: xUnit serializes
    /// InlineData through a round-trip that replaces an unpaired surrogate with U+FFFD, so the
    /// theory would silently test a well-formed string and pass for the wrong reason.
    /// </summary>
    [Fact]
    public void UnpairedSurrogate_IsRejected()
    {
        Assert.False(HardenedPath.IsSafePathComponent("COM\ud800\u00b9"));
        Assert.False(HardenedPath.IsSafePathComponent("COM\udfff\uff11"));
        Assert.False(HardenedPath.IsSafePathComponent("Contoso.\ud800"));
        Assert.False(HardenedPath.IsSafePathComponent("\udc00Contoso"));
        Assert.False(HardenedPath.IsSafePathComponent("Contoso\ud800"));

        // The well-formed pair beside it is still accepted, so the rule refuses malformed UTF-16
        // rather than the supplementary planes.
        Assert.True(HardenedPath.IsSafePathComponent("Contoso.\U0001d400ssembly"));
    }

    [Fact]
    public void Null_IsRejected()
    {
        Assert.False(HardenedPath.IsSafePathComponent(null));
    }

    [Fact]
    public void OverlongComponent_IsRejected()
    {
        Assert.False(HardenedPath.IsSafePathComponent(new string('a', 256)));
        Assert.True(HardenedPath.IsSafePathComponent(new string('a', 255)));
    }

    [Fact]
    public void ValidatePathComponent_NamesTheOffendingValue()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => HardenedPath.ValidatePathComponent("../payload", "package name"));

        Assert.Contains("package name", ex.Message, StringComparison.Ordinal);
        Assert.Contains("../payload", ex.Message, StringComparison.Ordinal);
    }

    // ===== IsSafeRelativePath: restored project inputs carry paths, not bare names =====

    [Theory]
    // The shapes a .deps.json asset key and a project.assets.json compile entry actually take.
    [InlineData("lib/net8.0/Contoso.dll")]
    [InlineData("lib/netstandard2.0/Contoso.Core.dll")]
    [InlineData("runtimes/win-x64/native/contoso.native.dll")]
    [InlineData("runtimes/linux-musl-arm64/lib/net9.0/Contoso.dll")]
    [InlineData("contoso.package/1.2.3")]
    [InlineData("contoso.package/1.2.3-preview.4.25060.1")]
    [InlineData("build/Contoso.props")]
    [InlineData("contoso.package.nuspec")]
    [InlineData(".signature.p7s")]
    [InlineData("ref/net8.0/Contoso.dll")]
    // A backslash-spelled variant of a legitimate path is still legitimate.
    [InlineData("lib\\net8.0\\Contoso.dll")]
    public void LegitimateRelativePath_IsAccepted(string value)
    {
        Assert.True(HardenedPath.IsSafeRelativePath(value));
    }

    [Theory]
    // Traversal, in either spelling, on every host.
    [InlineData("../payload.dll")]
    [InlineData("..\\payload.dll")]
    [InlineData("lib/../../payload.dll")]
    [InlineData("lib/net8.0/../../../payload.dll")]
    [InlineData("lib\\..\\..\\payload.dll")]
    // A device name in any segment, including the last.
    [InlineData("lib/net8.0/CON")]
    [InlineData("lib/CON/Contoso.dll")]
    [InlineData("lib/net8.0/COM1.dll")]
    [InlineData("lib/net8.0/CONIN$")]
    // Rooted values discard the trusted root when combined.
    [InlineData("/etc/passwd")]
    [InlineData("/usr/lib/payload.dll")]
    // Empty segments must not normalize their way past the rule.
    [InlineData("lib//Contoso.dll")]
    [InlineData("lib/./Contoso.dll")]
    [InlineData("/")]
    // Whitespace and trailing dots the host strips.
    [InlineData("lib/net8.0 /Contoso.dll")]
    [InlineData("lib/net8.0./Contoso.dll")]
    // Invisible characters.
    [InlineData("lib/net8.0/Contoso\u200b.dll")]
    [InlineData("")]
    [InlineData("   ")]
    public void HostileRelativePath_IsRejected(string value)
    {
        Assert.False(HardenedPath.IsSafeRelativePath(value));
    }

    [Fact]
    public void NullRelativePath_IsRejected()
    {
        Assert.False(HardenedPath.IsSafeRelativePath(null));
    }

    /// <summary>
    /// A Windows-rooted path is rejected on every host, not only where
    /// <see cref="Path.IsPathRooted(string)"/> agrees. On Unix <c>C:\payload.dll</c> is not rooted,
    /// so the refusal has to come from the component rule rejecting the volume qualifier.
    /// </summary>
    [Fact]
    public void WindowsRootedPath_IsRejectedOnEveryHost()
    {
        Assert.False(HardenedPath.IsSafeRelativePath("C:\\payload.dll"));
        Assert.False(HardenedPath.IsSafeRelativePath("C:/payload.dll"));
    }
}
