using BenchmarkDotNet.Running;
using ILInspector.CSharpPrinterBenchmarks;

if (args is ["--validate"])
    return WorkloadValidator.Run(Console.Out);

BenchmarkSwitcher
    .FromAssembly(typeof(CSharpPrinterAllocationBenchmarks).Assembly)
    .Run(args);
return 0;
