using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ILInspector.Metadata.MemorySafetyFixtures;

public abstract class MethodImplementationFixtures
{
    public MethodImplementationFixtures() { }

    public int Managed(int value) => value;
    public abstract int Abstract(int value);

    [DllImport("metadata-fixture-not-invoked")]
    public static safe extern int Native(int value);

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static safe extern int InternalCall(int value);

    public int Property { get; private set; }
    public abstract int AbstractProperty { get; set; }
    public event Action Event { add { } remove { } }
    public abstract event Action AbstractEvent;

    public int Field;
}

public delegate int RuntimeMethodImplementationFixture(int value);
