using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.CSharpPrinterBenchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Method)]
public class CSharpPrinterAllocationBenchmarks
{
    MetadataSource? _source;
    IrFunction? _raisedFunction;
    string? _expectedOutput;
    WorkloadDefinition? _definition;
    int _methodToken;

    [ParamsSource(nameof(Workloads))]
    public string Workload { get; set; } = null!;

    public static IEnumerable<string> Workloads => WorkloadCatalog.Ids;

    [GlobalSetup]
    public void Setup()
    {
        _definition = WorkloadCatalog.Get(Workload);
        _methodToken = WorkloadCatalog.ResolveUniqueMethodToken(_definition);
        _source = MetadataSource.Open(_definition.AssemblyPath);
        _raisedFunction = Import();
        _expectedOutput = RequireOutput(CSharpPrinter.PrintRaised(_raisedFunction));

        string renderOnlyOutput = RequireOutput(CSharpPrinter.Print(_raisedFunction));
        if (!StringComparer.Ordinal.Equals(_expectedOutput, renderOnlyOutput))
        {
            throw new InvalidOperationException(
                $"Render-only output differs from product-path output for {Workload}.");
        }
    }

    [Benchmark]
    public int RenderOnly()
        => RequireOutput(CSharpPrinter.Print(_raisedFunction!)).Length;

    [Benchmark]
    public int ProductPath()
        => RequireOutput(CSharpPrinter.PrintRaised(Import())).Length;

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            string renderOnlyOutput = RequireOutput(CSharpPrinter.Print(_raisedFunction!));
            string productPathOutput = RequireOutput(CSharpPrinter.PrintRaised(Import()));
            if (!StringComparer.Ordinal.Equals(_expectedOutput, renderOnlyOutput)
                || !StringComparer.Ordinal.Equals(_expectedOutput, productPathOutput))
            {
                throw new InvalidOperationException(
                    $"Measured output changed for {Workload}.");
            }
        }
        finally
        {
            _source?.Dispose();
        }
    }

    IrFunction Import()
    {
        IrFunction function = IrImporter.Import(
            _source!,
            _definition!.TypeName,
            _definition.MethodName,
            publicOnly: false)
        ?? throw new InvalidOperationException(
            $"Could not import {_definition.TypeName}.{_definition.MethodName}.");

        return function.MetadataToken == _methodToken
            ? function
            : throw new InvalidOperationException(
                $"Imported {_definition.TypeName}.{_definition.MethodName} "
                + $"as 0x{function.MetadataToken:X8}; "
                + $"expected 0x{_methodToken:X8}.");
    }

    internal static string RequireOutput(DecompilerResult result)
        => result.Succeeded && result.Output is { Length: > 0 } output
            ? output
            : throw new InvalidOperationException(
                "C# printer did not produce non-empty output.");

    internal static void ValidateOutputGuard()
    {
        try
        {
            RequireOutput(
                new DecompilerResult(
                    "",
                    DecompilationFidelity.Full,
                    []));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(
            "C# printer empty-output validation did not fail.");
    }
}
