using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class AdminDashboardView : UserControl
{
    private bool _isLoading;

    public AdminDashboardView()
    {
        InitializeComponent();

        Loaded += async (_, _) => await LoadAsync();
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        RefreshButton.IsEnabled = false;
        StatsPanel.IsVisible = false;

        ShowMessage("Загрузка данных…", false);

        try
        {
            await using var db = new MentoraContext();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            int activeStudents = await db.Users.CountAsync(u =>
                u.IdRole == 1 && u.IdUserStatus == 1);

            int freeStudents = await db.Users.CountAsync(u =>
                u.IdRole == 1
                && u.IdUserStatus == 1
                && !db.Assignments.Any(a =>
                    a.IdStudent == u.IdUser
                    && a.IdAssignmentStatus == 1));

            int activeCoaches = await db.Users.CountAsync(u =>
                u.IdRole == 2 && u.IdUserStatus == 1);

            int freeCoaches = await db.Users.CountAsync(u =>
                u.IdRole == 2
                && u.IdUserStatus == 1
                && !db.Assignments.Any(a =>
                    a.IdCoach == u.IdUser
                    && a.IdAssignmentStatus == 1));

            int activeAssignments = await db.Assignments.CountAsync(a =>
                a.IdAssignmentStatus == 1);

            int completedAssignments = await db.Assignments.CountAsync(a =>
                a.IdAssignmentStatus == 2);

            ActiveStudentsValue.Text = activeStudents.ToString();
            FreeStudentsValue.Text = freeStudents.ToString();
            ActiveCoachesValue.Text = activeCoaches.ToString();
            FreeCoachesValue.Text = freeCoaches.ToString();
            ActiveAssignmentsValue.Text = activeAssignments.ToString();
            CompletedAssignmentsValue.Text = completedAssignments.ToString();

            MessageText.IsVisible = false;
            StatsPanel.IsVisible = true;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить статистику. " +
                "Проверьте подключение к базе и нажмите «Обновить».",
                true);
        }
        finally
        {
            _isLoading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = Brush.Parse(isError ? "#B52C2C" : "#747C90");
        MessageText.IsVisible = true;
    }
}