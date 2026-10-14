using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using mentora.Models;

namespace mentora.Views;

public partial class AdminAssignmentWindow : Window
{
    private static readonly CultureInfo RussianCulture =
        CultureInfo.GetCultureInfo("ru-RU");

    private readonly int _assignmentId;

    private bool _isBusy;
    private bool _loaded;
    private string _studentLogin = "";
    private string _coachLogin = "";

    public AdminAssignmentWindow() : this(0)
    {
    }

    public AdminAssignmentWindow(int assignmentId)
    {
        InitializeComponent();

        _assignmentId = assignmentId;

        TitleText.Text = "Назначение №" + assignmentId;

        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_isBusy)
            return;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                DenyAccess();
                return;
            }

            var a = await db.Assignments
                .AsNoTracking()
                .Where(x => x.IdAssignment == _assignmentId)
                .Select(x => new
                {
                    x.IdAssignmentStatus,
                    StatusName = x.IdAssignmentStatusNavigation.Name,
                    x.CreatedAt,
                    x.ClosedAt,
                    x.ClosingReason,
                    CreatedByLogin = x.CreatedByNavigation.Login,
                    ClosedByLogin = x.ClosedByNavigation != null
                        ? x.ClosedByNavigation.Login
                        : null,

                    StudentLogin = x.IdStudentNavigation.IdUserNavigation.Login,
                    StudentCourse = x.IdStudentNavigation.IdUserNavigation.Course,
                    StudentSpecialty = x.IdStudentNavigation.IdSpecialtyNavigation.Abbreviation,
                    StudentSector = x.IdStudentNavigation.IdSectorNavigation.Name,
                    StudentGoal = x.IdStudentNavigation.AcademicGoal,
                    StudentInterest = x.IdStudentNavigation.Interest,

                    CoachLogin = x.IdCoachNavigation.IdUserNavigation.Login,
                    CoachCourse = x.IdCoachNavigation.IdUserNavigation.Course,
                    CoachSpecialty = x.IdCoachNavigation.IdSpecialtyNavigation.Abbreviation,
                    CoachSector = x.IdCoachNavigation.IdSectorNavigation.Name,
                    CoachGoal = x.IdCoachNavigation.AcademicGoal,
                    CoachInterest = x.IdCoachNavigation.Interest
                })
                .SingleOrDefaultAsync();

            if (a == null)
            {
                ShowMessage("Назначение не найдено.", true);
                ActionsBorder.IsVisible = false;
                return;
            }

            int meetingsTotal = await db.Meetings.CountAsync(m =>
                m.IdAssignment == _assignmentId);

            var meetings = await db.Meetings
                .AsNoTracking()
                .Where(m => m.IdAssignment == _assignmentId)
                .OrderByDescending(m => m.MeetingDate)
                .Take(20)
                .Select(m => new
                {
                    m.MeetingDate,
                    m.Topic,
                    m.Summary,
                    m.IsArchived
                })
                .ToListAsync();

            _studentLogin = a.StudentLogin;
            _coachLogin = a.CoachLogin;

            StatusValue.Text = a.StatusName;
            StatusValue.Foreground = StatusBrush(a.IdAssignmentStatus);

            CreatedValue.Text = Format(a.CreatedAt);
            CreatedByValue.Text = a.CreatedByLogin;

            ClosedPanel.IsVisible = a.IdAssignmentStatus != 1;

            if (a.IdAssignmentStatus != 1)
            {
                ClosedValue.Text = a.ClosedAt == null ? "—" : Format(a.ClosedAt.Value);
                ClosedByValue.Text = a.ClosedByLogin ?? "—";

                ReasonLabel.Text = a.IdAssignmentStatus == 2
                    ? "Итог работы"
                    : "Причина отмены";

                ReasonValue.Text = string.IsNullOrWhiteSpace(a.ClosingReason)
                    ? "—"
                    : a.ClosingReason;
            }

            StudentLoginValue.Text = a.StudentLogin;
            StudentCourseValue.Text = a.StudentCourse + " курс";
            StudentProfileValue.Text = a.StudentSpecialty + " · " + a.StudentSector;
            StudentGoalValue.Text = a.StudentGoal;
            StudentInterestValue.Text = a.StudentInterest;

            CoachLoginValue.Text = a.CoachLogin;
            CoachCourseValue.Text = a.CoachCourse + " курс";
            CoachProfileValue.Text = a.CoachSpecialty + " · " + a.CoachSector;
            CoachGoalValue.Text = a.CoachGoal;
            CoachInterestValue.Text = a.CoachInterest;

            MeetingsCountText.Text = meetingsTotal > 20
                ? "Всего встреч: " + meetingsTotal + ". Показаны последние 20."
                : "Всего встреч: " + meetingsTotal;

            NoMeetingsText.IsVisible = meetingsTotal == 0;

            MeetingsList.ItemsSource = meetings
                .Select(m => new AdminAssignmentMeetingRow
                {
                    DateText = Format(m.MeetingDate),
                    Topic = m.Topic,
                    Summary = m.Summary,
                    IsArchived = m.IsArchived
                })
                .ToList();

            ActionsBorder.IsVisible = a.IdAssignmentStatus == 1;

            _loaded = true;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить данные. " +
                "Проверьте подключение к базе и откройте карточку заново.",
                true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CompleteButton_Click(object? sender, RoutedEventArgs e)
    {
        await CloseAssignmentAsync(2);
    }

    private async void CancelAssignmentButton_Click(object? sender, RoutedEventArgs e)
    {
        await CloseAssignmentAsync(3);
    }

    private async Task CloseAssignmentAsync(int newStatus)
    {
        if (_isBusy || !_loaded)
            return;

        bool complete = newStatus == 2;

        string? reason = await AdminDialogs.AskReasonAsync(
            this,
            complete ? "Завершить назначение?" : "Отменить назначение?",
            complete
                ? "Укажите итог работы пары. Студент и коуч снова станут " +
                  "свободными, история сохранится."
                : "Укажите причину отмены. Студент снова станет свободным, " +
                  "а коуч сможет выбрать другого студента.",
            complete ? "Завершить" : "Подтвердить отмену",
            !complete);

        if (reason == null)
            return;

        bool done = false;
        string? error = null;

        SetBusy(true);

        try
        {
            await CloseCoreAsync(newStatus, reason);
            done = true;
        }
        catch (UnauthorizedAccessException)
        {
            DenyAccess();
            return;
        }
        catch (WindowException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось выполнить действие. " +
                "Проверьте подключение к базе и повторите попытку.";
        }
        finally
        {
            SetBusy(false);
        }

        if (done)
        {
            await LoadAsync();

            ShowMessage(
                complete ? "Назначение завершено." : "Назначение отменено.",
                false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
    }

    private async Task CloseCoreAsync(int newStatus, string reason)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        var assignment = await db.Assignments
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM assignments
                                  WHERE id_assignment = {_assignmentId}
                                  FOR UPDATE
                                  """)
            .SingleOrDefaultAsync();

        if (assignment == null)
            throw new WindowException("Назначение не найдено.");

        if (assignment.IdAssignmentStatus != 1)
            throw new WindowException("Назначение уже не активно. Данные обновлены.");

        DateTime now = DateTime.UtcNow;
        int adminId = UserSession.IdUser;

        assignment.IdAssignmentStatus = newStatus;
        assignment.ClosedAt = now;
        assignment.ClosedBy = adminId;
        assignment.ClosingReason = reason;
        assignment.UpdatedAt = now;

        db.ActionLogs.Add(new ActionLog
        {
            IdUser = adminId,
            ActionType = newStatus == 2
                ? "Завершение назначения"
                : "Отмена назначения",
            ObjectType = "Назначение",
            ObjectId = _assignmentId,
            OccurredAt = now,
            IsSuccess = true,
            Description = "Коуч " + _coachLogin + ", студент " + _studentLogin +
                          ". " + (newStatus == 2 ? "Итог: " : "Причина: ") + reason
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async void TransferButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || !_loaded)
            return;

        var window = new AdminTransferWindow(_studentLogin, _coachLogin);

        bool confirmed = await window.ShowDialog<bool>(this);

        if (!confirmed)
            return;

        int newCoachId = window.NewCoachId;
        string reason = window.Reason;

        int newAssignmentId = 0;
        string newCoachLogin = "";
        string? error = null;

        SetBusy(true);

        try
        {
            var result = await TransferCoreAsync(newCoachId, reason);

            newAssignmentId = result.NewId;
            newCoachLogin = result.CoachLogin;
        }
        catch (UnauthorizedAccessException)
        {
            DenyAccess();
            return;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: "23505",
                ConstraintName: "ux_assignments_active_coach"
                          or "ux_assignments_active_student"
            })
        {
            error =
                "У выбранного коуча или студента уже есть активное назначение. " +
                "Перенос отменён, данные не изменены.";
        }
        catch (WindowException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось выполнить перенос. Изменения отменены. " +
                "Проверьте подключение к базе и повторите попытку.";
        }
        finally
        {
            SetBusy(false);
        }

        if (newAssignmentId != 0)
        {
            await LoadAsync();

            ShowMessage(
                "Студент перенесён к коучу «" + newCoachLogin + "». " +
                "Это назначение закрыто как отменённое, создано новое " +
                "назначение №" + newAssignmentId + ".",
                false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
    }

    private async Task<(int NewId, string CoachLogin)> TransferCoreAsync(
        int newCoachId,
        string reason)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        var assignment = await db.Assignments
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM assignments
                                  WHERE id_assignment = {_assignmentId}
                                  FOR UPDATE
                                  """)
            .SingleOrDefaultAsync();

        if (assignment == null)
            throw new WindowException("Назначение не найдено.");

        if (assignment.IdAssignmentStatus != 1)
            throw new WindowException("Назначение уже не активно. Данные обновлены.");

        if (assignment.IdCoach == newCoachId)
            throw new WindowException("Выберите другого коуча.");

        int oldCoachId = assignment.IdCoach;
        int studentId = assignment.IdStudent;

        var users = await db.Users
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM users
                                  WHERE id_user IN ({oldCoachId}, {newCoachId}, {studentId})
                                  ORDER BY id_user
                                  FOR UPDATE
                                  """)
            .ToListAsync();

        var newCoach = users.SingleOrDefault(u => u.IdUser == newCoachId);
        var student = users.SingleOrDefault(u => u.IdUser == studentId);
        var oldCoach = users.SingleOrDefault(u => u.IdUser == oldCoachId);

        if (newCoach == null
            || newCoach.IdRole != 2
            || newCoach.IdUserStatus != 1
            || newCoach.Course == null
            || newCoach.Course < 2
            || newCoach.Course > 4)
        {
            throw new WindowException(
                "Выбранный коуч больше не подходит для назначения. Данные не изменены.");
        }

        if (student == null
            || student.IdRole != 1
            || student.IdUserStatus != 1
            || student.Course != 1)
        {
            throw new WindowException(
                "Студент не подходит для нового назначения. Данные не изменены.");
        }

        bool hasProfile = await db.Profiles.AnyAsync(p => p.IdUser == newCoachId);

        if (!hasProfile)
            throw new WindowException("У выбранного коуча нет анкеты.");

        bool newCoachBusy = await db.Assignments.AnyAsync(a =>
            a.IdAssignmentStatus == 1 && a.IdCoach == newCoachId);

        if (newCoachBusy)
        {
            throw new WindowException(
                "У выбранного коуча уже есть активное назначение. Данные не изменены.");
        }

        DateTime now = DateTime.UtcNow;
        int adminId = UserSession.IdUser;

        assignment.IdAssignmentStatus = 3;
        assignment.ClosedAt = now;
        assignment.ClosedBy = adminId;
        assignment.ClosingReason = "Перенос к коучу " + newCoach.Login + ". " + reason;
        assignment.UpdatedAt = now;

        await db.SaveChangesAsync();

        var newAssignment = new Assignment
        {
            RequestId = Guid.NewGuid(),
            IdCoach = newCoachId,
            IdStudent = studentId,
            IdAssignmentStatus = 1,
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Assignments.Add(newAssignment);
        await db.SaveChangesAsync();

        db.ActionLogs.Add(new ActionLog
        {
            IdUser = adminId,
            ActionType = "Перенос назначения",
            ObjectType = "Назначение",
            ObjectId = newAssignment.IdAssignment,
            OccurredAt = now,
            IsSuccess = true,
            Description = "Студент " + student.Login + ": коуч " +
                          (oldCoach?.Login ?? "?") + " ? " + newCoach.Login +
                          ". Прежнее назначение №" + _assignmentId +
                          ". Причина: " + reason
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return (newAssignment.IdAssignment, newCoach.Login);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DenyAccess()
    {
        if (Owner is MainWindow mainWindow)
            mainWindow.ShowLogin();

        Close();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        ContentPanel.IsEnabled = !isBusy;
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = Brush.Parse(isError ? "#B52C2C" : "#278450");
        MessageBorder.IsVisible = true;
        MessageBorder.BringIntoView();
    }

    private static string Format(DateTime value)
    {
        return value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", RussianCulture);
    }

    private static IBrush StatusBrush(int statusId)
    {
        return statusId switch
        {
            1 => Brush.Parse("#278450"),
            2 => Brush.Parse("#39446B"),
            3 => Brush.Parse("#B52C2C"),
            _ => Brush.Parse("#747C90")
        };
    }

    private sealed class WindowException : Exception
    {
        public WindowException(string message)
            : base(message)
        {
        }
    }
}

public class AdminAssignmentMeetingRow
{
    public string DateText { get; set; } = "";

    public string Topic { get; set; } = "";

    public string Summary { get; set; } = "";

    public bool IsArchived { get; set; }
}