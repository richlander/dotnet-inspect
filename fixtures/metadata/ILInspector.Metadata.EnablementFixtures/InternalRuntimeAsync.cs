using System;
using System.Threading.Tasks;

namespace ILInspector.Metadata.EnablementFixtures;

public static class InternalRuntimeAsync
{
    public static Func<Task> Create() => static async () => await Task.Yield();
}
