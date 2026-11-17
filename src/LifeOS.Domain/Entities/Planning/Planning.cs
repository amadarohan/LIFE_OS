using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Planning;

public sealed class WeeklyPlan : AuditableEntity, IConcurrencyTracked
{
    private readonly List<DailyPlan> _dailyPlans = [];

    private WeeklyPlan() { }

    public WeeklyPlan(DateOnly weekStartDate, string? theme)
    {
        if (weekStartDate.DayOfWeek != DayOfWeek.Saturday)
        {
            throw new DomainRuleException(
                "WeeklyPlan.StartMustBeSaturday",
                "هفته برنامه‌ریزی باید از شنبه شروع شود.");
        }

        WeekStartDate = weekStartDate;
        WeekEndDate = weekStartDate.AddDays(6);
        Theme = theme;
        Status = PlanStatus.Draft;
    }

    public DateOnly WeekStartDate { get; private set; }
    public DateOnly WeekEndDate { get; private set; }
    public string? Theme { get; private set; }
    public decimal AvailableHours { get; private set; }
    public decimal PlannedHours { get; private set; }
    public PlanStatus Status { get; private set; }
    public string? Reflection { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<DailyPlan> DailyPlans => _dailyPlans;

    /// <summary>Positive when the plan exceeds realistic capacity.</summary>
    public decimal OverloadHours => Math.Max(0m, PlannedHours - AvailableHours);

    public bool IsOverloaded => OverloadHours > 0m;

    public void Update(string? theme, decimal availableHours)
    {
        if (availableHours < 0m)
        {
            throw new DomainRuleException("WeeklyPlan.NegativeCapacity", "ظرفیت هفتگی نمی‌تواند منفی باشد.");
        }

        Theme = theme;
        AvailableHours = availableHours;
    }

    public void SetPlannedHours(decimal plannedHours)
    {
        if (plannedHours < 0m)
        {
            throw new DomainRuleException("WeeklyPlan.NegativePlanned", "ساعت برنامه‌ریزی‌شده نمی‌تواند منفی باشد.");
        }

        PlannedHours = plannedHours;
    }

    public void ChangeStatus(PlanStatus status) => Status = status;

    public void SetReflection(string? reflection) => Reflection = reflection;

    public DailyPlan AddDailyPlan(DateOnly planDate, string? mainFocus, int availableMinutes)
    {
        if (planDate < WeekStartDate || planDate > WeekEndDate)
        {
            throw new DomainRuleException("DailyPlan.OutsideWeek", "تاریخ روز برنامه باید داخل بازه هفته باشد.");
        }

        if (_dailyPlans.Any(d => d.PlanDate == planDate))
        {
            throw new DomainRuleException("DailyPlan.Duplicate", "برای این روز قبلاً برنامه ثبت شده است.");
        }

        var daily = new DailyPlan(Id, planDate, mainFocus, availableMinutes);
        _dailyPlans.Add(daily);
        return daily;
    }
}

public sealed class DailyPlan : AuditableEntity
{
    private readonly List<TimeBlock> _timeBlocks = [];

    private DailyPlan() { }

    internal DailyPlan(Guid weeklyPlanId, DateOnly planDate, string? mainFocus, int availableMinutes)
    {
        WeeklyPlanId = weeklyPlanId;
        PlanDate = planDate;
        MainFocus = mainFocus;
        AvailableMinutes = availableMinutes;
    }

    public Guid WeeklyPlanId { get; private set; }
    public DateOnly PlanDate { get; private set; }
    public string? MainFocus { get; private set; }
    public int AvailableMinutes { get; private set; }
    public int PlannedMinutes { get; private set; }
    public string? Reflection { get; private set; }

    public WeeklyPlan? WeeklyPlan { get; private set; }
    public IReadOnlyCollection<TimeBlock> TimeBlocks => _timeBlocks;

    public void Update(string? mainFocus, int availableMinutes)
    {
        if (availableMinutes < 0)
        {
            throw new DomainRuleException("DailyPlan.NegativeCapacity", "ظرفیت روزانه نمی‌تواند منفی باشد.");
        }

        MainFocus = mainFocus;
        AvailableMinutes = availableMinutes;
    }

    public void SetReflection(string? reflection) => Reflection = reflection;

    public void RecalculatePlannedMinutes()
    {
        PlannedMinutes = _timeBlocks
            .Where(b => b.Status != TimeBlockStatus.Skipped)
            .Sum(b => b.DurationMinutes);
    }

    public TimeBlock AddTimeBlock(
        string title,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        TimeBlockType blockType,
        Guid? taskId,
        Guid? calendarEventId,
        string? notes)
    {
        var block = new TimeBlock(Id, taskId, calendarEventId, title, startAtUtc, endAtUtc, blockType, notes);

        var overlaps = _timeBlocks.Any(b =>
            b.Status != TimeBlockStatus.Skipped &&
            startAtUtc < b.EndAtUtc &&
            endAtUtc > b.StartAtUtc);

        if (overlaps)
        {
            throw new DomainRuleException("TimeBlock.Overlap", "این بازه زمانی با یک بلوک دیگر هم‌پوشانی دارد.");
        }

        _timeBlocks.Add(block);
        RecalculatePlannedMinutes();
        return block;
    }

    public void RemoveTimeBlock(TimeBlock block)
    {
        _timeBlocks.Remove(block);
        RecalculatePlannedMinutes();
    }
}

public sealed class TimeBlock : Entity
{
    private TimeBlock() { }

    internal TimeBlock(
        Guid? dailyPlanId,
        Guid? taskId,
        Guid? calendarEventId,
        string title,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        TimeBlockType blockType,
        string? notes)
    {
        if (endAtUtc <= startAtUtc)
        {
            throw new DomainRuleException("TimeBlock.InvalidRange", "پایان بلوک زمانی باید بعد از شروع آن باشد.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("TimeBlock.TitleRequired", "عنوان بلوک زمانی الزامی است.");
        }

        DailyPlanId = dailyPlanId;
        TaskId = taskId;
        CalendarEventId = calendarEventId;
        Title = title.Trim();
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        BlockType = blockType;
        Status = TimeBlockStatus.Planned;
        Notes = notes;
        CreatedAtUtc = startAtUtc;
    }

    public Guid? DailyPlanId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? CalendarEventId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public DateTimeOffset StartAtUtc { get; private set; }
    public DateTimeOffset EndAtUtc { get; private set; }
    public TimeBlockType BlockType { get; private set; }
    public TimeBlockStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DailyPlan? DailyPlan { get; private set; }

    public int DurationMinutes => (int)Math.Round((EndAtUtc - StartAtUtc).TotalMinutes);

    public void Reschedule(DateTimeOffset startAtUtc, DateTimeOffset endAtUtc)
    {
        if (endAtUtc <= startAtUtc)
        {
            throw new DomainRuleException("TimeBlock.InvalidRange", "پایان بلوک زمانی باید بعد از شروع آن باشد.");
        }

        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
    }

    public void ChangeStatus(TimeBlockStatus status) => Status = status;

    public void Update(string title, TimeBlockType blockType, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("TimeBlock.TitleRequired", "عنوان بلوک زمانی الزامی است.");
        }

        Title = title.Trim();
        BlockType = blockType;
        Notes = notes;
    }
}
