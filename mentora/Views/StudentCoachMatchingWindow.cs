using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using mentora.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace mentora.Views;

public sealed class StudentCoachMatchingWindow : Window
{
    private readonly StackPanel _recommendations = new();
    private readonly TextBlock _statusText = new();

    private bool _busy;

    public StudentCoachMatchingWindow()
    {
        Title = "Подбор коуча";
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
            Text = "Подбор коуча",
            FontSize = 25,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D")
        });

        root.Children.Add(new TextBlock
        {
            Text = "Рекомендации сформированы на основе вашей анкеты.",
            FontSize = 14,
            Foreground = Brush.Parse("#747C90"),
            TextWrapping = TextWrapping.Wrap
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
            await LoadRecommendationsAsync();

        var closeButton = CreateButton("Закрыть");
        closeButton.Click += (_, _) => Close();

        actions.Children.Add(refreshButton);
        actions.Children.Add(closeButton);

        root.Children.Add(actions);
        root.Children.Add(_recommendations);

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        Opened += async (_, _) => await LoadRecommendationsAsync();
    }

    private async Task LoadRecommendationsAsync()
    {
        _recommendations.Children.Clear();

        if (!UserSession.IsAuthorized || UserSession.IdRole != 1)
        {
            _statusText.Text = "Необходимо авторизоваться как студент.";
            return;
        }

        int studentId = UserSession.IdUser;

        _statusText.Text = "Подбираем коучей...";

        try
        {
            await using var db = new MentoraContext();

            var student = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u =>
                    u.IdUser == studentId
                    && u.IdRole == 1
                    && u.IdUserStatus == 1);

            if (student == null)
            {
                _statusText.Text = "Профиль студента недоступен.";
                return;
            }

            bool hasCoach = await db.Assignments.AnyAsync(a =>
                a.IdStudent == studentId
                && a.IdAssignmentStatus == 1);

            if (hasCoach)
            {
                _statusText.Text =
                    "У вас уже есть назначенный коуч.";

                return;
            }

            var studentProfile = await db.Profiles
                .AsNoTracking()
                .Include(p => p.IdSpecialtyNavigation)
                .Include(p => p.IdSectorNavigation)
                .SingleOrDefaultAsync(p => p.IdUser == studentId);

            if (studentProfile == null)
            {
                _statusText.Text = "Анкета студента не найдена.";
                return;
            }

            var settings = await db.ProgramSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.IdSetting == 1);

            if (settings == null)
            {
                _statusText.Text = "Настройки программы не найдены.";
                return;
            }

            var lastRequest = await db.CoachRequests
                .AsNoTracking()
                .Where(r => r.IdStudent == studentId)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.IdCoachRequest)
                .FirstOrDefaultAsync();

            bool hasPendingRequest =
                lastRequest?.Status == CoachRequest.Pending;

            if (hasPendingRequest && lastRequest != null)
            {
                var requestedCoach = await db.Profiles
                    .AsNoTracking()
                    .Include(p => p.IdUserNavigation)
                    .SingleOrDefaultAsync(p =>
                        p.IdUser == lastRequest.IdCoach);

                string coachName =
                    requestedCoach?.IdUserNavigation.FullName
                    ?? requestedCoach?.IdUserNavigation.Login
                    ?? "коучу";

                _statusText.Text =
                    $"Ваш запрос коучу «{coachName}» ожидает ответа. " +
                    "Отправить следующий запрос можно после решения текущего.";
            }
            else if (lastRequest?.Status == CoachRequest.Rejected)
            {
                _statusText.Text =
                    "Предыдущий запрос отклонён. " +
                    "Вы можете выбрать другого коуча.";
            }
            else if (lastRequest?.Status == CoachRequest.Accepted)
            {
                _statusText.Text =
                    "Предыдущее назначение завершено. " +
                    "Вы можете выбрать коуча повторно.";
            }
            else
            {
                _statusText.Text = "Выберите подходящего коуча.";
            }

            var query = db.Profiles
                .AsNoTracking()
                .Include(p => p.IdUserNavigation)
                .Include(p => p.IdSpecialtyNavigation)
                .Include(p => p.IdSectorNavigation)
                .Where(p =>
                    p.IdUserNavigation.IdRole == 2
                    && p.IdUserNavigation.IdUserStatus == 1
                    && (
                        p.IdUserNavigation.Course == 2
                        || p.IdUserNavigation.Course == 3
                        || p.IdUserNavigation.Course == 4
                    )
                    && p.IdSpecialtyNavigation.IsActive
                    && p.IdSectorNavigation.IsActive
                    && !db.Assignments.Any(a =>
                        a.IdCoach == p.IdUser
                        && a.IdAssignmentStatus == 1));

            if (settings.RequireSameSpecialty)
            {
                query = query.Where(p =>
                    p.IdSpecialty == studentProfile.IdSpecialty);
            }

            var availableCoaches = await query.ToListAsync();

            var studentTokens = GetTokens(studentProfile.Interest);

            var recommendations = availableCoaches
                .Select(p =>
                {
                    var overlap = studentTokens
                        .Intersect(GetTokens(p.Interest))
                        .Count();

                    int score = 0;

                    var matches = new List<string>();

                    if (p.IdSpecialty == studentProfile.IdSpecialty)
                    {
                        score += 5;
                        matches.Add("специальность");
                    }

                    if (p.IdSector == studentProfile.IdSector)
                    {
                        score += 3;
                        matches.Add("сектор");
                    }

                    if (overlap > 0)
                    {
                        score += Math.Min(overlap * 2, 8);
                        matches.Add("интересы");
                    }

                    return new CoachCandidate
                    {
                        IdUser = p.IdUser,
                        Login = p.IdUserNavigation.Login,
                        Name = p.IdUserNavigation.FullName
                            ?? p.IdUserNavigation.Login,
                        Course = p.IdUserNavigation.Course ?? 0,
                        Specialty =
                            p.IdSpecialtyNavigation.Abbreviation,
                        Sector = p.IdSectorNavigation.Name,
                        AcademicGoal = p.AcademicGoal,
                        Interest = p.Interest,
                        Score = score,
                        MatchDetails = matches.Count == 0
                            ? "Коуч доступен; явных совпадений мало"
                            : "Совпадает: " + string.Join(", ", matches)
                    };
                })
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Login)
                .Take(5)
                .ToList();

            if (recommendations.Count == 0)
            {
                _recommendations.Children.Add(new TextBlock
                {
                    Text = "Сейчас нет доступных коучей по заданным условиям.",
                    FontSize = 14,
                    Foreground = Brush.Parse("#747C90"),
                    TextWrapping = TextWrapping.Wrap
                });

                return;
            }

            foreach (var candidate in recommendations)
            {
                _recommendations.Children.Add(
                    CreateCandidateCard(
                        candidate,
                        hasPendingRequest,
                        lastRequest?.IdCoach));
            }
        }
        catch (Exception)
        {
            _statusText.Text =
                "Не удалось загрузить рекомендации. " +
                "Проверьте подключение к базе и нажмите «Обновить».";
        }
    }

    private Border CreateCandidateCard(
        CoachCandidate candidate,
        bool hasPendingRequest,
        int? pendingCoachId)
    {
        var content = new StackPanel
        {
            Spacing = 8
        };

        content.Children.Add(new TextBlock
        {
            Text = candidate.Name,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D")
        });

        content.Children.Add(CreateText(
            $"Логин: {candidate.Login} · Курс: {candidate.Course}"));

        content.Children.Add(CreateText(
            $"Специальность: {candidate.Specialty}"));

        content.Children.Add(CreateText(
            $"Сектор: {candidate.Sector}"));

        content.Children.Add(CreateText(
            "Академическая цель: " + candidate.AcademicGoal));

        content.Children.Add(CreateText(
            "Интерес: " + candidate.Interest));

        content.Children.Add(new TextBlock
        {
            Text = candidate.MatchDetails,
            FontSize = 13,
            Foreground = Brush.Parse("#5B4BFF"),
            TextWrapping = TextWrapping.Wrap
        });

        var sendButton = CreateButton(
            pendingCoachId == candidate.IdUser
                ? "Запрос отправлен"
                : "Отправить запрос");

        sendButton.HorizontalAlignment = HorizontalAlignment.Left;
        sendButton.IsEnabled = !hasPendingRequest;

        sendButton.Click += async (_, _) =>
            await SendRequestAsync(candidate.IdUser, sendButton);

        content.Children.Add(sendButton);

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

    private async Task SendRequestAsync(int coachId, Button button)
    {
        if (_busy)
            return;

        _busy = true;
        button.IsEnabled = false;

        bool reload = false;
        int studentId = UserSession.IdUser;

        try
        {
            await using var db = new MentoraContext();
            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var users = await db.Users
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM users
                    WHERE id_user IN ({studentId}, {coachId})
                    ORDER BY id_user
                    FOR UPDATE
                    """)
                .ToListAsync();

            var student = users.SingleOrDefault(u =>
                u.IdUser == studentId);

            var coach = users.SingleOrDefault(u =>
                u.IdUser == coachId);

            if (student == null
                || student.IdRole != 1
                || student.IdUserStatus != 1)
            {
                throw new MatchRequestException(
                    "Профиль студента недоступен.");
            }

            if (coach == null
                || coach.IdRole != 2
                || coach.IdUserStatus != 1
                || coach.Course is not (2 or 3 or 4))
            {
                throw new MatchRequestException(
                    "Выбранный коуч больше недоступен.");
            }

            bool studentBusy = await db.Assignments.AnyAsync(a =>
                a.IdStudent == studentId
                && a.IdAssignmentStatus == 1);

            bool coachBusy = await db.Assignments.AnyAsync(a =>
                a.IdCoach == coachId
                && a.IdAssignmentStatus == 1);

            if (studentBusy)
            {
                throw new MatchRequestException(
                    "У вас уже есть назначенный коуч.");
            }

            if (coachBusy)
            {
                throw new MatchRequestException(
                    "Коуч уже назначил себе студента. Обновите рекомендации.");
            }

            bool requestPending = await db.CoachRequests.AnyAsync(r =>
                r.IdStudent == studentId
                && r.Status == CoachRequest.Pending);

            if (requestPending)
            {
                throw new MatchRequestException(
                    "У вас уже есть запрос, ожидающий решения.");
            }

            var settings = await db.ProgramSettings
                .AsNoTracking()
                .SingleAsync(s => s.IdSetting == 1);

            var studentProfile = await db.Profiles
                .AsNoTracking()
                .SingleAsync(p => p.IdUser == studentId);

            var coachProfile = await db.Profiles
                .AsNoTracking()
                .SingleAsync(p => p.IdUser == coachId);

            if (settings.RequireSameSpecialty
                && studentProfile.IdSpecialty != coachProfile.IdSpecialty)
            {
                throw new MatchRequestException(
                    "Настройки программы не разрешают выбрать коуча другой специальности.");
            }

            DateTime now = DateTime.UtcNow;

            var request = new CoachRequest
            {
                IdStudent = studentId,
                IdCoach = coachId,
                Status = CoachRequest.Pending,
                CreatedAt = now
            };

            db.CoachRequests.Add(request);

            await db.SaveChangesAsync();

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = studentId,
                ActionType = "Запрос коучу",
                ObjectType = "Запрос коучу",
                ObjectId = request.IdCoachRequest,
                OccurredAt = now,
                IsSuccess = true,
                Description =
                    $"Студент {student.Login} отправил запрос коучу {coach.Login}."
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            reload = true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: "23505"
            })
        {
            _statusText.Text =
                "Запрос уже отправлен или данные изменились. Обновите рекомендации.";

            reload = true;
        }
        catch (MatchRequestException ex)
        {
            _statusText.Text = ex.Message;
        }
        catch (Exception)
        {
            _statusText.Text =
                "Не удалось отправить запрос. Проверьте подключение к базе.";
        }
        finally
        {
            _busy = false;

            if (reload)
                await LoadRecommendationsAsync();
            else
                button.IsEnabled = true;
        }
    }

    private static HashSet<string> GetTokens(string? value)
    {
        return Regex
            .Split((value ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);
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

    private sealed class MatchRequestException : Exception
    {
        public MatchRequestException(string message)
            : base(message)
        {
        }
    }

    private sealed class CoachCandidate
    {
        public int IdUser { get; set; }

        public string Login { get; set; } = "";

        public string Name { get; set; } = "";

        public short Course { get; set; }

        public string Specialty { get; set; } = "";

        public string Sector { get; set; } = "";

        public string AcademicGoal { get; set; } = "";

        public string Interest { get; set; } = "";

        public int Score { get; set; }

        public string MatchDetails { get; set; } = "";
    }
}