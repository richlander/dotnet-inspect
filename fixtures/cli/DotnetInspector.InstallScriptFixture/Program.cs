string log = Environment.GetEnvironmentVariable("DOTNET_INSTALL_TEST_LOG")
    ?? throw new InvalidOperationException(
        "DOTNET_INSTALL_TEST_LOG was not provided.");

File.WriteAllLines(log, args);
