using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Projects;

public sealed class Project : AuditableEntity, IConcurrencyTracked
{
    private readonly List<ProjectMilestone> _milestones = [];
    private readonly List<GoalProject> _goalLinks = [];

    private Project() { }

    public Project(string name, Guid? lifeDomainId, string? description)
    {
        Rename(name);
        LifeDomainId = lifeDomainId;
        Description = description;
        Status = ProjectStatus.Draft;
        Priority = PriorityLevel.Medium;
        CurrencyCode = "IRR";
    }

    public Guid? LifeDomainId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ProjectStatus Status { get; private set; }
    public PriorityLevel Priority { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? TargetEndDate { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public decimal? EstimatedBudget { get; private set; }
    public decimal? ActualBudget { get; private set; }
    public string CurrencyCode { get; private set; } = "IRR";
    public decimal? EstimatedEffortHours { get; private set; }
    public decimal? ActualEffortHours { get; private set; }
    public decimal ProgressPercentage { get; private set; }
    public bool IsPinned { get; private set; }
    public bool IsArchived { get; private set; }
    public string? Notes { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<ProjectMilestone> Milestones => _milestones;
    public IReadOnlyCollection<GoalProject> GoalLinks => _goalLinks;

    /// <summary>True when the project is part of active execution views.</summary>
    public bool IsExecutable => !IsArchived && Status is ProjectStatus.Active or ProjectStatus.Planned;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("Project.NameRequired", "نام پروژه الزامی است.");
        }

        Name = name.Trim();
    }

    public void UpdateDetails(
        string name,
        Guid? lifeDomainId,
        string? description,
        PriorityLevel priority,
        DateOnly? startDate,
        DateOnly? targetEndDate,
        decimal? estimatedBudget,
        string currencyCode,
        decimal? estimatedEffortHours,
        string? notes)
    {
        Rename(name);

        if (startDate.HasValue && targetEndDate.HasValue && targetEndDate < startDate)
        {
            throw new DomainRuleException("Project.InvalidDateRange", "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.");
        }

        if (estimatedBudget is < 0)
        {
            throw new DomainRuleException("Project.NegativeBudget", "بودجه تخمینی نمی‌تواند منفی باشد.");
        }

        if (estimatedEffortHours is < 0)
        {
            throw new DomainRuleException("Project.NegativeEffort", "تلاش تخمینی نمی‌تواند منفی باشد.");
        }

        LifeDomainId = lifeDomainId;
        Description = description;
        Priority = priority;
        StartDate = startDate;
        TargetEndDate = targetEndDate;
        EstimatedBudget = estimatedBudget;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "IRR" : currencyCode.Trim().ToUpperInvariant();
        EstimatedEffortHours = estimatedEffortHours;
        Notes = notes;
    }

    public void ChangeStatus(ProjectStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return;
        }

        if (Status is ProjectStatus.Archived && status != ProjectStatus.Archived)
        {
            throw new DomainRuleException("Project.ArchivedIsTerminal", "پروژه بایگانی‌شده قابل بازگشایی مستقیم نیست.");
        }

        if (status == ProjectStatus.Completed)
        {
            CompletedAtUtc = now;
            ProgressPercentage = 100m;
        }
        else if (Status == ProjectStatus.Completed)
        {
            CompletedAtUtc = null;
        }

        Status = status;
    }

    public void SetPriority(PriorityLevel priority) => Priority = priority;

    public void SetPinned(bool isPinned) => IsPinned = isPinned;

    public void Archive()
    {
        IsArchived = true;
        Status = ProjectStatus.Archived;
    }

    public void Restore()
    {
        IsArchived = false;
        if (Status == ProjectStatus.Archived)
        {
            Status = ProjectStatus.OnHold;
        }
    }

    /// <summary>
    /// Progress is derived from milestones when any exist, otherwise from the explicit value.
    /// Manual progress is only accepted when there are no milestones, so the number always has
    /// a traceable source.
    /// </summary>
    public void RecalculateProgress()
    {
        if (_milestones.Count == 0)
        {
            return;
        }

        var countable = _milestones.Where(m => m.Status != MilestoneStatus.Cancelled).ToList();
        if (countable.Count == 0)
        {
            return;
        }

        var completed = countable.Count(m => m.Status == MilestoneStatus.Completed);
        ProgressPercentage = Math.Round(completed * 100m / countable.Count, 2);
    }

