using LifeOS.Application.Abstractions;
using LifeOS.Domain.Common;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LifeOS.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the modular monolith. Module boundaries are enforced by the
/// Application layer's service boundaries, not by separate contexts, so cross-module reporting
/// stays a single query instead of an in-memory join.
/// </summary>
public sealed class LifeOsDbContext : DbContext, IApplicationDbContext
{
    private readonly IClock _clock;

    public LifeOsDbContext(DbContextOptions<LifeOsDbContext> options, IClock clock)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BackupHistory> BackupHistories => Set<BackupHistory>();

    public DbSet<LifeDomain> LifeDomains => Set<LifeDomain>();
    public DbSet<LifeAreaMetric> LifeAreaMetrics => Set<LifeAreaMetric>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMilestone> ProjectMilestones => Set<ProjectMilestone>();
    public DbSet<ProjectDependency> ProjectDependencies => Set<ProjectDependency>();
    public DbSet<GoalProject> GoalProjects => Set<GoalProject>();

    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalKeyResult> GoalKeyResults => Set<GoalKeyResult>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<TaskTimeEntry> TaskTimeEntries => Set<TaskTimeEntry>();

    public DbSet<WeeklyPlan> WeeklyPlans => Set<WeeklyPlan>();
    public DbSet<DailyPlan> DailyPlans => Set<DailyPlan>();
    public DbSet<TimeBlock> TimeBlocks => Set<TimeBlock>();

    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<FinancialCommitment> FinancialCommitments => Set<FinancialCommitment>();
    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<Note> Notes => Set<Note>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<NoteTag> NoteTags => Set<NoteTag>();
    public DbSet<KnowledgeLink> KnowledgeLinks => Set<KnowledgeLink>();

    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<DecisionOption> DecisionOptions => Set<DecisionOption>();
    public DbSet<DecisionReview> DecisionReviews => Set<DecisionReview>();

    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<Commitment> Commitments => Set<Commitment>();

    public DbSet<ProgressSnapshot> ProgressSnapshots => Set<ProgressSnapshot>();
    public DbSet<WeeklyReview> WeeklyReviews => Set<WeeklyReview>();

    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiMemory> AiMemories => Set<AiMemory>();
    public DbSet<AiInsight> AiInsights => Set<AiInsight>();
    public DbSet<AiAction> AiActions => Set<AiAction>();
    public DbSet<AiExecutionLog> AiExecutionLogs => Set<AiExecutionLog>();

    // The Application layer sees IQueryable<T> only, so the DbSet<T> surface stays internal to
    // Infrastructure. Explicit implementation keeps the abstraction honest.
    IQueryable<AppSetting> IApplicationDbContext.AppSettings => AppSettings;
    IQueryable<Attachment> IApplicationDbContext.Attachments => Attachments;
    IQueryable<AuditLog> IApplicationDbContext.AuditLogs => AuditLogs;
    IQueryable<BackupHistory> IApplicationDbContext.BackupHistories => BackupHistories;

    IQueryable<LifeDomain> IApplicationDbContext.LifeDomains => LifeDomains;
    IQueryable<LifeAreaMetric> IApplicationDbContext.LifeAreaMetrics => LifeAreaMetrics;

    IQueryable<Project> IApplicationDbContext.Projects => Projects;
    IQueryable<ProjectMilestone> IApplicationDbContext.ProjectMilestones => ProjectMilestones;
    IQueryable<ProjectDependency> IApplicationDbContext.ProjectDependencies => ProjectDependencies;
    IQueryable<GoalProject> IApplicationDbContext.GoalProjects => GoalProjects;

    IQueryable<Goal> IApplicationDbContext.Goals => Goals;
    IQueryable<GoalKeyResult> IApplicationDbContext.GoalKeyResults => GoalKeyResults;

    IQueryable<TaskItem> IApplicationDbContext.Tasks => Tasks;
    IQueryable<TaskDependency> IApplicationDbContext.TaskDependencies => TaskDependencies;
    IQueryable<TaskTimeEntry> IApplicationDbContext.TaskTimeEntries => TaskTimeEntries;

    IQueryable<WeeklyPlan> IApplicationDbContext.WeeklyPlans => WeeklyPlans;
    IQueryable<DailyPlan> IApplicationDbContext.DailyPlans => DailyPlans;
    IQueryable<TimeBlock> IApplicationDbContext.TimeBlocks => TimeBlocks;

    IQueryable<FinancialAccount> IApplicationDbContext.FinancialAccounts => FinancialAccounts;
    IQueryable<FinancialTransaction> IApplicationDbContext.FinancialTransactions => FinancialTransactions;
    IQueryable<RecurringTransaction> IApplicationDbContext.RecurringTransactions => RecurringTransactions;
    IQueryable<FinancialCommitment> IApplicationDbContext.FinancialCommitments => FinancialCommitments;
    IQueryable<Budget> IApplicationDbContext.Budgets => Budgets;

