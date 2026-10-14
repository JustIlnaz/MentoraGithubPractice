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

public partial class AdminUsersView : UserControl
{
    private const int PageSize = 10;

    private bool _isBusy;
    private bool _filtersLoaded;
    private int _page = 1;
    private int _total;

    public AdminUsersView()
    {
        InitializeComponent();

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        if (_filtersLoaded)
            return;

        if (_isBusy)
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

            var specialties = await db.Specialties
                .AsNoTracking()
                .OrderBy(s => s.Abbreviation)
                .ToListAsync();

            var sectors = await db.Sectors
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();

            var statuses = await db.UserStatuses
                .AsNoTracking()
                .OrderBy(s => s.IdUserStatus)
                .ToListAsync();

            RoleFilter.ItemsSource = new List<FilterItem>
            {
                new(0, "Все роли"),
                new(1, "Студент"),
                new(2, "Коуч"),
                new(3, "Администратор")
            };

            CourseFilter.ItemsSource = new List<FilterItem>
            {
                new(0, "Все курсы"),
                new(1, "1 курс"),
                new(2, "2 курс"),
                new(3, "3 курс"),
                new(4, "4 курс")
            };

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

            var statusItems = new List<FilterItem> { new(0, "Все статусы") };
            statusItems.AddRange(statuses.Select(s =>
                new FilterItem(s.IdUserStatus, s.Name)));
            StatusFilter.ItemsSource = statusItems;

            AssignmentFilter.ItemsSource = new List<FilterItem>
            {
                new(0, "Любое"),
                new(1, "Есть активное назначение"),
                new(2, "Нет активного назначения")
            };

            SortFilter.ItemsSource = new List<FilterItem>
            {
                new(0, "Логин (А–Я)"),
                new(1, "Логин (Я–А)"),
                new(2, "Регистрация (новые)"),
                new(3, "Регистрация (старые)")
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
        LoginSearchBox.Text = "";

        RoleFilter.SelectedIndex = 0;
        CourseFilter.SelectedIndex = 0;
        SpecialtyFilter.SelectedIndex = 0;
        SectorFilter.SelectedIndex = 0;
        StatusFilter.SelectedIndex = 0;
        AssignmentFilter.SelectedIndex = 0;
        SortFilter.SelectedIndex = 0;
    }

    private async Task SearchAsync()
    {
        if (_isBusy || !_filtersLoaded)
            return;

        string search = LoginSearchBox.Text?.Trim() ?? "";

        int role = IdOf(RoleFilter);
        short course = (short)IdOf(CourseFilter);
        int specialtyId = IdOf(SpecialtyFilter);
        int sectorId = IdOf(SectorFilter);
        int statusId = IdOf(StatusFilter);
        int assignment = IdOf(AssignmentFilter);
        int sort = IdOf(SortFilter);

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

            IQueryable<User> query = db.Users.AsNoTracking();

            if (!string.IsNullOrEmpty(search))
            {
                string pattern = "%" + EscapeLike(search) + "%";

                query = query.Where(u =>
                    EF.Functions.ILike(u.Login, pattern, "\\"));
            }

            if (role != 0)
                query = query.Where(u => u.IdRole == role);

            if (course != 0)
                query = query.Where(u => u.Course == course);

            if (statusId != 0)
                query = query.Where(u => u.IdUserStatus == statusId);

            if (specialtyId != 0)
            {
                query = query.Where(u =>
                    u.Profile != null
                    && u.Profile.IdSpecialty == specialtyId);
            }

            if (sectorId != 0)
            {
                query = query.Where(u =>
                    u.Profile != null
                    && u.Profile.IdSector == sectorId);
            }

            if (assignment == 1)
            {
                query = query.Where(u =>
                    db.Assignments.Any(a =>
                        (a.IdStudent == u.IdUser || a.IdCoach == u.IdUser)
                        && a.IdAssignmentStatus == 1));
            }
            else if (assignment == 2)
            {
                query = query.Where(u =>
                    (u.IdRole == 1 || u.IdRole == 2)
                    && !db.Assignments.Any(a =>
                        (a.IdStudent == u.IdUser || a.IdCoach == u.IdUser)
                        && a.IdAssignmentStatus == 1));
            }

            int total = await query.CountAsync();

            int totalPages = Math.Max(
                1,
                (int)Math.Ceiling(total / (double)PageSize));

            int page = Math.Clamp(_page, 1, totalPages);

            IOrderedQueryable<User> ordered = sort switch
            {
                1 => query.OrderByDescending(u => u.Login).ThenBy(u => u.IdUser),
                2 => query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.IdUser),
                3 => query.OrderBy(u => u.CreatedAt).ThenBy(u => u.IdUser),
                _ => query.OrderBy(u => u.Login).ThenBy(u => u.IdUser)
            };

            var rows = await ordered
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(u => new AdminUserRow
                {
                    IdUser = u.IdUser,
                    Login = u.Login,
                    IdRole = u.IdRole,
                    RoleName = u.IdRoleNavigation.Name,
                    Course = u.Course,

                    Specialty = u.Profile != null
                        ? u.Profile.IdSpecialtyNavigation.Abbreviation
                        : null,

                    Sector = u.Profile != null
                        ? u.Profile.IdSectorNavigation.Name
                        : null,

                    IdUserStatus = u.IdUserStatus,
                    StatusName = u.IdUserStatusNavigation.Name,

                    HasActiveAssignment = db.Assignments.Any(a =>
                        (a.IdStudent == u.IdUser || a.IdCoach == u.IdUser)
                        && a.IdAssignmentStatus == 1),

                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            _total = total;
            _page = page;

            UsersList.ItemsSource = rows;

            ResultsText.Text = total == 0
                ? "Пользователи не найдены."
                : "Найдено пользователей: " + total;

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

    private async void LoginSearchBox_KeyDown(object? sender, KeyEventArgs e)
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

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not Button { Tag: AdminUserRow row })
            return;

        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var window = new AdminUserWindow(row.IdUser);

        await window.ShowDialog(owner);

        await SearchAsync();
    }

    private async void CreateAdminButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var window = new AdminCreateAdminWindow();

        bool created = await window.ShowDialog<bool>(owner);

        if (created)
        {
            _page = 1;
            await SearchAsync();
            ShowMessage("Администратор создан.", false);
        }
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

public class AdminUserRow
{
    private static readonly CultureInfo RussianCulture =
        CultureInfo.GetCultureInfo("ru-RU");

    public int IdUser { get; set; }

    public string Login { get; set; } = "";

    public int IdRole { get; set; }

    public string RoleName { get; set; } = "";

    public short? Course { get; set; }

    public string? Specialty { get; set; }

    public string? Sector { get; set; }

    public int IdUserStatus { get; set; }

    public string StatusName { get; set; } = "";

    public bool HasActiveAssignment { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public string RoleText => Course == null
        ? RoleName
        : RoleName + " · " + Course + " курс";

    public string ProfileText => Specialty == null
        ? "Анкеты нет"
        : Specialty + " · " + Sector;

    public string AssignmentText => IdRole == 3
        ? ""
        : HasActiveAssignment
            ? "Есть активное назначение"
            : "Без назначения";

    public string DatesText
    {
        get
        {
            string text = "Регистрация: " +
                          CreatedAt.ToLocalTime().ToString("dd.MM.yyyy", RussianCulture);

            text += LastLoginAt == null
                ? " · не входил"
                : " · вход: " +
                  LastLoginAt.Value.ToLocalTime().ToString("dd.MM.yyyy", RussianCulture);

            return text;
        }
    }

    public IBrush StatusBrush => StatusName switch
    {
        "Активен" => Brush.Parse("#278450"),
        "Заблокирован" => Brush.Parse("#B52C2C"),
        _ => Brush.Parse("#747C90")
    };
}