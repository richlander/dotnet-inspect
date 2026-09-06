using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;

namespace DotnetInspector.Tests;

public sealed class PackageFixtureTests
{
    private const string ToolV2Version = "1.0.0";
    private const string MetadataConfusionVersion = "1.0.0";
    private const string PointerId = "DotnetInspect.TestAssets.ToolV2";
    private const string LinuxId = "DotnetInspect.TestAssets.ToolV2.linux-x64";
    private const string MissingWindowsId = "DotnetInspect.TestAssets.ToolV2.win-x64";
    private const string MetadataConfusionId =
        "DotnetInspect.TestAssets.MetadataConfusion";

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PackageFixtureCatalog_PacksDeclaredToolV2Packages()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-package-fixtures-{Guid.NewGuid():N}");
        string packages = Path.Combine(temp, "packages");

        try
        {
            await PackAsync(
                root,
                temp,
                packages,
                "tool-v2",
                "linux-x64");
            await PackAsync(
                root,
                temp,
                packages,
                "tool-v2",
                "pointer");

            string[] actualPackages = Directory
                .EnumerateFiles(packages, "*.nupkg")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray()!;
            Assert.Equal(
                new[]
                {
                    $"{PointerId}.{ToolV2Version}.nupkg",
                    $"{LinuxId}.{ToolV2Version}.nupkg",
                }.Order(StringComparer.Ordinal),
                actualPackages);
            Assert.DoesNotContain(
                $"{MissingWindowsId}.{ToolV2Version}.nupkg",
                actualPackages);

            string pointerPath = Path.Combine(
                packages,
                $"{PointerId}.{ToolV2Version}.nupkg");
            using (ZipArchive pointer = ZipFile.OpenRead(pointerPath))
            {
                AssertPackageIdentity(
                    pointer,
                    PointerId,
                    ToolV2Version,
                    "DotnetTool",
                    containsManagedAssembly: false);
                Assert.NotNull(pointer.GetEntry("README.md"));

                XDocument settings = ReadXmlEntry(
                    pointer,
                    "tools/any/any/DotnetToolSettings.xml");
                Assert.Equal(
                    "2",
                    settings.Root?.Attribute("Version")?.Value);
                XElement command = Assert.Single(
                    settings.Descendants("Command"));
                Assert.Equal(
                    "dotnet-inspect-fixture",
                    command.Attribute("Name")?.Value);
                Assert.Null(command.Attribute("EntryPoint"));
                Assert.Null(command.Attribute("Runner"));

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
                $"{LinuxId}.{ToolV2Version}.nupkg");
            using (ZipArchive linux = ZipFile.OpenRead(linuxPath))
            {
                AssertPackageIdentity(
                    linux,
                    LinuxId,
                    ToolV2Version,
                    "DotnetToolRidPackage",
                    containsManagedAssembly: false);
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
    [Trait("Speed", "Slow")]
    public async Task PackageFixtureCatalog_PacksMetadataConfusionPackage()
    {
        string root = FindRepositoryRoot();
        string temp = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-metadata-fixture-{Guid.NewGuid():N}");
        string packages = Path.Combine(temp, "packages");

        try
        {
            await PackAsync(
                root,
                temp,
                packages,
                "metadata-confusion",
                "metadata-confusion");

            string packagePath = Assert.Single(
                Directory.EnumerateFiles(packages, "*.nupkg"));
            Assert.Equal(
                $"{MetadataConfusionId}.{MetadataConfusionVersion}.nupkg",
                Path.GetFileName(packagePath));
            await VerifyMetadataPackageAsync(temp, packagePath);
            await VerifyHostileVersionDiagnosticAsync(temp, packagePath);

            using ZipArchive package = ZipFile.OpenRead(packagePath);
            AssertPackageIdentity(
                package,
                MetadataConfusionId,
                MetadataConfusionVersion,
                expectedType: null,
                containsManagedAssembly: true);
            Assert.NotNull(package.GetEntry("README.md"));
            ZipArchiveEntry assembly = package.GetEntry(
                $"lib/net11.0/{MetadataConfusionId}.dll")
                ?? throw new Xunit.Sdk.XunitException(
                    "Metadata-confusion assembly is missing.");
            ZipArchiveEntry manifest = package.GetEntry(
                "content/metadata-fixture.json")
                ?? throw new Xunit.Sdk.XunitException(
                    "Metadata-confusion manifest is missing.");

            byte[] manifestBytes;
            using (Stream stream = manifest.Open())
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                manifestBytes = buffer.ToArray();
            }
            Assert.DoesNotContain((byte)'\r', manifestBytes);
            Assert.Equal((byte)'\n', manifestBytes[^1]);

            using (JsonDocument document =
                JsonDocument.Parse(manifestBytes))
            {
                JsonElement rootElement = document.RootElement;
                Assert.Equal(
                    1,
                    rootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(
                    MetadataConfusionVersion,
                    rootElement.GetProperty("packageVersion").GetString());
                Assert.Equal(
                    $"lib/net11.0/{MetadataConfusionId}.dll",
                    rootElement.GetProperty("assemblyPath").GetString());
                JsonElement[] specimens =
                [
                    .. rootElement
                        .GetProperty("specimens")
                        .EnumerateArray(),
                ];
                Assert.Equal(18, specimens.Length);
                Assert.Equal(
                    specimens.Length,
                    specimens
                        .Select(specimen =>
                            specimen.GetProperty("id").GetString())
                        .Distinct(StringComparer.Ordinal)
                        .Count());
                string repositoryRawJson = FindSpecimen(
                    specimens,
                    "repository-url-bidi")
                    .GetProperty("raw")
                    .GetRawText();
                Assert.True(
                    string.Equals(
                        repositoryRawJson,
                        @"""https://api.\u202Etentod\u202C.com/v3/index.json""",
                        StringComparison.Ordinal),
                    "repository-url-bidi did not preserve the expected "
                        + @"\u202E and \u202C JSON escapes.");
                string userStringRawJson = FindSpecimen(
                    specimens,
                    "user-string-osc52")
                    .GetProperty("raw")
                    .GetRawText();
                Assert.True(
                    userStringRawJson.Contains(
                        @"\u001B]52;c;",
                        StringComparison.Ordinal),
                    "user-string-osc52 did not preserve the expected "
                        + @"\u001B JSON escape.");
            }

            using Stream assemblyStream = assembly.Open();
            using var image = new MemoryStream();
            assemblyStream.CopyTo(image);
            image.Position = 0;
            using var pe = new PEReader(image);
            MetadataReader reader = pe.GetMetadataReader();
            string assemblyName =
                reader.GetString(reader.GetAssemblyDefinition().Name);
            Assert.True(
                string.Equals(
                    assemblyName,
                    "DotnetInspect.Metadata\u202Eeman\u202C",
                    StringComparison.Ordinal),
                "The assembly name did not preserve the expected "
                    + "U+202E and U+202C sequence.");
            Assert.Contains(
                reader.TypeDefinitions,
                handle =>
                {
                    TypeDefinition type = reader.GetTypeDefinition(handle);
                    return reader.GetString(type.Namespace)
                            == "Dotnet\u200BInspect.Metadata"
                        && reader.GetString(type.Name)
                            == "Route\u202EepyT\u202C";
                });
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
        Assert.Contains("type: choice", workflow);
        Assert.Contains("- tool-v2", workflow);
        Assert.Contains("- metadata-confusion", workflow);
        Assert.Contains("packages: write", workflow);
        Assert.Contains(
            "github.ref == 'refs/heads/main' && inputs.confirm == 'publish'",
            workflow);
        Assert.Contains(
            "https://nuget.pkg.github.com/richlander/index.json",
            workflow);
        Assert.Contains(
            "--filter-class \"DotnetInspector.Tests.PackageFixtureTests\"",
            workflow);
        Assert.Contains(
            "-p:FixtureFamily=\"$FIXTURE_FAMILY\"",
            workflow);
        Assert.Contains(
            "verify-package \"$package\"",
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
        int metadataPush = publication.IndexOf(
            $"DotnetInspect.TestAssets.MetadataConfusion.${{FIXTURE_VERSION}}.nupkg",
            StringComparison.Ordinal);
        Assert.True(
            linuxPush >= 0 && pointerPush > linuxPush,
            "The RID package must publish before the pointer package.");
        Assert.True(
            metadataPush >= 0,
            "The metadata-confusion package is not published.");
    }

    private static async Task PackAsync(
        string root,
        string temp,
        string packages,
        string fixtureFamily,
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
            $"-p:FixtureFamily={fixtureFamily}",
            $"-p:FixturePackage={fixturePackage}",
            $"-p:ArtifactsPath={Path.Combine(temp, fixtureFamily, "artifacts")}",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["NUGET_PACKAGES"] = Path.Combine(
            temp,
            "nuget-packages");

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

    private static async Task VerifyMetadataPackageAsync(
        string temp,
        string packagePath)
    {
        var result = await RunMetadataGeneratorAsync(temp, packagePath);
        Assert.True(
            result.ExitCode == 0,
            "Metadata fixture verification failed."
                + Environment.NewLine
                + result.Stdout
                + result.Stderr);
    }

    private static async Task VerifyHostileVersionDiagnosticAsync(
        string temp,
        string packagePath)
    {
        string hostilePackagePath = Path.Combine(
            temp,
            "hostile-version.nupkg");
        File.Copy(packagePath, hostilePackagePath);
        using (ZipArchive package = ZipFile.Open(
            hostilePackagePath,
            ZipArchiveMode.Update))
        {
            ZipArchiveEntry nuspec = Assert.Single(
                package.Entries,
                entry => entry.FullName.EndsWith(
                    ".nuspec",
                    StringComparison.OrdinalIgnoreCase));
            XDocument document;
            using (Stream source = nuspec.Open())
            {
                document = XDocument.Load(source);
            }
            Assert.Single(
                document.Descendants(),
                element => element.Name.LocalName == "version")
                .Value = "9.9.9\u202Ednwp\u202C\u2060";
            string nuspecPath = nuspec.FullName;
            nuspec.Delete();
            ZipArchiveEntry replacement = package.CreateEntry(nuspecPath);
            using Stream destination = replacement.Open();
            document.Save(destination);
        }

        var result = await RunMetadataGeneratorAsync(
            temp,
            hostilePackagePath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(
            result.Stderr.Contains(
                "The package version does not match the version-owned fixture generator.",
                StringComparison.Ordinal),
            "A hostile package version did not produce the controlled mismatch diagnostic.");
        Assert.True(
            result.Stderr.All(
                value => value is '\r' or '\n' or '\t'
                    || char.GetUnicodeCategory(value) is not (
                        UnicodeCategory.Control
                        or UnicodeCategory.Format
                        or UnicodeCategory.Surrogate
                        or UnicodeCategory.LineSeparator
                        or UnicodeCategory.ParagraphSeparator)),
            "The hostile package-version diagnostic rendered a live control, "
                + "format, surrogate, or separator scalar.");
    }

    private static async Task<(
        int ExitCode,
        string Stdout,
        string Stderr)> RunMetadataGeneratorAsync(
            string temp,
            string packagePath)
    {
        string generator = Assert.Single(
            Directory.EnumerateFiles(
                Path.Combine(
                    temp,
                    "metadata-confusion",
                    "artifacts",
                    "bin"),
                "MetadataConfusionGenerator.dll",
                SearchOption.AllDirectories));
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(generator);
        startInfo.ArgumentList.Add("verify-package");
        startInfo.ArgumentList.Add(packagePath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start metadata fixture verifier.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private static void AssertPackageIdentity(
        ZipArchive package,
        string expectedId,
        string expectedVersion,
        string? expectedType,
        bool containsManagedAssembly)
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
            expectedVersion,
            Assert.Single(
                metadata.Elements(),
                element => element.Name.LocalName == "version").Value);
        XElement[] packageTypes =
        [
            .. metadata.Descendants()
                .Where(
                    element =>
                        element.Name.LocalName == "packageType"),
        ];
        if (expectedType is null)
        {
            Assert.Empty(packageTypes);
        }
        else
        {
            Assert.Equal(
                expectedType,
                Assert.Single(packageTypes)
                    .Attribute("name")?
                    .Value);
        }
        XElement repository = Assert.Single(
            metadata.Elements(),
            element => element.Name.LocalName == "repository");
        Assert.Equal(
            "https://github.com/richlander/dotnet-inspect",
            repository.Attribute("url")?.Value);
        Assert.Equal(
            40,
            repository.Attribute("commit")?.Value.Length);
        Assert.Equal(
            containsManagedAssembly,
            package.Entries.Any(
                entry => entry.FullName.EndsWith(
                    ".dll",
                    StringComparison.OrdinalIgnoreCase)));
    }

    private static JsonElement FindSpecimen(
        IEnumerable<JsonElement> specimens,
        string id) =>
        specimens.Single(
            specimen =>
                specimen.GetProperty("id").GetString() == id);

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
