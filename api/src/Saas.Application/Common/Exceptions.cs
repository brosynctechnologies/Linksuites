namespace Saas.Application.Common;

/// <summary>Business errors; the API turns each into a ProblemDetails response.</summary>
public abstract class AppException(string message, int statusCode, string? code = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
    /// <summary>Extra ProblemDetails members (for example the list a 409 refers to). Never clinical text.</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}

public sealed class NotFoundException(string what) : AppException($"{what} was not found.", 404, "not_found");

public sealed class ConflictException(string message, string? code = null) : AppException(message, 409, code ?? "conflict");

public sealed class ForbiddenException(string message = "You do not have access to this.") : AppException(message, 403, "forbidden");

public sealed class PlanLimitException(string message) : AppException(message, 402, "plan_limit");

public sealed class BadRequestException(string message, IDictionary<string, string[]>? errors = null)
    : AppException(message, 400, "bad_request")
{
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static BadRequestException Field(string field, string message) =>
        new(message, new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class ServiceUnavailableException(string message) : AppException(message, 503, "unavailable");
