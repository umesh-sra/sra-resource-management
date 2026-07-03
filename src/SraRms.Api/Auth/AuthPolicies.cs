namespace SraRms.Api.Auth;

/// <summary>
/// Application roles (mapped from AD/Entra groups, FR-AUTH-2) and the policies
/// that gate endpoints. A user's effective permissions are the union of roles.
/// </summary>
public static class Roles
{
    public const string Administrator = "Administrator";
    public const string General = "General";
    public const string Report = "Report";
}

public static class Policies
{
    /// <summary>Data-changing operations — Administrator only.</summary>
    public const string Admin = "Admin";

    /// <summary>Read access to core data and dashboard — Administrator or General.</summary>
    public const string Read = "Read";

    /// <summary>Reporting endpoints — Report role.</summary>
    public const string Report = "Report";
}