    public void SetManualProgress(decimal percentage)
    {
        if (_milestones.Count > 0)
        {
            throw new DomainRuleException(
                "Project.ProgressDerived",
                "این پروژه نقطه‌عطف دارد؛ درصد پیشرفت از روی نقطه‌عطف‌ها محاسبه می‌شود.");
        }

        ProgressPercentage = Math.Clamp(Math.Round(percentage, 2), 0m, 100m);
    }

    public void RecordActualEffort(decimal hours)
    {
        if (hours < 0)
        {
            throw new DomainRuleException("Project.NegativeEffort", "تلاش واقعی نمی‌تواند منفی باشد.");
        }

        ActualEffortHours = hours;
    }

    public void RecordActualBudget(decimal amount)
    {
        if (amount < 0)
        {
            throw new DomainRuleException("Project.NegativeBudget", "هزینه واقعی نمی‌تواند منفی باشد.");
        }

        ActualBudget = amount;
    }

    public ProjectMilestone AddMilestone(string name, string? description, DateOnly? dueDate, int sortOrder)
    {
        var milestone = new ProjectMilestone(Id, name, description, dueDate, sortOrder);
        _milestones.Add(milestone);
        return milestone;
    }

    public void RemoveMilestone(ProjectMilestone milestone)
    {
        _milestones.Remove(milestone);
        RecalculateProgress();
    }

    public void LinkGoal(Guid goalId, decimal contributionWeight, DateTimeOffset now)
    {
        if (_goalLinks.Any(l => l.GoalId == goalId))
        {
            throw new DomainRuleException("Project.GoalAlreadyLinked", "این هدف قبلاً به پروژه متصل شده است.");
        }

        _goalLinks.Add(new GoalProject(goalId, Id, contributionWeight, now));
    }

    public void UnlinkGoal(Guid goalId)
    {
        var link = _goalLinks.FirstOrDefault(l => l.GoalId == goalId);
        if (link is not null)
        {
            _goalLinks.Remove(link);
        }
    }
}

public sealed class ProjectMilestone : AuditableEntity
{
    private ProjectMilestone() { }

    internal ProjectMilestone(Guid projectId, string name, string? description, DateOnly? dueDate, int sortOrder)
    {
        ProjectId = projectId;
        Rename(name);
        Description = description;
        DueDate = dueDate;
        SortOrder = sortOrder;
        Status = MilestoneStatus.Pending;
    }

    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public MilestoneStatus Status { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int SortOrder { get; private set; }

    public Project? Project { get; private set; }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("Milestone.NameRequired", "نام نقطه‌عطف الزامی است.");
        }

        Name = name.Trim();
    }

    public void Update(string name, string? description, DateOnly? dueDate, int sortOrder)
    {
        Rename(name);
        Description = description;
        DueDate = dueDate;
        SortOrder = sortOrder;
    }

    public void ChangeStatus(MilestoneStatus status, DateTimeOffset now)
    {
        Status = status;
        CompletedAtUtc = status == MilestoneStatus.Completed ? now : null;
    }
}

public sealed class ProjectDependency : Entity
{
    private ProjectDependency() { }

    public ProjectDependency(Guid projectId, Guid dependsOnProjectId, DependencyType dependencyType, DateTimeOffset createdAtUtc)
    {
        if (projectId == dependsOnProjectId)
        {
            throw new DomainRuleException("ProjectDependency.SelfReference", "یک پروژه نمی‌تواند به خودش وابسته باشد.");
        }

        ProjectId = projectId;
        DependsOnProjectId = dependsOnProjectId;
        DependencyType = dependencyType;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ProjectId { get; private set; }
    public Guid DependsOnProjectId { get; private set; }
    public DependencyType DependencyType { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Project? Project { get; private set; }
    public Project? DependsOnProject { get; private set; }
}

public sealed class GoalProject : Entity
{
    private GoalProject() { }

    internal GoalProject(Guid goalId, Guid projectId, decimal contributionWeight, DateTimeOffset createdAtUtc)
    {
        GoalId = goalId;
        ProjectId = projectId;
        ContributionWeight = Math.Clamp(contributionWeight, 0m, 100m);
        CreatedAtUtc = createdAtUtc;
    }

    public Guid GoalId { get; private set; }
    public Guid ProjectId { get; private set; }
    public decimal ContributionWeight { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Project? Project { get; private set; }
}
