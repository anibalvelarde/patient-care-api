namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

/// <summary>
/// WP-39/WP-40: where a resolved session price came from. Serialized (by WP-40 consumers) as
/// camelCase strings per the contract: "durationPrice" | "defaultAmount" | "none" — a
/// defaultAmount/none source signals "no pricing configured for this specialty/duration".
/// </summary>
public enum AmountSource
{
    None,
    DefaultAmount,
    DurationPrice,
}

/// <summary>
/// WP-39: result of the shared price-resolution rule (WP-40's dependency): the duration row
/// with the latest effectiveFrom ≤ the SESSION date → else SpecialtyType.DefaultAmount → else
/// none. <see cref="Amount"/> is null only when <see cref="Source"/> is <see cref="AmountSource.None"/>.
/// </summary>
public sealed record PriceResolution(decimal? Amount, AmountSource Source)
{
    public static readonly PriceResolution None = new(null, AmountSource.None);
}

/// <summary>WP-40: the contract's camelCase wire form of <see cref="AmountSource"/>.</summary>
public static class AmountSourceExtensions
{
    public static string? ToWireString(this AmountSource source) => source switch
    {
        AmountSource.DurationPrice => "durationPrice",
        AmountSource.DefaultAmount => "defaultAmount",
        _ => null,
    };
}
