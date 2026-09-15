namespace ILInspector.Analysis.LocalThrowFixtures;

public class LocalException : Exception;

public sealed class ChildException : LocalException;

public class GenericException<T> : Exception;

public sealed class SpecializedException : GenericException<int>;

public static class LocalThrowSamples
{
    public static void Direct() => throw new LocalException();

    public static void Derived() => throw new ChildException();

    public static void Generic() => throw new GenericException<int>();

    public static void Specialized() => throw new SpecializedException();

    public static void ConstructOnly() => _ = new LocalException();

    public static void HelperOnly() => Direct();

    public static void External() => throw new ArgumentNullException("value");

    public static void Unknown(Exception value) => throw value;

    public static void Null() => throw null!;

    public static void MultipleSites(bool first)
    {
        if (first)
            throw new LocalException();
        throw new ChildException();
    }

    public static void Caught()
    {
        try
        {
            throw new LocalException();
        }
        catch (LocalException)
        {
        }
    }

    public static void CatchOnly(Action action)
    {
        try
        {
            action();
        }
        catch (LocalException)
        {
        }
    }

    public static void Rethrow()
    {
        try
        {
            Direct();
        }
        catch
        {
            throw;
        }
    }

    public static async Task Deferred()
    {
        await Task.Yield();
        throw new LocalException();
    }
}

public abstract class AbstractThrowSample
{
    public abstract void NoBody();
}
