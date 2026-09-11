using System.Text.Json;
using System.Text.Json.Serialization;

namespace DependencyPolicy;

internal enum DependencyGraphKind
{
    Project,
    Assembly,
}

internal sealed class DependencyPolicyDocument
{
    public required int SchemaVersion { get; init; }
    public required string Solution { get; init; }
    public required string Configuration { get; init; }
    public required DependencyRule[] Rules { get; init; }
}

internal sealed class DependencyRule
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required DependencyGraphKind[] Graphs { get; init; }
    public required string[] Targets { get; init; }
    public string[] ExcludeTargets { get; init; } = [];
    public string[] ProjectPaths { get; init; } = [];
    public string[] ExcludeProjectPaths { get; init; } = [];
    public string[]? AllowOnly { get; init; }
    public string[]? Deny { get; init; }
    public string[] Except { get; init; } = [];
}

internal sealed class DependencyPolicyException(string message) : Exception(message);

internal static class PolicyLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false),
        },
    };

    internal static DependencyPolicyDocument Load(string path) =>
        Deserialize(File.ReadAllText(path));

    internal static DependencyPolicyDocument Deserialize(string json)
    {
        DependencyPolicyDocument document =
            JsonSerializer.Deserialize<DependencyPolicyDocument>(
                json,
                SerializerOptions)
            ?? throw new DependencyPolicyException(
                "The dependency policy document is empty.");

        Validate(document);
        return document;
    }

    private static void Validate(DependencyPolicyDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new DependencyPolicyException(
                $"Unsupported dependency policy schema version "
                + $"{document.SchemaVersion}; expected 1.");
        }

        RequireText(document.Solution, "solution");
        RequireText(document.Configuration, "configuration");
        if (document.Rules is null || document.Rules.Length == 0)
        {
            throw new DependencyPolicyException(
                "The dependency policy must contain at least one rule.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (DependencyRule rule in document.Rules)
        {
            if (rule is null)
            {
                throw new DependencyPolicyException(
                    "The dependency policy contains a null rule.");
            }

            RequireText(rule.Id, "rule id");
            RequireText(rule.Source, $"source for rule '{rule.Id}'");
            if (!ids.Add(rule.Id))
            {
                throw new DependencyPolicyException(
                    $"Duplicate dependency rule id '{rule.Id}'.");
            }

            RequireItems(rule.Graphs, $"graphs for rule '{rule.Id}'");
            if (rule.Graphs.Distinct().Count() != rule.Graphs.Length)
            {
                throw new DependencyPolicyException(
                    $"Rule '{rule.Id}' contains duplicate graph kinds.");
            }

            RequirePatterns(rule.Targets, $"targets for rule '{rule.Id}'");
            RequirePatterns(
                rule.ExcludeTargets,
                $"excluded targets for rule '{rule.Id}'");
            RequirePatterns(
                rule.ProjectPaths,
                $"project paths for rule '{rule.Id}'");
            RequirePatterns(
                rule.ExcludeProjectPaths,
                $"excluded project paths for rule '{rule.Id}'");
            RequirePatterns(
                rule.Except,
                $"dependency exceptions for rule '{rule.Id}'");
            RejectTokens(
                rule.Targets,
                $"targets for rule '{rule.Id}'");
            RejectTokens(
                rule.ExcludeTargets,
                $"excluded targets for rule '{rule.Id}'");
            RejectTokens(
                rule.ProjectPaths,
                $"project paths for rule '{rule.Id}'");
            RejectTokens(
                rule.ExcludeProjectPaths,
                $"excluded project paths for rule '{rule.Id}'");

            bool hasAllowOnly = rule.AllowOnly is not null;
            bool hasDeny = rule.Deny is not null;
            if (hasAllowOnly == hasDeny)
            {
                throw new DependencyPolicyException(
                    $"Rule '{rule.Id}' must specify exactly one of "
                    + "'allowOnly' or 'deny'.");
            }

            if (rule.AllowOnly is not null)
            {
                RequirePatterns(
                    rule.AllowOnly,
                    $"allowed dependencies for rule '{rule.Id}'");
                if (rule.Except.Length != 0)
                {
                    throw new DependencyPolicyException(
                        $"Rule '{rule.Id}' may use 'except' only with 'deny'.");
                }
            }
            else
            {
                RequirePatterns(
                    rule.Deny!,
                    $"denied dependencies for rule '{rule.Id}'");
                if (rule.Deny!.Length == 0)
                {
                    throw new DependencyPolicyException(
                        $"Rule '{rule.Id}' must deny at least one dependency.");
                }

                RejectTokens(
                    rule.Deny,
                    $"denied dependencies for rule '{rule.Id}'");
                RejectTokens(
                    rule.Except,
                    $"dependency exceptions for rule '{rule.Id}'");
            }

            foreach (string token in (rule.AllowOnly ?? [])
                .Where(pattern => pattern.StartsWith('$')))
            {
                if (token is not "$platform" and not "$repository")
                {
                    throw new DependencyPolicyException(
                        $"Rule '{rule.Id}' uses unknown dependency token "
                        + $"'{token}'.");
                }
            }

        }
    }

    private static void RejectTokens(
        IEnumerable<string> patterns,
        string name)
    {
        string? token = patterns.FirstOrDefault(
            pattern => pattern.StartsWith('$'));
        if (token is not null)
        {
            throw new DependencyPolicyException(
                $"Dependency policy {name} may not contain token "
                + $"'{token}'.");
        }
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
        {
            throw new DependencyPolicyException(
                $"Dependency policy {name} must be non-empty and canonical.");
        }
    }

    private static void RequirePatterns<T>(
        IReadOnlyCollection<T>? values,
        string name)
    {
        if (values is null)
        {
            throw new DependencyPolicyException(
                $"Dependency policy {name} is required.");
        }

        foreach (T value in values)
        {
            if (value is not string pattern
                || string.IsNullOrWhiteSpace(pattern)
                || pattern != pattern.Trim())
            {
                throw new DependencyPolicyException(
                    $"Dependency policy {name} contains an invalid pattern.");
            }
        }
    }

    private static void RequireItems<T>(
        IReadOnlyCollection<T>? values,
        string name)
    {
        if (values is null || values.Count == 0)
        {
            throw new DependencyPolicyException(
                $"Dependency policy {name} must contain at least one value.");
        }
    }
}
