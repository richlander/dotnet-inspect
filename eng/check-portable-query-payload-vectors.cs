// Checks the normative vectors behind docs/design/portable-query-payload.md.
//
// The design document states the principles of the canonical query payload in
// prose. The exact shape is not prose: it is the SHAPE region below, and the
// vectors file witnesses it. Everything after the SHAPE region is
// implementation, and is not normative; if it disagrees with the region or a
// vector, the implementation is wrong.
//
//   dotnet run eng/check-portable-query-payload-vectors.cs
//
// Vector categories:
//   encode        — an intent, possibly unordered, and the one canonical byte
//                   string it must produce; those bytes must also decode and
//                   re-emit unchanged.
//   encode-reject — an intent the codec must refuse before canonicalizing,
//                   with the reason. Limits are charged here, as parsed,
//                   before any duplicate collapses.
//   reject        — bytes the decoder must refuse, with the reason.
//   pair          — two (queryId, canonical) states and whether they are the
//                   same query. Identity is the pair, never bytes alone.
//
// Exit 0 means every vector holds; 2 means at least one did not.
//
// What this checker does not know: what any key, dimension, or order identity
// means, whether a value is a valid package identifier, or how terms compose.
// The codec validates structure and canonical form; it never interprets a value.

using System.Text;
using System.Text.Json;

// ═══════════════════════════════════════════════════════════════════════════════
// SHAPE — the normative structure. Change here, then add a vector.
// ═══════════════════════════════════════════════════════════════════════════════

// Wire properties in canonical order, and the intent part each one carries.
// An absent or empty part is omitted, never emitted as null or [].
(string Property, string Part)[] properties =
    [("t", "terms"), ("b", "bounds"), ("s", "stages"), ("o", "order")];

// What one tuple slot may hold.
//   Identity    a key, dimension, or order reference: string, ≤ MaxIdentityBytes
//   Text        a term value: string, ≤ MaxValueBytes, never interpreted
//   Count       an integer 1..2147483647, spelled with no sign, leading zero,
//               fraction, or exponent. That domain fits every host's native
//               integer and the stage owner's int; no larger count is admitted.
//   WindowBound a Count, or null meaning "no bound on this side". When both
//               bounds of a window are present, start <= end (the stage owner's
//               construction precondition, enforced here at decode).
//   Role        the string "base", or an integer index into s naming a top stage
//   Direction   asc | desc
// Token slots are fixed by their layout and listed with it.

// Identity texts — operators, directions, stage kinds, order kinds, and the
// base role — are NOT defined here. They are the intent model's
// (docs/design/portable-query-intent.md, "The intent contract"), and this codec
// carries them. The arrays below the SHAPE region mirror them for validation
// and are implementation, not a second definition.

// Tuple layouts. A term, a bound, and each stage kind are fixed-arity.
// An order operation is [Role, kind, ...]: the kind selects the tail.
//   term   [Identity(key), operator text, Text(value)]
//   bound  [Identity(dimension), Count(maximum)]        one bound per dimension
//   stage  head | tail | top   [stage text, Count]      a top's ranking binds through o
//          window              [stage text, WindowBound, WindowBound]   always three slots
//   order  named   [Role, "named",  Identity(reference), Direction]
//          fields  [Role, "fields", Identity(key), Direction, ...]   at least one pair

// Limits. Text limits count UTF-8 bytes. Depth counts the object as 1.
// Count limits are charged as parsed, before exact duplicates collapse.
const int MaxPayloadBytes     = 3 * 1024;
const int MaxDepth            = 4;
const int MaxTerms            = 24;
const int MaxBounds           = 8;
const int MaxStages           = 8;
const int MaxOrderOperations  = 8;
const int MaxOrderFieldTerms  = 8;   // aggregate across every operation
const int MaxIdentityBytes    = 64;  // keys, dimensions, order references
const int MaxValueBytes       = 256; // term values
const int PacketMaxValues     = 256; // the packet owner's per-payload allowance
const long MaxCount           = 2147483647; // the Count domain's upper bound

// Depth is 4 by construction of the layouts above; no payload can reach 5
// without first failing a layout rule. MaxDepth is a guard, not a reachable
// limit, and has no vector.

// Element orders and the text comparator are NOT defined here. They are the
// intent model's semantic orders (docs/design/portable-query-intent.md,
// "Semantic order"): Unicode scalar value, which is UTF-8 byte order. This
// codec emits them; the comparer below the SHAPE region implements that rule.

