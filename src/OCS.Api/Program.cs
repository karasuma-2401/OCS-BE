using dotenv.net;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Extensions;
using OCS.Api.Configuration;
using OCS.Api.Context;
using OCS.Api.Context.Seeds;

DotEnv.Load(new DotEnvOptions(probeForEnv: true, probeLevelsToSearch: 5));

var builder = WebApplication.CreateBuilder(args);

ProductionConfiguration.Validate(builder.Configuration, builder.Environment);

builder.Services.AddOcsApi(builder.Configuration);

var app = builder.Build();

// Handle --seed-demo flag: Apply migrations and seed sample data on demand
if (args.Contains("--seed-demo", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    app.Logger.LogInformation("Database migrated and demo data seeded successfully.");
    return;
}

// Auto-migrate schema in Development when connection string is provided (does NOT seed data)
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    var connString = app.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(connString))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await dbContext.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to apply database migrations on startup.");
        }
    }
}

// if (args.Contains("--seed-demo", StringComparer.Ordinal))
// {
//     if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
//     {
//         throw new InvalidOperationException("Demo seeding is only allowed in Development or Testing.");
//     }

//     await using var scope = app.Services.CreateAsyncScope();
//     await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
//     return;
// }

app.UseOcsApi();

app.Run();

public partial class Program;
