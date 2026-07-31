using System.Globalization;
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

        Assert.True(InertString.IsPermitted(value.ToString(), TextPolicy.Field));
        Assert.True(value.WasEncoded);
        Assert.Equal(VisualForm.BmpHex, value.Forms);
    }

    [Fact]
    public void Encode_RoundTripsThroughTheDecoder()
    {
        InertString value = InertString.Encode(Hazard, TextPolicy.Field);

        Assert.True(InertString.TryDecode(value.ToString(), out string? decoded));
        Assert.Equal(Hazard, decoded);
    }

    [Fact]
    public void Format_EncodesInterpolationHoles()
    {
        InertString message = InertString.Format(TextPolicy.Field, $"url: {Hazard}");

        Assert.Equal("url: a\\u202Eb", message.ToString());
        Assert.True(InertString.IsPermitted(message.ToString(), TextPolicy.Field));
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
        Assert.True(InertString.TryDecode(outer.ToString(), out string? decoded));
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
    public void Format_NullableInertHole_IsNotEncodedTwice()
    {
        // Redaction returns InertString?, so without a dedicated overload this hole binds to the
        // generic case, whose ToString hands the encoded text straight back to the encoder and
        // doubles every backslash the first pass introduced.
        InertString? inner = InertString.Encode(Hazard, TextPolicy.Field);

        InertString outer = InertString.Format(TextPolicy.Field, $"[{inner}]");

        Assert.Equal("[a\\u202Eb]", outer.ToString());
        Assert.True(InertString.TryDecode(outer.ToString(), out string? decoded));
        Assert.Equal($"[{Hazard}]", decoded);
    }

    [Fact]
    public void Format_NullInertHole_ContributesNothing()
    {
        InertString? missing = null;

        InertString outer = InertString.Format(TextPolicy.Field, $"[{missing}]");

        Assert.Equal("[]", outer.ToString());
    }

    [Fact]
    public void Join_SingleValue_ReportsNoFormFromTheUnusedSeparator()
    {
        // The separator is encoded up front, so folding its forms in unconditionally would make
        // a one-element join advertise a spelling its output does not contain.
        InertString only = InertString.Encode("plain", TextPolicy.Field);

        InertString joined = InertString.Join("\n", TextPolicy.Field, [only]);

        Assert.Equal("plain", joined.ToString());
        Assert.Equal(VisualForm.None, joined.Forms);
        Assert.Empty(joined.DescribeLegend());
    }

    [Fact]
    public void Join_MultipleValues_ReportsTheSeparatorForm()
    {
        InertString first = InertString.Encode("a", TextPolicy.Field);
        InertString second = InertString.Encode("b", TextPolicy.Field);

        InertString joined = InertString.Join("\n", TextPolicy.Field, [first, second]);

        Assert.Equal("a\\^Jb", joined.ToString());
        Assert.Equal(VisualForm.Caret, joined.Forms);
    }

    [Fact]
    public void Join_UnderProse_KeepsTheLineBreak()
    {
        InertString first = InertString.Encode("a", TextPolicy.Prose);
        InertString second = InertString.Encode("b", TextPolicy.Prose);

        InertString joined = InertString.Join(Environment.NewLine, TextPolicy.Prose, [first, second]);

        Assert.Equal($"a{Environment.NewLine}b", joined.ToString());
        Assert.Equal(VisualForm.None, joined.Forms);
    }

    [Fact]
    public void Join_NoValues_IsEmpty()
    {
        InertString joined = InertString.Join("\n", TextPolicy.Field, []);

        Assert.True(joined.IsEmpty);
        Assert.Equal(VisualForm.None, joined.Forms);
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

    [Fact]
    public void Splice_ReEncodesAValueThatThisPolicyRefuses()
    {
        // Built for a multi-line sink, so the line feed survived encoding.
        InertString prose = InertString.Encode("first\nsecond", TextPolicy.Prose);
        Assert.Equal("first\nsecond", prose.ToString());

        // Spliced into a single-line sink, it must not carry the line feed in with it.
        InertString field = InertString.Format(TextPolicy.Field, $"source: {prose}");

        Assert.DoesNotContain('\n', field.ToString());
        Assert.Contains(@"\^J", field.ToString(), StringComparison.Ordinal);
        Assert.True(InertString.IsPermitted(field.ToString(), TextPolicy.Field));
        Assert.True(field.Forms.HasFlag(VisualForm.Caret));
    }

    [Fact]
    public void Splice_LeavesAConformingValueByteForByteAlone()
    {
        InertString piece = InertString.Encode("a\u202Eb", TextPolicy.Field);
        InertString message = InertString.Format(TextPolicy.Field, $"{piece}");

        // The repair path must not fire here, or every splice would pay a decode/re-encode.
        Assert.Equal(piece.ToString(), message.ToString());
        Assert.Equal(piece.Forms, message.Forms);
    }

    [Fact]
    public void Join_ReEncodesValuesThatThePolicyRefuses()
    {
        InertString[] values =
        [
            InertString.Encode("one\ntwo", TextPolicy.Prose),
            InertString.Encode("three", TextPolicy.Field),
        ];

        InertString joined = InertString.Join(", ", TextPolicy.Field, values);

        Assert.DoesNotContain('\n', joined.ToString());
        Assert.True(InertString.IsPermitted(joined.ToString(), TextPolicy.Field));
        Assert.True(joined.Forms.HasFlag(VisualForm.Caret));
    }

    [Fact]
    public void Splice_ReportsEveryFormTheRepairIntroduced()
    {
        InertString prose = InertString.Encode("a\nb", TextPolicy.Prose);
        Assert.Equal(VisualForm.None, prose.Forms);

        InertString field = InertString.Format(TextPolicy.Field, $"{prose}");

        // Forms would be None if the splice trusted the incoming value's flags.
        Assert.NotEqual(VisualForm.None, field.Forms);
        Assert.NotEmpty(field.DescribeLegend());
    }

    [Fact]
    public void Empty_EqualsAnEncodedEmptyString()
    {
        InertString encoded = InertString.Encode("", TextPolicy.Field);

        Assert.Equal(InertString.Empty, encoded);
        Assert.True(InertString.Empty == encoded);
        Assert.Equal(InertString.Empty.GetHashCode(), encoded.GetHashCode());
    }

    [Fact]
    public void Holes_FormatUnderTheInvariantCultureWithOrWithoutASpecifier()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            double value = 1234.5;

            Assert.Equal("1234.5", InertString.Format(TextPolicy.Field, $"{value}").ToString());
            Assert.Equal("1234.5", InertString.Format(TextPolicy.Field, $"{value:F1}").ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Splice_TightensButNeverLoosens()
    {
        // Field encoded the line feed; Prose would have permitted it. The repair must not
        // undo that: composition may make a value more inert, never less, or a value could be
        // laundered by quoting it into a laxer sink.
        InertString strict = InertString.Encode("a\nb", TextPolicy.Field);
        InertString lax = InertString.Format(TextPolicy.Prose, $"{strict}");

        Assert.Equal(@"a\^Jb", lax.ToString());
        Assert.DoesNotContain('\n', lax.ToString());

        // So splice path is observable, and deliberately so.
        Assert.NotEqual(InertString.Format(TextPolicy.Prose, $"{"a\nb"}"), lax);
    }

    [Fact]
    public void Splice_IsAFixedPointUnderTheSamePolicy()
    {
        InertString piece = InertString.Encode("x\ny", TextPolicy.Prose);
        InertString once = InertString.Format(TextPolicy.Field, $"{piece}");
        InertString twice = InertString.Format(TextPolicy.Field, $"{once}");

        // Re-composing must not re-encode what the repair already spelled, or a value would
        // drift every time it passed through a sink.
        Assert.Equal(once, twice);
        Assert.Equal(once.Forms, twice.Forms);
    }

    [Fact]
    public void EmptyValuesAreIndistinguishableAcrossTheWholeSurface()
    {
        // Three field states now mean "empty": the CLR zero value, the constructed Empty, and
        // an encode of "". Empty is deliberately no longer default, so their agreement is a
        // real claim rather than a tautology, and every member has to honour it.
        InertString[] empties =
        [
            default,
            InertString.Empty,
            InertString.Encode("", TextPolicy.Field),
        ];

        InertString zero = empties[0];
        InertString encoded = empties[2];

        // Enumerated rather than spot-checked. The reviewed defect was exactly one member --
        // equality -- disagreeing with the other three about whether these are the same value,
        // so the bug class is "some member treats the zero value specially" and a hand-written
        // list of members is the same gate that already failed to catch it once.
        MethodInfo[] surface = typeof(InertString)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Length == 0 && m.ReturnType != typeof(void))
            .ToArray();

        Assert.NotEmpty(surface);
        foreach (MethodInfo member in surface)
        {
            string[] answers = empties.Select(e => Describe(member.Invoke(e, null))).ToArray();
            Assert.Equal(answers.Length, answers.Count(a => a == answers[0]));
        }

        foreach (InertString left in empties)
        {
            foreach (InertString right in empties)
            {
                Assert.True(left.Equals(right));
                Assert.True(left == right);
                Assert.False(left != right);
                Assert.Equal(left.GetHashCode(), right.GetHashCode());
            }
        }

        Assert.True(zero.Equals(encoded));

        static string Describe(object? value) => value switch
        {
            null => "<null>",
            IEnumerable<string> lines => string.Join("|", lines),
            _ => value.ToString() ?? "<null>",
        };
    }

    [Fact]
    public void ToString_DoesNotCopy()
    {
        InertString value = InertString.Encode("nothing to encode", TextPolicy.Field);

        Assert.Same(value.ToString(), value.ToString());
    }

    [Fact]
    public void Equality_ComparesTextRatherThanTheBackingInstance()
    {
        // Equality is by text, not by instance. Cheap to state and cheap to keep, and it
        // pins the property that a representation change must not quietly drop.
        string one = "a\u202Eb";
        string other = string.Concat("a\u202E", "b");
        Assert.False(ReferenceEquals(one, other));

        InertString left = InertString.Encode(one, TextPolicy.Field);
        InertString right = InertString.Encode(other, TextPolicy.Field);

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void NoPublicEntryPointTakesTextWithoutAPolicy()
    {
        // The invariant is not "Encode is a static method." It is "text cannot become an
        // InertString without a policy being applied to it." The conversion test above covers
        // only op_Implicit and op_Explicit, so a public constructor or a factory taking a bare
        // string would satisfy every other gate in this file while voiding the whole design.
        List<string> unguarded = [];

        foreach (ConstructorInfo ctor in typeof(InertString)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            if (TakesText(ctor.GetParameters()) && !TakesPolicy(ctor.GetParameters()))
            {
                unguarded.Add($".ctor({Describe(ctor.GetParameters())})");
            }
        }

        foreach (MethodInfo factory in typeof(InertString)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(InertString)))
        {
            if (TakesText(factory.GetParameters()) && !TakesPolicy(factory.GetParameters()))
            {
                unguarded.Add($"{factory.Name}({Describe(factory.GetParameters())})");
            }
        }

        Assert.Empty(unguarded);

        static bool TakesText(ParameterInfo[] parameters) => parameters.Any(p =>
            p.ParameterType == typeof(string)
            || p.ParameterType == typeof(char[])
            || p.ParameterType == typeof(ReadOnlyMemory<char>)
            || p.ParameterType == typeof(ReadOnlySpan<char>));

        static bool TakesPolicy(ParameterInfo[] parameters) => parameters.Any(p =>
            p.ParameterType == typeof(ScalarPolicy)
            || p.ParameterType == typeof(InertStringHandler));

        static string Describe(ParameterInfo[] parameters)
            => string.Join(", ", parameters.Select(p => p.ParameterType.Name));
    }
}
