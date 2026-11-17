using LifeOS.Application.Abstractions.Ai;
using LifeOS.Application.Ai;
using LifeOS.Application.Ai.Tools;
using LifeOS.Application.Engines;
using LifeOS.Application.Formatting;
using LifeOS.Application.UseCases.Analytics;
using LifeOS.Application.UseCases.Backup;
using LifeOS.Application.UseCases.Calendar;
using LifeOS.Application.UseCases.Decisions;
using LifeOS.Application.UseCases.Finance;
using LifeOS.Application.UseCases.Goals;
using LifeOS.Application.UseCases.Knowledge;
using LifeOS.Application.UseCases.LifeDomains;
using LifeOS.Application.UseCases.Planning;
using LifeOS.Application.UseCases.Projects;
using LifeOS.Application.UseCases.Settings;
using LifeOS.Application.UseCases.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application use cases and the pure engines they depend on. Engines are
    /// stateless, so they are singletons; use cases are scoped because they hold a DbContext.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<PriorityEngine>();
        services.AddSingleton<CapacityEngine>();
        services.AddSingleton<LifeDebtEngine>();
        services.AddSingleton<StagnationEngine>();
        services.AddSingleton<RealityCheckEngine>();
        services.AddSingleton<OpportunityCostEngine>();

        services.AddScoped<LifeDomainService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<GoalService>();
        services.AddScoped<TaskService>();
        services.AddScoped<PlanningService>();
        services.AddScoped<FinanceService>();
        services.AddScoped<KnowledgeService>();
        services.AddScoped<DecisionService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<BackupService>();

        // AI use cases. The context builder reads through IApplicationDbContext, so it is scoped
        // with the rest of the use cases rather than being a singleton.
        services.AddScoped<IAiContextBuilder, AiContextBuilder>();
        services.AddScoped<AiChatService>();
        services.AddScoped<AiActionService>();
        services.AddScoped<AiMemoryService>();
        services.AddScoped<AiInsightService>();
        services.AddScoped<AiToolInvoker>();

        // Tools are scoped because they query the database. They are resolved by name through the
        // registry below, never constructed ad hoc.
        services.AddScoped<IAiTool, GetOpenTasksTool>();
        services.AddScoped<IAiTool, GetProjectsTool>();
        services.AddScoped<IAiTool, GetGoalsTool>();
        services.AddScoped<IAiTool, GetCapacityTool>();
        services.AddScoped<IAiTool, GetLifeDebtTool>();
        services.AddScoped<IAiTool, ProposeCreateTaskTool>();
        services.AddScoped<AiToolRegistry>();

        services.AddSingleton<IDateTimeFormatter, PersianDateTimeFormatter>();
        services.AddSingleton<IMoneyFormatter, PersianMoneyFormatter>();
        services.AddSingleton<INumberFormatter, PersianNumberFormatter>();

        return services;
    }
}
