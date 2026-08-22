using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace DotnetInspector.Tests;

public sealed class PackageFixtureTests
{
    private const string Version = "1.0.0";
    private const string PointerId = "DotnetInspect.TestAssets.ToolV2";
    private const string LinuxId = "DotnetInspect.TestAssets.ToolV2.linux-x64";
    private const string MissingWindowsId = "DotnetInspect.TestAssets.ToolV2.win-x64";

    [Fact]
    public async Task PackageFixtureCatalog_PacksDeclaredToolV2Packages()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-package-fixtures-{Guid.NewGuid():N}");
        string packages = Path.Combine(temp, "packages");

        try
        {
            await PackAsync(root, temp, packages, "linux-x64");
            await PackAsync(root, temp, packages, "pointer");

            string[] actualPackages = Directory
                .EnumerateFiles(packages, "*.nupkg")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray()!;
            Assert.Equal(
                new[]
                {
                    $"{PointerId}.{Version}.nupkg",
                    $"{LinuxId}.{Version}.nupkg",
                }.Order(StringComparer.Ordinal),
                actualPackages);
            Assert.DoesNotContain(
                $"{MissingWindowsId}.{Version}.nupkg",
                actualPackages);

            string pointerPath = Path.Combine(
                packages,
                $"{PointerId}.{Version}.nupkg");
            using (ZipArchive pointer = ZipFile.OpenRead(pointerPath))
            {
                AssertPackageIdentity(pointer, PointerId, "DotnetTool");
                Assert.NotNull(pointer.GetEntry("README.md"));

                XDocument settings = ReadXmlEntry(
                    pointer,
                    "tools/net10.0/any/DotnetToolSettings.xml");
                Assert.Equal(
                    "2",
                    settings.Root?.Attribute("Version")?.Value);

                var ridPackages = settings
                    .Descendants("RuntimeIdentifierPackage")
                    .Select(element => (
                        Rid: element.Attribute("RuntimeIdentifier")?.Value,
                        Id: element.Attribute("Id")?.Value))
                    .ToArray();
                (string? Rid, string? Id)[] expectedRidPackages =
                [
                    (Rid: "linux-x64", Id: LinuxId),
                    (Rid: "win-x64", Id: MissingWindowsId),
                ];
                Assert.Equal(
                    expectedRidPackages,
                    ridPackages);
            }

            string linuxPath = Path.Combine(
                packages,
                $"{LinuxId}.{Version}.nupkg");
            using (ZipArchive linux = ZipFile.OpenRead(linuxPath))
            {
                AssertPackageIdentity(
                    linux,
                    LinuxId,
                    "DotnetToolRidPackage");
                Assert.NotNull(linux.GetEntry("README.md"));
                XDocument settings = ReadXmlEntry(
                    linux,
                    "tools/any/linux-x64/DotnetToolSettings.xml");
                Assert.Equal(
                    "executable",
                    settings
                        .Descendants("Command")
                        .Single()
                        .Attribute("Runner")?
                        .Value);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void PackageFixturePublisher_IsManualMainOnlyAndImmutable()
    {
        string workflow = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                ".github",
                "workflows",
                "publish-package-fixtures.yml"));

        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("packages: write", workflow);
        Assert.Contains(
            "github.ref == 'refs/heads/main' && inputs.confirm == 'publish'",
            workflow);
        Assert.Contains(
            "https://nuget.pkg.github.com/richlander/index.json",
            workflow);
        Assert.DoesNotContain("--skip-duplicate", workflow);

        int publishStep = workflow.IndexOf(
            "- name: Publish immutable fixture version",
            StringComparison.Ordinal);
        Assert.True(
            publishStep >= 0,
            "The fixture workflow does not define its publication step.");
        string publication = workflow[publishStep..];
        int linuxPush = publication.IndexOf(
            $"DotnetInspect.TestAssets.ToolV2.linux-x64.${{FIXTURE_VERSION}}.nupkg",
            StringComparison.Ordinal);
        int pointerPush = publication.IndexOf(
            $"DotnetInspect.TestAssets.ToolV2.${{FIXTURE_VERSION}}.nupkg",
            StringComparison.Ordinal);
        Assert.True(
            linuxPush >= 0 && pointerPush > linuxPush,
            "The RID package must publish before the pointer package.");
    }

    private static async Task PackAsync(
        string root,
        string temp,
        string packages,
        string fixturePackage)
    {
        string project = Path.Combine(
            root,
            "eng",
            "package-fixtures",
            "PackageFixtures.proj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[]
        {
            "pack",
            project,
            "-c",
            "Release",
            "-o",
            packages,
            "--nologo",
            $"-p:FixturePackage={fixturePackage}",
            $"-p:ArtifactsPath={Path.Combine(temp, fixturePackage, "artifacts")}",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet pack.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        Assert.True(
            process.ExitCode == 0,
            $"dotnet pack failed for {fixturePackage}.{Environment.NewLine}"
                + stdout
                + stderr);
    }

    private static void AssertPackageIdentity(
        ZipArchive package,
        string expectedId,
        string expectedType)
    {
        XDocument nuspec = ReadXmlEntry(package, $"{expectedId}.nuspec");
        XElement metadata = Assert.Single(
            nuspec.Descendants(),
            element => element.Name.LocalName == "metadata");
        Assert.Equal(
            expectedId,
            Assert.Single(
                metadata.Elements(),
                element => element.Name.LocalName == "id").Value);
        Assert.Equal(
            Version,
            Assert.Single(
                metadata.Elements(),
                element => element.Name.LocalName == "version").Value);
        Assert.Equal(
            expectedType,
            Assert.Single(
                metadata.Descendants(),
                element => element.Name.LocalName == "packageType")
                .Attribute("name")?
                .Value);
        XElement repository = Assert.Single(
            metadata.Elements(),
            element => element.Name.LocalName == "repository");
        Assert.Equal(
            "https://github.com/richlander/dotnet-inspect",
            repository.Attribute("url")?.Value);
        Assert.Equal(
            40,
            repository.Attribute("commit")?.Value.Length);
        Assert.DoesNotContain(
            package.Entries,
            entry => entry.FullName.EndsWith(
                ".dll",
                StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument ReadXmlEntry(
        ZipArchive package,
        string path)
    {
        ZipArchiveEntry entry = package.GetEntry(path)
            ?? throw new Xunit.Sdk.XunitException(
                $"Package does not contain {path}.");
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }
}
