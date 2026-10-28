using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OCS.Api.Auditing;
using OCS.Api.Context;
using OCS.Api.Context.Interceptors;
using OCS.Api.Context.Seeds;
using OCS.Api.HealthChecks;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Repositories;
using OCS.Api.Services;
using OCS.Api.Storage;
using Scalar.AspNetCore;

namespace OCS.Api.Extensions;

public static class ServiceCollectionExtensions
{
    private const string CorsPolicyName = "OcsClients";

    public static IServiceCollection AddOcsApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddApiConventions();
        services.AddOpenApi();
        services.AddHttpContextAccessor();
        services.AddScoped<SystemAuditContext>();
        services.AddScoped<IAuditContext, HttpAuditContext>();

        services
            .AddDatabase(configuration)
            .AddRepositories()
            .AddServices(configuration)
            .AddCaching(configuration)
            .AddBackgroundJobs(configuration)
            .AddObjectStorage(configuration)
            .AddJwtAuthentication(configuration)
            .AddCorsPolicy(configuration)
            .AddDependencyHealthChecks();

        return services;
    }

    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

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

        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }

    public static IServiceCollection AddServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<DevelopmentDataSeeder>();
        return services;
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var issuer = string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]) ? "OCS" : configuration["Jwt:Issuer"]!;
                var audience = string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]) ? "OCS" : configuration["Jwt:Audience"]!;
                var key = string.IsNullOrWhiteSpace(configuration["Jwt:Key"]) ? "ocs-super-secret-key-that-is-at-least-32-bytes-long" : configuration["Jwt:Key"]!;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:3000", "http://localhost:5173"];

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }));

        return services;
    }

    public static IServiceCollection AddCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddDistributedMemoryCache();
            return services;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "ocs:";
        });
        return services;
    }

    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer();
        return services;
    }

    public static IServiceCollection AddObjectStorage(
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
            return services;
        }

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = "auto",
                ForcePathStyle = true
            }));

        return services;
    }

    public static IServiceCollection AddDependencyHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<NeonHealthCheck>("neon", failureStatus: HealthStatus.Unhealthy, tags: ["ready"], timeout: TimeSpan.FromSeconds(10))
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Unhealthy, tags: ["ready"], timeout: TimeSpan.FromSeconds(10))
            .AddCheck<R2HealthCheck>("r2", failureStatus: HealthStatus.Unhealthy, tags: ["ready"], timeout: TimeSpan.FromSeconds(10));
        return services;
    }

    public static WebApplication UseOcsApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(async context =>
        {
            var service = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
            await service.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails { Status = context.HttpContext.Response.StatusCode }
            });
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.UseHttpsRedirection();
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/api/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteAsync
        });
        app.MapHealthChecks("/api/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthCheckResponseWriter.WriteAsync
        });

        return app;
    }
}
