namespace Neurocorp.Api.Core.BusinessObjects.AdminUsers;

/// <summary>
/// WP-41B: update roles and/or active status. Omitted/null field = unchanged.
/// <see cref="OperatorRoleTypeIds"/> REPLACES the operator-role set (identity role links are
/// never touched); an empty list is a hard 400 (an operator account must keep at least one
/// operator role — deactivate instead of stripping).
/// </summary>
public class AdminUserUpdateRequest
{
    public List<int>? OperatorRoleTypeIds { get; set; }
    public bool? IsActive { get; set; }
}
