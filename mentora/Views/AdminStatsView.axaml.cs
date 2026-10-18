using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using mentora.Models;
using mentora.Services;

namespace mentora.Views;

public partial class AdminStatsView : UserControl
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly AdminStatsReportService _reportService = new();
    private readonly AdminReportExportService _exportService = new();
    private bool _isBusy;
    private bool _isExporting;
    private AdminStatsReport? _currentReport;

    public AdminStatsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void ShowButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private async void AllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _isExporting)
            return;

        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        await LoadAsync();
    }

    private async Task<bool> LoadAsync()
    {
        if (_isBusy)
            return false;

        DateTimeOffset? from = DateFromPicker.SelectedDate;
        DateTimeOffset? to = DateToPicker.SelectedDate;
        if (from != null && to != null && from.Value.Date > to.Value.Date)
        {
            ShowMessage("Начало периода не может быть позже его окончания.", true);
            return false;
        }

        MessageText.IsVisible = false;
        _currentReport = null;
        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();
            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return false;
            }

            _currentReport = await _reportService.LoadAsync(db, from, to);
            DisplayReport(_currentReport);
            return true;
        }
        catch (Exception)
        {
            ShowMessage("Не удалось загрузить статистику. Проверьте подключение к базе данных.", true);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DisplayReport(AdminStatsReport report)
    {
        NewStudentsValue.Text = MetricValue(report, 0);
        NewCoachesValue.Text = MetricValue(report, 1);
        CreatedValue.Text = MetricValue(report, 2);
        CompletedValue.Text = MetricValue(report, 3);
        CancelledValue.Text = MetricValue(report, 4);
        MeetingsValue.Text = MetricValue(report, 5);
        ActiveNowValue.Text = MetricValue(report, 6);
        FreeNowValue.Text = MetricValue(report, 7);

        BySpecialtyList.ItemsSource = report.AssignmentsBySpecialty;
        BySectorList.ItemsSource = report.AssignmentsBySector;
        FreeList.ItemsSource = report.FreeStudentsBySpecialty;
        FreeStudentsList.ItemsSource = report.FreeStudents;

        BySpecialtyEmpty.IsVisible = report.AssignmentsBySpecialty.Count == 0;
        BySectorEmpty.IsVisible = report.AssignmentsBySector.Count == 0;
        FreeEmpty.IsVisible = report.FreeStudentsBySpecialty.Count == 0;
        FreeStudentsNote.Text = report.FreeStudents.Count == 0
            ? "Нет студентов, которым ещё не назначен коуч."
            : report.FreeStudents.Count < 100
                ? $"Показаны все студенты: {report.FreeStudents.Count}."
                : "Показаны первые 100 студентов, полный список доступен в таблице.";

        PeriodText.Text = FormatPeriod(report);
        StatsPanel.IsVisible = true;
        TablesPanel.IsVisible = true;
        FreeStudentsBorder.IsVisible = true;
    }

    private static string MetricValue(AdminStatsReport report, int index)
    {
        return report.Metrics[index].Value.ToString(RussianCulture);
    }

    private async void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _isExporting)
            return;

        _isExporting = true;
        ExportButton.IsEnabled = false;
        MessageText.IsVisible = false;

        try
        {
            if (!IsSelectedPeriodCurrent() && !await LoadAsync())
                return;

            if (_currentReport is not { } report)
                return;

            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider == null)
            {
                ShowMessage("Системный диалог сохранения файлов недоступен.", true);
                return;
            }

            SetBusy(true);
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить административный отчёт",
                SuggestedFileName = BuildSuggestedFileName(report) + ".xlsx",
                DefaultExtension = "xlsx",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Книга Excel")
                    {
                        Patterns = new[] { "*.xlsx" },
                        MimeTypes = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
                    },
                    new FilePickerFileType("Документ PDF")
                    {
                        Patterns = new[] { "*.pdf" },
                        MimeTypes = new[] { "application/pdf" }
                    }
                }
            });

            if (file == null)
                return;

            string extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (extension is not (".xlsx" or ".pdf"))
            {
                ShowMessage("Выберите формат Excel (.xlsx) или PDF (.pdf).", true);
                return;
            }

            await using var output = await file.OpenWriteAsync();
            if (extension == ".pdf")
                await _exportService.ExportPdfAsync(report, output);
            else
                await _exportService.ExportExcelAsync(report, output);

            ShowMessage($"Отчёт успешно сохранён: {file.Name}", false);
        }
        catch (Exception exception)
        {
            ShowMessage($"Не удалось экспортировать отчёт: {exception.Message}", true);
        }
        finally
        {
            SetBusy(false);
            _isExporting = false;
            ExportButton.IsEnabled = true;
        }
    }

    private bool IsSelectedPeriodCurrent()
    {
        DateTime? from = DateFromPicker.SelectedDate?.Date;
        DateTime? to = DateToPicker.SelectedDate?.Date;
        return _currentReport != null
            && _currentReport.PeriodStart == from
            && _currentReport.PeriodEnd == to;
    }

    private static string BuildSuggestedFileName(AdminStatsReport report)
    {
        string from = report.PeriodStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all";
        string to = report.PeriodEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all";
        return $"AdminReport_{from}_{to}";
    }

    private static string FormatPeriod(AdminStatsReport report)
    {
        string from = report.PeriodStart?.ToString("dd.MM.yyyy", RussianCulture) ?? "с начала";
        string to = report.PeriodEnd?.ToString("dd.MM.yyyy", RussianCulture) ?? "по настоящее время";
        return $"Период: {from} — {to} (даты включительно).";
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
