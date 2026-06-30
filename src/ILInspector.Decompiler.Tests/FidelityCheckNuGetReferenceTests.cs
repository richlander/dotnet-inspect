using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class FidelityCheckNuGetReferenceTests
{
    [Fact]
    public void PackageDependencyReferencePaths_SelectsCompatibleTfmDependencyGroup()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
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
                        <dependency id="Shared.Package" version="[8.0.0]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string sharedDir = Path.Combine(root, "shared.package", "8.0.0", "lib", "net8.0");
            Directory.CreateDirectory(sharedDir);
            string sharedPath = Path.Combine(sharedDir, "Shared.dll");
            File.WriteAllText(sharedPath, "");

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(sharedPath, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_PrefersExactRefAssetBeforeCompatibleLibFallback()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
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
            string libDir = Path.Combine(root, "shared.package", "8.0.0", "lib", "netstandard2.0");
            Directory.CreateDirectory(libDir);
            string libPath = Path.Combine(libDir, "Shared.dll");
            File.WriteAllText(libPath, "");

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(refPath, references);
            Assert.DoesNotContain(libPath, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_IgnoresNonExactRanges()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
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
                        <dependency id="Shared.Package" version="[1.0.0,2.0.0)" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """);

            string sharedDir = Path.Combine(root, "shared.package", "1.0.0", "lib", "net8.0");
            Directory.CreateDirectory(sharedDir);
            File.WriteAllText(Path.Combine(sharedDir, "Shared.dll"), "");

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Empty(references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_IncludesTransitiveDependencies()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
        try
        {
            string targetPath = CreatePackage(root, "Root.Package", "1.0.0", ("Direct.Package", "1.0.0"));
            string directPath = CreatePackage(root, "Direct.Package", "1.0.0", ("Transitive.Package", "2.0.0"));
            string transitivePath = CreatePackage(root, "Transitive.Package", "2.0.0");

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(directPath, references);
            Assert.Contains(transitivePath, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_SelectsHighestSemVerDependencyVersion()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
        try
        {
            string targetPath = CreatePackage(
                root,
                "Root.Package",
                "1.0.0",
                ("Shared.Preview", "1.0.0-preview.2"),
                ("Shared.Preview", "1.0.0-preview.10"),
                ("Shared.Stable", "2.0.0-preview.6"),
                ("Shared.Stable", "2.0.0"));
            string preview2 = CreatePackage(root, "Shared.Preview", "1.0.0-preview.2");
            string preview10 = CreatePackage(root, "Shared.Preview", "1.0.0-preview.10");
            string prerelease = CreatePackage(root, "Shared.Stable", "2.0.0-preview.6");
            string stable = CreatePackage(root, "Shared.Stable", "2.0.0");

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(preview10, references);
            Assert.DoesNotContain(preview2, references);
            Assert.Contains(stable, references);
            Assert.DoesNotContain(prerelease, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_DropsDependenciesFromSupersededVersions()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
        try
        {
            string targetPath = CreatePackage(
                root,
                "Root.Package",
                "1.0.0",
                ("Shared.Parent", "1.0.0"),
                ("Shared.Parent", "2.0.0"));
            string lowerParent = CreatePackage(root, "Shared.Parent", "1.0.0", ("Stale.Only", "1.0.0"));
            string higherParent = CreatePackage(root, "Shared.Parent", "2.0.0");
            string staleOnly = CreatePackage(root, "Stale.Only", "1.0.0");

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath, [root]);

            Assert.Contains(higherParent, references);
            Assert.DoesNotContain(lowerParent, references);
            Assert.DoesNotContain(staleOnly, references);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreatePackage(string root, string id, string version, params (string Id, string Version)[] dependencies)
    {
        string packageDir = Path.Combine(root, id.ToLowerInvariant(), version.ToLowerInvariant());
        string assetDir = Path.Combine(packageDir, "lib", "net8.0");
        Directory.CreateDirectory(assetDir);
        string assemblyPath = Path.Combine(assetDir, $"{id}.dll");
        File.WriteAllText(assemblyPath, "");

        var dependencyLines = dependencies
            .Select(dependency => $"        <dependency id=\"{dependency.Id}\" version=\"{dependency.Version}\" />");
        File.WriteAllText(
            Path.Combine(packageDir, $"{id.ToLowerInvariant()}.nuspec"),
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <dependencies>
                  <group targetFramework="net8.0">
            {{string.Join(Environment.NewLine, dependencyLines)}}
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        return assemblyPath;
    }
}
