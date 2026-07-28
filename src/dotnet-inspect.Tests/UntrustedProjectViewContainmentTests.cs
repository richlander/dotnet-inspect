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
/// The fixture is a hand-built packages folder plus a <c>project.assets.json</c>
/// whose <c>packageFolders</c> points at it. That avoids a restore, so the gate
/// needs no network and no feed.
/// </remarks>
[Collection("Console")]
public class UntrustedProjectViewContainmentTests : IDisposable
{
    private const string Bidi = "\u202E";
    private const string Vtab = "\u000B";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"HostileProj_{Guid.NewGuid():N}");
    private readonly string _assets;

    public UntrustedProjectViewContainmentTests()
    {
        var packages = Path.Combine(_dir, "packages");
        var package = Path.Combine(packages, "hostile.skill", "1.0.0");
        Directory.CreateDirectory(Path.Combine(package, "skills"));
        Directory.CreateDirectory(Path.Combine(package, "lib", "net10.0"));

        File.WriteAllText(
            Path.Combine(package, "AGENTS.md"),
            $"---\nname: Agents{Bidi}INJECTEDAGENTNAME\ndescription: AgentDesc{Vtab}INJECTEDAGENTDESC\n---\nbody\n");
        File.WriteAllText(
            Path.Combine(package, "skills", "SKILL.md"),
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
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static string BuildAssets(string packagesFolder)
    {
        var folder = JsonSerializer.Serialize(packagesFolder + Path.DirectorySeparatorChar);
        return $$"""
            {
              "version": 3,
              "targets": {
                "net10.0": {
                  "Hostile.Skill/1.0.0": {
                    "type": "package",
                    "compile": { "lib/net10.0/Hostile.Skill.dll": {} },
                    "runtime": { "lib/net10.0/Hostile.Skill.dll": {} }
                  }
                }
              },
              "libraries": {
                "Hostile.Skill/1.0.0": {
                  "sha512": "",
                  "type": "package",
                  "path": "hostile.skill/1.0.0",
                  "files": [
                    "AGENTS.md",
                    "hostile.skill.nuspec",
                    "lib/net10.0/Hostile.Skill.dll",
                    "skills/SKILL.md"
                  ]
                }
              },
              "projectFileDependencyGroups": { "net10.0": [ "Hostile.Skill >= 1.0.0" ] },
              "packageFolders": { {{folder}}: {} },
              "project": {
                "version": "1.0.0",
                "restore": { "projectName": "app", "projectStyle": "PackageReference" },
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0",
                    "dependencies": {
                      "Hostile.Skill": { "target": "Package", "version": "[1.0.0, )" }
                    }
                  }
                }
              }
            }
            """;
    }

    [Theory]
    [InlineData(true, "INJECTEDAGENTNAME", "INJECTEDAGENTDESC")]
    [InlineData(false, "INJECTEDSKILLNAME", "INJECTEDSKILLDESC")]
    public async Task ProjectFrontmatter_WithHostileText_RendersNoHazard(
        bool agentsIndex, string nameMarker, string descriptionMarker)
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
        // through separate frontmatter keys, so one rendering does not vouch
        // for the other.
        foreach (var marker in new[] { nameMarker, descriptionMarker })
        {
            Assert.True(
                output.Contains(marker, StringComparison.Ordinal),
                $"'{marker}' never rendered, so this gate proves nothing about its channel");
        }

        for (int i = 0; i < output.Length; i++)
        {
            char c = output[i];
            if (c is not '\t' and not '\n' and not '\r'
                && (char.IsControl(c)
                    || c is '\u061C' or '\u200E' or '\u200F' or '\u2028' or '\u2029'
                        or >= '\u202A' and <= '\u202E'
                        or >= '\u2066' and <= '\u2069'))
            {
                Assert.Fail($"rendered project output carries U+{(int)c:X4} at index {i}");
            }
        }
    }
}
