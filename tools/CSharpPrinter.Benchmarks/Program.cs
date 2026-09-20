using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using ILInspector.CSharpPrinterBenchmarks;

if (args is ["--validate"])
    return WorkloadValidator.Run(Console.Out);

bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
if (smoke)
    args = args.Where(arg => !StringComparer.Ordinal.Equals(arg, "--smoke")).ToArray();

Job job = (smoke ? Job.ShortRun : Job.Default)
    .WithToolchain(InProcessEmitToolchain.Instance)
    .WithId(smoke ? "Smoke" : "InProcess");
IConfig config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job);

BenchmarkSwitcher
    .FromAssembly(typeof(CSharpPrinterAllocationBenchmarks).Assembly)
    .Run(args, config);
return 0;
