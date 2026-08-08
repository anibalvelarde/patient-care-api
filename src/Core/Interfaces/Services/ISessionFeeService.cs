using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// WP-49 (BR3/BR4): the late chargeback and the first-class fee waiver.
/// See <c>SessionFeeService</c> for the rules and why they are shaped this way.
/// </summary>
public interface ISessionFeeService
{
    /// <summary>What the batch WOULD charge as of <paramref name="asOf"/> (defaults to today).</summary>
    Task<LateFeePreviewResult> PreviewLateFeesAsync(DateOnly? asOf);

    /// <summary>Charge the selected sessions, reporting per-session skips with reasons.</summary>
    Task<ApplyLateFeesResult> ApplyLateFeesAsync(ApplyLateFeesRequest request, int actingUserId);

    /// <summary>Forgive a late and/or no-show fee, with a mandatory reason.</summary>
    Task<WaiveFeeResult> WaiveFeeAsync(int sessionId, WaiveFeeRequest request, int actingUserId);
}
