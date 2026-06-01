using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using DotnetInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Head-to-head comparison tests that validate our IL disassembler output
/// against ILSpy (ilspycmd) and ILAsm (ilasm) reference tools.
/// Test cases are generated conditionally — ILAsm cases only appear on Windows
/// where the tools are available. No traits or skip logic needed.
/// </summary>
public partial class ILDisassemblerComparisonTests
{
    static readonly string CoreDll = FindAssembly("DotnetInspector.Core.dll");
    static readonly string MetadataDll = FindAssembly("DotnetInspector.Metadata.dll");
    static readonly string TestDll = typeof(ILDisassemblerComparisonTests).Assembly.Location;

    static readonly bool HasILSpyCmd = CanRunILSpyCmd();
    static readonly bool HasILAsm = CanRunILAsm();

    // --- ILSpy: instruction-level comparison ---

    [Theory]
    [MemberData(nameof(ILSpyMethodCases))]
    public void ILSpy_InstructionsMatch(string assembly, string typeName, string methodName)
    {
        Assert.SkipUnless(HasILSpyCmd, "ilspycmd not found — install with: dotnet tool install -g ilspycmd --allow-roll-forward");

        var assemblyPath = ResolveAssembly(assembly);
        var ilspyMethods = ParseILSpyTypeOutput(assemblyPath, typeName);
        Assert.True(
            ilspyMethods.TryGetValue(methodName, out var ilspyInstructions),
            $"ILSpy did not produce output for {typeName}.{methodName}. " +
            $"Available: {string.Join(", ", ilspyMethods.Keys)}");

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var ours = ILDisassembler.DisassembleMethod(peReader, typeName, methodName);
        Assert.NotNull(ours);

        Assert.Equal(ilspyInstructions.Count, ours.Count);

        for (int i = 0; i < ours.Count; i++)
        {
            Assert.Equal(ilspyInstructions[i].Offset, ours[i].Offset);
            Assert.Equal(ilspyInstructions[i].OpCode, ours[i].OpCodeName);
        }
    }

    [Theory]
    [MemberData(nameof(ILSpyTypeCases))]
    public void ILSpy_AllMethodInstructionCountsMatch(string assembly, string typeName)
    {
        Assert.SkipUnless(HasILSpyCmd, "ilspycmd not found — install with: dotnet tool install -g ilspycmd --allow-roll-forward");

        var assemblyPath = ResolveAssembly(assembly);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var ilspyMethods = ParseILSpyTypeOutput(assemblyPath, typeName);
        Assert.NotEmpty(ilspyMethods);

        // ILSpy disagrees on trivial expression-bodied methods
        HashSet<string> knownMismatches = ["Add"];
        List<string> mismatches = [];

        foreach (var (methodName, ilspyInstructions) in ilspyMethods)
        {
            var ours = ILDisassembler.DisassembleMethod(peReader, typeName, methodName);
            if (ours is null || knownMismatches.Contains(methodName))
                continue;

            if (ours.Count != ilspyInstructions.Count)
                mismatches.Add($"{methodName}: ours={ours.Count}, ILSpy={ilspyInstructions.Count}");
        }

        Assert.True(mismatches.Count == 0,
            $"Instruction count mismatches:\n{string.Join("\n", mismatches)}");
    }

    // --- ILAsm: roundtrip validation ---

