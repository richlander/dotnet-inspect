using System.Text;
using System.Text.Json;
using InertText;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task SkillDocuments_OmitPayloadsThatRequireContainment()
    {
        const string bidi = "\u202E";
        var (packagePath, packageTempDir) = CreateLocalReadmePackage(
            "Test.Skills.ContainedOutput",
            "README.md",
            "readme",
            null,
            null,
            ("skills/package-skill/SKILL.md", $"package{bidi}skill"));
        var (projectPath, projectTempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Skills.ContainedOutput",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [CompliantProjectSkill("skills/project-skill/SKILL.md", $"project{bidi}skill")]));

        try
        {
            var (packageExit, packageOutput, packageError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--raw");
            var (projectExit, projectOutput, projectError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--raw");
            var (packageJsonExit, packageJson, packageJsonError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--jsonl");
            var (projectJsonExit, projectJson, projectJsonError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--jsonl");
            var (contentExit, contentOutput, contentError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content", "--raw");
            var (contentBlocksExit, contentBlocksOutput, contentBlocksError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content");
            var (contentJsonExit, contentJson, contentJsonError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content", "--jsonl");

            Assert.Equal(0, packageExit);
            Assert.Equal(0, projectExit);
            Assert.Equal(0, packageJsonExit);
            Assert.Equal(0, projectJsonExit);
            Assert.Equal(0, contentExit);
            Assert.Equal(0, contentBlocksExit);
            Assert.Equal(0, contentJsonExit);
            AssertContainmentWarning(
                packageError,
                "skills/package-skill/SKILL.md");
            AssertContainmentWarning(
                projectError,
                "skills/project-skill/SKILL.md");
            AssertContainmentWarning(
                packageJsonError,
                "skills/package-skill/SKILL.md");
            AssertContainmentWarning(
                projectJsonError,
                "skills/project-skill/SKILL.md");
            AssertContainmentWarning(
                contentError,
                "skills/package-skill/SKILL.md");
            AssertContainmentWarning(
                contentBlocksError,
                "skills/package-skill/SKILL.md");
            AssertContainmentWarning(
                contentJsonError,
                "skills/package-skill/SKILL.md");
            string placeholder = InertString.ContainmentRequiredPlaceholder.ToString();
            Assert.Equal(placeholder, packageOutput);
            Assert.Equal(placeholder, projectOutput);
            Assert.Equal(placeholder + "\n", contentOutput);
            Assert.Contains(placeholder + "\n", contentBlocksOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(bidi, contentBlocksOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(bidi, packageJson, StringComparison.Ordinal);
            Assert.DoesNotContain(bidi, projectJson, StringComparison.Ordinal);
            Assert.DoesNotContain(bidi, contentJson, StringComparison.Ordinal);

            using var packageDocument = JsonDocument.Parse(packageJson);
            using var projectDocument = JsonDocument.Parse(projectJson);
            using var contentDocument = JsonDocument.Parse(contentJson);
            Assert.Equal(
                placeholder,
                packageDocument.RootElement.GetProperty("content").GetString());
            Assert.Equal(
                placeholder,
                projectDocument.RootElement.GetProperty("content").GetString());
            Assert.Equal(
                placeholder,
                contentDocument.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(packageTempDir, recursive: true);
            Directory.Delete(projectTempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_SemanticTailSelectsSameRowAcrossOutputs()
    {
        var firstSkill =
            CompliantProjectSkill("skills/first/SKILL.md", "first");
        var secondSkill =
            CompliantProjectSkill("skills/second/SKILL.md", "second");
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "A.Project.First",
                "1.0.0",
                "README.md",
                "first readme",
                Skills: [firstSkill]),
            new ProjectDocPackage(
                "B.Project.Second",
                "1.0.0",
                "README.md",
                "second readme",
                Skills: [secondSkill]));

        try
        {
            var markdown = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--markdown");
            var table = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--table");
            var tsv = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--tsv");
            var jsonl = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--jsonl");
            var json = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--json");
            var count = await RunProjectFixtureAsync(
                projectPath,
                "-S",
                "-n", "1", "--tail",
                "--count");
            var paths = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--paths");
            var value = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--fields", "Name",
                "--value");
            var bare = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1", "--tail",
                "--raw", "--body");

            foreach (var result in new[]
                     {
                         markdown,
                         table,
                         tsv,
                         jsonl,
                         json,
                     })
            {
                Assert.Equal(0, result.Exit);
                Assert.Empty(result.Error);
                Assert.Contains("B.Project.Second", result.Output);
                Assert.DoesNotContain("A.Project.First", result.Output);
            }

            Assert.Equal((0, "1\n", ""), count);
            Assert.Equal((0, "skills/second/SKILL.md\n", ""), paths);
            Assert.Equal((0, "second\n", ""), value);
            Assert.Equal(0, bare.Exit);
            Assert.Empty(bare.Error);
            Assert.Equal("second", bare.Output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SingleSection_UnavailableSemanticWindowWithholdsOutput()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.One",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    CompliantProjectSkill(
                        "skills/only/SKILL.md",
                        "only"),
                ]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "--rows", "1..2",
                "--json");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Project document row selection stage 1 requires row 2, "
                + "but only 1 rows are available.",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SingleSection_SemanticHeadCannotHideInvalidLaterRow()
    {
        var invalidSkill = """
            ---
            name: invalid
            ---
            # Invalid skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "A.Project.Valid",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    CompliantProjectSkill(
                        "skills/valid/SKILL.md",
                        "valid"),
                ]),
            new ProjectDocPackage(
                "B.Project.Invalid",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    new ProjectSkillDoc(
                        "skills/invalid/SKILL.md",
                        invalidSkill),
                ]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "1",
                "--json");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "must declare an Agent Skills-compliant description",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SingleSection_ExplicitLinesClipsRenderedText()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "A.Project.First",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    CompliantProjectSkill(
                        "skills/first/SKILL.md",
                        "first"),
                ]),
            new ProjectDocPackage(
                "B.Project.Second",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    CompliantProjectSkill(
                        "skills/second/SKILL.md",
                        "second"),
                ]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "-n", "2", "--lines",
                "--table");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(
                2,
                output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Contains("A.Project.First", output);
            Assert.DoesNotContain("B.Project.Second", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--rows", "1", "--rows requires N..M")]
    [InlineData("-n", "1 --lines", "Rendered-line selection cannot be combined with JSON output")]
    public async Task Project_SingleSection_RejectsInvalidRowRequestBeforeProjectResolution(
        string option,
        string operand,
        string expectedError)
    {
        string[] arguments = operand.Split(' ');
        var (exit, output, error) = await RunAppAsync(
            [
                "project",
                "missing-project",
                "-S", "Skills",
                option,
                .. arguments,
                "--json",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expectedError, error);
        Assert.DoesNotContain("project.assets.json", error);
    }

    [Fact]
    public async Task Project_MultiSection_RetainsLegacyWindowValidation()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "A.Project.First",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    CompliantProjectSkill(
                        "skills/first/SKILL.md",
                        "first"),
                ]),
            new ProjectDocPackage(
                "B.Project.Second",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    CompliantProjectSkill(
                        "skills/second/SKILL.md",
                        "second"),
                ]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "@Project",
                "--rows", "1",
                "--json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("A.Project.First", output);
            Assert.Contains("\"package_readme_file\"", output);
            Assert.DoesNotContain("B.Project.Second", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_MultiSection_InferredLinesRejectJsonBeforeProjectResolution()
    {
        var (exit, output, error) = await RunAppAsync(
            "project",
            "missing-project",
            "-S", "@Project",
            "-n", "1",
            "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            error);
        Assert.DoesNotContain("project.assets.json", error);
    }

    [Fact]
    public async Task SkillDocuments_ReportBoundedContainmentRanges()
    {
        const string bidi = "\u202E";
        string content =
            "---\nname: ranges\n---\nprefix\n  "
            + bidi
            + bidi
            + "x\u001B"
            + "x"
            + string.Join("x", Enumerable.Repeat(bidi, 8));
        const string SkillPath = "skills/ranges/SKILL.md";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Skills.ContainmentRanges",
            "README.md",
            "readme",
            null,
            null,
            (SkillPath, content));

        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                packagePath,
                "--content",
                "--path",
                SkillPath,
                "--body",
                "--raw");

            Assert.Equal(0, exit);
            Assert.Equal(
                InertString.ContainmentRequiredPlaceholder.ToString() + "\n",
                output);
            Assert.Contains(
                $"Skill document '{SkillPath}' was omitted because 10 text ranges require containment.",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "line 5, columns 3-4: 2 x U+202E (Format)",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "line 5, column 6: U+001B (Control)",
                error,
                StringComparison.Ordinal);
            Assert.Equal(
                7,
                error.Split(
                    "U+202E (Format)",
                    StringSplitOptions.None).Length - 1);
            Assert.Contains(
                "2 additional ranges omitted.",
                error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(bidi, error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SkillDocuments_PreserveSafeOriginalText()
    {
        const string Body = """Use Regex("\\d+") with C:\temp and literal \u202E text.""";
        var (packagePath, packageTempDir) = CreateLocalReadmePackage(
            "Test.Skills.SafeOriginal",
            "README.md",
            "readme",
            null,
            null,
            ("skills/package-skill/SKILL.md", Body));
        var (projectPath, projectTempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Skills.SafeOriginal",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [CompliantProjectSkill("skills/project-skill/SKILL.md", Body)]));

        try
        {
            var packageOutputPath = Path.Combine(packageTempDir, "package-skill.md");
            var contentOutputPath = Path.Combine(packageTempDir, "content-skill.md");
            var projectOutputPath = Path.Combine(projectTempDir, "project-skill.md");

            var (packageExit, packageOutput, packageError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--raw");
            var (contentExit, contentOutput, contentError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content", "--raw");
            var (contentBlocksExit, contentBlocksOutput, contentBlocksError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content");
            var (projectExit, projectOutput, projectError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--raw");
            var (packageJsonExit, packageJson, packageJsonError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--jsonl");
            var (contentJsonExit, contentJson, contentJsonError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content", "--jsonl");
            var (projectJsonExit, projectJson, projectJsonError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--jsonl");
            var (packageFileExit, packageFileOutput, packageFileError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--raw",
                "--output", packageOutputPath);
            var (contentFileExit, contentFileOutput, contentFileError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content", "--raw", "--output", contentOutputPath);
            var (projectFileExit, projectFileOutput, projectFileError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--raw",
                "--output", projectOutputPath);

            Assert.Equal(0, packageExit);
            Assert.Equal(0, contentExit);
            Assert.Equal(0, contentBlocksExit);
            Assert.Equal(0, projectExit);
            Assert.Equal(0, packageJsonExit);
            Assert.Equal(0, contentJsonExit);
            Assert.Equal(0, projectJsonExit);
            Assert.Equal(0, packageFileExit);
            Assert.Equal(0, contentFileExit);
            Assert.Equal(0, projectFileExit);
            Assert.Empty(packageError);
            Assert.Empty(contentError);
            Assert.Empty(contentBlocksError);
            Assert.Empty(projectError);
            Assert.Empty(packageJsonError);
            Assert.Empty(contentJsonError);
            Assert.Empty(projectJsonError);
            Assert.Empty(packageFileOutput);
            Assert.Empty(contentFileOutput);
            Assert.Empty(projectFileOutput);
            Assert.Empty(packageFileError);
            Assert.Empty(contentFileError);
            Assert.Empty(projectFileError);
            Assert.Equal(Body, packageOutput);
            Assert.Equal(Body + "\n", contentOutput);
            Assert.Contains(Body + "\n", contentBlocksOutput, StringComparison.Ordinal);
            Assert.Equal(Body, projectOutput);

            using var packageDocument = JsonDocument.Parse(packageJson);
            using var contentDocument = JsonDocument.Parse(contentJson);
            using var projectDocument = JsonDocument.Parse(projectJson);
            Assert.Equal(Body, packageDocument.RootElement.GetProperty("content").GetString());
            Assert.Equal(Body, contentDocument.RootElement.GetProperty("content").GetString());
            Assert.Equal(Body, projectDocument.RootElement.GetProperty("content").GetString());
            Assert.Equal(Body, File.ReadAllText(packageOutputPath));
            Assert.Equal(Body, File.ReadAllText(contentOutputPath));
            Assert.Equal(Body, File.ReadAllText(projectOutputPath));
        }
        finally
        {
            Directory.Delete(packageTempDir, recursive: true);
            Directory.Delete(projectTempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SkillDocuments_ClassifyRawContentBeforeNormalizingGitHubLinks()
    {
        const string bidi = "\u202E";
        var body = $"[docs](https://github.com/owner/repo/blob/main/docs/{bidi}INJECTED.md)";
        var (packagePath, packageTempDir) = CreateLocalReadmePackage(
            "Test.Skills.RawClassification",
            "README.md",
            "readme",
            null,
            null,
            ("skills/package-skill/SKILL.md", body));
        var (projectPath, projectTempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Skills.RawClassification",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [CompliantProjectSkill("skills/project-skill/SKILL.md", body)]));

        try
        {
            var (packageExit, packageOutput, packageError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--raw");
            var (projectExit, projectOutput, projectError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--raw");

            Assert.Equal(0, packageExit);
            Assert.Equal(0, projectExit);
            AssertContainmentWarning(
                packageError,
                "skills/package-skill/SKILL.md");
            AssertContainmentWarning(
                projectError,
                "skills/project-skill/SKILL.md");
            string placeholder = InertString.ContainmentRequiredPlaceholder.ToString();
            Assert.Equal(placeholder, packageOutput);
            Assert.Equal(placeholder, projectOutput);
            Assert.DoesNotContain("%E2%80%AE", packageOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("%E2%80%AE", projectOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(packageTempDir, recursive: true);
            Directory.Delete(projectTempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("-o")]
    [InlineData("--output")]
    public async Task SkillDocuments_OutputAliasesWritePackageAndProjectPayloads(string outputOption)
    {
        const string bidi = "\u202E";
        var (packagePath, packageTempDir) = CreateLocalReadmePackage(
            "Test.Skills.Output",
            "README.md",
            "readme",
            null,
            null,
            ("skills/package-skill/SKILL.md", $"package{bidi}skill"));
        var (projectPath, projectTempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Skills.Output",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [CompliantProjectSkill("skills/project-skill/SKILL.md", $"project{bidi}skill")]));

        try
        {
            var packageOutput = Path.Combine(packageTempDir, "package-skill.md");
            var packageContentOutput = Path.Combine(packageTempDir, "package-content-skill.md");
            var projectOutput = Path.Combine(projectTempDir, "project-skill.md");

            var (packageExit, packageStdout, packageError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--raw",
                outputOption, packageOutput);
            var (packageContentExit, packageContentStdout, packageContentError) = await RunAppAsync(
                "package", packagePath, "--path", "skills/package-skill/SKILL.md",
                "--content", "--raw", outputOption, packageContentOutput);
            var (projectExit, projectStdout, projectError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--raw", outputOption, projectOutput);

            Assert.Equal(0, packageExit);
            Assert.Equal(0, packageContentExit);
            Assert.Equal(0, projectExit);
            Assert.Empty(packageStdout);
            Assert.Empty(packageContentStdout);
            Assert.Empty(projectStdout);
            AssertContainmentWarning(
                packageError,
                "skills/package-skill/SKILL.md");
            AssertContainmentWarning(
                packageContentError,
                "skills/package-skill/SKILL.md");
            AssertContainmentWarning(
                projectError,
                "skills/project-skill/SKILL.md");
            Assert.Equal(
                InertString.ContainmentRequiredPlaceholder.ToString(),
                File.ReadAllText(packageOutput));
            Assert.Equal(
                InertString.ContainmentRequiredPlaceholder.ToString(),
                File.ReadAllText(packageContentOutput));
            Assert.Equal(
                InertString.ContainmentRequiredPlaceholder.ToString(),
                File.ReadAllText(projectOutput));
        }
        finally
        {
            Directory.Delete(packageTempDir, recursive: true);
            Directory.Delete(projectTempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_EmitsSkillRows()
    {
        var querySkill = """
            ---
            name: query
            description: Find APIs from restored dependencies.
            ---
            # Query skill
            """;
        var sourceSkill = """
            ---
            name: source
            description: Inspect SourceLink-backed files.
            ---
            # Source skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Skills.Query", "1.2.3", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/query/SKILL.md", querySkill)]),
            new ProjectDocPackage("Test.Project.Skills.Source", "4.5.6", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/source/SKILL.md", sourceSkill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);

            using var queryDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.Skills.Query")));
            Assert.Equal("Test.Project.Skills.Query", queryDocument.RootElement.GetProperty("package").GetString());
            Assert.Equal("skills/query/SKILL.md", queryDocument.RootElement.GetProperty("path").GetString());
            Assert.Equal("query", queryDocument.RootElement.GetProperty("name").GetString());
            Assert.Equal("Find APIs from restored dependencies.", queryDocument.RootElement.GetProperty("description").GetString());

            using var sourceDocument = JsonDocument.Parse(lines.Single(line => line.Contains("Test.Project.Skills.Source")));
            Assert.Equal("skills/source/SKILL.md", sourceDocument.RootElement.GetProperty("path").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("../../owned", "valid")]
    [InlineData("Uppercase", "uppercase")]
    [InlineData("-leading", "leading")]
    [InlineData("trailing-", "trailing")]
    [InlineData("two--hyphens", "two-hyphens")]
    [InlineData("with/slash", "with-slash")]
    [InlineData("with\\backslash", "with-backslash")]
    [InlineData("\"valid # not-a-comment\"", "valid")]
    [InlineData("different-name", "directory-name")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklm", "long-name")]
    public async Task Project_SkillsSection_RejectsNoncompliantSkillNames(
        string name, string directoryName)
    {
        var skill = $$"""
            ---
            name: {{name}}
            description: Package guidance.
            ---
            # Package skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Skills.Invalid", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc($"skills/{directoryName}/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "must declare an Agent Skills-compliant name that matches its containing directory",
                error);
            Assert.DoesNotContain(name, error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("inline-comment # a valid YAML comment")]
    [InlineData("\"inline-comment\" # a valid YAML comment")]
    [InlineData("'inline-comment' # a valid YAML comment")]
    public async Task Project_SkillsSection_AcceptsYamlInlineComments(string nameDeclaration)
    {
        var skill = $$"""
            ---
            name: {{nameDeclaration}}
            description: Package guidance. # a valid YAML comment
            ---
            # Package skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Skills.InlineComment", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/inline-comment/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                "inline-comment",
                document.RootElement.GetProperty("name").GetString());
            Assert.Equal(
                "Package guidance.",
                document.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Before\u202EINJECTED")]
    [InlineData("Before\fINJECTED")]
    [InlineData("Before\u0085INJECTED")]
    [InlineData("Before\u2028INJECTED")]
    [InlineData("Before\u2029INJECTED")]
    [InlineData("\fINJECTED")]
    [InlineData("\u0085INJECTED")]
    [InlineData("\u2028INJECTED")]
    [InlineData("\u2029INJECTED")]
    [InlineData("INJECTED\f")]
    [InlineData("INJECTED\u0085")]
    [InlineData("INJECTED\u2028")]
    [InlineData("INJECTED\u2029")]
    public async Task Project_SkillsInventory_ReplacesContainedYamlFields(string description)
    {
        var skill = $"""
            ---
            name: contained-description
            description: {description}
            ---
            # Package skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Skills.ContainedDescription",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [new ProjectSkillDoc("skills/contained-description/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");
            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Empty(error);
            Assert.DoesNotContain("INJECTED", output, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                InertString.ContainmentRequiredPlaceholder.ToString(),
                document.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("\f")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public async Task Project_SkillsInventory_PreservesBlockIndicatorConcerns(string concern)
    {
        var skill = $"""
            ---
            name: contained-indicator
            description: |{concern}
              INJECTED
            ---
            # Package skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Skills.ContainedIndicator",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [new ProjectSkillDoc("skills/contained-indicator/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.DoesNotContain("INJECTED", output, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                InertString.ContainmentRequiredPlaceholder.ToString(),
                document.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsInventory_FoldsLiteralBlockDescription()
    {
        var skill = """
            ---
            name: literal-description
            description: |
              Renders objects as Markdown.
              Use when a CLI needs agent-readable output.
            ---
            # Package skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Skills.LiteralDescription",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [new ProjectSkillDoc("skills/literal-description/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                "Renders objects as Markdown. Use when a CLI needs agent-readable output.",
                document.RootElement.GetProperty("description").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_AcceptsDirectoryNameAndExtensionFrontmatter()
    {
        var skill = """
            ---
            name: markout-output-formats
            description: Control output formats.
            version: 0.35.2
            ---
            # Markout output formats
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Markout", "0.35.2", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/markout-output-formats/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                "markout-output-formats",
                document.RootElement.GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_RejectsMissingName()
    {
        var skill = """
            ---
            description: Package guidance.
            ---
            # Package skill
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Skills.MissingName", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/missing-name/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "must declare an Agent Skills-compliant name that matches its containing directory",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public async Task Project_SkillsSection_ValidatesDescriptionLength(
        int descriptionLength,
        bool expectedSuccess)
    {
        var descriptionLine = descriptionLength < 0
            ? ""
            : $"description: {new string('d', descriptionLength)}\n";
        var skill = $"---\nname: description-boundary\n{descriptionLine}---\n# Package skill";
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Skills.Description", "1.0.0", "README.md", "readme", Skills:
                [new ProjectSkillDoc("skills/description-boundary/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(expectedSuccess ? 0 : 1, exit);
            if (expectedSuccess)
            {
                Assert.Empty(error);
                using var document = JsonDocument.Parse(output);
                Assert.Equal(
                    descriptionLength,
                    document.RootElement.GetProperty("description").GetString()!.Length);
            }
            else
            {
                Assert.Empty(output);
                Assert.Contains(
                    "must declare an Agent Skills-compliant description of 1 to 1024 characters",
                    error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_FailsWhenRestoredSkillFileIsMissing()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Skills.MissingFile", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/missing-file/SKILL.md", "# Package skill")]));
        var skillPath = Path.Combine(
            tempDir,
            "packages",
            "test.project.skills.missingfile",
            "1.0.0",
            "skills",
            "missing-file",
            "SKILL.md");
        File.Delete(skillPath);

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--jsonl");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "listed in project.assets.json is missing from the package cache",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPaths_EmitsSkillPaths()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Paths.One", "1.0.0", "README.md", "one", Skills:
                [CompliantProjectSkill("skills/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.Paths.Two", "1.0.0", "README.md", "two", Skills:
                [CompliantProjectSkill("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--paths");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(["skills/SKILL.md", "skills/two/SKILL.md"], output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectProjectionDestinations_MatchStdoutForShapeAndPrintConsumers()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Projection.Destination",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [
                    CompliantProjectSkill(
                        "skills/project-skill/SKILL.md",
                        "project skill"),
                    CompliantProjectSkill(
                        "skills/second-project-skill/SKILL.md",
                        "second project skill")
                ]));
        try
        {
            (string Name, string[] Arguments)[] cases =
            [
                ("paths", ["-S", "Skills", "--paths"]),
                ("paths-lines", ["-S", "Skills", "--paths", "--lines", "-n", "1"]),
                ("print", ["-S", "Skills", "--print", "--row", "1", "--body"]),
            ];

            foreach (var testCase in cases)
            {
                var outputPath = Path.Combine(tempDir, $"project-{testCase.Name}.txt");
                var baseline = await RunProjectFixtureAsync(
                    projectPath,
                    [.. testCase.Arguments, "--tips", "q"]);
                var redirected = await RunProjectFixtureAsync(
                    projectPath,
                    [.. testCase.Arguments, "--out", outputPath, "--tips", "q"]);

                Assert.Equal(0, baseline.Exit);
                Assert.Equal(baseline.Exit, redirected.Exit);
                Assert.Empty(baseline.Error);
                Assert.Empty(redirected.Error);
                Assert.Empty(redirected.Output);
                Assert.Equal(baseline.Output, File.ReadAllText(outputPath));
                if (testCase.Name == "paths-lines")
                {
                    Assert.Single(
                        baseline.Output.Split(
                            '\n',
                            StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectJsonArrayRejectsRenderedLineSelectionBeforeAcquisition()
    {
        string missingProject = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("n"),
            "missing.csproj");

        var (exit, output, error) = await RunAppAsync(
            "project",
            missingProject,
            "-S",
            "Skills",
            "--paths",
            "--json-array",
            "-n",
            "1",
            "--lines");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            error);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Project_SkillsValue_UsesSelectedField()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Value.One", "1.2.3", "README.md", "one", Skills:
                [CompliantProjectSkill("skills/value/SKILL.md", "one")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--fields", "Version", "--value");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("1.2.3", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_PrintsFirstSkillDocument()
    {
        var skill = """
            ---
            name: selected
            description: Test package skill guidance.
            ---
            # Skill guidance
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print", "2.0.0", "README.md", "# README body", Skills:
                [new ProjectSkillDoc("skills/selected/SKILL.md", skill)]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("# Skill guidance", output);
            Assert.DoesNotContain("name: selected", output);
            Assert.DoesNotContain("# README body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_SkipsDependenciesWithoutSkills()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"),
            new ProjectDocPackage("B.Project.HasSkills", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/selected/SKILL.md", "selected")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Equal("selected", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsSection_EmptyRendersNoSkillsNote()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("No skills found", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_JsonlIsSingleCompactRecord()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.Jsonl", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/jsonl/SKILL.md", "selected")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            using var document = JsonDocument.Parse(lines[0]);
            Assert.Equal("selected", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_RequiresRowWhenMultipleSkillDocuments()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.One", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/one/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.Print.Two", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("selected section has 2 printable rows; use --row N|first|last to choose one row", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_RowSelectionPrintsSelectedDocument()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.One", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/one/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.Print.Two", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--row", "2");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("two", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrint_RowSelectionCountsPrintableRowsOnly()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"),
            new ProjectDocPackage("B.Project.FirstPrintable", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/first/SKILL.md", "first")]),
            new ProjectDocPackage("C.Project.SecondPrintable", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/second/SKILL.md", "second")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print", "--body", "--row", "1");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("first", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsPrintAll_IsNoLongerRecognized()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.PrintAll.One", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/one/SKILL.md", "one")]),
            new ProjectDocPackage("Test.Project.PrintAll.Two", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/two/SKILL.md", "two")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--print-all");

            Assert.NotEqual(0, exit);
            Assert.DoesNotContain("--- Test.Project.PrintAll.One", output);
            Assert.Contains("--print-all", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsBare_PrintsSingleSkillDocument()
    {
        var skill = CompliantProjectSkill("skills/bare/SKILL.md", "selected");
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Bare", "1.0.0", "README.md", "readme", Skills:
                [skill]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--raw");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Equal(skill.Text, output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsBare_MultipleDocuments_RequiresRow()
    {
        var firstSkill = CompliantProjectSkill("skills/first/SKILL.md", "first");
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.NoSkills", "1.0.0", "README.md", "readme"),
            new ProjectDocPackage("B.Project.FirstPrintable", "1.0.0", "README.md", "readme", Skills:
                [firstSkill]),
            new ProjectDocPackage("C.Project.SecondPrintable", "1.0.0", "README.md", "readme", Skills:
                [CompliantProjectSkill("skills/second/SKILL.md", "second")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--raw");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "selected section has 2 printable rows; use --row "
                + "N|first|last to choose one row",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsBare_AppliesRowsBeforeDefaultSelection()
    {
        var firstSkill = CompliantProjectSkill("skills/first/SKILL.md", "first");
        var secondSkill = CompliantProjectSkill("skills/second/SKILL.md", "second");
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("A.Project.First", "1.0.0", "README.md", "readme", Skills:
                [firstSkill]),
            new ProjectDocPackage("B.Project.Second", "1.0.0", "README.md", "readme", Skills:
                [secondSkill]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Skills",
                "--raw", "--rows", "2..2");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(secondSkill.Text, output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_PrintRequiresSingleSelectedSection()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Print.Requires.Select", "1.0.0", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("--print requires -S/--select to match exactly one printable section", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_SkillsCount_ObservesSemanticHead()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Count.One", "1.0.0", "README.md", "readme", Skills:
                [
                    CompliantProjectSkill("skills/one/SKILL.md", "one"),
                    CompliantProjectSkill("skills/two/SKILL.md", "two")
                ]),
            new ProjectDocPackage("Test.Project.Count.None", "1.0.0", "README.md", "readme"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--count");
            var (windowedExit, windowedOutput, windowedError) = await RunProjectFixtureAsync(
                projectPath, "-S", "Skills", "--count", "-n", "1");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Equal("2\n", output);
            Assert.Equal(0, windowedExit);
            Assert.Empty(windowedError);
            Assert.Equal("1\n", windowedOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Discover_ListsSupportedDocumentSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "project", "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(
            """
            @Project             category
            Package README file  section
            Skills               section

            """.ReplaceLineEndings(),
            output);
        Assert.DoesNotContain("@All", output);
        Assert.DoesNotContain("@Default", output);
        Assert.DoesNotContain("@Hidden", output);
    }

    [Fact]
    public async Task Project_Discover_ProjectCategoryListsOwnedSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "project", "-D", "@Project", "--schema", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(
            """
            Package README file  section
            Skills               section

            """.ReplaceLineEndings(),
            output);
    }

    [Theory]
    [InlineData("@All")]
    [InlineData("@Default")]
    [InlineData("@Hidden")]
    public async Task Project_ComputedCategoryPolesAreRejected(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "project", "missing-project", "-S", selector, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"Select value '{selector}' not found", error);
        Assert.DoesNotContain("project.assets.json", error);
    }

    [Fact]
    public async Task Project_Discover_ExplicitTableDoesNotPromoteToTree()
    {
        var (exit, output, error) = await RunAppAsync(
            "project",
            "-D", "Skills,Package README file",
            "--table");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Package      column", output);
        Assert.Contains("Description  column", output);
        Assert.DoesNotContain("├─", output);
        Assert.DoesNotContain("└─", output);
    }

    [Fact]
    public async Task Project_Discover_TsvNoHeaderOmitsHeader()
    {
        var (exit, output, error) = await RunAppAsync(
            "project",
            "-D", "Skills",
            "--tsv",
            "--no-header",
            "--rows", "1");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("Package\tcolumn\n", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Project_Discover_JsonOutWritesOnlyToFile()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("project-discovery-out-");
        try
        {
            string path = Path.Combine(tempDirectory.FullName, "discovery.json");

            var (exit, output, error) = await RunAppAsync(
                "project",
                "-D", "Skills",
                "--json",
                "--out", path);

            Assert.Equal(0, exit);
            Assert.Empty(output);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var first = document.RootElement.EnumerateArray().First();
            Assert.Equal("Package", first.GetProperty("name").GetString());
            Assert.Equal("column", first.GetProperty("kind").GetString());
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Project_BareSelect_UsesSkillsOverview()
    {
        var skill = CompliantProjectSkill("skills/default/SKILL.md", "default");
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.DefaultSelect",
                "1.0.0",
                "README.md",
                "readme",
                Skills: [skill]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("## Skills", output);
            Assert.Contains("Test.Project.DefaultSelect", output);
            Assert.DoesNotContain("## Package README file", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_PrintsReadmeWithoutUsingAgentsOrProjectMd()
    {
        var agents = """
            ---
            name: selected
            ---
            # Agent guidance
            """;
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Readme", "2.0.0", "README.md", "# README body", agents, ProjectText: "# PROJECT body"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--print");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("# README body", output);
            Assert.DoesNotContain("# Agent guidance", output);
            Assert.DoesNotContain("# PROJECT body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_ListsOnlyRootReadmes()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "A.Project.RootReadme",
                "1.0.0",
                "README.md",
                "root"),
            new ProjectDocPackage(
                "B.Project.NestedReadme",
                "1.0.0",
                "docs/README.md",
                "nested"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--jsonl");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(
                Assert.Single(
                    output.Split(
                        '\n',
                        StringSplitOptions.RemoveEmptyEntries)));
            Assert.Equal(
                "A.Project.RootReadme",
                document.RootElement.GetProperty("package").GetString());
            Assert.Equal(
                "README.md",
                document.RootElement.GetProperty("path").GetString());
            Assert.DoesNotContain("B.Project.NestedReadme", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_FailsWhenListedFileIsMissing()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Readme.Missing",
                "1.0.0",
                "README.md",
                "readme"));
        File.Delete(Path.Combine(
            tempDir,
            "packages",
            "test.project.readme.missing",
            "1.0.0",
            "README.md"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "README listed in project.assets.json is missing from the "
                + "package cache",
                error);
            Assert.DoesNotContain("Test.Project.Readme.Missing", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_SemanticWindowReindexesPrintRows()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "A.Project.FirstReadme",
                "1.0.0",
                "README.md",
                "first"),
            new ProjectDocPackage(
                "B.Project.SecondReadme",
                "1.0.0",
                "README.md",
                "second"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--print",
                "--rows", "2..2",
                "--row", "1");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("second", output.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmePaths_SemanticWindowReindexesBeforeRowSelection()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "A.Project.FirstReadme",
                "1.0.0",
                "README.md",
                "first"),
            new ProjectDocPackage(
                "B.Project.SecondReadme",
                "1.0.0",
                "README.md",
                "second"));

        try
        {
            var selected = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--paths",
                "--rows", "2..2",
                "--row", "1",
                "--json");
            var excluded = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--paths",
                "--rows", "2..2",
                "--row", "2",
                "--json");

            Assert.Equal(0, selected.Exit);
            Assert.Empty(selected.Error);
            using (JsonDocument json = JsonDocument.Parse(selected.Output))
            {
                Assert.Equal(
                    1,
                    json.RootElement.GetProperty("row").GetInt32());
                Assert.Equal(
                    "B.Project.SecondReadme",
                    json.RootElement.GetProperty("label").GetString());
            }

            Assert.Equal(1, excluded.Exit);
            Assert.Empty(excluded.Output);
            Assert.Contains("row 2 is not in this section", excluded.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_UsesCountPathAndValueControls()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.Readme.Controls",
                "2.3.4",
                "README.md",
                "readme"));

        try
        {
            var count = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--count");
            var paths = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--paths");
            var value = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--fields", "Version",
                "--value");

            Assert.Equal(0, count.Exit);
            Assert.Equal("1", count.Output.Trim());
            Assert.Empty(count.Error);
            Assert.Equal(0, paths.Exit);
            Assert.Equal("README.md", paths.Output.Trim());
            Assert.Empty(paths.Error);
            Assert.Equal(0, value.Exit);
            Assert.Equal("2.3.4", value.Output.Trim());
            Assert.Empty(value.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_MultiSectionJson_UsesOneSectionDocument()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                "Test.Project.MultiSection",
                "1.0.0",
                "README.md",
                "readme",
                Skills:
                [CompliantProjectSkill(
                    "skills/multi-section/SKILL.md",
                    "skill")]));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "@Project",
                "--json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                ["package_readme_file", "skills"],
                document.RootElement
                    .EnumerateObject()
                    .Select(property => property.Name));
            Assert.Single(
                document.RootElement.GetProperty("skills").EnumerateArray());
            Assert.Single(
                document.RootElement
                    .GetProperty("package_readme_file")
                    .EnumerateArray());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_Help_ListsOnlySectionDrivenDocumentControls()
    {
        var (exit, output, error) = await RunAppAsync(
            "project",
            "--help");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("--agents-index", output);
        Assert.DoesNotContain("--readme", output);
        Assert.DoesNotContain("--source", output);
        Assert.Contains("--select", output);
        Assert.Contains("--print", output);
    }

    [Fact]
    public async Task Project_ReadmeSection_JsonlPrintUsesLfFraming()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.Readme.Jsonl", "2.0.0", "README.md", "# README body"));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--print",
                "--jsonl");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.DoesNotContain('\r', output);
            Assert.EndsWith("\n", output, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("# README body", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_DoesNotFallBackToProjectMd()
    {
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.ProjectMd", "2.0.0", "README.md", "", ProjectText: "# PROJECT body", OmitReadme: true));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("No root README.md files found", output);
            Assert.DoesNotContain("# PROJECT body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_NormalizesGithubBlobLinksToRaw()
    {
        const string readme = "See https://github.com/owner/repo/blob/main/docs/guide.md";
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage("Test.Project.RawLinks", "1.0.0", "README.md", readme));

        try
        {
            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--print");

            Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/docs/guide.md", output);
            Assert.DoesNotContain("github.com/owner/repo/blob", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Project_ReadmeSection_FullOutputPreservesExactBytes()
    {
        const string packageId = "Test.Project.ExactReadme";
        const string version = "1.0.0";
        var (projectPath, tempDir) = CreateProjectWithPackageDocs(
            new ProjectDocPackage(
                packageId,
                version,
                "README.md",
                "placeholder"));

        try
        {
            byte[] content =
            [
                .. Encoding.UTF8.Preamble,
                .. Encoding.UTF8.GetBytes(
                    "See https://github.com/owner/repo/blob/main/docs/guide.md\r\n"),
            ];
            string readmePath = Path.Combine(
                tempDir,
                "packages",
                packageId.ToLowerInvariant(),
                version,
                "README.md");
            File.WriteAllBytes(readmePath, content);
            string outputPath = Path.Combine(tempDir, "README.out");

            var (exit, output, error) = await RunProjectFixtureAsync(
                projectPath,
                "-S", "Package README file",
                "--print",
                "--out", outputPath);

            Assert.Equal(0, exit);
            Assert.Empty(output);
            Assert.Empty(error);
            Assert.Equal(content, File.ReadAllBytes(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
