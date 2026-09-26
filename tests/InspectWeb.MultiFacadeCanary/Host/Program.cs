extern alias AlphaFixture;
extern alias BetaFixture;

Console.WriteLine(
    "ts-jsexport multi-facade browser canary ready: "
    + $"{typeof(AlphaFixture::MultiFacade.Shared.Exports).Assembly.GetName().Name}"
    + " + "
    + $"{typeof(BetaFixture::MultiFacade.Shared.Exports).Assembly.GetName().Name}");
