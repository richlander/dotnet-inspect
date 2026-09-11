namespace CiChangeDetection;

internal static class GateAssertions
{
    internal static void AssertInvalidOperation(
        Action action,
        string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                expectedMessage,
                StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected InvalidOperationException containing '{expectedMessage}'.");
    }
}
