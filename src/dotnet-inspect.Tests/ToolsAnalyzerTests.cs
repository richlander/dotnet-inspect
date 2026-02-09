using DotnetInspector.Models;
using DotnetInspector;
using DotnetInspector.Inspectors;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for ToolsAnalyzer package content analysis.
/// </summary>
public class ToolsAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public ToolsAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tools-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void AnalyzeToolsDirectory_DetectsTargetFrameworks()
    {
        var toolsDir = Path.Combine(_tempDir, "tools");
        Directory.CreateDirectory(Path.Combine(toolsDir, "net8.0", "any"));
        Directory.CreateDirectory(Path.Combine(toolsDir, "net9.0", "any"));

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);

        Assert.NotNull(result.TargetFrameworks);
        Assert.Contains("net8.0", result.TargetFrameworks);
        Assert.Contains("net9.0", result.TargetFrameworks);
    }

    [Fact]
    public void AnalyzeToolsDirectory_AnyRid_SetsFrameworkDependent()
    {
        var toolsDir = Path.Combine(_tempDir, "tools");
        Directory.CreateDirectory(Path.Combine(toolsDir, "net8.0", "any"));

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);

        Assert.True(result.IsFrameworkDependent);
        Assert.NotNull(result.SupportedRids);
        Assert.Contains("any", result.SupportedRids);
    }

    [Fact]
    public void AnalyzeToolsDirectory_SpecificRid_SetsHasRidSpecificAssets()
    {
        var toolsDir = Path.Combine(_tempDir, "tools");
        Directory.CreateDirectory(Path.Combine(toolsDir, "net8.0", "win-x64"));
        Directory.CreateDirectory(Path.Combine(toolsDir, "net8.0", "linux-x64"));

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);

        Assert.True(result.HasRidSpecificAssets);
        Assert.NotNull(result.SupportedRids);
        Assert.Contains("win-x64", result.SupportedRids);
        Assert.Contains("linux-x64", result.SupportedRids);
    }

    [Fact]
    public void AnalyzeToolsDirectory_Version2SettingsXml_DetectsRidSpecificPointer()
    {
        var toolsDir = Path.Combine(_tempDir, "tools");
        var net8Dir = Path.Combine(toolsDir, "net8.0", "any");
        Directory.CreateDirectory(net8Dir);

        var settingsXml = """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="mytool" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="MyTool.win-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="MyTool.linux-x64" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """;
        File.WriteAllText(Path.Combine(net8Dir, "DotnetToolSettings.xml"), settingsXml);

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);

        Assert.True(result.IsRidSpecificPointerPackage);
        Assert.Equal("DotNetCliTool Version=\"2\" (RID-specific)", result.ToolFormat);
        Assert.NotNull(result.ToolCommands);
        Assert.Contains("mytool", result.ToolCommands);
        Assert.NotNull(result.RuntimeIdentifierPackages);
        Assert.Equal(2, result.RuntimeIdentifierPackages.Count);
        Assert.Contains(result.RuntimeIdentifierPackages, r => r.RuntimeIdentifier == "win-x64" && r.PackageId == "MyTool.win-x64");
    }

    [Fact]
    public void AnalyzeToolsDirectory_Version1SettingsXml_DetectsPortableTool()
    {
        var toolsDir = Path.Combine(_tempDir, "tools");
        var net8Dir = Path.Combine(toolsDir, "net8.0", "any");
        Directory.CreateDirectory(net8Dir);

        var settingsXml = """
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="dotnet-mytool" />
              </Commands>
            </DotNetCliTool>
            """;
        File.WriteAllText(Path.Combine(net8Dir, "DotnetToolSettings.xml"), settingsXml);

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);

        Assert.False(result.IsRidSpecificPointerPackage);
        Assert.Equal("DotNetCliTool Version=\"1\" (portable)", result.ToolFormat);
        Assert.NotNull(result.ToolCommands);
        Assert.Contains("dotnet-mytool", result.ToolCommands);
    }

    [Fact]
    public void AnalyzeLibDirectory_DetectsTargetFrameworks()
    {
        var libDir = Path.Combine(_tempDir, "lib");
        Directory.CreateDirectory(Path.Combine(libDir, "net8.0"));
        Directory.CreateDirectory(Path.Combine(libDir, "netstandard2.0"));
        Directory.CreateDirectory(Path.Combine(libDir, "net462"));

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeLibDirectory(libDir, result);

        Assert.NotNull(result.TargetFrameworks);
        Assert.Equal(3, result.TargetFrameworks.Count);
        Assert.Contains("net8.0", result.TargetFrameworks);
        Assert.Contains("netstandard2.0", result.TargetFrameworks);
        Assert.Contains("net462", result.TargetFrameworks);
    }

    [Fact]
    public void AnalyzeRuntimesDirectory_DetectsRidsAndNativeFiles()
    {
        var runtimesDir = Path.Combine(_tempDir, "runtimes");
        var winNative = Path.Combine(runtimesDir, "win-x64", "native");
        var linuxNative = Path.Combine(runtimesDir, "linux-x64", "native");
        Directory.CreateDirectory(winNative);
        Directory.CreateDirectory(linuxNative);
        File.WriteAllText(Path.Combine(winNative, "native.dll"), "");
        File.WriteAllText(Path.Combine(linuxNative, "libnative.so"), "");

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeRuntimesDirectory(runtimesDir, result);

        Assert.True(result.HasRidSpecificAssets);
        Assert.True(result.HasNativeDependencies);
        Assert.NotNull(result.SupportedRids);
        Assert.Contains("win-x64", result.SupportedRids);
        Assert.Contains("linux-x64", result.SupportedRids);
        Assert.NotNull(result.NativeFiles);
        Assert.Contains("win-x64: native.dll", result.NativeFiles);
        Assert.Contains("linux-x64: libnative.so", result.NativeFiles);
    }

    [Fact]
    public void AnalyzeContentDirectories_DetectsStandardDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "lib"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "tools"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "analyzers"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "build"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "runtimes"));

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeContentDirectories(_tempDir, result);

        Assert.NotNull(result.ContentDirectories);
        Assert.Equal(5, result.ContentDirectories.Count);
        Assert.Contains("lib", result.ContentDirectories);
        Assert.Contains("tools", result.ContentDirectories);
        Assert.Contains("analyzers", result.ContentDirectories);
        Assert.Contains("build", result.ContentDirectories);
        Assert.Contains("runtimes", result.ContentDirectories);
    }

    [Fact]
    public void AnalyzeContentDirectories_IgnoresNonStandardDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "lib"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "custom"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "docs"));

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeContentDirectories(_tempDir, result);

        Assert.NotNull(result.ContentDirectories);
        Assert.Single(result.ContentDirectories);
        Assert.Contains("lib", result.ContentDirectories);
    }

    [Fact]
    public void CountAssemblies_CountsDllsExcludingResources()
    {
        var libDir = Path.Combine(_tempDir, "lib", "net8.0");
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "MyLib.dll"), "");
        File.WriteAllText(Path.Combine(libDir, "MyLib.resources.dll"), "");
        File.WriteAllText(Path.Combine(libDir, "OtherLib.dll"), "");

        var count = ToolsAnalyzer.CountAssemblies(_tempDir);

        Assert.Equal(2, count);
    }

    [Fact]
    public void CountAssemblies_PrefersToolsOverLib()
    {
        var toolsDir = Path.Combine(_tempDir, "tools", "net8.0", "any");
        var libDir = Path.Combine(_tempDir, "lib", "net8.0");
        Directory.CreateDirectory(toolsDir);
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(toolsDir, "Tool.dll"), "");
        File.WriteAllText(Path.Combine(libDir, "Lib1.dll"), "");
        File.WriteAllText(Path.Combine(libDir, "Lib2.dll"), "");

        var count = ToolsAnalyzer.CountAssemblies(_tempDir);

        // Should count from tools, not lib
        Assert.Equal(1, count);
    }

    [Fact]
    public void CountAssemblies_NoToolsOrLib_ReturnsZero()
    {
        var count = ToolsAnalyzer.CountAssemblies(_tempDir);

        Assert.Equal(0, count);
    }

    [Fact]
    public void AnalyzeToolsDirectory_Version2_DoesNotSetFrameworkDependent()
    {
        // Version 2 tools with RID packages should NOT be marked as framework dependent
        // even if they have an "any" folder (which is just a pointer)
        var toolsDir = Path.Combine(_tempDir, "tools");
        var net8Dir = Path.Combine(toolsDir, "net8.0", "any");
        Directory.CreateDirectory(net8Dir);

        var settingsXml = """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="mytool" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="MyTool.win-x64" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """;
        File.WriteAllText(Path.Combine(net8Dir, "DotnetToolSettings.xml"), settingsXml);

        var result = new InspectionResult();
        ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);

        // Version 2 with RID packages should be self-contained, not framework-dependent
        Assert.False(result.IsFrameworkDependent);
        Assert.True(result.IsRidSpecificPointerPackage);
    }
}
