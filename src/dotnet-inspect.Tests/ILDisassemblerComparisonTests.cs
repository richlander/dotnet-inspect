using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DotnetInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Head-to-head comparison tests that validate our IL disassembler output
/// against ILSpy (ilspycmd) and ILAsm (ilasm) reference tools.
/// ILSpy tests run on all platforms; ILAsm roundtrip tests run only on Windows.
/// </summary>
public partial class ILDisassemblerComparisonTests
{
    static readonly string CoreDll = FindAssembly("DotnetInspector.Core.dll");
    static readonly string MetadataDll = FindAssembly("DotnetInspector.Metadata.dll");
    static readonly string TestDll = typeof(ILDisassemblerComparisonTests).Assembly.Location;

    static readonly bool HasILSpyCmd = CanRunILSpyCmd();
    static readonly bool HasILAsm = CanRunILAsm();

    // --- ILSpy comparison tests ---

    [Fact]
    [Trait("Category", "ILSpy")]
    public void ILSpy_CoreCache_Initialize_InstructionsMatch()
    {
        SkipIfNoILSpy();
        AssertMethodMatchesILSpy(
            CoreDll,
            "DotnetInspector.Core.CoreCache",
            "Initialize");
    }

    [Fact]
    [Trait("Category", "ILSpy")]
    public void ILSpy_CoreCache_GetBasePath_InstructionsMatch()
    {
        SkipIfNoILSpy();
        AssertMethodMatchesILSpy(
            CoreDll,
            "DotnetInspector.Core.CoreCache",
            "GetBasePath");
    }

    [Fact]
    [Trait("Category", "ILSpy")]
    public void ILSpy_CoreCache_GetDirectorySize_InstructionsMatch()
    {
        SkipIfNoILSpy();
        AssertMethodMatchesILSpy(
            CoreDll,
            "DotnetInspector.Core.CoreCache",
            "GetDirectorySize");
    }

    [Fact]
    [Trait("Category", "ILSpy")]
    public void ILSpy_ILSampleClass_Add_InstructionsMatch()
    {
        SkipIfNoILSpy();
        AssertMethodMatchesILSpy(
            TestDll,
            "DotnetInspector.Tests.ILSampleClass",
            "Add");
    }

    [Fact]
    [Trait("Category", "ILSpy")]
    public void ILSpy_ILSampleClass_Classify_InstructionsMatch()
    {
        SkipIfNoILSpy();
        AssertMethodMatchesILSpy(
            TestDll,
            "DotnetInspector.Tests.ILSampleClass",
            "Classify");
    }

    [Fact]
    [Trait("Category", "ILSpy")]
    public void ILSpy_ILSampleClass_CreateList_InstructionsMatch()
    {
        SkipIfNoILSpy();
        AssertMethodMatchesILSpy(
            TestDll,
            "DotnetInspector.Tests.ILSampleClass",
            "CreateList");
    }

    [Fact]
    [Trait("Category", "ILSpy")]
    public void ILSpy_AllMethods_InstructionCountsMatch()
    {
        SkipIfNoILSpy();

        using var stream = File.OpenRead(CoreDll);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var ilspyMethods = ParseILSpyTypeOutput(CoreDll, "DotnetInspector.Core.CoreCache");
        Assert.NotEmpty(ilspyMethods);

        List<string> mismatches = [];

        foreach (var (methodName, ilspyInstructions) in ilspyMethods)
        {
            var ours = ILDisassembler.DisassembleMethod(peReader, "DotnetInspector.Core.CoreCache", methodName);
            if (ours is null)
                continue; // Abstract or extern

            if (ours.Count != ilspyInstructions.Count)
            {
                mismatches.Add(
                    $"{methodName}: ours={ours.Count}, ILSpy={ilspyInstructions.Count}");
            }
        }

        Assert.True(mismatches.Count == 0,
            $"Instruction count mismatches:\n{string.Join("\n", mismatches)}");
    }

    // --- ILAsm roundtrip tests (Windows only) ---

    [Fact]
    [Trait("Category", "ILAsm")]
    public void ILAsm_CoreDll_Roundtrip_ProducesValidAssembly()
    {
        SkipIfNoILAsm();

        var outputDll = RoundtripWithILAsm(CoreDll);
        Assert.True(File.Exists(outputDll), $"ILAsm failed to produce {outputDll}");

        // Verify the reassembled DLL is a valid PE with metadata
        using var stream = File.OpenRead(outputDll);
        using var peReader = new PEReader(stream);
        Assert.True(peReader.HasMetadata);
        var reader = peReader.GetMetadataReader();
        Assert.True(reader.TypeDefinitions.Count > 0);
    }

