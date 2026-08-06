using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using InertText.Encoding;

namespace InertText;

/// <summary>
/// Text spelled under a <see cref="TextPolicy"/>, carried as a value rather than as a bare
/// <see cref="string"/>.
/// </summary>
/// <remarks>
/// This is the currency form, and it exists for auditability. Encoding on its own is
/// transactional — a <see cref="string"/> goes in and a <see cref="string"/> comes out — so a
/// treated value and an untreated one are the same type, and the only way to tell whether a
/// sink is safe is to trace every call path that reaches it. A distinct type inverts that: the
/// question becomes a type search, and a sink that accepts only <see cref="InertString"/>
/// cannot be handed raw text by accident.
///
/// The second half of that is what this type does <em>not</em> offer. Holding one of these
/// gives no way back to the text it was built from: the decoder lives in
/// <c>InertText.Encoding</c>, in its own namespace, and nothing here reaches it. So a file that
/// imports <c>InertText</c> and not <c>InertText.Encoding</c> cannot recover the original of any
/// value it handles, and that fact is legible in its using block rather than by tracing calls.
/// A reflection test enumerates the public surface of this namespace and accounts for every
/// member that returns text, so the property is enforced rather than merely intended.
///
/// The boundary is an audit aid, not a capability barrier — a file can always add the import.
/// The goal it does meet is that the dangerous half cannot arrive by accident or unnoticed.
///
/// The policy is named rather than supplied. An earlier shape took a caller-written predicate,
/// which read as the more general design and was worse in both directions: rules drifted apart
/// between sinks that should have shared them, and repairing a value meant handing the caller's
/// predicate the decoded original — walking the audit boundary back out through a callback, in a
/// file whose using block still named only the currency namespace. <see cref="TextPolicy"/> is
/// closed, so the rules are shared and no caller code runs during a repair.
///
/// The term and the contract are borrowed from BSD <c>vis(3)</c> ("visually encode
/// characters"): the output is inert, lossless (nothing is dropped, so the reader still sees
/// what was actually there), and invertible (the original can be recovered from it exactly).
/// This is not neutralization, which has none of the three.
///
/// "Inert" is scoped, and the scope matters: no terminal interprets the output as control and
/// no bidi algorithm reorders it. It does <em>not</em> mean the output is safe to drop into a
/// structured format. A <c>|</c> still breaks a Markdown cell, a backtick still opens a span,
/// and a <c>"</c> still terminates a JSON string — none is in any encoded category, and none
/// should be, because escaping those for its own grammar is the serializer's job. Visual
/// encoding and structural escaping compose; neither substitutes for the other.
///
/// There is deliberately no conversion <em>from</em> <see cref="string"/>, implicit or
/// explicit. One would restore exactly the confusion the type removes. Text enters through the
/// constructor, <see cref="FromEncoded(TextPolicy, string)"/>, <see cref="Format"/> or
/// <see cref="Join(string, TextPolicy, IEnumerable{InertString})"/>, all of which name a policy,
/// so every value has been spelled or validated under some policy.
///
/// Every one of those reads <c>(TextPolicy, payload)</c>, including
/// <see cref="IsPermitted(TextPolicy, string)"/> and <see cref="EnsurePermitted"/>. One shape
/// rather than one per member, and the policy set can grow without the type growing a member
/// per policy.
///
/// Note the "some": the type records that a policy was applied, not which one, because a value
/// is routinely built for one sink and spliced into a message bound for another. That makes
/// the useful invariant a property of composition rather than of storage — see
/// <see cref="EnsurePermitted"/>, which re-spells a spliced value under the policy in force.
///
/// Conversion <em>to</em> <see cref="string"/> through <see cref="ToString"/> is unrestricted,
/// which is safe here in a way it usually is not. The customary objection to a wrapper — that
/// <c>ToString()</c> launders it — assumes the payload is dangerous and the wrapper is what
/// holds it back. Here the payload is already inert, and the wrapper only records that fact.
/// Losing the wrapper loses provenance, not protection.
/// </remarks>
public readonly struct InertString : IEquatable<InertString>
{
    // A constructor is the only thing that assigns this, and both constructors assign encoder
    // output, so every value that a caller can build carries text. The `?` describes the single
    // state no constructor can reach: default(InertString), whose field the CLR zeroes without
    // running any constructor at all. A struct cannot suppress its zero value, so a non-nullable
    // annotation here would be unverifiable -- it compiles without a warning, because the
    // compiler does not track default(T) through to fields -- and would promise every reader
    // something the runtime is free to contradict.
    //
    // No downstream code is defensive about it. Text is the sole reader and maps the zero value
    // to empty; a reflection test fails any public member that reads around it.
    private readonly string? _text;

    // Whether bounding dropped anything on the way to this value. A fact rather than the length
    // it was bounded from, because a length is only meaningful within one encoding: the same
    // value re-spelled under a stricter policy grows, so a length carried across EnsurePermitted
    // would be compared against text measured in different units and could report a truncated
    // value as whole. The fact survives re-spelling; a length does not.
    private readonly bool _truncated;

    /// <summary>
    /// Encodes <paramref name="value"/> as <paramref name="policy"/> requires, yielding a value
    /// that can be carried to a sink.
    /// </summary>
    /// <remarks>
    /// The only way text enters the type, and the reason no member of it can take text without
    /// also naming a policy — a reflection test enforces that.
    ///
    /// Exists so that producing inert text does not require naming the capability namespace,
    /// which is what keeps the decoder out of the files that merely make inert text, and it is
    /// gated. How the encoding is carried out is an implementation detail of this type.
    /// </remarks>
    /// <param name="policy">The kind of text this is, which decides what may pass through.</param>
    /// <param name="value">The untreated text.</param>
    public InertString(TextPolicy policy, string value) => this = VisualEncoder.Encode(policy, value);

    /// <summary>
    /// Encodes <paramref name="value"/> as <paramref name="policy"/> requires, allocating only
    /// the resulting encoded string.
    /// </summary>
    public InertString(TextPolicy policy, ReadOnlySpan<char> value)
        => this = VisualEncoder.Encode(policy, value);

    /// <summary>
    /// Encodes <paramref name="value"/> and bounds it to <paramref name="maxLength"/> encoded
    /// characters, without dividing a spelling.
    /// </summary>
    /// <remarks>
    /// The sink-facing form: a cell, a column or a record field knows its own width, and this
    /// lets it say so once rather than encode and then bound as two steps whose intermediate
    /// value it must remember to keep.
    ///
    /// That remembering is the reason this overload carries <see cref="IsTruncated"/>. Bounding
    /// as a separate call leaves the caller holding both values, so it can answer "was anything
    /// dropped" by comparing them; bounding here does not, and the comparison a caller reaches
    /// for instead — encoded length against the raw input's — is wrong in the direction that
    /// matters, because spelling a scalar makes the text longer. A hostile value clipped
    /// mid-spelling would report as complete.
    /// </remarks>
    /// <param name="policy">The kind of text this is, which decides what may pass through.</param>
    /// <param name="value">The untreated text.</param>
    /// <param name="maxLength">The largest encoded length the sink can accept.</param>
    public InertString(TextPolicy policy, string value, int maxLength)
        => this = VisualEncoder.Encode(policy, value).Truncate(maxLength);

    /// <summary>
    /// Encodes <paramref name="value"/> and bounds it to <paramref name="maxLength"/> encoded
    /// characters, without dividing a spelling.
    /// </summary>
    public InertString(TextPolicy policy, ReadOnlySpan<char> value, int maxLength)
        => this = VisualEncoder.Encode(policy, value).Truncate(maxLength);

    // Takes text already spelled by the encoder, so it asserts rather than establishes the
    // invariant. Internal because composition needs it: Join and the interpolation handler
    // build their result piecewise and would otherwise have to re-encode an encoded string.
    internal InertString(string text, VisualForm forms, bool truncated = false)
    {
        _text = text;
        Forms = forms;
        _truncated = truncated;
    }

    /// <summary>The text, with the zero value read as empty.</summary>
    /// <remarks>
    /// The single point where that translation happens. Every other member reads this rather
    /// than the field, because spelling the translation at each use site is what let equality
    /// disagree with the rest of the type about whether the zero value and <c>Encode("")</c>
    /// are the same value. A reflection test enumerates the public surface and fails if any
    /// member answers differently for the two, which is the gate that keeps this honest.
    /// </remarks>
    private string Text => _text ?? string.Empty;

    /// <summary>The empty value, which trivially satisfies the invariant.</summary>
    /// <remarks>
    /// Constructed, not <c>default</c>. The zero value of a struct is an artifact of the CLR
    /// rather than a statement of intent, and naming it as the definition of "empty" describes
    /// how the runtime zeroes memory instead of what this value is. Stated properly, the
    /// contract is: no text, and no spellings emitted.
    ///
    /// <c>default(InertString)</c> is still reachable — a struct cannot suppress it — and it is
    /// still harmless, because empty text satisfies every policy <em>vacuously</em>: there is
    /// no scalar for a policy to refuse. So it is tolerated rather than blessed. The one place
    /// that tolerates it is <see cref="Text"/>, and a reflection test enumerates the public
    /// surface to catch any member that reads around it. Spelling that translation at four
    /// separate reads is what let equality disagree with <see cref="IsEmpty"/>,
    /// <see cref="ToString"/> and <see cref="GetHashCode"/> about whether the zero value and
    /// <c>Encode("")</c> are the same value.
    /// </remarks>
    public static InertString Empty { get; } = new(string.Empty, VisualForm.None);

    /// <summary>
    /// Reconstructs an unbounded value from text previously returned by <see cref="ToString"/>.
    /// </summary>
    /// <remarks>
    /// Validates the encoded representation without recovering the original text. The returned
    /// value retains <paramref name="encoded"/> itself, so the string overload allocates nothing.
    /// A malformed representation throws <see cref="FormatException"/> rather than producing a
    /// plausible value with a weakened invariant.
    ///
    /// This restores encoded text and its <see cref="Forms"/>, not provenance that the text does
    /// not carry. In particular, the result is not truncated. Persisting a value whose
    /// <see cref="IsTruncated"/> flag matters requires a higher-provenance envelope.
    /// </remarks>
    public static InertString FromEncoded(TextPolicy policy, string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        VisualForm forms = VisualEncoder.ValidateEncoded(policy, encoded);
        return new InertString(encoded, forms);
    }

    /// <summary>
    /// Reconstructs an unbounded value from encoded text, allocating only the retained string.
    /// </summary>
    public static InertString FromEncoded(
        TextPolicy policy,
        ReadOnlySpan<char> encoded)
    {
        VisualForm forms = VisualEncoder.ValidateEncoded(policy, encoded);
        return new InertString(encoded.ToString(), forms);
    }

    /// <summary>The spellings <see cref="InertString"/> emitted while producing this value.</summary>
    /// <remarks>
    /// Retained so a sink can print a legend for what it is about to show without re-deriving
    /// it. Composition unions the flags, so a message assembled from several pieces reports
    /// every spelling it contains.
    /// </remarks>
    public VisualForm Forms { get; }

    /// <summary>
    /// Whether producing this value required containing text rather than merely disambiguating a
    /// literal backslash.
    /// </summary>
    /// <remarks>
    /// This is the signal a view may aggregate when its CLI refuses artifact text by default.
    /// It reports what happened under the policy used to build this value; it is not a
    /// conformance check for a different policy. Use <see cref="EnsurePermitted"/> for that.
    /// </remarks>
    public bool RequiredContainment =>
        (Forms & ~VisualForm.Backslash) != VisualForm.None;

    /// <summary>Whether raw rendering must run the visual encoding backwards.</summary>
    public bool NeedsRawDecoding => Forms != VisualForm.None;

    /// <summary>Whether this value carries no text.</summary>
    public bool IsEmpty => Text.Length == 0;

    /// <summary>Whether anything this value was made from was dropped to fit a budget.</summary>
    /// <remarks>
    /// This records that a cut happened, not how much was lost, and the difference is what makes
    /// it survivable. A length is only meaningful inside one encoding: the same value re-spelled
    /// under a stricter policy grows, so a remembered length compared against re-spelled text
    /// can call a truncated value whole. Ten line feeds cut from eleven characters under
    /// <see cref="TextPolicy.Prose"/> re-spell to thirty under <see cref="TextPolicy.Field"/>,
    /// and thirty is not less than eleven.
    ///
    /// Every operation that builds a value from one carrying this therefore carries it too —
    /// <see cref="Bound"/> or-s it with the cut it just made, and <see cref="EnsurePermitted"/>,
    /// <see cref="Join(string, TextPolicy, IEnumerable{InertString})"/> and the interpolation
    /// handler propagate it — so a composed or
    /// re-spelled value cannot claim to be whole when part of it is missing.
    ///
    /// It is state the text does not determine, so two values can carry identical text and
    /// disagree here. <see cref="Equals(InertString)"/> therefore compares it, so that values
    /// which compare equal also render the same.
    /// </remarks>
    public bool IsTruncated => _truncated;

    /// <summary>The number of characters in the encoded text.</summary>
    /// <remarks>
    /// The encoded length, not the length of the text this was built from, because that is the
    /// number a sink has a budget in: encoding expands, so a scalar that arrived as one
    /// character can leave as ten, and a caller bounding what it emits has to measure what it
    /// will emit. The original's length is not recoverable from here by design.
    /// </remarks>
    public int Length => Text.Length;

    /// <summary>
    /// The position of the first spelling this value emitted, or <c>-1</c> when it emitted none.
    /// </summary>
    /// <remarks>
    /// Reports where treated text begins, so a caller can show a prefix it knows to be unchanged
    /// — a survey listing where in a name the first hazard sits, for instance — without asking
    /// what was there before. Every spelling is introduced by a backslash. A raw backslash may
    /// remain when it cannot introduce a spelling, so <see cref="Forms"/> distinguishes the
    /// first spelling from unrelated literal text.
    ///
    /// Answers the same question as <see cref="WasEncoded"/> and locates it;
    /// <c>IndexOfFirstEncoded() &gt;= 0</c> and <see cref="WasEncoded"/> agree. Like that member
    /// it reports what was done to this value, never what it satisfies.
    /// </remarks>
    public int IndexOfFirstEncoded() =>
        Forms == VisualForm.None ? -1 : Text.IndexOf('\\');

    /// <summary>Whether any scalar was encoded on the way in.</summary>
    /// <remarks>
    /// Reports what was <em>done</em> to this value, never what it <em>satisfies</em>. It is not
    /// a conformance check, and using it as one inverts the answer in both directions: text
    /// encoded under <see cref="TextPolicy.Prose"/> can carry a raw line feed and report
    /// <see langword="false"/> here while violating <see cref="TextPolicy.Field"/>, and text
    /// encoded under <see cref="TextPolicy.Field"/> reports <see langword="true"/> while
    /// satisfying every policy,
    /// because the spellings it emits are plain ASCII.
    ///
    /// Conformance is a relation between a value and a policy rather than a property of the
    /// value, so it cannot be cached on one. Ask <see cref="EnsurePermitted"/> instead.
    /// </remarks>
    public bool WasEncoded => Forms != VisualForm.None;

    /// <summary>The encoded text.</summary>
    public override string ToString() => Text;

    /// <summary>
    /// Shortens this value to at most <paramref name="maxLength"/> characters, cutting only
    /// where a cut cannot divide a spelling.
    /// </summary>
    /// <remarks>
    /// The budget-bounded form, for a sink that has to place treated text in a column or a
    /// record field. The budget is on the encoded length, because that is what the sink emits.
    ///
    /// Cutting at the requested position is not an option worth offering. A spelling is several
    /// characters wide, so an arbitrary cut can leave <c>\u2</c> behind. That is valid literal
    /// text in the encoding grammar and can no longer recover the scalar whose spelling was cut.
    /// So the cut is moved down to the nearest position that keeps every spelling whole, and a
    /// surrogate pair counts as one thing however it is spelled.
    ///
    /// A negative budget is read as zero rather than refused, and a budget at or above
    /// <see cref="Length"/> returns this value unchanged, so a caller can hand a configured
    /// limit straight in. Truncation is not reported separately because
    /// <c>result.Length &lt; Length</c> already answers it, and a flag that restates a
    /// comparison is a second thing to keep true.
    ///
    /// <see cref="Forms"/> is recomputed from what is kept, so a legend drawn from the result
    /// cannot name a spelling that was dropped with the tail.
    /// </remarks>
    /// <param name="maxLength">The largest encoded length the caller can accept.</param>
    public InertString Truncate(int maxLength) => Bound(0, maxLength);

    /// <summary>
    /// Takes as much of <paramref name="range"/> as can be taken without dividing a spelling.
    /// </summary>
    /// <remarks>
    /// The general form of <see cref="Truncate(int)"/>, for a caller that wants a window rather
    /// than a prefix. Total: every range is answerable, including a reversed one and one that
    /// runs off either end, both of which are read as the part that overlaps the text.
    ///
    /// Both bounds move inward — the start forward to the next whole spelling, the end back to
    /// the previous one — so the result is always a subset of what was asked for. That
    /// asymmetry is the point: returning less than a caller asked for is something it can
    /// detect from <see cref="Length"/>, while returning more is not. A window that contains no
    /// whole spelling is therefore empty rather than approximated.
    ///
    /// There is deliberately no exact counterpart that refuses an unusable window. An indexer
    /// wears the syntax of ordinary slicing while throwing on bounds that look perfectly
    /// reasonable — six of the twelve positions in an eleven-character value divide a spelling —
    /// and it would buy nothing, because comparing <see cref="Length"/> against the width that
    /// was asked for already reports whether anything had to move.
    /// </remarks>
    /// <param name="range">The window to take, in encoded characters.</param>
    public InertString Truncate(Range range)
    {
        string text = Text;

        // Index.GetOffset does not validate, so a from-end index past the start of the text
        // arrives negative and an absolute one can run past the end. Neither is clamped here,
        // because the walker is total in both bounds and a clamp that never changes an answer
        // is a line nothing can hold true.
        return Bound(range.Start.GetOffset(text.Length), range.End.GetOffset(text.Length));
    }

    private InertString Bound(int start, int end)
    {
        string text = Text;
        (int from, int to, VisualForm forms) = VisualEncoder.WindowWithin(text, start, end);

        // An unbounded request keeps this value rather than rebuilding an identical one, so the
        // common case of a budget nobody is near costs nothing.
        if (from == 0 && to == text.Length)
            return this;

        // Reaching here means the window is a proper subset, so this cut dropped something.
        // Or-ing with what the value already carried is why truncating an already-truncated
        // value cannot report the second cut as the only one.
        return new InertString(text[from..to], forms, true);
    }

    /// <summary>
    /// Builds a value from an interpolated string, encoding every part of it.
    /// </summary>
    /// <remarks>
    /// The composition path, and the reason the type is usable at a message-building site. A
    /// sink that takes an <see cref="InertString"/> would otherwise force callers back to
    /// <c>$"...{value}..."</c> on the encoded text, which produces a bare <see cref="string"/>
    /// and drops the guarantee at the one moment it is most needed.
    ///
    /// Interpolation holes are encoded, which is the point. Literals are encoded too, even
    /// though they come from source and are normally harmless, because an invariant with an
    /// exception in it has to be reasoned about at every use, and a bidi override is as
    /// invisible in a C# source file as it is anywhere else.
    /// </remarks>
    /// <param name="policy">The kind of text being built.</param>
    /// <param name="handler">The interpolated string, encoded piecewise as it is appended.</param>
    public static InertString Format(
        TextPolicy policy,
        [InterpolatedStringHandlerArgument(nameof(policy))] ref InertStringHandler handler)
        => handler.ToInertString();

    /// <summary>
    /// Concatenates <paramref name="values"/>, separated by <paramref name="separator"/>.
    /// </summary>
    /// <remarks>
    /// The separator is encoded under <paramref name="policy"/> like everything else, so joining
    /// with a line break requires a policy that permits one. Exempting it would be a hole exactly
    /// the size of one caller's mistake, and it would contradict the handler, which encodes
    /// source literals for the same reason.
    ///
    /// This is why <see cref="TextPolicy.Prose"/> still permits <c>CR</c>. Refusing it would
    /// render <c>Environment.NewLine</c> as <c>\^M</c> on Windows for every caller who joins
    /// with it, so dropping <c>CR</c> and encoding the separator cannot both hold.
    /// </remarks>
    /// <param name="separator">The text placed between values.</param>
    /// <param name="policy">
    /// The kind of text being built, applied to <paramref name="separator"/> and to any value
    /// that does not already satisfy it.
    /// </param>
    /// <param name="values">The values to join.</param>
    public static InertString Join(string separator, TextPolicy policy, IEnumerable<InertString> values)
    {
        ArgumentNullException.ThrowIfNull(separator);
        return Join(separator.AsSpan(), policy, values);
    }

    /// <summary>
    /// Concatenates <paramref name="values"/>, separated by <paramref name="separator"/>.
    /// </summary>
    public static InertString Join(
        ReadOnlySpan<char> separator,
        TextPolicy policy,
        IEnumerable<InertString> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        InertString encodedSeparator = VisualEncoder.Encode(policy, separator);
        StringBuilder builder = new();
        VisualForm forms = VisualForm.None;
        bool first = true;

        // A join whose parts were clipped is missing text, so the result cannot claim to be
        // whole. The flag says something was dropped, not where, which is imprecise for a part
        // cut out of the middle -- but the alternative is a composed value asserting it is
        // complete when it is not, and in a hardening library the safe error is over-marking.
        bool truncated = false;

        foreach (InertString value in values)
        {
            // The separator's spellings are folded in only when one is actually emitted, so a
            // single-element join cannot report a form the output does not contain.
            if (!first)
            {
                VisualEncoder.AppendForComposition(builder, encodedSeparator, ref forms);
            }

            InertString conformed = value.EnsurePermitted(policy);
            VisualEncoder.AppendForComposition(builder, conformed, ref forms);
            truncated |= conformed.IsTruncated;
            first = false;
        }

        return VisualEncoder.CompleteComposition(builder.ToString(), forms, truncated);
    }

    /// <summary>Names the spellings this value contains, one line each.</summary>
    public IReadOnlyList<string> DescribeLegend() => VisualEncoder.DescribeLegend(Forms);

    /// <summary>
    /// Reports whether every scalar in <paramref name="value"/> is permitted as it is.
    /// </summary>
    /// <remarks>
    /// The early-fail check, for callers that would rather reject text than display an encoded
    /// rendering of it. This is deliberately not "would encoding change it": a
    /// backslash is permitted by any sane policy but is still rewritten, and a check derived
    /// from the encoder would reject every Windows path.
    ///
    /// Takes raw text rather than an <see cref="InertString"/> because the question it answers
    /// is asked <em>before</em> treatment. A sink deciding whether text it already holds is safe
    /// for it should call <see cref="EnsurePermitted"/> instead, which repairs rather than
    /// reports.
    /// </remarks>
    public static bool IsPermitted(TextPolicy policy, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return IsPermitted(policy, value.AsSpan(), out _);
    }

    /// <summary>
    /// Reports whether every scalar in <paramref name="value"/> is permitted as it is.
    /// </summary>
    public static bool IsPermitted(TextPolicy policy, ReadOnlySpan<char> value)
        => IsPermitted(policy, value, out _);

    /// <summary>
    /// Reports whether every scalar in <paramref name="value"/> is permitted as it is, naming
    /// the first that is not.
    /// </summary>
    /// <remarks>
    /// The violation names a position and a classification, never the rendered character, which
    /// is what lets a survey mode report a finding without echoing artifact text.
    /// </remarks>
    public static bool IsPermitted(
        TextPolicy policy,
        string value,
        [NotNullWhen(false)] out ScalarViolation? violation)
    {
        ArgumentNullException.ThrowIfNull(value);
        return IsPermitted(policy, value.AsSpan(), out violation);
    }

    /// <summary>
    /// Reports whether every scalar in <paramref name="value"/> is permitted as it is, naming
    /// the first that is not.
    /// </summary>
    public static bool IsPermitted(
        TextPolicy policy,
        ReadOnlySpan<char> value,
        [NotNullWhen(false)] out ScalarViolation? violation)
    {
        ScalarPolicy permits = ScalarPolicies.For(policy);
        int i = 0;
        while (i < value.Length)
        {
            Rune scalar = VisualEncoder.DecodeAt(value, i, out int width, out bool isUnpairedSurrogate);

            if (isUnpairedSurrogate)
            {
                // The raw code unit, not the decoded scalar: DecodeAt yields U+FFFD here,
                // and the whole point of the report is to name the code point exactly.
                violation = new ScalarViolation(i, value[i], UnicodeCategory.Surrogate);
                return false;
            }

            if (!permits(scalar))
            {
                violation = new ScalarViolation(i, scalar.Value, Rune.GetUnicodeCategory(scalar));
                return false;
            }

            i += width;
        }

        violation = null;
        return true;
    }

    /// <summary>
    /// Returns this value restated under <paramref name="policy"/>, re-encoding it if it
    /// carries anything that policy refuses.
    /// </summary>
    /// <remarks>
    /// The type records that <em>a</em> policy was applied, not <em>which</em> one, so a value
    /// produced under a laxer policy can carry a scalar a stricter sink refuses — <see
    /// cref="TextPolicy.Prose"/> permits the line feed that <see cref="TextPolicy.Field"/>
    /// exists to remove. Splicing such a value in unexamined would put a raw newline into a
    /// single-line message and report <see cref="VisualForm.None"/> for it, which is the log
    /// injection this library exists to prevent, with the type appearing to vouch for it.
    ///
    /// Public because a sink that accepts an <see cref="InertString"/> has no other correct way
    /// to make one safe for itself. Re-encoding through <c>new InertString(policy, value.ToString())</c>
    /// is the obvious substitute and is wrong: the text it re-encodes is already encoded, so the
    /// backslashes double on every pass. This decodes first, and so is idempotent.
    ///
    /// This is the second thing invertibility buys. Because the encoding can be reversed
    /// exactly, a mismatched piece can be taken back to its source text and re-spelled under
    /// the policy actually in force, rather than rejected or trusted.
    ///
    /// The repair only ever tightens. A piece encoded under a stricter policy keeps its
    /// spellings when spliced into a laxer sink, because composition making a value <em>less</em>
    /// inert would let a caller launder one by quoting it somewhere permissive. The cost is that
    /// splice path is observable — the same source text can render differently depending on
    /// where it was encoded — which is a deliberate trade, not an oversight.
    ///
    /// Taking a <see cref="TextPolicy"/> rather than a predicate is what keeps the repair inside
    /// the library. The decode below recovers the hostile original, and with a caller-supplied
    /// predicate that original would be passed to it scalar by scalar — so a file that imports
    /// only the currency namespace could read back every character the value was built from. The
    /// enum has no such reverse channel.
    /// </remarks>
    /// <param name="policy">The kind of text the sink about to receive this value expects.</param>
    public InertString EnsurePermitted(TextPolicy policy)
    {
        string text = Text;

        if (IsPermitted(policy, text))
        {
            return this;
        }

        // Decoding cannot fail for a value this library produced: every spelling Encode emits is
        // one TryDecode accepts, including the pair of adjacent surrogate escapes that Join and
        // the interpolation handler produce when each half was encoded in a separate fragment.
        // A gate test composes such a value and asserts it still decodes, because when that
        // stopped holding the fallback silently re-encoded the escapes as literal text and two
        // unrelated inputs converged. The fallback remains so the failure mode is over-encoding
        // rather than a leak, but it is unreachable, not load-bearing.
        string original = VisualEncoder.TryDecode(text, out string? decoded) ? decoded : text;
        InertString respelled = VisualEncoder.Encode(policy, original);

        // Re-spelling answers "how is this written here", not "is this the whole value", so a
        // value that arrived clipped is still clipped afterwards. Carrying the fact is what
        // makes this survivable: the encoded length it was cut from measures the old spelling,
        // and re-spelling under a stricter policy makes the text longer, so a length compared
        // across the two can call a truncated value whole.
        return _truncated
            ? new InertString(respelled.Text, respelled.Forms, truncated: true)
            : respelled;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads through <see cref="Text"/> rather than the field, so the zero value and
    /// <c>Encode("")</c> compare equal. Comparing <c>_text</c> directly is the defect this
    /// replaced: <see langword="null"/> and <c>""</c> are not ordinally equal.
    ///
    /// <see cref="IsTruncated"/> participates because a sink renders a bounded value with a
    /// mark the whole value does not carry, so two values that disagree there are not
    /// substitutable even when their text matches. <see cref="Forms"/> is excluded instead, and
    /// safely: it is a function of the text, so equal text already implies equal forms.
    /// </remarks>
    public bool Equals(InertString other) =>
        string.Equals(Text, other.Text, StringComparison.Ordinal) && IsTruncated == other.IsTruncated;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InertString other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Text.GetHashCode(StringComparison.Ordinal), IsTruncated);

    /// <summary>Compares two values by their encoded text and whether either was bounded.</summary>
    public static bool operator ==(InertString left, InertString right) => left.Equals(right);

    /// <summary>Compares two values by their encoded text and whether either was bounded.</summary>
    public static bool operator !=(InertString left, InertString right) => !left.Equals(right);
}

