using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class AdminStatsView : UserControl
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private bool _isBusy;

    public AdminStatsView()
    {
        InitializeComponent();

        Loaded += async (_, _) => await LoadAsync();
    }

    private async void ShowButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async void AllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_isBusy)
            return;

        DateTimeOffset? from = DateFromPicker.SelectedDate;
        DateTimeOffset? to = DateToPicker.SelectedDate;

        if (from != null && to != null && from.Value.Date > to.Value.Date)
        {
            ShowMessage("Дата «с» не может быть позже даты «по».", true);
            return;
        }

        DateTime? fromUtc = from == null
            ? null
            : DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Local).ToUniversalTime();

        DateTime? toUtc = to == null
            ? null
            : DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

        MessageText.IsVisible = false;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            int newStudents = await db.Users.CountAsync(u =>
                u.IdRole == 1
                && (fromUtc == null || u.CreatedAt >= fromUtc)
                && (toUtc == null || u.CreatedAt < toUtc));

            int newCoaches = await db.Users.CountAsync(u =>
                u.IdRole == 2
                && (fromUtc == null || u.CreatedAt >= fromUtc)
                && (toUtc == null || u.CreatedAt < toUtc));

            int created = await db.Assignments.CountAsync(a =>
                (fromUtc == null || a.CreatedAt >= fromUtc)
                && (toUtc == null || a.CreatedAt < toUtc));

            int completed = await db.Assignments.CountAsync(a =>
                a.IdAssignmentStatus == 2
                && a.ClosedAt != null
                && (fromUtc == null || a.ClosedAt >= fromUtc)
                && (toUtc == null || a.ClosedAt < toUtc));

            int cancelled = await db.Assignments.CountAsync(a =>
                a.IdAssignmentStatus == 3
                && a.ClosedAt != null
                && (fromUtc == null || a.ClosedAt >= fromUtc)
                && (toUtc == null || a.ClosedAt < toUtc));

            int meetings = await db.Meetings.CountAsync(m =>
                !m.IsArchived
                && (fromUtc == null || m.MeetingDate >= fromUtc)
                && (toUtc == null || m.MeetingDate < toUtc));

            int activeNow = await db.Assignments.CountAsync(a => a.IdAssignmentStatus == 1);

            int freeNow = await db.Users.CountAsync(u =>
                u.IdRole == 1
                && u.IdUserStatus == 1
                && !db.Assignments.Any(a => a.IdStudent == u.IdUser && a.IdAssignmentStatus == 1));

            var bySpecialty = await db.Assignments
                .AsNoTracking()
                .Where(a => (fromUtc == null || a.CreatedAt >= fromUtc)
                            && (toUtc == null || a.CreatedAt < toUtc))
                .GroupBy(a => a.IdStudentNavigation.IdSpecialtyNavigation.Abbreviation)
                .Select(g => new StatRow { Name = g.Key, Count = g.Count() })
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Name)
                .ToListAsync();

            var bySector = await db.Assignments
                .AsNoTracking()
                .Where(a => (fromUtc == null || a.CreatedAt >= fromUtc)
                            && (toUtc == null || a.CreatedAt < toUtc))
                .GroupBy(a => a.IdStudentNavigation.IdSectorNavigation.Name)
                .Select(g => new StatRow { Name = g.Key, Count = g.Count() })
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Name)
                .ToListAsync();

            var free = await db.Profiles
                .AsNoTracking()
                .Where(p =>
                    p.IdUserNavigation.IdRole == 1
                    && p.IdUserNavigation.IdUserStatus == 1
                    && !db.Assignments.Any(a => a.IdStudent == p.IdUser && a.IdAssignmentStatus == 1))
                .GroupBy(p => p.IdSpecialtyNavigation.Abbreviation)
                .Select(g => new StatRow { Name = g.Key, Count = g.Count() })
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Name)
                .ToListAsync();

            NewStudentsValue.Text = newStudents.ToString();
            NewCoachesValue.Text = newCoaches.ToString();
            CreatedValue.Text = created.ToString();
            CompletedValue.Text = completed.ToString();
            CancelledValue.Text = cancelled.ToString();
            MeetingsValue.Text = meetings.ToString();
            ActiveNowValue.Text = activeNow.ToString();
            FreeNowValue.Text = freeNow.ToString();

            BySpecialtyList.ItemsSource = bySpecialty;
            BySectorList.ItemsSource = bySector;
            FreeList.ItemsSource = free;

            BySpecialtyEmpty.IsVisible = bySpecialty.Count == 0;
            BySectorEmpty.IsVisible = bySector.Count == 0;
            FreeEmpty.IsVisible = free.Count == 0;
            var freeStudents = await db.Profiles
    .AsNoTracking()
    .Where(p =>
        p.IdUserNavigation.IdRole == 1
        && p.IdUserNavigation.IdUserStatus == 1
        && !db.Assignments.Any(a => a.IdStudent == p.IdUser && a.IdAssignmentStatus == 1))
    .OrderBy(p => p.IdUserNavigation.CreatedAt)
    .Take(100)
    .Select(p => new FreeStudentRow
    {
        Login = p.IdUserNavigation.Login,
        Specialty = p.IdSpecialtyNavigation.Abbreviation,
        Sector = p.IdSectorNavigation.Name,
        RegisteredAt = p.IdUserNavigation.CreatedAt
    })
    .ToListAsync();

            FreeStudentsList.ItemsSource = freeStudents;

            FreeStudentsNote.Text = freeStudents.Count == 0
                ? "Все активные студенты уже закреплены за коучами."
                : freeStudents.Count < 100
                    ? "Всего: " + freeStudents.Count + ". Сначала те, кто ждёт дольше всех."
                    : "Показаны первые 100 студентов, ждущих дольше всех.";

            FreeStudentsBorder.IsVisible = true;

            PeriodText.Text = from == null && to == null
                ? "Период: за всё время (показатели «сейчас» не зависят от периода)."
                : "Период: " +
                  (from == null ? "…" : from.Value.ToString("dd.MM.yyyy", RussianCulture)) +
                  " — " +
                  (to == null ? "…" : to.Value.ToString("dd.MM.yyyy", RussianCulture)) +
                  " (показатели «сейчас» не зависят от периода).";

            StatsPanel.IsVisible = true;
            TablesPanel.IsVisible = true;
        }
        catch (Exception)
        {
            ShowMessage("Не удалось загрузить статистику. Проверьте подключение к базе.", true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RootPanel.IsEnabled = !isBusy;
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = Brush.Parse(isError ? "#B52C2C" : "#278450");
        MessageText.IsVisible = true;
    }
}

public class StatRow
{
    public string Name { get; set; } = "";

    public int Count { get; set; }
}
public class FreeStudentRow
{
    public string Login { get; set; } = "";

    public string Specialty { get; set; } = "";

    public string Sector { get; set; } = "";

    public DateTime RegisteredAt { get; set; }

    public string ProfileText => Specialty + " · " + Sector;

    public string DateText => "с " + RegisteredAt.ToLocalTime().ToString("dd.MM.yyyy");
}