    [Fact]
    [Trait("Category", "ILAsm")]
    public void ILAsm_Roundtrip_MethodCountPreserved()
    {
        SkipIfNoILAsm();

        int originalCount = CountMethods(CoreDll);
        var outputDll = RoundtripWithILAsm(CoreDll);
        int roundtripCount = CountMethods(outputDll);

        Assert.Equal(originalCount, roundtripCount);
    }

    [Fact]
    [Trait("Category", "ILAsm")]
    public void ILAsm_Roundtrip_OurDisassembler_ProducesSameOpcodes()
    {
        SkipIfNoILAsm();

        var outputDll = RoundtripWithILAsm(CoreDll);

        // Disassemble the same method from original and roundtripped assembly
        var original = DisassembleFrom(CoreDll, "DotnetInspector.Core.CoreCache", "Initialize");
        var roundtripped = DisassembleFrom(outputDll, "DotnetInspector.Core.CoreCache", "Initialize");

        Assert.NotNull(original);
        Assert.NotNull(roundtripped);
        Assert.Equal(original.Count, roundtripped.Count);

        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].OpCodeName, roundtripped[i].OpCodeName);
            Assert.Equal(original[i].Offset, roundtripped[i].Offset);
        }
    }

    // --- Core comparison logic ---

    static void AssertMethodMatchesILSpy(string assemblyPath, string typeName, string methodName)
    {
        var ilspyMethods = ParseILSpyTypeOutput(assemblyPath, typeName);
        Assert.True(
            ilspyMethods.TryGetValue(methodName, out var ilspyInstructions),
            $"ILSpy did not produce output for {typeName}.{methodName}. " +
            $"Available methods: {string.Join(", ", ilspyMethods.Keys)}");

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var ours = ILDisassembler.DisassembleMethod(peReader, typeName, methodName);
        Assert.NotNull(ours);

        Assert.Equal(ilspyInstructions.Count, ours.Count);

        for (int i = 0; i < ours.Count; i++)
        {
            var ourInstr = ours[i];
            var ilspyInstr = ilspyInstructions[i];

            // Offsets must match exactly
            Assert.Equal(ilspyInstr.Offset, ourInstr.Offset);

            // Opcodes must match exactly
            Assert.Equal(ilspyInstr.OpCode, ourInstr.OpCodeName);
        }
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

            // Detect method start: ".method" lines followed by method name
            // ILSpy format: ".method public hidebysig static \n\t\tvoid Initialize ("
            // We look for the method name in lines like "void Initialize ("
            // or "int32 Add (" between .method and the opening brace
            if (trimmed.StartsWith(".method", StringComparison.Ordinal))
            {
                currentMethod = null;
                currentInstructions = null;
                continue;
            }

            // Match a method name line like "void Initialize (" or "int32 Add ("
            // These appear between .method and the opening brace { of the body
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

                // End of method body
                if (trimmed.StartsWith("} // end of method", StringComparison.Ordinal))
                {
                    if (currentMethod is not null && currentInstructions.Count > 0)
                    {
                        // Handle overloads: keep the first occurrence
                        methods.TryAdd(currentMethod, currentInstructions);
                    }
                    currentMethod = null;
                    currentInstructions = null;
                }
            }
        }

        return methods;
    }

    record ILSpyInstruction(int Offset, string OpCode);

    // Matches: "IL_xxxx: opcode" — captures offset hex and opcode name
    [GeneratedRegex(@"^IL_([0-9a-fA-F]{4}):\s+(\S+)")]
    private static partial Regex ILInstructionPattern();

    // Matches method name from lines like "void Initialize (" or "int32 Add ("
    // The method name follows the return type and precedes " (" or " ()"
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
            ArgumentList = { assemblyPath, $"/output={outputPath}", "/utf8" },
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
            ArgumentList = { ilPath, $"/dll", $"/output={outputDll}", "/quiet" },
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

    /// <summary>
    /// Disassembles with ildasm, reassembles with ilasm, returns path to new DLL.
    /// </summary>
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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ildasm",
                ArgumentList = { "/?" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(TimeSpan.FromSeconds(10));
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void SkipIfNoILSpy()
    {
        if (!HasILSpyCmd)
            throw Xunit.Sdk.SkipException.ForSkip("ilspycmd is not available on this system");
    }

    static void SkipIfNoILAsm()
    {
        if (!HasILAsm)
            throw Xunit.Sdk.SkipException.ForSkip(
                "ilasm/ildasm require Windows. Filter with: --filter \"Category!=ILAsm\"");
    }
}
