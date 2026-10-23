using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OCS.Application.Common;

namespace OCS.Api.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);
        if (result.IsSuccess)
        {
            return controller.Ok(result.Data);
        }

        var error = result.Error!;
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new InvalidOperationException("Unmapped application error type.")
        };
        var response = controller.Problem(statusCode: status, title: error.Message);
        var problem = (ProblemDetails)response.Value!;
        // The shared factory may already have added these keys.
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier;
        return response;
    }
}
