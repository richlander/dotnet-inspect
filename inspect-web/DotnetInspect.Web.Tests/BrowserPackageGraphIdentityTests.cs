using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPackageGraphIdentityTests
{
    [Fact]
    public void PackageGraphIdentityUsesTheBoundedOrdinalFamilyPrefix()
    {
        Assert.Equal(
            [
                BrowserPackageGraphIdentityRole.Inspected,
                BrowserPackageGraphIdentityRole.SamePrefix,
                BrowserPackageGraphIdentityRole.SamePrefix,
                BrowserPackageGraphIdentityRole.External,
                BrowserPackageGraphIdentityRole.External,
            ],
            Classify(
                "Microsoft.Extensions.Hosting",
                "MICROSOFT.EXTENSIONS.HOSTING",
                "Microsoft.Extensions",
                "Microsoft.Extensions.Logging",
                "Microsoft.ExtensionsX.Logging",
                "Serilog"));
        Assert.Equal(
            [
                BrowserPackageGraphIdentityRole.Inspected,
                BrowserPackageGraphIdentityRole.SamePrefix,
            ],
            Classify("Serilog", "SERILOG", "Serilog.Sinks.Console"));
    }

    [Fact]
    public void PackageGraphIdentityUsesDotNetOrdinalCasing()
    {
        AssertOrdinalPair(
            0x212a,
            0x004b,
            BrowserPackageGraphIdentityRole.External);
        AssertOrdinalPair(
            0x017f,
            0x0053,
            BrowserPackageGraphIdentityRole.External);
        Assert.Equal(
            BrowserPackageGraphIdentityRole.SamePrefix,
            Classify("Acme.\u03A3.Root", "ACME.\u03C2.Child").Single());

        IEnumerable<(int Lower, int Upper)> greekPairs =
            new[] { 0x1f80, 0x1f90, 0x1fa0 }
                .SelectMany(start => Enumerable.Range(0, 8)
                    .Select(offset => (start + offset, start + offset + 8)))
                .Concat([(0x1fb3, 0x1fbc), (0x1fc3, 0x1fcc), (0x1ff3, 0x1ffc)]);
        foreach ((int lower, int upper) in greekPairs)
        {
            AssertOrdinalPair(
                lower,
                upper,
                BrowserPackageGraphIdentityRole.SamePrefix);
        }

        foreach (int lower in Enumerable.Range(0x16ebb, 25))
        {
            AssertOrdinalPair(
                lower,
                lower - 0x1b,
                BrowserPackageGraphIdentityRole.External);
        }
    }

    static void AssertOrdinalPair(
        int left,
        int right,
        BrowserPackageGraphIdentityRole expected)
    {
        string leftScalar = char.ConvertFromUtf32(left);
        string rightScalar = char.ConvertFromUtf32(right);
        Assert.Equal(
            expected,
            Classify($"Acme.{leftScalar}.Root", $"ACME.{rightScalar}.Child").Single());
        Assert.Equal(
            expected,
            Classify($"Acme.{rightScalar}.Root", $"ACME.{leftScalar}.Child").Single());
    }

    static BrowserPackageGraphIdentityRole[] Classify(
        string inspectedPackageId,
        params string[] packageIds)
    {
        string packageIdsJson = JsonSerializer.Serialize(
            packageIds,
            BrowserPackageJsonContext.Default.StringArray);
        string rolesJson = PackageExports.ClassifyPackageGraphIdentities(
            inspectedPackageId,
            packageIdsJson);
        return JsonSerializer.Deserialize(
            rolesJson,
            BrowserPackageJsonContext.Default.BrowserPackageGraphIdentityRoleArray)
            ?? throw new InvalidOperationException("The package graph roles are absent.");
    }
}
