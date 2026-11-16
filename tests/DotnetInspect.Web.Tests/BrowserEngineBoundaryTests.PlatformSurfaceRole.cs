using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspect.Web.Interop.Metadata;
using DotnetInspect.Web.Interop.Package;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGetFetch;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// Gates the Platform surface role owned by
/// <c>docs/design/inspect-web-platform-scope-roles.md</c>: the exact Platform
/// scope pairs each runtime-pack assembly with its same-named reference-pack
/// assembly, surface views read the reference, and implementation views keep
/// reading the runtime assembly.
/// </summary>
public sealed partial class BrowserEngineBoundaryTests
{
    const string PlatformRoleAssembly = "Contoso.PlatformRoles";
    const string PlatformRolePrivateAssembly = "Contoso.PlatformRoles.Private";

    // The reference, compiled under the updated memory-safety rules, declares
    // the caller-unsafe contract and a Type the runtime assembly forwards
    // elsewhere; it omits the runtime-only members.
    const string PlatformRoleReference = """
        [assembly: System.Reflection.AssemblyVersion("11.0.0.0")]
        namespace Contoso.PlatformRoles
        {
            public static class Shared
            {
                /// <summary>Reads a value from the reference surface.</summary>
                public static unsafe int Read(int value) => throw null!;
            }
            public sealed class ReferenceOnly { }
        }
        """;

    // The runtime assembly carries no markers, as .NET 11 runtime packs do,
    // and declares members and Types the reference omits. Its assembly
    // version differs from the reference's, because roles pair by name. It
    // forwards the reference's ReferenceOnly, and a Type the reference does
    // not declare, to the private assembly.
    const string PlatformRoleImplementation = """
        [assembly: System.Reflection.AssemblyVersion("11.0.0.1")]
        [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Contoso.PlatformRoles.ReferenceOnly))]
        [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Contoso.PlatformRoles.ForwardedOnly))]
        namespace Contoso.PlatformRoles
        {
            public static class Shared
            {
                public static int Read(int value) => value + 1;
                public static int RuntimeOnly() => 42;
            }
            internal sealed class ImplementationOnly { }
        }
        """;

    const string PlatformRolePrivate = """
        [assembly: System.Reflection.AssemblyVersion("11.0.0.0")]
        namespace Contoso.PlatformRoles.Private
        {
            internal sealed class PrivateDetail { }
            public static class Helper { public static int Value() => 1; }
        }
        namespace Contoso.PlatformRoles
        {
            public sealed class ReferenceOnly { }
            public sealed class ForwardedOnly { }
        }
        """;

