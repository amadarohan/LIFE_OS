using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Finance;

public sealed class FinancialAccount : AuditableEntity, IConcurrencyTracked
{
    private FinancialAccount() { }

    public FinancialAccount(string name, AccountType accountType, string currencyCode, decimal initialBalance, string? notes)
    {
        Rename(name);
        AccountType = accountType;
        CurrencyCode = NormalizeCurrency(currencyCode);
        InitialBalance = initialBalance;
        Notes = notes;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public AccountType AccountType { get; private set; }
    public string CurrencyCode { get; private set; } = "IRR";
    public decimal InitialBalance { get; private set; }
    public bool IsActive { get; private set; }
    public string? Notes { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("FinancialAccount.NameRequired", "نام حساب الزامی است.");
        }

        Name = name.Trim();
    }

    public void Update(string name, AccountType accountType, decimal initialBalance, string? notes)
    {
        Rename(name);
        AccountType = accountType;
        InitialBalance = initialBalance;
        Notes = notes;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    internal static string NormalizeCurrency(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new DomainRuleException("Finance.CurrencyRequired", "واحد پول الزامی است.");
        }

        var normalized = currencyCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new DomainRuleException("Finance.InvalidCurrency", "کد واحد پول باید سه حرفی باشد (مثلاً IRR یا IRT).");
        }

        return normalized;
    }
}

public sealed class FinancialTransaction : Entity
{
    private FinancialTransaction() { }

