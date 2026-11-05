using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Security.Claims;
using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OCS.Api.Auditing;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Context.Interceptors;
using OCS.Api.Context.Seeds;
using OCS.Api.HealthChecks;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Middleware;
using OCS.Api.Repositories;
using OCS.Api.Services;
using OCS.Api.Storage;
using Scalar.AspNetCore;
using StackExchange.Redis;

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
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            var addresses = (configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
                .Where(address => !string.IsNullOrWhiteSpace(address)).ToArray();
            // An empty known-proxy list means the middleware would trust everyone, so disable it.
            options.ForwardedHeaders = addresses.Length == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var address in addresses)
            {
                if (!IPAddress.TryParse(address, out var proxy)) throw new InvalidOperationException("Invalid trusted reverse proxy address.");
                options.KnownProxies.Add(proxy);
            }
        });
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
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ICustomerProfileRepository, CustomerProfileRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }

    public static IServiceCollection AddServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

        var emailProvider = configuration["Email:Provider"];
        if (string.Equals(emailProvider, "TestSink", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<TestInboxEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<TestInboxEmailSender>());
        }
        else
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }

        services.AddScoped<IOtpRateLimiter, OtpRateLimiter>();
        services.AddScoped<ILoginRateLimiter, LoginRateLimiter>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountManagementService, AccountManagementService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<ICustomerProfileService, CustomerProfileService>();
        services.AddScoped<IAppointmentReminderJob, AppointmentReminderJob>();
        services.AddScoped<DevelopmentDataSeeder>();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "ocs_antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var issuer = string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]) ? "OCS" : configuration["Jwt:Issuer"]!;
        var audience = string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]) ? "OCS" : configuration["Jwt:Audience"]!;
        var key = string.IsNullOrWhiteSpace(configuration["Jwt:Key"]) ? "ocs-super-secret-key-that-is-at-least-32-bytes-long" : configuration["Jwt:Key"]!;

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Cookies.TryGetValue("ocs_session", out var cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
                        {
                            context.Token = cookieToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var sidClaim = context.Principal?.FindFirst("sid")?.Value
                                       ?? context.Principal?.FindFirst(JwtRegisteredClaimNames.Sid)?.Value;

                        if (string.IsNullOrWhiteSpace(sidClaim) || !Guid.TryParse(sidClaim, out var sessionId))
                        {
                            context.Fail("Missing or invalid session identifier.");
                            return;
                        }

                        var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
                        var timeProvider = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();

                        var session = await unitOfWork.AuthRepository.GetSessionByIdAsync(sessionId, context.HttpContext.RequestAborted);
                        if (session is null)
                        {
                            context.Fail("Session not found.");
                            return;
                        }

                        if (session.RevokedAt.HasValue)
                        {
                            context.Fail("Session has been revoked.");
                            return;
                        }

                        var now = timeProvider.GetUtcNow();
                        if (now >= session.ExpiresAt)
                        {
                            context.Fail("Session has expired.");
                            return;
                        }

                        var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (session.Account is null || session.Account.Status != AccountStatus.Active
                            || session.Account.EmailVerifiedAt is null || !Guid.TryParse(subject, out var accountId)
                            || accountId != session.AccountId)
                        {
                            context.Fail("Account is inactive or locked.");
                            return;
                        }

                        // Authorization must use current roles, not claims from a stale JWT.
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            foreach (var role in identity.FindAll(ClaimTypes.Role).ToList()) identity.RemoveClaim(role);
                            foreach (var role in session.Account.AccountRoles.Select(x => x.Role?.Code).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                                identity.AddClaim(new Claim(ClaimTypes.Role, role!));
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        context.Response.Headers.CacheControl = "no-store";

                        var problem = new ProblemDetails
                        {
                            Status = StatusCodes.Status401Unauthorized,
                            Title = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn."
                        };
                        problem.Extensions["code"] = "UNAUTHORIZED";
                        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

                        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/problem+json";
                        context.Response.Headers.CacheControl = "no-store";

                        var problem = new ProblemDetails
                        {
                            Status = StatusCodes.Status403Forbidden,
                            Title = "Bạn không có quyền thực hiện thao tác này."
                        };
                        problem.Extensions["code"] = "FORBIDDEN";
                        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

                        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                    }
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
                .SetIsOriginAllowed(origin =>
                {
                    if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return uri.Host is "localhost" or "127.0.0.1";
                    }
                    return false;
                })
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
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
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
        app.UseForwardedHeaders();
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

        app.UseCors(CorsPolicyName);

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<CsrfValidationMiddleware>();
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

        var connectionString = app.Configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                RecurringJob.AddOrUpdate<IAppointmentReminderJob>(
                    "appointment-reminder-job",
                    job => job.ExecuteAsync(CancellationToken.None),
                    "*/15 * * * *");
            }
            catch
            {
                // In environments without active database or tests, recurring job registration can be safely ignored
            }
        }

        return app;
    }
}
