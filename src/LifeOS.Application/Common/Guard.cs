using LifeOS.Domain.Common;

namespace LifeOS.Application.Common;

/// <summary>
/// Converts domain rule violations and persistence failures into <see cref="Result"/> values.
/// Every use case goes through this so error handling is uniform and no exception escapes to the
/// UI as an unhandled crash.
/// </summary>
internal static class Guard
{
    public static async Task<Result> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return Result.Success();
        }
        catch (DomainRuleException ex)
        {
            return Result.Failure(ex.Rule, ex.Message);
        }
        catch (EntityNotFoundException ex)
        {
            return Result.Failure(ErrorCodes.NotFound, ex.Message);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(
                ErrorCodes.Concurrency,
                "این رکورد توسط عملیات دیگری تغییر کرده است. صفحه را بازخوانی کنید و دوباره تلاش کنید.");
        }
        catch (PersistenceConflictException ex)
        {
            return Result.Failure(ErrorCodes.Conflict, ex.Message);
        }
    }

    public static async Task<Result<T>> RunAsync<T>(Func<Task<Result<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (DomainRuleException ex)
        {
            return Result.Failure<T>(ex.Rule, ex.Message);
        }
        catch (EntityNotFoundException ex)
        {
            return Result.Failure<T>(ErrorCodes.NotFound, ex.Message);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<T>(
                ErrorCodes.Concurrency,
                "این رکورد توسط عملیات دیگری تغییر کرده است. صفحه را بازخوانی کنید و دوباره تلاش کنید.");
        }
        catch (PersistenceConflictException ex)
        {
            return Result.Failure<T>(ErrorCodes.Conflict, ex.Message);
        }
    }

    public static Result<T> NotFound<T>(string entityType, Guid id)
        => Result.Failure<T>(ErrorCodes.NotFound, $"{entityType} با شناسه {id} یافت نشد.");

    public static Result NotFound(string entityType, Guid id)
        => Result.Failure(ErrorCodes.NotFound, $"{entityType} با شناسه {id} یافت نشد.");

    /// <summary>
    /// Variant for use cases that decide their own failure inside the body. Named distinctly from
    /// <see cref="RunAsync(Func{Task})"/> because <c>Func&lt;Task&lt;Result&gt;&gt;</c> is
    /// covariant to <c>Func&lt;Task&gt;</c> and would otherwise be ambiguous.
    /// </summary>
    public static async Task<Result> RunResultAsync(Func<Task<Result>> action)
    {
        try
        {
            return await action();
        }
        catch (DomainRuleException ex)
        {
            return Result.Failure(ex.Rule, ex.Message);
        }
        catch (EntityNotFoundException ex)
        {
            return Result.Failure(ErrorCodes.NotFound, ex.Message);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(
                ErrorCodes.Concurrency,
                "این رکورد توسط عملیات دیگری تغییر کرده است. صفحه را بازخوانی کنید و دوباره تلاش کنید.");
        }
        catch (PersistenceConflictException ex)
        {
            return Result.Failure(ErrorCodes.Conflict, ex.Message);
        }
    }
}
