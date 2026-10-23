namespace OCS.Application.Common;

public sealed class Result<T>
{
    private Result(bool isSuccess, T? data, ApplicationError? error)
    {
        IsSuccess = isSuccess;
        Data = data;
        Error = error;
    }

    public bool IsSuccess { get; }
    public T? Data { get; }
    public ApplicationError? Error { get; }

    internal static Result<T> Success(T data) => new(true, data, null);

    internal static Result<T> Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }
}

public static class Result
{
    public static Result<T> Success<T>(T data) => Result<T>.Success(data);

    public static Result<T> Failure<T>(ApplicationError error) => Result<T>.Failure(error);
}
