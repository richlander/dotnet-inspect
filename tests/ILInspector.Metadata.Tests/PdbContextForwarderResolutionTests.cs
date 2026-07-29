namespace ILInspector.Metadata.Tests;

/// <summary>
/// Artifact canary for the type-forwarder resolution sink. A forwarder target is a name read from
/// the inspected assembly's metadata, so it is untrusted, and it becomes a path component. This
/// plants a real, readable payload exactly where a traversing name would land: on unguarded code
/// the resolution succeeds and hands back a path outside the forwarding assembly's directory,
/// which then feeds symbol acquisition and its network fetches.
/// </summary>
public class PdbContextForwarderResolutionTests
{
    [Theory]
    [InlineData("../payload")]
    [InlineData("..\\payload")]
    [InlineData("CON")]
    [InlineData("COM\u2074")]
    [InlineData("payload\u200b")]
    public void ForwarderTarget_WithUnsafeName_IsNotResolved(string hostileName)
    {
        var root = Directory.CreateTempSubdirectory("di-forwarder-");
        try
        {
            var assemblyDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app")).FullName;

            var realAssembly = typeof(PdbContextForwarderResolutionTests).Assembly.Location;
            Assert.True(File.Exists(realAssembly));

            // The payload sits one level above, where "../payload" lands...
            File.Copy(realAssembly, Path.Combine(root.FullName, "payload.dll"), overwrite: true);

            // ...and every hostile spelling also gets a file at its literal name inside the
            // directory, so a refusal cannot be mistaken for the file merely being absent.
            var literal = Path.Combine(assemblyDir, hostileName + ".dll");
            var literalDir = Path.GetDirectoryName(literal);
            if (literalDir != null && Directory.Exists(literalDir) && !hostileName.Contains(".."))
                File.Copy(realAssembly, literal, overwrite: true);

            Assert.Null(PdbContext.ResolveForwardedAssemblyPath(assemblyDir, hostileName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // Positive control: the guard must refuse traversal specifically, not disable local resolution.
    [Fact]
    public void ForwarderTarget_WithLegitimateName_ResolvesInTheAssemblyDirectory()
    {
        var root = Directory.CreateTempSubdirectory("di-forwarder-ok-");
        try
        {
            var assemblyDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app")).FullName;
            var realAssembly = typeof(PdbContextForwarderResolutionTests).Assembly.Location;
            var expected = Path.Combine(assemblyDir, "Legit.Neighbor.dll");
            File.Copy(realAssembly, expected, overwrite: true);

            Assert.Equal(expected, PdbContext.ResolveForwardedAssemblyPath(assemblyDir, "Legit.Neighbor"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ForwarderTarget_ThatDoesNotExist_IsNull()
    {
        var root = Directory.CreateTempSubdirectory("di-forwarder-missing-");
        try
        {
            Assert.Null(PdbContext.ResolveForwardedAssemblyPath(root.FullName, "Absent.Assembly"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
