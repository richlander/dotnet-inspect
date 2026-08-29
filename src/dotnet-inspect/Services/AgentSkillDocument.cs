using InertText;
using DotnetInspector.Models;

namespace DotnetInspector.Services;

internal static class AgentSkillDocument
{
    public static ContainmentSelectedText PrepareForOutput(
        string content,
        bool normalizeGithubLinksToRaw)
    {
        var raw = new InertString(TextPolicy.Prose, content);
        if (raw.RequiredContainment)
        {
            return ContainmentSelectedText.FromClassification(
                raw,
                content,
                InertString.ContainmentRequiredPlaceholder);
        }

        string presented = normalizeGithubLinksToRaw
            ? GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(content)
            : content;
        var normalized = new InertString(TextPolicy.Prose, presented);
        return ContainmentSelectedText.FromClassification(
            normalized,
            presented,
            InertString.ContainmentRequiredPlaceholder);
    }
}
