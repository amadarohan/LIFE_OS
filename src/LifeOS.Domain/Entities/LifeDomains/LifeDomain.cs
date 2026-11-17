using LifeOS.Domain.Common;
using LifeOS.Domain.Enums;

namespace LifeOS.Domain.Entities.LifeDomains;

public sealed class LifeDomain : AuditableEntity, IConcurrencyTracked
{
    private readonly List<LifeAreaMetric> _metrics = [];

    private LifeDomain() { }

    public LifeDomain(string name, string? description, string color, string icon, int sortOrder)
    {
        Rename(name);
        Description = description;
        Color = color;
        Icon = icon;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Color { get; private set; } = "#8c1f2b";
    public string Icon { get; private set; } = "circle";
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<LifeAreaMetric> Metrics => _metrics;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleException("LifeDomain.NameRequired", "نام حوزه زندگی الزامی است.");
        }

        Name = name.Trim();
    }

    public void Update(string name, string? description, string color, string icon, int sortOrder)
    {
        Rename(name);
        Description = description;
        Color = color;
        Icon = icon;
        SortOrder = sortOrder;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public LifeAreaMetric AddMetric(
        string name,
        string? description,
        string unit,
        decimal currentValue,
        decimal targetValue,
        MetricType metricType,
        MeasurementFrequency frequency)
    {
        var metric = new LifeAreaMetric(Id, name, description, unit, currentValue, targetValue, metricType, frequency);
        _metrics.Add(metric);
        return metric;
    }
}

public sealed class LifeAreaMetric : AuditableEntity
{
    private LifeAreaMetric() { }

    internal LifeAreaMetric(
        Guid lifeDomainId,
        string name,
        string? description,
        string unit,
        decimal currentValue,
        decimal targetValue,
        MetricType metricType,
        MeasurementFrequency frequency)
    {
        LifeDomainId = lifeDomainId;
        Name = name;
        Description = description;
        Unit = unit;
        CurrentValue = currentValue;
        TargetValue = targetValue;
        MetricType = metricType;
        MeasurementFrequency = frequency;
    }

    public Guid LifeDomainId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal CurrentValue { get; private set; }
    public decimal TargetValue { get; private set; }
    public MetricType MetricType { get; private set; }
    public MeasurementFrequency MeasurementFrequency { get; private set; }
    public DateTimeOffset? LastMeasuredAtUtc { get; private set; }

    public LifeDomain? LifeDomain { get; private set; }

    /// <summary>
    /// Progress toward the target. Returns null when the target is not meaningful
    /// (zero target, or a metric that is not directional).
    /// </summary>
    public decimal? ProgressPercentage
    {
        get
        {
            if (TargetValue == 0m)
            {
                return null;
            }

            var ratio = CurrentValue / TargetValue * 100m;
            return Math.Clamp(Math.Round(ratio, 2), 0m, 100m);
        }
    }

    public void Record(decimal value, DateTimeOffset measuredAtUtc)
    {
        CurrentValue = value;
        LastMeasuredAtUtc = measuredAtUtc;
    }

    public void UpdateDefinition(string name, string? description, string unit, decimal targetValue,
        MetricType metricType, MeasurementFrequency frequency)
    {
        Name = name;
        Description = description;
        Unit = unit;
        TargetValue = targetValue;
        MetricType = metricType;
        MeasurementFrequency = frequency;
    }
}
