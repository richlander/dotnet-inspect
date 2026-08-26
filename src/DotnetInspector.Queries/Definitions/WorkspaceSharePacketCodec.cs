using System.Buffers;
using System.Buffers.Text;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Queries.Definitions;

internal readonly record struct GroupExpressionPin(
    int SegmentIndex,
    int SeparatorIndex,
    int ValueStart,
    int ValueLength);

/// <summary>
/// Decodes and canonically emits the versioned base64url workspace share
/// packet used by the browser <c>w</c> query value.
/// </summary>
/// <remarks>
/// Decoding is synchronous and bounded. It restores no partial state: malformed
/// encoding, JSON, shape, identity, or topology produces one typed failure
/// before artifact acquisition or query planning. The codec is host-neutral,
/// NativeAOT-compatible, and safe for single-threaded Browser/Wasm.
/// </remarks>
public static class WorkspaceSharePacketCodec
{
    public const int CurrentFormatVersion = 1;
    public const int MaxEncodedLength = 16 * 1024;
    public const int MaxDecodedUtf8Length = 12 * 1024;
    public const int MaxJsonDepth = 16;
    public const int MaxJsonValues = 1024;
    public const int MaxTabs = 12;
    public const int MaxContexts = 24;

    private static readonly UTF8Encoding s_utf8Strict = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Decodes one complete canonical base64url packet.
    /// </summary>
    public static WorkspaceSharePacket Decode(
        string encoded,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(encoded);

        if (encoded.Length == 0)
            throw Failure(WorkspaceSharePacketFailureKind.Empty, "Workspace share state is empty.");
        if (encoded.Length > MaxEncodedLength)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.EncodedLimitExceeded,
                $"Workspace share state exceeds the {MaxEncodedLength}-character limit.");
        }

        byte[] utf8Json = DecodeBase64Url(encoded);
        if (utf8Json.Length > MaxDecodedUtf8Length)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
                $"Workspace share state exceeds the {MaxDecodedUtf8Length}-byte decoded limit.");
        }

        WorkspaceSharePacket packet = ParseJson(utf8Json, cancellationToken);

        string canonical = Encode(packet);
        if (!string.Equals(encoded, canonical, StringComparison.Ordinal))
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.NonCanonical,
                "Workspace share state is valid but not in canonical v1 form.");
        }

        return packet;
    }

    /// <summary>
    /// Parses one duplicate-free JSON packet shape into the validated semantic
    /// model. Insignificant whitespace, property order, and equivalent JSON
    /// string escapes are accepted; <see cref="Encode"/> restores the one
    /// canonical base64url representation.
    /// </summary>
    public static WorkspaceSharePacket ParseJson(
        string json,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length == 0)
            throw Failure(WorkspaceSharePacketFailureKind.Empty, "Workspace share JSON is empty.");
        if (json.Length > MaxDecodedUtf8Length)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
                $"Workspace share JSON exceeds the {MaxDecodedUtf8Length}-byte limit.");
        }

        byte[] utf8Json;
        try
        {
            int byteCount = s_utf8Strict.GetByteCount(json);
            if (byteCount > MaxDecodedUtf8Length)
            {
                throw Failure(
                    WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
                    $"Workspace share JSON exceeds the {MaxDecodedUtf8Length}-byte limit.");
            }

            utf8Json = s_utf8Strict.GetBytes(json);
        }
        catch (EncoderFallbackException ex)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidJson,
                "Workspace share JSON contains invalid Unicode.",
                ex);
        }

        return ParseJson(utf8Json, cancellationToken);
    }

    /// <summary>
    /// Emits the exact compact JSON text used by canonical packet encoding.
    /// </summary>
    public static string SerializeJson(WorkspaceSharePacket packet)
    {
        byte[] utf8Json = WriteValidatedJson(packet);
        return s_utf8Strict.GetString(utf8Json);
    }

    /// <summary>
    /// Emits the one canonical base64url representation of a validated packet.
    /// </summary>
    public static string Encode(WorkspaceSharePacket packet)
    {
        byte[] utf8Json = WriteValidatedJson(packet);

        string encoded = Convert.ToBase64String(utf8Json)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        if (encoded.Length > MaxEncodedLength)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.EncodedLimitExceeded,
                $"Workspace share state exceeds the {MaxEncodedLength}-character limit.");
        }

        return encoded;
    }

    private static WorkspaceSharePacket ParseJson(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (utf8Json.Length == 0)
            throw Failure(WorkspaceSharePacketFailureKind.Empty, "Workspace share JSON is empty.");
        if (utf8Json.Length > MaxDecodedUtf8Length)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
                $"Workspace share JSON exceeds the {MaxDecodedUtf8Length}-byte limit.");
        }

        ValidateUtf8(utf8Json.Span);
        ValidateJsonBudget(utf8Json.Span);

        JsonDocument document;
        try
        {
            document = HardenedJson.Parse(utf8Json);
        }
        catch (JsonException ex)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidJson,
                "Workspace share JSON is not valid duplicate-free JSON.",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidJson,
                "Workspace share JSON contains invalid property text.",
                ex);
        }

        using (document)
            return Bind(document.RootElement);
    }

    private static byte[] WriteValidatedJson(WorkspaceSharePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        byte[] utf8Json = WriteCanonicalJson(packet);
        if (utf8Json.Length > MaxDecodedUtf8Length)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
                $"Workspace share state exceeds the {MaxDecodedUtf8Length}-byte decoded limit.");
        }

        return utf8Json;
    }

    private static byte[] DecodeBase64Url(string encoded)
    {
        int remainder = encoded.Length % 4;
        if (remainder == 1)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidBase64Url,
                "Workspace share state is not valid canonical base64url.");
        }

        int padding = remainder == 0 ? 0 : 4 - remainder;
        char[] base64 = new char[encoded.Length + padding];
        for (int index = 0; index < encoded.Length; index++)
        {
            char character = encoded[index];
            base64[index] = character switch
            {
                >= 'A' and <= 'Z' => character,
                >= 'a' and <= 'z' => character,
                >= '0' and <= '9' => character,
                '-' => '+',
                '_' => '/',
                _ => throw Failure(
                    WorkspaceSharePacketFailureKind.InvalidBase64Url,
                    "Workspace share state is not valid canonical base64url."),
            };
        }

        for (int index = encoded.Length; index < base64.Length; index++)
            base64[index] = '=';

        byte[] decoded = new byte[base64.Length / 4 * 3];
        if (!Convert.TryFromBase64Chars(base64, decoded, out int written))
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidBase64Url,
                "Workspace share state is not valid canonical base64url.");
        }

        if (written == decoded.Length)
            return decoded;

        return decoded.AsSpan(0, written).ToArray();
    }

    private static void ValidateJsonBudget(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(
                utf8Json,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaxJsonDepth,
                });
            int values = 0;
            while (reader.Read())
            {
                if (reader.TokenType is
                    JsonTokenType.StartObject
                    or JsonTokenType.StartArray
                    or JsonTokenType.String
                    or JsonTokenType.Number
                    or JsonTokenType.True
                    or JsonTokenType.False
                    or JsonTokenType.Null)
                {
                    values++;
                    if (values > MaxJsonValues)
                    {
                        throw Failure(
                            WorkspaceSharePacketFailureKind.JsonValueLimitExceeded,
                            $"Workspace share state exceeds the {MaxJsonValues}-value JSON limit.");
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidJson,
                $"Workspace share state is invalid JSON or exceeds depth {MaxJsonDepth}.",
                ex);
        }
    }

    private static void ValidateUtf8(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            _ = s_utf8Strict.GetCharCount(utf8Json);
        }
        catch (DecoderFallbackException ex)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidJson,
                "Workspace share state is not valid UTF-8 JSON.",
                ex);
        }
    }

    private static WorkspaceSharePacket Bind(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw InvalidShape("Workspace share state must be one JSON object.");

        try
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name is not ("f" or "t" or "g" or "a" or "x"
                    or "v" or "y" or "m" or "s" or "c" or "l"))
                {
                    throw InvalidShape(
                        "Workspace share state contains an unknown property.");
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            throw InvalidShape(
                "Workspace share state contains an invalid property name.",
                ex);
        }

        JsonElement format = Required(root, "f");
        if (format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out int version))
            throw InvalidShape("Workspace share state requires integer format field 'f'.");
        if (version != CurrentFormatVersion)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.UnsupportedFormat,
                $"Unsupported workspace share format {version}; expected {CurrentFormatVersion}.");
        }

        WorkspaceShareTab[] tabs = ReadTabs(Required(root, "t"));
        WorkspaceShareContext[] contexts = ReadContexts(
            Required(root, "g"),
            tabs);

        int activeTab = ReadIndex(Required(root, "a"), "a", tabs.Length);
        int selectedContext = ReadIndex(
            Required(root, "x"),
            "x",
            contexts.Length);

        string? lens = OptionalString(root, "v");
        string? type = OptionalString(root, "y");
        string? memberAnchor = OptionalString(root, "m");
        string? memberSignature = OptionalString(root, "s");
        string? section = OptionalString(root, "c");
        string[] libraries = ReadLibraries(root);

        if (memberAnchor is not null && memberSignature is not null)
            throw InvalidShape("Workspace share state cannot set both 'm' and 's'.");
        if ((memberAnchor is not null || memberSignature is not null) && type is null)
            throw InvalidShape("Workspace share member selection requires type field 'y'.");

        return new WorkspaceSharePacket(
            tabs,
            contexts,
            activeTab,
            selectedContext,
            lens,
            type,
            memberAnchor,
            memberSignature,
            section,
            libraries);
    }

    private static WorkspaceShareTab[] ReadTabs(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw InvalidShape("Workspace share field 't' must be an array.");

        int count = element.GetArrayLength();
        if (count is < 1 or > MaxTabs)
        {
            throw InvalidShape(
                $"Workspace share field 't' requires between 1 and {MaxTabs} entries.");
        }

        var tabs = new WorkspaceShareTab[count];
        var seen = new HashSet<TabIdentity>();
        int tabIndex = 0;
        foreach (JsonElement tuple in element.EnumerateArray())
        {
            if (tuple.ValueKind != JsonValueKind.Array || tuple.GetArrayLength() != 4)
                throw InvalidShape("Every workspace share tab tuple must contain exactly four values.");

            JsonElement.ArrayEnumerator values = tuple.EnumerateArray();
            values.MoveNext();
            string source = RequiredString(values.Current, "tab source");
            values.MoveNext();
            string? version = NullableString(values.Current, "tab version");
            values.MoveNext();
            string? framework = NullableString(values.Current, "tab framework");
            values.MoveNext();
            string? runtimeIdentifier = NullableString(values.Current, "tab runtime identifier");

            WorkspaceShareSourceKind sourceKind;
            string identitySource;
            if (source[0] == ':')
            {
                sourceKind = WorkspaceShareSourceKind.Group;
                if (!IsGroupExpression(source))
                    throw InvalidShape("A workspace share group source is not a valid group expression.");
                if (version is not null
                    && !HasPlatformBase(source))
                {
                    throw InvalidShape("Only a well-known Platform group may carry a v1 group pin.");
                }

                identitySource = source;
            }
            else
            {
                sourceKind = WorkspaceShareSourceKind.Package;
                if (!PackageCoordinateResolver.IsCanonicalPackageId(source))
                    throw InvalidShape("A workspace share package source is not a valid NuGet package id.");
                identitySource = source.ToLowerInvariant();
            }

            if (version is not null
                && !RealizedMemberCoordinate.IsCanonicalPackageVersion(version))
            {
                throw InvalidShape(
                    "A workspace share version must be one normalized lowercase concrete NuGet version.");
            }

            if (framework is not null
                && !RealizedMemberCoordinate.IsCanonicalFramework(framework))
            {
                throw InvalidShape(
                    "A workspace share framework must be one canonical lowercase acquisition target.");
            }

            if (runtimeIdentifier is not null
                && !RealizedMemberCoordinate.IsCanonicalRuntimeIdentifier(runtimeIdentifier))
            {
                throw InvalidShape(
                    "A workspace share runtime identifier must be canonical lowercase target text.");
            }

            var identity = new TabIdentity(
                sourceKind,
                identitySource,
                version,
                framework,
                runtimeIdentifier);
            if (!seen.Add(identity))
                throw InvalidShape("Workspace share field 't' contains a duplicate source tuple.");

            tabs[tabIndex++] = new WorkspaceShareTab(
                sourceKind,
                source,
                version,
                framework,
                runtimeIdentifier);
        }

        return tabs;
    }

    private static WorkspaceShareContext[] ReadContexts(
        JsonElement element,
        WorkspaceShareTab[] tabs)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw InvalidShape("Workspace share field 'g' must be an array.");

        int count = element.GetArrayLength();
        if (count is < 1 or > MaxContexts)
        {
            throw InvalidShape(
                $"Workspace share field 'g' requires between 1 and {MaxContexts} entries.");
        }

        var contexts = new WorkspaceShareContext[count];
        var referenced = new bool[tabs.Length];
        var contextIdentities = new HashSet<string>(StringComparer.Ordinal);
        int contextIndex = 0;
        foreach (JsonElement context in element.EnumerateArray())
        {
            if (context.ValueKind != JsonValueKind.Array
                || context.GetArrayLength() is < 1 or > MaxTabs)
            {
                throw InvalidShape(
                    $"Every workspace share context requires between 1 and {MaxTabs} tab indexes.");
            }

            int[] indexes = new int[context.GetArrayLength()];
            var local = new HashSet<int>();
            string? framework = null;
            string? runtimeIdentifier = null;
            bool first = true;
            bool sawGroup = false;
            int indexOffset = 0;
            foreach (JsonElement indexElement in context.EnumerateArray())
            {
                int index = ReadIndex(indexElement, "context tab", tabs.Length);
                if (!local.Add(index))
                    throw InvalidShape("A workspace share context repeats a tab index.");

                WorkspaceShareTab tab = tabs[index];
                if (tab.SourceKind == WorkspaceShareSourceKind.Group)
                {
                    if (!first || sawGroup)
                    {
                        throw InvalidShape(
                            "A workspace share context may contain one group source, and it must be first.");
                    }

                    sawGroup = true;
                }

                if (first)
                {
                    framework = tab.Framework;
                    runtimeIdentifier = tab.RuntimeIdentifier;
                    first = false;
                }
                else if (!string.Equals(framework, tab.Framework, StringComparison.Ordinal)
                    || !string.Equals(
                        runtimeIdentifier,
                        tab.RuntimeIdentifier,
                        StringComparison.Ordinal))
                {
                    throw InvalidShape(
                        "Every tab in one workspace share context must declare the same framework and runtime identifier.");
                }

                indexes[indexOffset++] = index;
                referenced[index] = true;
            }

            string identity = string.Join(',', indexes);
            if (!contextIdentities.Add(identity))
                throw InvalidShape("Workspace share field 'g' contains a duplicate context.");

            contexts[contextIndex++] = new WorkspaceShareContext(indexes);
        }

        if (referenced.Any(value => !value))
            throw InvalidShape("Every workspace share tab must belong to at least one context.");

        return contexts;
    }

    private static string[] ReadLibraries(JsonElement root)
    {
        if (!root.TryGetProperty("l", out JsonElement element))
            return [];
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
            throw InvalidShape("Workspace share field 'l' must be a nonempty array when present.");

        var libraries = new string[element.GetArrayLength()];
        string? previous = null;
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string library = RequiredString(item, "library identity");
            if (previous is not null
                && string.CompareOrdinal(previous, library) >= 0)
            {
                throw InvalidShape(
                    "Workspace share library identities must be unique and in ascending ordinal order.");
            }

            libraries[index++] = library;
            previous = library;
        }

        return libraries;
    }

    private static int ReadIndex(JsonElement element, string field, int count)
    {
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out int value)
            || value < 0
            || value >= count)
        {
            throw InvalidShape(
                $"Workspace share {field} index must be an integer from 0 through {count - 1}.");
        }

        return value;
    }

    private static JsonElement Required(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
            throw InvalidShape($"Workspace share state requires field '{name}'.");
        return value;
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
            return null;
        return RequiredString(value, $"field '{name}'");
    }

    private static string? NullableString(JsonElement element, string owner) =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : RequiredString(element, owner);

    private static string RequiredString(JsonElement element, string owner)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw InvalidShape($"Workspace share {owner} must be a string.");

        string value;
        try
        {
            value = element.GetString() ?? "";
        }
        catch (InvalidOperationException ex)
        {
            throw InvalidShape(
                $"Workspace share {owner} contains invalid Unicode.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidShape(
                $"Workspace share {owner} must not be empty or whitespace.");
        }

        EnsureWellFormedUnicode(value, owner);
        return value;
    }

    private static void EnsureWellFormedUnicode(string value, string owner)
    {
        ReadOnlySpan<char> remaining = value;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                remaining,
                out _,
                out int consumed);
            if (status != OperationStatus.Done)
                throw InvalidShape($"Workspace share {owner} contains invalid Unicode.");
            remaining = remaining[consumed..];
        }
    }

    internal static bool IsGroupExpression(string value)
    {
        return TryParseGroupExpression(value, out IReadOnlyList<GroupExpressionPin> pins)
            && pins.Count == 0;
    }

    internal static bool TryParseGroupExpression(
        string value,
        out IReadOnlyList<GroupExpressionPin> pins)
    {
        pins = Array.Empty<GroupExpressionPin>();
        if (value.Length < 2 || value[0] != ':')
            return false;

        var parsedPins = new List<GroupExpressionPin>();
        int position = 1;
        int segmentIndex = 0;
        while (true)
        {
            int nameStart = position;
            while (position < value.Length
                && value[position] is not ('@' or ':' or '+'))
            {
                if (!IsGroupNameCharacter(value[position]))
                    return false;
                position++;
            }
            if (position == nameStart)
                return false;

            if (position < value.Length && value[position] == '@')
            {
                int separatorIndex = position++;
                int valueStart = position;
                while (position < value.Length
                    && value[position] is not (':' or '+'))
                {
                    if (!IsGroupVersionCharacter(value[position]))
                        return false;
                    position++;
                }
                if (position == valueStart)
                    return false;

                parsedPins.Add(new GroupExpressionPin(
                    segmentIndex,
                    separatorIndex,
                    valueStart,
                    position - valueStart));
            }

            if (position == value.Length)
            {
                pins = parsedPins.Count == 0
                    ? Array.Empty<GroupExpressionPin>()
                    : new ReadOnlyCollection<GroupExpressionPin>(
                        parsedPins.ToArray());
                return true;
            }

            position++;
            segmentIndex++;
        }
    }

    private static bool HasPlatformBase(string value)
    {
        int end = value.AsSpan(1).IndexOfAny(':', '+');
        ReadOnlySpan<char> baseSegment = end < 0
            ? value.AsSpan(1)
            : value.AsSpan(1, end);
        return baseSegment.SequenceEqual("Platform");
    }

    internal static bool IsGroupName(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;
        foreach (char character in value)
        {
            if (!IsGroupNameCharacter(character))
                return false;
        }

        return true;
    }

    private static bool IsGroupNameCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';

    private static bool IsGroupVersionCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '.' or '-';

    private static byte[] WriteCanonicalJson(WorkspaceSharePacket packet)
    {
        var writer = new CanonicalWriter();
        writer.WriteAscii("{\"f\":1,\"t\":["u8);
        for (int index = 0; index < packet.Tabs.Count; index++)
        {
            if (index > 0)
                writer.WriteByte((byte)',');
            WorkspaceShareTab tab = packet.Tabs[index];
            writer.WriteByte((byte)'[');
            writer.WriteString(tab.Source);
            writer.WriteByte((byte)',');
            writer.WriteNullableString(tab.Version);
            writer.WriteByte((byte)',');
            writer.WriteNullableString(tab.Framework);
            writer.WriteByte((byte)',');
            writer.WriteNullableString(tab.RuntimeIdentifier);
            writer.WriteByte((byte)']');
        }

        writer.WriteAscii("],\"g\":["u8);
        for (int contextIndex = 0; contextIndex < packet.Contexts.Count; contextIndex++)
        {
            if (contextIndex > 0)
                writer.WriteByte((byte)',');
            writer.WriteByte((byte)'[');
            IReadOnlyList<int> indexes = packet.Contexts[contextIndex].TabIndexes;
            for (int index = 0; index < indexes.Count; index++)
            {
                if (index > 0)
                    writer.WriteByte((byte)',');
                writer.WriteInteger(indexes[index]);
            }

            writer.WriteByte((byte)']');
        }

        writer.WriteAscii("],\"a\":"u8);
        writer.WriteInteger(packet.ActiveTabIndex);
        writer.WriteAscii(",\"x\":"u8);
        writer.WriteInteger(packet.SelectedContextIndex);
        writer.WriteOptionalProperty("v"u8, packet.Lens);
        writer.WriteOptionalProperty("y"u8, packet.Type);
        writer.WriteOptionalProperty("m"u8, packet.MemberAnchor);
        writer.WriteOptionalProperty("s"u8, packet.MemberSignature);
        writer.WriteOptionalProperty("c"u8, packet.Section);
        if (packet.Libraries.Count > 0)
        {
            writer.WriteAscii(",\"l\":["u8);
            for (int index = 0; index < packet.Libraries.Count; index++)
            {
                if (index > 0)
                    writer.WriteByte((byte)',');
                writer.WriteString(packet.Libraries[index]);
            }

            writer.WriteByte((byte)']');
        }

        writer.WriteByte((byte)'}');
        return writer.ToArray();
    }

    private static WorkspaceSharePacketException Failure(
        WorkspaceSharePacketFailureKind kind,
        string message) =>
        new(kind, message);

    private static WorkspaceSharePacketException Failure(
        WorkspaceSharePacketFailureKind kind,
        string message,
        Exception innerException) =>
        new(kind, message, innerException);

    private static WorkspaceSharePacketException InvalidShape(string message) =>
        Failure(WorkspaceSharePacketFailureKind.InvalidShape, message);

    private static WorkspaceSharePacketException InvalidShape(
        string message,
        Exception innerException) =>
        Failure(WorkspaceSharePacketFailureKind.InvalidShape, message, innerException);

    private readonly record struct TabIdentity(
        WorkspaceShareSourceKind SourceKind,
        string Source,
        string? Version,
        string? Framework,
        string? RuntimeIdentifier);

    private sealed class CanonicalWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new(512);

        public void WriteByte(byte value)
        {
            Span<byte> destination = _buffer.GetSpan(1);
            destination[0] = value;
            _buffer.Advance(1);
        }

        public void WriteAscii(ReadOnlySpan<byte> value)
        {
            value.CopyTo(_buffer.GetSpan(value.Length));
            _buffer.Advance(value.Length);
        }

        public void WriteInteger(int value)
        {
            Span<byte> destination = _buffer.GetSpan(11);
            if (!Utf8Formatter.TryFormat(value, destination, out int written))
                throw new InvalidOperationException("Could not format a workspace share index.");
            _buffer.Advance(written);
        }

        public void WriteOptionalProperty(ReadOnlySpan<byte> name, string? value)
        {
            if (value is null)
                return;
            WriteAscii(",\""u8);
            WriteAscii(name);
            WriteAscii("\":"u8);
            WriteString(value);
        }

        public void WriteNullableString(string? value)
        {
            if (value is null)
            {
                WriteAscii("null"u8);
                return;
            }

            WriteString(value);
        }

        public void WriteString(string value)
        {
            WriteByte((byte)'"');
            Span<byte> utf8 = stackalloc byte[4];
            ReadOnlySpan<char> remaining = value;
            while (!remaining.IsEmpty)
            {
                OperationStatus status = Rune.DecodeFromUtf16(
                    remaining,
                    out Rune rune,
                    out int consumed);
                if (status != OperationStatus.Done)
                {
                    throw InvalidShape(
                        "Workspace share state contains invalid Unicode.");
                }

                remaining = remaining[consumed..];
                int scalar = rune.Value;
                switch (scalar)
                {
                    case '"':
                        WriteAscii("\\\""u8);
                        break;
                    case '\\':
                        WriteAscii("\\\\"u8);
                        break;
                    case '\b':
                        WriteAscii("\\b"u8);
                        break;
                    case '\t':
                        WriteAscii("\\t"u8);
                        break;
                    case '\n':
                        WriteAscii("\\n"u8);
                        break;
                    case '\f':
                        WriteAscii("\\f"u8);
                        break;
                    case '\r':
                        WriteAscii("\\r"u8);
                        break;
                    case < 0x20:
                        WriteAscii("\\u00"u8);
                        WriteByte(HexLower((scalar >> 4) & 0xF));
                        WriteByte(HexLower(scalar & 0xF));
                        break;
                    default:
                        int written = rune.EncodeToUtf8(utf8);
                        WriteAscii(utf8[..written]);
                        break;
                }
            }

            WriteByte((byte)'"');
        }

        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

        private static byte HexLower(int value) =>
            (byte)(value < 10 ? '0' + value : 'a' + value - 10);
    }
}
