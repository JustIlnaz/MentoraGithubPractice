using Avalonia.Controls;
using Avalonia.Interactivity;
using mentora.Models;

namespace mentora.Views;

public partial class AdminView : UserControl
{
    private Button? _activeButton;

    public AdminView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (!UserSession.IsAuthorized || UserSession.IdRole != 3)
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            HeaderNameText.Text = UserSession.DisplayName;

            ShowSection(DashboardButton, "Главная", new AdminDashboardView());
        };
    }

    public void ShowSection(Button button, string title, Control content)
    {
        if (!UserSession.IsAuthorized || UserSession.IdRole != 3)
        {
            AdminGuard.ReturnToLogin(this);
            return;
        }

        _activeButton?.Classes.Remove("selected");
        button.Classes.Add("selected");
        _activeButton = button;

        PageTitleText.Text = title;
        SectionHost.Content = content;
    }

    private void DashboardButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(DashboardButton, "Главная", new AdminDashboardView());
    }

    private void LogoutButton_Click(object? sender, RoutedEventArgs e)
    {
        AdminGuard.ReturnToLogin(this);
    }
    private void UsersButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(UsersButton, "Пользователи", new AdminUsersView());
    }
    private void AssignmentsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(AssignmentsButton, "Назначения", new AdminAssignmentsView());
    }
    private void DictionariesButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(DictionariesButton, "Справочники", new AdminDictionariesView());
    }
    private void MeetingsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(MeetingsButton, "Встречи", new AdminMeetingsView());
    }

    private void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(StatsButton, "Статистика", new AdminStatsView());
    }

    private void LogButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(LogButton, "Журнал действий", new AdminLogView());
    }
    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection(SettingsButton, "Настройки программы", new AdminSettingsView());
    }
}