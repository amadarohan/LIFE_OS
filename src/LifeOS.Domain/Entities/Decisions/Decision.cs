using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Decisions;

public sealed class Decision : AuditableEntity, IConcurrencyTracked
{
    private readonly List<DecisionOption> _options = [];

    private Decision() { }

    public Decision(string title, string? description, string? context, DateOnly decisionDate, ImportanceLevel importance)
    {
        Rename(title);
        Description = description;
        Context = context;
        DecisionDate = decisionDate;
        Importance = importance;
        Status = DecisionStatus.Draft;
    }

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Context { get; private set; }
    public DateOnly DecisionDate { get; private set; }
    public DecisionStatus Status { get; private set; }
    public ImportanceLevel Importance { get; private set; }
    public Guid? ChosenOptionId { get; private set; }
    public string? ExpectedOutcome { get; private set; }
    public string? ActualOutcome { get; private set; }
    public DateOnly? ReviewDate { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<DecisionOption> Options => _options;

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("Decision.TitleRequired", "عنوان تصمیم الزامی است.");
        }

        Title = title.Trim();
    }

    public void UpdateDetails(string title, string? description, string? context, DateOnly decisionDate,
        ImportanceLevel importance, string? expectedOutcome, DateOnly? reviewDate)
    {
        Rename(title);
        Description = description;
        Context = context;
        DecisionDate = decisionDate;
        Importance = importance;
        ExpectedOutcome = expectedOutcome;
        ReviewDate = reviewDate;
    }

    public void ChangeStatus(DecisionStatus status) => Status = status;

    public DecisionOption AddOption(
        string name,
        string? description,
        string? expectedBenefits,
        string? potentialRisks,
        decimal? estimatedCost,
        decimal? estimatedEffort,
        decimal? probabilityOfSuccess,
        string? opportunityCost,
        decimal? score)
    {
        var option = new DecisionOption(Id, name, description, expectedBenefits, potentialRisks,
            estimatedCost, estimatedEffort, probabilityOfSuccess, opportunityCost, score);
        _options.Add(option);
        return option;
    }

    public void RemoveOption(DecisionOption option)
    {
        if (ChosenOptionId == option.Id)
        {
            throw new DomainRuleException("Decision.CannotRemoveChosen", "گزینه انتخاب‌شده قابل حذف نیست.");
        }

        _options.Remove(option);
    }

    /// <summary>
    /// Records the chosen option. The option must belong to this decision, and the decision
    /// moves to <see cref="DecisionStatus.Decided"/>.
    /// </summary>
    public void ChooseOption(Guid optionId)
    {
        if (_options.All(o => o.Id != optionId))
        {
            throw new DomainRuleException("Decision.OptionNotInDecision", "گزینه انتخاب‌شده متعلق به این تصمیم نیست.");
        }

        ChosenOptionId = optionId;
        Status = DecisionStatus.Decided;
    }

    public void RecordActualOutcome(string? actualOutcome)
    {
        ActualOutcome = actualOutcome;
    }
}

public sealed class DecisionOption : Entity
{
    private DecisionOption() { }

    internal DecisionOption(
        Guid decisionId,
        string name,
        string? description,
        string? expectedBenefits,
        string? potentialRisks,
        decimal? estimatedCost,
        decimal? estimatedEffort,
        decimal? probabilityOfSuccess,
        string? opportunityCost,
        decimal? score)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("DecisionOption.NameRequired", "نام گزینه الزامی است.");
        }

        if (probabilityOfSuccess is < 0m or > 100m)
        {
            throw new DomainRuleException("DecisionOption.InvalidProbability", "احتمال موفقیت باید بین ۰ تا ۱۰۰ باشد.");
        }

        DecisionId = decisionId;
        Name = name.Trim();
        Description = description;
        ExpectedBenefits = expectedBenefits;
        PotentialRisks = potentialRisks;
        EstimatedCost = estimatedCost;
        EstimatedEffort = estimatedEffort;
        ProbabilityOfSuccess = probabilityOfSuccess;
        OpportunityCost = opportunityCost;
        Score = score;
        CreatedAtUtc = DateTimeOffset.MinValue;
    }

    public Guid DecisionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? ExpectedBenefits { get; private set; }
    public string? PotentialRisks { get; private set; }
    public decimal? EstimatedCost { get; private set; }
    public decimal? EstimatedEffort { get; private set; }
    public decimal? ProbabilityOfSuccess { get; private set; }
    public string? OpportunityCost { get; private set; }
    public decimal? Score { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Decision? Decision { get; private set; }

    public void StampCreated(DateTimeOffset now) => CreatedAtUtc = now;

    public void Update(string name, string? description, string? expectedBenefits, string? potentialRisks,
        decimal? estimatedCost, decimal? estimatedEffort, decimal? probabilityOfSuccess,
        string? opportunityCost, decimal? score)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("DecisionOption.NameRequired", "نام گزینه الزامی است.");
        }

        if (probabilityOfSuccess is < 0m or > 100m)
        {
            throw new DomainRuleException("DecisionOption.InvalidProbability", "احتمال موفقیت باید بین ۰ تا ۱۰۰ باشد.");
        }

        Name = name.Trim();
        Description = description;
        ExpectedBenefits = expectedBenefits;
        PotentialRisks = potentialRisks;
        EstimatedCost = estimatedCost;
        EstimatedEffort = estimatedEffort;
        ProbabilityOfSuccess = probabilityOfSuccess;
        OpportunityCost = opportunityCost;
        Score = score;
    }
}

public sealed class DecisionReview : Entity
{
    private DecisionReview() { }

    public DecisionReview(
        Guid decisionId,
        DateOnly reviewDate,
        string? actualOutcome,
        string? whatWentWell,
        string? whatWentWrong,
        string? lessonsLearned,
        bool? wasDecisionSuccessful)
    {
        DecisionId = decisionId;
        ReviewDate = reviewDate;
        ActualOutcome = actualOutcome;
        WhatWentWell = whatWentWell;
        WhatWentWrong = whatWentWrong;
        LessonsLearned = lessonsLearned;
        WasDecisionSuccessful = wasDecisionSuccessful;
        CreatedAtUtc = DateTimeOffset.MinValue;
    }

    public Guid DecisionId { get; private set; }
    public DateOnly ReviewDate { get; private set; }
    public string? ActualOutcome { get; private set; }
    public string? WhatWentWell { get; private set; }
    public string? WhatWentWrong { get; private set; }
    public string? LessonsLearned { get; private set; }
    public bool? WasDecisionSuccessful { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Decision? Decision { get; private set; }

    public void StampCreated(DateTimeOffset now) => CreatedAtUtc = now;
}