// Strings, inherited from the packet owner: escape only quote, backslash, and
// C0 controls; \b \t \n \f \r where defined; lowercase \u00xx for other C0;
// every other scalar raw UTF-8, including U+007F and above; unpaired
// surrogates refused. No System.Text.Json encoder implements this rule.

// ═══════════════════════════════════════════════════════════════════════════════
// End of SHAPE. Everything below is implementation.
// ═══════════════════════════════════════════════════════════════════════════════

// Implements the model's comparator: Unicode scalar order == UTF-8 byte order.
Comparer<string> scalarOrder = Comparer<string>.Create((x, y) =>
    Encoding.UTF8.GetBytes(x).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(y)));

// Mirrors of the model's identity texts (portable-query-intent.md, "The intent
// contract"). Validation consults these; the model owns them.
string[] operators  = ["eq", "ne", "gte", "lte"];
string[] directions = ["asc", "desc"];
string[] orderKinds = ["named", "fields"];
Dictionary<string, Slot[]> stageLayouts = new()
{
    ["head"]   = [Slot.Count],
    ["tail"]   = [Slot.Count],
    ["top"]    = [Slot.Count],
    ["window"] = [Slot.WindowBound, Slot.WindowBound],
};

string root = FindRepoRoot();
string vectorsPath = Path.Combine(root, "docs", "design", "models", "portable-query-payload", "vectors.json");
if (!File.Exists(vectorsPath)) { Console.Error.WriteLine($"vectors file not found: {vectorsPath}"); return 2; }

using JsonDocument vectors = JsonDocument.Parse(File.ReadAllText(vectorsPath));
int failures = 0, nEncode = 0, nEncodeReject = 0, nReject = 0, nPair = 0;

foreach (JsonElement v in vectors.RootElement.GetProperty("encode").EnumerateArray())
{
    nEncode++;
    string name = v.GetProperty("name").GetString()!;
    string expected = v.GetProperty("canonical").GetString()!;
    string intentText = v.GetProperty("intent").GetRawText();

    string? reason = Encode(intentText, out string canonical);
    if (reason is not null) { Fail(name, $"intent refused as {reason}"); continue; }
    if (canonical != expected) { Fail(name, $"canonical bytes differ\n    expected {expected}\n    actual   {canonical}"); continue; }

    string? decodeReason = ValidateBytes(expected);
    if (decodeReason is not null) { Fail(name, $"canonical bytes rejected as {decodeReason}"); continue; }

    int bytes = Encoding.UTF8.GetByteCount(expected);
    using JsonDocument canonDoc = JsonDocument.Parse(expected);
    int values = CountValues(canonDoc.RootElement);
    if (bytes > MaxPayloadBytes) Fail(name, $"{bytes} bytes exceeds {MaxPayloadBytes}");
    if (values > PacketMaxValues) Fail(name, $"{values} JSON values exceeds the packet allowance of {PacketMaxValues}");
    if (v.TryGetProperty("expectValues", out JsonElement ev) && ev.GetInt32() != values) Fail(name, $"expected {ev.GetInt32()} JSON values, counted {values}");
    if (v.TryGetProperty("expectBytes", out JsonElement eb) && eb.GetInt32() != bytes) Fail(name, $"expected {eb.GetInt32()} bytes, counted {bytes}");
}

foreach (JsonElement v in vectors.RootElement.GetProperty("encode-reject").EnumerateArray())
{
    nEncodeReject++;
    string name = v.GetProperty("name").GetString()!;
    string expectedReason = v.GetProperty("reject").GetString()!;
    string? reason = Encode(v.GetProperty("intent").GetRawText(), out _);
    if (reason != expectedReason) Fail(name, $"expected intent rejection '{expectedReason}', got '{reason ?? "accepted"}'");
}

foreach (JsonElement v in vectors.RootElement.GetProperty("reject").EnumerateArray())
{
    nReject++;
    string name = v.GetProperty("name").GetString()!;
    string expectedReason = v.GetProperty("reject").GetString()!;
    string? reason = ValidateBytes(v.GetProperty("bytes").GetString()!);
    if (reason != expectedReason) Fail(name, $"expected rejection '{expectedReason}', got '{reason ?? "accepted"}'");
}

foreach (JsonElement v in vectors.RootElement.GetProperty("pair").EnumerateArray())
{
    nPair++;
    string name = v.GetProperty("name").GetString()!;
    JsonElement a = v.GetProperty("a"), b = v.GetProperty("b");
    bool same = a.GetProperty("queryId").GetString() == b.GetProperty("queryId").GetString()
             && a.GetProperty("canonical").GetString() == b.GetProperty("canonical").GetString();
    if (same != v.GetProperty("same").GetBoolean()) Fail(name, $"expected same={v.GetProperty("same").GetBoolean()}, identity says {same}");
}

