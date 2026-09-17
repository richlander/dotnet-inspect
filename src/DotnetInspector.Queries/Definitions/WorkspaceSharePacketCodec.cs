using System.Buffers;
using System.Buffers.Text;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;
using UntrustedDocuments;

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
    public const int LegacyFormatVersion = 1;
    public const int Format2Version = 2;
    public const int CurrentFormatVersion = 3;
    public const int MaxFormat1EncodedLength = 16 * 1024;
    public const int MaxFormat1DecodedUtf8Length = 12 * 1024;
    public const int MaxFormat1JsonDepth = 16;
    public const int MaxFormat1JsonValues = 1024;
    public const int MaxEncodedLength = 32 * 1024;
    public const int MaxDecodedUtf8Length = 24 * 1024;
    public const int MaxJsonDepth = 24;
    public const int MaxJsonValues = 2048;
    public const int MaxTabs = 12;
    public const int MaxContexts = 24;
    public const int MaxRegistrations = 24;

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
        ValidateEncodedLength(packet.FormatVersion, encoded.Length);

        string canonical = Encode(packet);
        if (!string.Equals(encoded, canonical, StringComparison.Ordinal))
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.NonCanonical,
                $"Workspace share state is valid but not in canonical format-{packet.FormatVersion} form.");
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
        ValidateEncodedLength(packet.FormatVersion, encoded.Length);

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
        ValidateJsonBudget(
            utf8Json.Span,
            MaxJsonDepth,
            MaxJsonValues);

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
        {
            int formatVersion = ReadFormatVersion(document.RootElement);
            ValidateDecodedLength(formatVersion, utf8Json.Length);
            if (formatVersion == LegacyFormatVersion)
            {
                ValidateJsonBudget(
                    utf8Json.Span,
                    MaxFormat1JsonDepth,
                    MaxFormat1JsonValues);
            }

            return Bind(document.RootElement, formatVersion);
        }
    }

    private static byte[] WriteValidatedJson(WorkspaceSharePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        byte[] utf8Json = WriteCanonicalJson(packet);
        ValidateDecodedLength(packet.FormatVersion, utf8Json.Length);

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

    private static void ValidateJsonBudget(
        ReadOnlySpan<byte> utf8Json,
        int maxDepth,
        int maxValues)
    {
        try
        {
            var reader = new Utf8JsonReader(
                utf8Json,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = maxDepth,
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
                    if (values > maxValues)
                    {
                        throw Failure(
                            WorkspaceSharePacketFailureKind.JsonValueLimitExceeded,
                            $"Workspace share state exceeds the {maxValues}-value JSON limit.");
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.InvalidJson,
                $"Workspace share state is invalid JSON or exceeds depth {maxDepth}.",
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

    private static int ReadFormatVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw InvalidShape("Workspace share state must be one JSON object.");

        JsonElement format = Required(root, "f");
        if (format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out int version))
        {
            throw InvalidShape(
                "Workspace share state requires integer format field 'f'.");
        }
        if (version is not (LegacyFormatVersion
            or Format2Version
            or CurrentFormatVersion))
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.UnsupportedFormat,
                $"Unsupported workspace share format {version}; expected "
                    + $"{LegacyFormatVersion}, {Format2Version}, or "
                    + $"{CurrentFormatVersion}.");
        }

        return version;
    }

    private static WorkspaceSharePacket Bind(
        JsonElement root,
        int formatVersion) =>
        formatVersion switch
        {
            LegacyFormatVersion => BindFormat1(root),
            Format2Version => BindFormat2(root),
            CurrentFormatVersion => BindFormat3(root),
            _ => throw new UnreachableException(),
        };

    private static WorkspaceSharePacket BindFormat1(JsonElement root)
    {
        ValidateProperties(
            root,
            "f",
            "t",
            "g",
            "a",
            "x",
            "v",
            "y",
            "m",
            "s",
            "c",
            "l");

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

    private static WorkspaceSharePacket BindFormat2(JsonElement root)
    {
        ValidateProperties(root, "f", "t", "g", "a", "x", "q", "v");
        if (root.TryGetProperty("q", out _))
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.UnsupportedFormat,
                "Query-bearing workspace share format 2 requires #6971.");
        }

        WorkspaceShareTab[] tabs = ReadTabs(Required(root, "t"));
        WorkspaceShareContext[] contexts = ReadContexts(
            Required(root, "g"),
            tabs);
        int? focusedTab = ReadNullableIndex(
            Required(root, "a"),
            "a",
            tabs.Length);
        if (focusedTab is int focused
            && tabs[focused].SourceKind != WorkspaceShareSourceKind.Package)
        {
            throw InvalidShape(
                "Workspace share format-2 focus must name a direct Package tuple.");
        }

        int selectedContext = ReadIndex(
            Required(root, "x"),
            "x",
            contexts.Length);
        WorkspaceShareViewState[] viewStates = ReadFormat2ViewStates(
            Required(root, "v"),
            tabs,
            Format2Version);

        return new WorkspaceSharePacket(
            tabs,
            contexts,
            focusedTab,
            selectedContext,
            viewStates);
    }

    private static WorkspaceSharePacket BindFormat3(JsonElement root)
    {
        ValidateProperties(root, "f", "t", "g", "r", "a", "x", "q", "v");
        if (root.TryGetProperty("q", out _))
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.UnsupportedFormat,
                "Query-bearing workspace share format 3 requires #6971.");
        }

        WorkspaceShareTab[] tabs = ReadTabs(
            Required(root, "t"),
            allowEmpty: true);
        WorkspaceShareContext[] contexts = ReadContexts(
            Required(root, "g"),
            tabs,
            allowEmpty: true);
        WorkspaceRegistration[] registrations = ReadRegistrations(
            Required(root, "r"));
        if (contexts.Length == 0 && registrations.Length == 0)
        {
            throw InvalidShape(
                "Workspace share format 3 requires at least one context or registration.");
        }

        int? focusedTab = ReadNullableIndex(
            Required(root, "a"),
            "a",
            tabs.Length);
        if (focusedTab is int focused
            && tabs[focused].SourceKind != WorkspaceShareSourceKind.Package)
        {
            throw InvalidShape(
                "Workspace share format-3 focus must name a direct Package tuple.");
        }

        int? selectedContext = ReadNullableIndex(
            Required(root, "x"),
            "x",
            contexts.Length);
        if ((contexts.Length == 0) != (selectedContext is null))
        {
            throw InvalidShape(
                "Workspace share format-3 selected context must be null exactly when contexts are empty.");
        }

        WorkspaceShareViewState[] viewStates = ReadFormat2ViewStates(
            Required(root, "v"),
            tabs,
            CurrentFormatVersion);

        return new WorkspaceSharePacket(
            tabs,
            contexts,
            registrations,
            focusedTab,
            selectedContext,
            viewStates);
    }

    private static void ValidateProperties(
        JsonElement root,
        params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        try
        {
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!names.Contains(property.Name))
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
    }

    private static WorkspaceShareTab[] ReadTabs(
        JsonElement element,
        bool allowEmpty = false)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw InvalidShape("Workspace share field 't' must be an array.");

        int count = element.GetArrayLength();
        if ((!allowEmpty && count == 0) || count > MaxTabs)
        {
            throw InvalidShape(
                allowEmpty
                    ? $"Workspace share field 't' permits at most {MaxTabs} entries."
                    : $"Workspace share field 't' requires between 1 and {MaxTabs} entries.");
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
        WorkspaceShareTab[] tabs,
        bool allowEmpty = false)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw InvalidShape("Workspace share field 'g' must be an array.");

        int count = element.GetArrayLength();
        if ((!allowEmpty && count == 0) || count > MaxContexts)
        {
            throw InvalidShape(
                allowEmpty
                    ? $"Workspace share field 'g' permits at most {MaxContexts} entries."
                    : $"Workspace share field 'g' requires between 1 and {MaxContexts} entries.");
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

    private static WorkspaceRegistration[] ReadRegistrations(
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw InvalidShape("Workspace share field 'r' must be an array.");
        if (element.GetArrayLength() > MaxRegistrations)
        {
            throw InvalidShape(
                $"Workspace share field 'r' permits at most {MaxRegistrations} entries.");
        }

        var registrations =
            new WorkspaceRegistration[element.GetArrayLength()];
        int index = 0;
        foreach (JsonElement tuple in element.EnumerateArray())
        {
            if (tuple.ValueKind != JsonValueKind.Array
                || tuple.GetArrayLength() < 2)
            {
                throw InvalidShape(
                    "Every workspace share registration must be one closed tuple.");
            }

            JsonElement.ArrayEnumerator values = tuple.EnumerateArray();
            values.MoveNext();
            string kind = RequiredString(
                values.Current,
                "registration kind");
            try
            {
                registrations[index++] = kind switch
                {
                    "l" when tuple.GetArrayLength() == 2 =>
                        new WorkspaceRegistration.ExactLibrary(
                            ReadExactLibraryCoordinate(Next(ref values))),
                    "p" when tuple.GetArrayLength() == 2 =>
                        new WorkspaceRegistration.PackagePrefix(
                            new PackagePrefixDeclaration(
                                RequiredString(
                                    Next(ref values),
                                    "registration package prefix"))),
                    "e" when tuple.GetArrayLength() == 5 =>
                        new WorkspaceRegistration.Ecosystem(
                            ReadEcosystemDeclaration(ref values)),
                    _ => throw InvalidShape(
                        "Workspace share registration tuple has an unknown kind or wrong arity."),
                };
            }
            catch (ArgumentException ex)
            {
                throw InvalidShape(
                    "Workspace share registration is invalid.",
                    ex);
            }
        }

        if (WorkspacePlan.ValidateRegistrations([.. registrations])
            is { } rejection)
        {
            throw InvalidShape(
                $"Workspace share registrations are invalid ({rejection}).");
        }

        return registrations;
    }

    private static WorkspaceEcosystemRegistrationDeclaration
        ReadEcosystemDeclaration(ref JsonElement.ArrayEnumerator values)
    {
        string id = RequiredString(
            Next(ref values),
            "Ecosystem registration id");
        string[] namespaceRoots = ReadStringArray(
            Next(ref values),
            "Ecosystem namespace roots");
        string[] corePackageIds = ReadStringArray(
            Next(ref values),
            "Ecosystem core packages");
        JsonElement populationsElement = Next(ref values);
        if (populationsElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidShape(
                "Workspace share Ecosystem populations must be an array.");
        }

        var populations =
            new WorkspaceEcosystemPopulationDeclaration[
                populationsElement.GetArrayLength()];
        int index = 0;
        foreach (JsonElement population in populationsElement.EnumerateArray())
        {
            populations[index++] = ReadEcosystemPopulation(population);
        }

        return new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create(id),
            namespaceRoots,
            corePackageIds.Select(package => new PackageCoordinate(package)),
            populations);
    }

    private static WorkspaceEcosystemPopulationDeclaration
        ReadEcosystemPopulation(JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array
            || tuple.GetArrayLength() != 2)
        {
            throw InvalidShape(
                "Workspace share Ecosystem population must contain exactly two values.");
        }

        JsonElement.ArrayEnumerator values = tuple.EnumerateArray();
        values.MoveNext();
        string kind = RequiredString(
            values.Current,
            "Ecosystem population kind");
        JsonElement payload = Next(ref values);
        try
        {
            return kind switch
            {
                "l" =>
                    new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                        ReadExactLibraryCoordinate(payload)),
                "t" =>
                    new WorkspaceEcosystemPopulationDeclaration.Platform(
                        new PlatformLibraryPopulationDeclaration(
                            ReadPlatformFamily(payload))),
                "p" =>
                    new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                        new PackagePrefixDeclaration(
                            RequiredString(
                                payload,
                                "Ecosystem package prefix"))),
                _ => throw InvalidShape(
                    "Workspace share Ecosystem population has an unknown kind."),
            };
        }
        catch (ArgumentException ex)
        {
            throw InvalidShape(
                "Workspace share Ecosystem population is invalid.",
                ex);
        }
    }

    private static ExactLibrarySourceCoordinate ReadExactLibraryCoordinate(
        JsonElement tuple)
    {
        if (tuple.ValueKind != JsonValueKind.Array)
        {
            throw InvalidShape(
                "Workspace share exact Library coordinate must be an array.");
        }

        JsonElement.ArrayEnumerator values = tuple.EnumerateArray();
        if (!values.MoveNext())
        {
            throw InvalidShape(
                "Workspace share exact Library coordinate requires a kind.");
        }
        string kind = RequiredString(
            values.Current,
            "exact Library coordinate kind");
        try
        {
            return kind switch
            {
                "p" when tuple.GetArrayLength() == 4 =>
                    new ExactLibrarySourceCoordinate.Package(
                        PackageSourceCoordinate.Create(
                            RequiredString(
                                Next(ref values),
                                "exact Library package id"),
                            RequiredString(
                                Next(ref values),
                                "exact Library package version")),
                        ReadManagedLibraryIdentity(Next(ref values))),
                "t" when tuple.GetArrayLength() == 3 =>
                    new ExactLibrarySourceCoordinate.Platform(
                        new PlatformLibraryPopulationDeclaration(
                            ReadPlatformFamily(Next(ref values))),
                        ReadManagedLibraryIdentity(Next(ref values))),
                _ => throw InvalidShape(
                    "Workspace share exact Library coordinate has an unknown kind or wrong arity."),
            };
        }
        catch (ArgumentException ex)
        {
            throw InvalidShape(
                "Workspace share exact Library coordinate is invalid.",
                ex);
        }
    }

    private static ManagedMetadataIdentity.Assembly ReadManagedLibraryIdentity(
        JsonElement element)
    {
        PortableLibraryIdentity library = ReadPortableLibraryIdentity(element);
        return new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                library.Name,
                Version.Parse(library.Version),
                library.Culture,
                library.PublicKeyToken));
    }

    private static PlatformFamily ReadPlatformFamily(JsonElement element)
    {
        string value = RequiredString(element, "Platform family");
        if (!Enum.TryParse(
                value,
                ignoreCase: false,
                out PlatformFamily family)
            || !Enum.IsDefined(family))
        {
            throw InvalidShape(
                "Workspace share Platform family must be 'DotNetRuntime' or 'AspNetCore'.");
        }

        return family;
    }

    private static string[] ReadStringArray(
        JsonElement element,
        string owner)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw InvalidShape(
                $"Workspace share {owner} must be an array.");
        }

        var values = new string[element.GetArrayLength()];
        int index = 0;
        foreach (JsonElement value in element.EnumerateArray())
            values[index++] = RequiredString(value, owner);
        return values;
    }

    private static JsonElement Next(ref JsonElement.ArrayEnumerator values)
    {
        if (!values.MoveNext())
            throw new UnreachableException();
        return values.Current;
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

    private static WorkspaceShareViewState[] ReadFormat2ViewStates(
        JsonElement element,
        WorkspaceShareTab[] tabs,
        int formatVersion)
    {
        if (element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() != tabs.Length + 1)
        {
            throw InvalidShape(
                $"Workspace share format-{formatVersion} field 'v' requires one leading "
                    + "Workspace row followed by one row for every tuple.");
        }

        var states = new WorkspaceShareViewState[tabs.Length + 1];
        int stateIndex = 0;
        foreach (JsonElement state in element.EnumerateArray())
        {
            if (state.ValueKind != JsonValueKind.Object)
            {
                throw InvalidShape(
                    $"Every format-{formatVersion} view row must be an object.");
            }

            ValidateProperties(state, "t", "r", "u", "f", "q", "l");
            if (state.TryGetProperty("q", out _)
                || state.TryGetProperty("l", out _))
            {
                throw Failure(
                    WorkspaceSharePacketFailureKind.UnsupportedFormat,
                    $"Query-bearing workspace share format {formatVersion} requires #6971.");
            }

            int? tabIndex = ReadNullableIndex(
                Required(state, "t"),
                "view-state t",
                tabs.Length);
            int? expected = stateIndex == 0 ? null : stateIndex - 1;
            if (tabIndex != expected)
            {
                throw InvalidShape(
                    $"Workspace share format-{formatVersion} view rows must begin with the "
                        + "null Workspace row and then follow tuple order.");
            }

            PortableRetainedSubjectContext? context =
                state.TryGetProperty("r", out JsonElement retained)
                    ? ReadRetainedContext(retained)
                    : null;
            PortableSubjectRequest? subject =
                state.TryGetProperty("u", out JsonElement requestedSubject)
                    ? ReadSubject(requestedSubject)
                    : null;
            string? facet = OptionalString(state, "f");

            if (tabIndex is int index
                && tabs[index].SourceKind != WorkspaceShareSourceKind.Package
                && (subject is not null || context is not null || facet is not null))
            {
                throw InvalidShape(
                    $"A format-{formatVersion} group tuple view row must remain "
                        + "undecorated.");
            }

            if (stateIndex == 0
                && subject is not PortableSubjectRequest.Workspace)
            {
                throw InvalidShape(
                    $"The leading format-{formatVersion} view row must request "
                        + "the Workspace subject.");
            }
            if (stateIndex == 0 && context is not null)
            {
                throw InvalidShape(
                    $"The leading format-{formatVersion} Workspace row cannot "
                        + "retain Package context.");
            }

            try
            {
                _ = new CommittedViewStateDefinition(
                    tabIndex is null ? null : $"t{tabIndex}",
                    subject,
                    context,
                    facet);
            }
            catch (ArgumentException ex)
            {
                throw InvalidShape(
                    $"Workspace share format-{formatVersion} view state is invalid.",
                    ex);
            }

            states[stateIndex++] = new WorkspaceShareViewState(
                tabIndex,
                subject,
                context,
                facet);
        }

        return states;
    }

    private static PortableSubjectRequest ReadSubject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw InvalidShape("Workspace share subject must be an object.");
        ValidateProperties(element, "k");
        return RequiredString(Required(element, "k"), "subject kind") switch
        {
            "workspace" => new PortableSubjectRequest.Workspace(),
            "package" => new PortableSubjectRequest.Package(),
            _ => throw InvalidShape(
                "Workspace share subject kind must be 'workspace' or 'package'."),
        };
    }

    private static PortableRetainedSubjectContext ReadRetainedContext(
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw InvalidShape("Workspace share retained context must be an object.");
        ValidateProperties(element, "k", "l", "y", "m", "s");

        string kind = RequiredString(
            Required(element, "k"),
            "retained-context kind");
        PortableLibraryIdentity? library =
            element.TryGetProperty("l", out JsonElement libraryElement)
                ? ReadPortableLibraryIdentity(libraryElement)
                : null;
        string? typeText = OptionalString(element, "y");
        string? memberAnchor = OptionalString(element, "m");
        string? memberSignature = OptionalString(element, "s");

        try
        {
            return kind switch
            {
                "package" when library is null
                    && typeText is null
                    && memberAnchor is null
                    && memberSignature is null =>
                    new PortableRetainedSubjectContext.Package(),
                "all-libraries" when library is null
                    && typeText is null
                    && memberAnchor is null
                    && memberSignature is null =>
                    new PortableRetainedSubjectContext.AllLibraries(),
                "library" when library is not null
                    && typeText is null
                    && memberAnchor is null
                    && memberSignature is null =>
                    new PortableRetainedSubjectContext.Library(library),
                "type" when library is not null
                    && typeText is not null
                    && memberAnchor is null
                    && memberSignature is null =>
                    new PortableRetainedSubjectContext.EscapedType(
                        library,
                        typeText),
                "member" when library is not null
                    && typeText is not null =>
                    new PortableRetainedSubjectContext.EscapedMember(
                        library,
                        typeText,
                        memberAnchor,
                        memberSignature),
                _ => throw InvalidShape(
                    "Workspace share retained context fields do not match its kind."),
            };
        }
        catch (ArgumentException ex)
        {
            throw InvalidShape(
                "Workspace share retained context is invalid.",
                ex);
        }
    }

    private static PortableLibraryIdentity ReadPortableLibraryIdentity(
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() != 4)
        {
            throw InvalidShape(
                "A portable Library identity must contain exactly four values.");
        }

        JsonElement.ArrayEnumerator values = element.EnumerateArray();
        values.MoveNext();
        string name = RequiredString(values.Current, "Library name");
        values.MoveNext();
        string version = RequiredString(values.Current, "Library version");
        values.MoveNext();
        string? culture = NullableString(values.Current, "Library culture");
        values.MoveNext();
        string? publicKeyToken = NullableString(
            values.Current,
            "Library public-key token");

        try
        {
            return new PortableLibraryIdentity(
                name,
                version,
                culture,
                publicKeyToken);
        }
        catch (ArgumentException ex)
        {
            throw InvalidShape(
                "Workspace share portable Library identity is invalid.",
                ex);
        }
    }

    private static int ReadIndex(JsonElement element, string field, int count)
    {
        if (count == 0)
        {
            throw InvalidShape(
                $"Workspace share {field} index must be null because its table is empty.");
        }
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

    private static int? ReadNullableIndex(
        JsonElement element,
        string field,
        int count) =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : ReadIndex(element, field, count);

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

    private static byte[] WriteCanonicalJson(WorkspaceSharePacket packet) =>
        packet.FormatVersion switch
        {
            LegacyFormatVersion => WriteFormat1CanonicalJson(packet),
            Format2Version => WriteFormat2CanonicalJson(packet),
            CurrentFormatVersion => WriteFormat3CanonicalJson(packet),
            _ => throw Failure(
                WorkspaceSharePacketFailureKind.UnsupportedFormat,
                $"Unsupported workspace share format {packet.FormatVersion}."),
        };

    private static byte[] WriteFormat1CanonicalJson(WorkspaceSharePacket packet)
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
        writer.WriteInteger(
            packet.SelectedContextIndex
                ?? throw InvalidShape(
                    "Workspace share format 1 requires a selected context."));
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

    private static byte[] WriteFormat2CanonicalJson(WorkspaceSharePacket packet)
    {
        if (packet.ViewStates.Count != packet.Tabs.Count + 1)
        {
            throw InvalidShape(
                "Workspace share format 2 requires one leading Workspace view "
                    + "row and one row for every tuple.");
        }

        var writer = new CanonicalWriter();
        writer.WriteAscii("{\"f\":2,\"t\":["u8);
        WriteTabs(writer, packet.Tabs);
        writer.WriteAscii("],\"g\":["u8);
        WriteContexts(writer, packet.Contexts);
        writer.WriteAscii("],\"a\":"u8);
        if (packet.FocusedTabIndex is int focused)
            writer.WriteInteger(focused);
        else
            writer.WriteAscii("null"u8);
        writer.WriteAscii(",\"x\":"u8);
        writer.WriteInteger(
            packet.SelectedContextIndex
                ?? throw InvalidShape(
                    "Workspace share format 2 requires a selected context."));
        writer.WriteAscii(",\"v\":["u8);
        for (int index = 0; index < packet.ViewStates.Count; index++)
        {
            if (index > 0)
                writer.WriteByte((byte)',');
            WriteFormat2ViewState(writer, packet.ViewStates[index]);
        }

        writer.WriteAscii("]}"u8);
        return writer.ToArray();
    }

    private static byte[] WriteFormat3CanonicalJson(WorkspaceSharePacket packet)
    {
        if (packet.ViewStates.Count != packet.Tabs.Count + 1)
        {
            throw InvalidShape(
                "Workspace share format 3 requires one leading Workspace view "
                    + "row and one row for every tuple.");
        }
        if (packet.Contexts.Count == 0 && packet.Registrations.Count == 0)
        {
            throw InvalidShape(
                "Workspace share format 3 requires at least one context or registration.");
        }
        if ((packet.Contexts.Count == 0)
            != (packet.SelectedContextIndex is null))
        {
            throw InvalidShape(
                "Workspace share format 3 selected context must be null exactly when contexts are empty.");
        }
        if (packet.Registrations.Count > MaxRegistrations)
        {
            throw InvalidShape(
                $"Workspace share format 3 permits at most {MaxRegistrations} registrations.");
        }

        var writer = new CanonicalWriter();
        writer.WriteAscii("{\"f\":3,\"t\":["u8);
        WriteTabs(writer, packet.Tabs);
        writer.WriteAscii("],\"g\":["u8);
        WriteContexts(writer, packet.Contexts);
        writer.WriteAscii("],\"r\":["u8);
        for (int index = 0; index < packet.Registrations.Count; index++)
        {
            if (index > 0)
                writer.WriteByte((byte)',');
            WriteRegistration(writer, packet.Registrations[index]);
        }
        writer.WriteAscii("],\"a\":"u8);
        if (packet.FocusedTabIndex is int focused)
            writer.WriteInteger(focused);
        else
            writer.WriteAscii("null"u8);
        writer.WriteAscii(",\"x\":"u8);
        if (packet.SelectedContextIndex is int selected)
            writer.WriteInteger(selected);
        else
            writer.WriteAscii("null"u8);
        writer.WriteAscii(",\"v\":["u8);
        for (int index = 0; index < packet.ViewStates.Count; index++)
        {
            if (index > 0)
                writer.WriteByte((byte)',');
            WriteFormat2ViewState(writer, packet.ViewStates[index]);
        }

        writer.WriteAscii("]}"u8);
        return writer.ToArray();
    }

    private static void WriteRegistration(
        CanonicalWriter writer,
        WorkspaceRegistration registration)
    {
        switch (registration)
        {
            case WorkspaceRegistration.ExactLibrary exact:
                writer.WriteAscii("[\"l\","u8);
                WriteExactLibraryCoordinate(writer, exact.Coordinate);
                writer.WriteByte((byte)']');
                break;
            case WorkspaceRegistration.PackagePrefix prefix:
                writer.WriteAscii("[\"p\","u8);
                writer.WriteString(prefix.Prefix.Prefix);
                writer.WriteByte((byte)']');
                break;
            case WorkspaceRegistration.Ecosystem ecosystem:
                WorkspaceEcosystemRegistrationDeclaration declaration =
                    ecosystem.Declaration;
                if (declaration.IntegrationScanner is not null)
                {
                    throw InvalidShape(
                        "Workspace share format 3 cannot preserve an Ecosystem integration scanner.");
                }

                writer.WriteAscii("[\"e\","u8);
                writer.WriteString(declaration.Id.Value);
                writer.WriteByte((byte)',');
                WriteStrings(writer, declaration.NamespaceRoots);
                writer.WriteByte((byte)',');
                WriteStrings(
                    writer,
                    declaration.CorePackages.Select(
                        package => package.PackageId));
                writer.WriteAscii(",["u8);
                for (int index = 0;
                    index < declaration.Populations.Length;
                    index++)
                {
                    if (index > 0)
                        writer.WriteByte((byte)',');
                    WriteEcosystemPopulation(
                        writer,
                        declaration.Populations[index]);
                }
                writer.WriteAscii("]]"u8);
                break;
            default:
                throw InvalidShape(
                    "Workspace share format 3 contains an unknown registration kind.");
        }
    }

    private static void WriteEcosystemPopulation(
        CanonicalWriter writer,
        WorkspaceEcosystemPopulationDeclaration population)
    {
        switch (population)
        {
            case WorkspaceEcosystemPopulationDeclaration.ExactLibrary exact:
                writer.WriteAscii("[\"l\","u8);
                WriteExactLibraryCoordinate(writer, exact.Coordinate);
                writer.WriteByte((byte)']');
                break;
            case WorkspaceEcosystemPopulationDeclaration.Platform platform:
                writer.WriteAscii("[\"t\","u8);
                writer.WriteString(platform.Population.Family.ToString());
                writer.WriteByte((byte)']');
                break;
            case WorkspaceEcosystemPopulationDeclaration.PackagePrefix prefix:
                writer.WriteAscii("[\"p\","u8);
                writer.WriteString(prefix.Prefix.Prefix);
                writer.WriteByte((byte)']');
                break;
            default:
                throw InvalidShape(
                    "Workspace share format 3 contains an unknown Ecosystem population kind.");
        }
    }

    private static void WriteExactLibraryCoordinate(
        CanonicalWriter writer,
        ExactLibrarySourceCoordinate coordinate)
    {
        switch (coordinate)
        {
            case ExactLibrarySourceCoordinate.Package package:
                writer.WriteAscii("[\"p\","u8);
                writer.WriteString(package.PackageCoordinate.PackageId);
                writer.WriteByte((byte)',');
                writer.WriteString(package.PackageCoordinate.Version);
                writer.WriteByte((byte)',');
                WritePortableLibraryIdentityTuple(
                    writer,
                    ToPortableLibraryIdentity(
                        coordinate.LibraryIdentity));
                writer.WriteByte((byte)']');
                break;
            case ExactLibrarySourceCoordinate.Platform platform:
                writer.WriteAscii("[\"t\","u8);
                writer.WriteString(platform.Population.Family.ToString());
                writer.WriteByte((byte)',');
                WritePortableLibraryIdentityTuple(
                    writer,
                    ToPortableLibraryIdentity(
                        coordinate.LibraryIdentity));
                writer.WriteByte((byte)']');
                break;
            default:
                throw InvalidShape(
                    "Workspace share format 3 exact Library registration has no portable source coordinate.");
        }
    }

    private static PortableLibraryIdentity ToPortableLibraryIdentity(
        ManagedMetadataIdentity.Assembly library)
    {
        AssemblyReferenceIdentity identity = library.Identity;
        Version version = identity.Version
            ?? throw InvalidShape(
                "Workspace share exact Library registration requires an assembly version.");
        try
        {
            return new PortableLibraryIdentity(
                identity.Name,
                version.ToString(4),
                identity.Culture,
                identity.PublicKeyToken);
        }
        catch (ArgumentException ex)
        {
            throw InvalidShape(
                "Workspace share exact Library identity is not portable.",
                ex);
        }
    }

    private static void WriteStrings(
        CanonicalWriter writer,
        IEnumerable<string> values)
    {
        writer.WriteByte((byte)'[');
        bool first = true;
        foreach (string value in values)
        {
            if (!first)
                writer.WriteByte((byte)',');
            writer.WriteString(value);
            first = false;
        }
        writer.WriteByte((byte)']');
    }

    private static void WriteTabs(
        CanonicalWriter writer,
        IReadOnlyList<WorkspaceShareTab> tabs)
    {
        for (int index = 0; index < tabs.Count; index++)
        {
            if (index > 0)
                writer.WriteByte((byte)',');
            WorkspaceShareTab tab = tabs[index];
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
    }

    private static void WriteContexts(
        CanonicalWriter writer,
        IReadOnlyList<WorkspaceShareContext> contexts)
    {
        for (int contextIndex = 0; contextIndex < contexts.Count; contextIndex++)
        {
            if (contextIndex > 0)
                writer.WriteByte((byte)',');
            writer.WriteByte((byte)'[');
            IReadOnlyList<int> indexes = contexts[contextIndex].TabIndexes;
            for (int index = 0; index < indexes.Count; index++)
            {
                if (index > 0)
                    writer.WriteByte((byte)',');
                writer.WriteInteger(indexes[index]);
            }

            writer.WriteByte((byte)']');
        }
    }

    private static void WriteFormat2ViewState(
        CanonicalWriter writer,
        WorkspaceShareViewState state)
    {
        writer.WriteAscii("{\"t\":"u8);
        if (state.TabIndex is int tabIndex)
            writer.WriteInteger(tabIndex);
        else
            writer.WriteAscii("null"u8);

        if (state.Context is not null)
        {
            writer.WriteAscii(",\"r\":"u8);
            WriteRetainedContext(writer, state.Context);
        }
        if (state.Subject is not null)
        {
            writer.WriteAscii(",\"u\":{\"k\":"u8);
            writer.WriteString(state.Subject.Kind switch
            {
                PortableSubjectRequestKind.Workspace => "workspace",
                PortableSubjectRequestKind.Package => "package",
                _ => throw new UnreachableException(),
            });
            writer.WriteByte((byte)'}');
        }

        writer.WriteOptionalProperty("f"u8, state.Facet);
        writer.WriteByte((byte)'}');
    }

    private static void WriteRetainedContext(
        CanonicalWriter writer,
        PortableRetainedSubjectContext context)
    {
        writer.WriteAscii("{\"k\":"u8);
        writer.WriteString(context.Kind switch
        {
            PortableRetainedSubjectContextKind.Package => "package",
            PortableRetainedSubjectContextKind.AllLibraries => "all-libraries",
            PortableRetainedSubjectContextKind.Library => "library",
            PortableRetainedSubjectContextKind.Type => "type",
            PortableRetainedSubjectContextKind.Member => "member",
            _ => throw new UnreachableException(),
        });

        switch (context)
        {
            case PortableRetainedSubjectContext.Library library:
                WritePortableLibraryIdentity(writer, library.LibraryIdentity);
                break;
            case PortableRetainedSubjectContext.Type type:
                WritePortableLibraryIdentity(writer, type.LibraryIdentity);
                writer.WriteAscii(",\"y\":"u8);
                writer.WriteString(type.TypeIdentity.ToEscapedFullName());
                break;
            case PortableRetainedSubjectContext.EscapedType type:
                WritePortableLibraryIdentity(writer, type.LibraryIdentity);
                writer.WriteAscii(",\"y\":"u8);
                writer.WriteString(type.EscapedTypeIdentity);
                break;
            case PortableRetainedSubjectContext.Member member:
                WritePortableLibraryIdentity(writer, member.LibraryIdentity);
                writer.WriteAscii(",\"y\":"u8);
                writer.WriteString(member.TypeIdentity.ToEscapedFullName());
                writer.WriteOptionalProperty("m"u8, member.MemberAnchor);
                writer.WriteOptionalProperty("s"u8, member.MemberSignature);
                break;
            case PortableRetainedSubjectContext.EscapedMember member:
                WritePortableLibraryIdentity(writer, member.LibraryIdentity);
                writer.WriteAscii(",\"y\":"u8);
                writer.WriteString(member.EscapedTypeIdentity);
                writer.WriteOptionalProperty("m"u8, member.MemberAnchor);
                writer.WriteOptionalProperty("s"u8, member.MemberSignature);
                break;
        }

        writer.WriteByte((byte)'}');
    }

    private static void WritePortableLibraryIdentity(
        CanonicalWriter writer,
        PortableLibraryIdentity library)
    {
        writer.WriteAscii(",\"l\":"u8);
        WritePortableLibraryIdentityTuple(writer, library);
    }

    private static void WritePortableLibraryIdentityTuple(
        CanonicalWriter writer,
        PortableLibraryIdentity library)
    {
        writer.WriteByte((byte)'[');
        writer.WriteString(library.Name);
        writer.WriteByte((byte)',');
        writer.WriteString(library.Version);
        writer.WriteByte((byte)',');
        writer.WriteNullableString(library.Culture);
        writer.WriteByte((byte)',');
        writer.WriteNullableString(library.PublicKeyToken);
        writer.WriteByte((byte)']');
    }

    private static void ValidateEncodedLength(
        int formatVersion,
        int length)
    {
        int limit = formatVersion == LegacyFormatVersion
            ? MaxFormat1EncodedLength
            : MaxEncodedLength;
        if (length > limit)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.EncodedLimitExceeded,
                $"Workspace share state exceeds the {limit}-character "
                    + $"format-{formatVersion} limit.");
        }
    }

    private static void ValidateDecodedLength(
        int formatVersion,
        int length)
    {
        int limit = formatVersion == LegacyFormatVersion
            ? MaxFormat1DecodedUtf8Length
            : MaxDecodedUtf8Length;
        if (length > limit)
        {
            throw Failure(
                WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
                $"Workspace share state exceeds the {limit}-byte decoded "
                    + $"format-{formatVersion} limit.");
        }
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
