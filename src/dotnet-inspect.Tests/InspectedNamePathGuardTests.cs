using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Two sinks that take a name out of an inspected artifact and join it onto a directory:
/// a type-forwarder target (from the assembly's metadata) and a RID package id (from the
/// package's <c>DotnetToolSettings.xml</c>). Each case is paired with a positive control that
/// plants the same payload at the legitimate location, so a refusal cannot pass by accident --
/// if the guard is removed, the hostile case resolves the planted payload exactly as the
/// control does.
/// </summary>
public class InspectedNamePathGuardTests
{
    /// <summary>A real managed assembly to plant, and a public type it actually defines.</summary>
    private static string PayloadSourceAssembly => typeof(ApiSurface).Assembly.Location;

    private const string PayloadTypeName = "ILInspector.Metadata.ApiSurface";

    private static ApiSurface SurfaceForwardingTo(string targetAssembly)
    {
        var api = new ApiSurface();
        api.TypeForwarders.Add(new TypeForwarder
        {
            TypeName = PayloadTypeName,
            TargetAssembly = targetAssembly
        });
        return api;
    }

    [Fact]
    public void ResolveForwardedTypes_WithTraversingTargetAssembly_DoesNotReadOutsideTheAssemblyDirectory()
    {
        var root = Directory.CreateTempSubdirectory("fwd-guard-");
        try
        {
            var appDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app"));
            var dllPath = Path.Combine(appDir.FullName, "main.dll");

            // Planted one level above the inspected assembly's own directory.
            File.Copy(PayloadSourceAssembly, Path.Combine(root.FullName, "payload.dll"));

            var api = SurfaceForwardingTo("../payload");
            ApiServices.ResolveForwardedTypes(api, dllPath, new VerboseLogger(false), includeAll: false);

            Assert.Empty(api.Types);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveForwardedTypes_WithOrdinaryTargetAssembly_StillResolvesTheForwardedType()
    {
        var root = Directory.CreateTempSubdirectory("fwd-control-");
        try
        {
            var appDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app"));
            var dllPath = Path.Combine(appDir.FullName, "main.dll");

            // Same payload, at the location a legitimate forwarder target names.
            File.Copy(PayloadSourceAssembly, Path.Combine(appDir.FullName, "payload.dll"));

            var api = SurfaceForwardingTo("payload");
            ApiServices.ResolveForwardedTypes(api, dllPath, new VerboseLogger(false), includeAll: false);

            Assert.Contains(api.Types, t => t.FullName == PayloadTypeName && t.IsForwarded);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static async Task<bool?> VerifyRidPackageAsync(string packageId, string localDir)
    {
        var reference = new RidPackageReference
        {
            RuntimeIdentifier = "osx-arm64",
            PackageId = packageId
        };
        var result = new InspectionResult { RuntimeIdentifierPackages = [reference] };

        using var client = new HttpClient();
        await RidPackageVerifier.VerifyAsync(client, result, "1.0.0", localDir, new VerboseLogger(false));

        return reference.Exists;
    }

    [Fact]
    public async Task VerifyAsync_WithTraversingPackageId_DoesNotReportAPackageOutsideTheLocalDirectory()
    {
        var root = Directory.CreateTempSubdirectory("rid-guard-");
        try
        {
            var localDir = Directory.CreateDirectory(Path.Combine(root.FullName, "packages"));
            File.WriteAllText(Path.Combine(root.FullName, "payload.1.0.0.nupkg"), "planted");

            Assert.NotEqual(true, await VerifyRidPackageAsync("../payload", localDir.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_WithOrdinaryPackageId_StillReportsASiblingPackage()
    {
        var root = Directory.CreateTempSubdirectory("rid-control-");
        try
        {
            var localDir = Directory.CreateDirectory(Path.Combine(root.FullName, "packages"));
            File.WriteAllText(Path.Combine(localDir.FullName, "payload.1.0.0.nupkg"), "planted");

            Assert.True(await VerifyRidPackageAsync("payload", localDir.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
