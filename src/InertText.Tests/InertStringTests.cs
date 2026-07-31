using System.Reflection;

namespace InertText.Tests;

/// <summary>
/// Gates the currency form: that a value cannot be built from untreated text without a policy,
/// that composition does not launder or double-encode, and that the type stays distinct from
/// <see cref="string"/>.
/// </summary>
public class InertStringTests
{
    private const string Hazard = "a\u202Eb";

    [Fact]
    public void Encode_ProducesAPayloadThePolicyAccepts()
    {
        InertString value = InertString.Encode(Hazard, TextPolicy.Field);

        Assert.True(VisualEncoder.IsPermitted(value.ToString(), TextPolicy.Field));
        Assert.True(value.WasEncoded);
        Assert.Equal(VisualForm.BmpHex, value.Forms);
    }

    [Fact]
    public void Encode_RoundTripsThroughTheDecoder()
    {
        InertString value = InertString.Encode(Hazard, TextPolicy.Field);

        Assert.True(VisualEncoder.TryDecode(value.ToString(), out string? decoded));
        Assert.Equal(Hazard, decoded);
    }

    [Fact]
    public void Format_EncodesInterpolationHoles()
    {
        InertString message = InertString.Format(TextPolicy.Field, $"url: {Hazard}");

        Assert.Equal("url: a\\u202Eb", message.ToString());
        Assert.True(VisualEncoder.IsPermitted(message.ToString(), TextPolicy.Field));
    }

    [Fact]
    public void Format_EncodesLiteralsToo()
    {
        // The invariant is unconditional on purpose. A bidi override is exactly as invisible in
        // a C# source file as it is in a feed response, so "literals are trusted" would be an
        // exemption that has to be re-argued at every call site.
        InertString message = InertString.Format(TextPolicy.Field, $"a\u202Eb {1}");

        Assert.Equal("a\\u202Eb 1", message.ToString());
    }

    [Fact]
    public void Format_AlreadyInertHole_IsNotEncodedTwice()
    {
        InertString inner = InertString.Encode(Hazard, TextPolicy.Field);

        InertString outer = InertString.Format(TextPolicy.Field, $"[{inner}]");

        Assert.Equal("[a\\u202Eb]", outer.ToString());
        Assert.True(VisualEncoder.TryDecode(outer.ToString(), out string? decoded));
        Assert.Equal($"[{Hazard}]", decoded);
    }

    [Fact]
    public void Format_UnionsTheFormsOfEveryPart()
    {
        InertString message = InertString.Format(
            TextPolicy.Field,
            $"{"\u001B"} {"\u007F"} {"\U0001D173"} {"c:\\tmp"}");

        Assert.Equal(
            VisualForm.Caret | VisualForm.CaretDelete | VisualForm.AstralHex | VisualForm.Backslash,
            message.Forms);
        Assert.Equal(4, message.DescribeLegend().Count);
    }

    [Fact]
    public void Format_OrdinaryText_IsUnchanged()
    {
        InertString message = InertString.Format(TextPolicy.Field, $"Package '{"Newtonsoft.Json"}' not found.");

        Assert.Equal("Package 'Newtonsoft.Json' not found.", message.ToString());
        Assert.False(message.WasEncoded);
        Assert.Empty(message.DescribeLegend());
    }

    [Fact]
    public void Format_RespectsThePolicyItIsGiven()
    {
        InertString field = InertString.Format(TextPolicy.Field, $"{"a\nb"}");
        InertString prose = InertString.Format(TextPolicy.Prose, $"{"a\nb"}");

        Assert.Equal("a\\^Jb", field.ToString());
        Assert.Equal("a\nb", prose.ToString());
    }

    [Fact]
    public void Empty_CarriesNoText()
    {
        Assert.True(InertString.Empty.IsEmpty);
        Assert.False(InertString.Empty.WasEncoded);
        Assert.Equal(string.Empty, InertString.Empty.ToString());
        Assert.Equal(string.Empty, default(InertString).ToString());
    }

    [Fact]
    public void Equality_ComparesTheEncodedText()
    {
        InertString left = InertString.Encode(Hazard, TextPolicy.Field);
        InertString right = InertString.Encode(Hazard, TextPolicy.Field);
        InertString other = InertString.Encode("b", TextPolicy.Field);

        Assert.True(left == right);
        Assert.True(left != other);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void NoConversionFromStringExists()
    {
        // The guard the whole design rests on. A conversion from string would let untreated
        // text satisfy an InertString parameter silently, which is precisely the confusion the
        // type was introduced to remove — so it is asserted rather than left to review.
        MethodInfo[] conversions = typeof(InertString)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Where(m => m.GetParameters() is [{ ParameterType.FullName: "System.String" }])
            .ToArray();

        Assert.Empty(conversions);
    }
}
