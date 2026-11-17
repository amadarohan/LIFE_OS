using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Finance;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Finance;

public sealed record AccountView(
    Guid Id,
    string Name,
    AccountType AccountType,
    string CurrencyCode,
    decimal InitialBalance,
    decimal CurrentBalance,
    bool IsActive,
    string? Notes);

public sealed record TransactionView(
    Guid Id,
    Guid FinancialAccountId,
    string AccountName,
    TransactionType TransactionType,
    decimal Amount,
    decimal SignedAmount,
    string CurrencyCode,
    string? Category,
    string? Description,
    DateOnly TransactionDate,
    bool IsForecast,
    bool IsRecurring);

public sealed record CommitmentView(
    Guid Id,
    string Name,
    string? Description,
    decimal TotalAmount,
    string CurrencyCode,
    decimal InstallmentAmount,
    int InstallmentCount,
    int PaidInstallments,
    int RemainingInstallments,
    decimal RemainingAmount,
    DateOnly? NextDueDate,
    RecurrenceFrequency Frequency,
    CommitmentStatus Status,
    bool IsOverdue);

public sealed record BudgetView(
    Guid Id,
    string Name,
    string Category,
    decimal Amount,
    string CurrencyCode,
    BudgetPeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal SpentAmount,
    decimal RemainingAmount,
    decimal UtilizationPercent,
    bool IsOverBudget);

public sealed record FinanceOverview(
    decimal TotalBalance,
    decimal MonthIncome,
    decimal MonthExpense,
    decimal MonthNet,
    decimal UpcomingCommitmentsAmount,
    int OverdueCommitmentCount,
    IReadOnlyList<AccountView> Accounts);

public sealed record CreateAccountRequest(
    string Name,
    AccountType AccountType,
    string CurrencyCode,
    decimal InitialBalance,
    string? Notes);

public sealed record CreateTransactionRequest(
    Guid FinancialAccountId,
    TransactionType TransactionType,
    decimal Amount,
    string? Category,
    string? Description,
    DateOnly TransactionDate,
    bool IsForecast,
    Guid? RelatedCommitmentId);

public sealed record CreateCommitmentRequest(
    string Name,
    string? Description,
    decimal TotalAmount,
    string CurrencyCode,
    decimal InstallmentAmount,
    int InstallmentCount,
    DateOnly? NextDueDate,
    RecurrenceFrequency Frequency,
    Guid? RelatedProjectId);

