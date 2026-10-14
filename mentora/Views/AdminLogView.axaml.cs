using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using mentora.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace mentora.Views;

public sealed class AdminOption
{
    public AdminOption(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }

    public string Name { get; }

    public override string ToString() => Name;
}

public partial class AdminLogView : UserControl
{
    private const int PageSize = 15;

    private bool _isBusy;
    private bool _ready;
    private int _page = 1;
    private int _total;

    public AdminLogView()
    {
        InitializeComponent();

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        if (_ready || _isBusy)
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

            var types = await db.ActionLogs
                .AsNoTracking()
                .Select(l => l.ActionType)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();

            var items = new List<AdminOption> { new(0, "Все действия") };
            items.AddRange(types.Select(t => new AdminOption(1, t)));
            ActionFilter.ItemsSource = items;

            ResultFilter.ItemsSource = new List<AdminOption>
            {
                new(0, "Все"),
                new(1, "Успешные"),
                new(2, "Неуспешные")
            };

            SortFilter.ItemsSource = new List<AdminOption>
            {
                new(0, "Сначала новые"),
                new(1, "Сначала старые"),
                new(2, "По действию (А–Я)"),
                new(3, "По логину (А–Я)")
            };

            ResetSelection();
            _ready = true;
        }
        catch (Exception)
        {
            ShowMessage("Не удалось загрузить журнал. Проверьте подключение к базе.", true);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        await SearchAsync();
    }

    private void ResetSelection()
    {
        UserSearchBox.Text = "";
        ActionFilter.SelectedIndex = 0;
        ResultFilter.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        SortFilter.SelectedIndex = 0;
    }

    private async Task SearchAsync()
    {
        if (_isBusy || !_ready)
            return;

        string search = UserSearchBox.Text?.Trim() ?? "";

        var actionItem = ActionFilter.SelectedItem as AdminOption;
        string? action = actionItem == null || actionItem.Id == 0 ? null : actionItem.Name;

        int result = (ResultFilter.SelectedItem as AdminOption)?.Id ?? 0;
        int sort = (SortFilter.SelectedItem as AdminOption)?.Id ?? 0;

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

            var query = from l in db.ActionLogs.AsNoTracking()
                        join u in db.Users on l.IdUser equals (int?)u.IdUser into g
                        from u in g.DefaultIfEmpty()
                        select new { Log = l, Login = u != null ? u.Login : null };

            if (!string.IsNullOrEmpty(search))
            {
                string pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(x => x.Login != null && EF.Functions.ILike(x.Login, pattern, "\\"));
            }

            if (action != null)
                query = query.Where(x => x.Log.ActionType == action);

            if (result == 1)
                query = query.Where(x => x.Log.IsSuccess);
            else if (result == 2)
                query = query.Where(x => !x.Log.IsSuccess);

            if (from != null)
            {
                DateTime fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Local).ToUniversalTime();
                query = query.Where(x => x.Log.OccurredAt >= fromUtc);
            }

            if (to != null)
            {
                DateTime toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
                query = query.Where(x => x.Log.OccurredAt < toUtc);
            }

            int total = await query.CountAsync();
            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            int page = Math.Clamp(_page, 1, totalPages);

            var ordered = sort switch
            {
                1 => query.OrderBy(x => x.Log.OccurredAt).ThenBy(x => x.Log.IdActionLog),
                2 => query.OrderBy(x => x.Log.ActionType).ThenByDescending(x => x.Log.OccurredAt),
                3 => query.OrderBy(x => x.Login).ThenByDescending(x => x.Log.OccurredAt),
                _ => query.OrderByDescending(x => x.Log.OccurredAt).ThenByDescending(x => x.Log.IdActionLog)
            };

            var rows = await ordered
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(x => new AdminLogRow
                {
                    OccurredAt = x.Log.OccurredAt,
                    Login = x.Login,
                    ActionType = x.Log.ActionType,
                    Description = x.Log.Description,
                    IsSuccess = x.Log.IsSuccess
                })
                .ToListAsync();

            _total = total;
            _page = page;

            LogList.ItemsSource = rows;

            ResultsText.Text = total == 0 ? "Записи не найдены." : "Найдено записей: " + total;

            PaginationPanel.IsVisible = total > PageSize;
            PreviousButton.IsEnabled = page > 1;
            NextButton.IsEnabled = page < totalPages;
            PageNumberText.Text = page + " / " + totalPages;
        }
        catch (Exception)
        {
            ShowMessage("Не удалось загрузить записи. Проверьте подключение к базе.", true);
        }
        finally
        {
            SetBusy(false);
        }
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

        ResetSelection();
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
}

public class AdminLogRow
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public DateTime OccurredAt { get; set; }

    public string? Login { get; set; }

    public string ActionType { get; set; } = "";

    public string? Description { get; set; }

    public bool IsSuccess { get; set; }

    public string DateText => OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", RussianCulture);

    public string ResultText => IsSuccess ? "Успешно" : "Ошибка";

    public IBrush ResultBrush => IsSuccess ? Brush.Parse("#278450") : Brush.Parse("#B52C2C");


}