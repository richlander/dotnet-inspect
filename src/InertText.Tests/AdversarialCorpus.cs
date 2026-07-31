namespace InertText.Tests;

/// <summary>
/// Named attacks, kept as data rather than as assertions.
/// </summary>
/// <remarks>
/// The tests around this file are property tests: they sweep every scalar and check
/// invertibility and injectivity, which is the right shape for proving the transform total.
/// What a sweep does not do is say what any of it was <em>for</em>. A corpus of real attacks
/// does, and it fails differently — a sweep regression says "some scalar broke", where a corpus
/// regression says "Trojan Source works again".
///
/// Each entry records what the payload attacks, because the value of a fixture is mostly in
/// knowing why it was added. Several of these are attacks on the reader of a terminal rather
/// than on a parser, which is the class this library exists for: nothing here is malformed, and
/// every payload is well-formed Unicode that a naive sink prints without complaint.
/// </remarks>
public sealed record Adversary(string Name, string Payload, string Attacks);

public static class AdversarialCorpus
{
    public static IReadOnlyList<Adversary> All { get; } =
    [
        new("TrojanSourceCommentOut",
            "if (isAdmin) {\u202E \u2066// safe\u2069\u2066",
            "CVE-2021-42574. Bidi overrides reorder the rendering so a reviewer sees code that "
            + "differs from what compiles. The text is unmodified; only its display order is."),

        new("BidiOverrideRightToLeft",
            "invoice\u202Egpj.exe",
            "RLO makes a .exe render as a .jpg. The classic file-name spoof, and the reason Cf "
            + "is refused wholesale rather than by an exception list."),

        new("AnsiClearScreen",
            "\u001B[2J\u001B[H owned",
            "A C0 escape that clears the terminal and homes the cursor, erasing whatever "
            + "diagnostics preceded it."),

        new("AnsiWindowTitle",
            "\u001B]0;rm -rf /\u0007",
            "OSC sequence that rewrites the terminal title. BEL-terminated, so it leaves no "
            + "visible trace in the transcript."),

        new("AnsiHyperlinkSpoof",
            "\u001B]8;;https://evil.example\u0007click here\u001B]8;;\u0007",
            "OSC 8 makes arbitrary text a hyperlink to an unrelated target, so the rendered "
            + "label and the destination disagree."),

        new("LogInjectionForgedLine",
            "package\nError: signature verified",
            "A newline forges a second diagnostic line that appears to come from the tool. This "
            + "is why the field policy refuses line terminators outright."),

        new("CarriageReturnOverwrite",
            "actual-package\rspoofed",
            "CR without LF rewinds to the start of the line, so the second half overwrites the "
            + "first and only the lie is visible."),

        new("NextLineControl",
            "package\u0085Error: forged",
            "U+0085 NEL is a line terminator that is neither CR nor LF, so a check written "
            + "against those two lets it through."),

        new("LineAndParagraphSeparator",
            "package\u2028forged\u2029second",
            "U+2028 and U+2029 terminate lines for many renderers and for JavaScript string "
            + "literals, while being category Zl and Zp rather than Cc."),

        new("TagCharacterSmuggling",
            "package\U000E0074\U000E0065\U000E0078\U000E0074",
            "Tag characters carry an invisible ASCII payload, the technique used to smuggle "
            + "instructions past a human reading an agent's transcript. They are astral, so a "
            + "speller that stops at \\uXXXX cannot even write them down."),

        new("LanguageTagPrefix",
            "package\U000E0001",
            "U+E0001 opens a tag sequence. Deprecated, invisible, and still assigned."),

        new("ZeroWidthSplitting",
            "mic\u200Broso\u200Cft\u200D.com",
            "Zero-width space, non-joiner and joiner split a name for a reader's eye while "
            + "leaving it a single token to anything doing string comparison."),

        new("SoftHyphenAndWordJoiner",
            "pack\u00ADage\u2060name",
            "SHY renders only at a line break and WJ never renders at all, so both hide inside "
            + "an identifier."),

        new("ByteOrderMarkInline",
            "\uFEFFpackage\uFEFFname",
            "A BOM in the middle of text is a zero-width no-break space. Invisible, and often "
            + "survives a round trip through tooling that strips only a leading one."),

        new("MongolianVowelSeparator",
            "pack\u180Eage",
            "U+180E changed category across Unicode versions, so a hand-maintained hazard list "
            + "written against an older table misses it. A category rule evaluated at runtime "
            + "does not."),

        new("InterlinearAnnotation",
            "package\uFFF9hidden\uFFFAshown\uFFFB",
            "Interlinear annotation marks let one run of text be displayed in place of another."),

        new("DeprecatedFormatControls",
            "package\u206Aname\u206E",
            "The deprecated U+206A-U+206F block toggles symmetric swapping and digit shapes; "
            + "still assigned, still Cf, and rarely on anyone's list."),

        new("MusicalSymbolFormatControl",
            "package\U0001D173name\U0001D17A",
            "Astral Cf. These are among the 127 encoded scalars above the BMP that a "
            + "\\uXXXX-only speller cannot represent, so it is neither total nor invertible."),

        new("UnpairedHighSurrogate",
            "package\uD83Dname",
            "Not a scalar at all. Rune cannot hold it and string.EnumerateRunes substitutes "
            + "U+FFFD for it, so a speller built on either is lossy on exactly this input."),

        new("UnpairedLowSurrogate",
            "package\uDE00name",
            "The other half of the same problem, which a check written only for high "
            + "surrogates misses."),

        new("NullAndDelete",
            "package\u0000name\u007F",
            "NUL truncates in anything C-adjacent; DEL is a C0-adjacent control that sits "
            + "outside the 0x00-0x1F range a range check usually covers."),

        new("BackslashCollision",
            "C:\\users\\\u202Efoo",
            "A real override immediately after a literal backslash. If the speller did not "
            + "double the backslash, the encoded form would read as \\\\u202E and decoding "
            + "could not tell a path containing that text from an encoded override, so the "
            + "transform would not invert."),
    ];

    /// <summary>
    /// A payload that is <em>not</em> caught by a category policy, kept here deliberately.
    /// </summary>
    /// <remarks>
    /// Cyrillic small a is category Ll, exactly like Latin a, and renders identically. No
    /// category rule catches it and none should — the fix is an allow-shaped policy for sinks
    /// with a constrained grammar, which is why the predicate is the caller's to choose. A
    /// corpus that contained only things the default policy catches would quietly imply the
    /// default is sufficient.
    /// </remarks>
    public static Adversary Homoglyph { get; } =
        new("CyrillicHomoglyph",
            "p\u0430ckage",
            "Typosquatting by substituting a visually identical scalar from another script.");

    public static TheoryData<string> Names
    {
        get
        {
            TheoryData<string> names = [];

            foreach (Adversary adversary in All)
            {
                names.Add(adversary.Name);
            }

            return names;
        }
    }

    public static Adversary ByName(string name) => All.Single(a => a.Name == name);
}
