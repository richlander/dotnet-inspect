using System.Globalization;
using System.Text;
using InertText;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The gate for issue #3628: no scalar that can act on a sink survives projection
/// into a metadata cell.
/// </summary>
/// <remarks>
/// This is the enforcement named by <see cref="MetadataTableProjector.ContainCellText"/>,
/// and it is written as a sweep over Unicode general categories rather than over a list
/// of characters on purpose. The defect it exists to prevent was a hand-written range
/// test — <c>c &lt; ' ' || c == '\x7f' || (c &gt;= '\x80' &amp;&amp; c &lt;= '\x9f')</c> —
/// which is exactly category <c>Cc</c>, and which therefore let every bidi override
/// through while looking like a complete answer. A test that pinned a list of characters
/// would have been written from the same understanding as the bug and would have passed.
/// That is not hypothetical: the rendering gate over this same output did pin such a
/// list, described itself as mirroring the projector's range, and stayed green through
/// the whole life of the defect.
///
/// So the sweep asks the question the other way round: for every scalar the Unicode
/// tables classify as able to act on a sink, does the projector's output still contain
/// it? That formulation cannot be satisfied by widening a range, and it fails if a
/// future edit narrows the rule for any reason.
/// </remarks>
public class MetadataCellContainmentTests
{
    /// <summary>
    /// Calls the containment directly rather than through a projected table, because the
    /// claim is about the neutralizer itself and a table would only exercise the scalars
    /// some fixture happens to hold.
    /// </summary>
    static string Contain(string value, int maxChars = 4096)
        => MetadataTableProjector.ContainCellText(value, maxChars).ToString();

    /// <summary>
    /// The categories a scalar must not reach a sink in, and why each one matters.
    /// </summary>
    /// <remarks>
    /// <c>Cc</c> carries the terminal control sequences; <c>Cf</c> carries visual
    /// reordering, and holds every code point Trojan Source (CVE-2021-42574) used;
    /// <c>Zl</c> and <c>Zp</c> terminate a line for consumers that split on more than
    /// <c>LF</c>. <c>Cs</c> is covered separately, because a surrogate cannot be
    /// expressed as a <see cref="Rune"/> and so cannot be swept the same way.
    /// </remarks>
    static readonly UnicodeCategory[] ActingCategories =
    [
        UnicodeCategory.Control,
        UnicodeCategory.Format,
        UnicodeCategory.LineSeparator,
        UnicodeCategory.ParagraphSeparator,
    ];

    [Fact]
    public void EveryActingScalar_IsSpelledRatherThanEmitted()
    {
        // Every scalar, not a sample: the whole point is that the rule is not allowed
        // to be a range, and a sample drawn by hand would re-import the bug's blind
        // spot. Surrogates are excluded here because Rune cannot represent one.
        var leaked = new List<string>();
        var swept = new HashSet<int>();

        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF)
                continue;

            var scalar = new Rune(cp);
            if (Array.IndexOf(ActingCategories, Rune.GetUnicodeCategory(scalar)) < 0)
                continue;

            swept.Add(cp);

            // Embedded between ordinary text, which is how a real cell carries it and
            // which catches a neutralizer that only inspects a prefix.
            string emitted = Contain("a" + scalar.ToString() + "b");

