using System.Text.Json;
using DotnetInspector.Core;

namespace DotnetInspector.Platforms.Formats;

/// <summary>Interprets shared-framework runtime configuration from UTF-8 bytes.</summary>
public static class PlatformRuntimeConfigurationReader
{
    public static PlatformManifestParseOutcome<PlatformRuntimeConfiguration>
        Parse(
            ReadOnlyMemory<byte> utf8Json,
            PlatformManifestParseBudget? budget = null,
            CancellationToken cancellationToken = default)
    {
        budget ??= PlatformManifestParseBudget.Default;
        cancellationToken.ThrowIfCancellationRequested();
        if (utf8Json.Length > budget.MaxBytes)
        {
            return Incomplete(
                "The runtime configuration exceeds the manifest byte limit.");
        }

        try
        {
            using JsonDocument document = ParseDocument(utf8Json);
            JsonElement root = RequireObject(document.RootElement, "root");
            if (!root.TryGetProperty(
                    "runtimeOptions",
                    out JsonElement runtimeOptions))
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind.InvalidDocumentShape,
                    "The runtime configuration has no runtimeOptions object.");
            }

            runtimeOptions = RequireObject(
                runtimeOptions,
                "runtimeOptions");
            var state = new RuntimeConfigurationState(budget);
            ManifestSettings defaults = ParseSettings(
                runtimeOptions,
                state);
            var frameworkElements =
                new List<(JsonElement Element, string Context)>();

            if (runtimeOptions.TryGetProperty(
                    "framework",
                    out JsonElement framework))
            {
                frameworkElements.Add((framework, "framework"));
            }

            if (runtimeOptions.TryGetProperty(
                    "frameworks",
                    out JsonElement frameworks))
            {
                if (frameworks.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(
                        PlatformManifestDiagnosticKind.InvalidDocumentShape,
                        "'runtimeOptions.frameworks' must be an array.");
                }

                foreach (JsonElement frameworkElement
                    in frameworks.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    frameworkElements.Add(
                        (frameworkElement, "frameworks entry"));
                }
            }

