namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

/// <summary>
/// WP-40 (BK-3): server-derived money to apply on a session update. Present only when the
/// edit changed Amount or Discount — otherwise stored ProviderAmount/GrossProfit stay
/// byte-identical (legacy sessions are never silently recomputed by unrelated edits).
/// </summary>
public sealed record SessionMoneyPatch(decimal ProviderAmount, decimal GrossProfit);
