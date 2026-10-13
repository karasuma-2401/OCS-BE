namespace OCS.Application.Common;

public sealed record Result<T>(bool IsSuccess, T? Data, string? Error);

public static class Result
{
    public static Result<T> Success<T>(T data) => new(true, data, null);

    public static Result<T> Failure<T>(string error) => new(false, default, error);
}
