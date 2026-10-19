using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using ClosedXML.Excel;
using mentora;
using mentora.Services;
using PdfSharp.Pdf.IO;
using Xunit;

namespace mentora.Tests;

public sealed class AdminReportExportServiceTests
{
    private readonly AdminReportExportService _service = new();

    [Fact]
    public async Task ExcelContainsTypedStatisticsAndAllActionLogs()
    {
        var report = CreateReport(actionLogCount: 3);
        using var stream = new MemoryStream();

        await _service.ExportExcelAsync(report, stream);

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(new[] { "Статистика", "Журнал действий" }, workbook.Worksheets.Select(sheet => sheet.Name));

        var statistics = workbook.Worksheet("Статистика");
        Assert.Equal("Административный отчёт", statistics.Cell(1, 1).GetString());
        Assert.Equal(XLDataType.Number, statistics.Cell(6, 2).DataType);
        Assert.Equal(17, statistics.Cell(6, 2).GetValue<int>());

        var logs = workbook.Worksheet("Журнал действий");
        Assert.Equal(XLDataType.DateTime, logs.Cell(5, 1).DataType);
        Assert.Equal(3, logs.LastRowUsed()!.RowNumber() - 4);
        Assert.Equal(report.ActionLogs[0].Description, logs.Cell(5, 4).GetString());
    }

    [Fact]
    public async Task ExcelHandlesEmptyStatisticsAndLogSets()
    {
        using var stream = new MemoryStream();

        await _service.ExportExcelAsync(CreateReport(actionLogCount: 0, withStatistics: false), stream);

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        Assert.Equal("За выбранный период записей нет.", workbook.Worksheet("Журнал действий").Cell(5, 1).GetString());
        Assert.Equal("За выбранный период записей нет.", workbook.Worksheet("Статистика").Cell(9, 1).GetString());
    }

    [Fact]
    public async Task PdfEmbedsUnicodeFontAndCreatesPagesForLongLog()
    {
        EnsureAvaloniaInitialized();
        var report = CreateReport(actionLogCount: 100);
        using var stream = new MemoryStream();

        await _service.ExportPdfAsync(report, stream).ConfigureAwait(false);

        byte[] pdfBytes = stream.ToArray();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, 5));
        string pdfSource = Encoding.ASCII.GetString(pdfBytes);
        Assert.Contains("Inter", pdfSource);
        Assert.Contains("/ToUnicode", pdfSource);

        stream.Position = 0;
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 1);
    }

    [Fact]
    public async Task ExcelAndPdfReportTheSamePeriodAndMetricSet()
    {
        EnsureAvaloniaInitialized();
        var report = CreateReport(actionLogCount: 1);
        using var excel = new MemoryStream();
        using var pdf = new MemoryStream();

        Task pdfExport = _service.ExportPdfAsync(report, pdf);
        await _service.ExportExcelAsync(report, excel).ConfigureAwait(false);
        await pdfExport.ConfigureAwait(false);

        excel.Position = 0;
        using var workbook = new XLWorkbook(excel);
        Assert.Equal("Период: 01.01.2026 — 31.01.2026", workbook.Worksheet("Статистика").Cell(2, 1).GetString());
        Assert.Equal("Показатель", workbook.Worksheet("Статистика").Cell(5, 1).GetString());

        pdf.Position = 0;
        using var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        Assert.NotEmpty(document.Pages);
    }

    [Fact]
    public void PeriodBoundsUseStartOfSelectedDayAndExclusiveNextDay()
    {
        var selectedDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        DateTime start = AdminStatsReportService.ToLocalDayStartUtc(selectedDate);
        DateTime end = AdminStatsReportService.ToLocalDayStartUtc(selectedDate.AddDays(1));

        Assert.True(start < end);
        Assert.True(start.AddTicks(1) < end);
        Assert.Equal(start.AddDays(1), end);
    }

    [Fact]
    public async Task ExportPropagatesFileWriteFailures()
    {
        var report = CreateReport(actionLogCount: 1);
        using var stream = new FailingWriteStream();

        await Assert.ThrowsAsync<IOException>(() => _service.ExportExcelAsync(report, stream));
    }

    private static AdminStatsReport CreateReport(int actionLogCount, bool withStatistics = true)
    {
        var logs = Enumerable.Range(0, actionLogCount)
            .Select(index => new AdminActionLogRow(
                new DateTime(2026, 1, 15, 12, 30, 0, DateTimeKind.Utc).AddMinutes(index),
                "admin",
                "Обновление записи",
                string.Concat(Enumerable.Repeat("Длинное описание действия ", 20)),
                true))
            .ToArray();

        return new AdminStatsReport
        {
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Metrics = withStatistics
                ? new[] { new AdminReportMetric("Кириллический показатель", 17) }
                : Array.Empty<AdminReportMetric>(),
            AssignmentsBySpecialty = withStatistics
                ? new[] { new AdminStatRow { Name = "Специальность", Count = 2 } }
                : Array.Empty<AdminStatRow>(),
            AssignmentsBySector = Array.Empty<AdminStatRow>(),
            FreeStudentsBySpecialty = Array.Empty<AdminStatRow>(),
            FreeStudents = Array.Empty<AdminFreeStudentRow>(),
            ActionLogs = logs
        };
    }

    private static void EnsureAvaloniaInitialized()
    {
        AvaloniaAppSetup.Value.ToString();
    }

    private static readonly Lazy<bool> AvaloniaAppSetup = new(() =>
    {
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .SetupWithoutStarting();
        return true;
    });

    private sealed class FailingWriteStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Simulated write failure.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Simulated write failure.");

        public override void WriteByte(byte value) => throw new IOException("Simulated write failure.");
    }
}
