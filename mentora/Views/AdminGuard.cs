using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public static class AdminGuard
{
    public static async Task<bool> IsAdminAsync(MentoraContext db)
    {
        if (!UserSession.IsAuthorized || UserSession.IdRole != 3)
            return false;

        int id = UserSession.IdUser;

        return await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.IdUser == id
                           && u.IdRole == 3
                           && u.IdUserStatus == 1);
    }

    public static void ReturnToLogin(Control control)
    {
        if (TopLevel.GetTopLevel(control) is MainWindow window)
            window.ShowLogin();
    }
}