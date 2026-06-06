namespace Neurocorp.Api.Core.BusinessObjects.Auth;

/// <summary>
/// A portable representation of a single authorization claim (type + value).
/// Used to move effective claims between the data layer, the token layer, and the API
/// without coupling Core to any specific token library. Record equality makes set
/// operations (union / except for denials) straightforward.
/// </summary>
public sealed record ClaimDto(string Type, string Value);
