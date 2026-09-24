namespace Tatkal.Authentication;

/// <summary>Role constants shared by every service so a token minted by
/// User Service means the same thing everywhere it's validated.</summary>
public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string System = "System"; // service-to-service calls
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string SystemOrAdmin = "SystemOrAdmin";
}
