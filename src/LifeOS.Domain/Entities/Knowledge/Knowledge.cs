using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.Knowledge;

public sealed class Note : AuditableEntity
{
    private readonly List<NoteTag> _tags = [];

    private Note() { }

    public Note(string title, string content, NoteType noteType)
    {
        Rename(title);
        Content = content;
        NoteType = noteType;
    }

    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public NoteType NoteType { get; private set; }
    public bool IsPinned { get; private set; }

    public IReadOnlyCollection<NoteTag> Tags => _tags;

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleException("Note.TitleRequired", "عنوان یادداشت الزامی است.");
        }

        Title = title.Trim();
    }

    public void Update(string title, string content, NoteType noteType)
    {
        Rename(title);
        Content = content;
        NoteType = noteType;
    }

    public void SetPinned(bool isPinned) => IsPinned = isPinned;

    public void AttachTag(Guid tagId)
    {
        if (_tags.Any(t => t.TagId == tagId))
        {
            return;
        }

        _tags.Add(new NoteTag(Id, tagId));
    }

    public void DetachTag(Guid tagId)
    {
        var link = _tags.FirstOrDefault(t => t.TagId == tagId);
        if (link is not null)
        {
            _tags.Remove(link);
        }
    }
}

public sealed class JournalEntry : AuditableEntity
{
    private JournalEntry() { }

    public JournalEntry(DateOnly entryDate, string title, string content)
    {
        EntryDate = entryDate;
        Title = title;
        Content = content;
    }

    public DateOnly EntryDate { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public int? Mood { get; private set; }
    public int? EnergyLevel { get; private set; }
    public int? StressLevel { get; private set; }

    public void Update(DateOnly entryDate, string title, string content)
    {
        EntryDate = entryDate;
        Title = title;
        Content = content;
    }

    /// <summary>All subjective measurements are optional and validated to a 1..10 scale.</summary>
    public void SetWellbeing(int? mood, int? energyLevel, int? stressLevel)
    {
        Mood = ValidateScale(mood, nameof(mood));
        EnergyLevel = ValidateScale(energyLevel, nameof(energyLevel));
        StressLevel = ValidateScale(stressLevel, nameof(stressLevel));
    }

    private static int? ValidateScale(int? value, string field)
    {
        if (value is null)
        {
            return null;
        }

        if (value is < 1 or > 10)
        {
            throw new DomainRuleException("Journal.ScaleOutOfRange", $"مقدار {field} باید بین ۱ تا ۱۰ باشد.");
        }

        return value;
    }
}

public sealed class Tag : Entity
{
    private Tag() { }

    public Tag(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("Tag.NameRequired", "نام برچسب الزامی است.");
        }

        Name = name.Trim();
        Color = color;
        CreatedAtUtc = DateTimeOffset.MinValue;
    }

    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "#8c1f2b";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void StampCreated(DateTimeOffset now) => CreatedAtUtc = now;

    public void Rename(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("Tag.NameRequired", "نام برچسب الزامی است.");
        }

        Name = name.Trim();
        Color = color;
    }
}

public sealed class NoteTag : Entity
{
    private NoteTag() { }

    internal NoteTag(Guid noteId, Guid tagId)
    {
        NoteId = noteId;
        TagId = tagId;
    }

    public Guid NoteId { get; private set; }
    public Guid TagId { get; private set; }

    public Note? Note { get; private set; }
    public Tag? Tag { get; private set; }
}

public sealed class KnowledgeLink : Entity
{
    private KnowledgeLink() { }

    public KnowledgeLink(
        KnowledgeEntityType sourceEntityType,
        Guid sourceEntityId,
        KnowledgeEntityType targetEntityType,
        Guid targetEntityId,
        string relationshipType,
        DateTimeOffset createdAtUtc)
    {
        if (sourceEntityType == targetEntityType && sourceEntityId == targetEntityId)
        {
            throw new DomainRuleException("KnowledgeLink.SelfReference", "یک آیتم نمی‌تواند به خودش پیوند داشته باشد.");
        }

        SourceEntityType = sourceEntityType;
        SourceEntityId = sourceEntityId;
        TargetEntityType = targetEntityType;
        TargetEntityId = targetEntityId;
        RelationshipType = relationshipType;
        CreatedAtUtc = createdAtUtc;
    }

    public KnowledgeEntityType SourceEntityType { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public KnowledgeEntityType TargetEntityType { get; private set; }
    public Guid TargetEntityId { get; private set; }
    public string RelationshipType { get; private set; } = "related";
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
