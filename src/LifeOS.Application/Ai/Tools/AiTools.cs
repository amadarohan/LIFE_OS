using System.Text.Json;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Application.Engines;
using LifeOS.Application.UseCases.Planning;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.Ai.Tools;

/// <summary>
/// Read-only tools the model may call. They return compact JSON summaries built from Application
/// queries — never entity graphs — so the model sees facts, not database internals.
/// </summary>
public sealed class GetOpenTasksTool : IAiTool
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IUserTimeZone _timeZone;

    public GetOpenTasksTool(IApplicationDbContext db, IClock clock, IUserTimeZone timeZone)
    {
        _db = db;
        _clock = clock;
        _timeZone = timeZone;
    }

    public string Name => "get_open_tasks";

    public AiToolCategory Category => AiToolCategory.Read;

    public string Description => "فهرست کارهای باز، عقب‌افتاده و اقدام‌های بعدی را برمی‌گرداند.";

    public JsonSchema InputSchema => new(
        "object",
        new Dictionary<string, JsonSchemaProperty>
        {
            ["limit"] = new("integer", "حداکثر تعداد کارهای بازگشتی (پیش‌فرض ۲۰).")
        });

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement input,
        AiToolContext context,
        CancellationToken cancellationToken)
    {
        var limit = ReadInt(input, "limit", 20, 1, 100);
        var today = _timeZone.TodayLocal;

        var tasks = await _db.ToListAsync(
            _db.Tasks
                .Where(t => t.Status != LifeTaskStatus.Completed
                            && t.Status != LifeTaskStatus.Cancelled
                            && t.Status != LifeTaskStatus.Deferred)
                .OrderBy(t => t.DueDate == null)
                .ThenBy(t => t.DueDate)
                .Take(limit),
            cancellationToken);

        var payload = tasks.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            status = t.Status.ToString(),
            priority = t.Priority.ToString(),
            dueDate = t.DueDate?.ToString("yyyy-MM-dd"),
            isOverdue = t.DueDate is { } d && d < today,
            isNextAction = t.IsNextAction,
            estimatedMinutes = t.EstimatedMinutes
        });

        return AiToolResult.Success(JsonSerializer.Serialize(new
        {
            today = today.ToString("yyyy-MM-dd"),
            count = tasks.Count,
            tasks = payload
        }));
    }

    internal static int ReadInt(JsonElement input, string property, int fallback, int min, int max)
    {
        if (input.ValueKind == JsonValueKind.Object
            && input.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed))
        {
            return Math.Clamp(parsed, min, max);
        }

        return fallback;
    }
}

public sealed class GetProjectsTool : IAiTool
{
    private readonly IApplicationDbContext _db;

    public GetProjectsTool(IApplicationDbContext db) => _db = db;

    public string Name => "get_projects";

    public AiToolCategory Category => AiToolCategory.Read;

    public string Description => "پروژه‌های فعال با پیشرفت، وضعیت و تاریخ هدف را برمی‌گرداند.";

    public JsonSchema InputSchema => new(
        "object",
        new Dictionary<string, JsonSchemaProperty>
        {
            ["limit"] = new("integer", "حداکثر تعداد پروژه‌های بازگشتی (پیش‌فرض ۲۰).")
        });

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement input,
        AiToolContext context,
        CancellationToken cancellationToken)
    {
        var limit = GetOpenTasksTool.ReadInt(input, "limit", 20, 1, 100);

        var projects = await _db.ToListAsync(
            _db.Projects
                .Where(p => !p.IsArchived
                            && p.Status != ProjectStatus.Completed
                            && p.Status != ProjectStatus.Cancelled)
                .OrderByDescending(p => p.Priority)
                .Take(limit),
            cancellationToken);

        var payload = projects.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            status = p.Status.ToString(),
            priority = p.Priority.ToString(),
            progressPercentage = p.ProgressPercentage,
            targetEndDate = p.TargetEndDate?.ToString("yyyy-MM-dd"),
            hasNextAction = p.Milestones.Any(m => m.Status == MilestoneStatus.Pending)
        });

        return AiToolResult.Success(JsonSerializer.Serialize(new
        {
            count = projects.Count,
            projects = payload
        }));
    }
}

public sealed class GetGoalsTool : IAiTool
{
    private readonly IApplicationDbContext _db;

    public GetGoalsTool(IApplicationDbContext db) => _db = db;

    public string Name => "get_goals";

    public AiToolCategory Category => AiToolCategory.Read;

    public string Description => "اهداف فعال با پیشرفت و تاریخ هدف را برمی‌گرداند.";

