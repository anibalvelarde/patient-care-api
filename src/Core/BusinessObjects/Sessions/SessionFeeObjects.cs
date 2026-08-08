using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

/// <summary>
/// WP-49 (BR3/BR4): wire shapes for the late-fee batch and the fee waiver.
/// Contract: <c>patient-care-super/_contracts/sessions-api.md</c> § "WP-49 fee policy".
/// </summary>

/// <summary>One session the late-fee batch would charge, as shown in the manager's preview.</summary>
public class LateFeePreviewItem
{
    public int SessionId { get; set; }
    public DateOnly SessionDate { get; set; }

    /// <summary>Whole days between the session date and <c>asOf</c>, in the clinic's calendar.</summary>
    public int DaysUnpaid { get; set; }

    public string PatientName { get; set; } = string.Empty;
    public int PatientId { get; set; }

    /// <summary>Who actually owes it — null when the patient has no caretaker linked.</summary>
    public string? CaretakerName { get; set; }

    /// <summary>The BR3 base: Amount − Discount − AmountPaid + on-site charge.</summary>
    public decimal UnpaidBalance { get; set; }

    /// <summary>30% of <see cref="UnpaidBalance"/>, rounded half away from zero.</summary>
    public decimal ProposedFee { get; set; }
}

/// <summary>The preview a manager reads before firing the batch.</summary>
public class LateFeePreviewResult
{
    /// <summary>The date the eligibility clock was evaluated against (clinic-local).</summary>
    public DateOnly AsOf { get; set; }

    public decimal RatePct { get; set; }
    public int GraceDays { get; set; }

    public IReadOnlyList<LateFeePreviewItem> Items { get; set; } = [];
    public int SessionCount { get; set; }
    public decimal TotalUnpaidBalance { get; set; }
    public decimal TotalProposedFee { get; set; }
}

/// <summary>
/// Fire the batch. <see cref="SessionIds"/> is required and explicit — the manager charges the
/// rows they reviewed, never "whatever is eligible right now", so a session that becomes
/// eligible between preview and apply is not swept in silently.
/// </summary>
public class ApplyLateFeesRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "Select at least one session to charge.")]
    public List<int> SessionIds { get; set; } = [];

    /// <summary>Evaluation date; defaults to the clinic's today when omitted.</summary>
    public DateOnly? AsOf { get; set; }
}

/// <summary>One session the batch actually charged.</summary>
public class LateFeeAppliedItem
{
    public int SessionId { get; set; }
    public decimal FeeApplied { get; set; }
    public decimal UnpaidBalanceBefore { get; set; }
    public decimal AmountDueAfter { get; set; }
}

/// <summary>
/// One session the batch declined to charge, WITH the reason. Skips are reported rather than
/// silently dropped: a manager who selected 12 sessions and got 9 charged needs to know why,
/// and "the balance was settled between preview and apply" is a normal, benign answer.
/// </summary>
public class LateFeeSkippedItem
{
    public int SessionId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ApplyLateFeesResult
{
    public DateOnly AsOf { get; set; }
    public IReadOnlyList<LateFeeAppliedItem> Applied { get; set; } = [];
    public IReadOnlyList<LateFeeSkippedItem> Skipped { get; set; } = [];
    public int AppliedCount { get; set; }
    public int SkippedCount { get; set; }
    public decimal TotalFeeApplied { get; set; }
}

/// <summary>
/// Which fee a waiver targets. Required with no default: a session can carry BOTH a no-show
/// fee and a late fee, so "waive the fee" is ambiguous and defaulting either way would
/// occasionally forgive money the manager did not mean to forgive.
///
/// ⚠️ This type is INTERNAL to the service layer and must not appear on a request or response
/// DTO. See the note on <see cref="WaiveFeeRequest.FeeKind"/> — it shipped as a wire type in
/// WP-49 and broke every waive call.
/// </summary>
public enum SessionFeeKind
{
    Late = 1,
    NoShow = 2,
    Both = 3,
}

public class WaiveFeeRequest
{
    /// <summary>
    /// <c>"Late"</c> | <c>"NoShow"</c> | <c>"Both"</c>, case-insensitive.
    ///
    /// A STRING on the wire, not the <see cref="SessionFeeKind"/> enum, for two reasons. First,
    /// it is the house convention — every other enum-ish DTO field in this API is a string
    /// (<c>ConfirmationRequest.ConfirmationMethod</c>, <c>ConfirmationResult</c>, patient
    /// <c>Gender</c>). Second, it is what actually works: the API registers no
    /// <c>JsonStringEnumConverter</c>, so System.Text.Json binds enums from INTEGERS only, and
    /// a bare enum property here rejected <c>"feeKind":"Late"</c> with a model-validation 400
    /// that named the JSON path but explained nothing. That is exactly how WP-49 shipped, and
    /// it broke every waive attempt from the UI — which sends the string.
    ///
    /// If a future change registers a global enum converter, this can become an enum again;
    /// until then, do not "tidy" it back.
    /// </summary>
    [Required(ErrorMessage = "Specify which fee to waive: Late, NoShow, or Both.")]
    public string? FeeKind { get; set; }

    /// <summary>
    /// Mandatory, and sanitized server-side before it reaches the Notes marker. Capped at 200
    /// characters to match the UI's maxlength.
    /// </summary>
    [Required(ErrorMessage = "A reason is required to waive a fee.")]
    [MaxLength(200)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Parses <see cref="FeeKind"/> to the internal enum. Returns false for null, blank, or
    /// anything unrecognised, so the caller can answer with a message that names the legal
    /// values instead of leaking a serializer diagnostic.
    ///
    /// Matches against the enum NAMES rather than calling <c>Enum.TryParse</c> directly:
    /// TryParse also accepts numeric strings, so <c>"1"</c> would quietly bind to
    /// <see cref="SessionFeeKind.Late"/> — reviving, as an undocumented second encoding, the
    /// very integer form this property exists to get away from.
    /// </summary>
    public bool TryParseFeeKind(out SessionFeeKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(FeeKind)) return false;

        var supplied = FeeKind.Trim();
        foreach (var name in Enum.GetNames<SessionFeeKind>())
        {
            if (string.Equals(name, supplied, StringComparison.OrdinalIgnoreCase))
            {
                kind = Enum.Parse<SessionFeeKind>(name);
                return true;
            }
        }
        return false;
    }
}

public class WaiveFeeResult
{
    public int SessionId { get; set; }

    /// <summary>Echoed as a string for the same reason the request takes one.</summary>
    public string FeeKind { get; set; } = string.Empty;

    /// <summary>Late fee removed by this waive (0 when the late leg was not waived).</summary>
    public decimal LateFeeWaived { get; set; }

    /// <summary>No-show fee removed by this waive (0 when the no-show leg was not waived).</summary>
    public decimal NoShowFeeWaived { get; set; }

    public decimal AmountDueAfter { get; set; }
    public decimal GrossProfitAfter { get; set; }
    public DateOnly WaivedOn { get; set; }
    public int WaivedByUserId { get; set; }
}
