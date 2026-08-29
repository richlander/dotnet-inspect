using InertText;

namespace DotnetInspector.Services;

internal static class AgentSkillDocument
{
    public static InertString PrepareForOutput(
        string content,
        bool normalizeGithubLinksToRaw)
    {
        var raw = new InertString(TextPolicy.Prose, content);
        if (raw.RequiredContainment)
        {
            return raw.ReplaceIfContainmentRequired(
                InertString.ContainmentRequiredPlaceholder);
        }

        string presented = normalizeGithubLinksToRaw
            ? GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(content)
            : content;
        return new InertString(TextPolicy.Prose, presented)
            .ReplaceIfContainmentRequired(
                InertString.ContainmentRequiredPlaceholder);
    }
}
