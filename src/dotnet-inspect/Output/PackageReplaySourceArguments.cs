using System.Collections.Immutable;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Output;

internal sealed record PackageReplaySources(
    ImmutableArray<string> Sources,
    ImmutableArray<string> AdditionalSources,
    string? ConfigFile,
    string? ConfigDirectory);

internal static class PackageReplaySourceArguments
{
    internal static bool TryCreate(
        NuGetSourceOptions? sourceOptions,
        string commandName,
        out PackageReplaySources? replaySources,
        out string? error,
        bool selectedVersionSourceRestriction = false,
        string? workingDirectory = null)
    {
        replaySources = null;
        error = null;
        if (sourceOptions is null
            || sourceOptions.Sources.Length == 0
                && sourceOptions.AdditionalSources.Length == 0
                && sourceOptions.ConfigFile is null
                && sourceOptions.ConfigDirectory is null)
        {
            return true;
        }

        workingDirectory ??= Directory.GetCurrentDirectory();
        string prefix = $"{commandName} cannot disclose a replayable package command because ";
        List<string> sources = [];
        List<string> additionalSources = [];
        foreach ((string option, string[] values, List<string> normalized) in new[]
        {
            ("--source", sourceOptions.Sources, sources),
            ("--add-source", sourceOptions.AdditionalSources, additionalSources),
        })
        {
            foreach (string value in values)
            {
                string replayValue;
                try
                {
                    replayValue = LocalPackageSourceIdentity.IsLocalSource(value)
                        ? LocalPackageSourceIdentity.Create(value, workingDirectory).CanonicalPath
                        : value;
                }
                catch (Exception ex) when (ex is
                    ArgumentException or IOException or NotSupportedException)
                {
                    error = prefix
                        + $"{option} contains a local package source path that cannot be resolved.";
                    return false;
                }

                if (!CanDiscloseSource(replayValue))
                {
                    error = prefix + (selectedVersionSourceRestriction
                        ? "the source that reported the selected package version contains URL "
                            + "components that must be redacted. Exact replay requires package "
                            + "source mapping that selects that configured source through "
                            + "--nugetconfig without printing its URL."
                        : $"{option} contains URL components that must be redacted. Configure "
                            + "that source in a nuget.config file and pass --nugetconfig instead.");
                    return false;
                }

                if (replayValue.Contains('`'))
                {
                    error = prefix
                        + $"{option} contains text that cannot be emitted losslessly.";
                    return false;
                }

                normalized.Add(replayValue);
            }
        }

        string? configError = null;
        if (!TryNormalizeConfigPath(
                sourceOptions.ConfigFile,
                "--nugetconfig",
                "Rename the config path before using it for package replay.",
                out string? configFile)
            || !TryNormalizeConfigPath(
                sourceOptions.ConfigDirectory,
                "--nugetconfig-directory",
                "Use a different config discovery directory for package replay.",
                out string? configDirectory))
        {
            error = configError;
            return false;
        }

        replaySources = new PackageReplaySources(
            [.. sources], [.. additionalSources], configFile, configDirectory);
        return true;

        bool TryNormalizeConfigPath(
            string? value,
            string option,
            string remedy,
            out string? path)
        {
            path = null;
            if (value is null)
                return true;

            try
            {
                path = Path.GetFullPath(value, workingDirectory);
            }
            catch (Exception ex) when (ex is
                ArgumentException or IOException or NotSupportedException)
            {
                configError = prefix + $"{option} contains a path that cannot be resolved. " + remedy;
                return false;
            }

            if (!InertString.IsPermitted(TextPolicy.Field, path) || path.Contains('`'))
            {
                configError = prefix
                    + $"{option} contains text that cannot be emitted losslessly. " + remedy;
                return false;
            }

            return true;
        }
    }

    // These are owner-issued configured spellings for a new invocation, not authorization receipts.
    internal static NuGetSourceOptions? RestrictToReportingSources(
        NuGetSourceOptions? original,
        IReadOnlyList<string> reportingSourceUrls)
    {
        ArgumentNullException.ThrowIfNull(reportingSourceUrls);
        if (reportingSourceUrls.Count == 0)
            return original;

        return new NuGetSourceOptions
        {
            Sources = [.. reportingSourceUrls],
            ConfigFile = original?.ConfigFile,
            ConfigDirectory = original?.ConfigDirectory,
        };
    }

    internal static string Format(PackageReplaySources? sources)
    {
        if (sources is null)
            return "";

        var arguments = new List<string>();
        arguments.AddRange(sources.Sources.Select(
            source => "--source " + ShellCommandText.Quote(source)));
        arguments.AddRange(sources.AdditionalSources.Select(
            source => "--add-source " + ShellCommandText.Quote(source)));
        if (sources.ConfigFile is string configFile)
            arguments.Add("--nugetconfig " + ShellCommandText.Quote(configFile));
        if (sources.ConfigDirectory is string configDirectory)
            arguments.Add("--nugetconfig-directory " + ShellCommandText.Quote(configDirectory));
        return string.Join(' ', arguments);
    }

    static bool CanDiscloseSource(string value)
    {
        string baseline = value;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            // Compare normalized spellings so harmless URL normalization is not redaction.
            baseline = uri.ToString();
        }

        return string.Equals(
            UrlRedaction.ForDiagnostics(value).ToString(),
            baseline,
            StringComparison.Ordinal);
    }
}
