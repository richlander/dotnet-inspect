using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Round-trip tests that validate our IL disassembler output against the
/// native ILAsm/ILDasm reference tools: disassemble, reassemble, and compare.
/// Tests skip when the tools are not installed.
/// </summary>
// Resolves real platform assemblies via PlatformResolver; share "Console" so it never runs in
// parallel with the DOTNET_ROOT-mutating PlatformResolverTests (#1256).
[Collection("Console")]
public class ILDisassemblerComparisonTests
{
    static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    static readonly string CoreDll = FindAssembly("DotnetInspector.Core.dll");
    static readonly string CacheDll = FindAssembly("DotnetInspector.Cache.dll");
    static readonly string MetadataDll = FindAssembly("ILInspector.Metadata.dll");
    static readonly string TestDll = typeof(ILDisassemblerComparisonTests).Assembly.Location;

    static readonly bool HasILAsm = CanRunILAsm();

    // --- ILAsm: roundtrip validation ---

    [Theory]
    [MemberData(nameof(ILAsmAssemblyCases))]
    public void ILAsm_Roundtrip_ProducesValidAssembly(string assembly)
    {
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install them with `source eng/activate-iltools.sh`");

        var assemblyPath = ResolveAssembly(assembly);
        var outputDll = RoundtripWithILAsm(assemblyPath);
        Assert.True(File.Exists(outputDll), $"ILAsm failed to produce {outputDll}");

        using var stream = File.OpenRead(outputDll);
        using var peReader = new PEReader(stream);
        Assert.True(peReader.HasMetadata);

        var reader = peReader.GetMetadataReader();
        Assert.True(reader.TypeDefinitions.Count > 0);
    }

    [Theory]
    [MemberData(nameof(ILAsmAssemblyCases))]
    public void ILAsm_Roundtrip_MethodCountPreserved(string assembly)
    {
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install them with `source eng/activate-iltools.sh`");

        var assemblyPath = ResolveAssembly(assembly);
        int originalCount = CountMethods(assemblyPath);
        var outputDll = RoundtripWithILAsm(assemblyPath);
        int roundtripCount = CountMethods(outputDll);

        Assert.Equal(originalCount, roundtripCount);
    }

