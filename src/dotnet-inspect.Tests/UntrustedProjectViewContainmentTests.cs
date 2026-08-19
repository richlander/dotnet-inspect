using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// Gate for package-authored text read by the <c>project</c> command (issue
/// #3319). <c>AGENTS.md</c> frontmatter lands in Markdown table cells, while a
/// noncompliant <c>skills/SKILL.md</c> identity must fail before rendering.
/// </summary>
/// <remarks>
/// The escaper on that path replaced the pipe and folded CR/LF, which keeps a
/// cell inside its row but does nothing about a vertical tab, an ANSI escape, or
/// a bidi override. Containment now lives on the row records so all AGENTS.md
/// writers inherit it. The Skills gate separately proves that a noncompliant
/// name is rejected without echoing package-authored identity text.
///
/// The fixture is a hand-built package folder plus a <c>project.assets.json</c>
/// whose library <c>path</c> is relative to a test-owned
/// <c>NUGET_PACKAGES</c> root. That avoids a restore, network, feed, and writes
/// to the developer's real package cache while exercising the product's
/// manifest-path containment.
/// </remarks>
[Collection("Console")]
public class UntrustedProjectViewContainmentTests : IDisposable
{
    private const string Bidi = "\u202E";
    private const string Vtab = "\u000B";

    /// <summary>
    /// The package identity as the assets file *reports* it, which is what the
    /// rows display. It is deliberately not the on-disk folder name: a package
    /// author controls both, but only a bidi override in a directory name would
    /// be unportable, and the display value is the channel under test.
    /// </summary>
    private const string HostileId = "Hostile" + Bidi + "INJECTEDPKGID.Skill";
    private const string HostileVersion = "1.0.0-" + Bidi + "INJECTEDVERSION";

    /// <summary>
    /// Matched by the <c>skills/**/SKILL.md</c> discovery glob, so the hostile
    /// text lands in the row's Path without needing a hostile file name.
    /// </summary>
    private const string HostileSkillDir = "sub" + Bidi + "INJECTEDSKILLPATH";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"HostileProj_{Guid.NewGuid():N}");
    private readonly string _assets;
    private readonly string? _originalNuGetPackages;

    public UntrustedProjectViewContainmentTests()
    {
        var packages = Path.Combine(_dir, "packages");
        var package = Path.Combine(packages, "hostile.skill", "1.0.0");
        Directory.CreateDirectory(Path.Combine(package, "skills", HostileSkillDir));
        Directory.CreateDirectory(Path.Combine(package, "lib", "net10.0"));

        File.WriteAllText(
            Path.Combine(package, "AGENTS.md"),
            $"---\nname: Agents{Bidi}INJECTEDAGENTNAME\ndescription: AgentDesc{Vtab}INJECTEDAGENTDESC\n---\nbody\n");
        File.WriteAllText(
            Path.Combine(package, "skills", HostileSkillDir, "SKILL.md"),
            $"---\nname: Skill{Bidi}INJECTEDSKILLNAME\ndescription: Desc{Vtab}INJECTEDSKILLDESC\n---\nbody\n");
        File.WriteAllText(Path.Combine(package, "hostile.skill.nuspec"), """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>Hostile.Skill</id><version>1.0.0</version>
              <authors>a</authors><description>d</description></metadata>
            </package>
            """);
        File.WriteAllText(Path.Combine(package, "lib", "net10.0", "Hostile.Skill.dll"), "MZ");

        var obj = Path.Combine(_dir, "app", "obj");
        Directory.CreateDirectory(obj);
        _assets = Path.Combine(obj, "project.assets.json");
        File.WriteAllText(_assets, BuildAssets(packages));
        _originalNuGetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable(
            "NUGET_PACKAGES",
            packages);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            "NUGET_PACKAGES",
            _originalNuGetPackages);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static string BuildAssets(string packagesRoot)
    {
        var folder = JsonSerializer.Serialize(packagesRoot);
        var library = $"{HostileId}/{HostileVersion}";
        var skillPath = $"skills/{HostileSkillDir}/SKILL.md";
        return $$"""
            {
              "version": 3,
              "targets": {
                "net10.0": {
                  "{{library}}": {
                    "type": "package",
                    "compile": { "lib/net10.0/Hostile.Skill.dll": {} },
                    "runtime": { "lib/net10.0/Hostile.Skill.dll": {} }
                  }
                }
              },
              "libraries": {
                "{{library}}": {
                  "sha512": "",
                  "type": "package",
                  "path": "hostile.skill/1.0.0",
                  "files": [
                    "AGENTS.md",
                    "hostile.skill.nuspec",
                    "lib/net10.0/Hostile.Skill.dll",
                    "{{skillPath}}"
                  ]
                }
              },
              "projectFileDependencyGroups": { "net10.0": [ "{{HostileId}} >= 1.0.0" ] },
              "packageFolders": { {{folder}}: {} },
              "project": {
                "version": "1.0.0",
                "restore": { "projectName": "app", "projectStyle": "PackageReference" },
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0",
                    "dependencies": {
                      "{{HostileId}}": { "target": "Package", "version": "[1.0.0, )" }
                    }
                  }
                }
              }
            }
            """;
    }

    [Fact]
    public async Task ProjectAgentsFrontmatter_WithHostileText_RendersNoHazard()
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ProjectCommand.ExecuteAsync(new ProjectOptions
            {
                ProjectPath = _assets,
                AgentsIndex = true,
            }));

        Assert.Equal(0, exit);

        // Per-marker non-vacuity: the name and the description reach the table
        // through separate frontmatter keys, and the package id, version, and
        // path arrive from the assets file rather than the frontmatter, so one
        // rendering vouches for none of the others. An earlier version of this
        // gate used benign values for the latter three and passed under tamper.
        foreach (var marker in new[] { "INJECTEDAGENTNAME", "INJECTEDAGENTDESC", "INJECTEDPKGID", "INJECTEDVERSION" })
        {
            Assert.True(
                output.Contains(marker, StringComparison.Ordinal),
                $"'{marker}' never rendered, so this gate proves nothing about its channel");
        }

        HostileOutputAssert.NoRenderingHazard(output, "UntrustedProjectViewContainmentTests");
    }

    [Fact]
    public async Task ProjectSkillFrontmatter_WithNoncompliantName_IsRejectedWithoutRenderingIt()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ProjectCommand.ExecuteAsync(new ProjectOptions
            {
                ProjectPath = _assets,
                Select = ["Skills"],
            }));

        Assert.Equal(1, exit);
        Assert.DoesNotContain("INJECTED", output);
        Assert.Contains(
            "must declare an Agent Skills-compliant name that matches its containing directory",
            error);
        Assert.DoesNotContain("INJECTED", error);
        HostileOutputAssert.NoRenderingHazard(output, "UntrustedProjectViewContainmentTests");
        HostileOutputAssert.NoRenderingHazard(error, "UntrustedProjectViewContainmentTests");
    }
}
