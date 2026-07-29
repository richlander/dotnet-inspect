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
    // Non-ASCII digits in the device-digit position: the Latin-1 superscripts are accepted by
    // Windows directly, and the others collapse onto the ASCII digit under best-fit ANSI.
    [InlineData("COM\u00b9")]
    [InlineData("COM\u00b2.txt")]
    [InlineData("LPT\u00b3")]
    [InlineData("COM\u2074")]
    [InlineData("LPT\u2079")]
    [InlineData("COM\uff11")]
    [InlineData("COM\u0661")]
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
    // The digit fold only fires when the whole stem becomes a device name.
    [InlineData("COM\u00b9Plus")]
    [InlineData("COM\uff11Plus")]
    [InlineData("Contoso.V\u2074")]
    [InlineData("COM\u00b94")]
    public void LegitimateComponent_IsAccepted(string value)
    {
        Assert.True(HardenedPath.IsSafePathComponent(value));
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
}
