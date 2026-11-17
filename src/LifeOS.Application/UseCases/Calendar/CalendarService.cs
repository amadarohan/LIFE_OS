using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Calendar;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Calendar;

public sealed record CalendarEventView(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    CalendarEventType EventType,
    string? Location,
    bool IsAllDay,
    Guid? RelatedProjectId,
    Guid? RelatedGoalId);

public sealed record CommitmentItemView(
    Guid Id,
    string Title,
    string? Description,
    CommitmentType CommitmentType,
    DateOnly? DueDate,
    CommitmentStatus Status,
    ImportanceLevel Importance,
    Guid? RelatedProjectId,
    Guid? RelatedTaskId,
    bool IsOverdue);

public sealed record AgendaDay(DateOnly Date, IReadOnlyList<CalendarEventView> Events, IReadOnlyList<CommitmentItemView> Commitments);

public sealed record CreateCalendarEventRequest(
    string Title,
    string? Description,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    CalendarEventType EventType,
    string? Location,
    bool IsAllDay,
    Guid? RelatedProjectId,
    Guid? RelatedGoalId);

public sealed record CreateCommitmentRequest(
    string Title,
    string? Description,
    CommitmentType CommitmentType,
    DateOnly? DueDate,
    ImportanceLevel Importance,
    Guid? RelatedProjectId,
    Guid? RelatedTaskId);

