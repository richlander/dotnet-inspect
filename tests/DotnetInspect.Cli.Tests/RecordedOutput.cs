using System.Runtime.CompilerServices;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Recorded command outputs that a gate compares byte for byte. The package
/// endpoint scope gates recorded theirs while the legacy <c>AssemblySet</c>
/// route was still their oracle, in the same run that proved each output
/// equal to it (docs/design/package-endpoint-scope.md, step 4 part 6). The
/// legacy route has since retired, and the recording keeps the gate.
/// </summary>
/// <remarks>
/// To accept an intentional output change, run the gate with
/// <c>DOTNET_INSPECT_RECORD_OUTPUT</c> set to the absolute path of this
/// project's <c>RecordedOutput</c> folder, then review the diff of the
/// rewritten files.
/// </remarks>
internal static class RecordedOutput
{
    private const string RecordVariable = "DOTNET_INSPECT_RECORD_OUTPUT";

    /// <summary>
    /// Asserts that <paramref name="actual"/> equals the output recorded for
    /// <paramref name="gate"/> and <paramref name="view"/>, or records it.
    /// </summary>
    public static void AssertEqual(
        string suite,
        string actual,
        string diagnostics,
        int view = 0,
        [CallerMemberName] string gate = "")
    {
        string name = Path.Combine(suite, $"{gate}.{view}.txt");
        if (Environment.GetEnvironmentVariable(RecordVariable) is { Length: > 0 } root)
        {
            string target = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, actual);
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "RecordedOutput", name);
        Assert.True(File.Exists(path), $"No recorded output at {path}; record it with {RecordVariable}.");
        string expected = File.ReadAllText(path);
        Assert.True(
            expected == actual,
            $"The output differs from RecordedOutput/{name}. If the change is intentional, "
                + $"rerun with {RecordVariable} and review the diff.{Environment.NewLine}{diagnostics}");
    }
}
