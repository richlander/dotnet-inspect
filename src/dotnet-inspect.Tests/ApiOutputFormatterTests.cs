using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using DotnetInspector.Output;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// Locks the <see cref="ApiOutputFormatter.SameType"/> matching used to scope
/// render rows (and, since #2233, the type-targeted decode gate) to a single
/// type. The regression under test (#2238): the old predicate normalized nested
/// names by blindly replacing '+' with '.', which dropped rows for a
/// <em>non-nested</em> type whose metadata name literally contains '+'.
/// </summary>
public class ApiOutputFormatterTests
{
    const string Asm = "PlusType";

    static ApiType Type(string? ns, string name, string? metadataName)
        => new() { Namespace = ns, Name = name, MetadataName = metadataName };

    // --- SameType: deterministic unit coverage (no external tooling) ---

    [Fact]
    public void SameType_NonNestedTypeWithLiteralPlus_Matches()
    {
        // Raw IL permits '+' in a type identifier (`.class public 'A+B'`). Such a
        // type's metadata name is "A+B" on both the analysis TypeRef and the API
        // surface. The old '+'→'.' replace turned the surface name into "A.B" and
        // silently dropped the type; MetadataName restores the match.
        var apiType = Type(ns: null, name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.Definition(Asm, "", "A+B");

        Assert.True(ApiOutputFormatter.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_NestedType_StillMatches()
    {
        // A genuinely nested type: analysis TypeRef uses the metadata '+'
        // separator (Outer+Inner); the API surface display name uses '.'
        // (Outer.Inner). MetadataName carries the '+' form so they reconcile.
        var apiType = Type(ns: "N", name: "Outer.Inner", metadataName: "Outer+Inner");
        var typeRef = TypeRef.Definition(Asm, "N", "Outer+Inner");

        Assert.True(ApiOutputFormatter.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_FallbackWhenMetadataNameAbsent_UsesReplace()
    {
        // Older serialized surfaces carry no MetadataName; the predicate falls
        // back to the legacy '+'→'.' reconciliation so nested types still match.
        var apiType = Type(ns: "N", name: "Outer.Inner", metadataName: null);
        var typeRef = TypeRef.Definition(Asm, "N", "Outer+Inner");

        Assert.True(ApiOutputFormatter.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_GlobalNamespace_MatchesNullSurfaceNamespace()
    {
        // The API surface stores the global namespace as null; a TypeRef stores
        // it as "". The predicate must treat these as equivalent.
        var apiType = Type(ns: null, name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.Definition(Asm, "", "A+B");

        Assert.True(ApiOutputFormatter.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_DifferentNamespace_DoesNotMatch()
    {
        var apiType = Type(ns: "N1", name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.Definition(Asm, "N2", "A+B");

        Assert.False(ApiOutputFormatter.SameType(typeRef, apiType));
    }

    [Fact]
    public void SameType_NonDefinitionTypeRef_DoesNotMatch()
    {
        var apiType = Type(ns: null, name: "A+B", metadataName: "A+B");
        var typeRef = TypeRef.SzArray(TypeRef.Definition(Asm, "", "A+B"));

        Assert.False(ApiOutputFormatter.SameType(typeRef, apiType));
    }

    // --- Extraction: MetadataName reconstruction from real metadata (no ilasm) ---

    [Fact]
    public void Extract_NestedType_PopulatesMetadataNameWithPlusSeparator()
    {
        var assemblyPath = typeof(ApiOutputFormatterTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, typesOnly: true);

        var outer = surface.Types.Single(t => t.Name == nameof(PlusFixtureOuter));
        Assert.Equal(nameof(PlusFixtureOuter), outer.MetadataName);

        // Nested public types are surfaced with a '.' display name but a '+'
        // metadata name, mirroring how the analysis TypeRef spells them.
        var inner = surface.Types.Single(t => t.Name == "PlusFixtureOuter.Inner");
        Assert.Equal("PlusFixtureOuter+Inner", inner.MetadataName);

        // The reconstructed metadata name is exactly what a TypeRef would carry,
        // so SameType reconciles the two without any string surgery.
        var typeRef = TypeRef.Definition(Asm, inner.Namespace ?? "", "PlusFixtureOuter+Inner");
        Assert.True(ApiOutputFormatter.SameType(typeRef, inner));
    }

    // --- Extraction: non-nested type with a literal '+' (requires ilasm) ---

    [Fact]
    public void Extract_NonNestedTypeWithLiteralPlus_MatchesTypeRef()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"plus-type-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string ilPath = Path.Combine(dir, "PlusType.il");
        string dllPath = Path.Combine(dir, "PlusType.dll");

        const string il = """
            .assembly extern mscorlib { }
            .assembly 'PlusType' { }
            .module 'PlusType.dll'

            .class public auto ansi beforefieldinit 'A+B'
                   extends [mscorlib]System.Object
            {
              .method public hidebysig specialname rtspecialname
                      instance void .ctor() cil managed
              {
                .maxstack 8
                ldarg.0
                call instance void [mscorlib]System.Object::.ctor()
                ret
              }
            }
            """;

        try
        {
            File.WriteAllText(ilPath, il);
            if (!TryAssemble(ilPath, dllPath) || !File.Exists(dllPath))
            {
                Assert.Skip("ilasm not available or failed to assemble the fixture");
                return;
            }

            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            var surface = ApiSurfaceExtractor.Extract(peReader, typesOnly: true);

            // The literal '+' must survive extraction unmangled in both the
            // display name and the metadata name (the type is not nested).
            var apiType = surface.Types.Single(
                t => t.Name == "A+B" && string.IsNullOrEmpty(t.Namespace));
            Assert.Equal("A+B", apiType.MetadataName);

            // End-to-end: the row would previously be dropped because
            // "A+B".Replace('+','.') == "A.B" != "A+B". It now matches.
            var typeRef = TypeRef.Definition(Asm, "", "A+B");
            Assert.True(ApiOutputFormatter.SameType(typeRef, apiType));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    static bool TryAssemble(string ilPath, string dllPath)
    {
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
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ilasm not found on PATH.
            return false;
        }
    }
}

/// <summary>Fixture with a public nested type used to exercise metadata-name reconstruction.</summary>
public class PlusFixtureOuter
{
    public class Inner { }
}
