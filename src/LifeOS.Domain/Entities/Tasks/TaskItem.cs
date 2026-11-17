using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Tasks;

public sealed class TaskItem : AuditableEntity, IConcurrencyTracked
{
    private readonly List<TaskTimeEntry> _timeEntries = [];

    private TaskItem() { }

    public TaskItem(string title, Guid? projectId, Guid? goalId, Guid? parentTaskId)
    {
        Rename(title);
        ProjectId = projectId;
        GoalId = goalId;
        ParentTaskId = parentTaskId;
        Status = LifeTaskStatus.Inbox;
        Priority = PriorityLevel.None;
        Source = TaskSource.Manual;
    }

    public Guid? ProjectId { get; private set; }
    public Guid? GoalId { get; private set; }
    public Guid? ParentTaskId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public LifeTaskStatus Status { get; private set; }
    public PriorityLevel Priority { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public DateOnly? ScheduledDate { get; private set; }
    public int? EstimatedMinutes { get; private set; }
    public int? ActualMinutes { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public bool IsRecurring { get; private set; }
    public string? RecurrenceRule { get; private set; }
    public TaskSource Source { get; private set; }
    public bool IsNextAction { get; private set; }
    public string? DeferReason { get; private set; }
    public DateOnly? ReviewDate { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<TaskTimeEntry> TimeEntries => _timeEntries;

    public bool IsOpen => Status is LifeTaskStatus.Inbox or LifeTaskStatus.Todo or LifeTaskStatus.InProgress
        or LifeTaskStatus.Waiting or LifeTaskStatus.Deferred;

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate.HasValue && DueDate.Value < today;

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("Task.TitleRequired", "عنوان کار الزامی است.");
        }

        Title = title.Trim();
    }

    public void UpdateDetails(
        string title,
        string? description,
        Guid? projectId,
        Guid? goalId,
        PriorityLevel priority,
        DateOnly? dueDate,
        DateOnly? scheduledDate,
        int? estimatedMinutes)
    {
        Rename(title);

        if (estimatedMinutes is < 0)
        {
            throw new DomainRuleException("Task.NegativeEstimate", "زمان تخمینی نمی‌تواند منفی باشد.");
        }

        Description = description;
        ProjectId = projectId;
        GoalId = goalId;
        Priority = priority;
        DueDate = dueDate;
        ScheduledDate = scheduledDate;
        EstimatedMinutes = estimatedMinutes;
    }

    /// <summary>
    /// Single authoritative status transition. Completion always stamps the timestamp and
    /// clears the next-action flag; reopening clears the completion timestamp.
    /// </summary>
    public void ChangeStatus(LifeTaskStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return;
        }

        if (Status == LifeTaskStatus.Completed && status != LifeTaskStatus.Completed)
        {
            CompletedAtUtc = null;
        }

        if (status == LifeTaskStatus.Completed)
        {
            CompletedAtUtc = now;
            IsNextAction = false;
        }

        if (status == LifeTaskStatus.Deferred && string.IsNullOrWhiteSpace(DeferReason))
        {
            throw new DomainRuleException(
                "Task.DeferReasonRequired",
                "برای به تعویق انداختن کار، دلیل یا تاریخ بازبینی لازم است.");
        }

        if (status != LifeTaskStatus.Deferred)
        {
            DeferReason = null;
            ReviewDate = null;
        }

        Status = status;
    }

    public void Defer(string reason, DateOnly? reviewDate)
    {
        if (string.IsNullOrWhiteSpace(reason) && reviewDate is null)
        {
            throw new DomainRuleException(
                "Task.DeferReasonRequired",
                "برای به تعویق انداختن کار، دلیل یا تاریخ بازبینی لازم است.");
        }

        DeferReason = reason;
        ReviewDate = reviewDate;
        Status = LifeTaskStatus.Deferred;
    }

    public void SetNextAction(bool isNextAction)
    {
        if (isNextAction && Status is LifeTaskStatus.Completed or LifeTaskStatus.Cancelled)
        {
            throw new DomainRuleException(
                "Task.NextActionOnClosedTask",
                "کار بسته‌شده نمی‌تواند اقدام بعدی باشد.");
        }

        IsNextAction = isNextAction;
    }

    public void SetPriority(PriorityLevel priority) => Priority = priority;

    public void Schedule(DateOnly? scheduledDate) => ScheduledDate = scheduledDate;

    public void SetRecurrence(bool isRecurring, string? recurrenceRule)
    {
        IsRecurring = isRecurring;
        RecurrenceRule = isRecurring ? recurrenceRule : null;
    }

    public void MarkSource(TaskSource source) => Source = source;

    public TaskTimeEntry AddTimeEntry(DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, string? description)
    {
        if (endedAtUtc <= startedAtUtc)
        {
            throw new DomainRuleException("TaskTimeEntry.InvalidRange", "پایان بازه زمانی باید بعد از شروع آن باشد.");
        }

        var entry = new TaskTimeEntry(Id, startedAtUtc, endedAtUtc, description);
        _timeEntries.Add(entry);
        ActualMinutes = _timeEntries.Sum(e => e.DurationMinutes);
        return entry;
    }

    public void SetActualMinutes(int? minutes)
    {
        if (minutes is < 0)
        {
            throw new DomainRuleException("Task.NegativeActual", "زمان واقعی نمی‌تواند منفی باشد.");
        }

        ActualMinutes = minutes;
    }
}

public sealed class TaskDependency : Entity
{
    private TaskDependency() { }

    public TaskDependency(Guid taskId, Guid dependsOnTaskId, DateTimeOffset createdAtUtc)
    {
        if (taskId == dependsOnTaskId)
        {
            throw new DomainRuleException("TaskDependency.SelfReference", "یک کار نمی‌تواند به خودش وابسته باشد.");
        }

        TaskId = taskId;
        DependsOnTaskId = dependsOnTaskId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid TaskId { get; private set; }
    public Guid DependsOnTaskId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public TaskItem? Task { get; private set; }
    public TaskItem? DependsOnTask { get; private set; }
}

public sealed class TaskTimeEntry : Entity
{
    private TaskTimeEntry() { }

    internal TaskTimeEntry(Guid taskId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, string? description)
    {
        TaskId = taskId;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        DurationMinutes = (int)Math.Round((endedAtUtc - startedAtUtc).TotalMinutes);
        Description = description;
        CreatedAtUtc = startedAtUtc;
    }

    public Guid TaskId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset EndedAtUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public TaskItem? Task { get; private set; }
}
