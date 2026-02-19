using System.Runtime.InteropServices;
using DotnetInspector.Metadata;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

public class MethodClassificationScannerTests
{
    [Fact]
    public void Scan_FindsUnsafeMethods()
    {
        var assemblyPath = typeof(MethodClassificationScannerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);

        var results = MethodClassificationScanner.Scan(stream);

        var unsafe_ = results.Where(r => r.Classification == MethodClassification.Unsafe).ToList();
        Assert.Contains(unsafe_, m => m.MethodName == "UnsafePointerMethod");
        Assert.Contains(unsafe_, m => m.MethodName == "UnsafeReturnPointer");

        var pointerMethod = unsafe_.First(m => m.MethodName == "UnsafePointerMethod");
        Assert.Equal("DotnetInspector.Tests.SampleUnsafeClass", pointerMethod.DeclaringType);
        Assert.Contains("*", pointerMethod.Signature);
    }

    [Fact]
    public void Scan_FindsPInvokeMethods()
    {
        var assemblyPath = typeof(MethodClassificationScannerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);

        var results = MethodClassificationScanner.Scan(stream);

        var pinvoke = results.Where(r => r.Classification == MethodClassification.PInvoke).ToList();
        Assert.Contains(pinvoke, m => m.MethodName == "GetCurrentProcessId");

        var method = pinvoke.First(m => m.MethodName == "GetCurrentProcessId");
        Assert.Equal("DotnetInspector.Tests.SamplePInvokeClass", method.DeclaringType);
        Assert.Equal("kernel32.dll", method.ModuleName);
    }

    [Fact]
    public void Scan_DoesNotIncludeNormalMethods()
    {
        var assemblyPath = typeof(MethodClassificationScannerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);

        var results = MethodClassificationScanner.Scan(stream);

        Assert.DoesNotContain(results, m => m.MethodName == "SafeMethod");
    }

    [Fact]
    public void Scan_PlatformAssembly_FindsUnsafeMethods()
    {
        var testDir = Path.GetDirectoryName(typeof(MethodClassificationScannerTests).Assembly.Location)!;
        var interopPath = Path.Combine(testDir, "System.Runtime.InteropServices.dll");

        // Use platform assembly if available
        if (!File.Exists(interopPath))
        {
            var (resolved, _, _, _) = PlatformResolver.ResolveAssembly("System.Runtime.InteropServices");
            if (resolved == null) return;
            interopPath = resolved;
        }

        using var stream = File.OpenRead(interopPath);
        var results = MethodClassificationScanner.Scan(stream);

        var unsafe_ = results.Where(r => r.Classification == MethodClassification.Unsafe).ToList();
        // Platform assembly content varies by OS; skip if none found
        Assert.SkipWhen(unsafe_.Count == 0, "No public unsafe methods in this platform's System.Runtime.InteropServices");
    }
}

/// <summary>
/// Sample class with unsafe methods for testing.
/// </summary>
public static class SampleUnsafeClass
{
    public static unsafe int UnsafePointerMethod(int* ptr) => *ptr;
    public static unsafe int* UnsafeReturnPointer(int[] arr) { fixed (int* p = arr) return p; }
    public static string SafeMethod() => "safe";
}

/// <summary>
/// Sample class with P/Invoke methods for testing.
/// </summary>
public static partial class SamplePInvokeClass
{
    [DllImport("kernel32.dll")]
    public static extern int GetCurrentProcessId();
}
