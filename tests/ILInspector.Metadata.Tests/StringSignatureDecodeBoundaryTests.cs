namespace ILInspector.Metadata.Tests;

public class StringSignatureDecodeBoundaryTests
{
    [Fact]
    public void MetadataStringDecoder_IsOnlyReachedThroughTheGateway()
    {
        var root = FindRepoRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "src", "ILInspector.Metadata"),
            Path.Combine(root, "src", "ILInspector.MetadataPrimitives"),
        };
        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "GuardedSignatureText.cs",
            "SignatureDecoder.cs",
            "TypeResolver.cs",
            // TypeNodeProvider delegates leaf-name callbacks to the string provider for
            // primitive/def/ref names; its top-level and nested-TypeSpec decode safety is
            // enforced separately by GuardedProviderDecode + TypeSpecGuard (issue #2575) and
            // frozen by ProviderSignatureDecodeBoundaryTests.
            "TypeNodeProvider.cs",
        };
        var violations = new List<string>();

        foreach (var sourceRoot in sourceRoots)
        {
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (allowedFiles.Contains(Path.GetFileName(file)))
                    continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("SignatureDecoder.Instance", StringComparison.Ordinal)
                        || lines[i].Contains("new SignatureDecoder(", StringComparison.Ordinal))
                        violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Metadata string signatures must enter through GuardedSignatureText. Raw uses:\n  "
            + string.Join("\n  ", violations));
    }

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
