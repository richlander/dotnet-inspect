namespace DotnetInspector.Fixtures;

public abstract class MemberBodylessEvidenceFixture
{
    [Obsolete("native marker")]
    [System.Runtime.InteropServices.DllImport("member-bodyless-native")]
    public static extern unsafe void* Native(int size);

    [Obsolete("abstract marker")]
    public abstract unsafe void Abstract(int* value);

    public int Mixed(byte value) => value;

    public unsafe int Mixed(int* value) => *value;
}