Console.WriteLine($"{nEncode} encode, {nEncodeReject} encode-reject, {nReject} reject, {nPair} pair vectors; {failures} failure(s)");
return failures == 0 ? 0 : 2;

// ─── Encode: intent → canonical bytes, or a reason ───────────────────────────
// Limits on parts and text are charged as parsed; the payload byte limit is
// charged on the canonical form, before emission succeeds.

string? Encode(string intentText, out string canonical)
{
    canonical = "";
    string? reason = ValidateStructure(intentText, out JsonDocument? doc, onTheWire: false);
    if (reason is not null) return reason;
    using (doc)
    {
        canonical = Canonicalize(doc!);
        return Encoding.UTF8.GetByteCount(canonical) > MaxPayloadBytes ? "limit-exceeded" : null;
    }
}

// ─── Decode: bytes → reason or null ──────────────────────────────────────────

string? ValidateBytes(string text)
{
    string? reason = ValidateStructure(text, out JsonDocument? doc, onTheWire: true);
    if (reason is not null) return reason;
    using (doc)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxPayloadBytes) return "limit-exceeded";
        // Structure holds; the bytes must be the one canonical spelling.
        return Canonicalize(doc!) == text ? null : "non-canonical";
    }
}

// ─── Structure: any JSON text → reason or null, plus the parsed document ──────
// Shared by decode and encode. Does not require canonical order or spelling;
// does require every layout, token, role, uniqueness, and limit rule. An empty
// part is refused only on the wire: an intent may carry one, and canonical
// emission omits it.

string? ValidateStructure(string text, out JsonDocument? doc, bool onTheWire)
{
    doc = null;
    JsonDocument parsed;
    try
    {
        parsed = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
    }
    catch (JsonException) { return "malformed"; }

    string? result = Check(parsed.RootElement);
    if (result is null) doc = parsed; else parsed.Dispose();
    return result;

    string? Check(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return "not-an-object";

        var names = new List<string>();
        foreach (JsonProperty p in obj.EnumerateObject()) names.Add(p.Name);
        if (names.Distinct().Count() != names.Count) return "duplicate-property";
        foreach (string n in names) if (!properties.Any(x => x.Property == n)) return "unknown-property";
        foreach (JsonProperty p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Array) return "bad-arity";
            if (p.Value.GetArrayLength() == 0 && onTheWire) return "empty-part";
        }

        string[] stageTokens = [];

        if (obj.TryGetProperty("t", out JsonElement t))
        {
            if (t.GetArrayLength() > MaxTerms) return "limit-exceeded";
            foreach (JsonElement term in t.EnumerateArray())
            {
                if (term.ValueKind != JsonValueKind.Array || term.GetArrayLength() != 3) return "bad-arity";
                string? r;
                if ((r = SlotString(term[0], Slot.Identity, out _)) is not null) return r;
                if ((r = SlotString(term[1], Slot.Token, out string op)) is not null) return r;
                if (!operators.Contains(op)) return "unknown-token";
                if ((r = SlotString(term[2], Slot.Text, out _)) is not null) return r;
            }
        }

        if (obj.TryGetProperty("b", out JsonElement b))
        {
            if (b.GetArrayLength() > MaxBounds) return "limit-exceeded";
            var dims = new HashSet<string>();
            foreach (JsonElement bound in b.EnumerateArray())
            {
                if (bound.ValueKind != JsonValueKind.Array || bound.GetArrayLength() != 2) return "bad-arity";
                string? r;
                if ((r = SlotString(bound[0], Slot.Identity, out string dim)) is not null) return r;
                if (!IsCount(bound[1])) return "bad-integer";
                if (!dims.Add(dim)) return "repeated-bound-dimension";
            }
        }

        if (obj.TryGetProperty("s", out JsonElement s))
        {
            int n = s.GetArrayLength();
            if (n > MaxStages) return "limit-exceeded";
            stageTokens = new string[n];
            int i = 0;
            foreach (JsonElement stage in s.EnumerateArray())
            {
                if (stage.ValueKind != JsonValueKind.Array || stage.GetArrayLength() < 1) return "bad-arity";
                string? r;
                if ((r = SlotString(stage[0], Slot.Token, out string tok)) is not null) return r;
                if (!stageLayouts.TryGetValue(tok, out Slot[]? layout)) return "unknown-token";
                if (stage.GetArrayLength() != 1 + layout.Length) return "bad-arity";
                for (int k = 0; k < layout.Length; k++)
                {
                    JsonElement slot = stage[k + 1];
                    if (slot.ValueKind == JsonValueKind.Null) { if (layout[k] != Slot.WindowBound) return "null-outside-window"; continue; }
                    if (!IsCount(slot)) return "bad-integer";
                }
                if (tok == "window" && stage[1].ValueKind != JsonValueKind.Null && stage[2].ValueKind != JsonValueKind.Null
                    && stage[1].GetInt64() > stage[2].GetInt64()) return "window-unordered";
                stageTokens[i++] = tok;
            }
        }

        if (obj.TryGetProperty("o", out JsonElement o))
        {
            if (o.GetArrayLength() > MaxOrderOperations) return "limit-exceeded";
            int fieldTerms = 0; bool sawBase = false; var stageRoles = new HashSet<int>();
            foreach (JsonElement op in o.EnumerateArray())
            {
                if (op.ValueKind != JsonValueKind.Array || op.GetArrayLength() < 2) return "bad-arity";
                JsonElement role = op[0];
                if (role.ValueKind == JsonValueKind.String)
                {
                    string? r;
                    if ((r = SlotString(role, Slot.Token, out string rs)) is not null) return r;
                    if (rs != "base") return "unknown-token";
                    if (sawBase) return "duplicate-role";
                    sawBase = true;
                }
                else if (role.ValueKind == JsonValueKind.Number && role.TryGetInt32(out int idx) && IsCanonicalInteger(role))
                {
                    if (idx < 0 || idx >= stageTokens.Length || stageTokens[idx] != "top") return "role-not-top";
                    if (!stageRoles.Add(idx)) return "duplicate-role";
                }
                else return role.ValueKind == JsonValueKind.Null ? "null-outside-window" : "bad-arity";

                string? kr;
                if ((kr = SlotString(op[1], Slot.Token, out string kind)) is not null) return kr;
                if (!orderKinds.Contains(kind)) return "unknown-token";
                int rest = op.GetArrayLength() - 2;
                if (kind == "named") { if (rest != 2) return "bad-arity"; }
                else { if (rest == 0 || rest % 2 != 0) return "bad-arity"; fieldTerms += rest / 2; }
                for (int k = 2; k < op.GetArrayLength(); k += 2)
                {
                    string? r;
                    if ((r = SlotString(op[k], Slot.Identity, out _)) is not null) return r;
                    if ((r = SlotString(op[k + 1], Slot.Token, out string dir)) is not null) return r;
                    if (!directions.Contains(dir)) return "unknown-token";
                }
            }
            if (fieldTerms > MaxOrderFieldTerms) return "limit-exceeded";
        }

        if (Depth(obj) > MaxDepth) return "limit-exceeded";
        return null;
    }
}

