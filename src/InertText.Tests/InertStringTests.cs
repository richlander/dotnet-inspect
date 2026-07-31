using System.Globalization;
using System.Reflection;

using InertText.Encoder;

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
        InertString value = new InertString(Hazard, TextPolicy.Field);

        Assert.True(InertString.IsPermitted(value.ToString(), TextPolicy.Field));
        Assert.True(value.WasEncoded);
        Assert.Equal(VisualForm.BmpHex, value.Forms);
    }

    [Fact]
    public void Encode_RoundTripsThroughTheDecoder()
    {
        InertString value = new InertString(Hazard, TextPolicy.Field);

        Assert.True(VisualEncoder.TryDecode(value.ToString(), out string? decoded));
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
        InertString inner = new InertString(Hazard, TextPolicy.Field);

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
        InertString left = new InertString(Hazard, TextPolicy.Field);
        InertString right = new InertString(Hazard, TextPolicy.Field);
        InertString other = new InertString("b", TextPolicy.Field);

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
        InertString? inner = new InertString(Hazard, TextPolicy.Field);

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
        InertString only = new InertString("plain", TextPolicy.Field);

        InertString joined = InertString.Join("\n", TextPolicy.Field, [only]);

        Assert.Equal("plain", joined.ToString());
        Assert.Equal(VisualForm.None, joined.Forms);
        Assert.Empty(joined.DescribeLegend());
    }

    [Fact]
    public void Join_MultipleValues_ReportsTheSeparatorForm()
    {
        InertString first = new InertString("a", TextPolicy.Field);
        InertString second = new InertString("b", TextPolicy.Field);

        InertString joined = InertString.Join("\n", TextPolicy.Field, [first, second]);

        Assert.Equal("a\\^Jb", joined.ToString());
        Assert.Equal(VisualForm.Caret, joined.Forms);
    }

    [Fact]
    public void Join_UnderProse_KeepsTheLineBreak()
    {
        InertString first = new InertString("a", TextPolicy.Prose);
        InertString second = new InertString("b", TextPolicy.Prose);

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
        InertString prose = new InertString("first\nsecond", TextPolicy.Prose);
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
        InertString piece = new InertString("a\u202Eb", TextPolicy.Field);
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
            new InertString("one\ntwo", TextPolicy.Prose),
            new InertString("three", TextPolicy.Field),
        ];

        InertString joined = InertString.Join(", ", TextPolicy.Field, values);

        Assert.DoesNotContain('\n', joined.ToString());
        Assert.True(InertString.IsPermitted(joined.ToString(), TextPolicy.Field));
        Assert.True(joined.Forms.HasFlag(VisualForm.Caret));
    }

    [Fact]
    public void Splice_ReportsEveryFormTheRepairIntroduced()
    {
        InertString prose = new InertString("a\nb", TextPolicy.Prose);
        Assert.Equal(VisualForm.None, prose.Forms);

        InertString field = InertString.Format(TextPolicy.Field, $"{prose}");

        // Forms would be None if the splice trusted the incoming value's flags.
        Assert.NotEqual(VisualForm.None, field.Forms);
        Assert.NotEmpty(field.DescribeLegend());
    }

    [Fact]
    public void Empty_EqualsAnEncodedEmptyString()
    {
        InertString encoded = new InertString("", TextPolicy.Field);

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
        InertString strict = new InertString("a\nb", TextPolicy.Field);
        InertString lax = InertString.Format(TextPolicy.Prose, $"{strict}");

        Assert.Equal(@"a\^Jb", lax.ToString());
        Assert.DoesNotContain('\n', lax.ToString());

        // So splice path is observable, and deliberately so.
        Assert.NotEqual(InertString.Format(TextPolicy.Prose, $"{"a\nb"}"), lax);
    }

    [Fact]
    public void Splice_IsAFixedPointUnderTheSamePolicy()
    {
        InertString piece = new InertString("x\ny", TextPolicy.Prose);
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
            new InertString("", TextPolicy.Field),
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
        InertString value = new InertString("nothing to encode", TextPolicy.Field);

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

        InertString left = new InertString(one, TextPolicy.Field);
        InertString right = new InertString(other, TextPolicy.Field);

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

    /// <summary>
    /// The auditable invariant: holding an <see cref="InertString"/> does not let you recover
    /// the text it was built from.
    /// </summary>
    /// <remarks>
    /// This is what the namespace split buys, and it is worth stating as a property rather than
    /// as a layout convention, because layout is what drifts. The decoder is the one operation
    /// that turns an inert value back into the hostile original, so it lives in
    /// <c>InertText.Encoder</c> and nothing in the currency namespace may offer a way to reach
    /// it. A reviewer can then read a file's using block instead of tracing its call graph: no
    /// <c>using InertText.Encoder</c> means no path back to the original, for every value that
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
    /// The search string is <c>InertText.Encoder</c>, not <c>using InertText.Encoder</c>. A
    /// using directive is one of two ways to reach a namespace, and the other leaves the import
    /// block untouched — <c>InertText.Encoder.VisualEncoder.TryDecode(...)</c> compiles in a file
    /// whose only directive is <c>using InertText</c>. Searching for the directive would show a
    /// clean import list for a file that decodes. The bare namespace catches both, because a
    /// fully-qualified call has to spell it too.
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

            return bare == typeof(string)
                || bare == typeof(string[])
                || (bare.IsGenericType && bare.GetGenericArguments().Contains(typeof(string)));
        }
    }

    /// <summary>
    /// The other half: the decoder is reachable, but only by naming the capability namespace.
    /// </summary>
    /// <remarks>
    /// Without this, the test above could be satisfied by deleting the decoder outright, which
    /// would take invertibility with it -- and invertibility is what lets <c>Conform</c> repair
    /// a spliced value instead of rejecting it.
    /// </remarks>
    [Fact]
    public void TheDecoderLivesInTheCapabilityNamespace()
    {
        MethodInfo? decode = typeof(VisualEncoder).GetMethod(nameof(VisualEncoder.TryDecode));

        Assert.NotNull(decode);
        Assert.Equal("InertText.Encoder", typeof(VisualEncoder).Namespace);

        InertString inert = new("\u202Ecmd", TextPolicy.Field);
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
    /// text would name <c>InertText.Encoder</c>, the decoder would sit one member access away in
    /// all of them, and the search would return the whole producer set. The signal would survive
    /// in form and be worthless in practice.
    ///
    /// Measured on the tree that introduced this: four production files produce inert text and
    /// <em>none</em> names the capability namespace. Routing production through the encoder
    /// would make that four out of five, so the constructor is not sugar over
    /// <c>VisualEncoder.Encode</c> — it is what keeps the false-positive rate at zero.
    ///
    /// The claim is about <em>production</em> code, and deliberately so. This test file names
    /// <c>InertText.Encoder</c> itself, as do the encoder's own tests: invertibility is a
    /// contract, so something has to decode in order to check it. A test that can decode is the
    /// system working, not a leak — the search that matters is over the files that ship. Read
    /// the counts above as production-only.
    ///
    /// Two distinct regressions are gated here, because both look like tidying:
    /// <list type="bullet">
    /// <item>Removing the public constructor in favour of an encoder factory, on the grounds
    /// that the constructor "just forwards".</item>
    /// <item>Moving <c>ScalarPolicy</c> or <c>TextPolicy</c> next to the encoder, on the grounds
    /// that the policy is "part of encoding". The creation path would still exist and would
    /// still drag the namespace in with it.</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void InertTextCanBeProducedWithoutNamingTheCapabilityNamespace()
    {
        const string Capability = "InertText.Encoder";

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

        // The policy a caller must pass has to be reachable from the currency namespace too --
        // a self-contained signature is no use if the only ScalarPolicy values live next to the
        // decoder.
        Assert.Equal("InertText", typeof(ScalarPolicy).Namespace);
        Assert.Equal("InertText", typeof(TextPolicy).Namespace);

        // Smoke check that the reflected signature is actually callable as described.
        InertString produced = new("\u202Ecmd", TextPolicy.Field);
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
}