/// <summary>
/// Assembles an <see cref="InertString"/> from an interpolated string, applying the policy to
/// each piece as it arrives.
/// </summary>
/// <remarks>
/// Encoding per piece rather than once at the end is what makes composition safe. Encoding the
/// assembled string would be indistinguishable from encoding a concatenation, and would
/// re-encode any already-inert part that was spliced in — turning <c>\u202E</c> into
/// <c>\\u202E</c> and breaking invertibility.
/// </remarks>
[InterpolatedStringHandler]
public ref struct InertStringHandler
{
    private readonly TextPolicy _policy;
    private readonly StringBuilder _builder;
    private VisualForm _forms;
    private bool _truncated;

    /// <summary>Called by the compiler for an interpolated string argument.</summary>
    /// <param name="literalLength">The total length of the literal parts.</param>
    /// <param name="formattedCount">The number of interpolation holes.</param>
    /// <param name="policy">The kind of text being built.</param>
    public InertStringHandler(int literalLength, int formattedCount, TextPolicy policy)
    {
        _policy = policy;
        _builder = new StringBuilder(literalLength + (formattedCount * 12));
        _forms = VisualForm.None;
    }

    /// <summary>Appends a literal part of the interpolated string.</summary>
    public void AppendLiteral(string value) => Append(value);

    /// <summary>Appends a literal span.</summary>
    public void AppendLiteral(ReadOnlySpan<char> value) => Append(value);

    /// <summary>Appends an interpolation hole.</summary>
    /// <remarks>
    /// Formatted under the invariant culture, matching the format-specifier overload. A message
    /// whose decimal separator depends on the ambient culture is a message that cannot be
    /// grepped, and these are diagnostics rather than presentation.
    /// </remarks>
    /// <remarks>
    /// Tests for <see cref="InertString"/> first, because overload resolution cannot. The
    /// dedicated overloads below bind on the <em>static</em> type of the hole, so a value reached
    /// through a generic parameter or through <see cref="object"/> lands here instead, and
    /// <c>ToString</c> would hand its already-encoded text back to the encoder — turning
    /// <c>\u202E</c> into <c>\\u202E</c>. A type test is the only thing that sees through
    /// either.
    /// </remarks>
    public void AppendFormatted<T>(T value)
    {
        if (value is InertString inert)
        {
            AppendFormatted(inert);
            return;
        }

        Append(value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString());
    }

    /// <summary>Appends an interpolation hole that carries a format specifier.</summary>
    /// <remarks>
    /// Tests for <see cref="InertString"/> for the same reason as the overload above. The
    /// specifier is discarded in that arm: an <see cref="InertString"/> has one rendering, and
    /// honouring a format string would mean re-deriving it from the encoded text.
    /// </remarks>
    public void AppendFormatted<T>(T value, string? format)
    {
        if (value is InertString inert)
        {
            AppendFormatted(inert);
            return;
        }

        Append(value is IFormattable formattable
            ? formattable.ToString(format, CultureInfo.InvariantCulture)
            : value?.ToString());
    }

    /// <summary>Appends a string-valued interpolation hole.</summary>
    public void AppendFormatted(string? value) => Append(value);

    /// <summary>Appends a span-valued interpolation hole.</summary>
    public void AppendFormatted(ReadOnlySpan<char> value) => Append(value);

    /// <summary>
    /// Appends a value that is already inert, without encoding it a second time.
    /// </summary>
    /// <remarks>
    /// Without this overload the generic case would run the encoder over text that has already
    /// been through it, doubling every backslash the first pass introduced. The value is still
    /// checked against this sink's policy, because being inert under some policy is not the
    /// same as being inert under this one; see <see cref="InertString.EnsurePermitted"/>.
    /// </remarks>
    public void AppendFormatted(InertString value)
    {
        InertString conformed = value.EnsurePermitted(_policy);
        VisualEncoder.AppendForComposition(_builder, conformed, ref _forms);

        // Splicing a clipped value into a message leaves the message missing that text, so the
        // result carries the fact for the same reason Join does.
        _truncated |= conformed.IsTruncated;
    }

    /// <summary>
    /// Appends an optional already-inert value, without encoding it a second time.
    /// </summary>
    /// <remarks>
    /// Needed as its own overload because a <c>InertString?</c> hole would otherwise bind to the
    /// generic case, whose <c>ToString</c> yields the encoded text and hands it back to the
    /// encoder. Redaction returns this shape, so the trap is on a live path rather than
    /// hypothetical.
    /// </remarks>
    public void AppendFormatted(InertString? value)
    {
        if (value is { } inert)
            AppendFormatted(inert);
    }

    internal InertString ToInertString() =>
        VisualEncoder.CompleteComposition(_builder.ToString(), _forms, _truncated);

    private void Append(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        Append(value.AsSpan());
    }

    private void Append(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return;

        InertString encoded = VisualEncoder.Encode(_policy, value);
        VisualEncoder.AppendForComposition(_builder, encoded, ref _forms);
    }
}
