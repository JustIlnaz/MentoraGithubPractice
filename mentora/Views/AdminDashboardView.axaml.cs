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

        ProgressPanel.IsVisible = false;

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

            int totalSteps = await db.GoalSteps.CountAsync(s =>
                s.IdGoalNavigation.IdAssignmentNavigation.IdAssignmentStatus != 3);
            int doneSteps = await db.GoalSteps.CountAsync(s =>
                s.IsDone
                && s.IdGoalNavigation.IdAssignmentNavigation.IdAssignmentStatus != 3);
            double overall = totalSteps == 0 ? 0 : doneSteps * 100.0 / totalSteps;

            OverallPercentValue.Text = Math.Round(overall) + "%";
            OverallBar.Value = overall;
            OverallText.Text = totalSteps == 0
                ? "Шагов пока нет."
                : "Выполнено шагов: " + doneSteps + " из " + totalSteps +
                  " (без отменённых назначений).";

            var activePairs = await db.Assignments
                .AsNoTracking()
                .Where(a => a.IdAssignmentStatus == 1)
                .Select(a => new
                {
                    a.IdAssignment,
                    Coach = a.IdCoachNavigation.IdUserNavigation.Login,
                    Student = a.IdStudentNavigation.IdUserNavigation.Login
                })
                .ToListAsync();

            var pairSteps = await db.GoalSteps
                .AsNoTracking()
                .Where(s => s.IdGoalNavigation.IdAssignmentNavigation.IdAssignmentStatus == 1)
                .GroupBy(s => s.IdGoalNavigation.IdAssignment)
                .Select(g => new
                {
                    IdAssignment = g.Key,
                    Total = g.Count(),
                    Done = g.Count(s => s.IsDone)
                })
                .ToListAsync();

            var pairs = activePairs
                .Select(p =>
                {
                    var st = pairSteps.FirstOrDefault(x => x.IdAssignment == p.IdAssignment);
                    int total = st?.Total ?? 0;
                    double percent = total == 0 ? 0 : st!.Done * 100.0 / total;
                    return new ProgressRow
                    {
                        Title = p.Student + " ← " + p.Coach,
                        Percent = percent,
                        PercentText = total == 0 ? "нет шагов" : Math.Round(percent) + "%"
                    };
                })
                .OrderByDescending(r => r.Percent)
                .ThenBy(r => r.Title)
                .Take(12)
                .ToList();
            PairsList.ItemsSource = pairs;
            PairsEmpty.IsVisible = pairs.Count == 0;

            DateTime firstMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-5);
            DateTime firstUtc = DateTime.SpecifyKind(firstMonth, DateTimeKind.Local).ToUniversalTime();
            var doneDates = await db.GoalSteps
                .AsNoTracking()
                .Where(s => s.IsDone && s.DoneAt >= firstUtc)
                .Select(s => s.DoneAt)
                .ToListAsync();

            var months = Enumerable.Range(0, 6)
                .Select(i => firstMonth.AddMonths(i))
                .Select(m => new MonthBarRow
                {
                    Label = m.ToString("MMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
                             .TrimEnd('.'),
                    Count = doneDates.Count(d =>
                        d != null
                        && d.Value.ToLocalTime().Year == m.Year
                        && d.Value.ToLocalTime().Month == m.Month)
                })
                .ToList();
            int maxCount = Math.Max(1, months.Max(m => m.Count));
            foreach (var m in months)
                m.BarHeight = m.Count == 0 ? 2 : 140.0 * m.Count / maxCount;
            MonthsChart.ItemsSource = months;

            ProgressPanel.IsVisible = true;

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

public class ProgressRow
{
    public string Title { get; set; } = "";
    public double Percent { get; set; } 
    public string PercentText { get; set; } = "";
}

public class MonthBarRow
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double BarHeight { get; set; }
}