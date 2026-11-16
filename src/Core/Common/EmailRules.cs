using System.Net.Mail;

namespace Neurocorp.Api.Core.Common;

/// <summary>
/// WP-62 (E2E F-5): the ONE place email input is cleaned and checked, for create and update of
/// patients, caretakers and therapists. Before this, create trimmed and blank→NULL'd inline
/// (WP-57) while PUT stored whatever it was sent (whitespace, untrimmed) and nothing checked the
/// format. Uniqueness stays the DB's job (<c>uq_systemuser_email</c>, case-insensitive collation).
/// </summary>
public static class EmailRules
{
    public const string InvalidMessage = "Email address is not valid.";

    /// <summary>Blank/whitespace → null (many NULLs are allowed by the unique key, many "" are not); otherwise trimmed.</summary>
    public static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    /// <summary>
    /// True for something that looks like a real address: one '@', no whitespace, a dot in the
    /// domain, and parseable by <see cref="MailAddress"/> as exactly itself (rejects display-name
    /// forms like <c>"Ann &lt;a@b.c&gt;"</c>). Pass a normalized (trimmed) value.
    /// </summary>
    public static bool IsValidFormat(string email)
    {
        if (string.IsNullOrEmpty(email) || email.Any(char.IsWhiteSpace)) return false;
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@')) return false;
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        if (dot <= 0 || dot == domain.Length - 1) return false;
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;
    }

    /// <summary>Create path: normalize, then 400 (ArgumentException) if present and malformed.</summary>
    public static string? NormalizeAndValidate(string? email)
    {
        var normalized = Normalize(email);
        if (normalized is not null && !IsValidFormat(normalized))
        {
            throw new ArgumentException(InvalidMessage);
        }
        return normalized;
    }

    /// <summary>
    /// Update path: 400 only when the request CHANGES the address to a malformed one, so a legacy
    /// malformed address on file never blocks an unrelated edit (e.g. a phone change that echoes it).
    /// <paramref name="requested"/> null = "leave unchanged" → nothing to check.
    /// </summary>
    public static void ValidateChange(string? requested, string? onFile)
    {
        if (requested is null) return;
        var normalized = Normalize(requested);
        if (normalized is null) return; // a clear is always allowed
        if (string.Equals(normalized, onFile?.Trim(), StringComparison.Ordinal)) return;
        if (!IsValidFormat(normalized))
        {
            throw new ArgumentException(InvalidMessage);
        }
    }
}
