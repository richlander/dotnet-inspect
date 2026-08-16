namespace ILInspector.Decompiler.Tests;

// Issue #1608: rectangular (multi-dimensional) array element/creation pseudo-members.
// The CLR models int[,] get/set/address and construction as calls to runtime-
// generated Get/Set/Address members and a rank-shaped .ctor; the printer must lower
// them to C# indexer / array-creation syntax (a[i, j], a[i, j] = v, new int[n0, n1]).
public static class RectangularArraySamples
{
    public static int MdGet(int[,] a, int i, int j) => a[i, j];
    public static void MdSet(int[,] a, int i, int j, int v) => a[i, j] = v;
    public static ref int MdAddress(int[,] a, int i, int j) => ref a[i, j];
    public static int[,] MdNew() => new int[3, 4];
    public static int[,][] MdNewJaggedElement() => new int[2, 3][];
    public static int Md3Get(int[,,] a, int i, int j, int k) => a[i, j, k];
    public static int SideEffects(int[,] a, ref int i, ref int j) => a[i++, ++j];

    // Address forwarded by ref/out to exercise the lvalue/keyword paths.
    public static void MdRefArg(int[,] a, int i, int j) => Inc(ref a[i, j]);
    public static void MdOutArg(int[,] a, int i, int j) => Zero(out a[i, j]);
    static void Inc(ref int x) => x++;
    static void Zero(out int x) => x = 0;

    // Canaries that must stay unchanged: GetLength/Rank, single-dim, jagged.
    public static int MdLength(int[,] a) => a.GetLength(0);
    public static int MdRank(int[,] a) => a.Rank;
    public static int SingleDim(int[] a, int i) => a[i];
    public static int Jagged(int[][] a, int i, int j) => a[i][j];
}

// Issue #1654: ordinary newarr over an array-typed element must put the new
// length in the outer rank, e.g. new int[n][] rather than new int[][n].
public static class JaggedArrayCreationSamples
{
    public static int[][] J2() => new int[3][];
    public static int[][][] J3() => new int[3][][];
    public static string[][] JStr() => new string[5][];
    public static int[][] JVar(int n) => new int[n][];
    public static int[][,] JMdElement() => new int[2][,];
    public static int[][][,] JSzThenMdElement() => new int[2][][,];
    public static int[][,][] JMdThenSzElement() => new int[2][,][];

    public static int[] SingleDimNew(int n) => new int[n];
    public static int[] ArrayLiteral() => new[] { 1, 2, 3 };
}

// Canary: a user type declaring its own Get/Set must NOT be rewritten as an
// indexer — only TypeRefKind.Array receivers are the rectangular pseudo-members.
public sealed class UserGridSample
{
    public int Get(int i, int j) => i + j;
    public void Set(int i, int j, int v) { }
}

public static class UserGridCalls
{
    public static int UserGet(UserGridSample g, int i, int j) => g.Get(i, j);
    public static void UserSet(UserGridSample g, int i, int j, int v) => g.Set(i, j, v);
}