public sealed record CreateBudgetRequest(
    string Name,
    string Category,
    decimal Amount,
    string CurrencyCode,
    BudgetPeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record TransactionFilter(
    Guid? AccountId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    TransactionType? TransactionType = null,
    string? Category = null,
    bool IncludeForecast = false);

/// <summary>
/// Finance use cases. Balances are always derived from transactions plus the account's opening
/// balance; nothing stores a running total that could drift out of sync.
/// </summary>
public sealed class FinanceService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public FinanceService(IApplicationDbContext db, IAuditWriter audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AccountView>> ListAccountsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.FinancialAccounts.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(a => a.IsActive);
        }

        var accounts = await _db.ToListAsync(query.OrderBy(a => a.Name), cancellationToken);
        var balances = await ComputeBalancesAsync(cancellationToken);

        return accounts
            .Select(a => new AccountView(
                a.Id,
                a.Name,
                a.AccountType,
                a.CurrencyCode,
                a.InitialBalance,
                balances.GetValueOrDefault(a.Id, a.InitialBalance),
                a.IsActive,
                a.Notes))
            .ToList();
    }

    public Task<Result<Guid>> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var duplicate = await _db.AnyAsync(
                _db.FinancialAccounts.Where(a => a.Name == request.Name.Trim()),
                cancellationToken);

            if (duplicate)
            {
                throw new DomainRuleException("FinancialAccount.DuplicateName", "حسابی با این نام قبلاً ثبت شده است.");
            }

            var account = new FinancialAccount(
                request.Name,
                request.AccountType,
                request.CurrencyCode,
                request.InitialBalance,
                request.Notes);

            _db.Add(account);
            await _audit.WriteAsync(
                nameof(FinancialAccount),
                account.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(account.Id);
        });

    public Task<Result> UpdateAccountAsync(
        Guid id,
        string name,
        AccountType accountType,
        decimal initialBalance,
        string? notes,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var account = await _db.FirstOrDefaultAsync(
                _db.FinancialAccounts.Where(a => a.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FinancialAccount), id);

            account.Update(name, accountType, initialBalance, notes);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetAccountActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var account = await _db.FirstOrDefaultAsync(
                _db.FinancialAccounts.Where(a => a.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FinancialAccount), id);

            account.SetActive(isActive);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<TransactionView>> ListTransactionsAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _db.FinancialTransactions.AsQueryable();

        if (filter.AccountId.HasValue)
        {
            query = query.Where(t => t.FinancialAccountId == filter.AccountId.Value);
        }

        if (filter.From.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(t => t.TransactionDate <= filter.To.Value);
        }

        if (filter.TransactionType.HasValue)
        {
            query = query.Where(t => t.TransactionType == filter.TransactionType.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(t => t.Category == filter.Category);
        }

        if (!filter.IncludeForecast)
        {
            query = query.Where(t => !t.IsForecast);
        }

        var transactions = await _db.ToListAsync(
            query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.CreatedAtUtc),
            cancellationToken);

        var accounts = await _db.ToListAsync(
            _db.FinancialAccounts.Select(a => new { a.Id, a.Name }),
            cancellationToken);

        var names = accounts.ToDictionary(a => a.Id, a => a.Name);

        return transactions
            .Select(t => new TransactionView(
                t.Id,
                t.FinancialAccountId,
                names.GetValueOrDefault(t.FinancialAccountId, string.Empty),
                t.TransactionType,
                t.Amount,
                t.SignedAmount,
                t.CurrencyCode,
                t.Category,
                t.Description,
                t.TransactionDate,
                t.IsForecast,
                t.IsRecurring))
            .ToList();
    }

    public Task<Result<Guid>> RecordTransactionAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var account = await _db.FirstOrDefaultAsync(
                _db.FinancialAccounts.Where(a => a.Id == request.FinancialAccountId),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FinancialAccount), request.FinancialAccountId);

            if (request.RelatedCommitmentId.HasValue)
            {
                var commitmentExists = await _db.AnyAsync(
                    _db.FinancialCommitments.Where(c => c.Id == request.RelatedCommitmentId.Value),
                    cancellationToken);

                if (!commitmentExists)
                {
                    throw new EntityNotFoundException(
                        nameof(FinancialCommitment),
                        request.RelatedCommitmentId.Value);
                }
            }

            var transaction = new FinancialTransaction(
                account.Id,
                request.TransactionType,
                request.Amount,
                account.CurrencyCode,
                request.Category,
                request.Description,
                request.TransactionDate,
                request.IsForecast,
                false,
                request.RelatedCommitmentId,
                _clock.UtcNow);

            _db.Add(transaction);
            await _audit.WriteAsync(
                nameof(FinancialTransaction),
                transaction.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(transaction.Id);
        });

    public Task<Result> UpdateTransactionAsync(
        Guid id,
        TransactionType transactionType,
        decimal amount,
        string? category,
        string? description,
        DateOnly transactionDate,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var transaction = await _db.FirstOrDefaultAsync(
                _db.FinancialTransactions.Where(t => t.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FinancialTransaction), id);

            transaction.UpdateDetails(transactionType, amount, category, description, transactionDate);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeleteTransactionAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var transaction = await _db.FirstOrDefaultAsync(
                _db.FinancialTransactions.Where(t => t.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FinancialTransaction), id);

            _db.Remove(transaction);
            await _audit.WriteAsync(
                nameof(FinancialTransaction),
                id,
                AuditActionType.Deleted,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<CommitmentView>> ListCommitmentsAsync(
        bool includeSettled = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.FinancialCommitments.AsQueryable();
        if (!includeSettled)
        {
            query = query.Where(c => c.Status != CommitmentStatus.Paid
                && c.Status != CommitmentStatus.Cancelled);
        }

        var commitments = await _db.ToListAsync(
            query.OrderBy(c => c.NextDueDate ?? DateOnly.MaxValue),
            cancellationToken);

        var today = _clock.TodayUtc;

        return commitments
            .Select(c => new CommitmentView(
                c.Id,
                c.Name,
                c.Description,
                c.TotalAmount,
                c.CurrencyCode,
                c.InstallmentAmount,
                c.InstallmentCount,
                c.PaidInstallments,
                c.RemainingInstallments,
                c.RemainingAmount,
                c.NextDueDate,
                c.Frequency,
                c.Status,
                c.NextDueDate.HasValue && c.NextDueDate.Value < today
                    && c.Status != CommitmentStatus.Paid
                    && c.Status != CommitmentStatus.Cancelled))
            .ToList();
    }

    public Task<Result<Guid>> CreateCommitmentAsync(
        CreateCommitmentRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            if (request.RelatedProjectId.HasValue)
            {
                var projectExists = await _db.AnyAsync(
                    _db.Projects.Where(p => p.Id == request.RelatedProjectId.Value),
                    cancellationToken);

                if (!projectExists)
                {
                    throw new EntityNotFoundException("Project", request.RelatedProjectId.Value);
                }
            }

            var commitment = new FinancialCommitment(
                request.Name,
                request.Description,
                request.TotalAmount,
                request.CurrencyCode,
                request.InstallmentAmount,
                request.InstallmentCount,
                request.NextDueDate,
                request.Frequency,
                request.RelatedProjectId);

            _db.Add(commitment);
            await _audit.WriteAsync(
                nameof(FinancialCommitment),
                commitment.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(commitment.Id);
        });

    /// <summary>
    /// Registers one installment payment. The domain advances the next due date and settles the
    /// commitment when the last installment is paid.
    /// </summary>
    public Task<Result> RegisterCommitmentPaymentAsync(
        Guid id,
        DateOnly paidOn,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var commitment = await _db.FirstOrDefaultAsync(
                _db.FinancialCommitments.Where(c => c.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FinancialCommitment), id);

            commitment.RegisterPayment(paidOn);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeCommitmentStatusAsync(
        Guid id,
        CommitmentStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var commitment = await _db.FirstOrDefaultAsync(
                _db.FinancialCommitments.Where(c => c.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FinancialCommitment), id);

            commitment.ChangeStatus(status);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<BudgetView>> ListBudgetsAsync(
        CancellationToken cancellationToken = default)
    {
        var budgets = await _db.ToListAsync(
            _db.Budgets.OrderByDescending(b => b.StartDate),
            cancellationToken);

        if (budgets.Count == 0)
        {
            return [];
        }

        var minStart = budgets.Min(b => b.StartDate);
        var maxEnd = budgets.Max(b => b.EndDate);

        var expenses = await _db.ToListAsync(
            _db.FinancialTransactions.Where(t =>
                !t.IsForecast &&
                t.TransactionType == TransactionType.Expense &&
                t.TransactionDate >= minStart &&
                t.TransactionDate <= maxEnd),
            cancellationToken);

        return budgets.Select(b =>
        {
            var spent = expenses
                .Where(t => t.Category == b.Category
                    && t.TransactionDate >= b.StartDate
                    && t.TransactionDate <= b.EndDate)
                .Sum(t => t.Amount);

            var remaining = b.Amount - spent;
            var utilization = b.Amount == 0m
                ? 0m
                : Math.Round(spent * 100m / b.Amount, 1);

            return new BudgetView(
                b.Id,
                b.Name,
                b.Category,
                b.Amount,
                b.CurrencyCode,
                b.PeriodType,
                b.StartDate,
                b.EndDate,
                spent,
                remaining,
                utilization,
                spent > b.Amount);
        }).ToList();
    }

    public Task<Result<Guid>> CreateBudgetAsync(
        CreateBudgetRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var budget = new Budget(
                request.Name,
                request.Category,
                request.Amount,
                request.CurrencyCode,
                request.PeriodType,
                request.StartDate,
                request.EndDate);

            budget.StampCreated(_clock.UtcNow);

            _db.Add(budget);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(budget.Id);
        });

    public Task<Result> UpdateBudgetAsync(
        Guid id,
        string name,
        string category,
        decimal amount,
        BudgetPeriodType periodType,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var budget = await _db.FirstOrDefaultAsync(
                _db.Budgets.Where(b => b.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Budget), id);

            budget.Update(name, category, amount, periodType, startDate, endDate);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeleteBudgetAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var budget = await _db.FirstOrDefaultAsync(
                _db.Budgets.Where(b => b.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Budget), id);

            _db.Remove(budget);
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>Dashboard aggregate. Every figure is computed from stored rows.</summary>
    public async Task<FinanceOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await ListAccountsAsync(includeInactive: false, cancellationToken);
        var today = _clock.TodayUtc;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var monthTransactions = await _db.ToListAsync(
            _db.FinancialTransactions.Where(t =>
                !t.IsForecast &&
                t.TransactionDate >= monthStart &&
                t.TransactionDate <= monthEnd),
            cancellationToken);

        var income = monthTransactions
            .Where(t => t.TransactionType == TransactionType.Income)
            .Sum(t => t.Amount);

        var expense = monthTransactions
            .Where(t => t.TransactionType == TransactionType.Expense)
            .Sum(t => t.Amount);

        var commitments = await ListCommitmentsAsync(includeSettled: false, cancellationToken);

        var upcoming = commitments
            .Where(c => c.NextDueDate.HasValue && c.NextDueDate.Value <= today.AddDays(30))
            .Sum(c => c.InstallmentAmount);

        return new FinanceOverview(
            accounts.Sum(a => a.CurrentBalance),
            income,
            expense,
            income - expense,
            upcoming,
            commitments.Count(c => c.IsOverdue),
            accounts);
    }

    private async Task<Dictionary<Guid, decimal>> ComputeBalancesAsync(CancellationToken cancellationToken)
    {
        var accounts = await _db.ToListAsync(
            _db.FinancialAccounts.Select(a => new { a.Id, a.InitialBalance }),
            cancellationToken);

        var transactions = await _db.ToListAsync(
            _db.FinancialTransactions
                .Where(t => !t.IsForecast)
                .Select(t => new { t.FinancialAccountId, t.TransactionType, t.Amount }),
            cancellationToken);

        var result = accounts.ToDictionary(a => a.Id, a => a.InitialBalance);

        foreach (var transaction in transactions)
        {
            if (!result.ContainsKey(transaction.FinancialAccountId))
            {
                continue;
            }

            var signed = transaction.TransactionType switch
            {
                TransactionType.Income => transaction.Amount,
                TransactionType.Expense => -transaction.Amount,
                TransactionType.Adjustment => transaction.Amount,
                _ => 0m
            };

            result[transaction.FinancialAccountId] += signed;
        }

        return result;
    }
}
