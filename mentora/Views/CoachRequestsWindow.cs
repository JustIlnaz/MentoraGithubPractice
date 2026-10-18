using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using mentora.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace mentora.Views;

public sealed class CoachRequestsWindow : Window
{
    private readonly StackPanel _requestsPanel = new();
    private readonly TextBlock _statusText = new();
    private readonly ScrollViewer _scrollViewer;

    private bool _busy;

    public CoachRequestsWindow()
    {
        Title = "Заявки студентов";
        Width = 820;
        Height = 700;
        MinWidth = 620;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F3F4FF");

        var root = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };

        root.Children.Add(new TextBlock
        {
            Text = "Заявки студентов",
            FontSize = 25,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D")
        });

        _statusText.FontSize = 14;
        _statusText.Foreground = Brush.Parse("#39446B");
        _statusText.TextWrapping = TextWrapping.Wrap;

        root.Children.Add(_statusText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };

        var refreshButton = CreateButton("Обновить");
        refreshButton.Click += async (_, _) =>
            await LoadRequestsAsync();

        var closeButton = CreateButton("Закрыть");
        closeButton.Click += (_, _) => Close();

        actions.Children.Add(refreshButton);
        actions.Children.Add(closeButton);

        root.Children.Add(actions);
        root.Children.Add(_requestsPanel);

        _scrollViewer = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        Content = _scrollViewer;

