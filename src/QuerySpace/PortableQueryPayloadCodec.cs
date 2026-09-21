using System.Globalization;
using System.Text;
using System.Text.Json;
using QuerySpace.Rows;

namespace QuerySpace;

/// <summary>
/// The single byte spelling a <see cref="PortableQueryIntent"/> has.
/// </summary>
/// <remarks>
/// <para>
/// One intent has exactly one canonical byte sequence. Two independent
/// implementations — this codec and the Browser adapter — must produce it
/// identically or sharing does not work, so every rule here is witnessed by
/// <c>docs/design/models/portable-query-payload/vectors.json</c>, which this
/// assembly's tests consume directly.
/// </para>
/// <para>
/// Canonical form is syntactic. It normalizes how a request is spelled, never what
/// it means: this codec does not interpret a value, does not know which keys a
/// vocabulary admits, and never normalizes text.
/// </para>
/// <para>
/// The shape is closed. The payload is one JSON object whose properties appear in
/// exactly the order <c>t</c> (terms), <c>b</c> (bounds), <c>s</c> (stages),
/// <c>o</c> (order); a part the intent does not use is omitted rather than emitted
/// as <c>null</c> or <c>[]</c>, so an intent with no parts is <c>{}</c>. Each part
/// is an array of tuples, every layout fixed-arity but the field list:
/// </para>
/// <code>
/// term    [key, operator, value]
/// bound   [dimension, maximum]                         one bound per dimension
/// stage   [window, start-or-null, end-or-null]         always three slots
///         [head | tail | top, count]
/// order   [role, named, reference, direction]
///         [role, fields, key, direction, ...]          at least one pair
/// </code>
/// <para>
/// A key, a dimension, and an order reference are identities: one to
/// <see cref="MaxIdentityBytes"/> UTF-8 bytes, and an empty one names nothing. A
/// value is opaque text up to <see cref="MaxValueBytes"/> bytes. A count is an
/// integer in <c>1..</c><see cref="MaxCount"/> spelled with no sign, leading zero,
/// fraction, or exponent; a window bound is a count or <c>null</c>, and a closed
/// window's bounds are ordered. A role is the baseline text or a stage index in the
/// same grammar with zero admitted, naming a ranking stage. The texts naming an
/// operator, a direction, a stage kind, an order kind, or the baseline role are
/// <see cref="PortableQueryModel"/>'s and are carried verbatim.
/// </para>
/// <para>
/// Owner: <c>docs/design/portable-query-payload.md</c> for the principles; this
/// type for the structure those principles describe.
/// </para>
/// </remarks>
public static class PortableQueryPayloadCodec
{
    /// <summary>The canonical payload's UTF-8 byte ceiling.</summary>
    public const int MaxPayloadBytes = 3 * 1024;

    /// <summary>Nesting depth, counting the payload object as one.</summary>
    public const int MaxDepth = 4;

    public const int MaxTerms = 24;

    public const int MaxBounds = 8;

    public const int MaxStages = 8;

    public const int MaxOrderOperations = 8;

    /// <summary>Field terms, counted across every order operation together.</summary>
    public const int MaxOrderFieldTerms = 8;

    /// <summary>UTF-8 bytes in a key, a dimension, or an order reference.</summary>
    public const int MaxIdentityBytes = 64;

    /// <summary>UTF-8 bytes in a term value.</summary>
    public const int MaxValueBytes = 256;

    /// <summary>
    /// The largest count a stage or bound may carry. Fits every host's native
    /// integer, so no host's width decides what another must admit.
    /// </summary>
    public const int MaxCount = int.MaxValue;

    private const string TermsProperty = "t";
    private const string BoundsProperty = "b";
    private const string StagesProperty = "s";
    private const string OrderProperty = "o";

    private static readonly string[] s_properties =
        [TermsProperty, BoundsProperty, StagesProperty, OrderProperty];

