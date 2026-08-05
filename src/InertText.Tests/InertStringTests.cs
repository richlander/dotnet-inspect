using System.Globalization;
using System.Reflection;
using System.Text;

using InertText.Encoding;

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
        InertString value = new InertString(TextPolicy.Field, Hazard);

        Assert.True(InertString.IsPermitted(TextPolicy.Field, value.ToString()));
        Assert.True(value.WasEncoded);
        Assert.Equal(VisualForm.BmpHex, value.Forms);
    }

    [Fact]
    public void Encode_RoundTripsThroughTheDecoder()
    {
        InertString value = new InertString(TextPolicy.Field, Hazard);

        Assert.True(VisualEncoder.TryDecode(value.ToString(), out string? decoded));
        Assert.Equal(Hazard, decoded);
    }

    [Fact]
    public void Format_EncodesInterpolationHoles()
    {
        InertString message = InertString.Format(TextPolicy.Field, $"url: {Hazard}");

        Assert.Equal("url: a\\u202Eb", message.ToString());
        Assert.True(InertString.IsPermitted(TextPolicy.Field, message.ToString()));
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
        InertString inner = new InertString(TextPolicy.Field, Hazard);

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
        InertString left = new InertString(TextPolicy.Field, Hazard);
        InertString right = new InertString(TextPolicy.Field, Hazard);
        InertString other = new InertString(TextPolicy.Field, "b");

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
        InertString? inner = new InertString(TextPolicy.Field, Hazard);

        InertString outer = InertString.Format(TextPolicy.Field, $"[{inner}]");

        Assert.Equal("[a\\u202Eb]", outer.ToString());
        Assert.True(VisualEncoder.TryDecode(outer.ToString(), out string? decoded));
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
        InertString only = new InertString(TextPolicy.Field, "plain");

        InertString joined = InertString.Join("\n", TextPolicy.Field, [only]);

        Assert.Equal("plain", joined.ToString());
        Assert.Equal(VisualForm.None, joined.Forms);
        Assert.Empty(joined.DescribeLegend());
    }

    [Fact]
    public void Join_MultipleValues_ReportsTheSeparatorForm()
    {
        InertString first = new InertString(TextPolicy.Field, "a");
        InertString second = new InertString(TextPolicy.Field, "b");

        InertString joined = InertString.Join("\n", TextPolicy.Field, [first, second]);

        Assert.Equal("a\\^Jb", joined.ToString());
        Assert.Equal(VisualForm.Caret, joined.Forms);
    }

    [Fact]
    public void Join_UnderProse_KeepsTheLineBreak()
    {
        InertString first = new InertString(TextPolicy.Prose, "a");
        InertString second = new InertString(TextPolicy.Prose, "b");

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
        InertString prose = new InertString(TextPolicy.Prose, "first\nsecond");
        Assert.Equal("first\nsecond", prose.ToString());

        // Spliced into a single-line sink, it must not carry the line feed in with it.
        InertString field = InertString.Format(TextPolicy.Field, $"source: {prose}");

        Assert.DoesNotContain('\n', field.ToString());
        Assert.Contains(@"\^J", field.ToString(), StringComparison.Ordinal);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, field.ToString()));
        Assert.True(field.Forms.HasFlag(VisualForm.Caret));
    }

    [Fact]
    public void Splice_LeavesAConformingValueByteForByteAlone()
    {
        InertString piece = new InertString(TextPolicy.Field, "a\u202Eb");
        InertString message = InertString.Format(TextPolicy.Field, $"{piece}");

        // The repair path must not fire here, or every splice would pay a decode/re-encode.
        Assert.Equal(piece.ToString(), message.ToString());
        Assert.Equal(piece.Forms, message.Forms);
    }

    [Fact]
    public void EnsurePermitted_IsIdempotent()
    {
        // The input has to carry both a prior escape and a scalar the target policy refuses, or
        // the repair path never runs and the test passes vacuously. Prose refuses the bidi
        // override (so the value arrives already carrying a backslash escape) but permits the
        // line feed (which Field then refuses, forcing the repair).
        InertString origin = new InertString(TextPolicy.Prose, "a\u202Eb\nc");
        Assert.Contains(@"\u202E", origin.ToString(), StringComparison.Ordinal);
        Assert.Contains('\n', origin.ToString());

        InertString once = origin.EnsurePermitted(TextPolicy.Field);
        InertString twice = once.EnsurePermitted(TextPolicy.Field);
        InertString thrice = twice.EnsurePermitted(TextPolicy.Field);

        // The repair must have fired, otherwise the assertions below prove nothing.
        Assert.NotEqual(origin.ToString(), once.ToString());
        Assert.Equal(once.ToString(), twice.ToString());
        Assert.Equal(once.ToString(), thrice.ToString());

        // Repair decodes before re-spelling, so the existing escape survives intact rather than
        // having its backslash escaped again. Dropping the TryDecode step doubles it here.
        Assert.Contains(@"\u202E", once.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\u202E", once.ToString(), StringComparison.Ordinal);

        // Pin the failure this guards against: the substitute a caller would otherwise reach for.
        InertString viaToString = new InertString(TextPolicy.Field, origin.ToString());
        Assert.Contains(@"\\u202E", viaToString.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EnsurePermitted_LeavesAStricterValueAloneWhenTheSinkIsLaxer()
    {
        // The one splice production actually performs: Field-encoded lines joined under Prose
        // (FeedFailureTelemetry.DescribeFailure). Field's output satisfies Prose, so the repair
        // must not fire -- if it did, every failure message would pay a decode/re-encode and the
        // Field spellings would be undone into text Prose permits.
        foreach (string source in new[] { "a\u202Eb", "x\ny", "p\u0007q", "\uD800lone" })
        {
            InertString strict = new InertString(TextPolicy.Field, source);
            InertString spliced = strict.EnsurePermitted(TextPolicy.Prose);

            Assert.Equal(strict.ToString(), spliced.ToString());
            Assert.Equal(strict.Forms, spliced.Forms);
        }
    }

    [Fact]
    public void WasEncoded_DoesNotAnswerWhetherAPolicyIsSatisfied()
    {
        // WasEncoded reports what was done, not what is satisfied, and it is wrong in both
        // directions. A sink that used it as a conformance check would admit the first value and
        // needlessly repair the second.
        InertString prose = new InertString(TextPolicy.Prose, "line1\nline2");
        Assert.False(prose.WasEncoded);
        Assert.False(InertString.IsPermitted(TextPolicy.Field, prose.ToString()));

        InertString field = new InertString(TextPolicy.Field, "a\u202Eb");
        Assert.True(field.WasEncoded);
        Assert.True(InertString.IsPermitted(TextPolicy.Field, field.ToString()));
        Assert.True(InertString.IsPermitted(TextPolicy.Prose, field.ToString()));
    }

    [Fact]
    public void Join_ReEncodesValuesThatThePolicyRefuses()
    {
        InertString[] values =
        [
            new InertString(TextPolicy.Prose, "one\ntwo"),
            new InertString(TextPolicy.Field, "three"),
        ];

        InertString joined = InertString.Join(", ", TextPolicy.Field, values);

        Assert.DoesNotContain('\n', joined.ToString());
        Assert.True(InertString.IsPermitted(TextPolicy.Field, joined.ToString()));
        Assert.True(joined.Forms.HasFlag(VisualForm.Caret));
    }

    [Fact]
    public void Splice_ReportsEveryFormTheRepairIntroduced()
    {
        InertString prose = new InertString(TextPolicy.Prose, "a\nb");
        Assert.Equal(VisualForm.None, prose.Forms);

        InertString field = InertString.Format(TextPolicy.Field, $"{prose}");

        // Forms would be None if the splice trusted the incoming value's flags.
        Assert.NotEqual(VisualForm.None, field.Forms);
        Assert.NotEmpty(field.DescribeLegend());
    }

    [Fact]
    public void Empty_EqualsAnEncodedEmptyString()
    {
        InertString encoded = new InertString(TextPolicy.Field, "");

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
        InertString strict = new InertString(TextPolicy.Field, "a\nb");
        InertString lax = InertString.Format(TextPolicy.Prose, $"{strict}");

        Assert.Equal(@"a\^Jb", lax.ToString());
        Assert.DoesNotContain('\n', lax.ToString());

        // So splice path is observable, and deliberately so.
        Assert.NotEqual(InertString.Format(TextPolicy.Prose, $"{"a\nb"}"), lax);
    }

    [Fact]
    public void Splice_IsAFixedPointUnderTheSamePolicy()
    {
        InertString piece = new InertString(TextPolicy.Prose, "x\ny");
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
            new InertString(TextPolicy.Field, ""),
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
        string source = "nothing to encode";
        InertString value = new InertString(TextPolicy.Field, source);

        Assert.Same(source, value.ToString());
        Assert.Same(value.ToString(), value.ToString());
    }

    [Fact]
    public void SpanInput_AllocatesOnlyTheRetainedString()
    {
        ReadOnlySpan<char> source = "nothing to encode".AsSpan();

        InertString value = new InertString(TextPolicy.Field, source);

        Assert.Equal(source, value.ToString());
    }

    [Fact]
    public void SpanPayloadApis_MatchStringInputs()
    {
        ReadOnlySpan<char> hazard = Hazard.AsSpan();
        InertString value = new InertString(TextPolicy.Field, hazard);
        InertString bounded = new InertString(TextPolicy.Field, hazard, maxLength: 7);
        InertString joined = InertString.Join(
            ", ".AsSpan(),
            TextPolicy.Field,
            [value, new InertString(TextPolicy.Field, "tail")]);
        InertString formatted = InertString.Format(TextPolicy.Field, $"[{hazard}]");

        Assert.Equal("a\\u202Eb", value.ToString());
        Assert.True(bounded.IsTruncated);
        Assert.False(InertString.IsPermitted(TextPolicy.Field, hazard));
        Assert.Equal("a\\u202Eb, tail", joined.ToString());
        Assert.Equal("[a\\u202Eb]", formatted.ToString());
    }

    [Fact]
    public void FromEncoded_StringRetainsTheValidatedInstance()
    {
        string encoded = new InertString(TextPolicy.Prose, "first\nvalue\u202Etail").ToString();

        InertString restored = InertString.FromEncoded(TextPolicy.Prose, encoded);

        Assert.Same(encoded, restored.ToString());
        Assert.Equal(VisualForm.BmpHex, restored.Forms);
        Assert.False(restored.IsTruncated);
    }

    [Fact]
    public void FromEncoded_SpanAllocatesTheRetainedString()
    {
        const string Encoded = "first\nvalue\\u202Etail";

        InertString restored = InertString.FromEncoded(
            TextPolicy.Prose,
            Encoded.AsSpan());

        Assert.Equal(Encoded, restored.ToString());
        Assert.Equal(VisualForm.BmpHex, restored.Forms);
    }

    [Fact]
    public void FromEncoded_AcceptsStricterSpellingComposedIntoLaxerPolicy()
    {
        InertString field = new InertString(TextPolicy.Field, "first\nsecond");
        InertString prose = InertString.Format(TextPolicy.Prose, $"{field}");

        InertString restored = InertString.FromEncoded(
            TextPolicy.Prose,
            prose.ToString());

        Assert.Equal("first\\^Jsecond", restored.ToString());
        Assert.Equal(VisualForm.Caret, restored.Forms);
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("\\x")]
    [InlineData(@"C:\tmp\package")]
    [InlineData(@"C:\Users\rich\.nuget\packages")]
    [InlineData("\\u001F")]
    [InlineData("\\U0000202E")]
    public void FromEncoded_AcceptsUnambiguousLiteralBackslashes(string encoded)
    {
        InertString restored = InertString.FromEncoded(TextPolicy.Field, encoded);

        Assert.Same(encoded, restored.ToString());
        Assert.Equal(VisualForm.None, restored.Forms);
        Assert.False(restored.RequiredContainment);
        Assert.False(restored.NeedsRawDecoding);
    }

    [Theory]
    [InlineData(TextPolicy.Field, "first\nsecond")]
    [InlineData(TextPolicy.Prose, "value\u202Etail")]
    [InlineData(TextPolicy.Field, "\\u0041")]
    [InlineData(TextPolicy.Field, "\\U0001F600")]
    public void FromEncoded_RejectsTextTheEncoderCannotProduce(
        TextPolicy policy,
        string encoded)
    {
        Assert.Throws<FormatException>(
            () => InertString.FromEncoded(policy, encoded));
    }

    [Fact]
    public void CompositionProtectsBackslashesThatBecomeSpellingPrefixes()
    {
        InertString slash = new(TextPolicy.Field, "\\");
        InertString composed = InertString.Format(TextPolicy.Field, $"{slash}u202E");

        Assert.Equal(@"\\u202E", composed.ToString());
        Assert.Equal(VisualForm.Backslash, composed.Forms);
        Assert.False(composed.RequiredContainment);
        Assert.True(composed.NeedsRawDecoding);
        Assert.True(VisualEncoder.TryDecode(composed.ToString(), out string? decoded));
        Assert.Equal(@"\u202E", decoded);
    }

    [Fact]
    public void CompositionReturnsToRawBackslashesWhenNoPrefixCollisionAppears()
    {
        InertString slash = new(TextPolicy.Field, "\\");
        InertString composed = InertString.Format(TextPolicy.Field, $"[{slash}]");

        Assert.Equal(@"[\]", composed.ToString());
        Assert.Equal(VisualForm.None, composed.Forms);
        Assert.False(composed.NeedsRawDecoding);
    }

    [Fact]
    public void FromEncoded_FailureDoesNotEchoTheInvalidText()
    {
        const string Invalid = "SHOULD-NOT-REACH-DIAGNOSTIC\u202E";

        FormatException exception = Assert.Throws<FormatException>(
            () => InertString.FromEncoded(TextPolicy.Field, Invalid));

        Assert.Equal(
            -1,
            exception.Message.IndexOf(
                "SHOULD-NOT-REACH-DIAGNOSTIC",
                StringComparison.Ordinal));
        Assert.Equal(-1, exception.Message.IndexOf("\u202E", StringComparison.Ordinal));
    }

    [Fact]
    public void FromEncoded_AcceptsComposedSurrogateEscapesAsBmpForms()
    {
        InertString restored = InertString.FromEncoded(
            TextPolicy.Field,
            "\\uD83D\\uDE00");

        Assert.Equal("\\uD83D\\uDE00", restored.ToString());
        Assert.Equal(VisualForm.BmpHex, restored.Forms);
    }

    [Fact]
    public void FromEncoded_RestoresEveryScalarWithoutCopying()
    {
        foreach (TextPolicy policy in Enum.GetValues<TextPolicy>())
        {
            for (int codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
            {
                if (!Rune.IsValid(codePoint))
                    continue;

                string original = new Rune(codePoint).ToString();
                InertString inert = new InertString(policy, original);
                string encoded = inert.ToString();

                InertString restored = InertString.FromEncoded(policy, encoded);

                Assert.True(
                    restored.Equals(inert),
                    $"U+{codePoint:X} did not restore under {policy}.");
                Assert.Same(encoded, restored.ToString());
            }
        }
    }

    [Fact]
    public void FromEncoded_RestoresEveryUnpairedSurrogate()
    {
        for (int codeUnit = 0xD800; codeUnit <= 0xDFFF; codeUnit++)
        {
            string original = new([(char)codeUnit]);
            InertString inert = new InertString(TextPolicy.Field, original);
            string encoded = inert.ToString();

            InertString restored = InertString.FromEncoded(
                TextPolicy.Field,
                encoded);

            Assert.True(
                restored.Equals(inert),
                $"U+{codeUnit:X4} did not restore.");
            Assert.Same(encoded, restored.ToString());
        }
    }

    [Fact]
    public void SpanOverloads_CoverEveryPayloadStringInput()
    {
        var missing = new List<string>();

        CheckConstructors(typeof(InertString));
        CheckMethods(
            typeof(InertString),
            BindingFlags.Public | BindingFlags.Static,
            ["FromEncoded", "IsPermitted", "Join"]);
        CheckMethods(
            typeof(InertStringHandler),
            BindingFlags.Public | BindingFlags.Instance,
            ["AppendFormatted", "AppendLiteral"],
            static parameters => parameters.Length == 1);
        CheckMethods(
            typeof(VisualEncoder),
            BindingFlags.Public | BindingFlags.Static,
            ["Encode", "TryDecode"]);

        Assert.Empty(missing);

        void CheckConstructors(Type type)
        {
            foreach (ConstructorInfo constructor in type.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Check(type, ".ctor", constructor.GetParameters(), isConstructor: true);
            }
        }

        void CheckMethods(
            Type type,
            BindingFlags flags,
            string[] names,
            Func<ParameterInfo[], bool>? include = null)
        {
            foreach (MethodInfo method in type.GetMethods(flags)
                .Where(m => names.Contains(m.Name, StringComparer.Ordinal)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (include is null || include(parameters))
                    Check(type, method.Name, parameters, isConstructor: false);
            }
        }

        void Check(
            Type type,
            string name,
            ParameterInfo[] parameters,
            bool isConstructor)
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                if (parameters[index].ParameterType != typeof(string))
                    continue;

                Type[] counterpart = parameters
                    .Select((parameter, candidate) => candidate == index
                        ? typeof(ReadOnlySpan<char>)
                        : parameter.ParameterType)
                    .ToArray();

                bool exists = isConstructor
                    ? type.GetConstructor(counterpart) is not null
                    : type.GetMethod(name, counterpart) is not null;

                if (!exists)
                    missing.Add($"{type.Name}.{name}({Describe(parameters)})");
            }
        }

        static string Describe(ParameterInfo[] parameters)
            => string.Join(", ", parameters.Select(p => p.ParameterType.Name));
    }

    [Fact]
    public void Equality_ComparesTextRatherThanTheBackingInstance()
    {
        // Equality is by text, not by instance. Cheap to state and cheap to keep, and it
        // pins the property that a representation change must not quietly drop.
        string one = "a\u202Eb";
        string other = string.Concat("a\u202E", "b");
        Assert.False(ReferenceEquals(one, other));

        InertString left = new InertString(TextPolicy.Field, one);
        InertString right = new InertString(TextPolicy.Field, other);

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
            p.ParameterType == typeof(TextPolicy)
            || p.ParameterType == typeof(InertStringHandler));

        static string Describe(ParameterInfo[] parameters)
            => string.Join(", ", parameters.Select(p => p.ParameterType.Name));
    }

    /// <summary>
    /// The auditable invariant: holding an <see cref="InertString"/> does not let you recover
    /// the text it was built from.
    /// </summary>
    /// <remarks>
    /// This is what the namespace split buys, and it is worth stating as a property rather than
    /// as a layout convention, because layout is what drifts. The decoder is the one operation
    /// that turns an inert value back into the hostile original, so it lives in
    /// <c>InertText.Encoding</c> and nothing in the currency namespace may offer a way to reach
    /// it. A reviewer can then read a file's using block instead of tracing its call graph: no
    /// <c>using InertText.Encoding</c> means no path back to the original, for every value that
    /// file touches.
    ///
    /// Enumerated rather than spot-checked, and accounted one by one, because the failure this
    /// guards against is an <em>addition</em> — a convenience overload that hands back the
    /// decoded form — and a test that asserts specific members exist would not notice one.
    ///
    /// The boundary is auditable, not unforgeable. A file can name the capability namespace and
    /// this test does not stop it. What it does stop is the capability arriving somewhere that
    /// looks like it has not got it.
    ///
    /// The search string is <c>InertText.Encoding</c>, not <c>using InertText.Encoding</c>. A
    /// using directive is one of two ways to name a namespace, and the other needs no directive
    /// at all — <c>InertText.Encoding.VisualEncoder.TryDecode(...)</c> compiles in a file with an
    /// empty import block. Searching for the directive would show a clean import list for a file
    /// that decodes. The bare namespace catches both, because a fully-qualified call has to
    /// spell it too.
    /// </remarks>
    [Fact]
    public void NoPublicMemberOfTheCurrencyNamespaceReturnsText()
    {
        List<string> crossings = [];

        foreach (Type type in typeof(InertString).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == "InertText"))
        {
            foreach (MethodInfo method in type.GetMethods(Declared))
            {
                if (IsTextual(method.ReturnType)
                    || method.GetParameters().Any(p => p.IsOut && IsTextual(p.ParameterType)))
                {
                    crossings.Add($"{type.Name}.{method.Name}");
                }
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                if (IsTextual(property.PropertyType))
                {
                    crossings.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        // Each of these hands back text, and each is accounted for by what that text is.
        string[] accounted =
        [
            // The encoded form. Handing it back is the type's purpose, and it is the treated
            // spelling rather than the original, so it is not a way back.
            "InertString.ToString",

            // Names the spellings emitted -- "\\uXXXX  the scalar at code point U+XXXX". Fixed
            // strings chosen by VisualForm flags; no artifact text reaches them.
            "InertString.DescribeLegend",

            // The record's generated ToString. Its three fields are an index, a code point and
            // a Unicode category, which is exactly why the violation reports an int rather than
            // the character: a survey can name what it refused without echoing it.
            "ScalarViolation.ToString",
        ];

        Assert.Equal(accounted.Order(), crossings.Order());

        static bool IsTextual(Type type)
        {
            Type bare = type.IsByRef ? type.GetElementType()! : type;

            // char-shaped returns count too. Span<char>/ReadOnlySpan<char>/Memory<char> and
            // char[] hand back the same text a string would, and a gate whose whole value is
            // catching a *future* addition should not be blind to the spellings an addition
            // would plausibly use.
            return bare == typeof(string)
                || bare == typeof(string[])
                || bare == typeof(char[])
                || (bare.IsGenericType
                    && bare.GetGenericArguments().Any(a => a == typeof(string) || a == typeof(char)));
        }
    }

    /// <summary>
    /// The other half: the decoder is reachable, but only by naming the capability namespace.
    /// </summary>
    /// <remarks>
    /// Without this, the test above could be satisfied by deleting the decoder outright, which
    /// would take invertibility with it -- and invertibility is what lets <c>EnsurePermitted</c> repair
    /// a spliced value instead of rejecting it.
    /// </remarks>
    [Fact]
    public void TheDecoderLivesInTheCapabilityNamespace()
    {
        MethodInfo? decode = typeof(VisualEncoder).GetMethod(
            nameof(VisualEncoder.TryDecode),
            [typeof(string), typeof(string).MakeByRefType()]);

        Assert.NotNull(decode);
        Assert.Equal("InertText.Encoding", typeof(VisualEncoder).Namespace);

        InertString inert = new(TextPolicy.Field, "\u202Ecmd");
        Assert.True(VisualEncoder.TryDecode(inert.ToString(), out string? original));
        Assert.Equal("\u202Ecmd", original);
    }

    /// <summary>
    /// The third leg, and the one that makes the other two worth having: producing inert text
    /// never requires naming the capability namespace.
    /// </summary>
    /// <remarks>
    /// The audit this design sells is a search — "which files can recover the original?" — and a
    /// search is only worth running if its answer is small. That is a property of the
    /// <em>producing</em> side, not the decoding side. If the only way to make an
    /// <see cref="InertString"/> were to call the encoder, then every file that produces inert
    /// text would name <c>InertText.Encoding</c>, the decoder would sit one member access away in
    /// all of them, and the search would return the whole producer set. The signal would survive
    /// in form and be worthless in practice.
    ///
    /// The measurement that matters is taken on a tree that has producers: every file that
    /// produces inert text does so without naming the capability namespace, so routing production
    /// through the encoder would turn each producer into a false positive. That is why the
    /// constructor is not sugar over <c>VisualEncoder.Encode</c> — it is what keeps the
    /// false-positive rate at zero. The count itself lives with the change that adds producers,
    /// since this branch ships the library alone.
    ///
    /// The claim is about <em>production</em> code, and deliberately so. This test file names
    /// <c>InertText.Encoding</c> itself, as do the encoder's own tests: invertibility is a
    /// contract, so something has to decode in order to check it. A test that can decode is the
    /// system working, not a leak — the search that matters is over the files that ship. Read
    /// the counts above as production-only.
    ///
    /// Two distinct regressions are gated here, because both look like tidying:
    /// <list type="bullet">
    /// <item>Removing the public constructor in favour of an encoder factory, on the grounds
    /// that the constructor "just forwards".</item>
    /// <item>Moving <c>TextPolicy</c> next to the encoder, on the grounds that the policy is
    /// "part of encoding". The creation path would still exist and would still drag the
    /// namespace in with it.</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void InertTextCanBeProducedWithoutNamingTheCapabilityNamespace()
    {
        const string Capability = "InertText.Encoding";

        // Every public way to obtain an InertString: constructors, and static members that
        // hand one back. Enumerated so a creation path added later is covered without an edit.
        List<(string Name, Type[] Needs)> paths =
        [
            .. typeof(InertString).GetConstructors(Declared)
                .Select(c => ($".ctor({Describe(c.GetParameters())})",
                    c.GetParameters().Select(p => p.ParameterType).ToArray())),
            .. typeof(InertString).GetMethods(Declared)
                .Where(m => m.IsStatic && m.ReturnType == typeof(InertString))
                .Select(m => ($"{m.Name}({Describe(m.GetParameters())})",
                    m.GetParameters().Select(p => p.ParameterType).ToArray())),
        ];

        // Non-vacuity. Without this the test passes by there being no creation path at all,
        // which is precisely the regression it exists to catch.
        Assert.NotEmpty(paths);

        (string Name, Type[] Needs)[] dragIn = [.. paths
            .Where(p => p.Needs.Any(t => Namespaces(t).Any(ns => ns == Capability)))];

        // Every path, not merely one of them. "Some way in is clean" is not the property --
        // a caller uses the path that fits its call site, so a single contaminated overload is
        // a hole for everyone who reaches for it.
        Assert.True(
            dragIn.Length == 0,
            $"These ways to build an InertString name a type from '{Capability}', so the files "
                + "that use them must import the decoder and the audit search stops being worth "
                + "running: "
                + string.Join("; ", dragIn.Select(p => p.Name)));

        // The policy a caller must name has to be reachable from the currency namespace too --
        // a self-contained signature is no use if the only TextPolicy values live next to the
        // decoder.
        Assert.Equal("InertText", typeof(TextPolicy).Namespace);

        // Smoke check that the reflected signature is actually callable as described.
        InertString produced = new(TextPolicy.Field, "\u202Ecmd");
        Assert.True(produced.WasEncoded);

        static IEnumerable<string> Namespaces(Type type)
        {
            Type bare = type.IsByRef ? type.GetElementType()! : type;

            yield return bare.Namespace ?? string.Empty;

            foreach (Type argument in bare.IsGenericType ? bare.GetGenericArguments() : [])
            {
                foreach (string nested in Namespaces(argument))
                {
                    yield return nested;
                }
            }
        }

        static string Describe(ParameterInfo[] parameters)
            => string.Join(", ", parameters.Select(p => p.ParameterType.Name));
    }

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// An already-inert value reached through a generic parameter is not encoded a second time.
    /// </summary>
    /// <remarks>
    /// Overload resolution binds on the <em>static</em> type of an interpolation hole, so the
    /// dedicated <c>AppendFormatted(InertString)</c> is invisible from inside a generic method:
    /// <c>T</c> is not <see cref="InertString"/> at the call site even when it is at run time.
    /// The generic overload then called <c>ToString</c> on already-encoded text and handed it
    /// back to the encoder, doubling every backslash. The trap was known for
    /// <see cref="InertString"/> and <c>InertString?</c> and closed with overloads; those
    /// overloads cannot reach this case, and only a type test can.
    /// </remarks>
    [Fact]
    public void Format_DoesNotDoubleEncode_WhenReachedThroughAGenericParameter()
    {
        InertString inner = new(TextPolicy.Field, Hazard);

        InertString direct = InertString.Format(TextPolicy.Field, $"{inner}");
        InertString viaGeneric = Wrap(inner);

        Assert.Equal(@"a\u202Eb", direct.ToString());
        Assert.Equal(direct.ToString(), viaGeneric.ToString());

        static InertString Wrap<T>(T value) => InertString.Format(TextPolicy.Field, $"{value}");
    }

    /// <summary>The same hole, reached through <see cref="object"/>.</summary>
    [Fact]
    public void Format_DoesNotDoubleEncode_WhenReachedThroughObject()
    {
        InertString inner = new(TextPolicy.Field, Hazard);
        object boxed = inner;

        Assert.Equal(
            @"a\u202Eb",
            InertString.Format(TextPolicy.Field, $"{boxed}").ToString());
    }

    /// <summary>The same hole again, through a hole that carries a format specifier.</summary>
    /// <remarks>
    /// A separate overload with its own body, so closing one and not the other is a live risk.
    /// </remarks>
    [Fact]
    public void Format_DoesNotDoubleEncode_WhenTheHoleCarriesAFormatSpecifier()
    {
        object boxed = new InertString(TextPolicy.Field, Hazard);

        Assert.Equal(
            @"a\u202Eb",
            InertString.Format(TextPolicy.Field, $"{boxed:X}").ToString());
    }

    /// <summary>
    /// Repairing a value for a policy yields text that policy accepts, for every ordered pair.
    /// </summary>
    /// <remarks>
    /// The property a caller-supplied predicate could not have. An allow-shaped predicate --
    /// the shape needed to catch a homoglyph -- refuses the escape alphabet itself, so
    /// <c>EnsurePermitted</c> returned text its own policy rejected, silently. Every
    /// <see cref="TextPolicy"/> is deny-shaped and permits graphic punctuation, so the escape
    /// spellings always conform, and the closed set is what makes that a checkable claim rather
    /// than a hope about what callers will pass.
    ///
    /// Swept over the full cross product, so a policy added later is covered without an edit.
    /// </remarks>
    [Fact]
    public void EnsurePermitted_AlwaysReturnsTextTheTargetPolicyAccepts()
    {
        const string Nasty = "a\u202Eb\u001B[31m\nc\td\\e\uD83D\uDE00";

        foreach (TextPolicy from in Enum.GetValues<TextPolicy>())
        {
            foreach (TextPolicy to in Enum.GetValues<TextPolicy>())
            {
                InertString repaired = new InertString(from, Nasty).EnsurePermitted(to);

                Assert.True(
                    InertString.IsPermitted(to, repaired.ToString(), out ScalarViolation? violation),
                    $"{from} -> {to} produced {violation}");
            }
        }
    }

    /// <summary>
    /// Repair is idempotent for every ordered pair, not only the one the case above covers.
    /// </summary>
    /// <remarks>
    /// This is what injectivity in the decoder buys, and it is why a second accepted spelling
    /// there would be a defect here.
    /// </remarks>
    [Fact]
    public void EnsurePermitted_IsIdempotent_ForEveryPolicyPair()
    {
        const string Nasty = "a\u202Eb\u001B[31m\nc\td\\e";

        foreach (TextPolicy from in Enum.GetValues<TextPolicy>())
        {
            foreach (TextPolicy to in Enum.GetValues<TextPolicy>())
            {
                InertString once = new InertString(from, Nasty).EnsurePermitted(to);
                InertString twice = once.EnsurePermitted(to);

                Assert.Equal(once.ToString(), twice.ToString());
            }
        }
    }

    /// <summary>
    /// No public member takes a delegate, which is what keeps a repair from running caller code.
    /// </summary>
    /// <remarks>
    /// The reason <see cref="TextPolicy"/> is an enum rather than a predicate. A repair decodes
    /// the value first -- it has to, or the backslashes double -- so a caller-supplied predicate
    /// would be handed the hostile original one scalar at a time, in a file whose using block
    /// names only the currency namespace. That is the audit boundary walked back out through a
    /// callback, and no reflection test over return types can see it, because the disclosure is
    /// an argument rather than a result.
    ///
    /// Stated over the whole public surface rather than as "EnsurePermitted takes an enum", so
    /// that reintroducing a predicate anywhere -- as an overload, on the handler, as an optional
    /// escape valve -- fails here.
    /// </remarks>
    [Fact]
    public void NoPublicMemberOfTheCurrencyNamespaceTakesADelegate()
    {
        List<string> callbacks = [];

        foreach (Type type in typeof(InertString).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == "InertText"))
        {
            foreach (MethodBase member in type.GetMethods(Declared)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(Declared)))
            {
                foreach (ParameterInfo parameter in member.GetParameters())
                {
                    Type bare = parameter.ParameterType.IsByRef
                        ? parameter.ParameterType.GetElementType()!
                        : parameter.ParameterType;

                    if (typeof(Delegate).IsAssignableFrom(bare))
                    {
                        callbacks.Add($"{type.Name}.{member.Name}({bare.Name})");
                    }
                }
            }
        }

        Assert.Empty(callbacks);
    }

    /// <summary>
    /// A value composed from fragments that were each encoded separately still decodes, so the
    /// repair in <see cref="InertString.EnsurePermitted"/> reaches its decode path.
    /// </summary>
    /// <remarks>
    /// The invariant the fallback in <c>EnsurePermitted</c> rests on. When it broke, nothing
    /// threw: the fallback treated the library's own encoded text as raw and encoded it a second
    /// time, which is only visible by comparing against a directly encoded value.
    ///
    /// A lone high surrogate and a lone low surrogate are the case that broke it, because each
    /// encodes to an escape alone and the two land adjacent once concatenated.
    /// </remarks>
    [Fact]
    public void Compose_OfFragmentsEncodedSeparately_StillDecodes()
    {
        InertString composed = InertString.Join(
            string.Empty,
            TextPolicy.Prose,
            [
                new InertString(TextPolicy.Prose, "\uD834"),
                new InertString(TextPolicy.Prose, "\uDD73"),
                new InertString(TextPolicy.Prose, "\n"),
            ]);

        Assert.True(
            VisualEncoder.TryDecode(composed.ToString(), out _),
            "a value this library composed must decode, or EnsurePermitted over-encodes it");
    }

    /// <summary>
    /// Splitting a surrogate pair across a composition boundary and repairing the result yields
    /// the same value as encoding that astral scalar directly.
    /// </summary>
    /// <remarks>
    /// "\uD834" + "\uDD73" is "\U0001D173" -- one scalar, not two halves -- so the two routes
    /// describe the same text and must agree. They did not: the composed spelling failed to
    /// decode, so the repair re-encoded its backslashes and produced \\uD834\\uDD73.
    /// </remarks>
    [Fact]
    public void Compose_OfTwoLoneSurrogates_RepairsToTheSameValueAsEncodingThePairDirectly()
    {
        InertString composed = InertString.Join(
            string.Empty,
            TextPolicy.Prose,
            [
                new InertString(TextPolicy.Prose, "\uD834"),
                new InertString(TextPolicy.Prose, "\uDD73"),
                new InertString(TextPolicy.Prose, "\n"),
            ]);

        InertString repaired = composed.EnsurePermitted(TextPolicy.Field);
        InertString direct = new(TextPolicy.Field, "\U0001D173\n");

        Assert.Equal(direct, repaired);
    }

    /// <summary>
    /// An astral scalar split across a composition boundary does not repair to the same value as
    /// the ASCII text that spells its halves.
    /// </summary>
    /// <remarks>
    /// The consequence of the two above, and the reason they are worth having. Encoding is meant
    /// to be injective: distinct inputs keep distinct spellings, which is what lets a reader
    /// treat the encoded form as evidence of what arrived. These two inputs converged on
    /// <c>\\uD834\\uDD73\^J</c>, so the rendered output no longer said which one produced it.
    /// </remarks>
    [Fact]
    public void Compose_DoesNotConverge_OnTheAsciiTextThatSpellsTheSurrogateHalves()
    {
        InertString newline = new(TextPolicy.Prose, "\n");

        InertString fromScalar = InertString.Join(
            string.Empty,
            TextPolicy.Prose,
            [
                new InertString(TextPolicy.Prose, "\uD834"),
                new InertString(TextPolicy.Prose, "\uDD73"),
                newline,
            ]).EnsurePermitted(TextPolicy.Field);

        InertString fromAsciiText = InertString.Join(
            string.Empty,
            TextPolicy.Prose,
            [
                new InertString(TextPolicy.Prose, @"\uD834\uDD73"),
                newline,
            ]).EnsurePermitted(TextPolicy.Field);

        Assert.NotEqual(fromAsciiText, fromScalar);
    }

    /// <summary>
    /// No project imports <c>InertText.Encoding</c> for every file at once, so naming the
    /// capability namespace stays a per-file act and the audit search keeps file granularity.
    /// </summary>
    /// <remarks>
    /// The audit sold by <c>docs/design/inert-text.md</c> is a search for the bare string
    /// <c>InertText.Encoding</c>, on the reasoning that both ways to reach a namespace spell it:
    /// a using directive, and a fully-qualified call. There is a third way, and it spells the
    /// namespace somewhere else entirely:
    ///
    /// <code>
    /// // one file, or a &lt;Using Include="InertText.Encoding" /&gt; item in the .csproj
    /// global using InertText.Encoding;
    /// </code>
    ///
    /// Every other file in that project can then call <c>VisualEncoder.TryDecode</c> with no
    /// local mention of the namespace at all. The search still finds the import — it is still
    /// text in the repository — but it stops answering "which files can decode?" and starts
    /// answering "which projects can decode?", and the file it points at is not the file doing
    /// the decoding. A reviewer reading a clean import list would conclude the file cannot
    /// decode, which is the failure the bare-string rule exists to prevent.
    ///
    /// So the granularity is an invariant of the build, not of the language, and it is gated
    /// here rather than asserted in prose. Nothing needs a project-wide encoder import:
    /// production does not name the namespace at all, and the test projects that legitimately
    /// decode do it with ordinary per-file directives.
    /// </remarks>
    [Fact]
    public void NoProjectImportsTheCapabilityNamespaceForEveryFileAtOnce()
    {
        const string Capability = "InertText.Encoding";

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "dotnet-inspect.slnx")))
        {
            root = root.Parent;
        }

        Assert.True(root is not null, "could not locate the repository root from the test binary");

        string source = Path.Combine(root!.FullName, "src");
        string[] candidates =
        [
            .. Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(source, "*.props", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(source, "*.targets", SearchOption.AllDirectories),
            // Repo-root build files are outside src/ but flow into every project under it, so a
            // <Using Include> there is the widest-reaching version of exactly this hazard.
            .. Directory.EnumerateFiles(root.FullName, "Directory.Build.*", SearchOption.TopDirectoryOnly),
        ];

        // Non-vacuity: a root that resolved to somewhere without sources would pass silently,
        // which is the same shape of bug as the invariant being gated.
        Assert.NotEmpty(candidates);

        List<string> offenders = [];
        foreach (string file in candidates)
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // Line-wise, skipping comments, because the example in this test's own doc comment
            // is the exact text being searched for -- and a commented-out import is not one.
            bool offends = File.ReadLines(file).Any(line =>
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    return false;
                }

                if (trimmed.Contains($"<Using Include=\"{Capability}\"", StringComparison.Ordinal))
                {
                    return true;
                }

                // global:: is a legal and equivalent spelling of the same import, so match it too
                // rather than letting one qualifier walk past the gate.
                const string Directive = "global using ";
                if (!trimmed.StartsWith(Directive, StringComparison.Ordinal))
                {
                    return false;
                }

                string imported = trimmed[Directive.Length..].TrimStart();
                if (imported.StartsWith("global::", StringComparison.Ordinal))
                {
                    imported = imported["global::".Length..];
                }

                return imported.StartsWith($"{Capability};", StringComparison.Ordinal);
            });

            if (offends)
            {
                offenders.Add(Path.GetRelativePath(root.FullName, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"A project-wide import of '{Capability}' lets any file in that project decode with "
                + "no local mention of the namespace, so grepping the bare string no longer says "
                + "which files can recover the original: "
                + string.Join("; ", offenders));
    }

    [Fact]
    public void ProductionCapabilityReferences_AreAnExplicitAllowList()
    {
        const string Capability = "InertText.Encoding";
        DirectoryInfo root = FindRepositoryRoot();
        string source = Path.Combine(root.FullName, "src");

        string[] actual =
        [
            .. Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Where(file => !Path.GetRelativePath(source, file)
                    .Split(Path.DirectorySeparatorChar)
                    .Any(segment => segment.EndsWith(".Tests", StringComparison.Ordinal)))
                .Where(file => File.ReadAllText(file).Contains(Capability, StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(root.FullName, file))
                .Order(StringComparer.Ordinal)
        ];

        string[] expected =
        [
            Path.Combine("src", "DotnetInspector.MetadataRendering", "MetadataProjectionRenderer.cs"),
            Path.Combine("src", "InertText", "InertString.cs"),
            Path.Combine("src", "InertText", "VisualEncoder.cs"),
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    /// <summary>
    /// No public documentation in the currency namespace names the encoder type, so the
    /// currency type's use of it stays an implementation detail rather than an advertised one.
    /// </summary>
    /// <remarks>
    /// The capability is deliberately public and deliberately opt-in: a caller who needs to
    /// decode names <c>InertText.Encoding</c> and that act is what the audit searches for. What
    /// must not happen is the currency type handing the capability over without that opt-in.
    ///
    /// A signature cannot: a separate test walks the public surface and fails any member that
    /// mentions a type from the capability namespace. Documentation is the other route, and it
    /// is easy to miss because it changes no signature. The public constructor used to open
    /// with "Forwards to <![CDATA[<see cref="VisualEncoder"/>]]>", which is a *navigable
    /// reference* — every consumer reading the constructor in IntelliSense was pointed straight
    /// at the reversing half, and two more members described their internals the same way.
    /// Documenting what a member delegates to is describing an implementation detail as though
    /// it were part of the contract.
    ///
    /// Naming the <em>namespace</em> stays allowed, and the type-level remarks do it: saying
    /// the decoder lives in <c>InertText.Encoding</c> and that nothing here reaches it is the
    /// opt-in disclaimer rather than a shortcut to it. The line is drawn at the type name,
    /// which is the thing a caller would have to write in order to use it.
    /// </remarks>
    [Fact]
    public void NoPublicDocumentationInTheCurrencyNamespaceNamesTheEncoderType()
    {
        const string EncoderType = "VisualEncoder";

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "dotnet-inspect.slnx")))
        {
            root = root.Parent;
        }

        Assert.True(root is not null, "could not locate the repository root from the test binary");

        string library = Path.Combine(root!.FullName, "src", "InertText");
        string[] files = Directory.GetFiles(library, "*.cs", SearchOption.AllDirectories);

        // Non-vacuity: a wrong path would pass silently.
        Assert.NotEmpty(files);

        List<string> offenders = [];
        int scanned = 0;

        foreach (string file in files)
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);

            // The capability's own file documents itself; the rule is about the currency side.
            if (lines.Any(l => l.StartsWith("namespace InertText.Encoding", StringComparison.Ordinal)))
            {
                continue;
            }

            scanned++;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (!trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmed.Contains(EncoderType, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root.FullName, file)}:{i + 1}");
                }
            }
        }

        // The currency namespace is more than one file, so a rule that only ever saw
        // InertString.cs would be weaker than it looks.
        Assert.True(scanned > 1, $"expected to scan several currency-namespace files, scanned {scanned}");

        Assert.True(
            offenders.Count == 0,
            $"Public documentation in the currency namespace names '{EncoderType}', which "
                + "advertises the reversing half to every consumer reading these members and "
                + "makes an implementation detail look like part of the contract. Describe what "
                + "the member guarantees instead, and leave where the capability lives to the "
                + $"type-level remarks: {string.Join("; ", offenders)}");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null
            && !File.Exists(Path.Combine(root.FullName, "dotnet-inspect.slnx")))
        {
            root = root.Parent;
        }

        return root ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root from the test binary.");
    }
}
