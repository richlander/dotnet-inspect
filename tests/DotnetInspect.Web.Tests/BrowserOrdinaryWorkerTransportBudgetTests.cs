using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetInspect.Web.Tests;

public sealed class BrowserOrdinaryWorkerTransportBudgetTests
{
    [Fact]
    public void ManagedTransportLimitsMatchTheProductionWorker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
            "inspect-web", "src", "engine-worker-ordinary.ts")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string source = File.ReadAllText(Path.Combine(directory.FullName,
            "inspect-web", "src", "engine-worker-ordinary.ts"));
        Assert.Equal(BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerTransportJsonCharacters,
            ReadLimit("engineWorkerOrdinaryMaximumJsonCharacters"));
        Assert.Equal(BrowserOrdinaryWorkerJsonBudget.MaxOrdinaryWorkerTransportCollectionEntries,
            ReadLimit("engineWorkerOrdinaryMaximumCollectionEntries"));

        int ReadLimit(string name)
        {
            var match = Regex.Match(source, $@"export const {name} = ([\d_]+);");
            Assert.True(match.Success, $"Missing production Worker limit {name}.");
            return int.Parse(match.Groups[1].Value.Replace("_", ""), CultureInfo.InvariantCulture);
        }
    }
}