            foreach ((JsonElement frameworkElement, string context)
                in frameworkElements
                    .OrderBy(
                        static entry =>
                            FrameworkElementSortKey(entry.Element),
                        StringComparer.Ordinal)
                    .ThenBy(
                        static entry => entry.Element.GetRawText(),
                        StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.AddReference(
                    ParseFramework(
                        RequireObject(frameworkElement, context),
                        defaults,
                        state));
            }

            if (state.SawRollForward && state.SawLegacyCompatibilitySetting)
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind.InvalidRollForward,
                    "A runtime configuration cannot combine rollForward with applyPatches or rollForwardOnNoCandidateFx.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new PlatformManifestParseOutcome<
                PlatformRuntimeConfiguration>.Succeeded(
                    new PlatformRuntimeConfiguration(
                        Array.AsReadOnly(state.References.ToArray())));
        }
        catch (ManifestBudgetException ex)
        {
            return Incomplete(ex.Message);
        }
        catch (ManifestReadException ex)
        {
            return Rejected(ex.Kind, ex.Message);
        }
        catch (JsonException)
        {
            return Rejected(
                PlatformManifestDiagnosticKind.MalformedJson,
                "The runtime configuration is malformed or contains duplicate properties.");
        }
    }

    private static PlatformFrameworkReference ParseFramework(
        JsonElement element,
        ManifestSettings defaults,
        RuntimeConfigurationState state)
    {
        if (!element.TryGetProperty("name", out JsonElement nameElement))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.InvalidDocumentShape,
                "A framework reference has no name.");
        }
        if (!PlatformFrameworkName.TryParse(
                ReadRequiredString(nameElement, "framework name"),
                out PlatformFrameworkName? name))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.InvalidFrameworkName,
                "A framework reference has an invalid name.");
        }

        if (!element.TryGetProperty(
                "version",
                out JsonElement versionElement))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.InvalidDocumentShape,
                "A framework reference has no version.");
        }
        if (!PlatformVersion.TryParse(
                ReadRequiredString(versionElement, "framework version"),
                out PlatformVersion? version))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.InvalidVersion,
                "A framework reference has a non-canonical version.");
        }

        ManifestSettings settings = defaults.Apply(
            ParseSettings(element, state));
        return new PlatformFrameworkReference(
            name!,
            version!,
            settings.RollForward
                ?? PlatformFrameworkRollForward.Minor,
            settings.ApplyPatches ?? true);
    }

    private static ManifestSettings ParseSettings(
        JsonElement element,
        RuntimeConfigurationState state)
    {
        PlatformFrameworkRollForward? rollForward = null;
        bool? applyPatches = null;

        if (element.TryGetProperty(
                "rollForward",
                out JsonElement rollForwardElement))
        {
            state.SawRollForward = true;
            string value = ReadRequiredString(
                rollForwardElement,
                "rollForward");
            PlatformFrameworkRollForward? parsed =
                ParseRollForward(value);
            if (parsed is null)
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind.InvalidRollForward,
                    "'rollForward' has an unsupported value.");
            }

            rollForward = parsed.Value;
        }

        if (element.TryGetProperty(
                "applyPatches",
                out JsonElement applyPatchesElement))
        {
            state.SawLegacyCompatibilitySetting = true;
            if (applyPatchesElement.ValueKind
                is not JsonValueKind.True
                and not JsonValueKind.False)
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind.InvalidRollForward,
                    "'applyPatches' must be a Boolean.");
            }

            applyPatches = applyPatchesElement.GetBoolean();
        }

        if (element.TryGetProperty(
                "rollForwardOnNoCandidateFx",
                out JsonElement legacyElement))
        {
            state.SawLegacyCompatibilitySetting = true;
            if (legacyElement.ValueKind != JsonValueKind.Number
                || !legacyElement.TryGetInt32(out int legacy)
                || legacy is < 0 or > 2)
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind.InvalidRollForward,
                    "'rollForwardOnNoCandidateFx' must be 0, 1, or 2.");
            }

            rollForward = legacy switch
            {
                0 => PlatformFrameworkRollForward.LatestPatch,
                1 => PlatformFrameworkRollForward.Minor,
                2 => PlatformFrameworkRollForward.Major,
                _ => throw new InvalidOperationException(),
            };
        }

        return new ManifestSettings(rollForward, applyPatches);
    }

    private static PlatformFrameworkRollForward? ParseRollForward(
        string value)
    {
        foreach (PlatformFrameworkRollForward candidate
            in Enum.GetValues<PlatformFrameworkRollForward>())
        {
            if (string.Equals(
                    value,
                    candidate.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string FrameworkElementSortKey(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(
                "name",
                out JsonElement name)
            && name.ValueKind == JsonValueKind.String)
        {
            try
            {
                return "0:" + name.GetString();
            }
            catch (InvalidOperationException)
            {
                return "1:string:" + name.GetRawText();
            }
        }

        return "1:"
            + element.ValueKind
            + ":"
            + element.GetRawText();
    }

    private static JsonDocument ParseDocument(
        ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            return HardenedJson.Parse(utf8Json);
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException(
                "The runtime configuration contains an invalid property name.",
                ex);
        }
    }

    private static PlatformManifestParseOutcome<PlatformRuntimeConfiguration>
        Rejected(
            PlatformManifestDiagnosticKind kind,
            string summary) =>
        new PlatformManifestParseOutcome<
            PlatformRuntimeConfiguration>.Rejected(
                new PlatformManifestDiagnostic(kind, summary));

    private static PlatformManifestParseOutcome<PlatformRuntimeConfiguration>
        Incomplete(string summary) =>
        new PlatformManifestParseOutcome<
            PlatformRuntimeConfiguration>.Incomplete(
                new PlatformManifestDiagnostic(
                    PlatformManifestDiagnosticKind.WorkLimitExceeded,
                    summary));

    private sealed class RuntimeConfigurationState
    {
        private readonly PlatformManifestParseBudget _budget;
        private readonly HashSet<PlatformFrameworkName> _names = [];

        internal RuntimeConfigurationState(
            PlatformManifestParseBudget budget) =>
            _budget = budget;

        internal List<PlatformFrameworkReference> References { get; } = [];
        internal bool SawRollForward { get; set; }
        internal bool SawLegacyCompatibilitySetting { get; set; }

        internal void AddReference(PlatformFrameworkReference reference)
        {
            if (!_names.Add(reference.Name))
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind
                        .DuplicateFrameworkReference,
                    "A framework name appears more than once in the runtime configuration.");
            }
            if (References.Count == _budget.MaxFrameworkReferences)
            {
                throw new ManifestBudgetException(
                    "The runtime configuration exceeds the framework-reference limit.");
            }

            References.Add(reference);
        }
    }

    private readonly record struct ManifestSettings(
        PlatformFrameworkRollForward? RollForward,
        bool? ApplyPatches)
    {
        internal ManifestSettings Apply(ManifestSettings overrides) =>
            new(
                overrides.RollForward ?? RollForward,
                overrides.ApplyPatches ?? ApplyPatches);
    }

    private static JsonElement RequireObject(
        JsonElement element,
        string name) =>
        PlatformManifestReaderHelpers.RequireObject(element, name);

    private static string ReadRequiredString(
        JsonElement element,
        string name) =>
        PlatformManifestReaderHelpers.ReadRequiredString(element, name);

    private static ManifestReadException Invalid(
        PlatformManifestDiagnosticKind kind,
        string summary) =>
        new(kind, summary);
}