    private static readonly UTF8Encoding s_utf8Strict = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonDocumentOptions s_jsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    /// <summary>
    /// Emits the one canonical payload for an intent.
    /// </summary>
    /// <exception cref="PortableQueryPayloadException">
    /// The intent breaches a declared limit or carries text the string rule refuses.
    /// Limits are charged as parsed, before exact duplicate terms collapse.
    /// </exception>
    public static string Encode(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(intent);

        Validate(intent);
        string canonical = Write(intent);
        if (s_utf8Strict.GetByteCount(canonical) > MaxPayloadBytes)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.LimitExceeded,
                $"The canonical payload exceeds the {MaxPayloadBytes}-byte limit.");
        }

        return canonical;
    }

    /// <summary>
    /// Reads one payload-shaped JSON object into an intent, accepting any spelling
    /// of the same shape.
    /// </summary>
    /// <remarks>
    /// This is a conversion boundary, not a second payload format: property order,
    /// element order, exact duplicate terms, empty parts, insignificant whitespace,
    /// and equivalent JSON string escapes are all accepted, and
    /// <see cref="Encode"/> restores the one canonical spelling. Every layout,
    /// token, role, and uniqueness rule still applies, and so does every limit that
    /// is a property of the intent — the part counts and the text lengths.
    /// <para>
    /// The payload's byte ceiling is not among them, because what this returns is
    /// an intent and not a payload: the text it reads may be spelled with
    /// whitespace a payload cannot carry, and several admissible intents are
    /// witnessed that way. <see cref="Encode"/> charges that ceiling against the
    /// canonical form, which is the artifact the limit is about. Untrusted bytes
    /// arrive through <see cref="Decode"/>, which charges it before parsing,
    /// because there the input <em>is</em> the payload.
    /// </para>
    /// </remarks>
    /// <exception cref="PortableQueryPayloadException">The text is not one.</exception>
    public static PortableQueryIntent ParseJson(
        string json,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(json);

        return Read(json, onTheWire: false);
    }

    /// <summary>
    /// Reads one canonical payload into an intent.
    /// </summary>
    /// <remarks>
    /// Bytes that arrive in any other spelling — reordered, padded, or carrying a
    /// duplicate the canonical form would have collapsed — are refused as
    /// <see cref="PortableQueryPayloadFailureKind.NonCanonical"/> rather than
    /// repaired.
    /// </remarks>
    /// <exception cref="PortableQueryPayloadException">The bytes are not one.</exception>
    public static PortableQueryIntent Decode(
        string payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(payload);

        RequireWithinPayloadCeiling(payload);
        PortableQueryIntent intent = Read(payload, onTheWire: true);

        if (!string.Equals(Write(intent), payload, StringComparison.Ordinal))
        {
            throw Failure(
                PortableQueryPayloadFailureKind.NonCanonical,
                "The payload is valid but is not in canonical form.");
        }

        return intent;
    }

    /// <summary>
    /// Charges the payload's byte ceiling against arriving bytes, before they are
    /// parsed, so the untrusted path never parses more than the declared maximum.
    /// </summary>
    private static void RequireWithinPayloadCeiling(string text)
    {
        int bytes;
        try
        {
            bytes = s_utf8Strict.GetByteCount(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.UnpairedSurrogate,
                "The text carries an unpaired surrogate.",
                exception);
        }

        if (bytes > MaxPayloadBytes)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.LimitExceeded,
                $"The payload exceeds the {MaxPayloadBytes}-byte limit.");
        }
    }

    // ─── Canonical write ──────────────────────────────────────────────────────
    // Sequence is canonical only where sequence carries meaning: stages and the
    // field terms inside one operation keep their order; everything else takes the
    // model's semantic order.

    private static string Write(PortableQueryIntent intent)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        bool firstProperty = true;

        void Property(string name)
        {
            if (!firstProperty) builder.Append(',');
            firstProperty = false;
            WriteString(builder, name);
            builder.Append(':');
        }

        if (intent.Terms.Count > 0)
        {
            Property(TermsProperty);
            WriteArray(
                builder,
                PortableQueryModel.InSemanticOrder(intent.Terms),
                static (target, term) =>
                {
                    target.Append('[');
                    WriteString(target, term.Key);
                    target.Append(',');
                    WriteString(target, PortableQueryModel.TextOf(term.Operator));
                    target.Append(',');
                    WriteString(target, term.Value);
                    target.Append(']');
                });
        }

        if (intent.Bounds.Count > 0)
        {
            Property(BoundsProperty);
            WriteArray(
                builder,
                PortableQueryModel.InSemanticOrder(intent.Bounds),
                static (target, bound) =>
                {
                    target.Append('[');
                    WriteString(target, bound.Dimension);
                    target.Append(',');
                    WriteNumber(target, bound.RequestedMaximum);
                    target.Append(']');
                });
        }

        if (intent.Stages.Count > 0)
        {
            Property(StagesProperty);
            WriteArray(
                builder,
                intent.Stages,
                static (target, stage) =>
                {
                    target.Append('[');
                    WriteString(target, PortableQueryModel.TextOf(stage.Kind));
                    target.Append(',');
                    if (stage.Kind is RowSelectionStageKind.Window)
                    {
                        WriteBound(target, stage.Start);
                        target.Append(',');
                        WriteBound(target, stage.End);
                    }
                    else
                    {
                        WriteNumber(target, stage.Count);
                    }

                    target.Append(']');
                });
        }

        if (intent.Order.Count > 0)
        {
            Property(OrderProperty);
            WriteArray(
                builder,
                PortableQueryModel.InSemanticOrder(intent.Order),
                static (target, operation) =>
                {
                    target.Append('[');
                    if (operation.Role.IsBaseline)
                        WriteString(target, PortableQueryModel.BaselineRoleText);
                    else
                        WriteNumber(target, operation.Role.StageIndex);
                    target.Append(',');
                    WriteString(target, PortableQueryModel.TextOf(operation.Kind));

                    if (operation.Kind is PortableQueryOrderKind.Named)
                    {
                        target.Append(',');
                        WriteString(target, operation.Reference);
                        target.Append(',');
                        WriteString(
                            target,
                            PortableQueryModel.TextOf(operation.Direction));
                    }
                    else
                    {
                        foreach (PortableQueryOrderTerm term in operation.Terms)
                        {
                            target.Append(',');
                            WriteString(target, term.Key);
                            target.Append(',');
                            WriteString(
                                target,
                                PortableQueryModel.TextOf(term.Direction));
                        }
                    }

                    target.Append(']');
                });
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static void WriteArray<T>(
        StringBuilder builder,
        IEnumerable<T> items,
        Action<StringBuilder, T> write)
    {
        builder.Append('[');
        bool first = true;
        foreach (T item in items)
        {
            if (!first) builder.Append(',');
            first = false;
            write(builder, item);
        }

        builder.Append(']');
    }

    private static void WriteBound(StringBuilder builder, int? bound)
    {
        if (bound is null) builder.Append("null");
        else WriteNumber(builder, bound.Value);
    }

    private static void WriteNumber(StringBuilder builder, int value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The packet owner's string rule, which this codec inherits rather than
    /// introducing a second convention: escape only quote, backslash, and C0
    /// controls, in their short forms where one exists and as lowercase
    /// <c>\u00xx</c> otherwise, and emit every other scalar as raw UTF-8.
    /// </summary>
    /// <remarks>
    /// Measured, no <c>System.Text.Json</c> encoder implements it — each uppercases
    /// the hex, escapes U+007F, U+0085, U+2028, and U+2029, and turns a
    /// supplementary character into a surrogate-pair escape.
    /// </remarks>
    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (Rune rune in value.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\t': builder.Append("\\t"); break;
                case '\n': builder.Append("\\n"); break;
                case '\f': builder.Append("\\f"); break;
                case '\r': builder.Append("\\r"); break;
                case < 0x20:
                    builder.Append("\\u").Append(
                        rune.Value.ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default: builder.Append(rune.ToString()); break;
            }
        }

        builder.Append('"');
    }

    // ─── Intent validation ────────────────────────────────────────────────────
    // Everything the shape declares that a constructed intent can still violate.
    // Structural rules the type system already enforces are absent by design.

    private static void Validate(PortableQueryIntent intent)
    {
        if (intent.Terms.Count > MaxTerms) throw TooMany("terms", MaxTerms);
        foreach (PortableQueryTerm term in intent.Terms)
        {
            ValidateText(term.Key, identity: true);
            ValidateText(term.Value, identity: false);
        }

        if (intent.Bounds.Count > MaxBounds) throw TooMany("execution bounds", MaxBounds);
        foreach (PortableQueryBound bound in intent.Bounds)
            ValidateText(bound.Dimension, identity: true);

        if (intent.Stages.Count > MaxStages) throw TooMany("selection stages", MaxStages);

        if (intent.Order.Count > MaxOrderOperations)
            throw TooMany("order operations", MaxOrderOperations);

        // One bound per dimension, one operation per role, and a ranking role that
        // names a ranking stage are guaranteed by PortableQueryIntent.Create. They
        // are the model's invariants, not the payload's, and are not re-checked
        // here: an intent that reached this method cannot violate one.
        int fieldTerms = 0;
        foreach (PortableQueryOrderOperation operation in intent.Order)
        {
            if (operation.Kind is PortableQueryOrderKind.Named)
            {
                ValidateText(operation.Reference, identity: true);
            }
            else
            {
                fieldTerms += operation.Terms.Count;
                foreach (PortableQueryOrderTerm term in operation.Terms)
                    ValidateText(term.Key, identity: true);
            }
        }

        if (fieldTerms > MaxOrderFieldTerms)
            throw TooMany("order field terms", MaxOrderFieldTerms);
    }

    private static void ValidateText(string value, bool identity)
    {
        int bytes;
        try
        {
            bytes = s_utf8Strict.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.UnpairedSurrogate,
                "Text carries an unpaired surrogate, which is refused rather than repaired.",
                exception);
        }

        if (identity && value.Length == 0)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.EmptyIdentity,
                "An identity names something; an empty one names nothing.");
        }

        int limit = identity ? MaxIdentityBytes : MaxValueBytes;
        if (bytes > limit) throw TooMany("UTF-8 bytes of text", limit);
    }

    // ─── Structural read ──────────────────────────────────────────────────────
    // Shared by both entry points. Parts are read in the canonical property
    // sequence rather than in document sequence, so which defect a payload with
    // several is reported for does not depend on how it was spelled. An empty part
    // is refused only on the wire: an intent may carry one, and canonical emission
    // omits it.

    private static PortableQueryIntent Read(string text, bool onTheWire)
    {
        using JsonDocument document = Parse(text);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.NotAnObject,
                "The payload is one closed JSON object.");
        }

        var names = new List<string>();
        foreach (JsonProperty property in root.EnumerateObject())
        {
            try
            {
                names.Add(property.Name);
            }
            catch (InvalidOperationException exception)
            {
                // The string rule applies to a property name as to any other string.
                throw Failure(
                    PortableQueryPayloadFailureKind.UnpairedSurrogate,
                    "A property name carries an unpaired surrogate.",
                    exception);
            }
        }

        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.DuplicateProperty,
                "A repeated property is invalid.");
        }

        foreach (string name in names)
        {
            if (!s_properties.Contains(name, StringComparer.Ordinal))
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.UnknownProperty,
                    $"The payload shape declares no property '{name}'.");
            }
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.BadArity,
                    "Every part is an array of tuples.");
            }

            if (property.Value.GetArrayLength() == 0 && onTheWire)
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.EmptyPart,
                    "Canonical form never carries an empty part.");
            }
        }

        List<PortableQueryTerm> terms = ReadTerms(root);
        List<PortableQueryBound> bounds = ReadBounds(root);
        List<PortableQueryStage> stages = ReadStages(root);
        List<PortableQueryOrderOperation> order = ReadOrder(root, stages);

        if (Depth(root) > MaxDepth) throw TooMany("levels of nesting", MaxDepth);

        return PortableQueryIntent.Create(terms, bounds, stages, order);
    }

    private static JsonDocument Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text, s_jsonOptions);
        }
        catch (JsonException exception)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.Malformed,
                "The payload is not JSON.",
                exception);
        }
    }

    private static List<PortableQueryTerm> ReadTerms(JsonElement root)
    {
        var terms = new List<PortableQueryTerm>();
        if (!root.TryGetProperty(TermsProperty, out JsonElement part)) return terms;
        if (part.GetArrayLength() > MaxTerms) throw TooMany("terms", MaxTerms);

        foreach (JsonElement tuple in part.EnumerateArray())
        {
            RequireArity(tuple, 3);
            string key = ReadSlotText(tuple[0], identity: true);
            string operatorText = ReadSlotText(tuple[1], identity: null);
            if (!PortableQueryModel.TryParseOperator(operatorText, out PortableQueryOperator @operator))
                throw UnknownToken(operatorText, "operator");
            string value = ReadSlotText(tuple[2], identity: false);
            terms.Add(new PortableQueryTerm(key, @operator, value));
        }

        return terms;
    }

    private static List<PortableQueryBound> ReadBounds(JsonElement root)
    {
        var bounds = new List<PortableQueryBound>();
        if (!root.TryGetProperty(BoundsProperty, out JsonElement part)) return bounds;
        if (part.GetArrayLength() > MaxBounds) throw TooMany("execution bounds", MaxBounds);

        var dimensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement tuple in part.EnumerateArray())
        {
            RequireArity(tuple, 2);
            string dimension = ReadSlotText(tuple[0], identity: true);
            // A null maximum takes the same reason a null stage count does.
            RequireNotNull(tuple[1]);
            int maximum = ReadCount(tuple[1]);
            if (!dimensions.Add(dimension))
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.RepeatedBoundDimension,
                    $"Dimension '{dimension}' carries more than one execution bound.");
            }

            bounds.Add(new PortableQueryBound(dimension, maximum));
        }

        return bounds;
    }

    private static List<PortableQueryStage> ReadStages(JsonElement root)
    {
        var stages = new List<PortableQueryStage>();
        if (!root.TryGetProperty(StagesProperty, out JsonElement part)) return stages;
        if (part.GetArrayLength() > MaxStages) throw TooMany("selection stages", MaxStages);

        foreach (JsonElement tuple in part.EnumerateArray())
        {
            if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() < 1)
                throw BadArity();

            string kindText = ReadSlotText(tuple[0], identity: null);
            if (!PortableQueryModel.TryParseStageKind(kindText, out RowSelectionStageKind kind))
                throw UnknownToken(kindText, "stage kind");

            if (kind is RowSelectionStageKind.Window)
            {
                RequireArity(tuple, 3);
                int? start = ReadWindowBound(tuple[1]);
                int? end = ReadWindowBound(tuple[2]);
                if (start is not null && end is not null && end < start)
                {
                    throw Failure(
                        PortableQueryPayloadFailureKind.WindowUnordered,
                        "A closed window's end cannot precede its start.");
                }

                stages.Add(PortableQueryStage.Window(start, end));
                continue;
            }

            RequireArity(tuple, 2);
            RequireNotNull(tuple[1]);
            int count = ReadCount(tuple[1]);
            stages.Add(kind switch
            {
                RowSelectionStageKind.Head => PortableQueryStage.Head(count),
                RowSelectionStageKind.Tail => PortableQueryStage.Tail(count),
                _ => PortableQueryStage.Top(count)
            });
        }

        return stages;
    }

    private static List<PortableQueryOrderOperation> ReadOrder(
        JsonElement root,
        List<PortableQueryStage> stages)
    {
        var order = new List<PortableQueryOrderOperation>();
        if (!root.TryGetProperty(OrderProperty, out JsonElement part)) return order;
        if (part.GetArrayLength() > MaxOrderOperations)
            throw TooMany("order operations", MaxOrderOperations);

        int fieldTerms = 0;
        bool sawBaseline = false;
        var stageRoles = new HashSet<int>();

        foreach (JsonElement tuple in part.EnumerateArray())
        {
            if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() < 2)
                throw BadArity();

            PortableQueryOrderRole role = ReadRole(
                tuple[0],
                stages,
                ref sawBaseline,
                stageRoles);

            string kindText = ReadSlotText(tuple[1], identity: null);
            if (!PortableQueryModel.TryParseOrderKind(kindText, out PortableQueryOrderKind kind))
                throw UnknownToken(kindText, "order kind");

            int rest = tuple.GetArrayLength() - 2;
            if (kind is PortableQueryOrderKind.Named)
            {
                if (rest != 2) throw BadArity();
            }
            else
            {
                if (rest == 0 || rest % 2 != 0) throw BadArity();
                fieldTerms += rest / 2;
            }

            // Both kinds spell their tail as identity-and-direction pairs, so the
            // slots are read once; a named operation is the one-pair case, whose
            // single pair is the reference and its direction.
            var pairs = new List<PortableQueryOrderTerm>(rest / 2);
            for (int index = 2; index < tuple.GetArrayLength(); index += 2)
            {
                string key = ReadSlotText(tuple[index], identity: true);
                string directionText = ReadSlotText(tuple[index + 1], identity: null);
                if (!PortableQueryModel.TryParseDirection(directionText, out PortableQueryDirection direction))
                    throw UnknownToken(directionText, "direction");
                pairs.Add(new PortableQueryOrderTerm(key, direction));
            }

            order.Add(kind is PortableQueryOrderKind.Named
                ? PortableQueryOrderOperation.Named(role, pairs[0].Key, pairs[0].Direction)
                : PortableQueryOrderOperation.Fields(role, pairs));
        }

        if (fieldTerms > MaxOrderFieldTerms)
            throw TooMany("order field terms", MaxOrderFieldTerms);

        return order;
    }

    private static PortableQueryOrderRole ReadRole(
        JsonElement slot,
        List<PortableQueryStage> stages,
        ref bool sawBaseline,
        HashSet<int> stageRoles)
    {
        if (slot.ValueKind == JsonValueKind.String)
        {
            string text = ReadSlotText(slot, identity: null);
            if (!string.Equals(text, PortableQueryModel.BaselineRoleText, StringComparison.Ordinal))
                throw UnknownToken(text, "role");
            if (sawBaseline)
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.DuplicateRole,
                    "At most one order operation carries the baseline role.");
            }

            sawBaseline = true;
            return PortableQueryOrderRole.Baseline;
        }

        if (slot.ValueKind == JsonValueKind.Number)
        {
            // A role index is the count grammar with zero admitted.
            if (!IsCanonicalInteger(slot)
                || !slot.TryGetInt32(out int index)
                || index < 0)
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.BadInteger,
                    "A role index is a canonical non-negative integer.");
            }

            if (index >= stages.Count
                || stages[index].Kind is not RowSelectionStageKind.Top)
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.RoleNotTop,
                    "A ranking role must name an existing ranking stage.");
            }

            if (!stageRoles.Add(index))
            {
                throw Failure(
                    PortableQueryPayloadFailureKind.DuplicateRole,
                    "At most one order operation per ranking stage.");
            }

            return PortableQueryOrderRole.ForStage(index);
        }

        if (slot.ValueKind == JsonValueKind.Null) throw NullOutsideWindow();
        throw BadArity();
    }

    private static string ReadSlotText(JsonElement slot, bool? identity)
    {
        if (slot.ValueKind == JsonValueKind.Null) throw NullOutsideWindow();
        if (slot.ValueKind != JsonValueKind.String) throw BadArity();

        string value;
        try
        {
            value = slot.GetString()!;
        }
        catch (InvalidOperationException exception)
        {
            // A lone surrogate escape, which unescaping refuses rather than repairs.
            throw Failure(
                PortableQueryPayloadFailureKind.UnpairedSurrogate,
                "Text carries an unpaired surrogate.",
                exception);
        }

        if (identity is true && value.Length == 0)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.EmptyIdentity,
                "An identity names something; an empty one names nothing.");
        }

        // A token slot carries one of the model's fixed texts, each well inside
        // every limit, so only the two open slot kinds are measured.
        int limit = identity switch
        {
            true => MaxIdentityBytes,
            false => MaxValueBytes,
            null => int.MaxValue
        };

        if (limit != int.MaxValue && s_utf8Strict.GetByteCount(value) > limit)
            throw TooMany("UTF-8 bytes of text", limit);

        return value;
    }

    private static int? ReadWindowBound(JsonElement slot) =>
        slot.ValueKind == JsonValueKind.Null ? null : ReadCount(slot);

    private static int ReadCount(JsonElement slot)
    {
        if (!IsCanonicalInteger(slot)
            || !slot.TryGetInt64(out long value)
            || value < 1
            || value > MaxCount)
        {
            throw Failure(
                PortableQueryPayloadFailureKind.BadInteger,
                $"A count is a canonical integer in 1..{MaxCount}.");
        }

        return (int)value;
    }

    private static bool IsCanonicalInteger(JsonElement slot)
    {
        if (slot.ValueKind != JsonValueKind.Number) return false;

        string raw = slot.GetRawText();
        if (raw.Contains('.', StringComparison.Ordinal)
            || raw.Contains('e', StringComparison.Ordinal)
            || raw.Contains('E', StringComparison.Ordinal)
            || raw.StartsWith('-')
            || raw.StartsWith('+'))
        {
            return false;
        }

        if (raw.Length > 1 && raw[0] == '0') return false;

        // Wider than Int64 is not a count either; the domain check follows.
        return slot.TryGetInt64(out _);
    }

    private static void RequireArity(JsonElement tuple, int slots)
    {
        if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != slots)
            throw BadArity();
    }

    private static void RequireNotNull(JsonElement slot)
    {
        if (slot.ValueKind == JsonValueKind.Null) throw NullOutsideWindow();
    }

    // Depth is four by construction of the declared layouts: no payload reaches
    // five without first failing a layout rule. The guard stands anyway, because a
    // limit the shape declares is a limit the codec charges.
    private static int Depth(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => 1 + element.EnumerateObject()
            .Select(property => Depth(property.Value))
            .DefaultIfEmpty(0)
            .Max(),
        JsonValueKind.Array => 1 + element.EnumerateArray()
            .Select(Depth)
            .DefaultIfEmpty(0)
            .Max(),
        _ => 1
    };

    private static PortableQueryPayloadException BadArity() =>
        Failure(
            PortableQueryPayloadFailureKind.BadArity,
            "A tuple does not have its declared shape.");

    private static PortableQueryPayloadException NullOutsideWindow() =>
        Failure(
            PortableQueryPayloadFailureKind.NullOutsideWindow,
            "null is permitted only in a window bound.");

    private static PortableQueryPayloadException UnknownToken(string text, string slot) =>
        Failure(
            PortableQueryPayloadFailureKind.UnknownToken,
            $"No {slot} identity is spelled '{text}'.");

    private static PortableQueryPayloadException TooMany(string what, int limit) =>
        Failure(
            PortableQueryPayloadFailureKind.LimitExceeded,
            $"A payload carries at most {limit} {what}.");

    private static PortableQueryPayloadException Failure(
        PortableQueryPayloadFailureKind kind,
        string message) => new(kind, message);

    private static PortableQueryPayloadException Failure(
        PortableQueryPayloadFailureKind kind,
        string message,
        Exception innerException) => new(kind, message, innerException);
}
