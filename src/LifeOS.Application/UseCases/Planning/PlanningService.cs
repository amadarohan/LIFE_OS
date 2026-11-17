using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Application.Engines;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Planning;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Planning;

public sealed record TimeBlockView(
    Guid Id,
    string Title,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    int DurationMinutes,
    TimeBlockType BlockType,
    TimeBlockStatus Status,
    Guid? TaskId,
    Guid? CalendarEventId,
    string? Notes);

public sealed record DailyPlanView(
    Guid Id,
    DateOnly PlanDate,
    string? MainFocus,
    int AvailableMinutes,
    int PlannedMinutes,
    string? Reflection,
    CapacityAssessment Capacity,
    IReadOnlyList<TimeBlockView> TimeBlocks);

public sealed record WeeklyPlanView(
    Guid Id,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    string? Theme,
    decimal AvailableHours,
    decimal PlannedHours,
    decimal OverloadHours,
    bool IsOverloaded,
    PlanStatus Status,
    string? Reflection,
    IReadOnlyList<DailyPlanView> DailyPlans);

public sealed record CreateWeeklyPlanRequest(DateOnly WeekStartDate, string? Theme, decimal AvailableHours);

public sealed record AddTimeBlockRequest(
    Guid DailyPlanId,
    string Title,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    TimeBlockType BlockType,
    Guid? TaskId,
    Guid? CalendarEventId,
    string? Notes);