/// <summary>Interprets managed membership from dependency-manifest UTF-8 bytes.</summary>
public static class PlatformDependencyManifestReader
{
    public static PlatformManifestParseOutcome<PlatformDependencyManifest>
        Parse(
            ReadOnlyMemory<byte> utf8Json,
            PlatformManifestParseBudget? budget = null,
            CancellationToken cancellationToken = default)
    {
        budget ??= PlatformManifestParseBudget.Default;
        cancellationToken.ThrowIfCancellationRequested();
        if (utf8Json.Length > budget.MaxBytes)
        {
            return Incomplete(
                "The dependency manifest exceeds the manifest byte limit.");
        }

        try
        {
            using JsonDocument document = ParseDocument(utf8Json);
            JsonElement root = PlatformManifestReaderHelpers.RequireObject(
                document.RootElement,
                "root");
            string runtimeTargetName = ReadRuntimeTargetName(root);
            JsonElement target = ReadRuntimeTarget(root, runtimeTargetName);

            int libraryCount = 0;
            int assetCount = 0;
            var assets = new List<PlatformManifestAssetCoordinate>();
            var seen = new HashSet<PlatformManifestAssetCoordinate>();
            foreach (JsonProperty library in target
                .EnumerateObject()
                .OrderBy(
                    static property => property.Name,
                    StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (libraryCount == budget.MaxLibraries)
                {
                    throw new ManifestBudgetException(
                        "The dependency manifest exceeds the library limit.");
                }
                libraryCount++;

                JsonElement libraryValue =
                    PlatformManifestReaderHelpers.RequireObject(
                        library.Value,
                        "target library");
                ReadAssetGroup(
                    libraryValue,
                    "runtime",
                    include: static _ => true,
                    budget,
                    ref assetCount,
                    assets,
                    seen,
                    cancellationToken);
                ReadAssetGroup(
                    libraryValue,
                    "native",
                    include: static asset => string.Equals(
                        asset.FileName,
                        "System.Private.CoreLib.dll",
                        StringComparison.Ordinal),
                    budget,
                    ref assetCount,
                    assets,
                    seen,
                    cancellationToken);
            }

            assets.Sort(
                static (left, right) =>
                    string.CompareOrdinal(left.Value, right.Value));
            cancellationToken.ThrowIfCancellationRequested();
            return new PlatformManifestParseOutcome<
                PlatformDependencyManifest>.Succeeded(
                    new PlatformDependencyManifest(
                        runtimeTargetName,
                        Array.AsReadOnly(assets.ToArray())));
        }
        catch (ManifestBudgetException ex)
        {
            return Incomplete(ex.Message);
        }
        catch (ManifestReadException ex)
        {
            return Rejected(ex.Kind, ex.Message);
        }
        catch (JsonException)
        {
            return Rejected(
                PlatformManifestDiagnosticKind.MalformedJson,
                "The dependency manifest is malformed or contains duplicate properties.");
        }
    }

    private static string ReadRuntimeTargetName(JsonElement root)
    {
        if (!root.TryGetProperty(
                "runtimeTarget",
                out JsonElement runtimeTarget))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.MissingRuntimeTarget,
                "The dependency manifest has no runtimeTarget object.");
        }

