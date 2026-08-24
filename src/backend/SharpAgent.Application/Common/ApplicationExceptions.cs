namespace SharpAgent.Application.Common;

/// <summary>Requested entity does not exist. Maps to 404.</summary>
public sealed class NotFoundException(string resource, string id)
    : Exception($"{resource} '{id}' was not found.")
{
    public const string Code = "not_found";

    public string Resource { get; } = resource;
}

/// <summary>State or uniqueness conflict. Maps to 409 with a stable problem code.</summary>
public sealed class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Input validation failure. Maps to 400 with per-field errors.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public const string Code = "validation_error";

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationException ForField(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [field] = [message],
        });
}
