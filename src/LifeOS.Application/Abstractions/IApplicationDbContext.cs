using LifeOS.Domain.Entities.Ai;
using LifeOS.Domain.Entities.Analytics;
using LifeOS.Domain.Entities.Calendar;
using LifeOS.Domain.Entities.Decisions;
using LifeOS.Domain.Entities.Finance;
using LifeOS.Domain.Entities.Goals;
using LifeOS.Domain.Entities.Knowledge;
using LifeOS.Domain.Entities.LifeDomains;
using LifeOS.Domain.Entities.Planning;
using LifeOS.Domain.Entities.Projects;
using LifeOS.Domain.Entities.System;
using LifeOS.Domain.Entities.Tasks;

namespace LifeOS.Application.Abstractions;

/// <summary>
/// Persistence boundary for the Application layer. Implemented by the EF Core context in
/// Infrastructure. Deliberately exposes <see cref="IQueryable{T}"/> rather than <c>DbSet</c>
/// so the Application layer has no EF Core dependency.
/// </summary>
public interface IApplicationDbContext
{
    IQueryable<AppSetting> AppSettings { get; }
    IQueryable<Attachment> Attachments { get; }
    IQueryable<AuditLog> AuditLogs { get; }
    IQueryable<BackupHistory> BackupHistories { get; }

    IQueryable<LifeDomain> LifeDomains { get; }
    IQueryable<LifeAreaMetric> LifeAreaMetrics { get; }

    IQueryable<Project> Projects { get; }
    IQueryable<ProjectMilestone> ProjectMilestones { get; }
    IQueryable<ProjectDependency> ProjectDependencies { get; }
    IQueryable<GoalProject> GoalProjects { get; }

    IQueryable<Goal> Goals { get; }
    IQueryable<GoalKeyResult> GoalKeyResults { get; }

    IQueryable<TaskItem> Tasks { get; }
    IQueryable<TaskDependency> TaskDependencies { get; }
    IQueryable<TaskTimeEntry> TaskTimeEntries { get; }

    IQueryable<WeeklyPlan> WeeklyPlans { get; }
    IQueryable<DailyPlan> DailyPlans { get; }
    IQueryable<TimeBlock> TimeBlocks { get; }

    IQueryable<FinancialAccount> FinancialAccounts { get; }
    IQueryable<FinancialTransaction> FinancialTransactions { get; }
    IQueryable<RecurringTransaction> RecurringTransactions { get; }
    IQueryable<FinancialCommitment> FinancialCommitments { get; }
    IQueryable<Budget> Budgets { get; }

    IQueryable<Note> Notes { get; }
    IQueryable<JournalEntry> JournalEntries { get; }
    IQueryable<Tag> Tags { get; }
    IQueryable<NoteTag> NoteTags { get; }
    IQueryable<KnowledgeLink> KnowledgeLinks { get; }

    IQueryable<Decision> Decisions { get; }
    IQueryable<DecisionOption> DecisionOptions { get; }
    IQueryable<DecisionReview> DecisionReviews { get; }

    IQueryable<CalendarEvent> CalendarEvents { get; }
    IQueryable<Commitment> Commitments { get; }

    IQueryable<ProgressSnapshot> ProgressSnapshots { get; }
    IQueryable<WeeklyReview> WeeklyReviews { get; }

    IQueryable<AiConversation> AiConversations { get; }
    IQueryable<AiMessage> AiMessages { get; }
    IQueryable<AiMemory> AiMemories { get; }
    IQueryable<AiInsight> AiInsights { get; }
    IQueryable<AiAction> AiActions { get; }
    IQueryable<AiExecutionLog> AiExecutionLogs { get; }

    void Add<TEntity>(TEntity entity) where TEntity : class;
    void AddRange<TEntity>(IEnumerable<TEntity> entities) where TEntity : class;
    void Remove<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);

    // Async materialization without an EF Core reference in this layer. Implemented by the
    // DbContext using EF's own async operators.
    Task<List<TEntity>> ToListAsync<TEntity>(IQueryable<TEntity> query, CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync<TEntity>(IQueryable<TEntity> query, CancellationToken cancellationToken = default)
        where TEntity : class;

    Task<int> CountAsync<TEntity>(IQueryable<TEntity> query, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync<TEntity>(IQueryable<TEntity> query, CancellationToken cancellationToken = default);

    // Aggregate loaders. These return fully materialized aggregates (children included) so the
    // Application layer never needs Include() and therefore never needs EF Core.
    Task<LifeDomain?> FindLifeDomainAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Project?> FindProjectAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Goal?> FindGoalAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaskItem?> FindTaskAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WeeklyPlan?> FindWeeklyPlanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DailyPlan?> FindDailyPlanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Decision?> FindDecisionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Note?> FindNoteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Unit of work for multi-entity operations that must be atomic.</summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
