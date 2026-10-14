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

public partial class AdminMeetingsView : UserControl
{
    private const int PageSize = 8;

    private bool _isBusy;
    private int _page = 1;
    private int _total;
    private int? _editingId;

    public AdminMeetingsView()
    {
        InitializeComponent();

        ArchiveFilter.ItemsSource = new List<AdminOption>
        {
            new(0, "Все"),
            new(1, "Только активные"),
            new(2, "Только архивные")
        };

        ArchiveFilter.SelectedIndex = 0;
        SortFilter.ItemsSource = new List<AdminOption>
        {
            new(0, "Сначала новые"),
            new(1, "Сначала старые"),
            new(2, "По теме (А–Я)"),
            new(3, "По логину коуча (А–Я)")
        };

        SortFilter.SelectedIndex = 0;

        Loaded += async (_, _) => await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_isBusy)
            return;

        string coach = CoachSearchBox.Text?.Trim() ?? "";
        string student = StudentSearchBox.Text?.Trim() ?? "";
        int archive = (ArchiveFilter.SelectedItem as AdminOption)?.Id ?? 0;
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

            IQueryable<Meeting> query = db.Meetings.AsNoTracking();

            if (!string.IsNullOrEmpty(coach))
            {
                string p = Like(coach);
                query = query.Where(m => EF.Functions.ILike(
                    m.IdAssignmentNavigation.IdCoachNavigation.IdUserNavigation.Login, p, "\\"));
            }

            if (!string.IsNullOrEmpty(student))
            {
                string p = Like(student);
                query = query.Where(m => EF.Functions.ILike(
                    m.IdAssignmentNavigation.IdStudentNavigation.IdUserNavigation.Login, p, "\\"));
            }

            if (archive == 1)
                query = query.Where(m => !m.IsArchived);
            else if (archive == 2)
                query = query.Where(m => m.IsArchived);

