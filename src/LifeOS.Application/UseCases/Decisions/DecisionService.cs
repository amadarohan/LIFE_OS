using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Decisions;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Decisions;

public sealed record DecisionOptionView(
    Guid Id,
    string Name,
    string? Description,
    string? ExpectedBenefits,
    string? PotentialRisks,
    decimal? EstimatedCost,
    decimal? EstimatedEffort,
    decimal? ProbabilityOfSuccess,
    string? OpportunityCost,
    decimal? Score,
    bool IsChosen);

public sealed record DecisionSummary(
    Guid Id,
    string Title,
    DecisionStatus Status,
    ImportanceLevel Importance,
    DateOnly DecisionDate,
    DateOnly? ReviewDate,
    int OptionCount,
    string? ChosenOptionName,
    bool IsReviewDue);

public sealed record DecisionDetail(
    Guid Id,
    string Title,
    string? Description,
    string? Context,
    DateOnly DecisionDate,
    DecisionStatus Status,
    ImportanceLevel Importance,
    Guid? ChosenOptionId,
    string? ExpectedOutcome,
    string? ActualOutcome,
    DateOnly? ReviewDate,
    IReadOnlyList<DecisionOptionView> Options);

public sealed record CreateDecisionRequest(
    string Title,
    string? Description,
    string? Context,
    DateOnly DecisionDate,
    ImportanceLevel Importance,
    string? ExpectedOutcome,
    DateOnly? ReviewDate);

public sealed record AddDecisionOptionRequest(
    Guid DecisionId,
    string Name,
    string? Description,
    string? ExpectedBenefits,
    string? PotentialRisks,
    decimal? EstimatedCost,
    decimal? EstimatedEffort,
    decimal? ProbabilityOfSuccess,
    string? OpportunityCost,
    decimal? Score);

public sealed record DecisionFilter(
    DecisionStatus? Status = null,
    ImportanceLevel? Importance = null,
    bool ReviewDueOnly = false);