    public JsonSchema InputSchema => new(
        "object",
        new Dictionary<string, JsonSchemaProperty>
        {
            ["limit"] = new("integer", "حداکثر تعداد اهداف بازگشتی (پیش‌فرض ۱۵).")
        });

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement input,
        AiToolContext context,
        CancellationToken cancellationToken)
    {
        var limit = GetOpenTasksTool.ReadInt(input, "limit", 15, 1, 100);

        var goals = await _db.ToListAsync(
            _db.Goals
                .Where(g => g.Status == GoalStatus.Active)
                .OrderByDescending(g => g.Priority)
                .Take(limit),
            cancellationToken);

        var payload = goals.Select(g => new
        {
            id = g.Id,
            name = g.Name,
            goalType = g.GoalType.ToString(),
            priority = g.Priority.ToString(),
            progressPercentage = g.ProgressPercentage,
            targetDate = g.TargetDate?.ToString("yyyy-MM-dd"),
            keyResultCount = g.KeyResults.Count
        });

        return AiToolResult.Success(JsonSerializer.Serialize(new
        {
            count = goals.Count,
            goals = payload
        }));
    }
}

public sealed class GetCapacityTool : IAiTool
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IUserTimeZone _timeZone;
    private readonly CapacityEngine _capacity;

    public GetCapacityTool(
        IApplicationDbContext db,
        IClock clock,
        IUserTimeZone timeZone,
        CapacityEngine capacity)
    {
        _db = db;
        _clock = clock;
        _timeZone = timeZone;
        _capacity = capacity;
    }

    public string Name => "get_capacity";

    public AiToolCategory Category => AiToolCategory.Read;

    public string Description => "ظرفیت هفته جاری و میزان اشغال آن را برمی‌گرداند.";

    public JsonSchema InputSchema => new(
        "object",
        new Dictionary<string, JsonSchemaProperty>());

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement input,
        AiToolContext context,
        CancellationToken cancellationToken)
    {
        var today = _timeZone.TodayLocal;
        var weekStart = PlanningService.StartOfWeek(today);

        var plan = await _db.FirstOrDefaultAsync(
            _db.WeeklyPlans.Where(w => w.WeekStartDate == weekStart),
            cancellationToken);

        if (plan is null)
        {
            return AiToolResult.Success(JsonSerializer.Serialize(new
            {
                hasPlan = false,
                weekStart = weekStart.ToString("yyyy-MM-dd"),
                message = "برای هفته جاری برنامه‌ای ثبت نشده است."
            }));
        }

        var assessment = _capacity.AssessWeek(
            (int)(plan.AvailableHours * 60),
            (int)(plan.PlannedHours * 60),
            0);

        return AiToolResult.Success(JsonSerializer.Serialize(new
        {
            hasPlan = true,
            weekStart = plan.WeekStartDate.ToString("yyyy-MM-dd"),
            weekEnd = plan.WeekEndDate.ToString("yyyy-MM-dd"),
            availableHours = plan.AvailableHours,
            plannedHours = plan.PlannedHours,
            overloadHours = plan.OverloadHours,
            isOverloaded = plan.IsOverloaded,
            utilizationPercent = assessment.UtilizationPercent,
            status = assessment.Status.ToString()
        }));
    }
}

