using OCS.Api.Extensions;
using OCS.Api.Configuration;
using OCS.Api.Context.Seeds;

var builder = WebApplication.CreateBuilder(args);

ProductionConfiguration.Validate(builder.Configuration, builder.Environment);

builder.Services.AddOcsApi(builder.Configuration);

var app = builder.Build();

if (args.Contains("--seed-demo", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    return;
}

app.UseOcsApi();

app.Run();

public partial class Program;
