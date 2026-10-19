using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class StudentView : UserControl
{
    private const int PageSize = 5;

    private bool _isLoading;
    private int _currentPage = 1;

    private List<StudentMeetingRow> _meetings = new();

    public StudentView()
    {
        InitializeComponent();

        Loaded += StudentView_Loaded;
    }

    private async void StudentView_Loaded(object? sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_isLoading)
        {
            return;
        }

        if (!UserSession.IsAuthorized || UserSession.IdRole != 1)
        {
            ReturnToLogin();
            return;
        }

        int studentId = UserSession.IdUser;

        _isLoading = true;
        RefreshButton.IsEnabled = false;
        DataContainer.IsVisible = false;

        ShowMessage("Загрузка данных…", false);

        try
        {
            await using var db = new MentoraContext();

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.RepeatableRead);

            var user = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.IdUser == studentId);

            if (user == null
                || user.IdRole != 1
                || user.IdUserStatus != 1)
            {
                if (IsCurrentStudent(studentId))
                {
                    ReturnToLogin();
                }

                return;
            }

            var profile = await db.Profiles
                .AsNoTracking()
                .Include(p => p.IdSpecialtyNavigation)
                .Include(p => p.IdSectorNavigation)
                .SingleOrDefaultAsync(p => p.IdUser == studentId);

            if (profile == null)
            {
                ShowMessage(
                    "Анкета не найдена. Обратитесь к администратору.");

                return;
            }

            var activeAssignment = await db.Assignments
                .AsNoTracking()
                .SingleOrDefaultAsync(a =>
                    a.IdStudent == studentId
                    && a.IdAssignmentStatus == 1);

            Profile? coachProfile = null;

            if (activeAssignment != null)
            {
                coachProfile = await db.Profiles
                    .AsNoTracking()
                    .Include(p => p.IdUserNavigation)
                    .Include(p => p.IdSpecialtyNavigation)
                    .Include(p => p.IdSectorNavigation)
                    .SingleOrDefaultAsync(p =>
                        p.IdUser == activeAssignment.IdCoach);

                if (coachProfile == null)
                {
                    ShowMessage(
                        "Не удалось найти анкету назначенного коуча. " +
                        "Обратитесь к администратору.");

                    return;
                }
            }

            var lastAssignment = await db.Assignments
                .AsNoTracking()
                .Where(a => a.IdStudent == studentId)
                .OrderByDescending(a => a.CreatedAt)
                .ThenByDescending(a => a.IdAssignment)
                .FirstOrDefaultAsync();

            var meetings = await db.Meetings
                .AsNoTracking()
                .Where(m =>
                    !m.IsArchived
                    && m.IdAssignmentNavigation.IdStudent == studentId)
                .OrderByDescending(m => m.MeetingDate)
                .ThenByDescending(m => m.IdMeeting)
                .Select(m => new StudentMeetingRow
                {
                    IdMeeting = m.IdMeeting,
                    MeetingDate = m.MeetingDate,
                    Topic = m.Topic,
                    Summary = m.Summary,

                    CoachLogin = m.IdAssignmentNavigation
                        .IdCoachNavigation
                        .IdUserNavigation
                        .Login,

                    AssignmentStatus = m.IdAssignmentNavigation
                        .IdAssignmentStatus,

                    FeedbackRating = db.MeetingFeedbacks
                        .Where(f => f.IdMeeting == m.IdMeeting)
                        .Select(f => (int?)f.Rating)
                        .FirstOrDefault(),

                    FeedbackComment = db.MeetingFeedbacks
                        .Where(f => f.IdMeeting == m.IdMeeting)
                        .Select(f => f.Comment)
                        .FirstOrDefault()
                })
                .ToListAsync();

            await transaction.CommitAsync();

            if (!IsCurrentStudent(studentId))
            {
                return;
            }

            UserSession.SetUser(user);

            HeaderNameText.Text = UserSession.DisplayName;
            GreetingText.Text =
                "Здравствуйте, " + UserSession.DisplayName + "!";

            LoginValue.Text = user.Login;
            CourseValue.Text = user.Course + " курс";
            SpecialtyValue.Text =
                profile.IdSpecialtyNavigation.Abbreviation;
            SectorValue.Text = profile.IdSectorNavigation.Name;
            GoalValue.Text = profile.AcademicGoal;
            InterestValue.Text = profile.Interest;

            bool hasCoach = activeAssignment != null;

            SelectionStatusText.Text = hasCoach
                ? "Коуч назначен"
                : "Свободен";

            NoCoachBorder.IsVisible = !hasCoach;
            CoachCard.IsVisible = hasCoach;

            if (coachProfile != null)
            {
                CoachLoginValue.Text =
                    coachProfile.IdUserNavigation.Login;

                CoachCourseValue.Text =
                    coachProfile.IdUserNavigation.Course + " курс";

                CoachSpecialtyValue.Text =
                    coachProfile.IdSpecialtyNavigation.Abbreviation;

                CoachSectorValue.Text =
                    coachProfile.IdSectorNavigation.Name;

                CoachGoalValue.Text = coachProfile.AcademicGoal;
                CoachInterestValue.Text = coachProfile.Interest;
            }

            SetProgramStatus(activeAssignment, lastAssignment);

            _meetings = meetings;
            _currentPage = 1;

            ShowMeetingsPage();

            MessageBorder.IsVisible = false;
            DataContainer.IsVisible = true;
        }
        catch (Exception)
        {
            if (IsCurrentStudent(studentId))
            {
                ShowMessage(
                    "Не удалось загрузить данные. " +
                    "Проверьте подключение к базе и нажмите «Обновить».");
            }
        }
        finally
        {
            _isLoading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void SetProgramStatus(Assignment? activeAssignment, Assignment? lastAssignment)
    {
        ProgramResultText.Text = "";
        ProgramResultText.IsVisible = false;

        if (activeAssignment != null)
        {
            ProgramStatusText.Text =
                "Вы участвуете в программе наставничества.";

            return;
        }

        if (lastAssignment == null)
        {
            ProgramStatusText.Text =
                "Ожидается назначение коуча.";

            return;
        }

        if (lastAssignment.IdAssignmentStatus == 2)
        {
            ProgramStatusText.Text =
                "Последняя программа завершена. " +
                "Вы доступны для нового назначения.";
        }
        else if (lastAssignment.IdAssignmentStatus == 3)
        {
            ProgramStatusText.Text =
                "Последнее назначение отменено. " +
                "Вы доступны для нового назначения.";
        }
        else
        {
            ProgramStatusText.Text = "Ожидается назначение коуча.";
        }

        if (!string.IsNullOrWhiteSpace(lastAssignment.ClosingReason))
        {
            ProgramResultText.Text =
                "Итог / причина: " + lastAssignment.ClosingReason;

            ProgramResultText.IsVisible = true;
        }
    }

    private void ShowMeetingsPage()
    {
        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(_meetings.Count / (double)PageSize));

        _currentPage = Math.Clamp(_currentPage, 1, totalPages);

        MeetingsList.ItemsSource = _meetings
            .Skip((_currentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        NoMeetingsText.IsVisible = _meetings.Count == 0;
        PaginationPanel.IsVisible = _meetings.Count > PageSize;

        PreviousButton.IsEnabled = _currentPage > 1;
        NextButton.IsEnabled = _currentPage < totalPages;

        PageNumberText.Text = _currentPage + " / " + totalPages;
    }

    private void PreviousButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            ShowMeetingsPage();
        }
    }

    private void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage * PageSize < _meetings.Count)
        {
            _currentPage++;
            ShowMeetingsPage();
        }
    }

    private async void LeaveFeedbackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: StudentMeetingRow row })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var window = new FeedbackWindow(row.IdMeeting, row.Topic);

        bool saved = await window.ShowDialog<bool>(owner);

        if (saved)
        {
            await LoadDataAsync();
        }
    }

    private void ProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection("profile");
    }

    private void MeetingsButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection("meetings");
    }

    private void CoachButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowSection("coach");
    }

    private void ShowSection(string section)
    {
        ProfilePanel.IsVisible = section == "profile";
        MeetingsPanel.IsVisible = section == "meetings";
        CoachPanel.IsVisible = section == "coach";

        ProfileButton.Classes.Remove("selected");
        MeetingsButton.Classes.Remove("selected");
        CoachButton.Classes.Remove("selected");

        switch (section)
        {
            case "profile":
                PageTitleText.Text = "Моя анкета";
                ProfileButton.Classes.Add("selected");
                break;

            case "meetings":
                PageTitleText.Text = "Журнал встреч";
                MeetingsButton.Classes.Add("selected");
                break;

            case "coach":
                PageTitleText.Text = "Коуч";
                CoachButton.Classes.Add("selected");
                break;
        }
    }

    private bool IsCurrentStudent(int studentId)
    {
        return UserSession.IsAuthorized
               && UserSession.IdUser == studentId
               && UserSession.IdRole == 1
               && TopLevel.GetTopLevel(this) is MainWindow;
    }

    private void LogoutButton_Click(object? sender, RoutedEventArgs e)
    {
        ReturnToLogin();
    }

    private void ReturnToLogin()
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.ShowLogin();
        }
    }

    private void ShowMessage(string message, bool isError = true)
    {
        MessageText.Text = message;

        MessageText.Foreground = Brush.Parse(
            isError ? "#B52C2C" : "#747C90");

        MessageBorder.IsVisible = true;
    }

    private async void FindCoachButton_Click(
    object? sender,
    RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var window = new StudentCoachMatchingWindow();

        await window.ShowDialog(owner);

        await LoadDataAsync();
    }
}

