namespace Tutor365.Domain.Exceptions;

public abstract class AppException : Exception
{
    public string ErrorCode { get; }
    protected AppException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
}

public class NotFoundException : AppException
{
    public NotFoundException(string entity, object key)
        : base($"{entity.ToUpperInvariant()}_NOT_FOUND", $"{entity} '{key}' could not be found.") { }
    public NotFoundException(string errorCode, string message, bool _) : base(errorCode, message) { }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message = "You do not have permission to access this resource.")
        : base("FORBIDDEN", message) { }
}

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string errorCode = "UNAUTHORIZED", string message = "Authentication is required.")
        : base(errorCode, message) { }
}

public class ConflictException : AppException
{
    public ConflictException(string errorCode, string message) : base(errorCode, message) { }
}

public class BusinessRuleException : AppException
{
    public BusinessRuleException(string errorCode, string message) : base(errorCode, message) { }
}

public class AppValidationException : AppException
{
    public IDictionary<string, string[]> Errors { get; }
    public AppValidationException(IDictionary<string, string[]> errors)
        : base("VALIDATION_FAILED", "One or more validation errors occurred.") => Errors = errors;
    public AppValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = new[] { error } }) { }
}
