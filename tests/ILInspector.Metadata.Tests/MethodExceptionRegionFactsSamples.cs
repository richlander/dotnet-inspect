namespace ILInspector.Metadata.Tests;

public static class MethodExceptionRegionFactsSamples
{
    public static int NoRegions(int value) => value + 1;

    public static int Catch(int value)
    {
        try
        {
            return 100 / value;
        }
        catch (DivideByZeroException)
        {
            return -1;
        }
    }

    public static int SharedCatchExtent(int value)
    {
        try
        {
            return checked(100 / value);
        }
        catch (DivideByZeroException)
        {
            return -1;
        }
        catch (OverflowException)
        {
            return -2;
        }
    }

    public static int FilterAndFinally(int value)
    {
        try
        {
            if (value < 0)
                throw new InvalidOperationException("negative");

            return value + 1;
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Length > 0)
        {
            return -1;
        }
        finally
        {
            GC.KeepAlive(value);
        }
    }

    public abstract class Shape
    {
        public abstract int NoBody();
    }
}
