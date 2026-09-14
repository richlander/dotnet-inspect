// Checks the normative vectors behind docs/design/portable-query-payload.md.
//
// The design document states the principles of the canonical query payload in
// prose. The exact shape — property order, tokens, tuple arity, limits — is not
// prose: it is the shape table below, and the vectors file witnesses it. This
// probe keeps the three in agreement until the product codec exists, at which
// point the codec's own tests consume the same vectors file and this file
// retires.
//
//   dotnet run eng/check-portable-query-payload-vectors.cs
//
// Every encode vector must canonicalize from its (possibly unordered) intent to
// exactly its canonical bytes; those bytes must validate, decode, and re-emit
// unchanged; and the vector's value count and byte size must sit under the
// declared limits. Every reject vector must be refused with exactly the reason
// it names. Exit code 0 means every vector holds; 2 means at least one did not.
//
// What this checker deliberately does not know: what any key, dimension, or
// order identity means, whether a value is a valid package identifier, or how
// terms compose. Those belong to the vocabulary and the intent model. The codec
// validates structure and canonical form; it never interprets a value.
//
// Escaping note for the eventual codec: this probe keeps every canonical vector
// to ASCII plus the two unambiguous escapes (quote and backslash), because the
// full scalar escaping rules are inherited from the packet owner and pin
// lowercase hex for C0 controls, while general-purpose serializers emit
// uppercase. The product codec must reuse the packet's canonical writer rather
// than a general serializer for exactly that reason.

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

// ─── Shape table ──────────────────────────────────────────────────────────────
// The normative structure of the payload. Change here, then add a vector.

string[] propertyOrder = ["t", "b", "s", "o"];
HashSet<string> operators = ["eq", "ne", "gte", "lte"];
HashSet<string> directions = ["asc", "desc"];
Dictionary<string, int> stageArity = new()
{
    ["head"] = 2, ["tail"] = 2, ["top"] = 2, ["window"] = 3,
};
HashSet<string> orderKinds = ["named", "fields"];

const int MaxPayloadBytes = 3 * 1024;
const int MaxDepth = 4;
const int MaxTerms = 24;
const int MaxBounds = 8;
const int MaxStages = 8;
const int MaxOrderOperations = 8;
const int MaxOrderFieldTerms = 8;
const int MaxIdentityBytes = 64;
const int MaxValueBytes = 256;
const int PacketMaxValues = 256;   // the packet owner's per-payload allowance

// ─── End shape table ──────────────────────────────────────────────────────────

string root = FindRepoRoot();
string vectorsPath = Path.Combine(
    root, "docs", "design", "models", "portable-query-payload", "vectors.json");
if (!File.Exists(vectorsPath))
{
    Console.Error.WriteLine($"vectors file not found: {vectorsPath}");
    return 2;
}

using JsonDocument vectors = JsonDocument.Parse(File.ReadAllText(vectorsPath));
int failures = 0;
int encodeCount = 0;
int rejectCount = 0;

foreach (JsonElement v in vectors.RootElement.GetProperty("encode").EnumerateArray())
{
    encodeCount++;
    string name = v.GetProperty("name").GetString()!;
    string expected = v.GetProperty("canonical").GetString()!;
    string intentText = v.GetProperty("intent").GetRawText();

    string? canonical = Canonicalize(intentText, out string? encodeError);
    if (canonical is null)
    {
        Fail(name, $"intent did not canonicalize: {encodeError}");
        continue;
    }
    if (canonical != expected)
    {
        Fail(name, $"canonical bytes differ\n    expected {expected}\n    actual   {canonical}");
        continue;
    }

    string? reason = Validate(expected);
    if (reason is not null)
    {
        Fail(name, $"canonical bytes were rejected as {reason}");
        continue;
    }

    string? reemitted = Canonicalize(expected, out _);
    if (reemitted != expected)
    {
        Fail(name, "decode then canonical write did not reproduce the bytes");
        continue;
    }

    int bytes = Encoding.UTF8.GetByteCount(expected);
    int values = CountValues(JsonDocument.Parse(expected).RootElement);
    if (bytes > MaxPayloadBytes)
        Fail(name, $"{bytes} bytes exceeds {MaxPayloadBytes}");
    if (values > PacketMaxValues)
        Fail(name, $"{values} JSON values exceeds the packet allowance of {PacketMaxValues}");
    if (v.TryGetProperty("expectValues", out JsonElement ev) && ev.GetInt32() != values)
        Fail(name, $"expected {ev.GetInt32()} JSON values, counted {values}");
}

