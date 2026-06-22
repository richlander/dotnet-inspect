using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

#nullable enable

/// <summary>
/// Slice 2 of #1113: a non-nullable value-type optional parameter encodes its
/// `= default` as a null constant, which the renderer printed as `= null`
/// (CS1750). The fix renders `= default` for value types while keeping `= null`
/// for reference types and Nullable&lt;T&gt; (both accept the null literal). Uses
/// this test assembly as the subject, like <see cref="NullabilityTests"/>.
/// </summary>
public sealed class DefaultValueRenderingTests
{
    static readonly ApiSurface Surface;

    static DefaultValueRenderingTests()
    {
        using var stream = File.OpenRead(typeof(DefaultValueRenderingTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static string Signature(string method) => Surface.Types
        .First(t => t.Name == nameof(DefaultValueFixtures))
        .Members.First(m => m.Name == method).Signature;

    [Fact]
    public void ValueTypeStruct_Default_RendersDefaultNotNull()
    {
        var sig = Signature(nameof(DefaultValueFixtures.ValueTypeStructDefault));
        Assert.Contains("ct = default", sig);
        Assert.DoesNotContain("ct = null", sig);
    }

    [Fact]
    public void CustomStruct_Default_RendersDefault()
    {
        var sig = Signature(nameof(DefaultValueFixtures.CustomStructDefault));
        Assert.Contains("s = default", sig);
        Assert.DoesNotContain("s = null", sig);
    }

    // --- regression canaries: these must stay `= null` / unchanged ---

    [Fact]
    public void RefType_Null_StaysNull()
    {
        var sig = Signature(nameof(DefaultValueFixtures.RefTypeNull));
        Assert.Contains("r = null", sig);
        Assert.DoesNotContain("= default", sig);
    }

    [Fact]
    public void NullableValue_Null_StaysNull()
    {
        // Nullable<T> is a value type but accepts the null literal as its default.
        var sig = Signature(nameof(DefaultValueFixtures.NullableValueNull));
        Assert.Contains("n = null", sig);
        Assert.DoesNotContain("n = default", sig);
    }

    [Fact]
    public void Int_Default_Unchanged()
        => Assert.Contains("i = 5", Signature(nameof(DefaultValueFixtures.IntDefault)));

    [Fact]
    public void Bool_Default_Unchanged()
        => Assert.Contains("b = true", Signature(nameof(DefaultValueFixtures.BoolDefault)));

    [Fact]
    public void Enum_NonZeroDefault_RendersMemberName()
    {
        var sig = Signature(nameof(DefaultValueFixtures.EnumNonZeroDefault));
        Assert.Contains("color = ILInspector.Metadata.Tests.DefaultValueFixtures.SampleColor.Green", sig);
        Assert.DoesNotContain("color = 1", sig);
    }

    [Fact]
    public void Enum_ZeroDefault_RendersZeroMemberName()
    {
        var sig = Signature(nameof(DefaultValueFixtures.EnumZeroDefault));
        Assert.Contains("color = ILInspector.Metadata.Tests.DefaultValueFixtures.SampleColor.Red", sig);
        Assert.DoesNotContain("color = 0", sig);
    }

    [Fact]
    public void Enum_UnnamedFlagsDefault_RendersCast()
    {
        var sig = Signature(nameof(DefaultValueFixtures.EnumUnnamedFlagsDefault));
        Assert.Contains("flags = (ILInspector.Metadata.Tests.DefaultValueFixtures.SampleFlags)3", sig);
    }

    [Fact]
    public void Enum_ExternalDefault_RendersCast()
    {
        var sig = Signature(nameof(DefaultValueFixtures.ExternalEnumDefault));
        Assert.Contains("day = (System.DayOfWeek)1", sig);
        Assert.DoesNotContain("day = 1", sig);
    }
}

public class DefaultValueFixtures
{
    public struct PlainStruct { public int X; }

    public void ValueTypeStructDefault(System.Threading.CancellationToken ct = default) { }
    public void CustomStructDefault(PlainStruct s = default) { }
    public void RefTypeNull(string? r = null) { }
    public void NullableValueNull(int? n = null) { }
    public void IntDefault(int i = 5) { }
    public void BoolDefault(bool b = true) { }
    public void EnumNonZeroDefault(SampleColor color = SampleColor.Green) { }
    public void EnumZeroDefault(SampleColor color = SampleColor.Red) { }
    public void EnumUnnamedFlagsDefault(SampleFlags flags = (SampleFlags)3) { }
    public void ExternalEnumDefault(DayOfWeek day = DayOfWeek.Monday) { }

    public enum SampleColor
    {
        Red = 0,
        Green = 1,
        Blue = 2
    }

    [Flags]
    public enum SampleFlags
    {
        One = 1,
        Two = 2
    }
}
