using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ILInspector.Analysis.Classification;
using ILInspector.Metadata;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Tests;

// Resolves real platform assemblies via PlatformResolver; share "Console" so it never runs in
// parallel with the DOTNET_ROOT-mutating PlatformResolverTests (#1256).
[Collection("Console")]
public class MethodClassificationScannerTests
{
    // The method classification analyzers' rows over one image, every analyzer
    // in metadata order: the production path that replaced the scanner.
    static List<ClassifiedMethodRow> Classify(Stream stream)
    {
        using var peReader = new PEReader(stream);
        ClassificationQuestion[] questions =
        [
            new(MethodClassificationAnalyzer.PInvoke, ClassificationClosing.Rows),
            new(MethodClassificationAnalyzer.Async, ClassificationClosing.Rows),
            new(MethodClassificationAnalyzer.PointerSignature, ClassificationClosing.Rows),
        ];
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, questions);
        return [.. questions.SelectMany(question =>
            Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(question)).Methods)];
    }

    const string MemorySafetyFixture =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetySpellingFixture";
    const string UnsafeAsyncFixture =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures.UnsafeAsyncFixtures";
    const string PointerTargetFixture =
        $"{UnsafeAsyncFixture}.PointerTarget";
    const string ClassicAsyncFixture =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncInventoryFixtures";

    [Fact]
    public void Classify_FindsUnsafeMethods()
    {
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var stream = File.OpenRead(assemblyPath);

        var results = Classify(stream);

        var unsafe_ = results.Where(r => r.Classification == MethodClassification.Unsafe).ToList();
        Assert.Contains(
            unsafe_,
            method => method.MethodName.ToString() == "PointerNoneMethod"
                && method.DeclaringType.ToString() == MemorySafetyFixture);
        Assert.Contains(unsafe_, method => method.MethodName.ToString() == "PointerReturn");

        var pointerMethod = unsafe_.First(
            method => method.MethodName.ToString() == "PointerNoneMethod"
                && method.DeclaringType.ToString() == MemorySafetyFixture);
        Assert.Contains("*", pointerMethod.Signature.ToString());
        Assert.NotNull(pointerMethod.Anchor);
        Assert.Equal("System.Int32", pointerMethod.ReturnType?.ToString());
    }

    [Fact]
    public void Classify_FindsPInvokeMethods()
    {
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var stream = File.OpenRead(assemblyPath);

        var results = Classify(stream);

        var pinvoke = results.Where(r => r.Classification == MethodClassification.PInvoke).ToList();
        Assert.Contains(
            pinvoke,
            method => method.MethodName.ToString() == "SafeExtern"
                && method.DeclaringType.ToString() == MemorySafetyFixture);

        var method = pinvoke.First(
            method => method.MethodName.ToString() == "SafeExtern"
                && method.DeclaringType.ToString() == MemorySafetyFixture);
        Assert.Equal("__dotnet_inspect_memory_safety_fixture__", method.ModuleName?.ToString());
        Assert.NotNull(method.Anchor);
        Assert.Equal("System.Int32", method.ReturnType?.ToString());
    }

    [Fact]
    public void ScanAuditMetadata_CountsAllPInvokeMethods()
    {
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var metadata = AssemblyDetailScanner.ScanAuditMetadata(peReader);

        Assert.True(metadata.PInvokeMethodCount >= 2);
    }

    [Fact]
    public void Classify_DoesNotIncludeNormalMethods()
    {
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var stream = File.OpenRead(assemblyPath);

        var results = Classify(stream);

        Assert.DoesNotContain(
            results,
            method => method.MethodName.ToString() == "NormalMethod"
                && method.DeclaringType.ToString() == MemorySafetyFixture);
    }

    [Fact]
    public void Classify_ClassifiesRuntimeAsyncMethods()
    {
        // Synthesize an assembly with a method carrying MethodImplAttributes.Async
        // (0x2000) so the runtime-async path is covered regardless of the SDK or the
        // repo's runtime-async build setting.
        using var stream = BuildAssemblyWithRuntimeAsyncMethod();

        var results = Classify(stream);

        var method = results.FirstOrDefault(m => m.MethodName.ToString() == "FakeRuntimeAsync");
        Assert.NotNull(method);
        Assert.Equal(MethodClassification.RuntimeAsync, method.Classification);
        Assert.NotNull(method.Anchor);
        Assert.Equal("System.Threading.Tasks.Task`1<System.Int32>", method.ReturnType?.ToString());
    }

    [Fact]
    public void Classify_ClassifiesStateMachineAsyncMethods()
    {
        string assemblyPath = FixtureCatalog.DecompilerClassicAsync.AssemblyPath();
        using var stream = File.OpenRead(assemblyPath);

        var results = Classify(stream);

        var method = results.FirstOrDefault(
            method => method.MethodName.ToString() == "Plain"
                && method.DeclaringType.ToString() == ClassicAsyncFixture);
        Assert.NotNull(method);
        Assert.Equal(MethodClassification.StateMachineAsync, method.Classification);
        Assert.NotNull(method.Anchor);
        Assert.Equal("System.Threading.Tasks.Task`1<System.Int32>", method.ReturnType?.ToString());
    }

    [Fact]
    public void Classify_FormatsNestedGenericDeclaringTypesFromExactSegments()
    {
        using var stream = BuildAssemblyWithNestedGenericRuntimeAsyncMethod();

        var results = Classify(stream);

        var method = Assert.Single(
            results,
            result => result.MethodName.ToString() == "NestedRuntimeAsync");
        Assert.Equal(
            "DotnetInspect.Cli.Tests.GenericAsyncOuter<T1, T2>.BufferedAsyncEnumerable",
            method.DeclaringType.ToString());
    }

    [Fact]
    public void Classify_ClassifiesRealAsyncMethodAsAsync()
    {
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var stream = File.OpenRead(assemblyPath);

        var results = Classify(stream);

        Assert.Contains(
            results,
            method => method.MethodName.ToString() == "AwaitPointerReceiver"
                && method.DeclaringType.ToString() == UnsafeAsyncFixture
                && method.Classification == MethodClassification.RuntimeAsync);
    }

    [Fact]
    public void Classify_RealTestAssemblyWithMtpSurfaceStaysWithinBudget()
    {
        const long LargeAssemblyThresholdBytes = 8L * 1024 * 1024;
        string assemblyPath =
            typeof(MethodClassificationScannerTests).Assembly.Location;
        long assemblyLength = new FileInfo(assemblyPath).Length;
        Assert.True(
            assemblyLength > LargeAssemblyThresholdBytes,
            $"Expected the MTP test assembly to exceed 8 MiB; actual size was {assemblyLength:N0} bytes.");

        // P/Invoke and pointer rows over the large assembly stay within the
        // identity budget. Async is not asked: this assembly carries a spoofed
        // state-machine attribute, and adversarial async outcomes belong to
        // the StateMachineRelationshipIndex tests.
        using var peReader = new PEReader(File.OpenRead(assemblyPath));
        ClassificationQuestion pinvoke = new(MethodClassificationAnalyzer.PInvoke, ClassificationClosing.Rows);
        ClassificationQuestion pointer = new(MethodClassificationAnalyzer.PointerSignature, ClassificationClosing.Rows);
        MethodClassificationResult result = MethodClassificationQuery.Execute(peReader, [pinvoke, pointer]);

        Assert.Null(result.Critical);
        Assert.Contains(
            Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(pinvoke)).Methods,
            method => method.MethodName.ToString() == nameof(SamplePInvokeClass.GetCurrentProcessId));
        Assert.Contains(
            Assert.IsType<ClassificationAnswer.Rows>(result.AnswerTo(pointer)).Methods,
            method => method.MethodName.ToString() == nameof(SampleUnsafeClass.UnsafePointerMethod));
    }

    [Fact]
    public void Classify_DoesNotClassifyNonAsyncTaskMethods()
    {
        string assemblyPath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var stream = File.OpenRead(assemblyPath);

        var results = Classify(stream);

        Assert.DoesNotContain(
            results,
            method => method.MethodName.ToString() == "GetTask"
                && method.DeclaringType.ToString() == PointerTargetFixture);
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

    private static MemoryStream BuildAssemblyWithNestedGenericRuntimeAsyncMethod()
    {
        const MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000;
        var ab = new PersistedAssemblyBuilder(
            new AssemblyName("NestedRuntimeAsyncFixture"),
            typeof(object).Assembly);
        var module = ab.DefineDynamicModule("NestedRuntimeAsyncFixture");
        var outer = module.DefineType(
            "DotnetInspect.Cli.Tests.GenericAsyncOuter`2",
            TypeAttributes.Public | TypeAttributes.Class);
        outer.DefineGenericParameters("T1", "T2");
        var nested = outer.DefineNestedType(
            "BufferedAsyncEnumerable",
            TypeAttributes.NestedPublic | TypeAttributes.Class);
        var method = nested.DefineMethod(
            "NestedRuntimeAsync",
            MethodAttributes.Public,
            typeof(Task),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        method.SetImplementationFlags(AsyncImplFlag);
        nested.CreateType();
        outer.CreateType();

        var stream = new MemoryStream();
        ab.Save(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Classify_PlatformAssembly_FindsUnsafeMethods()
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
        var results = Classify(stream);

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
    public static uint CallsUnsafeAs(ref int value) => Unsafe.As<int, uint>(ref value);
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

    public static int CallGetCurrentProcessId() =>
        GetCurrentProcessId();
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
