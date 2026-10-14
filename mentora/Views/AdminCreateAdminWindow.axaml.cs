using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using mentora.Models;

namespace mentora.Views;

public partial class AdminCreateAdminWindow : Window
{
    private bool _isBusy;

    public AdminCreateAdminWindow()
    {
        InitializeComponent();
    }

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        LoginError.IsVisible = false;
        PasswordError.IsVisible = false;
        RepeatError.IsVisible = false;
        MessageText.IsVisible = false;

        string login = LoginTextBox.Text?.Trim() ?? "";
        string password = PasswordTextBox.Text ?? "";
        string repeat = RepeatTextBox.Text ?? "";

        bool valid = true;

        if (!Regex.IsMatch(login, @"\A[A-Za-zА-Яа-яЁё0-9_.-]{4,50}\z"))
        {
            Show(LoginError,
                "От 4 до 50 символов: буквы, цифры, точка, дефис и подчёркивание. Без пробелов.");
            valid = false;
        }

        if (password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            Show(PasswordError,
                "Минимум 8 символов, хотя бы одна буква и одна цифра.");
            valid = false;
        }
        else if (Encoding.UTF8.GetByteCount(password) > 72)
        {
            Show(PasswordError, "Пароль слишком длинный. Максимум 72 байта.");
            valid = false;
        }
        else if (password != repeat)
        {
            Show(RepeatError, "Пароли не совпадают.");
            valid = false;
        }

        if (!valid)
            return;

        bool confirmed = await AdminDialogs.ConfirmAsync(
            this,
            "Создать администратора?",
            "Пользователь «" + login + "» получит полный доступ к управлению программой.",
            "Создать",
            false);

        if (!confirmed)
            return;

        bool done = false;
        string? error = null;

        _isBusy = true;
        ContentPanel.IsEnabled = false;

        try
        {
            string hash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12));

            await using var db = new MentoraContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                if (Owner is MainWindow mainWindow)
                    mainWindow.ShowLogin();

                Close(false);
                return;
            }

            string lowered = login.ToLower();

            bool exists = await db.Users.AnyAsync(u => u.Login.ToLower() == lowered);

            if (exists)
            {
                Show(LoginError, "Этот логин уже занят.");
            }
            else
            {
                DateTime now = DateTime.UtcNow;

                var user = new User
                {
                    Login = login,
                    PasswordHash = hash,
                    IdRole = 3,
                    Course = null,
                    IdUserStatus = 1,
                    FailedLoginAttempts = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                db.Users.Add(user);
                await db.SaveChangesAsync();

                db.ActionLogs.Add(new ActionLog
                {
                    IdUser = UserSession.IdUser,
                    ActionType = "Создание администратора",
                    ObjectType = "Пользователь",
                    ObjectId = user.IdUser,
                    OccurredAt = now,
                    IsSuccess = true,
                    Description = login
                });

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                done = true;
            }
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            Show(LoginError, "Этот логин уже занят.");
        }
        catch (Exception)
        {
            error = "Не удалось создать администратора. Проверьте подключение к базе.";
        }
        finally
        {
            _isBusy = false;
            ContentPanel.IsEnabled = true;
        }

        if (done)
        {
            Close(true);
            return;
        }

        if (error != null)
            Show(MessageText, error);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        Close(false);
    }

    private static void Show(TextBlock block, string text)
    {
        block.Text = text;
        block.IsVisible = true;
    }
}