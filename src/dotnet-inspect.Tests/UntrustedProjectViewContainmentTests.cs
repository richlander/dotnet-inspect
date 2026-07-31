using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// Gate for the <c>project</c> channel (issue #3319). The YAML frontmatter this
/// command reads out of a dependency's <c>AGENTS.md</c> and <c>skills/SKILL.md</c>
/// is text a package author wrote, and it lands in a Markdown table cell.
/// </summary>
/// <remarks>
/// The escaper on that path replaced the pipe and folded CR/LF, which keeps a
/// cell inside its row but does nothing about a vertical tab, an ANSI escape, or
/// a bidi override. A second writer on the same rows escaped nothing at all, so
/// containment now lives on the row records and both writers inherit it.
///
/// The fixture is a hand-built package folder plus a <c>project.assets.json</c>
/// whose library <c>path</c> is <em>rooted</em> at it. That avoids a restore, so
/// the gate needs no network and no feed — and, more importantly, no real
/// package cache: a relative library path is resolved against
/// <c>NuGetCache.GetNuGetCachePath()</c>, so an earlier version of this fixture
/// silently read whatever happened to sit in the developer's
/// <c>~/.nuget/packages</c> and would have found nothing on a clean machine.
/// <c>packageFolders</c> is not consulted by the parser at all.
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
        File.WriteAllText(_assets, BuildAssets(package));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static string BuildAssets(string packageRoot)
    {
        var folder = JsonSerializer.Serialize(packageRoot);
        var rootedPath = JsonSerializer.Serialize(packageRoot.Replace(Path.DirectorySeparatorChar, '/'));
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
                  "path": {{rootedPath}},
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

    [Theory]
    [InlineData(true, new[] { "INJECTEDAGENTNAME", "INJECTEDAGENTDESC", "INJECTEDPKGID", "INJECTEDVERSION" })]
    [InlineData(false, new[]
    {
        "INJECTEDSKILLNAME", "INJECTEDSKILLDESC", "INJECTEDPKGID", "INJECTEDVERSION", "INJECTEDSKILLPATH",
    })]
    public async Task ProjectFrontmatter_WithHostileText_RendersNoHazard(
        bool agentsIndex, string[] markers)
    {
        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ProjectCommand.ExecuteAsync(new ProjectOptions
            {
                ProjectPath = _assets,
                AgentsIndex = agentsIndex,
                Select = agentsIndex ? null : ["@All"],
            }));

        Assert.Equal(0, exit);

        // Per-marker non-vacuity: the name and the description reach the table
        // through separate frontmatter keys, and the package id, version, and
        // path arrive from the assets file rather than the frontmatter, so one
        // rendering vouches for none of the others. An earlier version of this
        // gate used benign values for the latter three and passed under tamper.
        foreach (var marker in markers)
        {
            Assert.True(
                output.Contains(marker, StringComparison.Ordinal),
                $"'{marker}' never rendered, so this gate proves nothing about its channel");
        }

        HostileOutputAssert.NoRenderingHazard(output, "UntrustedProjectViewContainmentTests");
    }
}
