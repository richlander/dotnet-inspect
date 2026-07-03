using System.Diagnostics;
using System.IO;
using System.Linq;
using DotnetInspector.Output;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Xunit;

namespace DotnetInspector.Tests;

public class ApiOutputFormatterTests
{
    [Fact]
    public void SameType_MatchesNonNestedTypeWithPlusInName()
    {
        // 1. Create a non-nested type with '+' in its name
        string il = @"
.assembly extern mscorlib { }
.assembly 'PlusType' { }
.module 'PlusType.dll'

.class public auto ansi beforefieldinit 'A+B'
       extends [mscorlib]System.Object
{
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    .maxstack  8
    ldarg.0
    call       instance void [mscorlib]System.Object::.ctor()
    ret
  }
}

.class public auto ansi beforefieldinit A
       extends [mscorlib]System.Object
{
  .class nested public auto ansi beforefieldinit B
         extends [mscorlib]System.Object
  {
    .method public hidebysig specialname rtspecialname 
            instance void  .ctor() cil managed
    {
      .maxstack  8
      ldarg.0
      call       instance void [mscorlib]System.Object::.ctor()
      ret
    }
  }
}
";
        string dir = Path.GetTempPath();
        string ilPath = Path.Combine(dir, "PlusType.il");
        string dllPath = Path.Combine(dir, "PlusType.dll");
        File.WriteAllText(ilPath, il);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ilasm",
                ArgumentList = { ilPath, "-dll", $"-output={dllPath}", "-quiet" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process == null) 
            {
                Assert.Skip("ilasm not available");
                return;
            }
            process.WaitForExit(30000);
            if (process.ExitCode != 0) 
            {
                Assert.Skip("ilasm execution failed");
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Skip("ilasm not found in PATH");
            return;
        }

        try
        {

            // Load ApiSurface
            using var stream = File.OpenRead(dllPath);
            using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
            var surface = ApiSurfaceExtractor.Extract(peReader);
            Assert.NotNull(surface);

            var apiTypeLiteral = surface.Types.FirstOrDefault(t => t.Name == "A+B" && t.Namespace == "");
            Assert.NotNull(apiTypeLiteral);
            Assert.Equal("A+B", apiTypeLiteral.MetadataName);

            var apiTypeNested = surface.Types.FirstOrDefault(t => t.Name == "A.B" && t.Namespace == "");
            Assert.NotNull(apiTypeNested);
            Assert.Equal("A+B", apiTypeNested.MetadataName);

            // Create TypeRefs
            var typeRefLiteral = TypeRef.Definition("PlusType", "", "A+B");
            var typeRefNested = TypeRef.Definition("PlusType", "", "A+B");

            // SameType should match literal against literal ApiType
            Assert.True(ApiOutputFormatter.SameType(typeRefLiteral, apiTypeLiteral));
            
            // Nested and literal TypeRefs are identical because TypeRef does not store IsNested.
            // But they should both match their corresponding ApiTypes thanks to MetadataName.
            Assert.True(ApiOutputFormatter.SameType(typeRefNested, apiTypeNested));
        }
        finally
        {
            if (File.Exists(ilPath)) File.Delete(ilPath);
            if (File.Exists(dllPath)) File.Delete(dllPath);
        }
    }
}
