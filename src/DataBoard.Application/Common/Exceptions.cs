namespace DataBoard.Application.Common;

/// <summary>Input is invalid. Maps to HTTP 400.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Not signed in or bad credentials. Maps to HTTP 401.</summary>
public sealed class UnauthorizedException(string message = "Authentication is required.") : Exception(message);

/// <summary>Signed in but not allowed. Maps to HTTP 403.</summary>
public sealed class ForbiddenException(string message = "You do not have permission to do this.") : Exception(message);

/// <summary>Resource does not exist, or the caller must not learn that it does. Maps to HTTP 404.</summary>
public sealed class NotFoundException(string message = "The requested resource was not found.") : Exception(message);

/// <summary>State conflict such as a duplicate. Maps to HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
