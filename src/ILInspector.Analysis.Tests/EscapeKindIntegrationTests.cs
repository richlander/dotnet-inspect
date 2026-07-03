using System;
using System.Linq;
using Xunit;

namespace ILInspector.Analysis.Tests;

public class EscapeKindIntegrationTests
{
    [Fact]
    public void TestAsyncIteratorHoistedCapture()
    {
        var index = LibraryBodyIndex.Open(typeof(ModernEscapeTestsFixtures).Assembly.Location);
        var occurrences = index.GetAllocationOccurrences().Values.SelectMany(x => x).ToArray();
        
        var xAlloc = occurrences.FirstOrDefault(o => o.Detail == "newobj" && o.AllocatedType?.Name == "Object");
        Assert.NotNull(xAlloc);
        Assert.Equal(AllocationEscape.Escapes, xAlloc.Escape);
        Assert.Equal(AllocationEscapeKind.None, xAlloc.EscapeKind);
    }
}

public class ModernEscapeTestsFixtures
{
    public async System.Collections.Generic.IAsyncEnumerable<object> GetObjectsAsync()
    {
        var x = new object();
        yield return x;
        await System.Threading.Tasks.Task.Yield();
        System.Console.WriteLine(x);
    }
}
