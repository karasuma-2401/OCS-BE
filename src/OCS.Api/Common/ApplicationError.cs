namespace OCS.Api.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    RateLimited,
    ServiceUnavailable
}

public sealed record ApplicationError
{
    public ApplicationError(
        string code,
        string message,
        ErrorType type,
        IReadOnlyDictionary<string, object?>? extensions = null)
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
        Extensions = extensions;
    }

    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }
    public IReadOnlyDictionary<string, object?>? Extensions { get; }

    public static ApplicationError NotFound(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        => new(code, message, ErrorType.NotFound, extensions);

    public static ApplicationError Validation(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        => new(code, message, ErrorType.Validation, extensions);

    public static ApplicationError Conflict(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        => new(code, message, ErrorType.Conflict, extensions);

    public static ApplicationError Unauthorized(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        => new(code, message, ErrorType.Unauthorized, extensions);

    public static ApplicationError Forbidden(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        => new(code, message, ErrorType.Forbidden, extensions);

    public static ApplicationError RateLimited(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        => new(code, message, ErrorType.RateLimited, extensions);

    public static ApplicationError ServiceUnavailable(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
        => new(code, message, ErrorType.ServiceUnavailable, extensions);
}
