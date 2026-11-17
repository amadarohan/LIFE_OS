using LifeOS.Application.Abstractions;
using LifeOS.Application.Common;
using LifeOS.Domain.Common;
using LifeOS.Domain.Entities.Knowledge;
using LifeOS.Domain.Enums;

namespace LifeOS.Application.UseCases.Knowledge;

public sealed record TagView(Guid Id, string Name, string Color);

public sealed record NoteSummary(
    Guid Id,
    string Title,
    NoteType NoteType,
    bool IsPinned,
    string Excerpt,
    IReadOnlyList<TagView> Tags,
    DateTimeOffset UpdatedAtUtc);

public sealed record NoteDetail(
    Guid Id,
    string Title,
    string Content,
    NoteType NoteType,
    bool IsPinned,
    IReadOnlyList<TagView> Tags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record JournalEntryView(
    Guid Id,
    DateOnly EntryDate,
    string Title,
    string Content,
    int? Mood,
    int? EnergyLevel,
    int? StressLevel,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateNoteRequest(string Title, string Content, NoteType NoteType, IReadOnlyList<Guid> TagIds);

public sealed record CreateJournalEntryRequest(
    DateOnly EntryDate,
    string Title,
    string Content,
    int? Mood,
    int? EnergyLevel,
    int? StressLevel);

public sealed record NoteFilter(
    string? SearchText = null,
    NoteType? NoteType = null,
    Guid? TagId = null,
    bool PinnedOnly = false);

/// <summary>
/// Notes, journal and the knowledge graph. Search is done in memory over the loaded page because
/// SQL Server collation cannot be relied on for Persian text matching.
/// </summary>
public sealed class KnowledgeService
{
    private const int ExcerptLength = 180;

    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public KnowledgeService(IApplicationDbContext db, IAuditWriter audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NoteSummary>> ListNotesAsync(
        NoteFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Notes.AsQueryable();

        if (filter.NoteType.HasValue)
        {
            query = query.Where(n => n.NoteType == filter.NoteType.Value);
        }

        if (filter.PinnedOnly)
        {
            query = query.Where(n => n.IsPinned);
        }

        if (filter.TagId.HasValue)
        {
            var noteIds = await _db.ToListAsync(
                _db.NoteTags.Where(nt => nt.TagId == filter.TagId.Value).Select(nt => nt.NoteId),
                cancellationToken);

            query = query.Where(n => noteIds.Contains(n.Id));
        }

        var notes = await _db.ToListAsync(
            query.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAtUtc),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            notes = notes
                .Where(n => n.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || n.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var tagsByNote = await LoadTagsByNoteAsync(notes.Select(n => n.Id).ToList(), cancellationToken);

        return notes
            .Select(n => new NoteSummary(
                n.Id,
                n.Title,
                n.NoteType,
                n.IsPinned,
                BuildExcerpt(n.Content),
                tagsByNote.GetValueOrDefault(n.Id) ?? [],
                n.UpdatedAtUtc))
            .ToList();
    }

    public async Task<Result<NoteDetail>> GetNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var note = await _db.FindNoteAsync(id, cancellationToken);
        if (note is null)
        {
            return Guard.NotFound<NoteDetail>("یادداشت", id);
        }

        var tagIds = note.Tags.Select(t => t.TagId).ToList();
        var tags = tagIds.Count == 0
            ? []
            : await _db.ToListAsync(
                _db.Tags.Where(t => tagIds.Contains(t.Id)),
                cancellationToken);

        return Result.Success(new NoteDetail(
            note.Id,
            note.Title,
            note.Content,
            note.NoteType,
            note.IsPinned,
            tags.Select(t => new TagView(t.Id, t.Name, t.Color)).ToList(),
            note.CreatedAtUtc,
            note.UpdatedAtUtc));
    }

    public Task<Result<Guid>> CreateNoteAsync(
        CreateNoteRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var note = new Note(request.Title, request.Content, request.NoteType);

            foreach (var tagId in request.TagIds.Distinct())
            {
                var exists = await _db.AnyAsync(_db.Tags.Where(t => t.Id == tagId), cancellationToken);
                if (!exists)
                {
                    throw new EntityNotFoundException(nameof(Tag), tagId);
                }

                note.AttachTag(tagId);
            }

            _db.Add(note);
            await _audit.WriteAsync(
                nameof(Note),
                note.Id,
                AuditActionType.Created,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(note.Id);
        });

    public Task<Result> UpdateNoteAsync(
        Guid id,
        string title,
        string content,
        NoteType noteType,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var note = await _db.FindNoteAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Note), id);

            note.Update(title, content, noteType);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> SetNotePinnedAsync(
        Guid id,
        bool isPinned,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var note = await _db.FindNoteAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Note), id);

            note.SetPinned(isPinned);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> AttachTagAsync(
        Guid noteId,
        Guid tagId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var note = await _db.FindNoteAsync(noteId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Note), noteId);

            var tagExists = await _db.AnyAsync(_db.Tags.Where(t => t.Id == tagId), cancellationToken);
            if (!tagExists)
            {
                throw new EntityNotFoundException(nameof(Tag), tagId);
            }

            note.AttachTag(tagId);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DetachTagAsync(
        Guid noteId,
        Guid tagId,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var note = await _db.FindNoteAsync(noteId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Note), noteId);

            note.DetachTag(tagId);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeleteNoteAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var note = await _db.FindNoteAsync(id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Note), id);

            _db.Remove(note);
            await _audit.WriteAsync(
                nameof(Note),
                id,
                AuditActionType.Deleted,
                null,
                null,
                AuditSource.User,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<TagView>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        var tags = await _db.ToListAsync(_db.Tags.OrderBy(t => t.Name), cancellationToken);
        return tags.Select(t => new TagView(t.Id, t.Name, t.Color)).ToList();
    }

    public Task<Result<Guid>> CreateTagAsync(
        string name,
        string color,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var duplicate = await _db.AnyAsync(
                _db.Tags.Where(t => t.Name == name.Trim()),
                cancellationToken);

            if (duplicate)
            {
                throw new DomainRuleException("Tag.DuplicateName", "برچسبی با این نام قبلاً ثبت شده است.");
            }

            var tag = new Tag(name, color);
            tag.StampCreated(_clock.UtcNow);

            _db.Add(tag);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(tag.Id);
        });

    public Task<Result> RenameTagAsync(
        Guid id,
        string name,
        string color,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var tag = await _db.FirstOrDefaultAsync(_db.Tags.Where(t => t.Id == id), cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Tag), id);

            tag.Rename(name, color);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<JournalEntryView>> ListJournalAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.JournalEntries.AsQueryable();

        if (from.HasValue)
        {
            query = query.Where(j => j.EntryDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(j => j.EntryDate <= to.Value);
        }

        var entries = await _db.ToListAsync(
            query.OrderByDescending(j => j.EntryDate),
            cancellationToken);

        return entries
            .Select(j => new JournalEntryView(
                j.Id,
                j.EntryDate,
                j.Title,
                j.Content,
                j.Mood,
                j.EnergyLevel,
                j.StressLevel,
                j.CreatedAtUtc))
            .ToList();
    }

    public Task<Result<Guid>> CreateJournalEntryAsync(
        CreateJournalEntryRequest request,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var duplicate = await _db.AnyAsync(
                _db.JournalEntries.Where(j => j.EntryDate == request.EntryDate),
                cancellationToken);

            if (duplicate)
            {
                throw new DomainRuleException(
                    "Journal.DuplicateDate",
                    "برای این تاریخ قبلاً یادداشت روزانه ثبت شده است.");
            }

            var entry = new JournalEntry(request.EntryDate, request.Title, request.Content);
            entry.SetWellbeing(request.Mood, request.EnergyLevel, request.StressLevel);

            _db.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(entry.Id);
        });

    public Task<Result> UpdateJournalEntryAsync(
        Guid id,
        DateOnly entryDate,
        string title,
        string content,
        int? mood,
        int? energyLevel,
        int? stressLevel,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var entry = await _db.FirstOrDefaultAsync(
                _db.JournalEntries.Where(j => j.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(JournalEntry), id);

            entry.Update(entryDate, title, content);
            entry.SetWellbeing(mood, energyLevel, stressLevel);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result> DeleteJournalEntryAsync(Guid id, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var entry = await _db.FirstOrDefaultAsync(
                _db.JournalEntries.Where(j => j.Id == id),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(JournalEntry), id);

            _db.Remove(entry);
            await _db.SaveChangesAsync(cancellationToken);
        });

    public Task<Result<Guid>> LinkAsync(
        KnowledgeEntityType sourceType,
        Guid sourceId,
        KnowledgeEntityType targetType,
        Guid targetId,
        string relationshipType,
        CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var duplicate = await _db.AnyAsync(
                _db.KnowledgeLinks.Where(l =>
                    l.SourceEntityType == sourceType &&
                    l.SourceEntityId == sourceId &&
                    l.TargetEntityType == targetType &&
                    l.TargetEntityId == targetId),
                cancellationToken);

            if (duplicate)
            {
                throw new DomainRuleException("KnowledgeLink.Duplicate", "این پیوند قبلاً ثبت شده است.");
            }

            var link = new KnowledgeLink(
                sourceType,
                sourceId,
                targetType,
                targetId,
                relationshipType,
                _clock.UtcNow);

            _db.Add(link);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(link.Id);
        });

    public Task<Result> UnlinkAsync(Guid linkId, CancellationToken cancellationToken = default)
        => Guard.RunAsync(async () =>
        {
            var link = await _db.FirstOrDefaultAsync(
                _db.KnowledgeLinks.Where(l => l.Id == linkId),
                cancellationToken)
                ?? throw new EntityNotFoundException(nameof(KnowledgeLink), linkId);

            _db.Remove(link);
            await _db.SaveChangesAsync(cancellationToken);
        });

    private async Task<Dictionary<Guid, IReadOnlyList<TagView>>> LoadTagsByNoteAsync(
        IReadOnlyList<Guid> noteIds,
        CancellationToken cancellationToken)
    {
        if (noteIds.Count == 0)
        {
            return [];
        }

        var links = await _db.ToListAsync(
            _db.NoteTags.Where(nt => noteIds.Contains(nt.NoteId)),
            cancellationToken);

        if (links.Count == 0)
        {
            return [];
        }

        var tagIds = links.Select(l => l.TagId).Distinct().ToList();
        var tags = await _db.ToListAsync(
            _db.Tags.Where(t => tagIds.Contains(t.Id)),
            cancellationToken);

        var tagLookup = tags.ToDictionary(t => t.Id, t => new TagView(t.Id, t.Name, t.Color));

        return links
            .GroupBy(l => l.NoteId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TagView>)g
                    .Select(l => tagLookup.GetValueOrDefault(l.TagId))
                    .Where(t => t is not null)
                    .Select(t => t!)
                    .ToList());
    }

    private static string BuildExcerpt(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= ExcerptLength
            ? normalized
            : normalized[..ExcerptLength] + "…";
    }
}
