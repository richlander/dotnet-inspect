namespace Shared;

// Preserves the root-to-private-use-site shape from dotnet-inspect commit
// ac9e9b4d2be08d4cdeeb851aafef3811bdcb35e1:
// MemberSourceDiffPresentationAdapter.Create -> CreateMappedTextDiff -> AddChange.
public static class RootPathEntry
{
    public static void Create() => CreateMappedTextDiff();

    public static void CreateAlternative() => CreateMappedTextDiff();

    public static void CreateOuter() => Create();

    internal static void CreateMappedTextDiff()
    {
        AddChange();
        AddChange();
    }

    static void AddChange()
    {
    }

    public static int Value
    {
        get
        {
            AccessorUse();
            return 0;
        }
        private set => AccessorUse();
    }

    public static void AssignValue() => Value = 1;

    static void AccessorUse()
    {
    }

    public static void CycleRoot() => CycleA();

    static void CycleA() => CycleB();

    static void CycleB()
    {
        CycleA();
        CycleUse();
    }

    static void CycleUse()
    {
    }

    public static async Task AsyncRoot()
    {
        await Task.Yield();
        AsyncUse();
    }

    static void AsyncUse()
    {
    }
}
