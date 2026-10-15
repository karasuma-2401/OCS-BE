using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OCS.Application.Abstractions.Persistence;
using OCS.Application.Abstractions.Auditing;
using OCS.Infrastructure.HealthChecks;
using OCS.Infrastructure.Persistence;
using OCS.Infrastructure.Persistence.Interceptors;
using OCS.Infrastructure.Storage;

namespace OCS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddCaching(configuration);
        services.AddBackgroundJobs(configuration);
        services.AddObjectStorage(configuration);
        services.AddDependencyHealthChecks();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    private static void AddDependencyHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<NeonHealthCheck>(
                "neon",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(10))
            .AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(10))
            .AddCheck<R2HealthCheck>(
                "r2",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(10));
    }

    private static void AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.TryAddScoped<SystemAuditContext>();
        services.TryAddScoped<IAuditContext>(provider => provider.GetRequiredService<SystemAuditContext>());
        services.AddScoped<TimestampSaveChangesInterceptor>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString);
            }

            options.AddInterceptors(
                provider.GetRequiredService<TimestampSaveChangesInterceptor>(),
                provider.GetRequiredService<AuditSaveChangesInterceptor>());
        });
    }

    private static void AddCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddDistributedMemoryCache();
            return;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "ocs:";
        });
    }

    private static void AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer();
    }

    private static void AddObjectStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(R2Options.SectionName);
        var options = section.Get<R2Options>() ?? new R2Options();

        services.Configure<R2Options>(section);

        if (string.IsNullOrWhiteSpace(options.ServiceUrl) ||
            string.IsNullOrWhiteSpace(options.AccessKeyId) ||
            string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            return;
        }

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = "auto",
                ForcePathStyle = true
            }));
    }
}
