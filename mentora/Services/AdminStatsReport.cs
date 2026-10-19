using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Services;

public sealed class AdminStatsReport
{
    public DateTime? PeriodStart { get; init; }

    public DateTime? PeriodEnd { get; init; }

    public IReadOnlyList<AdminReportMetric> Metrics { get; init; } = Array.Empty<AdminReportMetric>();

    public IReadOnlyList<AdminStatRow> AssignmentsBySpecialty { get; init; } = Array.Empty<AdminStatRow>();

    public IReadOnlyList<AdminStatRow> AssignmentsBySector { get; init; } = Array.Empty<AdminStatRow>();

    public IReadOnlyList<AdminStatRow> FreeStudentsBySpecialty { get; init; } = Array.Empty<AdminStatRow>();

    public IReadOnlyList<AdminFreeStudentRow> FreeStudents { get; init; } = Array.Empty<AdminFreeStudentRow>();

    public IReadOnlyList<AdminActionLogRow> ActionLogs { get; init; } = Array.Empty<AdminActionLogRow>();
}

public sealed record AdminReportMetric(string Name, int Value);

public sealed class AdminStatRow
{
    public string Name { get; init; } = "";

    public int Count { get; init; }
}

public sealed class AdminFreeStudentRow
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string Login { get; init; } = "";

    public string Specialty { get; init; } = "";

    public string Sector { get; init; } = "";

    public DateTime RegisteredAt { get; init; }

    public string ProfileText => Specialty + " — " + Sector;

    public string DateText => "с " + RegisteredAt.ToLocalTime().ToString("dd.MM.yyyy", RussianCulture);
}

public sealed record AdminActionLogRow(
    DateTime OccurredAt,
    string? Login,
    string ActionType,
    string? Description,
    bool IsSuccess);

public sealed class AdminStatsReportService
{
    private const int FreeStudentsDisplayLimit = 100;

    public async Task<AdminStatsReport> LoadAsync(
        MentoraContext db,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from != null && to != null && from.Value.Date > to.Value.Date)
            throw new ArgumentException("Начало периода не может быть позже его окончания.");

        DateTime? fromUtc = from == null ? null : ToLocalDayStartUtc(from.Value);
        DateTime? toUtc = to == null ? null : ToLocalDayStartUtc(to.Value.AddDays(1));

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
            .Select(g => new AdminStatRow { Name = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Name)
            .ToListAsync();

        var bySector = await db.Assignments
            .AsNoTracking()
            .Where(a => (fromUtc == null || a.CreatedAt >= fromUtc)
                        && (toUtc == null || a.CreatedAt < toUtc))
            .GroupBy(a => a.IdStudentNavigation.IdSectorNavigation.Name)
            .Select(g => new AdminStatRow { Name = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Name)
            .ToListAsync();

        var freeBySpecialty = await db.Profiles
            .AsNoTracking()
            .Where(p =>
                p.IdUserNavigation.IdRole == 1
                && p.IdUserNavigation.IdUserStatus == 1
                && !db.Assignments.Any(a => a.IdStudent == p.IdUser && a.IdAssignmentStatus == 1))
            .GroupBy(p => p.IdSpecialtyNavigation.Abbreviation)
            .Select(g => new AdminStatRow { Name = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Name)
            .ToListAsync();

        var freeStudents = await db.Profiles
            .AsNoTracking()
            .Where(p =>
                p.IdUserNavigation.IdRole == 1
                && p.IdUserNavigation.IdUserStatus == 1
                && !db.Assignments.Any(a => a.IdStudent == p.IdUser && a.IdAssignmentStatus == 1))
            .OrderBy(p => p.IdUserNavigation.CreatedAt)
            .Take(FreeStudentsDisplayLimit)
            .Select(p => new AdminFreeStudentRow
            {
                Login = p.IdUserNavigation.Login,
                Specialty = p.IdSpecialtyNavigation.Abbreviation,
                Sector = p.IdSectorNavigation.Name,
                RegisteredAt = p.IdUserNavigation.CreatedAt
            })
            .ToListAsync();

        var actionLogs = await (
                from log in db.ActionLogs.AsNoTracking()
                join user in db.Users on log.IdUser equals (int?)user.IdUser into users
                from user in users.DefaultIfEmpty()
                where (fromUtc == null || log.OccurredAt >= fromUtc)
                    && (toUtc == null || log.OccurredAt < toUtc)
                orderby log.OccurredAt descending, log.IdActionLog descending
                select new AdminActionLogRow(
                    log.OccurredAt,
                    user == null ? null : user.Login,
                    log.ActionType,
                    log.Description,
                    log.IsSuccess))
            .ToListAsync();

        return new AdminStatsReport
        {
            PeriodStart = from?.DateTime.Date,
            PeriodEnd = to?.DateTime.Date,
            Metrics = new[]
            {
                new AdminReportMetric("Новых студентов", newStudents),
                new AdminReportMetric("Новых коучей", newCoaches),
                new AdminReportMetric("Создано назначений", created),
                new AdminReportMetric("Завершено назначений", completed),
                new AdminReportMetric("Отменено назначений", cancelled),
                new AdminReportMetric("Проведено встреч", meetings),
                new AdminReportMetric("Активных назначений сейчас", activeNow),
                new AdminReportMetric("Свободных студентов сейчас", freeNow)
            },
            AssignmentsBySpecialty = bySpecialty,
            AssignmentsBySector = bySector,
            FreeStudentsBySpecialty = freeBySpecialty,
            FreeStudents = freeStudents,
            ActionLogs = actionLogs
        };
    }

    public static DateTime ToLocalDayStartUtc(DateTimeOffset date)
    {
        return DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();
    }
}