            if (from != null)
            {
                DateTime fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Local).ToUniversalTime();
                query = query.Where(m => m.MeetingDate >= fromUtc);
            }

            if (to != null)
            {
                DateTime toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
                query = query.Where(m => m.MeetingDate < toUtc);
            }

            int total = await query.CountAsync();
            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            int page = Math.Clamp(_page, 1, totalPages);

            IOrderedQueryable<Meeting> ordered = sort switch
            {
                1 => query.OrderBy(m => m.MeetingDate).ThenBy(m => m.IdMeeting),
                2 => query.OrderBy(m => m.Topic).ThenBy(m => m.IdMeeting),
                3 => query.OrderBy(m => m.IdAssignmentNavigation.IdCoachNavigation.IdUserNavigation.Login)
                    .ThenByDescending(m => m.MeetingDate),
                _ => query.OrderByDescending(m => m.MeetingDate).ThenByDescending(m => m.IdMeeting)
            };

            var rows = await ordered
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(m => new AdminMeetingRow
                {
                    IdMeeting = m.IdMeeting,
                    MeetingDate = m.MeetingDate,
                    Topic = m.Topic,
                    Summary = m.Summary,
                    CoachComment = m.CoachComment,
                    IsArchived = m.IsArchived,
                    CoachLogin = m.IdAssignmentNavigation.IdCoachNavigation.IdUserNavigation.Login,
                    StudentLogin = m.IdAssignmentNavigation.IdStudentNavigation.IdUserNavigation.Login
                })
                .ToListAsync();

            _total = total;
            _page = page;

            MeetingsList.ItemsSource = rows;

            ResultsText.Text = total == 0 ? "Встречи не найдены." : "Найдено встреч: " + total;

            PaginationPanel.IsVisible = total > PageSize;
            PreviousButton.IsEnabled = page > 1;
            NextButton.IsEnabled = page < totalPages;
            PageNumberText.Text = page + " / " + totalPages;
        }
        catch (Exception)
        {
            ShowMessage("Не удалось загрузить встречи. Проверьте подключение к базе.", true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string Like(string value)
    {
        return "%" + value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
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

        CoachSearchBox.Text = "";
        StudentSearchBox.Text = "";
        ArchiveFilter.SelectedIndex = 0;
        SortFilter.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;

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

    private void EditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not Button { Tag: AdminMeetingRow row })
            return;

        _editingId = row.IdMeeting;

        DateTime local = row.MeetingDate.ToLocalTime();

        FormTitle.Text = "Редактирование встречи";
        MeetingDatePicker.SelectedDate = new DateTimeOffset(local.Date);
        MeetingTimePicker.SelectedTime = new TimeSpan(local.Hour, local.Minute, 0);
        TopicTextBox.Text = row.Topic;
        SummaryTextBox.Text = row.Summary;
        CommentTextBox.Text = row.CoachComment ?? "";

        DateError.IsVisible = false;
        TimeError.IsVisible = false;
        TopicError.IsVisible = false;
        SummaryError.IsVisible = false;
        MessageText.IsVisible = false;

        FormBorder.IsVisible = true;
        FormBorder.BringIntoView();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        _editingId = null;
        FormBorder.IsVisible = false;
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _editingId == null)
            return;

        DateError.IsVisible = false;
        TimeError.IsVisible = false;
        TopicError.IsVisible = false;
        SummaryError.IsVisible = false;

        DateTimeOffset? date = MeetingDatePicker.SelectedDate;
        TimeSpan? time = MeetingTimePicker.SelectedTime;
        string topic = TopicTextBox.Text?.Trim() ?? "";
        string summary = SummaryTextBox.Text?.Trim() ?? "";
        string comment = CommentTextBox.Text?.Trim() ?? "";

        bool valid = true;

        if (date == null)
        {
            DateError.Text = "Выберите дату.";
            DateError.IsVisible = true;
            valid = false;
        }

        if (time == null)
        {
            TimeError.Text = "Укажите время.";
            TimeError.IsVisible = true;
            valid = false;
        }

        if (topic.Length < 3 || topic.Length > 200)
        {
            TopicError.Text = "Тема должна содержать от 3 до 200 символов.";
            TopicError.IsVisible = true;
            valid = false;
        }

        if (summary.Length < 10 || summary.Length > 1000)
        {
            SummaryError.Text = "Краткий итог должен содержать от 10 до 1000 символов.";
            SummaryError.IsVisible = true;
            valid = false;
        }

        if (!valid)
            return;

        DateTime meetingUtc = DateTime
            .SpecifyKind(date!.Value.Date + time!.Value, DateTimeKind.Local)
            .ToUniversalTime();

        int id = _editingId.Value;
        string? error = null;
        bool saved = false;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            var meeting = await db.Meetings
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM meetings
                                      WHERE id_meeting = {id}
                                      FOR UPDATE
                                      """)
                .SingleOrDefaultAsync();

            if (meeting == null)
            {
                error = "Встреча не найдена. Обновите список.";
            }
            else
            {
                DateTime now = DateTime.UtcNow;

                meeting.MeetingDate = meetingUtc;
                meeting.Topic = topic;
                meeting.Summary = summary;
                meeting.CoachComment = string.IsNullOrWhiteSpace(comment) ? null : comment;
                meeting.UpdatedBy = UserSession.IdUser;
                meeting.UpdatedAt = now;

                db.ActionLogs.Add(new ActionLog
                {
                    IdUser = UserSession.IdUser,
                    ActionType = "Изменение встречи",
                    ObjectType = "Встреча",
                    ObjectId = id,
                    OccurredAt = now,
                    IsSuccess = true,
                    Description = "Администратор изменил встречу №" + id + "."
                });

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                saved = true;
            }
        }
        catch (Exception)
        {
            error = "Не удалось сохранить встречу. Проверьте подключение к базе.";
        }
        finally
        {
            SetBusy(false);
        }

        if (saved)
        {
            _editingId = null;
            FormBorder.IsVisible = false;
            await SearchAsync();
            ShowMessage("Встреча сохранена.", false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
    }

    private async void ToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not Button { Tag: AdminMeetingRow row })
            return;

        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        bool archive = !row.IsArchived;

        bool confirmed = await AdminDialogs.ConfirmAsync(
            owner,
            archive ? "Архивировать встречу?" : "Восстановить встречу?",
            archive
                ? "Встреча «" + row.Topic + "» перестанет показываться студенту. Данные сохранятся."
                : "Встреча «" + row.Topic + "» снова будет видна студенту.",
            archive ? "Архивировать" : "Восстановить",
            archive);

        if (!confirmed)
            return;

        string? error = null;
        bool done = false;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            int id = row.IdMeeting;

            var meeting = await db.Meetings
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM meetings
                                      WHERE id_meeting = {id}
                                      FOR UPDATE
                                      """)
                .SingleOrDefaultAsync();

            if (meeting == null)
            {
                error = "Встреча не найдена. Обновите список.";
            }
            else if (meeting.IsArchived == archive)
            {
                error = "Статус уже изменён. Данные обновлены.";
            }
            else
            {
                DateTime now = DateTime.UtcNow;

                meeting.IsArchived = archive;
                meeting.UpdatedBy = UserSession.IdUser;
                meeting.UpdatedAt = now;

                db.ActionLogs.Add(new ActionLog
                {
                    IdUser = UserSession.IdUser,
                    ActionType = archive ? "Архивация встречи" : "Восстановление встречи",
                    ObjectType = "Встреча",
                    ObjectId = id,
                    OccurredAt = now,
                    IsSuccess = true,
                    Description = "Встреча №" + id
                });

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                done = true;
            }
        }
        catch (Exception)
        {
            error = "Не удалось изменить статус. Проверьте подключение к базе.";
        }
        finally
        {
            SetBusy(false);
        }

        if (done)
        {
            await SearchAsync();
            ShowMessage(archive ? "Встреча архивирована." : "Встреча восстановлена.", false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
            await SearchAsync();
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

public class AdminMeetingRow
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public int IdMeeting { get; set; }

    public DateTime MeetingDate { get; set; }

    public string Topic { get; set; } = "";

    public string Summary { get; set; } = "";

    public string? CoachComment { get; set; }

    public bool IsArchived { get; set; }

    public string CoachLogin { get; set; } = "";

    public string StudentLogin { get; set; } = "";

    public string HeaderText => MeetingDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm", RussianCulture) +
                                " · коуч " + CoachLogin + " · студент " + StudentLogin;

    public bool HasComment => !string.IsNullOrWhiteSpace(CoachComment);

    public string CommentText => "Комментарий: " + CoachComment;

    public string ToggleText => IsArchived ? "Восстановить" : "Архивировать";
}