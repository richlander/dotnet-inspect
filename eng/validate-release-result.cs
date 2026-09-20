using System.Text.Json;

try
{
    if (args is ["--self-test"])
    {
        RunSelfTest();
        return;
    }

    Dictionary<string, string> options = ParseOptions(args);
    string expectedTag = Required(options, "--expected-tag");
    string expectedSha = Required(options, "--expected-sha");
    string releaseTag = ReadRequiredString(Required(options, "--release"), "tag_name");
    string tagCommitSha = ReadRequiredString(Required(options, "--tag-commit"), "sha");

    Validate(releaseTag, tagCommitSha, expectedTag, expectedSha);
    Console.WriteLine($"GitHub release {expectedTag} targets commit {expectedSha}.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Release result validation failed: {ex.Message}");
    Environment.ExitCode = 1;
}

static void Validate(
    string releaseTag,
    string tagCommitSha,
    string expectedTag,
    string expectedSha)
{
    RequireSha(expectedSha, "Expected");
    RequireSha(tagCommitSha, "Release tag");

    if (releaseTag != expectedTag)
    {
        throw new InvalidOperationException(
            $"Release tag is '{releaseTag}', not expected tag '{expectedTag}'.");
    }

    if (!tagCommitSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Release tag {expectedTag} targets {tagCommitSha}, not expected commit {expectedSha}.");
    }
}

static void RequireSha(string value, string label)
{
    if (value.Length != 40 || value.Any(c => !Uri.IsHexDigit(c)))
        throw new InvalidOperationException($"{label} SHA '{value}' is invalid.");
}

static string ReadRequiredString(string path, string property)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    if (!root.TryGetProperty(property, out JsonElement value) ||
        value.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new InvalidOperationException(
            $"JSON property '{property}' is missing or invalid in {path}.");
    }

    return value.GetString()!;
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    if (arguments.Length == 0 || arguments.Length % 2 != 0)
        throw new ArgumentException("Options must be supplied as --name value pairs.");

    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < arguments.Length; i += 2)
    {
        if (!arguments[i].StartsWith("--", StringComparison.Ordinal) ||
            !options.TryAdd(arguments[i], arguments[i + 1]))
        {
            throw new ArgumentException($"Invalid or duplicate option '{arguments[i]}'.");
        }
    }

    return options;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required option {name}.");

static void RunSelfTest()
{
    const string expectedTag = "v1.2.3";
    const string expectedSha = "1111111111111111111111111111111111111111";
    const string otherSha = "2222222222222222222222222222222222222222";

    Validate(expectedTag, expectedSha, expectedTag, expectedSha);
    ExpectFailure(
        () => Validate("v1.2.4", expectedSha, expectedTag, expectedSha),
        "not expected tag");
    ExpectFailure(
        () => Validate(expectedTag, otherSha, expectedTag, expectedSha),
        "not expected commit");
    ExpectFailure(
        () => Validate(expectedTag, expectedSha, expectedTag, "not-a-sha"),
        "Expected SHA");

    string scratch = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-release-result-{Guid.NewGuid():N}");
    Directory.CreateDirectory(scratch);
    try
    {
        string releasePath = Path.Combine(scratch, "release.json");
        string tagCommitPath = Path.Combine(scratch, "tag-commit.json");
        File.WriteAllText(
            releasePath,
            """
            {"tag_name":"v1.2.3","target_commitish":"main"}
            """);
        File.WriteAllText(
            tagCommitPath,
            """
            {"sha":"1111111111111111111111111111111111111111"}
            """);

        Validate(
            ReadRequiredString(releasePath, "tag_name"),
            ReadRequiredString(tagCommitPath, "sha"),
            expectedTag,
            expectedSha);
    }
    finally
    {
        Directory.Delete(scratch, recursive: true);
    }

    Console.WriteLine("Release result validator self-test passed.");
}

static void ExpectFailure(Action action, string messageFragment)
{
    try
    {
        action();
    }
    catch (InvalidOperationException ex) when (
        ex.Message.Contains(messageFragment, StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException($"Expected failure containing '{messageFragment}'.");
}
