using OCS.Api.Extensions;
using OCS.Api.Configuration;
using OCS.Api.Context.Seeds;
using dotenv.net;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;

var builder = WebApplication.CreateBuilder(args);

// Local secrets are loaded in Development; explicit environment variables take precedence.
if (builder.Environment.IsDevelopment())
{
    var envPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".env"));
    if (File.Exists(envPath))
    {
        DotEnv.Load(new DotEnvOptions(envFilePaths: [envPath], overwriteExistingVars: false));
    }
    else
    {
        DotEnv.Load(new DotEnvOptions(probeForEnv: true, probeLevelsToSearch: 6, overwriteExistingVars: false));
    }
    builder.Configuration.AddEnvironmentVariables();
}

ProductionConfiguration.Validate(builder.Configuration, builder.Environment);

builder.Services.AddOcsApi(builder.Configuration);

var app = builder.Build();

if (args.Contains("--check-migrations", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var pending = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetPendingMigrationsAsync();
    var pendingIds = pending.ToArray();
    if (pendingIds.Length > 0)
        throw new InvalidOperationException($"Database migrations are pending: {string.Join(", ", pendingIds)}. Review the auth migration runbook before deployment.");
    return;
}

if (args.Contains("--apply-migrations", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}

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
