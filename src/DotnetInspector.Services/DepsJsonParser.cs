using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Parses .deps.json files for runtime dependencies.
/// </summary>
public static class DepsJsonParser
{
    /// <summary>
    /// Parses a deps file while preserving whether the projection was complete.
    /// </summary>
    public static DepsJsonParseResult TryParse(string depsPath)
    {
        try
        {
            return new DepsJsonParseResult(
                ParseCore(File.ReadAllText(depsPath)),
                Error: null);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException
                or InvalidOperationException)
        {
            return new DepsJsonParseResult(
                Data: null,
                Error: ex.Message);
        }
    }

    public static DepsJsonData Parse(string depsPath)
    {
        DepsJsonParseResult parsed = TryParse(depsPath);
        return parsed.Data ?? new DepsJsonData();
    }

    private static DepsJsonData ParseCore(string json)
    {
        var result = new DepsJsonData();
        using var doc = HardenedJson.Parse(json);
        RequireObject(doc.RootElement, "root");

        // Get runtime target
        if (doc.RootElement.TryGetProperty("runtimeTarget", out var runtimeTarget))
        {
            RequireObject(runtimeTarget, "runtimeTarget");
            if (runtimeTarget.TryGetProperty("name", out var name))
            {
                string targetName = ReadOptionalString(name, "runtimeTarget.name") ?? "";
                // Format: .NETCoreApp,Version=v8.0/win-x64 or .NETCoreApp,Version=v8.0
                int separator = targetName.IndexOf('/');
                if (separator >= 0 && separator + 1 < targetName.Length)
                {
                    result.RuntimeTargetRid = targetName[(separator + 1)..];
                }
            }
        }

        // Get runtime dependencies
        if (doc.RootElement.TryGetProperty("libraries", out var libraries))
        {
            RequireObject(libraries, "libraries");
            foreach (var lib in libraries.EnumerateObject())
            {
                string libraryName = ReadPropertyName(lib);
                int separator = libraryName.IndexOf('/');
                if (separator <= 0
                    || separator != libraryName.LastIndexOf('/')
                    || separator == libraryName.Length - 1)
                    continue;

                RequireObject(lib.Value, "library entry");
                if (lib.Value.TryGetProperty("type", out var typeElem))
                {
                    string type = ReadOptionalString(
                        typeElem,
                        "library type") ?? "";
                    if (type == "package")
                    {
                        result.RuntimeDependencies ??= [];
                        result.RuntimeDependencies.Add(new PackageDependency
                        {
                            Id = libraryName[..separator],
                            Version = libraryName[(separator + 1)..],
                        });
                    }
                }
            }
        }

        return result;
    }

    private static void RequireObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"'{propertyName}' must be a JSON object.");
        }
    }

    private static string? ReadOptionalString(
        JsonElement element,
        string propertyName)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Null => null,
                _ => throw new InvalidDataException(
                    $"'{propertyName}' must be a JSON string or null."),
            };
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(
                $"'{propertyName}' must be valid UTF-16.",
                ex);
        }
    }

    private static string ReadPropertyName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(
                "Library property name must be valid UTF-16.",
                ex);
        }
    }
}

/// <summary>
/// Complete parsed deps data or a visible reason why the file could not be projected.
/// </summary>
public sealed record DepsJsonParseResult(
    DepsJsonData? Data,
    string? Error)
{
    public bool IsComplete => Data is not null;
}