foreach (JsonElement v in vectors.RootElement.GetProperty("reject").EnumerateArray())
{
    rejectCount++;
    string name = v.GetProperty("name").GetString()!;
    string bytes = v.GetProperty("bytes").GetString()!;
    string expectedReason = v.GetProperty("reject").GetString()!;
    string? reason = Validate(bytes);
    if (reason != expectedReason)
        Fail(name, $"expected rejection '{expectedReason}', got '{reason ?? "accepted"}'");
}

Console.WriteLine(
    $"{encodeCount} encode vectors, {rejectCount} reject vectors, {failures} failure(s)");
return failures == 0 ? 0 : 2;

// ─── Validation: bytes → reason or null ───────────────────────────────────────

string? Validate(string text)
{
    // Inherited escaping rejection, checked on the raw text before parsing.
    if (Regex.IsMatch(text, @"\\u[dD][89abAB][0-9a-fA-F]{2}(?!\\u[dD][c-fC-F][0-9a-fA-F]{2})")
        || Regex.IsMatch(text, @"(?<!\\u[dD][89abAB][0-9a-fA-F]{2})\\u[dD][c-fC-F][0-9a-fA-F]{2}"))
        return "unpaired-surrogate";

    JsonDocument doc;
    try
    {
        doc = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
    }
    catch (JsonException)
    {
        // Leading zeros and similar are lexically invalid JSON.
        return Regex.IsMatch(text, @"[\[,]\s*0\d") ? "bad-integer" : "malformed";
    }

    using (doc)
    {
        JsonElement obj = doc.RootElement;
        if (obj.ValueKind != JsonValueKind.Object) return "not-an-object";

        var names = new List<string>();
        foreach (JsonProperty p in obj.EnumerateObject()) names.Add(p.Name);
        if (names.Distinct().Count() != names.Count) return "duplicate-property";
        foreach (string n in names)
            if (!propertyOrder.Contains(n)) return "unknown-property";

        foreach (JsonProperty p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Array) return "bad-arity";
            if (p.Value.GetArrayLength() == 0) return "empty-part";
        }

        int stageCount = 0;
        string[] stageTokens = [];

        if (obj.TryGetProperty("t", out JsonElement t))
        {
            if (t.GetArrayLength() > MaxTerms) return "limit-exceeded";
            foreach (JsonElement term in t.EnumerateArray())
            {
                if (term.ValueKind != JsonValueKind.Array || term.GetArrayLength() != 3) return "bad-arity";
                foreach (JsonElement slot in term.EnumerateArray())
                    if (slot.ValueKind != JsonValueKind.String) return slot.ValueKind == JsonValueKind.Null ? "null-outside-window" : "bad-arity";
                if (!operators.Contains(term[1].GetString()!)) return "unknown-token";
                if (Utf8(term[0]) > MaxIdentityBytes || Utf8(term[2]) > MaxValueBytes) return "limit-exceeded";
            }
        }

        if (obj.TryGetProperty("b", out JsonElement b))
        {
            if (b.GetArrayLength() > MaxBounds) return "limit-exceeded";
            var dims = new HashSet<string>();
            foreach (JsonElement bound in b.EnumerateArray())
            {
                if (bound.ValueKind != JsonValueKind.Array || bound.GetArrayLength() != 2) return "bad-arity";
                if (bound[0].ValueKind != JsonValueKind.String) return "bad-arity";
                if (!IsCanonicalPositiveInteger(bound[1])) return "bad-integer";
                if (Utf8(bound[0]) > MaxIdentityBytes) return "limit-exceeded";
                if (!dims.Add(bound[0].GetString()!)) return "repeated-bound-dimension";
            }
        }

        if (obj.TryGetProperty("s", out JsonElement s))
        {
            stageCount = s.GetArrayLength();
            if (stageCount > MaxStages) return "limit-exceeded";
            stageTokens = new string[stageCount];
            int i = 0;
            foreach (JsonElement stage in s.EnumerateArray())
            {
                if (stage.ValueKind != JsonValueKind.Array || stage.GetArrayLength() < 1) return "bad-arity";
                if (stage[0].ValueKind != JsonValueKind.String) return "bad-arity";
                string tok = stage[0].GetString()!;
                if (!stageArity.TryGetValue(tok, out int arity)) return "unknown-token";
                if (stage.GetArrayLength() != arity) return "bad-arity";
                for (int k = 1; k < arity; k++)
                {
                    JsonElement slot = stage[k];
                    if (slot.ValueKind == JsonValueKind.Null)
                    {
                        if (tok != "window") return "null-outside-window";
                        continue;
                    }
                    if (!IsCanonicalPositiveInteger(slot)) return "bad-integer";
                }
                stageTokens[i++] = tok;
            }
        }

        if (obj.TryGetProperty("o", out JsonElement o))
        {
            if (o.GetArrayLength() > MaxOrderOperations) return "limit-exceeded";
            int fieldTerms = 0;
            bool sawBase = false;
            var stageRoles = new HashSet<int>();
            foreach (JsonElement op in o.EnumerateArray())
            {
                if (op.ValueKind != JsonValueKind.Array || op.GetArrayLength() < 2) return "bad-arity";
                JsonElement role = op[0];
                if (role.ValueKind == JsonValueKind.String)
                {
                    if (role.GetString() != "base") return "unknown-token";
                    if (sawBase) return "duplicate-role";
                    sawBase = true;
                }
                else if (role.ValueKind == JsonValueKind.Number && role.TryGetInt32(out int idx))
                {
                    if (idx < 0 || idx >= stageCount || stageTokens[idx] != "top") return "role-not-top";
                    if (!stageRoles.Add(idx)) return "duplicate-role";
                }
                else return "bad-arity";

                if (op[1].ValueKind != JsonValueKind.String) return "bad-arity";
                string kind = op[1].GetString()!;
                if (!orderKinds.Contains(kind)) return "unknown-token";
                int rest = op.GetArrayLength() - 2;
                if (kind == "named")
                {
                    if (rest != 2) return "bad-arity";
                }
                else
                {
                    if (rest == 0 || rest % 2 != 0) return "bad-arity";
                    fieldTerms += rest / 2;
                }
                for (int k = 2; k < op.GetArrayLength(); k++)
                {
                    if (op[k].ValueKind != JsonValueKind.String)
                        return op[k].ValueKind == JsonValueKind.Null ? "null-outside-window" : "bad-arity";
                    if (Utf8(op[k]) > MaxIdentityBytes) return "limit-exceeded";
                }
                for (int k = 3; k < op.GetArrayLength(); k += 2)
                    if (!directions.Contains(op[k].GetString()!)) return "unknown-token";
            }
            if (fieldTerms > MaxOrderFieldTerms) return "limit-exceeded";
        }

        if (Depth(obj) > MaxDepth) return "limit-exceeded";
        if (Encoding.UTF8.GetByteCount(text) > MaxPayloadBytes) return "limit-exceeded";

        // Structure is valid; now the bytes must be the one canonical spelling.
        string? canonical = Canonicalize(text, out _);
        return canonical == text ? null : "non-canonical";
    }
}

