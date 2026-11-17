using System.Text;
using LifeOS.Application.Abstractions;
using LifeOS.Application.Abstractions.Ai;
using LifeOS.Domain.Entities.Finance;
using LifeOS.Domain.Entities.Goals;
using LifeOS.Domain.Entities.Knowledge;
using LifeOS.Domain.Entities.Planning;
using LifeOS.Domain.Entities.Projects;
using LifeOS.Domain.Entities.Tasks;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.Ai;

/// <summary>
/// Assembles the context handed to a model. Three rules drive the design:
/// <list type="bullet">
/// <item>Minimal — only sections relevant to the request category are included.</item>
/// <item>Traceable — every item keeps its source entity references so the UI can show evidence.</item>
/// <item>Budgeted — a global character budget is enforced, and sections are dropped whole rather
/// than truncated mid-sentence.</item>
/// </list>
/// User-authored text is wrapped in &lt;user_data&gt; blocks and scanned by the injection guard.
/// </summary>
public sealed class AiContextBuilder : IAiContextBuilder
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IUserTimeZone _timeZone;
    private readonly IPromptInjectionGuard _injectionGuard;

    public AiContextBuilder(
        IApplicationDbContext db,
        IClock clock,
        IUserTimeZone timeZone,
        IPromptInjectionGuard injectionGuard)
    {
        _db = db;
        _clock = clock;
        _timeZone = timeZone;
        _injectionGuard = injectionGuard;
    }

    public async Task<AiContext> BuildAsync(AiContextRequest request, CancellationToken cancellationToken)
    {
        var items = new List<ContextItem>();
        var today = _timeZone.TodayLocal;

        // Every category gets the temporal anchor; without it the model cannot reason about "now".
        items.Add(BuildTemporalAnchor(today));

        switch (request.Category)
        {
            case AiTaskCategory.SimpleClassification:
            case AiTaskCategory.Extraction:
                await AddFocusSectionAsync(items, today, cancellationToken);
                break;

            case AiTaskCategory.Summarization:
                await AddFocusSectionAsync(items, today, cancellationToken);
                await AddProjectsSectionAsync(items, cancellationToken);
                break;

            case AiTaskCategory.SensitiveAnalysis:
                await AddFocusSectionAsync(items, today, cancellationToken);
                await AddProjectsSectionAsync(items, cancellationToken);
                await AddGoalsSectionAsync(items, cancellationToken);
                if (request.IncludeFinance)
                {
                    await AddFinanceSectionAsync(items, today, cancellationToken);
                }

                if (request.IncludeJournal)
                {
                    await AddJournalSectionAsync(items, today, cancellationToken);
                }

                break;

            case AiTaskCategory.StrategicAnalysis:
            case AiTaskCategory.ScenarioEvaluation:
                await AddFocusSectionAsync(items, today, cancellationToken);
                await AddProjectsSectionAsync(items, cancellationToken);
                await AddGoalsSectionAsync(items, cancellationToken);
                await AddCapacitySectionAsync(items, today, cancellationToken);
                break;
        }

        // A request scoped to one entity always includes that entity, whatever the category.
        if (request.RelatedEntityId is { } relatedId && !string.IsNullOrWhiteSpace(request.RelatedEntityType))
        {
            var scoped = await BuildScopedItemAsync(request.RelatedEntityType, relatedId, cancellationToken);
            if (scoped is not null)
            {
                items.Insert(1, scoped);
            }
        }

        var budgeted = ApplyBudget(items, request.CharacterBudget);

        return new AiContext(
            budgeted,
            AggregateLevel(budgeted),
            budgeted.Sum(i => i.RenderedText.Length));
    }

    // -----------------------------------------------------------------------------------------
    // Sections
    // -----------------------------------------------------------------------------------------

    private ContextItem BuildTemporalAnchor(DateOnly today)
    {
        var text = new StringBuilder()
            .AppendLine($"تاریخ امروز (شمسی): {FormatJalali(today)}")
            .AppendLine($"روز هفته: {DayName(today.DayOfWeek)}")
            .Append($"تاریخ میلادی: {today:yyyy-MM-dd}")
            .ToString();

        return new ContextItem("temporal", "زمینه زمانی", text, SensitivityLevel.Public, []);
    }

    private async Task AddFocusSectionAsync(
        List<ContextItem> items,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var openTasks = await _db.ToListAsync(
            _db.Tasks
                .Where(t => t.Status != LifeTaskStatus.Completed
                            && t.Status != LifeTaskStatus.Cancelled
                            && t.Status != LifeTaskStatus.Deferred)
                .OrderBy(t => t.DueDate == null)
                .ThenBy(t => t.DueDate)
                .Take(40),
            cancellationToken);

        if (openTasks.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var task in openTasks)
        {
            var due = task.DueDate is { } d
                ? (d < today ? $" — عقب‌افتاده از {FormatJalali(d)}" : $" — سررسید {FormatJalali(d)}")
                : string.Empty;

            text.AppendLine($"- [{StatusLabel(task.Status)}] {WrapUserData(task.Title)} " +
                            $"(اولویت: {PriorityLabel(task.Priority)}{due})");
        }

        var sources = openTasks
            .Select(t => new ContextSource("Task", t.Id, t.Title))
            .ToList();

        items.Add(new ContextItem(
            "openTasks",
            "کارهای باز",
            text.ToString().TrimEnd(),
            SensitivityLevel.Personal,
            sources));
    }

    private async Task AddProjectsSectionAsync(
        List<ContextItem> items,
        CancellationToken cancellationToken)
    {
        var projects = await _db.ToListAsync(
            _db.Projects
                .Where(p => !p.IsArchived
                            && p.Status != ProjectStatus.Completed
                            && p.Status != ProjectStatus.Cancelled)
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.TargetEndDate == null)
                .ThenBy(p => p.TargetEndDate)
                .Take(20),
            cancellationToken);

        if (projects.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var project in projects)
        {
            var target = project.TargetEndDate is { } d ? $" — هدف {FormatJalali(d)}" : string.Empty;
            text.AppendLine($"- {WrapUserData(project.Name)} " +
                            $"(وضعیت: {ProjectStatusLabel(project.Status)}, " +
                            $"پیشرفت: {project.ProgressPercentage:0}%{target})");
        }

        var sources = projects
            .Select(p => new ContextSource("Project", p.Id, p.Name))
            .ToList();

        items.Add(new ContextItem(
            "activeProjects",
            "پروژه‌های فعال",
            text.ToString().TrimEnd(),
            SensitivityLevel.Personal,
            sources));
    }

    private async Task AddGoalsSectionAsync(
        List<ContextItem> items,
        CancellationToken cancellationToken)
    {
        var goals = await _db.ToListAsync(
            _db.Goals
                .Where(g => g.Status == GoalStatus.Active)
                .OrderByDescending(g => g.Priority)
                .Take(15),
            cancellationToken);

        if (goals.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var goal in goals)
        {
            var target = goal.TargetDate is { } d ? $" — هدف {FormatJalali(d)}" : string.Empty;
            text.AppendLine($"- {WrapUserData(goal.Name)} " +
                            $"(نوع: {GoalTypeLabel(goal.GoalType)}, " +
                            $"پیشرفت: {goal.ProgressPercentage:0}%{target})");
        }

        var sources = goals
            .Select(g => new ContextSource("Goal", g.Id, g.Name))
            .ToList();

        items.Add(new ContextItem(
            "activeGoals",
            "اهداف فعال",
            text.ToString().TrimEnd(),
            SensitivityLevel.Personal,
            sources));
    }

    private async Task AddCapacitySectionAsync(
        List<ContextItem> items,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var weekStart = UseCases.Planning.PlanningService.StartOfWeek(today);

        var plan = await _db.FirstOrDefaultAsync(
            _db.WeeklyPlans.Where(w => w.WeekStartDate == weekStart),
            cancellationToken);

        if (plan is null)
        {
            return;
        }

        var text = new StringBuilder()
            .AppendLine($"هفته از {FormatJalali(plan.WeekStartDate)} تا {FormatJalali(plan.WeekEndDate)}")
            .AppendLine($"ساعت در دسترس: {plan.AvailableHours:0.#}")
            .AppendLine($"ساعت برنامه‌ریزی‌شده: {plan.PlannedHours:0.#}")
            .Append($"اضافه‌بار: {plan.OverloadHours:0.#} ساعت")
            .ToString();

        items.Add(new ContextItem(
            "weeklyCapacity",
            "ظرفیت هفته جاری",
            text,
            SensitivityLevel.Personal,
            [new ContextSource("WeeklyPlan", plan.Id, $"هفته {FormatJalali(plan.WeekStartDate)}")]));
    }

    private async Task AddFinanceSectionAsync(
        List<ContextItem> items,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var accounts = await _db.ToListAsync(
            _db.FinancialAccounts.Where(a => a.IsActive),
            cancellationToken);

        var transactions = await _db.ToListAsync(
            _db.FinancialTransactions
                .Where(t => t.TransactionDate >= today.AddDays(-30))
                .OrderByDescending(t => t.TransactionDate)
                .Take(60),
            cancellationToken);

        var commitments = await _db.ToListAsync(
            _db.FinancialCommitments
                .Where(c => c.Status == CommitmentStatus.Active || c.Status == CommitmentStatus.Overdue)
                .OrderBy(c => c.NextDueDate)
                .Take(20),
            cancellationToken);

        if (accounts.Count == 0 && transactions.Count == 0 && commitments.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();

        if (accounts.Count > 0)
        {
            text.AppendLine("### حساب‌ها");
            foreach (var account in accounts)
            {
                text.AppendLine($"- {WrapUserData(account.Name)} ({account.CurrencyCode}): " +
                                $"موجودی اولیه {account.InitialBalance:0.##}");
            }
        }

        if (transactions.Count > 0)
        {
            text.AppendLine("### تراکنش‌های ۳۰ روز گذشته");
            foreach (var transaction in transactions)
            {
                text.AppendLine($"- {FormatJalali(transaction.TransactionDate)} " +
                                $"{TransactionTypeLabel(transaction.TransactionType)} " +
                                $"{transaction.Amount:0.##} {transaction.CurrencyCode}" +
                                (string.IsNullOrWhiteSpace(transaction.Category)
                                    ? string.Empty
                                    : $" — {WrapUserData(transaction.Category)}"));
            }
        }

        if (commitments.Count > 0)
        {
            text.AppendLine("### تعهدات مالی جاری");
            foreach (var commitment in commitments)
            {
                var due = commitment.NextDueDate is { } d
                    ? $", سررسید بعدی {FormatJalali(d)}"
                    : string.Empty;

                text.AppendLine($"- {WrapUserData(commitment.Name)}: " +
                                $"باقی‌مانده {commitment.RemainingAmount:0.##} {commitment.CurrencyCode}{due}");
            }
        }

        var sources = accounts.Select(a => new ContextSource("FinancialAccount", a.Id, a.Name))
            .Concat(commitments.Select(c => new ContextSource("FinancialCommitment", c.Id, c.Name)))
            .ToList();

        items.Add(new ContextItem(
            "finance",
            "وضعیت مالی",
            text.ToString().TrimEnd(),
            SensitivityLevel.Sensitive,
            sources));
    }

    private async Task AddJournalSectionAsync(
        List<ContextItem> items,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var entries = await _db.ToListAsync(
            _db.JournalEntries
                .Where(j => j.EntryDate >= today.AddDays(-14))
                .OrderByDescending(j => j.EntryDate)
                .Take(10),
            cancellationToken);

        if (entries.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            var wellbeing = new List<string>();
            if (entry.Mood is { } mood) wellbeing.Add($"خلق {mood}/10");
            if (entry.EnergyLevel is { } energy) wellbeing.Add($"انرژی {energy}/10");
            if (entry.StressLevel is { } stress) wellbeing.Add($"استرس {stress}/10");

            text.AppendLine($"- {FormatJalali(entry.EntryDate)}: {WrapUserData(entry.Title)}" +
                            (wellbeing.Count > 0 ? $" ({string.Join(", ", wellbeing)})" : string.Empty));
            text.AppendLine($"  {WrapUserData(Truncate(entry.Content, 240))}");
        }

        var sources = entries
            .Select(e => new ContextSource("JournalEntry", e.Id, e.Title))
            .ToList();

        items.Add(new ContextItem(
            "journal",
            "دفتر روزانه (۱۴ روز گذشته)",
            text.ToString().TrimEnd(),
            SensitivityLevel.Sensitive,
            sources));
    }

    private async Task<ContextItem?> BuildScopedItemAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        switch (entityType)
        {
            case "Project":
            {
                var project = await _db.FirstOrDefaultAsync(
                    _db.Projects.Where(p => p.Id == entityId),
                    cancellationToken);

                if (project is null) return null;

                var text = new StringBuilder()
                    .AppendLine($"نام: {WrapUserData(project.Name)}")
                    .AppendLine($"وضعیت: {ProjectStatusLabel(project.Status)}")
                    .AppendLine($"پیشرفت: {project.ProgressPercentage:0}%")
                    .AppendLine($"توضیح: {WrapUserData(Truncate(project.Description, 400))}")
                    .Append($"یادداشت: {WrapUserData(Truncate(project.Notes, 400))}")
                    .ToString();

                return new ContextItem(
                    "scopedProject",
                    "پروژه انتخاب‌شده",
                    text,
                    SensitivityLevel.Personal,
                    [new ContextSource("Project", project.Id, project.Name)]);
            }

            case "Goal":
            {
                var goal = await _db.FirstOrDefaultAsync(
                    _db.Goals.Where(g => g.Id == entityId),
                    cancellationToken);

                if (goal is null) return null;

                var text = new StringBuilder()
                    .AppendLine($"نام: {WrapUserData(goal.Name)}")
                    .AppendLine($"وضعیت: {GoalStatusLabel(goal.Status)}")
                    .AppendLine($"پیشرفت: {goal.ProgressPercentage:0}%")
                    .AppendLine($"چرا مهم است: {WrapUserData(Truncate(goal.WhyItMatters, 400))}")
                    .Append($"معیار موفقیت: {WrapUserData(Truncate(goal.SuccessCriteria, 400))}")
                    .ToString();

                return new ContextItem(
                    "scopedGoal",
                    "هدف انتخاب‌شده",
                    text,
                    SensitivityLevel.Personal,
                    [new ContextSource("Goal", goal.Id, goal.Name)]);
            }

            case "Task":
            {
                var task = await _db.FirstOrDefaultAsync(
                    _db.Tasks.Where(t => t.Id == entityId),
                    cancellationToken);

                if (task is null) return null;

                var text = new StringBuilder()
                    .AppendLine($"عنوان: {WrapUserData(task.Title)}")
                    .AppendLine($"وضعیت: {StatusLabel(task.Status)}")
                    .AppendLine($"اولویت: {PriorityLabel(task.Priority)}")
                    .Append($"توضیح: {WrapUserData(Truncate(task.Description, 400))}")
                    .ToString();

                return new ContextItem(
                    "scopedTask",
                    "کار انتخاب‌شده",
                    text,
                    SensitivityLevel.Personal,
                    [new ContextSource("Task", task.Id, task.Title)]);
            }

            case "Note":
            {
                var note = await _db.FirstOrDefaultAsync(
                    _db.Notes.Where(n => n.Id == entityId),
                    cancellationToken);

                if (note is null) return null;

                var text = new StringBuilder()
                    .AppendLine($"عنوان: {WrapUserData(note.Title)}")
                    .Append($"محتوا: {WrapUserData(Truncate(note.Content, 1200))}")
                    .ToString();

                return new ContextItem(
                    "scopedNote",
                    "یادداشت انتخاب‌شده",
                    text,
                    SensitivityLevel.Personal,
                    [new ContextSource("Note", note.Id, note.Title)]);
            }

            default:
                return null;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Budget and aggregation
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Drops whole sections once the budget is exceeded. The temporal anchor and the scoped entity
    /// are always kept, because without them the model cannot answer meaningfully.
    /// </summary>
    private static List<ContextItem> ApplyBudget(List<ContextItem> items, int characterBudget)
    {
        if (characterBudget <= 0)
        {
            characterBudget = 6000;
        }

        var kept = new List<ContextItem>();
        var used = 0;

        foreach (var item in items)
        {
            var isEssential = item.Key is "temporal" or "scopedProject" or "scopedGoal"
                or "scopedTask" or "scopedNote";

            if (!isEssential && used + item.RenderedText.Length > characterBudget)
            {
                continue;
            }

            kept.Add(item);
            used += item.RenderedText.Length;
        }

        return kept;
    }

    private static SensitivityLevel AggregateLevel(IReadOnlyList<ContextItem> items)
    {
        if (items.Any(i => i.Level == SensitivityLevel.Sensitive))
        {
            return SensitivityLevel.Sensitive;
        }

        if (items.Any(i => i.Level == SensitivityLevel.Personal))
        {
            return SensitivityLevel.Personal;
        }

        return SensitivityLevel.Public;
    }

    // -----------------------------------------------------------------------------------------
    // Formatting helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Wraps user-authored text so the system prompt can tell the model this is data, not
    /// instructions. The guard strips any attempt to close the block early.
    /// </summary>
    private string WrapUserData(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(خالی)";
        }

        var scan = _injectionGuard.Scan(text);
        return $"<user_data>{scan.SanitizedContent}</user_data>";
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(خالی)";
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }

    private static string FormatJalali(DateOnly date)
        => Formatting.JalaliDateConverter.ToJalali(date).ToString();

    private static string DayName(DayOfWeek day) => Formatting.JalaliDateConverter.DayName(day);

    private static string StatusLabel(LifeTaskStatus status) => status switch
    {
        LifeTaskStatus.Inbox => "صندوق ورودی",
        LifeTaskStatus.Todo => "انجام‌نشده",
        LifeTaskStatus.InProgress => "در حال انجام",
        LifeTaskStatus.Waiting => "در انتظار",
        LifeTaskStatus.Completed => "انجام‌شده",
        LifeTaskStatus.Cancelled => "لغو‌شده",
        LifeTaskStatus.Deferred => "به تعویق افتاده",
        _ => status.ToString()
    };

    private static string PriorityLabel(PriorityLevel priority) => priority switch
    {
        PriorityLevel.None => "بدون اولویت",
        PriorityLevel.Low => "کم",
        PriorityLevel.Medium => "متوسط",
        PriorityLevel.High => "زیاد",
        PriorityLevel.Critical => "بحرانی",
        _ => priority.ToString()
    };

    private static string ProjectStatusLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.Draft => "پیش‌نویس",
        ProjectStatus.Planned => "برنامه‌ریزی‌شده",
        ProjectStatus.Active => "فعال",
        ProjectStatus.OnHold => "متوقف",
        ProjectStatus.Completed => "تکمیل‌شده",
        ProjectStatus.Cancelled => "لغو‌شده",
        ProjectStatus.Archived => "بایگانی",
        _ => status.ToString()
    };

    private static string GoalStatusLabel(GoalStatus status) => status switch
    {
        GoalStatus.Draft => "پیش‌نویس",
        GoalStatus.Active => "فعال",
        GoalStatus.OnHold => "متوقف",
        GoalStatus.Completed => "تکمیل‌شده",
        GoalStatus.Abandoned => "رها‌شده",
        _ => status.ToString()
    };

    private static string GoalTypeLabel(GoalType type) => type switch
    {
        GoalType.ShortTerm => "کوتاه‌مدت",
        GoalType.MidTerm => "میان‌مدت",
        GoalType.LongTerm => "بلندمدت",
        GoalType.Habit => "عادت",
        GoalType.Outcome => "نتیجه",
        _ => type.ToString()
    };

    private static string TransactionTypeLabel(TransactionType type) => type switch
    {
        TransactionType.Income => "درآمد",
        TransactionType.Expense => "هزینه",
        TransactionType.Transfer => "انتقال",
        TransactionType.Adjustment => "تعدیل",
        _ => type.ToString()
    };
}
