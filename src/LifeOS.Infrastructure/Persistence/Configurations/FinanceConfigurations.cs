using LifeOS.Domain.Entities.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeOS.Infrastructure.Persistence.Configurations;

public sealed class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("FinancialAccounts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.AccountType).EnumAsString().IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(ColumnConventions.CurrencyLength).IsRequired();
        builder.Property(x => x.InitialBalance).Money().IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("FinancialTransactions", t =>
        {
            t.HasCheckConstraint("CK_FinancialTransactions_AmountPositive", "[Amount] > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TransactionType).EnumAsString().IsRequired();
        builder.Property(x => x.Amount).Money().IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(ColumnConventions.CurrencyLength).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(ColumnConventions.NameLength);
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => x.TransactionDate);
        builder.HasIndex(x => new { x.FinancialAccountId, x.TransactionDate });
        builder.HasIndex(x => new { x.IsForecast, x.TransactionDate });

        builder.HasOne(x => x.FinancialAccount)
            .WithMany()
            .HasForeignKey(x => x.FinancialAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FinancialCommitment>()
            .WithMany()
            .HasForeignKey(x => x.RelatedCommitmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class RecurringTransactionConfiguration : IEntityTypeConfiguration<RecurringTransaction>
{
    public void Configure(EntityTypeBuilder<RecurringTransaction> builder)
    {
        builder.ToTable("RecurringTransactions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.TransactionType).EnumAsString().IsRequired();
        builder.Property(x => x.Amount).Money().IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(ColumnConventions.CurrencyLength).IsRequired();
        builder.Property(x => x.Frequency).EnumAsString().IsRequired();
        builder.Property(x => x.Category).HasMaxLength(ColumnConventions.NameLength);
        builder.Property(x => x.Notes).HasMaxLength(ColumnConventions.MediumTextLength);

        builder.HasIndex(x => x.NextOccurrenceDate);

        builder.HasOne(x => x.FinancialAccount)
            .WithMany()
            .HasForeignKey(x => x.FinancialAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FinancialCommitmentConfiguration : IEntityTypeConfiguration<FinancialCommitment>
{
    public void Configure(EntityTypeBuilder<FinancialCommitment> builder)
    {
        builder.ToTable("FinancialCommitments", t =>
        {
            t.HasCheckConstraint(
                "CK_FinancialCommitments_Installments",
                "[PaidInstallments] >= 0 AND [PaidInstallments] <= [InstallmentCount]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ColumnConventions.MediumTextLength);
        builder.Property(x => x.TotalAmount).Money().IsRequired();
        builder.Property(x => x.InstallmentAmount).Money().IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(ColumnConventions.CurrencyLength).IsRequired();
        builder.Property(x => x.Frequency).EnumAsString().IsRequired();
        builder.Property(x => x.Status).EnumAsString().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();
        builder.Property(x => x.UpdatedAtUtc).UtcTimestamp();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.NextDueDate, x.Status });

        builder.HasOne<Domain.Entities.Projects.Project>()
            .WithMany()
            .HasForeignKey(x => x.RelatedProjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("Budgets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(ColumnConventions.NameLength).IsRequired();
        builder.Property(x => x.Amount).Money().IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(ColumnConventions.CurrencyLength).IsRequired();
        builder.Property(x => x.PeriodType).EnumAsString().IsRequired();
        builder.Property(x => x.CreatedAtUtc).UtcTimestamp();

        builder.HasIndex(x => new { x.Category, x.StartDate });
    }
}
