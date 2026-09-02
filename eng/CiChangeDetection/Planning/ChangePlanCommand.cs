namespace CiChangeDetection.Planning;

/// <summary>
/// The planner command boundary. It validates arguments, plans one candidate,
/// and publishes exactly one compact plan line, or refuses with a nonzero
/// status, a bounded ASCII diagnostic on standard error, and no plan.
/// </summary>
internal static class ChangePlanCommand
{
    internal const string Usage =
        "Usage: dotnet run eng/ci-plan.cs -- "
        + "<pull-request|push|merge-group> "
        + "--base <full-object-id> --candidate <full-object-id> "
        + "--evidence-directory <directory> [--repository <directory>]";

    /// <summary>
    /// Runs the command against explicit writers.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="standardOutput">The plan output writer.</param>
    /// <param name="standardError">The diagnostic writer.</param>
    /// <returns>Zero on a published plan; nonzero on refusal.</returns>
    internal static int Execute(
        string[] args,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        string? evidenceDirectory = FindUnambiguousEvidenceDirectory(args);
        try
        {
            if (evidenceDirectory is not null)
            {
                ChangePlanPublisher.PrepareEvidenceDirectory(
                    evidenceDirectory);
            }

            PlanRequest request = Parse(args);
            evidenceDirectory = request.EvidenceDirectory;
            if (!Directory.Exists(evidenceDirectory))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceDirectory,
                    "the evidence directory does not exist");
            }

            PlanningResult result = ChangePlanner.Plan(
                request.Repository,
                request.Kind,
                request.BaseObjectId,
                request.CandidateObjectId);
            _ = ChangePlanPublisher.Publish(
                result,
                request.EvidenceDirectory,
                standardOutput);
            return 0;
        }
        catch (PlanRefusalException refusal)
        {
            if (evidenceDirectory is not null)
            {
                ChangePlanPublisher.RemoveScopes(evidenceDirectory);
            }

            standardError.WriteLine($"ci-plan refused: {refusal.Message}");
            standardError.Flush();
            return 1;
        }
    }

    private static string? FindUnambiguousEvidenceDirectory(string[] args)
    {
        string? found = null;
        bool ambiguous = false;
        for (int index = 1; index + 1 < args.Length; index += 2)
        {
            if (args[index] != "--evidence-directory")
            {
                continue;
            }

            string candidate = args[index + 1];
            if (found is not null
                && !string.Equals(
                    found,
                    candidate,
                    StringComparison.Ordinal))
            {
                ambiguous = true;
            }

            found ??= candidate;
        }

        return !ambiguous
            && found is not null
            && Directory.Exists(found)
            ? found
            : null;
    }

    /// <summary>
    /// Parses and validates the argument vector.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The validated request.</returns>
    internal static PlanRequest Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.Usage,
                Usage);
        }

        PlanEventKind kind = args[0] switch
        {
            "pull-request" => PlanEventKind.PullRequestSyntheticCandidate,
            "push" => PlanEventKind.Push,
            "merge-group" => PlanEventKind.MergeGroup,
            _ => throw new PlanRefusalException(
                PlanRefusalCategory.Usage,
                "the first argument must name a supported event"),
        };

        string? baseObjectId = null;
        string? candidateObjectId = null;
        string? evidenceDirectory = null;
        string? repository = null;
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.Usage,
                    "an option is missing its value");
            }

            string value = args[index + 1];
            switch (args[index])
            {
                case "--base" when baseObjectId is null:
                    baseObjectId = value;
                    break;
                case "--candidate" when candidateObjectId is null:
                    candidateObjectId = value;
                    break;
                case "--evidence-directory" when evidenceDirectory is null:
                    evidenceDirectory = value;
                    break;
                case "--repository" when repository is null:
                    repository = value;
                    break;
                default:
                    throw new PlanRefusalException(
                        PlanRefusalCategory.Usage,
                        "an option is unknown or repeated");
            }
        }

        if (baseObjectId is null
            || candidateObjectId is null
            || evidenceDirectory is null)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.Usage,
                "a required option is missing");
        }

        repository ??= Environment.CurrentDirectory;
        if (!Directory.Exists(repository))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.Repository,
                "the repository directory does not exist");
        }

        return new PlanRequest(
            kind,
            baseObjectId,
            candidateObjectId,
            evidenceDirectory,
            repository);
    }

    /// <summary>
    /// One validated planning request.
    /// </summary>
    /// <param name="Kind">The event provenance kind.</param>
    /// <param name="BaseObjectId">The base endpoint object ID.</param>
    /// <param name="CandidateObjectId">The candidate endpoint object ID.</param>
    /// <param name="EvidenceDirectory">The explicit evidence directory.</param>
    /// <param name="Repository">The checked repository root directory.</param>
    internal sealed record PlanRequest(
        PlanEventKind Kind,
        string BaseObjectId,
        string CandidateObjectId,
        string EvidenceDirectory,
        string Repository);
}
