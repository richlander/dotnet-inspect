using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class AssemblyDependencyResolverTests
{
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
