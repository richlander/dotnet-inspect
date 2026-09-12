using System.Text;
using System.Text.Json;
using DotnetInspector.Core;

namespace DotnetInspector.DependencyManifests;

public static class ApplicationDependencyManifestReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static ApplicationDependencyManifestParseOutcome Parse(
        ReadOnlyMemory<byte> utf8Json,
        ApplicationDependencyManifestParseBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        budget ??= ApplicationDependencyManifestParseBudget.Default;
        cancellationToken.ThrowIfCancellationRequested();
        if (utf8Json.Length > budget.MaxBytes)
        {
            return Incomplete(
                "The dependency manifest exceeds the byte limit.");
        }

        try
        {
            ValidateUtf8(utf8Json.Span);
            using JsonDocument document = HardenedJson.Parse(utf8Json);
            JsonElement root = RequireObject(
                document.RootElement,
                ApplicationDependencyManifestDiagnosticKind
                    .InvalidDocumentShape);
            string runtimeTargetName = ReadRuntimeTargetName(root, budget);
            JsonElement targets = ReadObjectProperty(
                root,
                "targets",
                ApplicationDependencyManifestDiagnosticKind
                    .InvalidDocumentShape);
            JsonElement libraries = ReadObjectProperty(
                root,
                "libraries",
                ApplicationDependencyManifestDiagnosticKind
                    .InvalidDocumentShape);
            if (!targets.TryGetProperty(
                    runtimeTargetName,
                    out JsonElement runtimeTarget))
            {
                throw Invalid(
                    ApplicationDependencyManifestDiagnosticKind
                        .MissingRuntimeTarget,
                    "The named runtime target is absent.");
            }
            runtimeTarget = RequireObject(
                runtimeTarget,
                ApplicationDependencyManifestDiagnosticKind
                    .InvalidDocumentShape);

            string compilationTargetName =
                CompilationTargetName(runtimeTargetName);
            JsonElement compilationTarget;
            if (string.Equals(
                    compilationTargetName,
                    runtimeTargetName,
                    StringComparison.Ordinal))
            {
                compilationTarget = runtimeTarget;
            }
            else if (!targets.TryGetProperty(
                    compilationTargetName,
                    out compilationTarget))
            {
                throw Invalid(
                    ApplicationDependencyManifestDiagnosticKind
                        .MissingCompilationTarget,
                    "The runtime target's compilation target is absent.");
            }
            else
            {
                compilationTarget = RequireObject(
                    compilationTarget,
                    ApplicationDependencyManifestDiagnosticKind
                        .InvalidDocumentShape);
            }

            var keys = new SortedSet<string>(StringComparer.Ordinal);
            AddTargetKeys(
                compilationTarget,
                keys,
                budget,
                cancellationToken);
            if (!string.Equals(
                    compilationTargetName,
                    runtimeTargetName,
                    StringComparison.Ordinal))
            {
                AddTargetKeys(
                    runtimeTarget,
                    keys,
                    budget,
                    cancellationToken);
            }

            int assetCount = 0;
            var result = new List<ApplicationDependencyManifestLibrary>(
                keys.Count);
            foreach (string key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LibraryMetadata metadata = ReadLibraryMetadata(
                    libraries,
                    key,
                    budget);
                var assets =
                    new List<ApplicationDependencyManifestAsset>();
                if (compilationTarget.TryGetProperty(
                        key,
                        out JsonElement compileLibrary))
                {
                    ReadAssetGroup(
                        RequireObject(
                            compileLibrary,
                            ApplicationDependencyManifestDiagnosticKind
                                .InvalidDocumentShape),
                        "compile",
                        ApplicationDependencyAssetRole.Compile,
                        assets,
                        budget,
                        ref assetCount,
                        cancellationToken);
                }
                if (runtimeTarget.TryGetProperty(
                        key,
                        out JsonElement runtimeLibrary))
                {
                    ReadAssetGroup(
                        RequireObject(
                            runtimeLibrary,
                            ApplicationDependencyManifestDiagnosticKind
                                .InvalidDocumentShape),
                        "runtime",
                        ApplicationDependencyAssetRole.Runtime,
                        assets,
                        budget,
                        ref assetCount,
                        cancellationToken);
                }

                result.Add(
                    new ApplicationDependencyManifestLibrary(
                        key,
                        metadata.Kind,
                        metadata.DeclaredPath,
                        Array.AsReadOnly(
                            assets
                                .OrderBy(
                                    static asset => asset.Role)
                                .ThenBy(
                                    static asset =>
                                        asset.Coordinate.Value,
                                    StringComparer.Ordinal)
                                .ToArray())));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ApplicationDependencyManifestParseOutcome.Succeeded(
                new ApplicationDependencyManifest(
                    runtimeTargetName,
                    compilationTargetName,
                    Array.AsReadOnly(result.ToArray())));
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
                ApplicationDependencyManifestDiagnosticKind.MalformedJson,
                "The dependency manifest is malformed or contains duplicate properties.");
        }
        catch (InvalidOperationException)
        {
            return Rejected(
                ApplicationDependencyManifestDiagnosticKind.MalformedJson,
                "The dependency manifest contains invalid JSON text.");
        }
    }

    private static string ReadRuntimeTargetName(
        JsonElement root,
        ApplicationDependencyManifestParseBudget budget)
    {
        if (!root.TryGetProperty(
                "runtimeTarget",
                out JsonElement runtimeTarget))
        {
            throw Invalid(
                ApplicationDependencyManifestDiagnosticKind
                    .MissingRuntimeTarget,
                "The dependency manifest has no runtime target.");
        }

        string name;
        if (runtimeTarget.ValueKind == JsonValueKind.String)
        {
            name = ReadRequiredString(
                runtimeTarget,
                budget,
                ApplicationDependencyManifestDiagnosticKind
                    .MissingRuntimeTarget);
        }
        else
        {
            runtimeTarget = RequireObject(
                runtimeTarget,
                ApplicationDependencyManifestDiagnosticKind
                    .MissingRuntimeTarget);
            if (!runtimeTarget.TryGetProperty(
                    "name",
                    out JsonElement nameElement))
            {
                throw Invalid(
                    ApplicationDependencyManifestDiagnosticKind
                        .MissingRuntimeTarget,
                    "The dependency manifest runtime target has no name.");
            }
            name = ReadRequiredString(
                nameElement,
                budget,
                ApplicationDependencyManifestDiagnosticKind
                    .MissingRuntimeTarget);
        }

        return name;
    }

    private static string CompilationTargetName(string runtimeTargetName)
    {
        int separator = runtimeTargetName.IndexOf('/');
        return separator < 0
            ? runtimeTargetName
            : runtimeTargetName[..separator];
    }

    private static void AddTargetKeys(
        JsonElement target,
        SortedSet<string> keys,
        ApplicationDependencyManifestParseBudget budget,
        CancellationToken cancellationToken)
    {
        foreach (JsonProperty library in target
            .EnumerateObject()
            .OrderBy(
                static property => property.Name,
                StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateScalar(library.Name, budget);
            keys.Add(library.Name);
            if (keys.Count > budget.MaxLibraries)
            {
                throw new ManifestBudgetException(
                    "The dependency manifest exceeds the library limit.");
            }
        }
    }

    private static LibraryMetadata ReadLibraryMetadata(
        JsonElement libraries,
        string key,
        ApplicationDependencyManifestParseBudget budget)
    {
        if (!libraries.TryGetProperty(key, out JsonElement library))
            return new(ApplicationDependencyLibraryKind.Unknown, null);

        library = RequireObject(
            library,
            ApplicationDependencyManifestDiagnosticKind
                .InvalidLibraryMetadata);
        ApplicationDependencyLibraryKind kind =
            ApplicationDependencyLibraryKind.Unknown;
        if (library.TryGetProperty("type", out JsonElement type))
        {
            string value = ReadRequiredString(
                type,
                budget,
                ApplicationDependencyManifestDiagnosticKind
                    .InvalidLibraryMetadata);
            kind = value switch
            {
                "package" => ApplicationDependencyLibraryKind.Package,
                "project" => ApplicationDependencyLibraryKind.Project,
                "reference" => ApplicationDependencyLibraryKind.Reference,
                "runtimepack" =>
                    ApplicationDependencyLibraryKind.RuntimePack,
                "referenceassembly" =>
                    ApplicationDependencyLibraryKind.ReferenceAssembly,
                _ => ApplicationDependencyLibraryKind.Unknown,
            };
        }

        ApplicationDependencyManifestCoordinate? path = null;
        if (library.TryGetProperty("path", out JsonElement pathElement))
        {
            string value = ReadRequiredString(
                pathElement,
                budget,
                ApplicationDependencyManifestDiagnosticKind
                    .InvalidLibraryMetadata);
            if (!ApplicationDependencyManifestCoordinate.TryParse(
                    value,
                    out path))
            {
                throw Invalid(
                    ApplicationDependencyManifestDiagnosticKind
                        .InvalidLibraryMetadata,
                    "A dependency library path is invalid.");
            }
        }

        return new(kind, path);
    }

    private static void ReadAssetGroup(
        JsonElement library,
        string propertyName,
        ApplicationDependencyAssetRole role,
        List<ApplicationDependencyManifestAsset> assets,
        ApplicationDependencyManifestParseBudget budget,
        ref int assetCount,
        CancellationToken cancellationToken)
    {
        if (!library.TryGetProperty(
                propertyName,
                out JsonElement group))
        {
            return;
        }
        group = RequireObject(
            group,
            ApplicationDependencyManifestDiagnosticKind
                .InvalidDocumentShape);

        foreach (JsonProperty asset in group
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
            if (asset.Name == "_._")
                continue;

            ValidateScalar(asset.Name, budget);
            if (!ApplicationDependencyManifestCoordinate.TryParse(
                    asset.Name,
                    out ApplicationDependencyManifestCoordinate? coordinate))
            {
                throw Invalid(
                    ApplicationDependencyManifestDiagnosticKind
                        .InvalidAssetCoordinate,
                    "A dependency asset coordinate is invalid.");
            }

            JsonElement value = RequireObject(
                asset.Value,
                ApplicationDependencyManifestDiagnosticKind
                    .InvalidDocumentShape);
            ApplicationDependencyManifestCoordinate? localPath = null;
            if (value.TryGetProperty(
                    "localPath",
                    out JsonElement localPathElement))
            {
                string localValue = ReadRequiredString(
                    localPathElement,
                    budget,
                    ApplicationDependencyManifestDiagnosticKind
                        .InvalidAssetCoordinate);
                if (!ApplicationDependencyManifestCoordinate.TryParse(
                        localValue,
                        out localPath))
                {
                    throw Invalid(
                        ApplicationDependencyManifestDiagnosticKind
                            .InvalidAssetCoordinate,
                        "A dependency asset local path is invalid.");
                }
            }

            assets.Add(
                new ApplicationDependencyManifestAsset(
                    role,
                    coordinate,
                    localPath));
        }
    }

    private static JsonElement ReadObjectProperty(
        JsonElement element,
        string propertyName,
        ApplicationDependencyManifestDiagnosticKind kind)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            throw Invalid(
                kind,
                "The dependency manifest is missing a required object.");
        }

        return RequireObject(value, kind);
    }

    private static JsonElement RequireObject(
        JsonElement element,
        ApplicationDependencyManifestDiagnosticKind kind)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(
                kind,
                "A dependency manifest value has an invalid shape.");
        }

        return element;
    }

    private static string ReadRequiredString(
        JsonElement element,
        ApplicationDependencyManifestParseBudget budget,
        ApplicationDependencyManifestDiagnosticKind kind)
    {
        if (element.ValueKind != JsonValueKind.String
            || element.GetString() is not { Length: > 0 } value)
        {
            throw Invalid(
                kind,
                "A dependency manifest value must be a non-empty string.");
        }
        ValidateScalar(value, budget);
        return value;
    }

    private static void ValidateScalar(
        string value,
        ApplicationDependencyManifestParseBudget budget)
    {
        if (value.Length > budget.MaxScalarCharacters)
        {
            throw new ManifestBudgetException(
                "The dependency manifest exceeds the scalar limit.");
        }
    }

    private static ManifestReadException Invalid(
        ApplicationDependencyManifestDiagnosticKind kind,
        string summary) =>
        new(kind, summary);

    private static void ValidateUtf8(ReadOnlySpan<byte> utf8)
    {
        try
        {
            StrictUtf8.GetCharCount(utf8);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ManifestReadException(
                ApplicationDependencyManifestDiagnosticKind.MalformedJson,
                "The dependency manifest contains invalid UTF-8.",
                exception);
        }
    }

    private static ApplicationDependencyManifestParseOutcome Rejected(
        ApplicationDependencyManifestDiagnosticKind kind,
        string summary) =>
        new ApplicationDependencyManifestParseOutcome.Rejected(
            new ApplicationDependencyManifestDiagnostic(kind, summary));

    private static ApplicationDependencyManifestParseOutcome Incomplete(
        string summary) =>
        new ApplicationDependencyManifestParseOutcome.Incomplete(
            new ApplicationDependencyManifestDiagnostic(
                ApplicationDependencyManifestDiagnosticKind
                    .WorkLimitExceeded,
                summary));

    private readonly record struct LibraryMetadata(
        ApplicationDependencyLibraryKind Kind,
        ApplicationDependencyManifestCoordinate? DeclaredPath);

    private sealed class ManifestReadException(
        ApplicationDependencyManifestDiagnosticKind kind,
        string message,
        Exception? innerException = null) :
        Exception(message, innerException)
    {
        internal ApplicationDependencyManifestDiagnosticKind Kind { get; } =
            kind;
    }

    private sealed class ManifestBudgetException(string message) :
        Exception(message);
}
