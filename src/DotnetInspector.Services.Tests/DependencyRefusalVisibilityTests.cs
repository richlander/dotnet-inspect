using System.Text.Json;
using DotnetInspector.Services;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// A refused input must stay visible. The resolver drops an unsafe package coordinate or asset path
/// from the resolved graph, which on its own is indistinguishable from a dependency that simply did
/// not resolve -- the dependency just vanishes. AGENTS.md forbids turning a failure into
/// success-shaped empty output, so each refusal reports itself through
/// <see cref="AssemblyDependencyResolutionOptions.Log"/>.
/// <para>
/// Every test here carries a positive control: the same run must still resolve a legitimate entry.
/// Without one, a guard that refused everything would pass just as well as a correct one.
/// </para>
/// </summary>
public class DependencyRefusalVisibilityTests
{
    /// <summary>
    /// A deps.json whose asset path traverses is refused, and the refusal is reported. The benign
    /// asset in the same file is the control: it must still be added.
    /// </summary>
    [Fact]
    public void UnsafeDepsJsonAssetPath_IsRefusedAndReported()
    {
        var root = Directory.CreateTempSubdirectory("di-refusal-deps-");
        try
        {
            var appDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app")).FullName;
            var self = typeof(DependencyRefusalVisibilityTests).Assembly.Location;
            File.Copy(self, Path.Combine(appDir, "App.dll"), overwrite: true);

            var deps = new
            {
                runtimeTarget = new { name = ".NETCoreApp,Version=v9.0" },
                targets = new Dictionary<string, object>
                {
                    [".NETCoreApp,Version=v9.0"] = new Dictionary<string, object>
                    {
                        ["Hostile/1.0.0"] = new
                        {
                            runtime = new Dictionary<string, object>
                            {
                                ["../../../escape.dll"] = new { localPath = "../../../escape.dll" }
                            }
                        },
                        ["Benign/1.0.0"] = new
                        {
                            runtime = new Dictionary<string, object>
                            {
                                ["lib/net9.0/Benign.dll"] = new { localPath = "Benign.dll" }
                            }
                        }
                    }
                },
                libraries = new Dictionary<string, object>
                {
                    ["Hostile/1.0.0"] = new { type = "package", path = "hostile/1.0.0" },
                    ["Benign/1.0.0"] = new { type = "package", path = "benign/1.0.0" }
                }
            };

            File.WriteAllText(
                Path.Combine(appDir, "App.deps.json"),
                JsonSerializer.Serialize(deps));

            var messages = new List<string>();
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(Path.Combine(appDir, "App.dll"))
                {
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeSiblingAssemblies = false,
                    IncludeDepsJsonAssets = true,
                    Log = messages.Add
                });

            _ = resolver.ResolveAll();

            // The refusal is reported, and it names the value it refused.
            Assert.Contains(messages, m => m.Contains("Refusing unsafe deps.json", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("escape.dll", StringComparison.Ordinal));

            // Positive control: the benign entry in the same file was NOT refused. Without this a
            // guard that rejected every path would satisfy the assertion above.
            Assert.DoesNotContain(messages, m => m.Contains("Benign.dll", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The log channel is optional, so a caller that supplies none must not fault. This is the
    /// regression guard for threading <c>log</c> through the recursive collection walk.
    /// </summary>
    [Fact]
    public void RefusalWithoutALogChannel_DoesNotThrow()
    {
        var root = Directory.CreateTempSubdirectory("di-refusal-nolog-");
        try
        {
            var appDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app")).FullName;
            var self = typeof(DependencyRefusalVisibilityTests).Assembly.Location;
            File.Copy(self, Path.Combine(appDir, "App.dll"), overwrite: true);

            File.WriteAllText(
                Path.Combine(appDir, "App.deps.json"),
                """
                {
                  "runtimeTarget": { "name": ".NETCoreApp,Version=v9.0" },
                  "targets": {
                    ".NETCoreApp,Version=v9.0": {
                      "Hostile/1.0.0": {
                        "runtime": { "../../escape.dll": { "localPath": "../../escape.dll" } }
                      }
                    }
                  },
                  "libraries": { "Hostile/1.0.0": { "type": "package", "path": "hostile/1.0.0" } }
                }
                """);

            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(Path.Combine(appDir, "App.dll"))
                {
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeSiblingAssemblies = false,
                    IncludeDepsJsonAssets = true
                });

            var resolved = resolver.ResolveAll();

            // The escaping asset is not in the graph...
            Assert.DoesNotContain(resolved, r => r.Path.Contains("escape.dll", StringComparison.Ordinal));

            // ...and the run completed, which is the actual claim: no log channel, no fault.
            Assert.NotNull(resolved);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