        runtimeTarget = PlatformManifestReaderHelpers.RequireObject(
            runtimeTarget,
            "runtimeTarget");
        if (!runtimeTarget.TryGetProperty(
                "name",
                out JsonElement name))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.MissingRuntimeTarget,
                "The dependency manifest has no runtimeTarget.name.");
        }

        return PlatformManifestReaderHelpers.ReadRequiredString(
            name,
            "runtimeTarget.name");
    }

    private static JsonElement ReadRuntimeTarget(
        JsonElement root,
        string runtimeTargetName)
    {
        if (!root.TryGetProperty("targets", out JsonElement targets))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.MissingRuntimeTargetAssets,
                "The dependency manifest has no targets object.");
        }

        targets = PlatformManifestReaderHelpers.RequireObject(
            targets,
            "targets");
        if (!targets.TryGetProperty(
                runtimeTargetName,
                out JsonElement target))
        {
            throw Invalid(
                PlatformManifestDiagnosticKind.MissingRuntimeTargetAssets,
                "The dependency manifest has no assets for runtimeTarget.name.");
        }

        return PlatformManifestReaderHelpers.RequireObject(
            target,
            "runtime target");
    }

    private static void ReadAssetGroup(
        JsonElement library,
        string propertyName,
        Func<PlatformManifestAssetCoordinate, bool> include,
        PlatformManifestParseBudget budget,
        ref int assetCount,
        List<PlatformManifestAssetCoordinate> assets,
        HashSet<PlatformManifestAssetCoordinate> seen,
        CancellationToken cancellationToken)
    {
        if (!library.TryGetProperty(
                propertyName,
                out JsonElement group))
        {
            return;
        }

        group = PlatformManifestReaderHelpers.RequireObject(
            group,
            propertyName);
        foreach (JsonProperty assetProperty in group
            .EnumerateObject()
            .OrderBy(
                static property => property.Name,
                StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assetCount == budget.MaxAssets)
            {
                throw new ManifestBudgetException(
                    "The dependency manifest exceeds the asset limit.");
            }
            assetCount++;

            PlatformManifestReaderHelpers.RequireObject(
                assetProperty.Value,
                "asset metadata");
            if (!PlatformManifestAssetCoordinate.TryParse(
                    assetProperty.Name,
                    out PlatformManifestAssetCoordinate? asset))
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind.InvalidAssetCoordinate,
                    "The dependency manifest contains an invalid asset coordinate.");
            }

            if (include(asset!) && !seen.Add(asset!))
            {
                throw Invalid(
                    PlatformManifestDiagnosticKind.InvalidAssetCoordinate,
                    "The dependency manifest repeats a managed asset coordinate.");
            }
            if (include(asset!))
                assets.Add(asset!);
        }
    }

    private static PlatformManifestParseOutcome<PlatformDependencyManifest>
        Rejected(
            PlatformManifestDiagnosticKind kind,
            string summary) =>
        new PlatformManifestParseOutcome<
            PlatformDependencyManifest>.Rejected(
                new PlatformManifestDiagnostic(kind, summary));

    private static PlatformManifestParseOutcome<PlatformDependencyManifest>
        Incomplete(string summary) =>
        new PlatformManifestParseOutcome<
            PlatformDependencyManifest>.Incomplete(
                new PlatformManifestDiagnostic(
                    PlatformManifestDiagnosticKind.WorkLimitExceeded,
                    summary));

    private static JsonDocument ParseDocument(
        ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            return HardenedJson.Parse(utf8Json);
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException(
                "The dependency manifest contains an invalid property name.",
                ex);
        }
    }

    private static ManifestReadException Invalid(
        PlatformManifestDiagnosticKind kind,
        string summary) =>
        new(kind, summary);
}

internal static class PlatformManifestReaderHelpers
{
    internal static JsonElement RequireObject(
        JsonElement element,
        string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ManifestReadException(
                PlatformManifestDiagnosticKind.InvalidDocumentShape,
                $"'{name}' must be a JSON object.");
        }

        return element;
    }

    internal static string ReadRequiredString(
        JsonElement element,
        string name)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ManifestReadException(
                PlatformManifestDiagnosticKind.InvalidDocumentShape,
                $"'{name}' must be a JSON string.");
        }

        try
        {
            string? value = element.GetString();
            if (string.IsNullOrEmpty(value))
            {
                throw new ManifestReadException(
                    PlatformManifestDiagnosticKind.InvalidDocumentShape,
                    $"'{name}' must not be empty.");
            }

            return value;
        }
        catch (InvalidOperationException ex)
        {
            throw new ManifestReadException(
                PlatformManifestDiagnosticKind.InvalidDocumentShape,
                $"'{name}' must contain valid text.",
                ex);
        }
    }
}

internal sealed class ManifestReadException : Exception
{
    internal ManifestReadException(
        PlatformManifestDiagnosticKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException) =>
        Kind = kind;

    internal PlatformManifestDiagnosticKind Kind { get; }
}

internal sealed class ManifestBudgetException : Exception
{
    internal ManifestBudgetException(string message)
        : base(message)
    {
    }
}