    IQueryable<Note> IApplicationDbContext.Notes => Notes;
    IQueryable<JournalEntry> IApplicationDbContext.JournalEntries => JournalEntries;
    IQueryable<Tag> IApplicationDbContext.Tags => Tags;
    IQueryable<NoteTag> IApplicationDbContext.NoteTags => NoteTags;
    IQueryable<KnowledgeLink> IApplicationDbContext.KnowledgeLinks => KnowledgeLinks;

    IQueryable<Decision> IApplicationDbContext.Decisions => Decisions;
    IQueryable<DecisionOption> IApplicationDbContext.DecisionOptions => DecisionOptions;
    IQueryable<DecisionReview> IApplicationDbContext.DecisionReviews => DecisionReviews;

    IQueryable<CalendarEvent> IApplicationDbContext.CalendarEvents => CalendarEvents;
    IQueryable<Commitment> IApplicationDbContext.Commitments => Commitments;

    IQueryable<ProgressSnapshot> IApplicationDbContext.ProgressSnapshots => ProgressSnapshots;
    IQueryable<WeeklyReview> IApplicationDbContext.WeeklyReviews => WeeklyReviews;

    IQueryable<AiConversation> IApplicationDbContext.AiConversations => AiConversations;
    IQueryable<AiMessage> IApplicationDbContext.AiMessages => AiMessages;
    IQueryable<AiMemory> IApplicationDbContext.AiMemories => AiMemories;
    IQueryable<AiInsight> IApplicationDbContext.AiInsights => AiInsights;
    IQueryable<AiAction> IApplicationDbContext.AiActions => AiActions;
    IQueryable<AiExecutionLog> IApplicationDbContext.AiExecutionLogs => AiExecutionLogs;

    // DbContext already exposes Add/AddRange/Remove returning EntityEntry, so these are explicit
    // implementations to satisfy the void-returning Application contract.
    void IApplicationDbContext.Add<TEntity>(TEntity entity) => Add(entity);

    void IApplicationDbContext.AddRange<TEntity>(IEnumerable<TEntity> entities) => AddRange(entities);

    void IApplicationDbContext.Remove<TEntity>(TEntity entity) => Remove(entity);

    Task<List<TEntity>> IApplicationDbContext.ToListAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        => EntityFrameworkQueryableExtensions.ToListAsync(query, cancellationToken);

    Task<TEntity?> IApplicationDbContext.FirstOrDefaultAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        where TEntity : class
        => EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, cancellationToken);

    Task<int> IApplicationDbContext.CountAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        => EntityFrameworkQueryableExtensions.CountAsync(query, cancellationToken);

    Task<bool> IApplicationDbContext.AnyAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        => EntityFrameworkQueryableExtensions.AnyAsync(query, cancellationToken);

    Task<LifeDomain?> IApplicationDbContext.FindLifeDomainAsync(Guid id, CancellationToken cancellationToken)
        => LifeDomains.Include(x => x.Metrics).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    Task<Project?> IApplicationDbContext.FindProjectAsync(Guid id, CancellationToken cancellationToken)
        => Projects
            .Include(x => x.Milestones)
            .Include(x => x.GoalLinks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    Task<Goal?> IApplicationDbContext.FindGoalAsync(Guid id, CancellationToken cancellationToken)
        => Goals.Include(x => x.KeyResults).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    Task<TaskItem?> IApplicationDbContext.FindTaskAsync(Guid id, CancellationToken cancellationToken)
        => Tasks.Include(x => x.TimeEntries).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    Task<WeeklyPlan?> IApplicationDbContext.FindWeeklyPlanAsync(Guid id, CancellationToken cancellationToken)
        => WeeklyPlans.Include(x => x.DailyPlans).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    Task<DailyPlan?> IApplicationDbContext.FindDailyPlanAsync(Guid id, CancellationToken cancellationToken)
        => DailyPlans.Include(x => x.TimeBlocks).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    Task<Decision?> IApplicationDbContext.FindDecisionAsync(Guid id, CancellationToken cancellationToken)
        => Decisions.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    Task<Note?> IApplicationDbContext.FindNoteAsync(Guid id, CancellationToken cancellationToken)
        => Notes.Include(x => x.Tags).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LifeOsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Stamps audit timestamps centrally so no use case can forget them. Timestamps always come
    /// from <see cref="IClock"/>, never from <c>DateTimeOffset.UtcNow</c> inside entities.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    private void StampAuditFields()
    {
        var now = _clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.StampCreated(now);
                    break;
                case EntityState.Modified:
                    entry.Entity.StampUpdated(now);
                    break;
            }
        }
    }

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        return new EfTransactionScope(transaction);
    }

    private sealed class EfTransactionScope : ITransactionScope
    {
        private readonly IDbContextTransaction _transaction;
        private bool _completed;

        public EfTransactionScope(IDbContextTransaction transaction) => _transaction = transaction;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.CommitAsync(cancellationToken);
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.RollbackAsync(cancellationToken);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                await _transaction.RollbackAsync();
            }

            await _transaction.DisposeAsync();
        }
    }
}
