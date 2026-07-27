using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Fast gate for the ReturnToSender constructor identity restored by #3251.
///
/// The end-to-end coverage in <c>ReturnToSenderPrototypeTests</c> is
/// <c>Speed=Slow</c>, and the PR test job drops that trait — which is exactly how
/// the #3129 sanitizer silently rewrote <c>.ctor</c> to <c>__ctor</c> on
/// <c>main</c>. These cases carry no trait, so they run in the PR job.
/// </summary>
[Trait("Area", "RoundTrip")]
public class MemberIdentifierNameTests
{
    [Fact]
    public void Constructor_KeepsMetadataName()
    {
        Assert.Equal(
            ".ctor",
            CompileBackSourceComposer.MemberIdentifierName(".ctor", isConstructor: true));
    }

    /// <summary>
    /// Non-vacuity: the sanitizer really does corrupt <c>.ctor</c>, so
    /// <see cref="Constructor_KeepsMetadataName"/> cannot pass by accident if the
    /// guard is deleted.
    /// </summary>
    [Fact]
    public void Sanitizer_WouldCorruptConstructorName_SoTheGuardIsLoadBearing()
    {
        string sanitized = CSharpNaming.SourceMethodName(".ctor");

        Assert.NotEqual(".ctor", sanitized);
        Assert.Equal(
            sanitized,
            CompileBackSourceComposer.MemberIdentifierName(".ctor", isConstructor: false));
    }

    [Theory]
    [InlineData("Parse")]
    [InlineData("<Run>g__Local|0_0")]
    [InlineData("<>c__DisplayClass0_0")]
    [InlineData("op_Addition")]
    public void NonConstructor_StillRoutesThroughTheSanitizer(string metadataName)
    {
        Assert.Equal(
            CSharpNaming.SourceMethodName(metadataName),
            CompileBackSourceComposer.MemberIdentifierName(metadataName, isConstructor: false));
    }
}
