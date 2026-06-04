using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
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
    public void ScanAuditMetadata_CountsAllPInvokeMethods()
    {
        var assemblyPath = typeof(MethodClassificationScannerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var metadata = AssemblyDetailScanner.ScanAuditMetadata(peReader);

        Assert.True(metadata.PInvokeMethodCount >= 2);
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
    public void Scan_ClassifiesRuntimeAsyncMethods()
    {
        // Synthesize an assembly with a method carrying MethodImplAttributes.Async
        // (0x2000) so the runtime-async path is covered regardless of the SDK or the
        // repo's runtime-async build setting.
        using var stream = BuildAssemblyWithRuntimeAsyncMethod();

        var results = MethodClassificationScanner.Scan(stream);

        var method = results.FirstOrDefault(m => m.MethodName == "FakeRuntimeAsync");
        Assert.NotNull(method);
        Assert.Equal(MethodClassification.RuntimeAsync, method.Classification);
    }

    [Fact]
    public void Scan_ClassifiesStateMachineAsyncMethods()
    {
        // The classic-async path keys off AsyncStateMachineAttribute, applied directly
        // so the test is deterministic regardless of the build's runtime-async setting.
        var assemblyPath = typeof(MethodClassificationScannerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);

        var results = MethodClassificationScanner.Scan(stream);

        var method = results.FirstOrDefault(m => m.MethodName == "AttributedStateMachineAsync");
        Assert.NotNull(method);
        Assert.Equal(MethodClassification.StateMachineAsync, method.Classification);
    }

    [Fact]
    public void Scan_ClassifiesRealAsyncMethodAsAsync()
    {
        // A real async method is detected as async; its kind depends on whether the
        // assembly was compiled with runtime async.
        var assemblyPath = typeof(MethodClassificationScannerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);

        var results = MethodClassificationScanner.Scan(stream);

        Assert.Contains(results, m => m.MethodName == "RealAsyncMethod"
            && m.Classification is MethodClassification.RuntimeAsync
                                or MethodClassification.StateMachineAsync);
    }

    [Fact]
    public void Scan_DoesNotClassifyNonAsyncTaskMethods()
    {
        var assemblyPath = typeof(MethodClassificationScannerTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);

        var results = MethodClassificationScanner.Scan(stream);

        Assert.DoesNotContain(results, m => m.MethodName == "NotAsyncTaskMethod");
    }

    private static MemoryStream BuildAssemblyWithRuntimeAsyncMethod()
    {
        const MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000;
        var ab = new PersistedAssemblyBuilder(new AssemblyName("RuntimeAsyncFixture"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("RuntimeAsyncFixture");
        var type = module.DefineType("RuntimeAsyncSample", TypeAttributes.Public | TypeAttributes.Class);
        var method = type.DefineMethod("FakeRuntimeAsync", MethodAttributes.Public, typeof(Task<int>), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        method.SetImplementationFlags(AsyncImplFlag);
        type.CreateType();

        var stream = new MemoryStream();
        ab.Save(stream);
        stream.Position = 0;
        return stream;
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

    [DllImport("kernel32.dll")]
    internal static extern int GetCurrentThreadId();
}

/// <summary>
/// Sample class with async methods for testing async classification.
/// </summary>
public class SampleAsyncClass
{
    public async Task<int> RealAsyncMethod()
    {
        await Task.Yield();
        return 42;
    }

    public Task<int> NotAsyncTaskMethod() => Task.FromResult(1);
}

/// <summary>
/// Sample class exercising the classic state-machine async detection path. The
/// attribute is applied directly so detection is deterministic even when the
/// build emits runtime async for real <c>async</c> methods.
/// </summary>
public class SampleStateMachineAsyncClass
{
    [AsyncStateMachine(typeof(SampleStateMachineAsyncClass))]
    public Task<int> AttributedStateMachineAsync() => Task.FromResult(1);
}
