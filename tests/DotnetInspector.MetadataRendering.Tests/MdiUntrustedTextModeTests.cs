using System.Globalization;
using System.Text;
using System.Text.Json;
using Mdi;

namespace DotnetInspector.MetadataRendering.Tests;

/// <summary>
/// End-to-end gate over the three treatments <c>mdi</c> offers for text that came
/// out of an artifact: refuse (the default), contain, and print raw.
/// <para>
/// These drive <see cref="MdiCommand.Invoke(string[], TextWriter?, TextWriter?)"/>
/// with an argv, rather than the <c>Execute*</c> entry points the rest of the
/// containment gate uses. That is the point: the mode is chosen by option
/// parsing, and the CLI's default deliberately differs from the library's, so
/// nothing below the argv layer can observe either. A test that called
/// <c>Execute</c> with an explicit mode would pass no matter which default the
/// command line picked, and would not notice the two flags being combined.
/// </para>
/// <para>
/// The tiers are gated on <em>opposite</em> claims, because each has its own way
/// of being vacuously satisfied. Refusal is asserted to produce no output, so it
/// is also asserted to name where the text is — otherwise refusing everything
/// unconditionally would pass. Containment is asserted to spell every scalar, so
/// it is also asserted to emit none of them raw. Raw is asserted to emit the
/// scalars verbatim, which is the only assertion here that would fail if the flag
/// were quietly a no-op that fell back to containment.
/// </para>
/// </summary>
public sealed class MdiUntrustedTextModeTests(HostileAssemblyFixture fixture)
    : IClassFixture<HostileAssemblyFixture>
{
    /// <summary>
    /// The scalars <see cref="HostileAssemblyFixture.Payload"/> carries, as text.
    /// Decoded from the same bytes the fixture splices so the two cannot drift
    /// apart; spelling them again here would be a second source of truth for the
    /// same fact.
    /// </summary>
    static readonly string PayloadText = Encoding.UTF8.GetString(HostileAssemblyFixture.Payload);

    /// <summary>
    /// The scalars in the payload that actually constitute the threat.
    /// <para>
    /// The payload is a CSI sequence, so it also carries <c>[</c>, <c>3</c>,
    /// <c>1</c> and <c>m</c> — ordinary graphic ASCII that appears throughout any
    /// diagnostic, any table, and any offset. Asserting over the payload as a
    /// whole would make "did the text leak?" indistinguishable from "does the
    /// output contain the digit 3", which is both always true and never
    /// interesting. What must never leak is the part a terminal would act on.
    /// </para>
    /// </summary>
    static readonly Rune[] DangerousScalars =
        [.. PayloadText.EnumerateRunes().Where(HostileAssemblyFixture.IsNonGraphic)];

    /// <summary>
    /// Scalars a format cannot carry verbatim without ceasing to be that format.
    /// <para>
    /// TSV is line oriented and delimits records by newline, so Markout replaces
    /// the line and paragraph separators with a space when writing a cell.
    /// Markdown has no such conflict and carries everything. JSONL escapes rather
    /// than substitutes, so nothing is lost there — see
    /// <see cref="AsConsumerReceivesIt"/>.
    /// </para>
    /// <para>
    /// This is the serializer keeping its own output well-formed, one layer below
    /// <c>mdi</c>, and it bounds what the raw tier can promise: the flag hands the
    /// text over uncontained, which is not the same as guaranteeing every scalar
    /// reaches the stream. The exceptions are listed rather than skipped so that a
    /// format quietly dropping something <em>else</em> still fails here.
    /// </para>
    /// </summary>
    static Rune[] ScalarsTheFormatCannotCarry(string format) => format switch
    {
        "tsv" => [new Rune(0x2028), new Rune(0x2029)],
        _ => [],
    };

    /// <summary>
    /// What a consumer of <paramref name="output"/> actually receives, which is the
    /// only thing the threat model cares about.
    /// <para>
    /// Substring matching is not format-independent. JSON requires every scalar
    /// below U+0020 to be escaped (RFC 8259), so a real ESC reaches a JSONL
    /// consumer spelled <c>\u001B</c> — six characters that <em>parse back</em> to
    /// the one dangerous scalar. That is the serializer keeping its own output
    /// well-formed, not <c>mdi</c> containing anything, and a test that looked for
    /// a literal ESC would call it containment and be wrong. Decoding first asks
    /// the question that matters: what does the reader end up holding?
    /// </para>
    /// </summary>
    static string AsConsumerReceivesIt(string output, string format)
    {
        if (format != "jsonl")
            return output;

        var decoded = new StringBuilder();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length == 0 || line[0] != '{')
            {
                decoded.AppendLine(line);
                continue;
            }

            using var document = JsonDocument.Parse(line);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    decoded.AppendLine(property.Value.GetString());
            }
        }

        return decoded.ToString();
    }

    /// <summary>
    /// Every view that renders artifact text, with the argv that reaches it.
    /// Refusal has to hold on all of them: the table path projects rows, the heap
    /// path reads one value by address, and the overview reports the metadata
    /// version stamp on a route that bypasses row projection entirely — the route
    /// that leaked a raw ESC once already.
    /// </summary>
    static readonly (string View, string[] Args)[] TextBearingViews =
    [
        ("table", ["--table", "TypeDef"]),
        ("overview", ["--overview"]),
    ];

    public static TheoryData<string, string[], string> TextBearingViewsAndFormats()
    {
        var data = new TheoryData<string, string[], string>();
        foreach (string format in (string[])["md", "tsv", "jsonl"])
        {
            foreach ((string view, string[] args) in TextBearingViews)
                data.Add(view, args, format);
        }

        return data;
    }

    int Run(string[] args, out string output, out string error)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = MdiCommand.Invoke([fixture.Path, .. args], stdout, stderr);
        output = stdout.ToString();
        error = stderr.ToString();
        return code;
    }

    /// <summary>
    /// The default refuses, on every text-bearing view and in every format, and
    /// says where the text is rather than only that something was wrong. The
    /// coordinate is the whole value of refusing: a reader who cannot see the
    /// text still has to be able to go look at it.
    /// </summary>
    [Theory]
    [MemberData(nameof(TextBearingViewsAndFormats))]
    public void Default_Refuses_AndNamesWhereTheTextIs(string view, string[] args, string format)
    {
        int code = Run([.. args, "--format", format], out string output, out string error);

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);

        // The category is what makes the diagnostic actionable without the text:
        // it distinguishes a bidi override from a stray control.
        Assert.Contains("U+", error, StringComparison.Ordinal);
        Assert.True(
            error.Contains("Format", StringComparison.Ordinal)
                || error.Contains("Control", StringComparison.Ordinal)
                || error.Contains("LineSeparator", StringComparison.Ordinal),
            $"The {view} refusal in {format} named no Unicode category: {error}");

        // Both ways forward, so refusal is a fork in the road and not a dead end.
        Assert.Contains("--show-untrusted-text", error, StringComparison.Ordinal);
        Assert.Contains("--dangerously-print-raw", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal must not deliver the payload it is refusing to deliver.
    /// <para>
    /// This is the case with real consequences. The diagnostic goes to stderr,
    /// which is read on a terminal and is almost never piped through whatever
    /// treatment the caller applied to stdout — so a message that quoted the
    /// characters would hand a bidi override or a CSI sequence straight to the
    /// terminal by the one route the user cannot redirect, and it would do it in
    /// the mode chosen specifically to avoid that.
    /// </para>
    /// <para>
    /// Asserted over the whole payload, raw <em>and</em> contained: the contained
    /// spelling is safe to render but still echoes the artifact, and the point of
    /// this tier is that the text does not appear at all.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(TextBearingViewsAndFormats))]
    public void Refusal_EchoesNeitherTheRawTextNorItsContainedForm(
        string view, string[] args, string format)
    {
        Run([.. args, "--format", format], out _, out string error);

        foreach (Rune rune in DangerousScalars)
        {
            Assert.False(
                error.Contains(rune.ToString(), StringComparison.Ordinal),
                $"The {view} refusal in {format} echoed U+{rune.Value:X4} raw.");
        }

        foreach (string contained in HostileAssemblyFixture.NeutralizedForms)
        {
            Assert.False(
                error.Contains(contained, StringComparison.Ordinal),
                $"The {view} refusal in {format} echoed the contained form {contained}.");
        }
    }

    /// <summary>
    /// The refusal reports the offending scalar as a code point, and that code
    /// point is one the artifact actually carries. Without this, the "U+" the
    /// first test looks for could be any constant.
    /// </summary>
    [Fact]
    public void Refusal_ReportsACodePointThePayloadCarries()
    {
        Run(["--table", "TypeDef"], out _, out string error);

        int marker = error.IndexOf("U+", StringComparison.Ordinal);
        Assert.True(marker >= 0, error);

        string digits = new([.. error.Skip(marker + 2).TakeWhile(Uri.IsHexDigit)]);
        int reported = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        Assert.Contains(PayloadText.EnumerateRunes(), rune => rune.Value == reported);
    }

    /// <summary>
    /// The contained tier renders, and renders inertly: every scalar in the
    /// payload appears in its spelled form and none appears raw. This is the
    /// behaviour that used to be unconditional, so it is also the compatibility
    /// case — the output here is what every existing caller got.
    /// </summary>
    [Theory]
    [MemberData(nameof(TextBearingViewsAndFormats))]
    public void ShowUntrustedText_RendersEveryScalarInItsContainedForm(
        string view, string[] args, string format)
    {
        int code = Run(
            [.. args, "--format", format, "--show-untrusted-text"],
            out string output,
            out string error);

        Assert.Equal(0, code);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);

        string[] expected = view == "overview"
            ? HostileAssemblyFixture.VersionNeutralizedForms
            : HostileAssemblyFixture.NeutralizedForms;

        foreach (string contained in expected)
            Assert.Contains(contained, output, StringComparison.Ordinal);

        string received = AsConsumerReceivesIt(output, format);
        foreach (Rune rune in DangerousScalars)
        {
            Assert.False(
                received.Contains(rune.ToString(), StringComparison.Ordinal),
                $"The contained {view} rendering in {format} still delivered U+{rune.Value:X4}.");
        }
    }

    /// <summary>
    /// The raw tier prints the artifact text verbatim. Asserted positively,
    /// because every other assertion in this file is satisfied by a mode that
    /// contains — so a flag that silently fell back to containment would pass all
    /// of them. This is the only case that can tell the escape hatch is real.
    /// </summary>
    [Theory]
    [MemberData(nameof(TextBearingViewsAndFormats))]
    public void DangerouslyPrintRaw_EmitsTheArtifactTextVerbatim(
        string view, string[] args, string format)
    {
        int code = Run(
            [.. args, "--format", format, "--show-untrusted-text", "--dangerously-print-raw"],
            out string output,
            out string error);

        Assert.Equal(0, code);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);

        string payload = view == "overview"
            ? Encoding.UTF8.GetString(HostileAssemblyFixture.VersionPayload)
            : PayloadText;

        Rune[] unavailable = ScalarsTheFormatCannotCarry(format);
        string received = AsConsumerReceivesIt(output, format);

        // Whatever the format does to stay well-formed, mdi itself must have
        // contained nothing: not one scalar arrives in its neutralized spelling.
        //
        // Asserted after decoding, because the two spellings collide on the wire.
        // mdi neutralizes U+009F to the six characters \u009F, and JSON escapes a
        // real U+009F to the same six. They are only distinguishable by what they
        // parse back to: the escape yields the scalar, the containment yields the
        // literal text — which is exactly the difference the two tiers exist to
        // make, so this is the layer at which to ask.
        foreach (string contained in HostileAssemblyFixture.NeutralizedForms)
        {
            Assert.False(
                received.Contains(contained, StringComparison.Ordinal),
                $"The raw {view} rendering in {format} still contained {contained}.");
        }
        foreach (Rune rune in payload.EnumerateRunes().Where(HostileAssemblyFixture.IsNonGraphic))
        {
            if (unavailable.Contains(rune))
                continue;

            Assert.True(
                received.Contains(rune.ToString(), StringComparison.Ordinal),
                $"The raw {view} rendering in {format} did not deliver U+{rune.Value:X4}, "
                + "so the escape hatch is not actually handing over the text.");
        }
    }

    /// <summary>
    /// The flags are two axes rather than a three-way choice, and the raw one
    /// alone is inert: refusing happens first, so nothing would ever reach the
    /// spelling decision it governs.
    /// <para>
    /// A flag that silently does nothing is worse than one that is rejected,
    /// because the user who reached for it believed they had asked for raw output
    /// and would read contained output as the artifact's own text. Saying so is
    /// also what keeps the two axes visible: reaching a live control character is
    /// two separately named mistakes, not one. See
    /// <c>docs/design/untrusted-data-threat-model.md#presentation</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRawFlagAloneIsRejectedRatherThanSilentlyIgnored()
    {
        int code = Run(
            ["--table", "TypeDef", "--dangerously-print-raw"],
            out string output,
            out string error);

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("--show-untrusted-text", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The trust axis is genuinely independent of the rendering axis: asking not
    /// to refuse does not by itself ask for raw text. Without this, a change that
    /// made <c>--show-untrusted-text</c> imply raw output would collapse the two
    /// axes into one and nothing else here would notice, because every other case
    /// passes the flags together.
    /// </summary>
    [Fact]
    public void TheTrustOptOutDoesNotByItselfDisableEncoding()
    {
        Assert.Equal(0, Run(["--table", "TypeDef", "--show-untrusted-text"], out string output, out _));

        foreach (Rune rune in DangerousScalars)
        {
            Assert.False(
                output.Contains(rune.ToString(), StringComparison.Ordinal),
                $"--show-untrusted-text alone emitted U+{rune.Value:X4} raw, "
                + "so it is doing the rendering axis's job as well as its own.");
        }

        Assert.Contains(@"\u202E", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refusal costs nothing on an artifact that carries no such text: the
    /// default and the contained tier produce identical bytes.
    /// <para>
    /// This is the claim that makes a refusing default affordable to ship. The
    /// modes differ only in whether they <em>can</em> fail, so on ordinary
    /// assemblies — which is to say almost all of them — nobody sees a change.
    /// The clean assembly used here is the very one the fixture splices its
    /// payload into, so the pair differs in exactly the payload and nothing else.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("md")]
    [InlineData("tsv")]
    [InlineData("jsonl")]
    public void OnAnAssemblyWithNoSuchText_TheDefaultMatchesContainmentExactly(string format)
    {
        string clean = typeof(HostileAssemblyFixture).Assembly.Location;

        var byDefault = new StringWriter();
        int defaultCode = MdiCommand.Invoke(
            [clean, "--table", "TypeDef", "--format", format], byDefault, new StringWriter());

        var contained = new StringWriter();
        int containedCode = MdiCommand.Invoke(
            [clean, "--table", "TypeDef", "--format", format, "--show-untrusted-text"],
            contained,
            new StringWriter());

        Assert.Equal(0, defaultCode);
        Assert.Equal(0, containedCode);
        Assert.NotEmpty(byDefault.ToString());
        Assert.Equal(contained.ToString(), byDefault.ToString());
    }

    /// <summary>
    /// The heap view reads one value straight out of <c>#Strings</c> by address,
    /// so it reaches the text without going through row projection and needs its
    /// own case at each tier rather than inheriting the table's result.
    /// </summary>
    [Fact]
    public void TheHeapViewHonoursAllThreeTiers()
    {
        string[] heap = ["--heap", $"#Strings:0x{fixture.CanaryHeapOffset:x}"];

        Assert.Equal(1, Run(heap, out string refused, out string error));
        Assert.Equal(string.Empty, refused);
        Assert.Contains("U+", error, StringComparison.Ordinal);

        Assert.Equal(0, Run([.. heap, "--show-untrusted-text"], out string inert, out _));
        foreach (string form in HostileAssemblyFixture.NeutralizedForms)
            Assert.Contains(form, inert, StringComparison.Ordinal);

        Assert.Equal(
            0,
            Run([.. heap, "--show-untrusted-text", "--dangerously-print-raw"], out string raw, out _));
        Assert.Contains("\u202E", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// The coordinate in a refusal is spelled the way <c>--heap</c> accepts it, so
    /// the way forward is a command a reader can paste rather than one they have
    /// to assemble. Gated because the two spellings are produced by different code
    /// and nothing else would notice them drifting apart.
    /// </summary>
    [Fact]
    public void TheReportedCoordinateIsAcceptedByTheHeapOption()
    {
        Run(["--table", "TypeDef"], out _, out string error);

        int start = error.IndexOf('#');
        Assert.True(start >= 0, error);
        string coordinate = new([.. error.Skip(start).TakeWhile(c => !char.IsWhiteSpace(c))]);

        int code = Run(
            ["--heap", coordinate, "--show-untrusted-text"], out string output, out string heapError);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, heapError);
        Assert.Contains(@"\u202E", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Raw text is still bounded, and the bound still falls on a scalar boundary.
    /// A budget that divided a surrogate pair would leave a lone surrogate in the
    /// output, which cannot be encoded as UTF-8 and would corrupt the byte stream
    /// of every format — a way for the unsafe tier to be unsafe to the
    /// <em>tool</em> rather than merely to the reader.
    /// </summary>
    [Theory]
    [InlineData("md")]
    [InlineData("tsv")]
    [InlineData("jsonl")]
    public void RawTextIsNeverCutThroughASurrogatePair(string format)
    {
        // Sweep every budget across the payload so the astral scalar is bisected
        // at some point in the range rather than only if a guessed budget lands on it.
        for (int budget = 0; budget <= 40; budget++)
        {
            Run(
                ["--table", "TypeDef", "--format", format, "--dangerously-print-raw",
                 "--max-chars", budget.ToString(CultureInfo.InvariantCulture)],
                out string output,
                out _);

            for (int i = 0; i < output.Length; i++)
            {
                if (!char.IsSurrogate(output[i]))
                    continue;

                bool paired = char.IsHighSurrogate(output[i])
                    && i + 1 < output.Length
                    && char.IsLowSurrogate(output[i + 1]);

                Assert.True(
                    paired || (char.IsLowSurrogate(output[i]) && i > 0 && char.IsHighSurrogate(output[i - 1])),
                    $"A lone surrogate survived at index {i} with --max-chars {budget} in {format}.");
            }
        }
    }
}