        Opened += async (_, _) => await LoadRequestsAsync();
    }

    private async Task LoadRequestsAsync()
    {
        _requestsPanel.Children.Clear();

        if (!UserSession.IsAuthorized || UserSession.IdRole != 2)
        {
            _statusText.Text = "Необходимо авторизоваться как коуч.";
            return;
        }

        int coachId = UserSession.IdUser;

        try
        {
            await using var db = new MentoraContext();

            var coach = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u =>
                    u.IdUser == coachId
                    && u.IdRole == 2
                    && u.IdUserStatus == 1);

            if (coach == null)
            {
                _statusText.Text = "Профиль коуча недоступен.";
                return;
            }

            bool coachBusy = await db.Assignments.AnyAsync(a =>
                a.IdCoach == coachId
                && a.IdAssignmentStatus == 1);

            var requests = await db.CoachRequests
                .AsNoTracking()
                .Where(r =>
                    r.IdCoach == coachId
                    && r.Status == CoachRequest.Pending)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();

            if (requests.Count == 0)
            {
                _statusText.Text = "Новых заявок нет.";
                return;
            }

            _statusText.Text = coachBusy
                ? "У вас уже есть активный студент. Принимать новые заявки нельзя."
                : $"Входящих заявок: {requests.Count}";

            var studentIds = requests
                .Select(r => r.IdStudent)
                .Distinct()
                .ToArray();

            var studentProfiles = await db.Profiles
                .AsNoTracking()
                .Include(p => p.IdUserNavigation)
                .Include(p => p.IdSpecialtyNavigation)
                .Include(p => p.IdSectorNavigation)
                .Where(p => studentIds.Contains(p.IdUser))
                .ToListAsync();

            foreach (var request in requests)
            {
                var profile = studentProfiles
                    .SingleOrDefault(p => p.IdUser == request.IdStudent);

                if (profile == null)
                    continue;

                _requestsPanel.Children.Add(
                    CreateRequestCard(request.IdCoachRequest, profile, coachBusy));
            }
        }
        catch (Exception)
        {
            _statusText.Text =
                "Не удалось загрузить заявки. Проверьте подключение к базе.";
        }
    }

    private Border CreateRequestCard(
        int requestId,
        Profile student,
        bool coachBusy)
    {
        var content = new StackPanel
        {
            Spacing = 8
        };

        content.Children.Add(new TextBlock
        {
            Text = student.IdUserNavigation.FullName
                ?? student.IdUserNavigation.Login,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D")
        });

        content.Children.Add(CreateText(
            $"Логин: {student.IdUserNavigation.Login}"));

        content.Children.Add(CreateText(
            $"Курс: {student.IdUserNavigation.Course}"));

        content.Children.Add(CreateText(
            $"Специальность: {student.IdSpecialtyNavigation.Abbreviation}"));

        content.Children.Add(CreateText(
            $"Сектор: {student.IdSectorNavigation.Name}"));

        content.Children.Add(CreateText(
            "Академическая цель: " + student.AcademicGoal));

        content.Children.Add(CreateText(
            "Интерес: " + student.Interest));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var acceptButton = CreateButton("Принять");
        acceptButton.IsEnabled = !coachBusy;
        acceptButton.Click += async (_, _) =>
            await DecideRequestAsync(requestId, true);

        var rejectButton = CreateButton("Отклонить");
        rejectButton.Background = Brush.Parse("#E8EAF5");
        rejectButton.Foreground = Brush.Parse("#19234D");
        rejectButton.Click += async (_, _) =>
            await DecideRequestAsync(requestId, false);

        actions.Children.Add(acceptButton);
        actions.Children.Add(rejectButton);

        content.Children.Add(actions);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#E7E9F5"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content
        };
    }

    private async Task DecideRequestAsync(
        int requestId,
        bool accept)
    {
        if (_busy)
            return;

        _busy = true;
        _scrollViewer.IsEnabled = false;

        int coachId = UserSession.IdUser;

        string resultMessage = "";

        try
        {
            await using var db = new MentoraContext();

            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var request = await db.CoachRequests
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM coach_requests
                    WHERE id_coach_request = {requestId}
                      AND id_coach = {coachId}
                      AND status = {CoachRequest.Pending}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync();

            if (request == null)
            {
                throw new RequestDecisionException(
                    "Эта заявка уже обработана. Обновите список.");
            }

            DateTime now = DateTime.UtcNow;

            if (accept)
            {
                var users = await db.Users
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM users
                        WHERE id_user IN ({coachId}, {request.IdStudent})
                        ORDER BY id_user
                        FOR UPDATE
                        """)
                    .ToListAsync();

                var coach = users.SingleOrDefault(u =>
                    u.IdUser == coachId);

                var student = users.SingleOrDefault(u =>
                    u.IdUser == request.IdStudent);

                if (coach == null
                    || coach.IdRole != 2
                    || coach.IdUserStatus != 1
                    || coach.Course is not (2 or 3 or 4))
                {
                    throw new RequestDecisionException(
                        "Профиль коуча недоступен.");
                }

                if (student == null
                    || student.IdRole != 1
                    || student.IdUserStatus != 1)
                {
                    throw new RequestDecisionException(
                        "Профиль студента недоступен.");
                }

                bool coachBusy = await db.Assignments.AnyAsync(a =>
                    a.IdCoach == coachId
                    && a.IdAssignmentStatus == 1);

                bool studentBusy = await db.Assignments.AnyAsync(a =>
                    a.IdStudent == request.IdStudent
                    && a.IdAssignmentStatus == 1);

                if (coachBusy)
                {
                    throw new RequestDecisionException(
                        "У вас уже есть активный студент.");
                }

                if (studentBusy)
                {
                    throw new RequestDecisionException(
                        "Студент уже назначен другому коучу.");
                }

                var settings = await db.ProgramSettings
                    .AsNoTracking()
                    .SingleAsync(s => s.IdSetting == 1);

                var coachProfile = await db.Profiles
                    .AsNoTracking()
                    .SingleAsync(p => p.IdUser == coachId);

                var studentProfile = await db.Profiles
                    .AsNoTracking()
                    .SingleAsync(p => p.IdUser == request.IdStudent);

                if (settings.RequireSameSpecialty
                    && coachProfile.IdSpecialty != studentProfile.IdSpecialty)
                {
                    throw new RequestDecisionException(
                        "Текущие настройки программы не позволяют принять эту заявку.");
                }

                db.Assignments.Add(new Assignment
                {
                    RequestId = Guid.NewGuid(),
                    IdCoach = coachId,
                    IdStudent = request.IdStudent,
                    IdAssignmentStatus = 1,
                    CreatedBy = coachId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                request.Status = CoachRequest.Accepted;

                var otherRequests = await db.CoachRequests
                    .Where(r =>
                        r.IdCoach == coachId
                        && r.Status == CoachRequest.Pending
                        && r.IdCoachRequest != request.IdCoachRequest)
                    .ToListAsync();

                foreach (var other in otherRequests)
                {
                    other.Status = CoachRequest.Rejected;
                    other.DecidedAt = now;
                    other.DecidedBy = coachId;
                }
            }
            else
            {
                request.Status = CoachRequest.Rejected;
            }

            request.DecidedAt = now;
            request.DecidedBy = coachId;

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = coachId,
                ActionType = accept
                    ? "Принятие запроса коучу"
                    : "Отклонение запроса коучу",
                ObjectType = "Запрос коучу",
                ObjectId = request.IdCoachRequest,
                OccurredAt = now,
                IsSuccess = true,
                Description =
                    $"Коуч обработал запрос студента с ID {request.IdStudent}."
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            resultMessage = accept
                ? "Запрос принят. Студент закреплён за вами."
                : "Запрос отклонён.";
        }
        catch (RequestDecisionException ex)
        {
            resultMessage = ex.Message;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: "23505"
            })
        {
            resultMessage =
                "Назначение изменилось. Обновите данные и попробуйте снова.";
        }
        catch (Exception)
        {
            resultMessage =
                "Не удалось обработать запрос. Проверьте подключение к базе.";
        }
        finally
        {
            _busy = false;
            _scrollViewer.IsEnabled = true;

            await LoadRequestsAsync();
        }

        _statusText.Text = resultMessage;
    }

    private static TextBlock CreateText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = Brush.Parse("#39446B"),
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Content = text,
            Padding = new Thickness(14, 8),
            Background = Brush.Parse("#5B4BFF"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };
    }

    private sealed class RequestDecisionException : Exception
    {
        public RequestDecisionException(string message)
            : base(message)
        {
        }
    }
}