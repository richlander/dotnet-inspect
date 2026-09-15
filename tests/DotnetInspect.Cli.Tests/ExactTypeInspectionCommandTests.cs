using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ExactTypeInspectionCommandTests
{
    const string PackageId = "System.Text.Json";
    const string Version = "10.0.0";
    const string Framework = "net10.0";

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PinnedPackageExactType_NamespacePrefixFallsBackToListing()
    {
        TypeOptions options = await Parsers.TypeOptionsParserTests.ParseSuccessAsync(
            "type", "System.Text.Json.Serialization",
            "--package", $"{PackageId}@{Version}", "--tfm", Framework);
        options = options with { TipLevel = TipLevel.Quiet };
        Assert.True(CliExactTypeInspection.IsEligible(options));

        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Showing best-effort prefix matches", result.Error, StringComparison.Ordinal);
        Assert.Contains("JsonConverter", result.Output, StringComparison.Ordinal);
        Assert.Contains("JsonIgnoreAttribute", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PinnedPackageExactType_SelectedConstraintFailuresRemainVisible()
    {
        const string package = "Exact.Type.ConstraintEvidence";
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            package, Version, NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
            new MemoryStream(ConstraintPackage()), TestContext.Current.CancellationToken);
        using var http = new HttpClient(new NoNetworkHandler());
        var loadOptions = new WorkspaceContextLoadOptions
        {
            HttpClient = http,
            SourceAuthorization = new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
            PackageStore = store,
        };
        TypeOptions options = Options() with
        {
            PackagePath = $"{package}@{Version}",
            TypeName = "N.Selected`1",
            OriginalTypeQuery = "N.Selected`1",
        };
        var envelope = await CliExactTypeInspection.ExecuteAsync(
            options, loadOptions, TestContext.Current.CancellationToken);
        var available = Assert.IsType<ExactTypeInspectionResult.Available>(envelope.Content);
        ApiSurface surface = CliExactTypeInspection.AdaptAvailable(options, available).Loaded.Api;
        Assert.NotEmpty(surface.ConstraintResolutionFailuresBySubject);
        Assert.All(surface.ConstraintResolutionFailuresBySubject.Keys,
            subject => Assert.Null(subject.SourceAssemblyPath));
        Assert.Contains(surface.ConstraintResolutionFailuresBySubject.Keys,
            subject => subject.SubjectToken == available.Type.MetadataToken);
        Assert.Contains(surface.ConstraintResolutionFailuresBySubject.Keys,
            subject => subject.SubjectToken == available.Type.Members.Single(member => member.Name == "SelectedMethod").MetadataToken);

        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteExactAsync(options, loadOptions));
        Assert.Contains("Generic-constraint classification was incomplete", result.Error, StringComparison.Ordinal);
        Assert.Contains("Missing.Constraint.Dependency", result.Error, StringComparison.Ordinal);
        Assert.Contains($"0x{available.Type.MetadataToken:X8}", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PinnedPackageExactType_PreservesDefaultOutput()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
            new MemoryStream(Package()),
            TestContext.Current.CancellationToken);
        using var http = new HttpClient(new NoNetworkHandler());
        TypeOptions options =
            await Parsers.TypeOptionsParserTests.ParseSuccessAsync(
                "type", "JsonSerializer",
                "--package", $"{PackageId}@{Version}",
                "--tfm", Framework);
        Assert.Equal(Verbosity.Minimal, options.Verbosity);
        Assert.False(options.DocsExplicitlySet);
        options = options with { TipLevel = TipLevel.Quiet };

        Assert.True(CliExactTypeInspection.IsEligible(options));
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteExactAsync(
                options,
                new WorkspaceContextLoadOptions
                {
                    HttpClient = http,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization(
                            [PackageSource.NuGetOrg]),
                    PackageStore = store,
                    UseVersionCache = false,
                    IncludePackageRootBindings = true,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal(
            """
            static class System.Text.Json.JsonSerializer (System.Text.Json 10.0.0)
            ├─ Inherits
            │  └─ System.Object
            ├─ Properties (1)
            │  └─ bool IsReflectionEnabledByDefault { get; }
            └─ Methods (9 logical, 103 overloads)
               ├─ Deserialize (40 overloads)
               ├─ DeserializeAsync (10 overloads)
               ├─ DeserializeAsyncEnumerable (8 overloads)
               ├─ Serialize (15 overloads)
               ├─ SerializeAsync (10 overloads)
               ├─ SerializeToDocument (5 overloads)
               ├─ SerializeToElement (5 overloads)
               ├─ SerializeToNode (5 overloads)
               └─ SerializeToUtf8Bytes (5 overloads)

            """,
            result.Output);
    }

    [Theory]
    [InlineData(
        "JsonConverter",
        "net10.0",
        "abstract class System.Text.Json.Serialization.JsonConverter")]
    [InlineData(
        "JsonSerializer",
        "NET10.0",
        "static class System.Text.Json.JsonSerializer")]
    public async Task PinnedPackageExactType_PreservesNormalizedExactSelection(
        string typeName,
        string framework,
        string expectedDeclaration)
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
            new MemoryStream(Package()),
            TestContext.Current.CancellationToken);
        using var http = new HttpClient(new NoNetworkHandler());
        TypeOptions options =
            await Parsers.TypeOptionsParserTests.ParseSuccessAsync(
                "type", typeName,
                "--package", $"{PackageId}@{Version}",
                "--tfm", framework);
        options = options with { TipLevel = TipLevel.Quiet };

        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteExactAsync(
                options,
                new WorkspaceContextLoadOptions
                {
                    HttpClient = http,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization(
                            [PackageSource.NuGetOrg]),
                    PackageStore = store,
                    UseVersionCache = false,
                    IncludePackageRootBindings = true,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Contains(
            expectedDeclaration,
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PinnedPackageExactType_PresentsAssemblyIdentityWithoutInventingPath()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
            new MemoryStream(Package()),
            TestContext.Current.CancellationToken);
        using var http = new HttpClient(new NoNetworkHandler());
        var (preamble, error) = ApiCommand.RunPreamble(Options() with
        {
            PackagePath = $"{PackageId}@10.0",
            Select = [SectionNames.TypeInfo],
        });
        Assert.Null(error);
        TypeOptions options = Assert.IsType<TypeOptions>(preamble.Options);

        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteExactAsync(
                options,
                new WorkspaceContextLoadOptions
                {
                    HttpClient = http,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization(
                            [PackageSource.NuGetOrg]),
                    PackageStore = store,
                    UseVersionCache = false,
                    IncludePackageRootBindings = true,
                }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Contains(
            "System.Text.Json",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("NuGet", result.Output, StringComparison.Ordinal);
        Assert.Contains("10.0.0", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Path.GetFullPath("System.Text.Json.dll"),
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("lib/net10.0", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultRoute_UsesOnlyExplicitWorkspaceContent()
    {
        const string inMemoryPackage = "Exact.Type.Route.OnlyInMemory";
        var options = await Parsers.TypeOptionsParserTests.ParseSuccessAsync(
            "type", "JsonSerializer",
            "--package", $"{inMemoryPackage}@{Version}",
            "--tfm", Framework);
        options = options with { TipLevel = TipLevel.Quiet };
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            inMemoryPackage, Version,
            NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url),
            new MemoryStream(Package()),
            TestContext.Current.CancellationToken);
        using var http = new HttpClient(new NoNetworkHandler());
        Assert.False(File.Exists("System.Text.Json"));
        Assert.False(Directory.Exists($"{inMemoryPackage}@{Version}"));
        var result = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteExactAsync(options, new WorkspaceContextLoadOptions
            {
                HttpClient = http,
                SourceAuthorization = new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
                PackageStore = store,
                UseVersionCache = false,
                IncludePackageRootBindings = true,
            }));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"System.Text.Json.JsonSerializer ({inMemoryPackage} {Version})",
            result.Output, StringComparison.Ordinal);
        Assert.Contains("SerializeToUtf8Bytes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Eligibility_LeavesExcludedRoutesOnCompatibilityPath()
    {
        TypeOptions eligible = Options();
        Assert.True(CliExactTypeInspection.IsEligible(eligible));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { TypeName = null }));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { TypeName = "*Serializer*" }));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { PackagePath = PackageId }));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { Tfm = null }));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { ProjectPath = "app.csproj" }));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { PlatformAssembly = "System.Text.Json" }));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { PdbPath = "library.pdb" }));
        Assert.False(CliExactTypeInspection.IsEligible(eligible with { ShowDocs = true, DocsExplicitlySet = true }));
        Assert.True(CliExactTypeInspection.IsEligible(eligible with
        {
            Verbosity = Verbosity.Normal,
            DocsExplicitlySet = true,
            ShowDocs = false,
        }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    Tfm = "all",
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    AssemblyPath = "library.dll",
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    Verbosity = Verbosity.Normal,
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    IncludeSections = ["Decompiled Source"],
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    IncludeSections = [SectionNames.CalledTypes],
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    IncludeSections = [SectionNames.AllocationFacts],
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    IncludeSections = [SectionNames.UnsafeMembers],
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    IncludeSections = [SectionNames.TopLeverage],
                }));
        Assert.False(
            CliExactTypeInspection.IsEligible(
                eligible with
                {
                    IncludeSections = [SectionNames.PerformanceTriage],
                }));
    }

    static TypeOptions Options() =>
        new()
        {
            TypeName = "JsonSerializer",
            OriginalTypeQuery = "JsonSerializer",
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TipLevel = TipLevel.Quiet,
        };

    static byte[] Package()
    {
        string assembly = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "ExactType",
            "System.Text.Json.dll");
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            ZipArchiveEntry entry =
                archive.CreateEntry(
                    $"lib/{Framework}/System.Text.Json.dll",
                    CompressionLevel.NoCompression);
            using Stream destination = entry.Open();
            using FileStream source = File.OpenRead(assembly);
            source.CopyTo(destination);
        }
        return output.ToArray();
    }

    static byte[] ConstraintPackage()
    {
        var dependency = new PersistedAssemblyBuilder(
            new AssemblyName("Missing.Constraint.Dependency"), typeof(object).Assembly);
        Type constraint = dependency.DefineDynamicModule("Dependency")
            .DefineType("Missing.Constraint", TypeAttributes.Public).CreateType()!;
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Constraint.Subjects"), typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("Constraint.Subjects");
        foreach (string name in new[] { "Earlier", "Selected" })
        {
            TypeBuilder type = module.DefineType("N." + name + "`1", TypeAttributes.Public);
            type.DefineGenericParameters("T")[0].SetBaseTypeConstraint(constraint);
            MethodBuilder method = type.DefineMethod(
                name + "Method", MethodAttributes.Public | MethodAttributes.Static);
            method.DefineGenericParameters("U")[0].SetBaseTypeConstraint(constraint);
            method.SetReturnType(typeof(void));
            method.GetILGenerator().Emit(OpCodes.Ret);
            type.CreateType();
        }
        using var image = new MemoryStream();
        assembly.Save(image);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry($"ref/{Framework}/Constraint.Subjects.dll").Open();
            image.Position = 0;
            image.CopyTo(entry);
        }
        return output.ToArray();
    }

    sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
