extern alias WrongContractFixture;

using TsJsExport;
using TsJsExport.ContextFixtures.Alpha;
using TsJsExport.ContextFixtures.Beta;
using TsJsExport.ContextFixtures.Host;
using WrongContractContext =
    WrongContractFixture::TsJsExport.WrongContractContext;

namespace ILInspector.JsExportSurface.Tests;

public sealed class JsExportContextLoaderTests
{
    [Fact]
    public void ContextRootsResolveExactCompiledAssemblySet()
    {
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            typeof(MultiAssemblyContext).Assembly.Location,
            typeof(MultiAssemblyContext).FullName!,
            [AppContext.BaseDirectory],
            "test",
            error,
            out var roots);

        Assert.True(success, error.ToString());
        Assert.Empty(error.ToString());
        Assert.Equal(
            [
                typeof(AlphaExports).Assembly.GetName().Name,
                typeof(BetaExports).Assembly.GetName().Name,
                typeof(MultiAssemblyContext).Assembly.GetName().Name,
            ],
            roots.Select(root => root.Assembly.Name));
        Assert.Equal(
            [
                "TsJsExport.ContextFixtures.Alpha.ts",
                "TsJsExport.ContextFixtures.Beta.ts",
                "TsJsExport.ContextFixtures.Host.ts",
            ],
            roots.Select(root => root.ArtifactName));
    }

    [Fact]
    public void ContextMissingRootAssemblyFailsClosed()
    {
        using var directory = new TemporaryDirectory();
        string contextPath = directory.CopyAssembly(
            typeof(MultiAssemblyContext).Assembly.Location);
        directory.CopyAssembly(typeof(AlphaExports).Assembly.Location);
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            contextPath,
            typeof(MultiAssemblyContext).FullName!,
            [directory.Path],
            "test",
            error,
            out var roots);

        Assert.False(success);
        Assert.Empty(roots);
        Assert.Contains(
            "rooted assembly was not found",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("CON")]
    [InlineData("COM¹")]
    [InlineData("COM².txt")]
    [InlineData("LPT³")]
    [InlineData("bad/name")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void ContextRejectsNonPortableArtifactNames(string assemblyName)
    {
        Assert.False(
            JsExportContextLoader.TryGetArtifactName(
                assemblyName,
                out string? artifactName));
        Assert.Null(artifactName);
    }

    [Theory]
    [InlineData(252, true)]
    [InlineData(253, false)]
    public void ContextArtifactNamesEnforcePortableAsciiLength(
        int assemblyNameLength,
        bool expected)
    {
        bool success = JsExportContextLoader.TryGetArtifactName(
            new string('a', assemblyNameLength),
            out string? artifactName);

        Assert.Equal(expected, success);
        Assert.Equal(
            expected ? new string('a', assemblyNameLength) + ".ts" : null,
            artifactName);
    }

    [Theory]
    [InlineData(126, true)]
    [InlineData(127, false)]
    public void ContextArtifactNamesEnforcePortableUtf8Length(
        int assemblyNameLength,
        bool expected)
    {
        bool success = JsExportContextLoader.TryGetArtifactName(
            new string('é', assemblyNameLength),
            out string? artifactName);

        Assert.Equal(expected, success);
        Assert.Equal(
            expected ? new string('é', assemblyNameLength) + ".ts" : null,
            artifactName);
    }

    [Fact]
    public void ContextRejectsCaseInsensitiveArtifactNameCollisions()
    {
        Assert.True(
            JsExportContextLoader.ArtifactNamesCollide(
                "Facade.ts",
                "facade.ts"));
        Assert.False(
            JsExportContextLoader.ArtifactNamesCollide(
                "Facade.One.ts",
                "Facade.Two.ts"));
    }

    [Fact]
    public void ContextRejectsDuplicateAssemblyRoots()
    {
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            typeof(DuplicateAssemblyContext).Assembly.Location,
            typeof(DuplicateAssemblyContext).FullName!,
            [AppContext.BaseDirectory],
            "test",
            error,
            out var roots);

        Assert.False(success);
        Assert.Empty(roots);
        Assert.Contains(
            "more than one root for assembly",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextIncludesEveryExportAcrossRootedAssembly()
    {
        var error = new StringWriter();

        bool success = JsExportContextGenerator.TryGenerate(
            typeof(AlphaOnlyContext).Assembly.Location,
            typeof(AlphaOnlyContext).FullName!,
            [AppContext.BaseDirectory],
            "./dotnet.js",
            "test",
            error,
            out var facades);

        Assert.True(success, error.ToString());
        GeneratedJsExportFacade facade = Assert.Single(facades);
        Assert.Contains(
            "export function identifyAlpha(",
            facade.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "export function double(",
            facade.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextRejectsRootAttributeFromWrongContractIdentity()
    {
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            typeof(WrongContractIdentityContext).Assembly.Location,
            typeof(WrongContractIdentityContext).FullName!,
            [],
            "test",
            error,
            out var roots);

        Assert.False(success);
        Assert.Empty(roots);
        Assert.Contains(
            "uses JsExportRootAttribute from incompatible contract assembly",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextIgnoresLocallyDefinedLookalikeRootAttribute()
    {
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            typeof(WrongContractContext).Assembly.Location,
            typeof(WrongContractContext).FullName!,
            [],
            "test",
            error,
            out var roots);

        Assert.False(success);
        Assert.Empty(roots);
        Assert.Contains(
            "has no JsExportRoot declarations from TsJsExport.Contracts",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextFailureReturnsNoFacadeSources()
    {
        var error = new StringWriter();

        bool success = JsExportContextGenerator.TryGenerate(
            typeof(EmptySurfaceContext).Assembly.Location,
            typeof(EmptySurfaceContext).FullName!,
            [AppContext.BaseDirectory],
            "./dotnet.js",
            "test",
            error,
            out var facades);

        Assert.False(success);
        Assert.Empty(facades);
        Assert.Contains(
            "has no supported [JSExport] methods",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextRejectsGenericRootType()
    {
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            typeof(GenericRootContext).Assembly.Location,
            typeof(GenericRootContext).FullName!,
            [],
            "test",
            error,
            out var roots);

        Assert.False(success);
        Assert.Empty(roots);
        Assert.Contains(
            "is generic",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextRootsResolveFromExplicitSearchDirectories()
    {
        using var directory = new TemporaryDirectory();
        string contextPath = directory.CopyAssembly(
            typeof(MultiAssemblyContext).Assembly.Location);
        string alphaDirectory = directory.CreateSubdirectory("alpha");
        string betaDirectory = directory.CreateSubdirectory("beta");
        File.Copy(
            typeof(AlphaExports).Assembly.Location,
            System.IO.Path.Combine(
                alphaDirectory,
                System.IO.Path.GetFileName(
                    typeof(AlphaExports).Assembly.Location)));
        File.Copy(
            typeof(BetaExports).Assembly.Location,
            System.IO.Path.Combine(
                betaDirectory,
                System.IO.Path.GetFileName(
                    typeof(BetaExports).Assembly.Location)));
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            contextPath,
            typeof(MultiAssemblyContext).FullName!,
            [alphaDirectory, betaDirectory],
            "test",
            error,
            out var roots);

        Assert.True(success, error.ToString());
        Assert.Equal(3, roots.Length);
    }

    [Fact]
    public void ContextRejectsAmbiguousAssemblyIdentity()
    {
        using var directory = new TemporaryDirectory();
        string contextPath = directory.CopyAssembly(
            typeof(MultiAssemblyContext).Assembly.Location);
        string firstDirectory = directory.CreateSubdirectory("first");
        string secondDirectory = directory.CreateSubdirectory("second");
        string alphaFileName = System.IO.Path.GetFileName(
            typeof(AlphaExports).Assembly.Location);
        File.Copy(
            typeof(AlphaExports).Assembly.Location,
            System.IO.Path.Combine(firstDirectory, alphaFileName));
        File.Copy(
            typeof(AlphaExports).Assembly.Location,
            System.IO.Path.Combine(secondDirectory, alphaFileName));
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            contextPath,
            typeof(MultiAssemblyContext).FullName!,
            [firstDirectory, secondDirectory],
            "test",
            error,
            out var roots);

        Assert.False(success);
        Assert.Empty(roots);
        Assert.Contains(
            "rooted assembly resolution is ambiguous",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextRejectsCaseDistinctCandidatePathsOnCaseSensitiveHost()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TemporaryDirectory();
        string upperDirectory = directory.CreateSubdirectory("A");
        string lowerDirectory = System.IO.Path.Combine(directory.Path, "a");
        if (Directory.Exists(lowerDirectory))
            return;
        Directory.CreateDirectory(lowerDirectory);

        string contextPath = directory.CopyAssembly(
            typeof(MultiAssemblyContext).Assembly.Location);
        string alphaFileName = System.IO.Path.GetFileName(
            typeof(AlphaExports).Assembly.Location);
        string upperAlpha = System.IO.Path.Combine(
            upperDirectory,
            alphaFileName);
        string lowerAlpha = System.IO.Path.Combine(
            lowerDirectory,
            alphaFileName);
        File.Copy(typeof(AlphaExports).Assembly.Location, upperAlpha);
        File.Copy(typeof(AlphaExports).Assembly.Location, lowerAlpha);
        string betaPath = System.IO.Path.Combine(
            directory.Path,
            System.IO.Path.GetFileName(
                typeof(BetaExports).Assembly.Location));
        File.Copy(typeof(BetaExports).Assembly.Location, betaPath);
        var error = new StringWriter();

        bool success = JsExportContextLoader.TryResolve(
            contextPath,
            typeof(MultiAssemblyContext).FullName!,
            [upperAlpha, lowerAlpha, betaPath],
            "test",
            error,
            out var roots);

        Assert.False(success);
        Assert.Empty(roots);
        Assert.Contains(
            "rooted assembly resolution is ambiguous",
            error.ToString(),
            StringComparison.Ordinal);
    }

    sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                $"ts-jsexport-context-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateSubdirectory(string name) =>
            Directory.CreateDirectory(
                System.IO.Path.Combine(Path, name)).FullName;

        public string CopyAssembly(string sourcePath)
        {
            string destination = System.IO.Path.Combine(
                Path,
                System.IO.Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destination);
            return destination;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