            if (emitted.Contains(scalar.ToString(), StringComparison.Ordinal))
                leaked.Add($"U+{cp:X4} ({Rune.GetUnicodeCategory(scalar)})");
        }

        // Non-vacuity, stated as membership rather than as a count. A threshold is a
        // magic number that has to be re-guessed whenever Unicode adds a format
        // character; naming one scalar per category says what the sweep is for, and
        // fails if a category is dropped from the set above.
        foreach (int required in (int[])[0x0000, 0x001B, 0x007F, 0x202E, 0x200D, 0x2028, 0x2029])
        {
            Assert.True(
                swept.Contains(required),
                $"The sweep never reached U+{required:X4}; ActingCategories is incomplete.");
        }

        Assert.True(
            leaked.Count == 0,
            $"{leaked.Count} scalar(s) reached the cell unspelled: "
                + string.Join(", ", leaked.Take(20)));
    }

    /// <summary>
    /// The specific regression from #3628, named so a failure reads as itself rather
    /// than as one entry in a sweep of a million code points.
    /// </summary>
    [Theory]
    [InlineData(0x202E, "RIGHT-TO-LEFT OVERRIDE — the Trojan Source character")]
    [InlineData(0x202D, "LEFT-TO-RIGHT OVERRIDE")]
    [InlineData(0x2066, "LEFT-TO-RIGHT ISOLATE")]
    [InlineData(0x200D, "ZERO WIDTH JOINER")]
    [InlineData(0x2028, "LINE SEPARATOR")]
    [InlineData(0x2029, "PARAGRAPH SEPARATOR")]
    [InlineData(0x001B, "ESC — the one class the old range test did cover")]
    [InlineData(0x0085, "NEL, a C1 control")]
    public void NamedHazard_DoesNotSurviveProjection(int codePoint, string why)
    {
        string raw = "Hostile" + new Rune(codePoint).ToString() + "INJECTED";
        string emitted = Contain(raw);

        Assert.DoesNotContain(new Rune(codePoint).ToString(), emitted, StringComparison.Ordinal);

        // The text is still there and still legible — this is encoding, not redaction,
        // so a reader can still tell which row they are looking at.
        Assert.StartsWith("Hostile", emitted, StringComparison.Ordinal);
        Assert.EndsWith("INJECTED", emitted, StringComparison.Ordinal);
        Assert.True(emitted.Length > raw.Length - 1, why);
    }

    /// <summary>
    /// A cell may never carry a record separator, whatever the enclosing format is.
    /// </summary>
    /// <remarks>
    /// Distinct from the sweep above: that one asks whether a scalar is spelled, this
    /// one asks whether the row can be divided. <c>TAB</c> is the case worth stating
    /// on its own, because it is invisible in Markdown and is the field separator in
    /// the TSV rendering of the very same projection.
    /// </remarks>
    [Fact]
    public void NoCellCanCarryARecordSeparator()
    {
        foreach (char separator in new[] { '\n', '\r', '\t' })
            Assert.DoesNotContain(separator, Contain($"before{separator}after"));
    }

    /// <summary>
    /// Ill-formed text cannot corrupt the output on UTF-8 conversion.
    /// </summary>
    [Fact]
    public void UnpairedSurrogate_IsSpelledRatherThanEmitted()
    {
        string emitted = Contain("a\uD800b");

        Assert.DoesNotContain(emitted, char.IsSurrogate);
        Assert.Contains("D800", emitted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A budget cut never leaves a partial spelling behind, and always says it cut.
    /// </summary>
    /// <remarks>
    /// The budget is applied to the emitted text, and a spelling is several characters
    /// wide, so a naive cut can strand <c>\u2</c> — text that no longer means what it
    /// came from and that re-encodes to something a different input also produces.
    /// Swept across every budget from nothing to past the end, because the interesting
    /// positions are exactly the ones inside a spelling.
    /// </remarks>
    [Fact]
    public void ATruncatedCellNeverStrandsAPartialSpelling()
    {
        const string raw = "ab\u202Ecd\u2028ef";
        int whole = MetadataTableProjector.ContainCellText(raw, int.MaxValue).Length;

        for (int budget = 0; budget <= 32; budget++)
        {
            InertString contained = MetadataTableProjector.ContainCellText(raw, budget);
            string emitted = contained.ToString();
            bool truncated = contained.IsTruncated;

            Assert.True(emitted.Length <= budget, $"Budget {budget} emitted {emitted.Length} chars.");
            Assert.DoesNotContain('\u202E', emitted);
            Assert.DoesNotContain('\u2028', emitted);

            // A backslash that introduces nothing is the signature of a divided
            // spelling; the last one must always still have its body attached.
            int trailing = emitted.LastIndexOf('\\');
            if (trailing >= 0)
            {
                string tail = emitted[trailing..];
                Assert.True(
                    tail.Length >= 3,
                    $"Budget {budget} stranded a partial spelling: '{emitted}'.");
            }

            // The flag is what puts the ellipsis on a clipped cell, so a budget that
            // dropped text and reported otherwise renders a partial value as a whole one.
            Assert.Equal(emitted.Length < whole, truncated);
        }
    }

    /// <summary>
    /// Ordinary text is not disturbed, so the containment is not paid for in legibility.
    /// </summary>
    /// <remarks>
    /// The structural punctuation of a resolved name is graphic, and a neutralizer that
    /// mangled it would push callers toward reading the raw value instead — which is how
    /// a containment gets removed later.
    /// </remarks>
    [Theory]
    [InlineData("System.Collections.Generic.List`1")]
    [InlineData("<Module>")]
    [InlineData("TypeRef[3] :: mscorlib")]
    [InlineData("get_Item")]
    [InlineData("\"quoted\"")]
    [InlineData("\U0001F600 astral")]
    public void GraphicText_PassesThroughUnchanged(string value)
        => Assert.Equal(value, Contain(value));
}
