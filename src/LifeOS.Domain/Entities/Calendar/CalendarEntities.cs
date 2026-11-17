using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Calendar;

public sealed class CalendarEvent : AuditableEntity
{
    private CalendarEvent() { }

    public CalendarEvent(
        string title,
        string? description,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        CalendarEventType eventType,
        string? location,
        bool isAllDay,
        Guid? relatedProjectId,
        Guid? relatedGoalId)
    {
        if (endAtUtc <= startAtUtc)
        {
            throw new DomainRuleException("CalendarEvent.InvalidRange", "پایان رویداد باید بعد از شروع آن باشد.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("CalendarEvent.TitleRequired", "عنوان رویداد الزامی است.");
        }

        Title = title.Trim();
        Description = description;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        EventType = eventType;
        Location = location;
        IsAllDay = isAllDay;
        RelatedProjectId = relatedProjectId;
        RelatedGoalId = relatedGoalId;
    }

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset StartAtUtc { get; private set; }
    public DateTimeOffset EndAtUtc { get; private set; }
    public CalendarEventType EventType { get; private set; }
    public string? Location { get; private set; }
    public bool IsAllDay { get; private set; }
    public Guid? RelatedProjectId { get; private set; }
    public Guid? RelatedGoalId { get; private set; }

    public void Update(string title, string? description, DateTimeOffset startAtUtc, DateTimeOffset endAtUtc,
        CalendarEventType eventType, string? location, bool isAllDay)
    {
        if (endAtUtc <= startAtUtc)
        {
            throw new DomainRuleException("CalendarEvent.InvalidRange", "پایان رویداد باید بعد از شروع آن باشد.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("CalendarEvent.TitleRequired", "عنوان رویداد الزامی است.");
        }

        Title = title.Trim();
        Description = description;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        EventType = eventType;
        Location = location;
        IsAllDay = isAllDay;
    }
}

public sealed class Commitment : AuditableEntity
{
    private Commitment() { }

    public Commitment(
        string title,
        string? description,
        CommitmentType commitmentType,
        DateOnly? dueDate,
        ImportanceLevel importance,
        Guid? relatedProjectId,
        Guid? relatedTaskId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("Commitment.TitleRequired", "عنوان تعهد الزامی است.");
        }

        Title = title.Trim();
        Description = description;
        CommitmentType = commitmentType;
        DueDate = dueDate;
        Importance = importance;
        RelatedProjectId = relatedProjectId;
        RelatedTaskId = relatedTaskId;
        Status = CommitmentStatus.Active;
    }

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public CommitmentType CommitmentType { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public CommitmentStatus Status { get; private set; }
    public ImportanceLevel Importance { get; private set; }
    public Guid? RelatedProjectId { get; private set; }
    public Guid? RelatedTaskId { get; private set; }

    public void Update(string title, string? description, CommitmentType commitmentType, DateOnly? dueDate,
        ImportanceLevel importance)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("Commitment.TitleRequired", "عنوان تعهد الزامی است.");
        }

        Title = title.Trim();
        Description = description;
        CommitmentType = commitmentType;
        DueDate = dueDate;
        Importance = importance;
    }

    public void ChangeStatus(CommitmentStatus status) => Status = status;

    public bool IsOverdue(DateOnly today)
        => Status == CommitmentStatus.Active && DueDate.HasValue && DueDate.Value < today;
}
