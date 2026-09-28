using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using DotnetInspector.PackageHouseCliBenchmarks;

if (args is ["--validate"])
    return await PackagePayloadCliBenchmarks.ValidateAsync(Console.Out);

bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
if (smoke)
    args = args.Where(arg => !StringComparer.Ordinal.Equals(arg, "--smoke")).ToArray();

Job job = Job.Default
    .WithToolchain(InProcessEmitToolchain.Instance)
    .WithInvocationCount(1)
    .WithUnrollFactor(1)
    .WithWarmupCount(smoke ? 2 : 8)
    .WithIterationCount(smoke ? 3 : 30)
    .WithId(smoke ? "Smoke" : "InProcess");
IConfig config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job);

BenchmarkRunner.Run<PackagePayloadCliBenchmarks>(config, args);
return 0;
