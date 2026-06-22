using DotnetInspector.Core;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class PdbIdentityTests
{
    [Fact]
    public void LoadPdbFromFile_AcceptsMatchingPortablePdb()
    {
        var (assemblyCopy, tempDir) = CopyAssemblyWithoutPdb(typeof(PdbIdentityTests).Assembly.Location);
        var pdbPath = Path.ChangeExtension(typeof(PdbIdentityTests).Assembly.Location, ".pdb");
        try
        {
            Assert.True(File.Exists(pdbPath), $"Expected test PDB at {pdbPath}");
            using var context = PdbContext.Open(assemblyCopy);
            Assert.False(context.HasPdb);

            context.LoadPdbFromFile(pdbPath);

            Assert.True(context.HasPdb);
            Assert.Equal(pdbPath, context.PortablePdbPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadPdbFromFile_RejectsMismatchedPortablePdb()
    {
        var (assemblyCopy, tempDir) = CopyAssemblyWithoutPdb(typeof(PdbIdentityTests).Assembly.Location);
        var mismatchedPdb = Path.ChangeExtension(typeof(CoreCache).Assembly.Location, ".pdb");
        try
        {
            Assert.True(File.Exists(mismatchedPdb), $"Expected mismatched PDB at {mismatchedPdb}");
            using var context = PdbContext.Open(assemblyCopy);
            Assert.False(context.HasPdb);

            context.LoadPdbFromFile(mismatchedPdb);

            Assert.False(context.HasPdb);
            Assert.Null(context.PortablePdbPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static (string AssemblyCopy, string TempDir) CopyAssemblyWithoutPdb(string assemblyPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pdb-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var assemblyCopy = Path.Combine(tempDir, Path.GetFileName(assemblyPath));
        File.Copy(assemblyPath, assemblyCopy);
        return (assemblyCopy, tempDir);
    }
}
