using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Commands;

internal sealed record MatchDiscoveryReplayRequest(
    string? CandidateAssembly,
    string? CandidatePackage,
    string? CandidateTfm,
    string? ReplayLibrary,
    PackageReplaySources? ReplaySources,
    bool IncludeAll);

internal static class MatchDiscoveryReplay
{
    internal const string DisclosurePrefix =
        "Ranks structural candidates only. A rank does not establish Exact, Near, or Different, "
            + "nor semantic equivalence, authorship, copying intent, or vulnerability. ";

    internal const string Disclosure =
        DisclosurePrefix
            + "Run pairwise `match` on a candidate to obtain a checked relation.";

    internal static string DisclosureFor(
        MatchDiscoveryReplayRequest request)
        => request.CandidateAssembly is string candidateAssembly
            ? DisclosurePrefix
                + "Ranked tokens index "
                + Path.GetFileName(candidateAssembly)
                + ", which defines them rather than the assembly named on the command line; run "
                + "pairwise `match` in "
                + ShellCommandText.CurrentDialectName
                + " on a candidate with `"
                + ReplayOptions(request, candidateAssembly)
                + "` to obtain a checked relation."
            : request.CandidatePackage is string
                && request.ReplayLibrary is string replayLibrary
                    ? DisclosurePrefix
                        + "Ranked tokens index the package image selected for this run; run "
                        + "pairwise `match` in "
                        + ShellCommandText.CurrentDialectName
                        + " on a candidate with `"
                        + ReplayOptions(request, replayLibrary)
                        + "` to obtain a checked relation against that same image."
                    : request.ReplayLibrary is string directLibrary
                        ? DisclosurePrefix
                            + "Ranked tokens index the directly selected image; run pairwise "
                            + "`match` in "
                            + ShellCommandText.CurrentDialectName
                            + " on a candidate with `"
                            + ReplayOptions(request, directLibrary)
                            + "` to obtain a checked relation against that same image."
                        : Disclosure;

    static string ReplayOptions(
        MatchDiscoveryReplayRequest request,
        string library)
    {
        var options = new List<string>();
        if (request.CandidatePackage is string candidatePackage)
        {
            options.Add(
                "--package "
                    + ShellCommandText.Quote(candidatePackage));
            options.Add("--library " + ShellCommandText.Quote(library));
            if (request.CandidateTfm is string candidateTfm)
                options.Add("--tfm " + ShellCommandText.Quote(candidateTfm));

            if (request.ReplaySources is { } sources)
                options.Add(PackageReplaySourceArguments.Format(sources));
        }
        else
        {
            options.Add("--library " + ShellCommandText.Quote(library));
        }

        if (request.IncludeAll)
            options.Add("--all");

        return string.Join(' ', options);
    }
}