    public FinancialTransaction(
        Guid financialAccountId,
        TransactionType transactionType,
        decimal amount,
        string currencyCode,
        string? category,
        string? description,
        DateOnly transactionDate,
        bool isForecast,
        bool isRecurring,
        Guid? relatedCommitmentId,
        DateTimeOffset createdAtUtc)
    {
        if (amount <= 0m)
        {
            throw new DomainRuleException("FinancialTransaction.AmountPositive", "مبلغ تراکنش باید بزرگ‌تر از صفر باشد.");
        }

        FinancialAccountId = financialAccountId;
        TransactionType = transactionType;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        CurrencyCode = FinancialAccount.NormalizeCurrency(currencyCode);
        Category = category;
        Description = description;
        TransactionDate = transactionDate;
        IsForecast = isForecast;
        IsRecurring = isRecurring;
        RelatedCommitmentId = relatedCommitmentId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid FinancialAccountId { get; private set; }
    public TransactionType TransactionType { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = "IRR";
    public string? Category { get; private set; }
    public string? Description { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public bool IsForecast { get; private set; }
    public bool IsRecurring { get; private set; }
    public Guid? RelatedCommitmentId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public FinancialAccount? FinancialAccount { get; private set; }

    /// <summary>Signed effect on the account balance. Forecast rows are never included in actuals.</summary>
    public decimal SignedAmount => TransactionType switch
    {
        TransactionType.Income => Amount,
        TransactionType.Expense => -Amount,
        TransactionType.Transfer => 0m,
        TransactionType.Adjustment => Amount,
        _ => 0m
    };

    public void UpdateDetails(
        TransactionType transactionType,
        decimal amount,
        string? category,
        string? description,
        DateOnly transactionDate)
    {
        if (amount <= 0m)
        {
            throw new DomainRuleException("FinancialTransaction.AmountPositive", "مبلغ تراکنش باید بزرگ‌تر از صفر باشد.");
        }

        TransactionType = transactionType;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Category = category;
        Description = description;
        TransactionDate = transactionDate;
    }
}

public sealed class RecurringTransaction : Entity
{
    private RecurringTransaction() { }

    public RecurringTransaction(
        Guid financialAccountId,
        string name,
        TransactionType transactionType,
        decimal amount,
        string currencyCode,
        RecurrenceFrequency frequency,
        DateOnly startDate,
        DateOnly? endDate,
        string? category,
        string? notes)
    {
        if (amount <= 0m)
        {
            throw new DomainRuleException("RecurringTransaction.AmountPositive", "مبلغ باید بزرگ‌تر از صفر باشد.");
        }

        if (endDate.HasValue && endDate < startDate)
        {
            throw new DomainRuleException("RecurringTransaction.InvalidRange", "تاریخ پایان باید بعد از تاریخ شروع باشد.");
        }

        FinancialAccountId = financialAccountId;
        Name = name;
        TransactionType = transactionType;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        CurrencyCode = FinancialAccount.NormalizeCurrency(currencyCode);
        Frequency = frequency;
        StartDate = startDate;
        EndDate = endDate;
        NextOccurrenceDate = startDate;
        Category = category;
        Notes = notes;
        IsActive = true;
    }

    public Guid FinancialAccountId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public TransactionType TransactionType { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = "IRR";
    public RecurrenceFrequency Frequency { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateOnly NextOccurrenceDate { get; private set; }
    public bool IsActive { get; private set; }
    public string? Category { get; private set; }
    public string? Notes { get; private set; }

    public FinancialAccount? FinancialAccount { get; private set; }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Advances the next occurrence using calendar-aware arithmetic.</summary>
    public void AdvanceNextOccurrence()
    {
        var next = Frequency switch
        {
            RecurrenceFrequency.Daily => NextOccurrenceDate.AddDays(1),
            RecurrenceFrequency.Weekly => NextOccurrenceDate.AddDays(7),
            RecurrenceFrequency.Monthly => NextOccurrenceDate.AddMonths(1),
            RecurrenceFrequency.Quarterly => NextOccurrenceDate.AddMonths(3),
            RecurrenceFrequency.Yearly => NextOccurrenceDate.AddYears(1),
            _ => NextOccurrenceDate.AddMonths(1)
        };

        NextOccurrenceDate = next;

        if (EndDate.HasValue && NextOccurrenceDate > EndDate.Value)
        {
            IsActive = false;
        }
    }
}

public sealed class FinancialCommitment : AuditableEntity, IConcurrencyTracked
{
    private FinancialCommitment() { }

    public FinancialCommitment(
        string name,
        string? description,
        decimal totalAmount,
        string currencyCode,
        decimal installmentAmount,
        int installmentCount,
        DateOnly? nextDueDate,
        RecurrenceFrequency frequency,
        Guid? relatedProjectId)
    {
        if (totalAmount <= 0m)
        {
            throw new DomainRuleException("FinancialCommitment.TotalPositive", "مبلغ کل باید بزرگ‌تر از صفر باشد.");
        }

        if (installmentCount <= 0)
        {
            throw new DomainRuleException("FinancialCommitment.InstallmentCountPositive", "تعداد اقساط باید بزرگ‌تر از صفر باشد.");
        }

        if (installmentAmount <= 0m)
        {
            throw new DomainRuleException("FinancialCommitment.InstallmentPositive", "مبلغ هر قسط باید بزرگ‌تر از صفر باشد.");
        }

        Name = name;
        Description = description;
        TotalAmount = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        CurrencyCode = FinancialAccount.NormalizeCurrency(currencyCode);
        InstallmentAmount = decimal.Round(installmentAmount, 2, MidpointRounding.AwayFromZero);
        InstallmentCount = installmentCount;
        NextDueDate = nextDueDate;
        Frequency = frequency;
        RelatedProjectId = relatedProjectId;
        Status = CommitmentStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string CurrencyCode { get; private set; } = "IRR";
    public decimal InstallmentAmount { get; private set; }
    public int InstallmentCount { get; private set; }
    public int PaidInstallments { get; private set; }
    public DateOnly? NextDueDate { get; private set; }
    public RecurrenceFrequency Frequency { get; private set; }
    public CommitmentStatus Status { get; private set; }
    public Guid? RelatedProjectId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public decimal RemainingAmount => decimal.Round(TotalAmount - (InstallmentAmount * PaidInstallments), 2);

    public int RemainingInstallments => Math.Max(0, InstallmentCount - PaidInstallments);

    public void RegisterPayment(DateOnly paidOn)
    {
        if (PaidInstallments >= InstallmentCount)
        {
            throw new DomainRuleException("FinancialCommitment.AlreadySettled", "همه اقساط این تعهد پرداخت شده است.");
        }

        PaidInstallments++;

        if (PaidInstallments >= InstallmentCount)
        {
            Status = CommitmentStatus.Paid;
            NextDueDate = null;
            return;
        }

        NextDueDate = Frequency switch
        {
            RecurrenceFrequency.Daily => paidOn.AddDays(1),
            RecurrenceFrequency.Weekly => paidOn.AddDays(7),
            RecurrenceFrequency.Monthly => paidOn.AddMonths(1),
            RecurrenceFrequency.Quarterly => paidOn.AddMonths(3),
            RecurrenceFrequency.Yearly => paidOn.AddYears(1),
            _ => paidOn.AddMonths(1)
        };
    }

    public void ChangeStatus(CommitmentStatus status) => Status = status;

    public void UpdateDetails(string name, string? description, decimal totalAmount, decimal installmentAmount,
        int installmentCount, DateOnly? nextDueDate, RecurrenceFrequency frequency)
    {
        if (totalAmount <= 0m || installmentAmount <= 0m || installmentCount <= 0)
        {
            throw new DomainRuleException("FinancialCommitment.InvalidAmounts", "مبالغ و تعداد اقساط باید معتبر باشند.");
        }

        Name = name;
        Description = description;
        TotalAmount = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        InstallmentAmount = decimal.Round(installmentAmount, 2, MidpointRounding.AwayFromZero);
        InstallmentCount = installmentCount;
        NextDueDate = nextDueDate;
        Frequency = frequency;
    }
}

public sealed class Budget : Entity
{
    private Budget() { }

    public Budget(
        string name,
        string category,
        decimal amount,
        string currencyCode,
        BudgetPeriodType periodType,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (amount <= 0m)
        {
            throw new DomainRuleException("Budget.AmountPositive", "مبلغ بودجه باید بزرگ‌تر از صفر باشد.");
        }

        if (endDate < startDate)
        {
            throw new DomainRuleException("Budget.InvalidRange", "تاریخ پایان بودجه باید بعد از شروع آن باشد.");
        }

        Name = name;
        Category = category;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        CurrencyCode = FinancialAccount.NormalizeCurrency(currencyCode);
        PeriodType = periodType;
        StartDate = startDate;
        EndDate = endDate;
        CreatedAtUtc = DateTimeOffset.MinValue;
    }

    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = "IRR";
    public BudgetPeriodType PeriodType { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void StampCreated(DateTimeOffset now) => CreatedAtUtc = now;

    public void Update(string name, string category, decimal amount, BudgetPeriodType periodType,
        DateOnly startDate, DateOnly endDate)
    {
        if (amount <= 0m)
        {
            throw new DomainRuleException("Budget.AmountPositive", "مبلغ بودجه باید بزرگ‌تر از صفر باشد.");
        }

        if (endDate < startDate)
        {
            throw new DomainRuleException("Budget.InvalidRange", "تاریخ پایان بودجه باید بعد از شروع آن باشد.");
        }

        Name = name;
        Category = category;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        PeriodType = periodType;
        StartDate = startDate;
        EndDate = endDate;
    }
}
