using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class FidelityCheckNuGetReferenceTests
{
    [Fact]
    public void PackageDependencyReferencePaths_SelectsCompatibleTfmDependencyGroup()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
        string? previousRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", root);
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

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath);

            Assert.Contains(sharedPath, references);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_PrefersExactRefAssetBeforeCompatibleLibFallback()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
        string? previousRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", root);
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

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath);

            Assert.Contains(refPath, references);
            Assert.DoesNotContain(libPath, references);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousRoot);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageDependencyReferencePaths_IgnoresNonExactRanges()
    {
        string root = Directory.CreateTempSubdirectory("dotnet-inspect-nuget-context-").FullName;
        string? previousRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", root);
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

            var references = FidelityCheck.PackageDependencyReferencePaths(targetPath);

            Assert.Empty(references);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousRoot);
            Directory.Delete(root, recursive: true);
        }
    }
}
