namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// Dry-run result of a duplicate-patient merge (WP-22): identity summaries of both records,
/// exactly what would move/be deleted, which blank survivor fields would be filled, plus
/// warnings (informational) and blockers (execute would 409). Computed by the same plan step
/// the execute path runs, so preview and result counts always agree.
/// </summary>
public class PatientMergePreview
{
    public PatientMergeIdentity Survivor { get; set; } = new();
    public PatientMergeIdentity Eliminated { get; set; } = new();
    public PatientMergePlannedCounts Counts { get; set; } = new();
    public IReadOnlyList<PatientMergeCaretakerDisposition> Caretakers { get; set; } = [];
    public IReadOnlyList<PatientMergeFieldFill> FieldFills { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public IReadOnlyList<string> Blockers { get; set; } = [];
}

/// <summary>Identity card for one side of the merge, as shown in the confirmation UI.</summary>
public class PatientMergeIdentity
{
    public int PatientId { get; set; }
    public int UserId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? MedicalRecordNumber { get; set; }
    public string? Cedula { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public bool IsActive { get; set; }
    public int SessionCount { get; set; }
    public int PlanCount { get; set; }
    public int CaretakerCount { get; set; }
}

public class PatientMergePlannedCounts
{
    public int SessionsToRemap { get; set; }
    public int PlansToRemap { get; set; }
    public int CaretakerLinksToRemap { get; set; }
    public int CaretakerLinksToDedupe { get; set; }
    public int SyntheticCaretakersToDelete { get; set; }
}

/// <summary>What the merge does with one of the eliminated record's caretaker links.</summary>
public class PatientMergeCaretakerDisposition
{
    public const string Remap = "remap";
    public const string DedupeDelete = "dedupe-delete";
    public const string RetireSynthetic = "retire-synthetic";

    public int CaretakerId { get; set; }
    public string CaretakerName { get; set; } = string.Empty;
    public bool IsSynthetic { get; set; }
    public string Disposition { get; set; } = Remap;
    public bool PrimaryFlagDropped { get; set; }
}

/// <summary>A blank survivor field that inherits the eliminated record's value.</summary>
public class PatientMergeFieldFill
{
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
