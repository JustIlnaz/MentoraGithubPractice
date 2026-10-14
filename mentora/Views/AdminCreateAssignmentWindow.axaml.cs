using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using mentora.Models;

namespace mentora.Views;

public partial class AdminCreateAssignmentWindow : Window
{
    private readonly Guid _requestId = Guid.NewGuid();

    private bool _isBusy;
    private int _studentTyping;
    private int _coachTyping;
    private int _studentVersion;
    private int _coachVersion;

    public AdminCreateAssignmentWindow()
    {
        InitializeComponent();

        Opened += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            await using var db = new MentoraContext();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                DenyAccess();
                return;
            }
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось проверить доступ. Проверьте подключение к базе.",
                true);

            return;
        }

        await ReloadStudentsAsync();
        await ReloadCoachesAsync();
    }

    private async void StudentSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        int typing = ++_studentTyping;

        await Task.Delay(300);

        if (typing != _studentTyping)
            return;

        await ReloadStudentsAsync();
    }

    private async void CoachSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        int typing = ++_coachTyping;

        await Task.Delay(300);

        if (typing != _coachTyping)
            return;

        await ReloadCoachesAsync();
    }

    private async Task ReloadStudentsAsync()
    {
        int version = ++_studentVersion;
        int? selectedId = (StudentComboBox.SelectedItem as Candidate)?.Id;

        try
        {
            var items = await LoadCandidatesAsync(1, StudentSearchBox.Text?.Trim() ?? "");

            if (version != _studentVersion)
                return;

            StudentComboBox.ItemsSource = items;
            StudentComboBox.SelectedItem = items.FirstOrDefault(c => c.Id == selectedId);
            StudentEmptyText.IsVisible = items.Count == 0;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить студентов. Проверьте подключение к базе.",
                true);
        }
    }

    private async Task ReloadCoachesAsync()
    {
        int version = ++_coachVersion;
        int? selectedId = (CoachComboBox.SelectedItem as Candidate)?.Id;

        try
        {
            var items = await LoadCandidatesAsync(2, CoachSearchBox.Text?.Trim() ?? "");

            if (version != _coachVersion)
                return;

            CoachComboBox.ItemsSource = items;
            CoachComboBox.SelectedItem = items.FirstOrDefault(c => c.Id == selectedId);
            CoachEmptyText.IsVisible = items.Count == 0;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить коучей. Проверьте подключение к базе.",
                true);
        }
    }

    private static async Task<List<Candidate>> LoadCandidatesAsync(int role, string search)
    {
        await using var db = new MentoraContext();

        IQueryable<User> query = db.Users
            .AsNoTracking()
            .Where(u =>
                u.IdRole == role
                && u.IdUserStatus == 1
                && u.Profile != null
                && !db.Assignments.Any(a =>
                    a.IdAssignmentStatus == 1
                    && (a.IdStudent == u.IdUser || a.IdCoach == u.IdUser)));

        if (role == 1)
            query = query.Where(u => u.Course == 1);
        else
            query = query.Where(u => u.Course >= 2 && u.Course <= 4);

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = "%" + EscapeLike(search) + "%";

            query = query.Where(u =>
                EF.Functions.ILike(u.Login, pattern, "\\"));
        }

        var rows = await query
            .OrderBy(u => u.Login)
            .Take(50)
            .Select(u => new
            {
                u.IdUser,
                u.Login,
                u.Course,
                Specialty = u.Profile != null
                    ? u.Profile.IdSpecialtyNavigation.Abbreviation
                    : "",
                Sector = u.Profile != null
                    ? u.Profile.IdSectorNavigation.Name
                    : ""
            })
            .ToListAsync();

        return rows
            .Select(r => new Candidate(
                r.IdUser,
                r.Login,
                r.Login + " · " + r.Course + " курс · " + r.Specialty + " · " + r.Sector))
            .ToList();
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        StudentError.IsVisible = false;
        CoachError.IsVisible = false;
        MessageBorder.IsVisible = false;

        var student = StudentComboBox.SelectedItem as Candidate;
        var coach = CoachComboBox.SelectedItem as Candidate;

        bool valid = true;

        if (student == null)
        {
            StudentError.Text = "Выберите студента.";
            StudentError.IsVisible = true;
            valid = false;
        }

        if (coach == null)
        {
            CoachError.Text = "Выберите коуча.";
            CoachError.IsVisible = true;
            valid = false;
        }

        if (!valid)
            return;

        bool confirmed = await AdminDialogs.ConfirmAsync(
            this,
            "Создать назначение?",
            "Коуч «" + coach!.Login + "» будет закреплён за студентом «" +
            student!.Login + "».",
            "Создать",
            false);

        if (!confirmed)
            return;

        int coachId = coach.Id;
        int studentId = student.Id;

        bool done = false;
        string? error = null;

        SetBusy(true);

        try
        {
            await CreateCoreAsync(coachId, studentId);
            done = true;
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
                ConstraintName: "assignments_request_id_key"
            })
        {
            done = true;
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
                "У коуча или студента уже есть активное назначение. " +
                "Списки обновлены.";
        }
        catch (WindowException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось создать назначение. " +
                "Проверьте подключение к базе и повторите попытку.";
        }
        finally
        {
            SetBusy(false);
        }

        if (done)
        {
            Close(true);
            return;
        }

        if (error != null)
        {
            ShowMessage(error, true);
            await ReloadStudentsAsync();
            await ReloadCoachesAsync();
        }
    }

    private async Task CreateCoreAsync(int coachId, int studentId)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        var users = await db.Users
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM users
                                  WHERE id_user IN ({coachId}, {studentId})
                                  ORDER BY id_user
                                  FOR UPDATE
                                  """)
            .ToListAsync();

        var coach = users.SingleOrDefault(u => u.IdUser == coachId);
        var student = users.SingleOrDefault(u => u.IdUser == studentId);

        if (coach == null
            || coach.IdRole != 2
            || coach.IdUserStatus != 1
            || coach.Course == null
            || coach.Course < 2
            || coach.Course > 4)
        {
            throw new WindowException(
                "Коуч больше не подходит для назначения. Списки обновлены.");
        }

        if (student == null
            || student.IdRole != 1
            || student.IdUserStatus != 1
            || student.Course != 1)
        {
            throw new WindowException(
                "Студент больше не подходит для назначения. Списки обновлены.");
        }

        int profiles = await db.Profiles.CountAsync(p =>
            p.IdUser == coachId || p.IdUser == studentId);

        if (profiles != 2)
            throw new WindowException("У коуча или студента нет анкеты.");

        bool coachBusy = await db.Assignments.AnyAsync(a =>
            a.IdAssignmentStatus == 1 && a.IdCoach == coachId);

        if (coachBusy)
        {
            throw new WindowException(
                "У коуча уже есть активное назначение. Списки обновлены.");
        }

        bool studentBusy = await db.Assignments.AnyAsync(a =>
            a.IdAssignmentStatus == 1 && a.IdStudent == studentId);

        if (studentBusy)
        {
            throw new WindowException(
                "У студента уже есть активное назначение. Списки обновлены.");
        }

        DateTime now = DateTime.UtcNow;

        var assignment = new Assignment
        {
            RequestId = _requestId,
            IdCoach = coachId,
            IdStudent = studentId,
            IdAssignmentStatus = 1,
            CreatedBy = UserSession.IdUser,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        db.ActionLogs.Add(new ActionLog
        {
            IdUser = UserSession.IdUser,
            ActionType = "Создание назначения",
            ObjectType = "Назначение",
            ObjectId = assignment.IdAssignment,
            OccurredAt = now,
            IsSuccess = true,
            Description = "Администратор вручную назначил коуча " + coach.Login +
                          " студенту " + student.Login + "."
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        Close(false);
    }

    private void DenyAccess()
    {
        if (Owner is MainWindow mainWindow)
            mainWindow.ShowLogin();

        Close(false);
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
    }

    private sealed class Candidate
    {
        public Candidate(int id, string login, string text)
        {
            Id = id;
            Login = login;
            Text = text;
        }

        public int Id { get; }

        public string Login { get; }

        public string Text { get; }

        public override string ToString() => Text;
    }

    private sealed class WindowException : Exception
    {
        public WindowException(string message)
            : base(message)
        {
        }
    }
}