// Reads a string slot, applying the slot's byte limit and refusing unpaired surrogates.
string? SlotString(JsonElement e, Slot slot, out string value)
{
    value = "";
    if (e.ValueKind == JsonValueKind.Null) return "null-outside-window";
    if (e.ValueKind != JsonValueKind.String) return "bad-arity";
    try { value = e.GetString()!; }
    catch (InvalidOperationException) { return "unpaired-surrogate"; }   // lone surrogate escape
    int limit = slot switch { Slot.Identity => MaxIdentityBytes, Slot.Text => MaxValueBytes, _ => int.MaxValue };
    return Encoding.UTF8.GetByteCount(value) > limit ? "limit-exceeded" : null;
}

// ─── Canonical write: a structurally valid document → the one canonical spelling

string Canonicalize(JsonDocument doc)
{
    JsonElement obj = doc.RootElement;
    var sb = new StringBuilder();
    sb.Append('{');
    bool first = true;
    void Prop(string name) { if (!first) sb.Append(','); first = false; WriteString(sb, name); sb.Append(':'); }

    if (obj.TryGetProperty("t", out JsonElement t) && t.GetArrayLength() > 0)
    {
        var terms = t.EnumerateArray()
            .Select(x => (k: x[0].GetString()!, op: x[1].GetString()!, v: x[2].GetString()!))
            .Distinct()
            .OrderBy(x => x.k, scalarOrder).ThenBy(x => x.op, scalarOrder).ThenBy(x => x.v, scalarOrder);
        Prop("t"); sb.Append('[');
        bool f = true;
        foreach (var x in terms) { if (!f) sb.Append(','); f = false; sb.Append('['); WriteString(sb, x.k); sb.Append(','); WriteString(sb, x.op); sb.Append(','); WriteString(sb, x.v); sb.Append(']'); }
        sb.Append(']');
    }
    if (obj.TryGetProperty("b", out JsonElement b) && b.GetArrayLength() > 0)
    {
        var bounds = b.EnumerateArray().Select(x => (d: x[0].GetString()!, m: x[1].GetRawText())).OrderBy(x => x.d, scalarOrder);
        Prop("b"); sb.Append('[');
        bool f = true;
        foreach (var x in bounds) { if (!f) sb.Append(','); f = false; sb.Append('['); WriteString(sb, x.d); sb.Append(',').Append(x.m).Append(']'); }
        sb.Append(']');
    }
    if (obj.TryGetProperty("s", out JsonElement s) && s.GetArrayLength() > 0)
    {
        Prop("s"); sb.Append('[');
        bool f = true;
        foreach (JsonElement stage in s.EnumerateArray()) { if (!f) sb.Append(','); f = false; WriteTuple(sb, stage); }
        sb.Append(']');
    }
    if (obj.TryGetProperty("o", out JsonElement o) && o.GetArrayLength() > 0)
    {
        var ops = o.EnumerateArray().OrderBy(x => x[0].ValueKind == JsonValueKind.String ? -1 : x[0].GetInt32());
        Prop("o"); sb.Append('[');
        bool f = true;
        foreach (JsonElement op in ops) { if (!f) sb.Append(','); f = false; WriteTuple(sb, op); }
        sb.Append(']');
    }
    sb.Append('}');
    return sb.ToString();
}

