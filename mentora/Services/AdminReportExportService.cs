using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace mentora.Services;

public sealed class AdminReportExportService
{
    public Task ExportExcelAsync(
        AdminStatsReport report,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);

        return Task.Run(() => WriteExcel(report, output), cancellationToken);
    }

    private static void WriteExcel(AdminStatsReport report, Stream output)
    {
        using var workbook = new XLWorkbook();
        WriteStatisticsWorksheet(workbook.Worksheets.Add("Статистика"), report);
        WriteActionLogsWorksheet(workbook.Worksheets.Add("Журнал действий"), report);
        workbook.SaveAs(output);
    }

    private static void WriteStatisticsWorksheet(IXLWorksheet worksheet, AdminStatsReport report)
    {
        worksheet.Cell(1, 1).Value = "Административный отчёт";
        worksheet.Range(1, 1, 1, 2).Merge();
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;
        worksheet.Cell(2, 1).Value = PeriodText(report);
        worksheet.Range(2, 1, 2, 2).Merge();

        int row = 4;
        row = WriteSectionTitle(worksheet, row, "Основные показатели", 2);
        row = WriteTableHeader(worksheet, row, "Показатель", "Значение");

        foreach (var metric in report.Metrics)
        {
            worksheet.Cell(row, 1).Value = metric.Name;
            worksheet.Cell(row, 2).Value = metric.Value;
            row++;
        }

        row++;
        row = WriteDistribution(worksheet, row, "Назначения по специальностям", report.AssignmentsBySpecialty);
        row = WriteDistribution(worksheet, row, "Назначения по секторам", report.AssignmentsBySector);
        row = WriteDistribution(worksheet, row, "Свободные студенты по специальностям", report.FreeStudentsBySpecialty);

        row = WriteSectionTitle(worksheet, row, "Студенты, которым ещё не назначен коуч", 4);
        row = WriteTableHeader(worksheet, row, "Логин", "Специальность", "Сектор", "Дата регистрации");

        if (report.FreeStudents.Count == 0)
        {
            WriteEmptyMessage(worksheet, row, 4);
        }
        else
        {
            foreach (var student in report.FreeStudents)
            {
                worksheet.Cell(row, 1).Value = student.Login;
                worksheet.Cell(row, 2).Value = student.Specialty;
                worksheet.Cell(row, 3).Value = student.Sector;
                worksheet.Cell(row, 4).Value = student.RegisteredAt.ToLocalTime();
                worksheet.Cell(row, 4).Style.DateFormat.Format = "dd.mm.yyyy";
                row++;
            }
        }

        worksheet.Columns(1, 4).Style.Alignment.WrapText = true;
        worksheet.Column(1).Width = 42;
        worksheet.Column(2).Width = 28;
        worksheet.Column(3).Width = 32;
        worksheet.Column(4).Width = 20;
        worksheet.SheetView.FreezeRows(5);
    }

    private static int WriteDistribution(
        IXLWorksheet worksheet,
        int row,
        string title,
        System.Collections.Generic.IReadOnlyList<AdminStatRow> values)
    {
        row = WriteSectionTitle(worksheet, row, title, 2);
        row = WriteTableHeader(worksheet, row, "Наименование", "Количество");

        if (values.Count == 0)
        {
            WriteEmptyMessage(worksheet, row, 2);
            return row + 2;
        }

        foreach (var value in values)
        {
            worksheet.Cell(row, 1).Value = value.Name;
            worksheet.Cell(row, 2).Value = value.Count;
            row++;
        }

        return row + 1;
    }

    private static int WriteSectionTitle(IXLWorksheet worksheet, int row, string title, int columns)
    {
        worksheet.Cell(row, 1).Value = title;
        worksheet.Range(row, 1, row, columns).Merge();
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 1).Style.Font.FontSize = 12;
        worksheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E9FF");
        return row + 1;
    }

    private static int WriteTableHeader(IXLWorksheet worksheet, int row, params string[] labels)
    {
        for (int column = 0; column < labels.Length; column++)
            worksheet.Cell(row, column + 1).Value = labels[column];

        var header = worksheet.Range(row, 1, row, labels.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#5B4BFF");
        header.Style.Alignment.WrapText = true;
        return row + 1;
    }

    private static void WriteEmptyMessage(IXLWorksheet worksheet, int row, int columns)
    {
        worksheet.Cell(row, 1).Value = "За выбранный период записей нет.";
        worksheet.Range(row, 1, row, columns).Merge();
        worksheet.Cell(row, 1).Style.Font.Italic = true;
    }

    private static void WriteActionLogsWorksheet(IXLWorksheet worksheet, AdminStatsReport report)
    {
        worksheet.Cell(1, 1).Value = "Журнал действий";
        worksheet.Range(1, 1, 1, 5).Merge();
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;
        worksheet.Cell(2, 1).Value = PeriodText(report);
        worksheet.Range(2, 1, 2, 5).Merge();

        string[] headers = { "Дата и время", "Пользователь", "Действие", "Описание", "Результат" };
        for (int column = 0; column < headers.Length; column++)
            worksheet.Cell(4, column + 1).Value = headers[column];

        var header = worksheet.Range(4, 1, 4, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#5B4BFF");
        header.Style.Alignment.WrapText = true;

        if (report.ActionLogs.Count == 0)
        {
            WriteEmptyMessage(worksheet, 5, headers.Length);
        }
        else
        {
            int row = 5;
            foreach (var log in report.ActionLogs)
            {
                worksheet.Cell(row, 1).Value = log.OccurredAt.ToLocalTime();
                worksheet.Cell(row, 1).Style.DateFormat.Format = "dd.mm.yyyy hh:mm:ss";
                worksheet.Cell(row, 2).Value = log.Login ?? "";
                worksheet.Cell(row, 3).Value = log.ActionType;
                worksheet.Cell(row, 4).Value = log.Description ?? "";
                worksheet.Cell(row, 5).Value = log.IsSuccess ? "Успешно" : "Ошибка";
                row++;
            }
        }

        worksheet.Columns(1, 5).Style.Alignment.WrapText = true;
        worksheet.Column(1).Width = 22;
        worksheet.Column(2).Width = 24;
        worksheet.Column(3).Width = 28;
        worksheet.Column(4).Width = 70;
        worksheet.Column(5).Width = 16;
        worksheet.SheetView.FreezeRows(4);
    }

    internal static string PeriodText(AdminStatsReport report)
    {
        string from = report.PeriodStart?.ToString("dd.MM.yyyy") ?? "с начала";
        string to = report.PeriodEnd?.ToString("dd.MM.yyyy") ?? "по настоящее время";
        return $"Период: {from} — {to}";
    }
}
