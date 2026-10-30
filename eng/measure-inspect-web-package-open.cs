#:project ../src/DotnetInspect.Web.Interop.Package/DotnetInspect.Web.Interop.Package.csproj
#:property EnablePreviewFeatures=true

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;

// Run the production Browser export in a managed Release host. Each process
// starts with an empty Browser session cache; its second request is warm.
// Build once and run the resulting apphost when measuring TCP bytes with strace.
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: <probe> <package> <version> <framework>");
    return 2;
}

#pragma warning disable CA1416 // Deliberate managed-host measurement of the Browser export.
Console.WriteLine("package\tversion\tframework\tcache\tms\tjsonBytes\tsha256\tresidentBytes");
for (int i = 0; i < 2; i++)
{
    Stopwatch watch = Stopwatch.StartNew();
    string result = await PackageExports.QueryPackage(args[0], args[1], args[2]);
    watch.Stop();
    byte[] json = Encoding.UTF8.GetBytes(result);
    using JsonDocument stats = JsonDocument.Parse(PackageExports.PackageCacheStats());
    Console.WriteLine(string.Join('\t', args[0], args[1], args[2],
        i == 0 ? "cold" : "warm", watch.Elapsed.TotalMilliseconds.ToString("F1"),
        json.Length, Convert.ToHexStringLower(SHA256.HashData(json)),
        stats.RootElement.GetProperty("residentBytes").GetInt64()));
}
return 0;