public class StudentMeetingRow
{
    private static readonly CultureInfo RussianCulture =
        CultureInfo.GetCultureInfo("ru-RU");

    public int IdMeeting { get; set; }

    public DateTime MeetingDate { get; set; }

    public string Topic { get; set; } = "";

    public string Summary { get; set; } = "";

    public string CoachLogin { get; set; } = "";

    public int AssignmentStatus { get; set; }

    public int? FeedbackRating { get; set; }

    public string? FeedbackComment { get; set; }

    public bool HasFeedback => FeedbackRating != null;

    public bool CanLeaveFeedback =>
        FeedbackRating == null && MeetingDate <= DateTime.UtcNow;

    public string FeedbackText
    {
        get
        {
            if (FeedbackRating == null)
            {
                return "";
            }

            int rating = Math.Clamp(FeedbackRating.Value, 0, 5);

            string stars = new string('★', rating) + new string('☆', 5 - rating);

            return string.IsNullOrWhiteSpace(FeedbackComment)
                ? "Ваш отзыв: " + stars
                : "Ваш отзыв: " + stars + " — " + FeedbackComment;
        }
    }

    public string MonthText => MeetingDate
        .ToLocalTime()
        .ToString("MMM", RussianCulture)
        .TrimEnd('.')
        .ToUpper(RussianCulture);

    public string DayText => MeetingDate
        .ToLocalTime()
        .ToString("dd");

    public string Details
    {
        get
        {
            string status = AssignmentStatus switch
            {
                1 => "Активное назначение",
                2 => "Программа завершена",
                3 => "Назначение отменено",
                _ => ""
            };

            string date = MeetingDate
                .ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm", RussianCulture);

            return date
                   + " · Коуч: " + CoachLogin
                   + " · " + status;
        }
    }
}