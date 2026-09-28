namespace Urbanova.Domain;

/// <summary>All domain enums. No hard-coded engineering thresholds here — see Classification options (Phase 7).</summary>
public enum ProjectStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

public enum FileValidationStatus
{
    Pending = 0,
    Valid = 1,
    Invalid = 2,
    Unsupported = 3
}

public enum AnalysisStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public enum ScenarioKind
{
    Baseline = 0,
    Alternative = 1
}

public enum ProblemClass
{
    Acceptable = 0,
    Moderate = 1,
    ProblemArea = 2
}

/// <summary>
/// Mandatory provenance on every recommendation (PRD §13).
/// Never present Assumption/Estimated/PendingValidation as Validated.
/// </summary>
public enum EvidenceLevel
{
    Validated = 0,
    Calculated = 1,
    Estimated = 2,
    UserProvided = 3,
    PendingValidation = 4,
    Assumption = 5
}

/// <summary>Missing price must surface as Unavailable — never invent a price (PRD §14).</summary>
public enum CostStatus
{
    Calculated = 0,
    Unavailable = 1,
    PendingValidation = 2
}

public enum ReportFormat
{
    Json = 0,
    Html = 1
}
