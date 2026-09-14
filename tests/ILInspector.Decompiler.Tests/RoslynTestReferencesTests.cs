using Xunit;

namespace ILInspector.Decompiler.Tests;

// Guards the managed-only filter that keeps native / unmanaged DLLs enumerated in
// TRUSTED_PLATFORM_ASSEMBLIES out of the Roslyn reference set (issue #2942): passing
// an unmanaged PE to Roslyn fails every compile with CS0009.
public class RoslynTestReferencesTests
{
    [Fact]
    public void IsManagedAssembly_TrueForManagedAssembly()
    {
        string managed = typeof(object).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(managed));
        Assert.True(RoslynTestReferences.IsManagedAssembly(managed));
    }

    [Fact]
    public void IsManagedAssembly_FalseForNonPeFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ii-2942-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03]); // "MZ" then junk — not a valid PE
        try
        {
            Assert.False(RoslynTestReferences.IsManagedAssembly(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsManagedAssembly_FalseForMissingFile()
        => Assert.False(RoslynTestReferences.IsManagedAssembly(
            Path.Combine(Path.GetTempPath(), $"ii-2942-missing-{Guid.NewGuid():N}.dll")));

    [Fact]
    public void TrustedPlatform_IsNonEmpty()
        => Assert.NotEmpty(RoslynTestReferences.TrustedPlatform);
}
