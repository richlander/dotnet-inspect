using InertText;
using DotnetInspector.Services;
using DotnetInspect.Cli.Models;

namespace DotnetInspect.Cli.Services;

internal static class AgentSkillDocument
{
    private const int MaxDiagnosticRanges = 8;

    public static ContainmentSelectedText PrepareForOutput(
        string source,
        string content,
        int sourceLineOffset,
        bool normalizeGithubLinksToRaw)
    {
        var raw = new InertString(TextPolicy.Prose, content);
        if (raw.RequiredContainment)
        {
            return ContainmentSelectedText.FromClassification(
                raw,
                content,
                InertString.ContainmentRequiredPlaceholder,
                CreateDiagnostic(source, content, sourceLineOffset));
        }

        string presented = normalizeGithubLinksToRaw
            ? GitHubUrlResolver.NormalizeGitHubFileLinksToRaw(content)
            : content;
        var normalized = new InertString(TextPolicy.Prose, presented);
        return ContainmentSelectedText.FromClassification(
            normalized,
            presented,
            InertString.ContainmentRequiredPlaceholder,
            normalized.RequiredContainment
                ? CreateDiagnostic(source, presented, sourceLineOffset)
                : null);
    }

    private static ContainmentDiagnostic CreateDiagnostic(
        string source,
        string content,
        int sourceLineOffset)
    {
        var ranges = new List<ContainmentConcernRange>(MaxDiagnosticRanges);
        ConcernRangeBuilder? current = null;
        int totalRangeCount = 0;
        int searchStart = 0;
        int positionIndex = 0;
        int line = sourceLineOffset + 1;
        int column = 1;

        // Classification already established that the payload will be omitted. This bounded
        // second pass records only coordinates and scalar classifications for the diagnostic.
        void FlushCurrent()
        {
            if (current is null)
                return;

            totalRangeCount++;
            if (ranges.Count < MaxDiagnosticRanges)
                ranges.Add(current.ToRange());
            current = null;
        }

        while (searchStart < content.Length
            && !InertString.IsPermitted(
                TextPolicy.Prose,
                content.AsSpan(searchStart),
                out ScalarViolation? relative))
        {
            ScalarViolation violation = relative.Value;
            int index = searchStart + violation.Index;
            AdvancePosition(
                content,
                ref positionIndex,
                index,
                ref line,
                ref column);
            int violationLine = line;
            int violationColumn = column;
            int width = violation.Scalar > char.MaxValue ? 2 : 1;

            if (current is not null
                && current.EndIndex == index
                && current.Scalar == violation.Scalar
                && current.Category == violation.Category)
            {
                current.EndIndex = index + width;
                current.EndLine = violationLine;
                current.EndColumn = violationColumn;
                current.ScalarCount++;
            }
            else
            {
                FlushCurrent();
                current = new ConcernRangeBuilder(
                    index + width,
                    violationLine,
                    violationColumn,
                    violation.Scalar,
                    violation.Category);
            }

            AdvancePosition(
                content,
                ref positionIndex,
                index + width,
                ref line,
                ref column);
            searchStart = index + width;
        }

        FlushCurrent();
        return new ContainmentDiagnostic(source, totalRangeCount, ranges);
    }

    private static void AdvancePosition(
        string content,
        ref int index,
        int through,
        ref int line,
        ref int column)
    {
        while (index < through)
        {
            char current = content[index];
            if (current == '\r')
            {
                if (index + 1 < through && content[index + 1] == '\n')
                    index++;
                line++;
                column = 1;
            }
            else if (current is '\n' or '\u2028' or '\u2029')
            {
                line++;
                column = 1;
            }
            else
            {
                if (char.IsHighSurrogate(current)
                    && index + 1 < through
                    && char.IsLowSurrogate(content[index + 1]))
                {
                    index++;
                }
                column++;
            }

            index++;
        }
    }

    private sealed class ConcernRangeBuilder(
        int endIndex,
        int line,
        int column,
        int scalar,
        System.Globalization.UnicodeCategory category)
    {
        public int EndIndex { get; set; } = endIndex;
        public int StartLine { get; } = line;
        public int StartColumn { get; } = column;
        public int EndLine { get; set; } = line;
        public int EndColumn { get; set; } = column;
        public int Scalar { get; } = scalar;
        public System.Globalization.UnicodeCategory Category { get; } = category;
        public int ScalarCount { get; set; } = 1;

        public ContainmentConcernRange ToRange()
            => new(
                StartLine,
                StartColumn,
                EndLine,
                EndColumn,
                Scalar,
                Category,
                ScalarCount);
    }
}
