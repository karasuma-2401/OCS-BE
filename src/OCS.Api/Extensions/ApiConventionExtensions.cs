using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Common;

namespace OCS.Api.Extensions;

internal static class ApiConventionExtensions
{
    public static IServiceCollection AddApiConventions(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            problem.Extensions.TryAdd("code", GetErrorCode(problem.Status));
            problem.Extensions.TryAdd(
                "traceId",
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .ToDictionary(
                        entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                        entry => entry.Value!.Errors
                            .Select(error => error.ErrorMessage)
                            .ToArray());

                var details = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                    Type = "https://httpstatuses.com/400"
                };

                details.Extensions["code"] = ApiErrorCodes.ValidationFailed;
                details.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

                var response = new BadRequestObjectResult(details);
                response.ContentTypes.Add("application/problem+json");
                return response;
            };
        });

        return services;
    }

    private static string GetErrorCode(int? statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ApiErrorCodes.BadRequest,
        StatusCodes.Status401Unauthorized => ApiErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ApiErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ApiErrorCodes.NotFound,
        StatusCodes.Status409Conflict => ApiErrorCodes.Conflict,
        _ => ApiErrorCodes.InternalError
    };
}
