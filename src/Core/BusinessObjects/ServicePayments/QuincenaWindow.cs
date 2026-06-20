namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// The suggested "quincena" pay window for a given date: 1st–15th when the day is
/// &lt;= 15, otherwise 16th–end-of-month. A UX default for the date-range picker only —
/// the system imposes no quincena constraint and accepts any range.
/// </summary>
public class QuincenaWindow
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}
