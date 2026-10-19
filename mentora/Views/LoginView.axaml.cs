using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class LoginView : UserControl
{
    private const int MaxFailedAttempts = 5;
    private const int LockMinutes = 15;

    private const string GenericError =
        "Не удалось войти. Проверьте логин и пароль или повторите попытку позже.";

    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("dummy-password-for-timing", 12);

    private bool _isBusy;

    public LoginView()
    {
        InitializeComponent();
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoginAsync();
    }

    private async void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoginAsync();
        }
    }

    private async Task LoginAsync()
    {
        if (_isBusy)
        {
            return;
        }

        HideMessages();

        string login = LoginTextBox.Text?.Trim() ?? "";
        string password = PasswordTextBox.Text ?? "";

        LoginTextBox.Text = login;

        bool hasErrors = false;

        if (string.IsNullOrWhiteSpace(login))
        {
            LoginErrorText.Text = "Введите логин.";
            LoginErrorText.IsVisible = true;
            hasErrors = true;
        }

        if (string.IsNullOrEmpty(password))
        {
            PasswordErrorText.Text = "Введите пароль.";
            PasswordErrorText.IsVisible = true;
            hasErrors = true;
        }

        if (hasErrors)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not MainWindow mainWindow)
        {
            ShowMessage("Не удалось открыть главное окно.");
            return;
        }

        SetBusy(true);
        UserSession.Clear();

        try
        {
            await using var db = new MentoraContext();

            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var user = await db.Users
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM users
                    WHERE LOWER(login) = LOWER({login})
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync();

            DateTime now = DateTime.UtcNow;

            bool loginSucceeded = false;
            string errorMessage = GenericError;
            string logDescription = "Неверные учётные данные.";

            if (user == null)
            {
                logDescription = "Неверные учётные данные.";

                await Task.Run(() => BCrypt.Net.BCrypt.Verify(password, DummyHash));
            }
            else if (user.LockedUntil.HasValue && user.LockedUntil.Value > now)
            {
                errorMessage = GenericError;

                logDescription = "Вход отклонён: временное ограничение попыток.";
            }
            else
            {
                if (user.LockedUntil.HasValue)
                {
                    user.LockedUntil = null;
                    user.FailedLoginAttempts = 0;
                }

                bool passwordCorrect = await Task.Run(
                    () => BCrypt.Net.BCrypt.Verify(
                        password,
                        user.PasswordHash));

                if (!passwordCorrect)
                {
                    user.FailedLoginAttempts++;

                    if (user.FailedLoginAttempts >= MaxFailedAttempts)
                    {
                        user.LockedUntil = DateTime.UtcNow.AddMinutes(LockMinutes);

                        errorMessage = GenericError;

                        logDescription = "Временная блокировка после неверных паролей.";
                    }
                }
                else if (user.IdUserStatus != 1)
                {
                    errorMessage = "Учётная запись заблокирована или архивирована. " + "Обратитесь к администратору.";

                    logDescription = "Вход отклонён: учётная запись неактивна.";
                }
                else if (user.IdRole < 1 || user.IdRole > 3)
                {
                    errorMessage = "Для пользователя не настроена допустимая роль. " + "Обратитесь к администратору.";

                    logDescription = "Вход отклонён: недопустимая роль.";
                }
                else
                {
                    user.FailedLoginAttempts = 0;
                    user.LockedUntil = null;
                    user.LastLoginAt = DateTime.UtcNow;

                    loginSucceeded = true;
                    logDescription = "Вход в приложение выполнен.";
                }
            }

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = user?.IdUser,

                ActionType = loginSucceeded
                    ? "Успешный вход"
                    : "Неудачный вход",

                ObjectType = user == null ? null : "Пользователь",
                ObjectId = user?.IdUser,
                OccurredAt = DateTime.UtcNow,
                IsSuccess = loginSucceeded,
                Description = logDescription
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            PasswordTextBox.Text = "";

            if (!loginSucceeded || user == null)
            {
                ShowMessage(errorMessage);
                return;
            }

            UserSession.SetUser(user);
            mainWindow.ShowAuthorizedPage();
        }

        catch (Exception ex)
        {
            UserSession.Clear();
            PasswordTextBox.Text = "";

            System.Diagnostics.Debug.WriteLine(ex.ToString());
            System.Diagnostics.Debug.WriteLine(
                ex.InnerException?.ToString() ?? "InnerException отсутствует");

            ShowMessage(
                "Ошибка входа: " + ex.Message);
        }

        finally
        {
            SetBusy(false);
        }
    }

    private void RegisterButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.ShowRegistration();
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;

        LoginButton.IsEnabled = !isBusy;
        RegisterButton.IsEnabled = !isBusy;
        LoginTextBox.IsEnabled = !isBusy;
        PasswordTextBox.IsEnabled = !isBusy;

        LoginButton.Content = isBusy ? "Выполняется вход…" : "Войти";
    }

    private void HideMessages()
    {
        LoginErrorText.IsVisible = false;
        PasswordErrorText.IsVisible = false;
        MessageBorder.IsVisible = false;
    }

    private void ShowMessage(string message)
    {
        MessageBorder.Background = Avalonia.Media.Brush.Parse("#FFF1F1");

        MessageText.Foreground = Avalonia.Media.Brush.Parse("#B52C2C");

        MessageText.Text = message;
        MessageBorder.IsVisible = true;
    }

    public void ShowRegistrationSuccess(string login)
    {
        LoginTextBox.Text = login;
        PasswordTextBox.Text = "";

        MessageBorder.Background = Avalonia.Media.Brush.Parse("#EDF9F1");

        MessageText.Foreground = Avalonia.Media.Brush.Parse("#278450");

        MessageText.Text = "Регистрация завершена. Введите пароль, чтобы войти.";

        MessageBorder.IsVisible = true;
    }
}