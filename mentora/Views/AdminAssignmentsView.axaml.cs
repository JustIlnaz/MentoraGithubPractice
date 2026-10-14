using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class AdminAssignmentsView : UserControl
{
    private const int PageSize = 10;

    private bool _isBusy;
    private bool _filtersLoaded;
    private int _page = 1;
    private int _total;

    public AdminAssignmentsView()
    {
        InitializeComponent();

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        if (_filtersLoaded || _isBusy)
            return;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            var statuses = await db.AssignmentStatuses
                .AsNoTracking()
                .OrderBy(s => s.IdAssignmentStatus)
                .ToListAsync();

            var specialties = await db.Specialties
                .AsNoTracking()
                .OrderBy(s => s.Abbreviation)
                .ToListAsync();

            var sectors = await db.Sectors
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();

            var statusItems = new List<FilterItem> { new(0, "Все статусы") };
            statusItems.AddRange(statuses.Select(s =>
                new FilterItem(s.IdAssignmentStatus, s.Name)));
            StatusFilter.ItemsSource = statusItems;

            var specialtyItems = new List<FilterItem> { new(0, "Все специальности") };
            specialtyItems.AddRange(specialties.Select(s =>
                new FilterItem(
                    s.IdSpecialty,
                    s.IsActive ? s.Abbreviation : s.Abbreviation + " (архив)")));
            SpecialtyFilter.ItemsSource = specialtyItems;

            var sectorItems = new List<FilterItem> { new(0, "Все секторы") };
            sectorItems.AddRange(sectors.Select(s =>
                new FilterItem(
                    s.IdSector,
                    s.IsActive ? s.Name : s.Name + " (архив)")));
            SectorFilter.ItemsSource = sectorItems;

            SortFilter.ItemsSource = new List<FilterItem>
            {
                new(0, "Сначала новые"),
                new(1, "Сначала старые"),
                new(2, "Логин студента (А–Я)"),
                new(3, "Логин коуча (А–Я)")
            };

            ResetFilterSelection();

            _filtersLoaded = true;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить фильтры. " +
                "Проверьте подключение к базе и откройте раздел заново.",
                true);

            return;
        }
        finally
        {
            SetBusy(false);
        }

        _page = 1;
        await SearchAsync();
    }

    private void ResetFilterSelection()
    {
        StatusFilter.SelectedIndex = 0;
        SpecialtyFilter.SelectedIndex = 0;
        SectorFilter.SelectedIndex = 0;

        CoachSearchBox.Text = "";
        StudentSearchBox.Text = "";

        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        SortFilter.SelectedIndex = 0;
    }

    private async Task SearchAsync()
    {
        if (_isBusy || !_filtersLoaded)
            return;

        int statusId = IdOf(StatusFilter);
        int specialtyId = IdOf(SpecialtyFilter);
        int sectorId = IdOf(SectorFilter);

        string coachSearch = CoachSearchBox.Text?.Trim() ?? "";
        string studentSearch = StudentSearchBox.Text?.Trim() ?? "";

        int sort = IdOf(SortFilter);

        DateTimeOffset? from = DateFromPicker.SelectedDate;
        DateTimeOffset? to = DateToPicker.SelectedDate;

        if (from != null && to != null && from.Value.Date > to.Value.Date)
        {
            ShowMessage("Дата «с» не может быть позже даты «по».", true);
            return;
        }

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

            IQueryable<Assignment> query = db.Assignments.AsNoTracking();

            if (statusId != 0)
                query = query.Where(a => a.IdAssignmentStatus == statusId);

            if (specialtyId != 0)
            {
                query = query.Where(a =>
                    a.IdStudentNavigation.IdSpecialty == specialtyId);
            }

            if (sectorId != 0)
            {
                query = query.Where(a =>
                    a.IdStudentNavigation.IdSector == sectorId);
            }

            if (!string.IsNullOrEmpty(coachSearch))
            {
                string pattern = "%" + EscapeLike(coachSearch) + "%";

                query = query.Where(a =>
                    EF.Functions.ILike(
                        a.IdCoachNavigation.IdUserNavigation.Login,
                        pattern,
                        "\\"));
            }

            if (!string.IsNullOrEmpty(studentSearch))
            {
                string pattern = "%" + EscapeLike(studentSearch) + "%";

                query = query.Where(a =>
                    EF.Functions.ILike(
                        a.IdStudentNavigation.IdUserNavigation.Login,
                        pattern,
                        "\\"));
            }

            if (from != null)
            {
                DateTime fromUtc = DateTime
                    .SpecifyKind(from.Value.Date, DateTimeKind.Local)
                    .ToUniversalTime();

                query = query.Where(a => a.CreatedAt >= fromUtc);
            }

            if (to != null)
            {
                DateTime toUtc = DateTime
                    .SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Local)
                    .ToUniversalTime();

                query = query.Where(a => a.CreatedAt < toUtc);
            }

            int total = await query.CountAsync();

            int totalPages = Math.Max(
                1,
                (int)Math.Ceiling(total / (double)PageSize));

            int page = Math.Clamp(_page, 1, totalPages);

            IOrderedQueryable<Assignment> ordered = sort switch
            {
                1 => query.OrderBy(a => a.CreatedAt).ThenBy(a => a.IdAssignment),
                2 => query.OrderBy(a => a.IdStudentNavigation.IdUserNavigation.Login).ThenBy(a => a.IdAssignment),
                3 => query.OrderBy(a => a.IdCoachNavigation.IdUserNavigation.Login).ThenBy(a => a.IdAssignment),
                _ => query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.IdAssignment)
            };

            var rows = await ordered
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(a => new AdminAssignmentRow
                {
                    IdAssignment = a.IdAssignment,
                    StudentLogin = a.IdStudentNavigation.IdUserNavigation.Login,
                    CoachLogin = a.IdCoachNavigation.IdUserNavigation.Login,
                    Specialty = a.IdStudentNavigation.IdSpecialtyNavigation.Abbreviation,
                    Sector = a.IdStudentNavigation.IdSectorNavigation.Name,
                    IdAssignmentStatus = a.IdAssignmentStatus,
                    StatusName = a.IdAssignmentStatusNavigation.Name,
                    CreatedAt = a.CreatedAt,
                    ClosedAt = a.ClosedAt
                })
                .ToListAsync();

            _total = total;
            _page = page;

            AssignmentsList.ItemsSource = rows;

            ResultsText.Text = total == 0
                ? "Назначения не найдены."
                : "Найдено назначений: " + total;

            PaginationPanel.IsVisible = total > PageSize;
            PreviousButton.IsEnabled = page > 1;
            NextButton.IsEnabled = page < totalPages;
            PageNumberText.Text = page + " / " + totalPages;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить список. " +
                "Проверьте подключение к базе и повторите попытку.",
                true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private static int IdOf(ComboBox box)
    {
        return (box.SelectedItem as FilterItem)?.Id ?? 0;
    }

    private async void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        _page = 1;
        await SearchAsync();
    }

    private async void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        ResetFilterSelection();

        _page = 1;
        await SearchAsync();
    }

    private async void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        _page = 1;
        await SearchAsync();
    }

    private async void PreviousButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _page <= 1)
            return;

        _page--;
        await SearchAsync();
    }

    private async void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _page * PageSize >= _total)
            return;

        _page++;
        await SearchAsync();
    }

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var window = new AdminCreateAssignmentWindow();

        bool created = await window.ShowDialog<bool>(owner);

        if (created)
        {
            _page = 1;
            await SearchAsync();
            ShowMessage("Назначение создано.", false);
        }
    }

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not Button { Tag: AdminAssignmentRow row })
            return;

        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var window = new AdminAssignmentWindow(row.IdAssignment);

        await window.ShowDialog(owner);

        await SearchAsync();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        FiltersPanel.IsEnabled = !isBusy;
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = Brush.Parse(isError ? "#B52C2C" : "#278450");
        MessageText.IsVisible = true;
    }

    private sealed class FilterItem
    {
        public FilterItem(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }
}

public class AdminAssignmentRow
{
    private static readonly CultureInfo RussianCulture =
        CultureInfo.GetCultureInfo("ru-RU");

    public int IdAssignment { get; set; }

    public string StudentLogin { get; set; } = "";

    public string CoachLogin { get; set; } = "";

    public string Specialty { get; set; } = "";

    public string Sector { get; set; } = "";

    public int IdAssignmentStatus { get; set; }

    public string StatusName { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public string StudentText => "Студент: " + StudentLogin;

    public string CoachText => "Коуч: " + CoachLogin;

    public string ProfileText => Specialty + " · " + Sector;

    public string CreatedText => "Создано: " +
        CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", RussianCulture);

    public string ClosedText => ClosedAt == null
        ? ""
        : "Закрыто: " +
          ClosedAt.Value.ToLocalTime().ToString("dd.MM.yyyy", RussianCulture);

    public IBrush StatusBrush => IdAssignmentStatus switch
    {
        1 => Brush.Parse("#278450"),
        2 => Brush.Parse("#39446B"),
        3 => Brush.Parse("#B52C2C"),
        _ => Brush.Parse("#747C90")
    };
}