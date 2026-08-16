namespace CiChangeDetection;

internal static class GateAssertions
{
    internal static void AssertAll(
        IReadOnlyDictionary<string, string> values,
        string expected)
    {
        if (values.Values.Any(value => value != expected))
        {
            throw new InvalidOperationException(
                $"Expected every output to be {expected}, got {FormatValues(values)}.");
        }
    }

    internal static void AssertRouting(
        IReadOnlyDictionary<string, string> values,
        string selected,
        string notSelected)
    {
        if (values[selected] != "true" || values[notSelected] != "false")
        {
            throw new InvalidOperationException(
                $"Expected {selected}=true and {notSelected}=false, got " +
                FormatValues(values));
        }
    }

    internal static void AssertDetectionFails(
        DetectionHarness harness,
        DetectionScenario scenario)
    {
        try
        {
            harness.Run(scenario);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.StartsWith(
                "Change detection exited ",
                StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            "The Actions-compatible shell did not stop after a failed command.");
    }

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

    internal static string FormatValues(
        IReadOnlyDictionary<string, string> values) =>
        $"[{string.Join(", ", values.Select(item => $"{item.Key}={item.Value}"))}]";
}
