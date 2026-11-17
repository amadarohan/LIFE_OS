namespace LifeOS.Domain.Enums;

public enum ProjectStatus
{
    Draft = 0,
    Planned = 1,
    Active = 2,
    OnHold = 3,
    Completed = 4,
    Cancelled = 5,
    Archived = 6
}

public enum GoalType
{
    ShortTerm = 0,
    MidTerm = 1,
    LongTerm = 2,
    Habit = 3,
    Outcome = 4
}

public enum GoalStatus
{
    Draft = 0,
    Active = 1,
    OnHold = 2,
    Completed = 3,
    Abandoned = 4
}

public enum LifeTaskStatus
{
    Inbox = 0,
    Todo = 1,
    InProgress = 2,
    Waiting = 3,
    Completed = 4,
    Cancelled = 5,
    Deferred = 6
}

public enum PriorityLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum MilestoneStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Missed = 3,
    Cancelled = 4
}

public enum DependencyType
{
    FinishToStart = 0,
    StartToStart = 1,
    FinishToFinish = 2
}

public enum MetricType
{
    Number = 0,
    Currency = 1,
    Percentage = 2,
    Duration = 3,
    Boolean = 4
}

public enum MeasurementFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
    AdHoc = 5
}

public enum PlanStatus
{
    Draft = 0,
    Active = 1,
    Completed = 2,
    Reviewed = 3
}

public enum TimeBlockType
{
    Focus = 0,
    Meeting = 1,
    Personal = 2,
    Break = 3,
    Buffer = 4,
    Admin = 5
}

public enum TimeBlockStatus
{
    Planned = 0,
    InProgress = 1,
    Done = 2,
    Skipped = 3
}

public enum AccountType
{
    Cash = 0,
    Bank = 1,
    Savings = 2,
    Credit = 3,
    Investment = 4,
    Other = 5
}

public enum TransactionType
{
    Income = 0,
    Expense = 1,
    Transfer = 2,
    Adjustment = 3
}

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4
}

public enum CommitmentStatus
{
    Active = 0,
    Paid = 1,
    Overdue = 2,
    Cancelled = 3,
    Completed = 4
}

public enum BudgetPeriodType
{
    Weekly = 0,
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3,
    Custom = 4
}

public enum NoteType
{
    General = 0,
    Idea = 1,
    Reference = 2,
    Meeting = 3,
    Learning = 4
}

public enum DecisionStatus
{
    Draft = 0,
    Evaluating = 1,
    Decided = 2,
    Reviewed = 3,
    Cancelled = 4
}

public enum ImportanceLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum CalendarEventType
{
    Appointment = 0,
    Meeting = 1,
    Personal = 2,
    Deadline = 3,
    Reminder = 4,
    Other = 5
}

public enum CommitmentType
{
    Appointment = 0,
    Responsibility = 1,
    ImportantDate = 2,
    FinancialDueDate = 3,
    Work = 4,
    Household = 5,
    Other = 6
}

public enum SnapshotEntityType
{
    Project = 0,
    Goal = 1,
    LifeDomain = 2
}

public enum AiMessageRole
{
    System = 0,
    User = 1,
    Assistant = 2,
    Tool = 3
}

public enum AiMemoryType
{
    Profile = 0,
    Episodic = 1,
    Operational = 2
}

public enum AiInsightType
{
    Overload = 0,
    Stagnation = 1,
    PriorityConflict = 2,
    Risk = 3,
    Opportunity = 4,
    LifeDebt = 5,
    RealityCheck = 6,
    General = 7
}

public enum AiInsightStatus
{
    New = 0,
    Acknowledged = 1,
    Dismissed = 2,
    Resolved = 3,
    Expired = 4
}

public enum AiActionStatus
{
    Proposed = 0,
    Approved = 1,
    Rejected = 2,
    Executed = 3,
    Failed = 4,
    Expired = 5,
    Cancelled = 6
}

public enum AiActionType
{
    CreateProject = 0,
    UpdateProject = 1,
    CreateTask = 2,
    UpdateTask = 3,
    CreateGoal = 4,
    UpdateGoal = 5,
    CreateDecision = 6,
    SaveWeeklyPlan = 7,
    RecordFinancialTransaction = 8,
    ReorganizeTasks = 9,
    AdjustBudget = 10
}

public enum AiExecutionStatus
{
    Succeeded = 0,
    Failed = 1,
    Rejected = 2
}

public enum AiProviderKind
{
    Local = 0,
    Cloud = 1,
    Fake = 2
}

public enum SensitivityLevel
{
    Public = 0,
    Personal = 1,
    Sensitive = 2
}

public enum AiTaskCategory
{
    SimpleClassification = 0,
    Extraction = 1,
    Summarization = 2,
    SensitiveAnalysis = 3,
    StrategicAnalysis = 4,
    ScenarioEvaluation = 5
}

public enum AiToolCategory
{
    Read = 0,
    Analysis = 1,
    Proposal = 2,
    Execution = 3
}

public enum AuditActionType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    Restored = 3,
    Approved = 4,
    Executed = 5
}

public enum AuditSource
{
    User = 0,
    Ai = 1,
    System = 2,
    BackgroundJob = 3
}

public enum BackupType
{
    Manual = 0,
    Scheduled = 1,
    PreRestore = 2
}

public enum BackupStatus
{
    InProgress = 0,
    Completed = 1,
    Failed = 2
}

public enum KnowledgeEntityType
{
    Note = 0,
    JournalEntry = 1,
    Project = 2,
    Goal = 3,
    Decision = 4,
    Task = 5,
    LifeDomain = 6
}

public enum TaskSource
{
    Manual = 0,
    QuickCapture = 1,
    Ai = 2,
    Import = 3,
    Recurrence = 4
}
