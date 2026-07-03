
public class ExtraModernFixtures {
    public async System.Collections.Generic.IAsyncEnumerable<object> GetObjectsAsync()
    {
        var x = new object();
        yield return x;
        await System.Threading.Tasks.Task.Yield();
        System.Console.WriteLine(x);
    }
}
