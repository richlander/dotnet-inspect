using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class AssemblyDependencyResolverTests
{
    [Fact]
    public void Resolve_PlatformReference_RollsForwardToInstalledAssembly()
    {
        var current = typeof(System.Data.Common.DbDataReader).Assembly.GetName();
        string token = Convert.ToHexString(current.GetPublicKeyToken()!).ToLowerInvariant();
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(
            typeof(AssemblyDependencyResolverTests).Assembly.Location)
        {
            AllowPlatformAssemblyVersionRollForward = true,
        });

        var resolved = resolver.Resolve(
            new AssemblyReferenceIdentity(current.Name!, new Version(1, 0, 0, 0), null, token),
            AssemblyResolutionScope.Platform);

        Assert.NotNull(resolved);
        Assert.Equal(current.Name + ".dll", Path.GetFileName(resolved.Path));

        var future = resolver.Resolve(
            new AssemblyReferenceIdentity(
                current.Name!,
                new Version(current.Version!.Major + 1, 0, 0, 0),
                null,
                token),
            AssemblyResolutionScope.Platform);
        Assert.Null(future);
    }

    [Fact]
    public void ResolveAll_IncludesTargetByDefaultAndLetsHarnessExcludeIt()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string target = Path.Combine(root, "Target.dll");
            string sibling = Path.Combine(root, "Sibling.dll");
            File.WriteAllText(target, "");
            File.WriteAllText(sibling, "");

            var defaultResolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(target)
            {
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
            });

            var harnessResolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(target)
            {
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
                ExcludeTargetAssembly = true,
            });

            Assert.Contains(defaultResolver.ResolveAll(), dependency => dependency.Path == target);
            Assert.Contains(defaultResolver.ResolveAll(), dependency => dependency.Path == sibling);
            Assert.DoesNotContain(harnessResolver.ResolveAll(), dependency => dependency.Path == target);
            Assert.Contains(harnessResolver.ResolveAll(), dependency => dependency.Path == sibling);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAll_RecordsPackageProvenanceForRefAssets()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string refDir = Path.Combine(root, "shared.package", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(refDir);
            string refPath = Path.Combine(refDir, "Shared.dll");
            File.WriteAllText(refPath, "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
            });

            var dependency = Assert.Single(resolver.ResolveAll());
            Assert.Equal(refPath, dependency.Path);
            Assert.Equal(AssemblyDependencyProvenance.PackageDependency, dependency.Provenance);
            Assert.Equal("shared.package", dependency.PackageId);
            Assert.Equal("8.0.0", dependency.PackageVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAll_PrefersSiblingAssemblyOverPackageAndFrameworkCandidates()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            string siblingPath = Path.Combine(targetDir, "Shared.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(siblingPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string packageRefDir = Path.Combine(root, "shared.package", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(packageRefDir);
            File.WriteAllText(Path.Combine(packageRefDir, "Shared.dll"), "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeDepsJsonAssets = false,
            });

            var dependency = Assert.Single(resolver.ResolveAll(), dependency => Path.GetFileName(dependency.Path) == "Shared.dll");
            Assert.Equal(siblingPath, dependency.Path);
            Assert.Equal(AssemblyDependencyProvenance.SiblingAssembly, dependency.Provenance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveAll_CanPreferImplementationPackageAssetsForDecompilerCallers()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string refDir = Path.Combine(root, "shared.package", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(refDir);
            string refPath = Path.Combine(refDir, "Shared.dll");
            File.WriteAllText(refPath, "");
            string libDir = Path.Combine(root, "shared.package", "8.0.0", "lib", "net8.0");
            Directory.CreateDirectory(libDir);
            string libPath = Path.Combine(libDir, "Shared.dll");
            File.WriteAllText(libPath, "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeTrustedPlatformAssemblies = false,
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
                PreferImplementationAssemblies = true,
            });

            var dependency = Assert.Single(resolver.ResolveAll());
            Assert.Equal(libPath, dependency.Path);
            Assert.NotEqual(refPath, dependency.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_SelectsCurrentTfmDependencyGroup()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Root.Package</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework=".NETStandard2.0">
                        <dependency id="Shared.Package" version="2.0.0" />
                      </group>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string netstandardPath = CreatePackageAsset(root, "Shared.Package", "2.0.0", "lib", "netstandard2.0", "Shared.dll");
            string net8Path = CreatePackageAsset(root, "Shared.Package", "8.0.0", "lib", "net8.0", "Shared.dll");

            var references = AssemblyDependencyResolver.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(net8Path, references);
            Assert.DoesNotContain(netstandardPath, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_HandlesBracketExactRangeAndLowercaseVersionDirectory()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="Shared.Package" version="[8.0.0-RC1]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string dependencyPath = CreatePackageAsset(root, "Shared.Package", "8.0.0-rc1", "lib", "net8.0", "Shared.dll");

            var references = AssemblyDependencyResolver.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(dependencyPath, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_PlatformTrustFindsTrustedAssemblyWhenPackageCandidateHasSameName()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="System.Runtime" version="8.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string refDir = Path.Combine(root, "system.runtime", "8.0.0", "ref", "net8.0");
            Directory.CreateDirectory(refDir);
            File.WriteAllText(Path.Combine(refDir, "System.Runtime.dll"), "");

            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                PackageRoots = [root],
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
            });

            var resolved = resolver.Resolve(
                new AssemblyReferenceIdentity("System.Runtime", Version: null, Culture: null, PublicKeyToken: null),
                AssemblyResolutionScope.Platform);

            Assert.NotNull(resolved);
            Assert.Equal("System.Runtime.dll", Path.GetFileName(resolved.Path));
            using (resolved.OpenRead())
            {
            }
            Assert.DoesNotContain($"{Path.DirectorySeparatorChar}system.runtime{Path.DirectorySeparatorChar}", resolved.Path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Artifact canary for the nuspec dependency-coordinate sink. A dependency id and version are
    /// read from a nuspec, which is a feed artifact rather than something this process authored,
    /// and both become path components. This plants a readable payload exactly where a traversing
    /// id lands: on unguarded code the resolver escapes the package root, recurses into that
    /// directory, and returns its assemblies as the package's references.
    /// </summary>
    /// <remarks>
    /// Which spellings actually traverse is a property of the host. <c>..\escape</c> is a traversal
    /// on Windows and an ordinary directory name on Unix, so on this machine that case exercises
    /// the guard's literal-name branch and proves nothing about traversal.
    /// <see cref="HostileDependencyIds_ContainACaseThatTraversesOnThisHost"/> is what stops the
    /// whole theory degrading to literal names, which is how it would pass while the traversal
    /// guard was gone.
    /// </remarks>
    private static readonly string[] HostileIds = ["../escape", "..\\escape", "CON"];

    public static TheoryData<string> HostileDependencyIds => [.. HostileIds];

    /// <summary>
    /// Non-vacuity gate for the theory below: at least one hostile id must use a separator this
    /// host recognises, or every case is a literal directory name and the theory asserts nothing
    /// about traversal. It reads the same data the theory does, so removing the traversing
    /// spelling fails here rather than silently weakening the theory.
    /// </summary>
    [Fact]
    public void HostileDependencyIds_ContainACaseThatTraversesOnThisHost()
    {
        Assert.Contains(
            HostileIds,
            id => id.Contains(Path.DirectorySeparatorChar)
                || id.Contains(Path.AltDirectorySeparatorChar));
    }

    [Theory]
    [MemberData(nameof(HostileDependencyIds))]
    public async Task PackageDependencyReferencePaths_WithTraversingDependencyId_DoesNotEscapePackageRoot(string hostileId)
    {
        string sandbox = Directory.CreateTempSubdirectory("dotnet-inspect-deps-traversal-").FullName;
        try
        {
            string root = Path.Combine(sandbox, "packages");
            Directory.CreateDirectory(root);

            string packageDir = Path.Combine(root, "root.package", "1.0.0");
            string targetDir = Path.Combine(packageDir, "lib", "net8.0");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "Root.dll");
            File.WriteAllText(targetPath, "");
            File.WriteAllText(
                Path.Combine(packageDir, "root.package.nuspec"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Root.Package</id>
                    <version>1.0.0</version>
                    <dependencies>
                      <group targetFramework="net8.0">
                        <dependency id="{System.Security.SecurityElement.Escape(hostileId)}" version="1.0.0" />
                        <dependency id="Legit.Package" version="1.0.0" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            // The payload sits one level above the package root, where "../escape" lands, and
            // also at the literal name inside it, so a refusal cannot be mistaken for absence.
            string payload = CreatePackageAsset(sandbox, "escape", "1.0.0", "lib", "net8.0", "Payload.dll");
            string literal = CreatePackageAsset(root, hostileId.Replace("..", "dots"), "1.0.0", "lib", "net8.0", "Payload.dll");

            // Positive control: the guard must refuse traversal specifically, not stop resolving.
            string legit = CreatePackageAsset(root, "Legit.Package", "1.0.0", "lib", "net8.0", "Legit.dll");

            IReadOnlyList<string> references = [];
            var stderr = await StderrCapture.RunAsync(() =>
                references = AssemblyDependencyResolver.PackageDependencyReferencePaths(targetPath, [root]));

            // The refusal has to reach the user. It was reported through the optional
            // Action<string>? log, and no caller in the product passes one, so the message went
            // nowhere: the dependency simply vanished from the result and the run exited 0. A
            // refused coordinate is indistinguishable from an absent one unless it is announced.
            Assert.Contains("refusing unsafe package coordinate", stderr, StringComparison.Ordinal);
            Assert.Contains(hostileId, stderr, StringComparison.Ordinal);

            // Canonicalise before comparing. Path.Combine does not normalise, so an escaping id
            // yields "<root>/../escape/lib/net8.0/Payload.dll" -- a string that both StartsWith
            // "<root>/" and differs character-for-character from the payload's own path. Asserting
            // on the raw strings therefore passed with the guard entirely removed: only the "CON"
            // row failed, and the traversal rows, the whole point of the theory, proved nothing.
            var resolved = references.Select(Path.GetFullPath).ToList();
            var rootPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

            Assert.Contains(Path.GetFullPath(legit), resolved);
            Assert.DoesNotContain(Path.GetFullPath(payload), resolved);
            Assert.DoesNotContain(Path.GetFullPath(literal), resolved);
            Assert.All(resolved, path => Assert.StartsWith(rootPrefix, path, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    static string CreatePackageAsset(string root, string id, string version, string assetKind, string tfm, string fileName)
    {
        string assetDir = Path.Combine(root, id.ToLowerInvariant(), version.ToLowerInvariant(), assetKind, tfm);
        Directory.CreateDirectory(assetDir);
        string path = Path.Combine(assetDir, fileName);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Resolve_HonorsVersionAndCultureWhenIdentitySpecifiesThem()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-assembly-deps-").FullName;
        try
        {
            string targetPath = Path.Combine(root, "Target.dll");
            File.WriteAllText(targetPath, "");
            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
            {
                IncludeAspNetCoreSharedFramework = false,
                IncludeSiblingAssemblies = false,
                IncludeDepsJsonAssets = false,
            });

            var runtimeVersion = typeof(object).Assembly.GetName().Version;
            Assert.NotNull(resolver.Resolve(
                new AssemblyReferenceIdentity("System.Private.CoreLib", runtimeVersion, Culture: null, PublicKeyToken: null),
                AssemblyResolutionScope.Platform));
            Assert.Null(resolver.Resolve(
                new AssemblyReferenceIdentity("System.Private.CoreLib", new Version(0, 0, 0, 0), Culture: null, PublicKeyToken: null),
                AssemblyResolutionScope.Platform));
            Assert.Null(resolver.Resolve(
                new AssemblyReferenceIdentity("System.Private.CoreLib", Version: null, Culture: "not-a-real-culture", PublicKeyToken: null),
                AssemblyResolutionScope.Platform));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