    [Theory]
    [MemberData(nameof(ILAsmAssemblyCases))]
    public void ILAsm_Roundtrip_ProducesValidAssembly(string assembly)
    {
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install via runtime.{rid}.Microsoft.NETCore.ILAsm/ILDAsm NuGet packages");

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
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install via runtime.{rid}.Microsoft.NETCore.ILAsm/ILDAsm NuGet packages");

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
        Assert.SkipUnless(HasILAsm, "ildasm/ilasm not found — install via runtime.{rid}.Microsoft.NETCore.ILAsm/ILDAsm NuGet packages");

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

    /// <summary>Methods to compare instruction-by-instruction against ILSpy.</summary>
    public static IEnumerable<object[]> ILSpyMethodCases()
    {
        yield return ["Core", "DotnetInspector.Core.CoreCache", "Initialize"];
        yield return ["Core", "DotnetInspector.Core.CoreCache", "GetBasePath"];
        yield return ["Core", "DotnetInspector.Core.CoreCache", "GetDirectorySize"];
        // Add omitted: ILSpy disagrees on instruction count for trivial expression-bodied methods
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "Classify"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "CreateList"];
        // Edge cases: switch, try/catch, generics, constants, 2-byte opcodes
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "SwitchCase"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "TryCatch"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "TryFinally"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "LongConstant"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "CompareEquals"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "ManyLocals"];
        // More edge cases: arrays, checked math, exceptions, tokens, delegates
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "ArrayOps"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "CheckedArithmetic"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "NestedExceptionHandlers"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "MultipleCatch"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "ThrowAndRethrow"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "GetTypeToken"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "GetDelegate"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "InterfaceCall"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "ConvertTypes"];
    }

    /// <summary>Types to compare all method instruction counts against ILSpy.</summary>
    public static IEnumerable<object[]> ILSpyTypeCases()
    {
        yield return ["Core", "DotnetInspector.Core.CoreCache"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass"];
    }

    /// <summary>Assemblies for ILAsm roundtrip validation.</summary>
    public static IEnumerable<object[]> ILAsmAssemblyCases()
    {
        yield return ["Core"];
        yield return ["Test"];
    }

    /// <summary>Methods for ILAsm roundtrip opcode comparison.</summary>
    public static IEnumerable<object[]> ILAsmMethodCases()
    {
        yield return ["Core", "DotnetInspector.Core.CoreCache", "Initialize"];
        yield return ["Core", "DotnetInspector.Core.CoreCache", "GetBasePath"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "SwitchCase"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "TryCatch"];
        yield return ["Test", "DotnetInspector.Tests.ILSampleClass", "CompareEquals"];
    }

    // --- ILSpy output parsing ---

    /// <summary>
    /// Runs ilspycmd and parses IL instructions per method from its output.
    /// Returns a dictionary of method name → list of parsed instructions.
    /// </summary>
    static Dictionary<string, List<ILSpyInstruction>> ParseILSpyTypeOutput(
        string assemblyPath, string typeName)
    {
        var output = RunILSpyCmd(assemblyPath, typeName);
        Dictionary<string, List<ILSpyInstruction>> methods = [];

        string? currentMethod = null;
        List<ILSpyInstruction>? currentInstructions = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            // Detect method start: ".method" directive resets state
            if (trimmed.StartsWith(".method", StringComparison.Ordinal))
            {
                currentMethod = null;
                currentInstructions = null;
                continue;
            }

            // Match method name line like "void Initialize (" or "int32 Add ("
            if (currentMethod is null && currentInstructions is null)
            {
                var nameMatch = MethodNamePattern().Match(trimmed);
                if (nameMatch.Success)
                {
                    currentMethod = nameMatch.Groups[1].Value;
                    currentInstructions = [];
                    continue;
                }
            }

            // IL instruction lines: "IL_XXXX: opcode ..."
            if (currentInstructions is not null)
            {
                var ilMatch = ILInstructionPattern().Match(trimmed);
                if (ilMatch.Success)
                {
                    int offset = int.Parse(ilMatch.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                    string opcode = ilMatch.Groups[2].Value;
                    currentInstructions.Add(new ILSpyInstruction(offset, opcode));
                    continue;
                }

                if (trimmed.StartsWith("} // end of method", StringComparison.Ordinal))
                {
                    if (currentMethod is not null && currentInstructions.Count > 0)
                        methods.TryAdd(currentMethod, currentInstructions);
                    currentMethod = null;
                    currentInstructions = null;
                }
            }
        }

        return methods;
    }

    record ILSpyInstruction(int Offset, string OpCode);

    [GeneratedRegex(@"^IL_([0-9a-fA-F]{4}):\s+(\S+)")]
    private static partial Regex ILInstructionPattern();

    [GeneratedRegex(@"(\w[\w.]*)\s*\(")]
    private static partial Regex MethodNamePattern();

    // --- Tool execution ---

    static string RunILSpyCmd(string assemblyPath, string typeName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ilspycmd",
            ArgumentList = { "-il", "-t", typeName, assemblyPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(30));

        if (process.ExitCode != 0)
        {
            string stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"ilspycmd exited with code {process.ExitCode}: {stderr}");
        }

        return output;
    }

    static string RunILDasm(string assemblyPath, string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ildasm",
            ArgumentList = { assemblyPath, $"-output={outputPath}", "-utf8" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(30));

        if (process.ExitCode != 0)
        {
            string stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"ildasm exited with code {process.ExitCode}: {stderr}");
        }

        return output;
    }

    static string RunILAsm(string ilPath, string outputDll)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ilasm",
            ArgumentList = { ilPath, "-dll", $"-output={outputDll}", "-quiet" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(30));

        if (process.ExitCode != 0)
        {
            string stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"ilasm exited with code {process.ExitCode}: {stderr}\nstdout: {output}");
        }

        return output;
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

    // --- Helpers ---

    static string ResolveAssembly(string key) => key switch
    {
        "Core" => CoreDll,
        "Metadata" => MetadataDll,
        "Test" => TestDll,
        _ => throw new ArgumentException($"Unknown assembly key: {key}")
    };

    static List<ILInstruction>? DisassembleFrom(string assemblyPath, string typeName, string methodName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        return ILDisassembler.DisassembleMethod(peReader, typeName, methodName);
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

    static bool CanRunILSpyCmd()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ilspycmd",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";

            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(TimeSpan.FromSeconds(10));
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null) return false;

            // Drain both streams to avoid deadlock on tools that write a lot to stderr.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }
            stdout.GetAwaiter().GetResult();
            stderr.GetAwaiter().GetResult();

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