    [Fact]
    public async Task PlatformWorkspace_ExactScopeReadsReferenceSurface()
    {
        const string framework = "net11.0";
        const string version = "11.0.7412";
        const string pack = "netcore.app";
        string assemblyFile = PlatformRoleAssembly + ".dll";
        string privateFile = PlatformRolePrivateAssembly + ".dll";
        byte[] privateImplementation = CompilePlatformRoleFixture(
            PlatformRolePrivateAssembly,
            PlatformRolePrivate);
        byte[] implementation = CompilePlatformRoleFixture(
            PlatformRoleAssembly,
            PlatformRoleImplementation,
            references: [privateImplementation]);
        using var referenceDocumentation = new MemoryStream();
        byte[] reference = CompilePlatformRoleFixture(
            PlatformRoleAssembly,
            PlatformRoleReference,
            updatedMemorySafetyRules: true,
            documentation: referenceDocumentation);
        var handler = new ExactPlatformRangeHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] = PlatformPackage(
                    (assemblyFile, implementation),
                    (privateFile, privateImplementation)),
                ["microsoft.netcore.app.ref"] = PackageEntries(
                    ($"ref/{framework}/{assemblyFile}", reference),
                    ($"ref/{framework}/{PlatformRoleAssembly}.xml",
                        referenceDocumentation.ToArray())),
            });
        using IPackageSourceClient packageClient =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    OperationTimeout = TimeSpan.FromSeconds(30),
                });
        using var client = new HttpClient(handler, disposeHandler: false);
        IPackageSourceAuthorization authorization =
            BrowserPackageWorkspace.SourceAuthorizationFor(packageClient);

        BrowserPlatformScope? scope = null;
        try
        {
            await using (BrowserPlatformScopeResolution exact = await OpenAsync(assemblyFile))
            {
                scope = exact.Scope;
                Assert.True(exact.Scope.ExactPackageRealization);
                Assert.True(exact.Scope.HasReferenceSurface(exact.Participant));
                Assert.NotSame(exact.Participant, exact.SurfaceParticipant);
                Assert.True(handler.PackageRequestsFor("microsoft.netcore.app.ref") > 0);
            }

            // The Type list is the reference surface.
            BrowserPackageSurface surface = Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePackAssembly(
                        framework, version, assemblyFile, pack, assemblyFile),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
            string[] listed = [.. surface.Types.Select(type => type.Name)];
            Assert.Contains("ReferenceOnly", listed);
            Assert.Contains("Shared", listed);
            Assert.DoesNotContain("ImplementationOnly", listed);

            // The runtime forwarder of a Type the reference declares has that
            // Type's id, so a host lists the Type once, as its forwarder; a
            // forwarder the reference omits has an id no listed Type has.
            using (var navigation = new BrowserPlatformForwarderNavigation(
                client, packageClient, authorization, TimeSpan.FromSeconds(30)))
            {
                BrowserPlatformForwarderNavigationResult opened = await navigation.OpenAsync(
                    framework, version, assemblyFile, pack,
                    TestContext.Current.CancellationToken);
                BrowserPlatformForwarderView view = Assert.IsType<BrowserPlatformForwarderView>(
                    PackageExports.ProjectForwarderResult(opened).View);
                HashSet<string> listedIds = [.. surface.Types.Select(type => type.Id)];
                Assert.Equal(
                    [
                        $"{PlatformRoleAssembly}:Contoso.PlatformRoles.ForwardedOnly",
                        $"{PlatformRoleAssembly}:Contoso.PlatformRoles.ReferenceOnly",
                    ],
                    view.Forwarders.Select(row => row.Id).Order(StringComparer.Ordinal));
                Assert.Equal(
                    [$"{PlatformRoleAssembly}:Contoso.PlatformRoles.ReferenceOnly"],
                    view.Forwarders.Select(row => row.Id).Where(listedIds.Contains));
                BrowserPlatformForwarderRow forwarded = Assert.Single(
                    view.Forwarders,
                    row => row.Id == $"{PlatformRoleAssembly}:Contoso.PlatformRoles.ReferenceOnly");
                Assert.Equal(PlatformRolePrivateAssembly, forwarded.TargetAssembly);
                Assert.NotNull(forwarded.Action);

                // Activation still follows the runtime ExportedType evidence
                // to the defining Type.
                BrowserPlatformForwarderView destination = Assert.IsType<BrowserPlatformForwarderView>(
                    PackageExports.ProjectForwarderResult(
                        await navigation.ActivateAsync(
                            forwarded.Action!, TestContext.Current.CancellationToken)).View);
                Assert.Equal(
                    $"{PlatformRolePrivateAssembly}:Contoso.PlatformRoles.ReferenceOnly",
                    destination.SelectedTypeId);
            }

            // A surface-declared member renders its reference caller-unsafe
            // contract; a member the surface omits renders from the
            // implementation.
            ApiType sharedReference;
            ApiType sharedImplementation;
            await using (BrowserPlatformScopeResolution resolution = await OpenAsync(assemblyFile))
            {
                sharedReference = Assert.Single(
                    resolution.Scope.UseSurfaceParticipant(
                        resolution.SurfaceParticipant,
                        BrowserMemberResolution.ImplementationSurface).Types,
                    type => type.Name == "Shared");
                sharedImplementation = Assert.Single(
                    resolution.Scope.UseParticipant(
                        resolution.Participant,
                        BrowserMemberResolution.ImplementationSurface).Types,
                    type => type.Name == "Shared");
            }
            ApiMember readReference = Assert.Single(
                sharedReference.Members, member => member.Name == "Read");
            string read = Assert.IsType<string>(
                (await Declaration(sharedReference, readReference)).Text);
            Assert.Contains("unsafe", read, StringComparison.Ordinal);
            ApiMember runtimeOnly = Assert.Single(
                sharedImplementation.Members, member => member.Name == "RuntimeOnly");
            Assert.DoesNotContain(
                sharedReference.Members, member => member.Name == "RuntimeOnly");
            string runtimeOnlyText = Assert.IsType<string>(
                (await Declaration(sharedImplementation, runtimeOnly)).Text);
            Assert.Contains("RuntimeOnly", runtimeOnlyText, StringComparison.Ordinal);
            Assert.DoesNotContain("unsafe", runtimeOnlyText, StringComparison.Ordinal);

            // Documentation comes from the surface, requested by the reference
            // identity even though it differs from the runtime identity.
            var documentation = Assert.IsType<DotnetInspector.Queries.CompiledDocumentationOutcome.Available>(
                await BrowserPlatformWorkspace.QueryMemberDocumentationAsync(
                    framework,
                    version,
                    assemblyFile,
                    pack,
                    "M:Contoso.PlatformRoles.Shared.Read(System.Int32)",
                    client,
                    packageClient,
                    authorization,
                    TimeSpan.FromSeconds(30),
                    TestContext.Current.CancellationToken));
            Assert.Equal(
                "Reads a value from the reference surface.",
                documentation.Documentation.Summary);

            // An implementation view accepts the surface row's identity and
            // reference metadata token, and selects the runtime body.
            await using (BrowserMemberResolution.ScopedPlatformResolution body =
                await BrowserMemberResolution.PlatformImplementationMemberAsync(
                    framework,
                    version,
                    assemblyFile,
                    pack,
                    sharedReference.DefinitionName!.ToEscapedFullName(),
                    readReference.Name,
                    Analysis.CallGraphMemberResolver
                        .CreateSelector(sharedReference, readReference).Key,
                    readReference.DeclarationMetadataToken
                        ?? readReference.MetadataToken
                        ?? 0,
                    cancellationToken: TestContext.Current.CancellationToken))
            {
                Assert.Equal("Read", body.Member.Member.Name);
                Assert.True(body.Member.BodyToken != 0);
            }

            // An assembly with no reference counterpart is its own surface,
            // and accumulation re-realizes both roles of earlier selections.
            await using (BrowserPlatformScopeResolution accumulated = await OpenAsync(privateFile))
            {
                scope = accumulated.Scope;
                Assert.Equal(2, accumulated.Scope.Members.Length);
                Assert.False(accumulated.Scope.HasReferenceSurface(accumulated.Participant));
                Assert.Same(accumulated.Participant, accumulated.SurfaceParticipant);
                Assert.True(accumulated.Scope.HasReferenceSurface(
                    accumulated.Scope.Participant(
                        BrowserPlatformWorkspace.RuntimeFamily,
                        PlatformRoleAssembly)));
            }
            BrowserPackageSurface privateSurface = Assert.IsType<BrowserPackageSurface>(
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePackAssembly(
                        framework, version, privateFile, pack, privateFile),
                    BrowserPackageJsonContext.Default.BrowserPackageSurface));
            Assert.Contains(privateSurface.Types, type => type.Name == "PrivateDetail");
        }
        finally
        {
            if (scope is not null)
                await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        }

        Task<BrowserPlatformScopeResolution> OpenAsync(string name) =>
            BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework, version, name, pack,
                client, packageClient, authorization,
                acquireCompletePopulation: false,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken);

        async Task<BrowserMemberDeclaration> Declaration(ApiType type, ApiMember member) =>
            JsonSerializer.Deserialize(
                await MetadataExports.QueryPlatformMemberDeclaration(
                    framework,
                    version,
                    assemblyFile,
                    pack,
                    type.DefinitionName!.ToEscapedFullName(),
                    member.Name,
                    Analysis.CallGraphMemberResolver.CreateSelector(type, member).Key,
                    member.DeclarationMetadataToken ?? member.MetadataToken ?? 0),
                BrowserMetadataJsonContext.Default.BrowserMemberDeclaration)
            ?? throw new InvalidOperationException(
                "The platform declaration export returned null.");
    }

    static byte[] CompilePlatformRoleFixture(
        string assemblyName,
        string source,
        bool updatedMemorySafetyRules = false,
        Stream? documentation = null,
        IEnumerable<byte[]>? references = null)
    {
        CSharpParseOptions parse = new CSharpParseOptions(
                LanguageVersion.Preview,
                documentation is null
                    ? DocumentationMode.None
                    : DocumentationMode.Diagnose)
            .WithFeatures(updatedMemorySafetyRules
                ? [new("updated-memory-safety-rules", "")]
                : []);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parse)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                .. (references ?? []).Select(image => MetadataReference.CreateFromImage(image)),
            ],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));
        using var image = new MemoryStream();
        EmitResult result = compilation.Emit(
            image,
            xmlDocumentationStream: documentation);
        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)));
        return image.ToArray();
    }

    // A failed reference acquisition is not an absent reference pack: the
    // Library open fails visibly instead of silently reading the runtime
    // assembly as its surface.
    [Fact]
    public async Task PlatformWorkspace_ReferenceTransportFailureStaysVisible()
    {
        const string framework = "net11.0";
        const string version = "11.0.7413";
        string assemblyFile = PlatformRoleAssembly + ".dll";
        var inner = new ExactPlatformRangeHandler(
            version,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft.netcore.app.runtime.linux-x64"] = PlatformPackage(
                    (assemblyFile, CompilePlatformRoleFixture(
                        PlatformRoleAssembly,
                        PlatformRoleImplementation,
                        references: [CompilePlatformRoleFixture(
                            PlatformRolePrivateAssembly,
                            PlatformRolePrivate)]))),
                ["microsoft.netcore.app.ref"] = PackageEntries(
                    ($"ref/{framework}/{assemblyFile}", CompilePlatformRoleFixture(
                        PlatformRoleAssembly,
                        PlatformRoleReference,
                        updatedMemorySafetyRules: true))),
            });
        using var handler = new ReferencePackTransportFailureHandler(inner);
        using IPackageSourceClient packageClient =
            BrowserPackageWorkspace.CreateGallerySource(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(30),
                    OperationTimeout = TimeSpan.FromSeconds(30),
                });
        using var client = new HttpClient(handler, disposeHandler: false);

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(
            () => BrowserPlatformWorkspace.OpenAssemblyAsync(
                framework, version, assemblyFile, "netcore.app",
                client, packageClient,
                BrowserPackageWorkspace.SourceAuthorizationFor(packageClient),
                acquireCompletePopulation: false,
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken));

        Assert.True(handler.ReferenceRequests > 0);
        Assert.Contains(
            "reference realization",
            failure.ToString(),
            StringComparison.Ordinal);
    }

    sealed class ReferencePackTransportFailureHandler(HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        int _referenceRequests;

        internal int ReferenceRequests => Volatile.Read(ref _referenceRequests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            FailReference(request);
            return base.SendAsync(request, cancellationToken);
        }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            FailReference(request);
            return base.Send(request, cancellationToken);
        }

        void FailReference(HttpRequestMessage request)
        {
            if (request.RequestUri!.AbsoluteUri.Contains(
                    "microsoft.netcore.app.ref.",
                    StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _referenceRequests);
                throw new HttpRequestException(
                    "Simulated reference-pack transport failure.");
            }
        }
    }
}
