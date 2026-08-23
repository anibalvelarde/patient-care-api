using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class ConfirmationRequest
{
    public string ConfirmationMethod { get; set; } = ConfirmationValues.MethodPhone;

    // WP-55 B-2e: must be one of the four results, else 400. Before this an unrecognized value
    // fell through the BookingService result switch and/or truncated into the MySQL enum → 500.
    [AllowedValues(ConfirmationValues.Confirmed, ConfirmationValues.NoAnswer,
        ConfirmationValues.LeftMessage, ConfirmationValues.Declined)]
    public string ConfirmationResult { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