/// <summary>
/// Calendar events and life commitments. The agenda is built from stored rows only; nothing is
/// synthesized for display.
/// </summary>
public sealed class CalendarService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;
    private readonly IUserTimeZone _timeZone;

    public CalendarService(
        IApplicationDbContext db,
        IAuditWriter audit,
        IClock clock,
        IUserTimeZone timeZone)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _timeZone = timeZone;
    }

    public async Task<IReadOnlyList<CalendarEventView>> ListEventsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var events = await _db.ToListAsync(
            _db.CalendarEvents
                .Where(e => e.StartAtUtc < toUtc && e.EndAtUtc > fromUtc)
                .OrderBy(e => e.StartAtUtc),
            cancellationToken);

        return events.Select(ToView).ToList();
    }

    public Task<Result<Guid>> CreateEventAsync(
        CreateCalendarEventRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            await EnsureLinksExistAsync(request.RelatedProjectId, request.RelatedGoalId, null, cancellationToken);

            var calendarEvent = new CalendarEvent(
                request.Title,
                request.Description,
                request.StartAtUtc,
                request.EndAtUtc,
                request.EventType,
                request.Location,
                request.IsAllDay,
                request.RelatedProjectId,
                request.RelatedGoalId);

            _db.Add(calendarEvent);
            await _audit.WriteAsync(
                nameof(CalendarEvent),
                calendarEvent.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(calendarEvent.Id);
        });

    public Task<Result> UpdateEventAsync(
        Guid id,
        string title,
        string? description,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        CalendarEventType eventType,
        string? location,
        bool isAllDay,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var calendarEvent = await _db.FirstOrDefaultAsync(
                _db.CalendarEvents.Where(e => e.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(CalendarEvent), id);

            calendarEvent.Update(title, description, startAtUtc, endAtUtc, eventType, location, isAllDay);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeleteEventAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var calendarEvent = await _db.FirstOrDefaultAsync(
                _db.CalendarEvents.Where(e => e.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(CalendarEvent), id);

            _db.Remove(calendarEvent);
            await _audit.WriteAsync(
                nameof(CalendarEvent),
                id,
                AuditActionType.Deleted,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<CommitmentItemView>> ListCommitmentsAsync(
        bool includeClosed = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Commitments.AsQueryable();
        if (!includeClosed)
        {
            query = query.Where(c => c.Status == CommitmentStatus.Active
                || c.Status == CommitmentStatus.Overdue);
        }

        var commitments = await _db.ToListAsync(
            query.OrderBy(c => c.DueDate ?? DateOnly.MaxValue),
            cancellationToken);

        var today = _clock.TodayUtc;
        return commitments.Select(c => ToView(c, today)).ToList();
    }

    public Task<Result<Guid>> CreateCommitmentAsync(
        CreateCommitmentRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            await EnsureLinksExistAsync(request.RelatedProjectId, null, request.RelatedTaskId, cancellationToken);

            var commitment = new Commitment(
                request.Title,
                request.Description,
                request.CommitmentType,
                request.DueDate,
                request.Importance,
                request.RelatedProjectId,
                request.RelatedTaskId);

            _db.Add(commitment);
            await _audit.WriteAsync(
                nameof(Commitment),
                commitment.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(commitment.Id);
        });

    public Task<Result> UpdateCommitmentAsync(
        Guid id,
        string title,
        string? description,
        CommitmentType commitmentType,
        DateOnly? dueDate,
        ImportanceLevel importance,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var commitment = await _db.FirstOrDefaultAsync(
                _db.Commitments.Where(c => c.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Commitment), id);

            commitment.Update(title, description, commitmentType, dueDate, importance);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> ChangeCommitmentStatusAsync(
        Guid id,
        CommitmentStatus status,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var commitment = await _db.FirstOrDefaultAsync(
                _db.Commitments.Where(c => c.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Commitment), id);

            commitment.ChangeStatus(status);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeleteCommitmentAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var commitment = await _db.FirstOrDefaultAsync(
                _db.Commitments.Where(c => c.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Commitment), id);

            _db.Remove(commitment);
            await _db.SaveChangesAsync(cancellationToken);
        });

    /// <summary>
    /// Agenda for a local date range. Events are grouped by the user's local date, not UTC, so a
    /// late-evening event does not appear on the wrong day.
    /// </summary>
    public async Task<IReadOnlyList<AgendaDay>> GetAgendaAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate)
        {
            return [];
        }

        var fromUtc = _timeZone.ToUtc(fromDate, TimeOnly.MinValue);
        var toUtc = _timeZone.ToUtc(toDate.AddDays(1), TimeOnly.MinValue);

        var events = await ListEventsAsync(fromUtc, toUtc, cancellationToken);
        var commitments = await ListCommitmentsAsync(includeClosed: false, cancellationToken);

        var days = new List<AgendaDay>();
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var dayEvents = events
                .Where(e => _timeZone.ToLocalDate(e.StartAtUtc) == date)
                .ToList();

            var dayCommitments = commitments
                .Where(c => c.DueDate == date)
                .ToList();

            days.Add(new AgendaDay(date, dayEvents, dayCommitments));
        }

        return days;
    }

    private static CalendarEventView ToView(CalendarEvent e)
        => new(
            e.Id,
            e.Title,
            e.Description,
            e.StartAtUtc,
            e.EndAtUtc,
            e.EventType,
            e.Location,
            e.IsAllDay,
            e.RelatedProjectId,
            e.RelatedGoalId);

    private static CommitmentItemView ToView(Commitment c, DateOnly today)
        => new(
            c.Id,
            c.Title,
            c.Description,
            c.CommitmentType,
            c.DueDate,
            c.Status,
            c.Importance,
            c.RelatedProjectId,
            c.RelatedTaskId,
            c.IsOverdue(today));

    private async Task EnsureLinksExistAsync(
        Guid? projectId,
        Guid? goalId,
        Guid? taskId,
        CancellationToken cancellationToken)
    {
        if (projectId.HasValue)
        {
            var exists = await _db.AnyAsync(_db.Projects.Where(p => p.Id == projectId.Value), cancellationToken);
            if (!exists)
            {
                throw new EntityNotFoundException("Project", projectId.Value);
            }
        }

        if (goalId.HasValue)
        {
            var exists = await _db.AnyAsync(_db.Goals.Where(g => g.Id == goalId.Value), cancellationToken);
            if (!exists)
            {
                throw new EntityNotFoundException("Goal", goalId.Value);
            }
        }

        if (taskId.HasValue)
        {
            var exists = await _db.AnyAsync(_db.Tasks.Where(t => t.Id == taskId.Value), cancellationToken);
            if (!exists)
            {
                throw new EntityNotFoundException("TaskItem", taskId.Value);
            }
        }
    }
}
