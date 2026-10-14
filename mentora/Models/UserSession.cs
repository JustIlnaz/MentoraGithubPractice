namespace mentora.Models;

public static class UserSession
{
    public static int IdUser { get; private set; }

    public static int IdRole { get; private set; }

    public static string Login { get; private set; } = "";

    public static string DisplayName { get; private set; } = "";

    public static bool IsAuthorized => IdUser > 0;

    public static void SetUser(User user)
    {
        IdUser = user.IdUser;
        IdRole = user.IdRole;
        Login = user.Login;

        DisplayName = string.IsNullOrWhiteSpace(user.FullName)
            ? user.Login
            : user.FullName;
    }

    public static void Clear()
    {
        IdUser = 0;
        IdRole = 0;
        Login = "";
        DisplayName = "";
    }
}