// Writes a tuple of scalars in declared sequence, in canonical spelling.
static void WriteTuple(StringBuilder sb, JsonElement tuple)
{
    sb.Append('[');
    bool f = true;
    foreach (JsonElement e in tuple.EnumerateArray())
    {
        if (!f) sb.Append(','); f = false;
        switch (e.ValueKind)
        {
            case JsonValueKind.String: WriteString(sb, e.GetString()!); break;
            case JsonValueKind.Number: sb.Append(e.GetRawText()); break;
            case JsonValueKind.Null: sb.Append("null"); break;
            default: throw new InvalidOperationException("nested structure inside a tuple");
        }
    }
    sb.Append(']');
}

// The packet owner's string rule. Not any System.Text.Json encoder.
static void WriteString(StringBuilder sb, string s)
{
    sb.Append('"');
    foreach (Rune r in s.EnumerateRunes())
    {
        switch (r.Value)
        {
            case '"':  sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\b': sb.Append("\\b"); break;
            case '\t': sb.Append("\\t"); break;
            case '\n': sb.Append("\\n"); break;
            case '\f': sb.Append("\\f"); break;
            case '\r': sb.Append("\\r"); break;
            case < 0x20: sb.Append("\\u").Append(r.Value.ToString("x4")); break;
            default: sb.Append(r.ToString()); break;   // raw UTF-8 on output, U+007F and above included
        }
    }
    sb.Append('"');
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

static bool IsCanonicalInteger(JsonElement e)
{
    if (e.ValueKind != JsonValueKind.Number) return false;
    string raw = e.GetRawText();
    if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E') || raw.StartsWith('-') || raw.StartsWith('+')) return false;
    if (raw.Length > 1 && raw[0] == '0') return false;
    return e.TryGetInt64(out _);   // wider than Int64 is not a Count either; IsCount applies the domain
}

static bool IsCount(JsonElement e) => IsCanonicalInteger(e) && e.GetInt64() >= 1 && e.GetInt64() <= MaxCount;

static int Depth(JsonElement e) => e.ValueKind switch
{
    JsonValueKind.Object => 1 + e.EnumerateObject().Select(p => Depth(p.Value)).DefaultIfEmpty(0).Max(),
    JsonValueKind.Array  => 1 + e.EnumerateArray().Select(Depth).DefaultIfEmpty(0).Max(),
    _ => 1,
};

static int CountValues(JsonElement e) => e.ValueKind switch
{
    JsonValueKind.Object => 1 + e.EnumerateObject().Sum(p => CountValues(p.Value)),
    JsonValueKind.Array  => 1 + e.EnumerateArray().Sum(CountValues),
    _ => 1,
};

void Fail(string vector, string message) { failures++; Console.Error.WriteLine($"FAIL {vector}: {message}"); }

static string FindRepoRoot()
{
    string? dir = Directory.GetCurrentDirectory();
    while (dir is not null && !File.Exists(Path.Combine(dir, "AGENTS.md"))) dir = Path.GetDirectoryName(dir);
    return dir ?? throw new InvalidOperationException("Run from inside the repository.");
}

// Slot kinds named by the SHAPE region. Declared last because a top-level
// program must place type declarations after every statement.
enum Slot { Identity, Token, Text, Count, WindowBound }
