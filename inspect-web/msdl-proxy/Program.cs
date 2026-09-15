using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MsdlProxy;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.Services.AddHttpClient(MsdlClient.Name, client =>
    client.Timeout = TimeSpan.FromSeconds(30));

builder.Build().Run();