/// <summary>
/// Weekly and daily planning. The week always starts on Saturday (Persian week), and every
/// capacity number shown to the user comes from <see cref="CapacityEngine"/> rather than being
/// computed ad hoc in the UI.
/// </summary>
public sealed class PlanningService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly CapacityEngine _capacity;

    public PlanningService(
        IApplicationDbContext db,
        IAuditWriter audit,
        IClock clock,
        CapacityEngine capacity)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _capacity = capacity;
    }

    /// <summary>Saturday of the week containing <paramref name="date"/>.</summary>
    public static DateOnly StartOfWeek(DateOnly date)
    {
        var delta = ((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        return date.AddDays(-delta);
    }

    public async Task<Result<WeeklyPlanView>> GetCurrentWeekAsync(CancellationToken cancellationToken = default)
    {
        var weekStart = StartOfWeek(_clock.TodayUtc);

        var plan = await _db.FirstOrDefaultAsync(
            _db.WeeklyPlans.Where(p => p.WeekStartDate == weekStart),
            cancellationToken);

        if (plan is null)
        {
            return Guard.NotFound<WeeklyPlanView>("برنامه هفتگی", Guid.Empty);
        }

        return await GetAsync(plan.Id, cancellationToken);
    }

    public async Task<Result<WeeklyPlanView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _db.FindWeeklyPlanAsync(id, cancellationToken);
        if (plan is null)
        {
            return Guard.NotFound<WeeklyPlanView>("برنامه هفتگی", id);
        }

        var dailyIds = plan.DailyPlans.Select(d => d.Id).ToList();

        var blocks = dailyIds.Count == 0
            ? []
            : await _db.ToListAsync(
                _db.TimeBlocks.Where(b => b.DailyPlanId != null && dailyIds.Contains(b.DailyPlanId.Value)),
                cancellationToken);

        var blocksByDay = blocks
            .Where(b => b.DailyPlanId.HasValue)
            .GroupBy(b => b.DailyPlanId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.StartAtUtc).ToList());

        var days = plan.DailyPlans
            .OrderBy(d => d.PlanDate)
            .Select(d =>
            {
                var dayBlocks = blocksByDay.GetValueOrDefault(d.Id) ?? [];
                var planned = dayBlocks
                    .Where(b => b.Status != TimeBlockStatus.Skipped)
                    .Sum(b => b.DurationMinutes);

                return new DailyPlanView(
                    d.Id,
                    d.PlanDate,
                    d.MainFocus,
                    d.AvailableMinutes,
                    planned,
                    d.Reflection,
                    _capacity.Assess(d.AvailableMinutes, planned),
                    dayBlocks.Select(b => new TimeBlockView(
                        b.Id,
                        b.Title,
                        b.StartAtUtc,
                        b.EndAtUtc,
                        b.DurationMinutes,
                        b.BlockType,
                        b.Status,
                        b.TaskId,
                        b.CalendarEventId,
                        b.Notes)).ToList());
            })
            .ToList();

        return Result.Success(new WeeklyPlanView(
            plan.Id,
            plan.WeekStartDate,
            plan.WeekEndDate,
            plan.Theme,
            plan.AvailableHours,
            plan.PlannedHours,
            plan.OverloadHours,
            plan.IsOverloaded,
            plan.Status,
            plan.Reflection,
            days));
    }

    public Task<Result<Guid>> CreateAsync(
        CreateWeeklyPlanRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var exists = await _db.AnyAsync(
                _db.WeeklyPlans.Where(p => p.WeekStartDate == request.WeekStartDate),
                cancellationToken);

            if (exists)
            {
                throw new DomainRuleException(
                    "WeeklyPlan.Duplicate",
                    "برای این هفته قبلاً برنامه ثبت شده است.");
            }

            var plan = new WeeklyPlan(request.WeekStartDate, request.Theme);
            plan.Update(request.Theme, request.AvailableHours);

            _db.Add(plan);
            await _audit.WriteAsync(
                nameof(WeeklyPlan),
                plan.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(plan.Id);
        });

    public Task<Result> UpdateAsync(
        Guid id,
        string? theme,
        decimal availableHours,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var plan = await _db.FindWeeklyPlanAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(WeeklyPlan), id);

            plan.Update(theme, availableHours);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeStatusAsync(
        Guid id,
        PlanStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var plan = await _db.FindWeeklyPlanAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(WeeklyPlan), id);

            plan.ChangeStatus(status);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetReflectionAsync(
        Guid id,
        string? reflection,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var plan = await _db.FindWeeklyPlanAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(WeeklyPlan), id);

            plan.SetReflection(reflection);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> AddDailyPlanAsync(
        Guid weeklyPlanId,
        DateOnly planDate,
        string? mainFocus,
        int availableMinutes,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var plan = await _db.FindWeeklyPlanAsync(weeklyPlanId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(WeeklyPlan), weeklyPlanId);

            var daily = plan.AddDailyPlan(planDate, mainFocus, availableMinutes);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(daily.Id);
        });

    public Task<Result> UpdateDailyPlanAsync(
        Guid dailyPlanId,
        string? mainFocus,
        int availableMinutes,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var daily = await _db.FindDailyPlanAsync(dailyPlanId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(DailyPlan), dailyPlanId);

            daily.Update(mainFocus, availableMinutes);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> AddTimeBlockAsync(
        AddTimeBlockRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var daily = await _db.FindDailyPlanAsync(request.DailyPlanId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(DailyPlan), request.DailyPlanId);

            if (request.TaskId.HasValue)
            {
                var taskExists = await _db.AnyAsync(
                    _db.Tasks.Where(t => t.Id == request.TaskId.Value),
                    cancellationToken);

                if (!taskExists)
                {
                    throw new EntityNotFoundException("TaskItem", request.TaskId.Value);
                }
            }

            var block = daily.AddTimeBlock(
                request.Title,
                request.StartAtUtc,
                request.EndAtUtc,
                request.BlockType,
                request.TaskId,
                request.CalendarEventId,
                request.Notes);

            await SyncWeeklyPlannedHoursAsync(daily.WeeklyPlanId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(block.Id);
        });

    public Task<Result> RescheduleTimeBlockAsync(
        Guid dailyPlanId,
        Guid blockId,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var daily = await _db.FindDailyPlanAsync(dailyPlanId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(DailyPlan), dailyPlanId);

            var block = daily.TimeBlocks.FirstOrDefault(b => b.Id == blockId)
                ?? throw new EntityNotFoundException(nameof(TimeBlock), blockId);

            // Overlap must be re-checked against the other blocks after the move.
            var overlaps = daily.TimeBlocks.Any(b =>
                b.Id != blockId &&
                b.Status != TimeBlockStatus.Skipped &&
                startAtUtc < b.EndAtUtc &&
                endAtUtc > b.StartAtUtc);

            if (overlaps)
            {
                throw new DomainRuleException("TimeBlock.Overlap", "این بازه زمانی با یک بلوک دیگر هم‌پوشانی دارد.");
            }

            block.Reschedule(startAtUtc, endAtUtc);
            daily.RecalculatePlannedMinutes();

            await SyncWeeklyPlannedHoursAsync(daily.WeeklyPlanId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeTimeBlockStatusAsync(
        Guid dailyPlanId,
        Guid blockId,
        TimeBlockStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var daily = await _db.FindDailyPlanAsync(dailyPlanId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(DailyPlan), dailyPlanId);

            var block = daily.TimeBlocks.FirstOrDefault(b => b.Id == blockId)
                ?? throw new EntityNotFoundException(nameof(TimeBlock), blockId);

            block.ChangeStatus(status);
            daily.RecalculatePlannedMinutes();

            await SyncWeeklyPlannedHoursAsync(daily.WeeklyPlanId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RemoveTimeBlockAsync(
        Guid dailyPlanId,
        Guid blockId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var daily = await _db.FindDailyPlanAsync(dailyPlanId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(DailyPlan), dailyPlanId);

            var block = daily.TimeBlocks.FirstOrDefault(b => b.Id == blockId)
                ?? throw new EntityNotFoundException(nameof(TimeBlock), blockId);

            daily.RemoveTimeBlock(block);

            await SyncWeeklyPlannedHoursAsync(daily.WeeklyPlanId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>
    /// Keeps the weekly planned-hours total consistent with the sum of its daily blocks, so the
    /// overload indicator cannot drift away from the actual plan.
    /// </summary>
    private async Task SyncWeeklyPlannedHoursAsync(Guid weeklyPlanId, CancellationToken cancellationToken)
    {
        var plan = await _db.FindWeeklyPlanAsync(weeklyPlanId, cancellationToken);
        if (plan is null)
        {
            return;
        }

        var dailyIds = plan.DailyPlans.Select(d => d.Id).ToList();
        if (dailyIds.Count == 0)
        {
            plan.SetPlannedHours(0m);
            return;
        }

        var blocks = await _db.ToListAsync(
            _db.TimeBlocks.Where(b => b.DailyPlanId != null && dailyIds.Contains(b.DailyPlanId.Value)),
            cancellationToken);

        var totalMinutes = blocks
            .Where(b => b.Status != TimeBlockStatus.Skipped)
            .Sum(b => b.DurationMinutes);

        plan.SetPlannedHours(Math.Round(totalMinutes / 60m, 2));
    }
}
