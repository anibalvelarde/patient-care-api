namespace Neurocorp.Api.Core.Configurations;

/// <summary>
/// WP-54 D9: kill-switch for the change-log capture interceptor. Bound from the "ChangeLog"
/// config section (env <c>ChangeLog__Enabled</c>), default <c>true</c>. If capture ever
/// misbehaves in production it can be turned off without a redeploy; the read endpoints keep
/// serving whatever is already logged.
/// </summary>
public class ChangeLogOptions
{
    public const string SectionName = "ChangeLog";

    public bool Enabled { get; set; } = true;
}