/// <summary>
/// Decision journal. A decision cannot be marked decided without a chosen option, and the chosen
/// option cannot be deleted, so the record stays auditable after the fact.
/// </summary>
public sealed class DecisionService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public DecisionService(IApplicationDbContext db, IAuditWriter audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<DecisionSummary>> ListAsync(
        DecisionFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Decisions.AsQueryable();
        var today = _clock.TodayUtc;

        if (filter.Status.HasValue)
        {
            query = query.Where(d => d.Status == filter.Status.Value);
        }

        if (filter.Importance.HasValue)
        {
            query = query.Where(d => d.Importance == filter.Importance.Value);
        }

        if (filter.ReviewDueOnly)
        {
            query = query.Where(d => d.ReviewDate != null
                && d.ReviewDate <= today
                && d.Status == DecisionStatus.Decided);
        }

        var decisions = await _db.ToListAsync(
            query.OrderByDescending(d => d.DecisionDate),
            cancellationToken);

        if (decisions.Count == 0)
        {
            return [];
        }

        var decisionIds = decisions.Select(d => d.Id).ToList();

        var options = await _db.ToListAsync(
            _db.DecisionOptions.Where(o => decisionIds.Contains(o.DecisionId)),
            cancellationToken);

        var optionsByDecision = options
            .GroupBy(o => o.DecisionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return decisions.Select(d =>
        {
            var decisionOptions = optionsByDecision.GetValueOrDefault(d.Id) ?? [];
            var chosen = d.ChosenOptionId.HasValue
                ? decisionOptions.FirstOrDefault(o => o.Id == d.ChosenOptionId.Value)?.Name
                : null;

            return new DecisionSummary(
                d.Id,
                d.Title,
                d.Status,
                d.Importance,
                d.DecisionDate,
                d.ReviewDate,
                decisionOptions.Count,
                chosen,
                d.ReviewDate.HasValue
                    && d.ReviewDate.Value <= today
                    && d.Status == DecisionStatus.Decided);
        }).ToList();
    }

    public async Task<Result<DecisionDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var decision = await _db.FindDecisionAsync(id, cancellationToken);
        if (decision is null)
        {
            return Guard.NotFound<DecisionDetail>("تصمیم", id);
        }

        return Result.Success(new DecisionDetail(
            decision.Id,
            decision.Title,
            decision.Description,
            decision.Context,
            decision.DecisionDate,
            decision.Status,
            decision.Importance,
            decision.ChosenOptionId,
            decision.ExpectedOutcome,
            decision.ActualOutcome,
            decision.ReviewDate,
            decision.Options
                .OrderByDescending(o => o.Score ?? 0m)
                .Select(o => new DecisionOptionView(
                    o.Id,
                    o.Name,
                    o.Description,
                    o.ExpectedBenefits,
                    o.PotentialRisks,
                    o.EstimatedCost,
                    o.EstimatedEffort,
                    o.ProbabilityOfSuccess,
                    o.OpportunityCost,
                    o.Score,
                    decision.ChosenOptionId == o.Id))
                .ToList()));
    }

    public Task<Result<Guid>> CreateAsync(
        CreateDecisionRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = new Decision(
                request.Title,
                request.Description,
                request.Context,
                request.DecisionDate,
                request.Importance);

            decision.UpdateDetails(
                request.Title,
                request.Description,
                request.Context,
                request.DecisionDate,
                request.Importance,
                request.ExpectedOutcome,
                request.ReviewDate);

            _db.Add(decision);
            await _audit.WriteAsync(
                nameof(Decision),
                decision.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(decision.Id);
        });

    public Task<Result> UpdateAsync(
        Guid id,
        string title,
        string? description,
        string? context,
        DateOnly decisionDate,
        ImportanceLevel importance,
        string? expectedOutcome,
        DateOnly? reviewDate,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = await _db.FindDecisionAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Decision), id);

            decision.UpdateDetails(
                title,
                description,
                context,
                decisionDate,
                importance,
                expectedOutcome,
                reviewDate);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeStatusAsync(
        Guid id,
        DecisionStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = await _db.FindDecisionAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Decision), id);

            if (status == DecisionStatus.Decided && decision.ChosenOptionId is null)
            {
                throw new DomainRuleException(
                    "Decision.OptionRequired",
                    "برای ثبت تصمیم باید یکی از گزینه‌ها انتخاب شود.");
            }

            decision.ChangeStatus(status);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> AddOptionAsync(
        AddDecisionOptionRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = await _db.FindDecisionAsync(request.DecisionId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Decision), request.DecisionId);

            var option = decision.AddOption(
                request.Name,
                request.Description,
                request.ExpectedBenefits,
                request.PotentialRisks,
                request.EstimatedCost,
                request.EstimatedEffort,
                request.ProbabilityOfSuccess,
                request.OpportunityCost,
                request.Score);

            option.StampCreated(_clock.UtcNow);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(option.Id);
        });

    public Task<Result> UpdateOptionAsync(
        Guid decisionId,
        Guid optionId,
        string name,
        string? description,
        string? expectedBenefits,
        string? potentialRisks,
        decimal? estimatedCost,
        decimal? estimatedEffort,
        decimal? probabilityOfSuccess,
        string? opportunityCost,
        decimal? score,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = await _db.FindDecisionAsync(decisionId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Decision), decisionId);

            var option = decision.Options.FirstOrDefault(o => o.Id == optionId)
                ?? throw new EntityNotFoundException(nameof(DecisionOption), optionId);

            option.Update(
                name,
                description,
                expectedBenefits,
                potentialRisks,
                estimatedCost,
                estimatedEffort,
                probabilityOfSuccess,
                opportunityCost,
                score);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RemoveOptionAsync(
        Guid decisionId,
        Guid optionId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = await _db.FindDecisionAsync(decisionId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Decision), decisionId);

            var option = decision.Options.FirstOrDefault(o => o.Id == optionId)
                ?? throw new EntityNotFoundException(nameof(DecisionOption), optionId);

            decision.RemoveOption(option);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChooseOptionAsync(
        Guid decisionId,
        Guid optionId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = await _db.FindDecisionAsync(decisionId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Decision), decisionId);

            decision.ChooseOption(optionId);

            await _audit.WriteAsync(
                nameof(Decision),
                decision.Id,
                AuditActionType.Updated,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> RecordOutcomeAsync(
        Guid decisionId,
        string? actualOutcome,
        string? whatWentWell,
        string? whatWentWrong,
        string? lessonsLearned,
        bool? wasSuccessful,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var decision = await _db.FindDecisionAsync(decisionId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Decision), decisionId);

            decision.RecordActualOutcome(actualOutcome);

            var review = new DecisionReview(
                decisionId,
                _clock.TodayUtc,
                actualOutcome,
                whatWentWell,
                whatWentWrong,
                lessonsLearned,
                wasSuccessful);

            review.StampCreated(_clock.UtcNow);

            _db.Add(review);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<DecisionReview>> ListReviewsAsync(
        Guid decisionId,
        CancellationToken cancellationToken = default)
        => await _db.ToListAsync(
            _db.DecisionReviews
                .Where(r => r.DecisionId == decisionId)
                .OrderByDescending(r => r.ReviewDate),
            cancellationToken);
}