    [Theory]
    [MemberData(nameof(ILAsmMethodCases))]
    public void ILAsm_Roundtrip_OpcodesPreserved(string assembly, string typeName, string methodName)
    {
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install them with `source eng/activate-iltools.sh`");

        var assemblyPath = ResolveAssembly(assembly);
        var outputDll = RoundtripWithILAsm(assemblyPath);

        var original = DisassembleFrom(assemblyPath, typeName, methodName);
        var roundtripped = DisassembleFrom(outputDll, typeName, methodName);

        Assert.NotNull(original);
        Assert.NotNull(roundtripped);
        Assert.Equal(original.Count, roundtripped.Count);

        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].OpCodeName, roundtripped[i].OpCodeName);
            Assert.Equal(original[i].Offset, roundtripped[i].Offset);
        }
    }

    // --- Test case data (conditional on tool availability) ---

    /// <summary>Assemblies for ILAsm roundtrip validation.</summary>
    public static IEnumerable<object[]> ILAsmAssemblyCases()
    {
        yield return ["Core"];
        yield return ["Cache"];
        yield return ["Test"];
    }

    /// <summary>Methods for ILAsm roundtrip opcode comparison.</summary>
    public static IEnumerable<object[]> ILAsmMethodCases()
    {
        yield return ["Cache", "DotnetInspector.Cache.PersistentCache", "Initialize"];
        yield return ["Cache", "DotnetInspector.Cache.PersistentCache", "GetBasePath"];
        yield return ["Test", "DotnetInspect.Cli.Tests.ILSampleClass", "SwitchCase"];
        yield return ["Test", "DotnetInspect.Cli.Tests.ILSampleClass", "TryCatch"];
        yield return ["Test", "DotnetInspect.Cli.Tests.ILSampleClass", "CompareEquals"];
    }

    [Fact]
    public void ToolRunner_DrainsRedirectedStreamsConcurrently()
    {
        const string Payload =
            "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
            + "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var tempDir = Path.Combine(Path.GetTempPath(), $"tool-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string fileName;
            string[] arguments;
            if (OperatingSystem.IsWindows())
            {
                string scriptPath = Path.Combine(tempDir, "write-output.cmd");
                File.WriteAllText(
                    scriptPath,
                    $$"""
                    @echo off
                    for /L %%i in (1,1,4096) do @echo {{Payload}} 1>&2
                    echo complete
                    """);
                fileName = "cmd.exe";
                arguments = ["/d", "/s", "/c", scriptPath];
            }
            else
            {
                string scriptPath = Path.Combine(tempDir, "write-output.sh");
                File.WriteAllText(
                    scriptPath,
                    $$"""
                    i=0
                    while [ "$i" -lt 4096 ]; do
                      printf '%s\n' '{{Payload}}' >&2
                      i=$((i + 1))
                    done
                    printf '%s\n' complete
                    """);
                fileName = "/bin/sh";
                arguments = [scriptPath];
            }

            var result = RunTool(fileName, arguments, TimeSpan.FromSeconds(15));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("complete", result.StandardOutput);
            Assert.True(
                result.StandardError.Length > 64 * 1024,
                $"Expected redirected stderr to exceed pipe capacity; got {result.StandardError.Length} characters.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // --- Tool execution ---

    static string RunILDasm(string assemblyPath, string outputPath)
    {
        var result = RunTool(
            "ildasm",
            [assemblyPath, $"-output={outputPath}", "-utf8"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ildasm exited with code {result.ExitCode}: {result.StandardError}");
        }

        return result.StandardOutput;
    }

    static string RunILAsm(string ilPath, string outputDll)
    {
        var result = RunTool(
            "ilasm",
            [ilPath, "-dll", $"-output={outputDll}", "-quiet"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ilasm exited with code {result.ExitCode}: {result.StandardError}"
                + $"\nstdout: {result.StandardOutput}");
        }

        return result.StandardOutput;
    }

    static string RoundtripWithILAsm(string assemblyPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"il-roundtrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string ilPath = Path.Combine(tempDir, "output.il");
        string outputDll = Path.Combine(tempDir, "roundtripped.dll");

        RunILDasm(assemblyPath, ilPath);
        Assert.True(File.Exists(ilPath), "ildasm did not produce IL output");

        RunILAsm(ilPath, outputDll);
        return outputDll;
    }

    /// <summary>
    /// The signature-fidelity gate for <see cref="ArrayShapeText"/>: ILAsm spells a rank-1
    /// multi-dimensional array <c>int32[...]</c>, and preserves explicit sizes and lower bounds.
    /// <c>CanonicalIL</c> emits IL that is reassembled elsewhere, so collapsing any of those
    /// shapes would silently emit a different type.
    /// </summary>
    /// <remarks>
    /// Comparing the rendered text against itself would be circular, so this reassembles what
    /// <c>CanonicalIL</c> spelled and requires the resulting signature blob to be byte-identical
    /// to the one ILAsm produced from the original source.
    /// </remarks>
    [Fact]
    public void CanonicalIL_ArraySpellings_ReassembleToTheSameSignature()
    {
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install them with `source eng/activate-iltools.sh`");

        string[] sourceSpellings =
        [
            "int32[...]",
            "int32[]",
            "int32[,]",
            "int32[0...,0...]",
            "int32[0...,]",
            "int32[0,6]",
            "int32[-2...,6]",
            "int32[6,-2...3]",
        ];
        var tempDir = Path.Combine(Path.GetTempPath(), $"array-shape-fidelity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var (originalBlobs, rendered) = AssembleAndRender(sourceSpellings, tempDir, "original");

            Assert.Equal(sourceSpellings, rendered);
            Assert.Equal(
                sourceSpellings.Length,
                rendered.Distinct(StringComparer.Ordinal).Count());

            // Reassembling what CanonicalIL spelled must reproduce the exact signatures.
            var (roundtrippedBlobs, _) = AssembleAndRender(rendered, tempDir, "roundtripped");

            for (int i = 0; i < sourceSpellings.Length; i++)
            {
                Assert.Equal(originalBlobs[i], roundtrippedBlobs[i]);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void CanonicalIL_UnrepresentableArrayShape_IsRejectedByILAsm()
    {
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install them with `source eng/activate-iltools.sh`");

        string rendered = ArrayShapeText.Format(
            "int32",
            new ArrayShape(1, [6], []));
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"array-shape-rejection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string ilPath = Path.Combine(tempDir, "invalid.il");
            string outputDll = Path.Combine(tempDir, "invalid.dll");
            File.WriteAllText(
                ilPath,
                $$"""
                .assembly invalid { }
                .class public Shapes {
                  .method public static void M({{rendered}} a) cil managed { ret }
                }
                """);

            Assert.False(
                TryRunTool("ilasm", [ilPath, "-dll", $"-output={outputDll}", "-quiet"]));
            Assert.False(File.Exists(outputDll));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Assembles a class with one static method per spelling, then returns each method's raw
    /// signature blob alongside the parameter type as <c>CanonicalIL</c> renders it.
    /// </summary>
    static (byte[][] SignatureBlobs, string[] Rendered) AssembleAndRender(
        IReadOnlyList<string> parameterSpellings,
        string tempDir,
        string name)
    {
        var il = new System.Text.StringBuilder();
        il.AppendLine(".assembly extern mscorlib { .ver 4:0:0:0 }");
        il.AppendLine($".assembly {name} {{}}");
        il.AppendLine(".class public Shapes {");
        for (int i = 0; i < parameterSpellings.Count; i++)
        {
            il.AppendLine(
                $"  .method public static void M{i}({parameterSpellings[i]} a) cil managed {{ ret }}");
        }

        il.AppendLine("}");

        string ilPath = Path.Combine(tempDir, $"{name}.il");
        string dllPath = Path.Combine(tempDir, $"{name}.dll");
        File.WriteAllText(ilPath, il.ToString());
        RunILAsm(ilPath, dllPath);

        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var blobs = new byte[parameterSpellings.Count][];
        var rendered = new string[parameterSpellings.Count];
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            string methodName = reader.GetString(method.Name);
            if (!methodName.StartsWith('M') || !int.TryParse(methodName[1..], out int index))
                continue;

            blobs[index] = reader.GetBlobBytes(method.Signature);
            rendered[index] = method
                .DecodeSignature(ILSignatureTypeProvider.Instance, genericContext: null)
                .ParameterTypes[0];
        }

        Assert.All(blobs, blob => Assert.NotNull(blob));
        return (blobs, rendered);
    }

    // --- Helpers ---

    static string ResolveAssembly(string key) => key switch
    {
        "Core" => CoreDll,
        "Cache" => CacheDll,
        "Metadata" => MetadataDll,
        "Test" => TestDll,
        _ => throw new ArgumentException($"Unknown assembly key: {key}")
    };

    static List<ILInstructionText>? DisassembleFrom(string assemblyPath, string typeName, string methodName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        return MetadataInstructionProducer.DisassembleMethod(peReader, typeName, methodName);
    }

    static int CountMethods(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        return reader.MethodDefinitions.Count;
    }

    static string FindAssembly(string name)
    {
        var testDir = Path.GetDirectoryName(typeof(ILDisassemblerComparisonTests).Assembly.Location)!;
        return Path.Combine(testDir, name);
    }

    static bool CanRunILAsm()
    {
        // The ildasm/ilasm global tools target a specific .NET runtime and may be present on PATH
        // yet unable to execute (e.g. they exit ~150 when that runtime is absent). A process that
        // merely *starts* is not enough — probe a real round-trip of the test assembly and only
        // treat the tools as usable on a clean, output-producing exit. Otherwise the comparison
        // tests must Assert.Skip rather than fail.
        var tempDir = Path.Combine(Path.GetTempPath(), $"il-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var probeIl = Path.Combine(tempDir, "probe.il");
            var probeDll = Path.Combine(tempDir, "probe.dll");

            if (!TryRunTool("ildasm", [TestDll, $"-output={probeIl}", "-utf8"]) || !File.Exists(probeIl))
                return false;

            if (!TryRunTool("ilasm", [probeIl, "-dll", $"-output={probeDll}", "-quiet"]) || !File.Exists(probeDll))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Runs an external tool to completion, returning true only on a clean (exit code 0) run.
    /// Never throws and never blocks indefinitely; any failure to start, time out, or non-zero
    /// exit is reported as false so callers can fall back to skipping.
    /// </summary>
    static bool TryRunTool(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            return RunTool(fileName, arguments).ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    static ToolResult RunTool(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        TimeSpan effectiveTimeout = timeout ?? ToolTimeout;
        if (!process.WaitForExit(effectiveTimeout))
        {
            OutOfProcessCliProcess.KillAndWaitForExit(process, TimeSpan.FromSeconds(5));
            Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(5));
            throw new TimeoutException(
                $"{fileName} did not exit within {effectiveTimeout}: "
                + string.Join(" ", arguments));
        }

        if (!Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(5)))
            throw new TimeoutException($"{fileName} output streams did not close after exit.");

        return new ToolResult(
            process.ExitCode,
            stdout.GetAwaiter().GetResult(),
            stderr.GetAwaiter().GetResult());
    }

    readonly record struct ToolResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
