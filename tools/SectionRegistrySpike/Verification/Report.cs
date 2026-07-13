namespace SectionRegistrySpike.Verification;

/// <summary>
/// Collects Markdown evidence lines and pass/fail assertions for one verification run. Every
/// <see cref="Check"/> failure is recorded with a message; <see cref="Success"/> is used to pick
/// the process exit code.
/// </summary>
public sealed class Report
{
    private readonly List<string> _lines = [];
    private readonly List<string> _failures = [];

    public bool Success => _failures.Count == 0;

    public void Heading(string text, int level = 2) => _lines.Add($"{new string('#', level)} {text}");

    public void Line(string text = "") => _lines.Add(text);

    public void Bullet(string text) => _lines.Add($"- {text}");

    public void Code(IEnumerable<string> lines)
    {
        _lines.Add("```text");
        foreach (var line in lines)
            _lines.Add(line);
        _lines.Add("```");
    }

    /// <summary>Records an assertion. Always emits a line; failures are also tracked for the exit code.</summary>
    public void Check(bool condition, string description)
    {
        if (condition)
        {
            Bullet($"PASS — {description}");
            return;
        }

        Bullet($"**FAIL — {description}**");
        _failures.Add(description);
    }

    public IReadOnlyList<string> Failures => _failures;

    public string Render() => string.Join('\n', _lines) + "\n";
}