public sealed class GetLifeDebtTool : IAiTool
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IUserTimeZone _timeZone;
    private readonly LifeDebtEngine _engine;

    public GetLifeDebtTool(
        IApplicationDbContext db,
        IClock clock,
        IUserTimeZone timeZone,
        LifeDebtEngine engine)
    {
        _db = db;
        _clock = clock;
        _timeZone = timeZone;
        _engine = engine;
    }

    public string Name => "get_life_debt";

    public AiToolCategory Category => AiToolCategory.Analysis;

    public string Description => "بدهی‌های زندگی: کارهای عقب‌افتاده، تعهدات فراموش‌شده و پروژه‌های راکد.";

    public JsonSchema InputSchema => new(
        "object",
        new Dictionary<string, JsonSchemaProperty>());

    public async Task<AiToolResult> ExecuteAsync(
        JsonElement input,
        AiToolContext context,
        CancellationToken cancellationToken)
    {
        var today = _timeZone.TodayLocal;

        var overdueTasks = await _db.ToListAsync(
            _db.Tasks
                .Where(t => t.DueDate != null
                            && t.DueDate < today
                            && t.Status != LifeTaskStatus.Completed
                            && t.Status != LifeTaskStatus.Cancelled)
                .OrderBy(t => t.DueDate)
                .Take(50),
            cancellationToken);

        var overdueCommitments = await _db.ToListAsync(
            _db.Commitments
                .Where(c => c.DueDate != null
                            && c.DueDate < today
                            && c.Status == CommitmentStatus.Active)
                .OrderBy(c => c.DueDate)
                .Take(50),
            cancellationToken);

        var report = _engine.Evaluate(
            new LifeDebtInput(
                overdueTasks
                    .Where(t => t.DueDate is not null)
                    .Select(t => new OverdueTaskInfo(t.Id, t.Title, t.DueDate!.Value, t.Priority))
                    .ToList(),
                overdueCommitments
                    .Where(c => c.DueDate is not null)
                    .Select(c => new OverdueCommitmentInfo(c.Id, c.Title, c.DueDate!.Value, c.Importance))
                    .ToList(),
                [],
                [],
                [],
                []),
            today);

        var payload = report.MostUrgent(10).Select(item => new
        {
            kind = item.Kind.ToString(),
            title = item.Title,
            ageDays = item.AgeDays,
            severity = item.Severity.ToString(),
            explanation = item.Explanation
        });

        return AiToolResult.Success(JsonSerializer.Serialize(new
        {
            totalCount = report.TotalCount,
            items = payload
        }));
    }
}

/// <summary>
/// Proposal tool: it never writes. It returns a structured proposal that becomes an
/// <c>AiAction</c> awaiting human approval.
/// </summary>
public sealed class ProposeCreateTaskTool : IAiTool
{
    private readonly IClock _clock;

    public ProposeCreateTaskTool(IClock clock) => _clock = clock;

    public string Name => "propose_create_task";

    public AiToolCategory Category => AiToolCategory.Proposal;

    public string Description => "ایجاد یک کار جدید را پیشنهاد می‌دهد. اجرا فقط پس از تأیید کاربر.";

    public JsonSchema InputSchema => new(
        "object",
        new Dictionary<string, JsonSchemaProperty>
        {
            ["title"] = new("string", "عنوان کار.", IsRequired: true),
            ["description"] = new("string", "توضیح کار."),
            ["priority"] = new(
                "string",
                "اولویت کار.",
                AllowedValues: ["None", "Low", "Medium", "High", "Critical"]),
            ["dueDate"] = new("string", "تاریخ سررسید به شکل yyyy-MM-dd."),
            ["estimatedMinutes"] = new("integer", "زمان تخمینی به دقیقه.")
        });

    public Task<AiToolResult> ExecuteAsync(
        JsonElement input,
        AiToolContext context,
        CancellationToken cancellationToken)
    {
        if (input.ValueKind != JsonValueKind.Object
            || !input.TryGetProperty("title", out var titleElement)
            || titleElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(titleElement.GetString()))
        {
            return Task.FromResult(AiToolResult.Failure(
                "Tool.InvalidInput",
                "عنوان کار الزامی است."));
        }

        var title = titleElement.GetString()!.Trim();
        var description = ReadString(input, "description");
        var priority = ReadEnum(input, "priority", PriorityLevel.Medium);
        var dueDate = ReadDate(input, "dueDate");
        var estimatedMinutes = input.TryGetProperty("estimatedMinutes", out var minutes)
                               && minutes.TryGetInt32(out var parsedMinutes)
            ? parsedMinutes
            : (int?)null;

        var payload = JsonSerializer.Serialize(new
        {
            Title = title,
            Description = description,
            ProjectId = (Guid?)null,
            GoalId = (Guid?)null,
            Priority = priority,
            DueDate = dueDate,
            ScheduledDate = (DateOnly?)null,
            EstimatedMinutes = estimatedMinutes
        });

        var proposal = new AiActionProposal(
            AiActionType.CreateTask,
            $"ایجاد کار «{title}»",
            payload,
            ComputeContextHash(payload));

        return Task.FromResult(AiToolResult.Proposed(proposal));
    }

    internal static string? ReadString(JsonElement input, string property)
        => input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static TEnum ReadEnum<TEnum>(JsonElement input, string property, TEnum fallback)
        where TEnum : struct, Enum
    {
        var raw = ReadString(input, property);
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    internal static DateOnly? ReadDate(JsonElement input, string property)
    {
        var raw = ReadString(input, property);
        return DateOnly.TryParse(raw, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Hash of the proposal payload. The executor compares it against live data to detect that the
    /// world changed between proposal and approval.
    /// </summary>
    internal static string ComputeContextHash(string payloadJson)
        => AiContextHash.Compute(payloadJson);
}
