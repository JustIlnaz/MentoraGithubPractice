using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace mentora.Services;

internal static class AdminReportPdfWriter
{
    private static readonly object FontResolverLock = new();
    private const string FontFamily = "Inter";
    private static InterFontResolver? _fontResolver;

    public static Task WriteAsync(
        AdminStatsReport report,
        Stream output,
        CancellationToken cancellationToken)
    {
        EnsureFontResolver();
        return Task.Run(() => Write(report, output), cancellationToken);
    }

    private static void Write(AdminStatsReport report, Stream output)
    {
        using var document = new PdfDocument();
        using (var renderer = new ReportRenderer(document, report))
            renderer.Render();

        document.Save(output, closeStream: false);
    }

    private static void EnsureFontResolver()
    {
        lock (FontResolverLock)
        {
            if (_fontResolver != null)
                return;

            if (GlobalFontSettings.FontResolver == null)
            {
                _fontResolver = new InterFontResolver(
                    LoadFont("Inter-Regular.ttf"),
                    LoadFont("Inter-Bold.ttf"));
                GlobalFontSettings.FontResolver = _fontResolver;
            }
        }
    }

    private static byte[] LoadFont(string fileName)
    {
        var fontUri = new Uri($"avares://Avalonia.Fonts.Inter/Assets/{fileName}");
        using var fontStream = AssetLoader.Open(fontUri);
        using var memoryStream = new MemoryStream();
        fontStream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private sealed class InterFontResolver : IFontResolver
    {
        private const string RegularFace = "Inter-Regular";
        private const string BoldFace = "Inter-Bold";
        private readonly byte[] _regularFont;
        private readonly byte[] _boldFont;

        public InterFontResolver(byte[] regularFont, byte[] boldFont)
        {
            _regularFont = regularFont;
            _boldFont = boldFont;
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            return new FontResolverInfo(isBold ? BoldFace : RegularFace);
        }

        public byte[] GetFont(string faceName)
        {
            return faceName switch
            {
                RegularFace => _regularFont,
                BoldFace => _boldFont,
                _ => throw new ArgumentException("Неизвестное начертание шрифта.", nameof(faceName))
            };
        }
    }

    private sealed class ReportRenderer : IDisposable
    {
        private const double Margin = 32;
        private const double LineHeight = 12;
        private const double CellPadding = 5;
        private const double SectionHeight = 24;
        private const double HeaderHeight = 24;
        private static readonly XColor Accent = XColor.FromArgb(91, 75, 255);
        private static readonly XColor Border = XColor.FromArgb(211, 215, 226);
        private static readonly XPdfFontOptions FontOptions = new(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile);
        private static readonly XFont TitleFont = new(FontFamily, 17, XFontStyleEx.Bold, FontOptions);
        private static readonly XFont SectionFont = new(FontFamily, 11, XFontStyleEx.Bold, FontOptions);
        private static readonly XFont BodyFont = new(FontFamily, 8, XFontStyleEx.Regular, FontOptions);
        private static readonly XFont HeaderFont = new(FontFamily, 8, XFontStyleEx.Bold, FontOptions);

        private readonly PdfDocument _document;
        private readonly AdminStatsReport _report;
        private readonly Dictionary<(XFont Font, string Text), double> _textWidths = new();
        private PdfPage _page;
        private XGraphics _graphics;
        private double _y;

        public ReportRenderer(PdfDocument document, AdminStatsReport report)
        {
            _document = document;
            _report = report;
            _page = _document.AddPage();
            _page.Size = PageSize.A4;
            _page.Orientation = PageOrientation.Landscape;
            _graphics = XGraphics.FromPdfPage(_page);
            _y = Margin;
            DrawPageHeading();
        }

        public void Render()
        {
            WriteTable(
                "Основные показатели",
                new[] { "Показатель", "Значение" },
                new[] { 630d, 148d },
                _report.Metrics.Select(metric => new[]
                {
                    metric.Name,
                    metric.Value.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
                }));

            WriteDistribution("Назначения по специальностям", _report.AssignmentsBySpecialty);
            WriteDistribution("Назначения по секторам", _report.AssignmentsBySector);
            WriteDistribution("Свободные студенты по специальностям", _report.FreeStudentsBySpecialty);

            WriteTable(
                "Студенты, которым ещё не назначен коуч",
                new[] { "Логин", "Специальность", "Сектор", "Дата регистрации" },
                new[] { 190d, 190d, 280d, 118d },
                _report.FreeStudents.Select(student => new[]
                {
                    student.Login,
                    student.Specialty,
                    student.Sector,
                    student.RegisteredAt.ToLocalTime().ToString("dd.MM.yyyy")
                }));

            WriteTable(
                "Журнал действий",
                new[] { "Дата и время", "Пользователь", "Действие", "Описание", "Результат" },
                new[] { 105d, 110d, 145d, 350d, 68d },
                _report.ActionLogs.Select(log => new[]
                {
                    log.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                    log.Login ?? "",
                    log.ActionType,
                    log.Description ?? "",
                    log.IsSuccess ? "Успешно" : "Ошибка"
                }));
        }

        public void Dispose()
        {
            _graphics.Dispose();
        }

        private void WriteDistribution(string title, IReadOnlyList<AdminStatRow> values)
        {
            WriteTable(
                title,
                new[] { "Наименование", "Количество" },
                new[] { 630d, 148d },
                values.Select(value => new[]
                {
                    value.Name,
                    value.Count.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
                }));
        }

        private void WriteTable(string title, string[] headers, double[] widths, IEnumerable<string[]> rows)
        {
            DrawSectionAndHeaders(title, headers, widths);
            bool hasRows = false;

            foreach (string[] row in rows)
            {
                hasRows = true;
                DrawRow(row, title, headers, widths);
            }

            if (!hasRows)
                DrawRow(new[] { "За выбранный период записей нет." }, title, headers, widths);

            _y += 12;
        }

        private void DrawRow(string[] cells, string title, string[] headers, double[] widths)
        {
            var wrapped = new List<string[]>(cells.Length);
            int lineCount = 1;
            for (int column = 0; column < cells.Length; column++)
            {
                var lines = WrapText(cells[column] ?? "", BodyFont, widths[column] - CellPadding * 2);
                wrapped.Add(lines);
                lineCount = Math.Max(lineCount, lines.Length);
            }

            while (wrapped.Count < widths.Length)
                wrapped.Add(new[] { "" });

            int lineOffset = 0;
            while (lineOffset < lineCount)
            {
                double availableHeight = Bottom - _y - 4;
                int visibleLines = (int)Math.Floor((availableHeight - CellPadding * 2) / LineHeight);
                if (visibleLines < 1)
                {
                    AddPage();
                    DrawPageHeading();
                    DrawSectionAndHeaders(title, headers, widths);
                    continue;
                }

                int segmentLines = Math.Min(visibleLines, lineCount - lineOffset);
                double rowHeight = segmentLines * LineHeight + CellPadding * 2;
                double x = Margin;

                for (int column = 0; column < widths.Length; column++)
                {
                    _graphics.DrawRectangle(new XPen(Border, 0.5), XBrushes.White, x, _y, widths[column], rowHeight);
                    for (int line = 0; line < segmentLines; line++)
                    {
                        int wrappedLine = lineOffset + line;
                        if (wrappedLine < wrapped[column].Length)
                        {
                            _graphics.DrawString(
                                wrapped[column][wrappedLine],
                                BodyFont,
                                XBrushes.Black,
                                x + CellPadding,
                                _y + CellPadding + BodyFont.Size + line * LineHeight);
                        }
                    }

                    x += widths[column];
                }

                _y += rowHeight;
                lineOffset += segmentLines;

                if (lineOffset < lineCount)
                {
                    AddPage();
                    DrawPageHeading();
                    DrawSectionAndHeaders(title, headers, widths);
                }
            }
        }

        private void DrawSectionAndHeaders(string title, string[] headers, double[] widths)
        {
            if (_y + SectionHeight + HeaderHeight > Bottom)
            {
                AddPage();
                DrawPageHeading();
            }

            _graphics.DrawString(title, SectionFont, XBrushes.Black, Margin, _y + SectionFont.Size + 3);
            _y += SectionHeight;

            double x = Margin;
            for (int column = 0; column < headers.Length; column++)
            {
                _graphics.DrawRectangle(new XPen(Accent), new XSolidBrush(Accent), x, _y, widths[column], HeaderHeight);
                var lines = WrapText(headers[column], HeaderFont, widths[column] - CellPadding * 2);
                _graphics.DrawString(
                    lines[0],
                    HeaderFont,
                    XBrushes.White,
                    x + CellPadding,
                    _y + CellPadding + HeaderFont.Size);
                x += widths[column];
            }

            _y += HeaderHeight;
        }

        private void AddPage()
        {
            _graphics.Dispose();
            _page = _document.AddPage();
            _page.Size = PageSize.A4;
            _page.Orientation = PageOrientation.Landscape;
            _graphics = XGraphics.FromPdfPage(_page);
            _y = Margin;
        }

        private void DrawPageHeading()
        {
            _graphics.DrawString("Административный отчёт", TitleFont, XBrushes.Black, Margin, _y + TitleFont.Size);
            _y += 24;
            _graphics.DrawString(AdminReportExportService.PeriodText(_report), BodyFont, XBrushes.Black, Margin, _y + BodyFont.Size);
            _y += 24;
        }

        private double Bottom => _page.Height.Point - Margin;

        private string[] WrapText(string text, XFont font, double width)
        {
            var result = new List<string>();
            foreach (string paragraph in text.Replace("\r", "").Split('\n'))
            {
                if (paragraph.Length == 0)
                {
                    result.Add("");
                    continue;
                }

                string current = "";
                double currentWidth = 0;
                double spaceWidth = MeasureText(" ", font);
                foreach (string word in paragraph.Split(' '))
                {
                    if (word.Length == 0)
                        continue;

                    double wordWidth = MeasureText(word, font);
                    double candidateWidth = currentWidth + (current.Length == 0 ? 0 : spaceWidth) + wordWidth;
                    if (candidateWidth <= width)
                    {
                        current = current.Length == 0 ? word : current + " " + word;
                        currentWidth = candidateWidth;
                        continue;
                    }

                    if (current.Length > 0)
                    {
                        result.Add(current);
                        current = "";
                        currentWidth = 0;
                    }

                    if (wordWidth <= width)
                    {
                        current = word;
                        currentWidth = wordWidth;
                        continue;
                    }

                    foreach (char character in word)
                    {
                        string characterText = character.ToString();
                        double characterWidth = MeasureText(characterText, font);
                        if (current.Length > 0 && currentWidth + characterWidth > width)
                        {
                            result.Add(current);
                            current = characterText;
                            currentWidth = characterWidth;
                        }
                        else
                        {
                            current += character;
                            currentWidth += characterWidth;
                        }
                    }
                }

                if (current.Length > 0)
                    result.Add(current);
            }

            return result.Count == 0 ? new[] { "" } : result.ToArray();
        }

        private double MeasureText(string text, XFont font)
        {
            var key = (font, text);
            if (_textWidths.TryGetValue(key, out double width))
                return width;

            width = _graphics.MeasureString(text, font).Width;
            _textWidths.Add(key, width);
            return width;
        }
    }
}
