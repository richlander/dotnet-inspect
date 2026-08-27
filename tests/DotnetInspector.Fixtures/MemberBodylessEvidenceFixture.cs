namespace DotnetInspector.Fixtures;

public abstract class MemberBodylessEvidenceFixture
{
    [Obsolete("native marker")]
    [System.Runtime.InteropServices.DllImport("member-bodyless-native")]
    public static extern unsafe void* Native(int size);

    [Obsolete("abstract marker")]
    public abstract unsafe void Abstract(int* value);

    public int Executable(int value) => value;

    public int Mixed(byte value) => value;

    public unsafe int Mixed(int* value) => *value;

    public int this[byte value] => value;

    public unsafe int this[int* value] => *value;

    public void Generic<T>(T value) { }

    public void Generic<T1, T2>(T1 first, T2 second) { }

    public void Generic(byte value) { }
}

public sealed class MemberBodylessExtensionReceiver;

public static class MemberBodylessExtensionFixture
{
    [System.Runtime.InteropServices.DllImport("member-bodyless-native")]
    public static extern void AttachedNative(
        this MemberBodylessExtensionReceiver receiver);
}
