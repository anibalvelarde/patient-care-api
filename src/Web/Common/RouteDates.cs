namespace Neurocorp.Api.Web.Common;

/// <summary>
/// Helpers for parsing date values that arrive as route segments. Shared by the controllers that
/// were split out of the former god SessionsController (Sessions + Schedule).
/// </summary>
public static class RouteDates
{
    /// <summary>
    /// Parse a route date string, falling back to today (UTC) when it cannot be parsed.
    /// Preserves the lenient behavior of the original SessionsController.ConvertStringToDateOnly.
    /// </summary>
    public static DateOnly ParseOrToday(string dateString)
        => DateOnly.TryParse(dateString, out var date) ? date : DateOnly.FromDateTime(DateTime.UtcNow);
}
