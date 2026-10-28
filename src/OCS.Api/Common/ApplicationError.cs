namespace OCS.Api.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden
}

public sealed record ApplicationError
{
    public ApplicationError(string code, string message, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        Code = code;
        Message = message;
        Type = type;
    }

    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }

    public static ApplicationError NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static ApplicationError Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static ApplicationError Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static ApplicationError Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static ApplicationError Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
