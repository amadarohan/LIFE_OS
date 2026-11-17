namespace LifeOS.Application.Common;

/// <summary>
/// Explicit outcome of an application operation. Failures carry a machine-readable code and a
/// Persian message that is safe to show in the UI.
/// </summary>
public sealed class Result
{
    private Result(bool isSuccess, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string errorCode, string errorMessage)
        => new(false, errorCode, errorMessage);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(string errorCode, string errorMessage)
        => Result<T>.Failure(errorCode, errorMessage);
}

public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string errorCode, string errorMessage)
        => new(false, default, errorCode, errorMessage);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, string, TOut> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(ErrorCode!, ErrorMessage!);
}

/// <summary>Standard error codes shared across features.</summary>
public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string Validation = "validation";
    public const string Conflict = "conflict";
    public const string Concurrency = "concurrency";
    public const string Forbidden = "forbidden";
    public const string DependencyCycle = "dependency_cycle";
    public const string StaleProposal = "stale_proposal";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string PrivacyBlocked = "privacy_blocked";
    public const string Unexpected = "unexpected";
}
