using System.Globalization;
using System.Reflection;

namespace InspectWeb.Engine;

internal static class BrowserBuildIdentityReader
{
    public static BrowserBuildIdentity Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        string version = informationalVersion.Split('+', 2)[0];
        Dictionary<string, string> metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Value))
            .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value!,
                StringComparer.Ordinal);
        metadata.TryGetValue("RepositoryCommit", out string? commit);
        metadata.TryGetValue("RepositoryUrl", out string? repositoryUrl);
        metadata.TryGetValue("BuildTimestampUtc", out string? builtAtUtc);

        return Create(version, commit, repositoryUrl, builtAtUtc);
    }

    internal static BrowserBuildIdentity Create(
        string version,
        string? commit,
        string? repositoryUrl,
        string? builtAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        commit = IsCommit(commit) ? commit : null;
        string? commitUrl = commit is not null
            && Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri? repository)
            && repository.Scheme == Uri.UriSchemeHttps
                ? $"{repository.ToString().TrimEnd('/')}/commit/{commit}"
                : null;
        builtAtUtc = DateTimeOffset.TryParse(
            builtAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset timestamp)
                ? timestamp.ToString("O", CultureInfo.InvariantCulture)
                : null;

        return new BrowserBuildIdentity(version, commit, builtAtUtc, commitUrl);
    }

    private static bool IsCommit(string? value) =>
        value is { Length: 40 }
        && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');
}