// ─── Canonicalization: any structurally valid JSON → the one canonical spelling

string? Canonicalize(string text, out string? error)
{
    error = null;
    JsonDocument doc;
    try { doc = JsonDocument.Parse(text); }
    catch (JsonException e) { error = e.Message; return null; }

    using (doc)
    {
        JsonElement obj = doc.RootElement;
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            w.WriteStartObject();

            if (obj.TryGetProperty("t", out JsonElement t) && t.GetArrayLength() > 0)
            {
                var terms = t.EnumerateArray()
                    .Select(x => (k: x[0].GetString()!, op: x[1].GetString()!, v: x[2].GetString()!))
                    .Distinct()
                    .OrderBy(x => x.k, StringComparer.Ordinal)
                    .ThenBy(x => x.op, StringComparer.Ordinal)
                    .ThenBy(x => x.v, StringComparer.Ordinal);
                w.WritePropertyName("t");
                w.WriteStartArray();
                foreach (var x in terms)
                {
                    w.WriteStartArray(); w.WriteStringValue(x.k); w.WriteStringValue(x.op); w.WriteStringValue(x.v); w.WriteEndArray();
                }
                w.WriteEndArray();
            }

            if (obj.TryGetProperty("b", out JsonElement b) && b.GetArrayLength() > 0)
            {
                var bounds = b.EnumerateArray()
                    .Select(x => (d: x[0].GetString()!, m: x[1].GetInt64()))
                    .OrderBy(x => x.d, StringComparer.Ordinal);
                w.WritePropertyName("b");
                w.WriteStartArray();
                foreach (var x in bounds)
                {
                    w.WriteStartArray(); w.WriteStringValue(x.d); w.WriteNumberValue(x.m); w.WriteEndArray();
                }
                w.WriteEndArray();
            }

            if (obj.TryGetProperty("s", out JsonElement s) && s.GetArrayLength() > 0)
            {
                w.WritePropertyName("s");
                w.WriteStartArray();
                foreach (JsonElement stage in s.EnumerateArray()) stage.WriteTo(w);
                w.WriteEndArray();
            }

            if (obj.TryGetProperty("o", out JsonElement o) && o.GetArrayLength() > 0)
            {
                var ops = o.EnumerateArray()
                    .OrderBy(x => x[0].ValueKind == JsonValueKind.String ? -1 : x[0].GetInt32());
                w.WritePropertyName("o");
                w.WriteStartArray();
                foreach (JsonElement op in ops) op.WriteTo(w);
                w.WriteEndArray();
            }

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

static bool IsCanonicalPositiveInteger(JsonElement e)
{
    if (e.ValueKind != JsonValueKind.Number) return false;
    string raw = e.GetRawText();
    if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E') || raw.StartsWith('-') || raw.StartsWith('+')) return false;
    if (raw.Length > 1 && raw[0] == '0') return false;
    return e.TryGetInt64(out long n) && n > 0;
}

static int Utf8(JsonElement s) => Encoding.UTF8.GetByteCount(s.GetString()!);

static int Depth(JsonElement e) => e.ValueKind switch
{
    JsonValueKind.Object => 1 + e.EnumerateObject().Select(p => Depth(p.Value)).DefaultIfEmpty(0).Max(),
    JsonValueKind.Array => 1 + e.EnumerateArray().Select(Depth).DefaultIfEmpty(0).Max(),
    _ => 1,
};

static int CountValues(JsonElement e) => e.ValueKind switch
{
    JsonValueKind.Object => 1 + e.EnumerateObject().Sum(p => CountValues(p.Value)),
    JsonValueKind.Array => 1 + e.EnumerateArray().Sum(CountValues),
    _ => 1,
};

void Fail(string vector, string message)
{
    failures++;
    Console.Error.WriteLine($"FAIL {vector}: {message}");
}

static string FindRepoRoot()
{
    string? dir = AppContext.BaseDirectory;
    // File-based apps build under a temp path; walk from the current directory instead.
    dir = Directory.GetCurrentDirectory();
    while (dir is not null && !File.Exists(Path.Combine(dir, "AGENTS.md")))
        dir = Path.GetDirectoryName(dir);
    return dir ?? throw new InvalidOperationException("Run from inside the repository.");
}
