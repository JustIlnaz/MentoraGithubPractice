using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using mentora.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace mentora.Views;

public partial class AdminDictionariesView : UserControl
{
    private bool _isBusy;
    private bool _specialtiesMode = true;
    private int? _editingId;

    public AdminDictionariesView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            ApplyMode();
            await LoadAsync();
        };
    }

    private void ApplyMode()
    {
        SpecialtiesTab.Classes.Remove("selected");
        SectorsTab.Classes.Remove("selected");

        if (_specialtiesMode)
            SpecialtiesTab.Classes.Add("selected");
        else
            SectorsTab.Classes.Add("selected");

        AddButton.Content = _specialtiesMode
            ? "Добавить специальность"
            : "Добавить сектор";

        CloseForm();
    }

    private async void SpecialtiesTab_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _specialtiesMode)
            return;

        _specialtiesMode = true;
        ApplyMode();
        await LoadAsync();
    }

    private async void SectorsTab_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || !_specialtiesMode)
            return;

        _specialtiesMode = false;
        ApplyMode();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_isBusy)
            return;

        bool specialties = _specialtiesMode;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            List<DictionaryRow> rows;

            if (specialties)
            {
                var items = await db.Specialties
                    .AsNoTracking()
                    .OrderByDescending(s => s.IsActive)
                    .ThenBy(s => s.Abbreviation)
                    .Select(s => new
                    {
                        s.IdSpecialty,
                        s.Abbreviation,
                        s.Description,
                        s.IsActive,
                        Used = db.Profiles.Count(p => p.IdSpecialty == s.IdSpecialty)
                    })
                    .ToListAsync();

                rows = items
                    .Select(s => new DictionaryRow
                    {
                        Id = s.IdSpecialty,
                        Title = s.Abbreviation,
                        Description = s.Description,
                        IsActive = s.IsActive,
                        UsedCount = s.Used
                    })
                    .ToList();
            }
            else
            {
                var items = await db.Sectors
                    .AsNoTracking()
                    .OrderByDescending(s => s.IsActive)
                    .ThenBy(s => s.Name)
                    .Select(s => new
                    {
                        s.IdSector,
                        s.Name,
                        s.IsActive,
                        Used = db.Profiles.Count(p => p.IdSector == s.IdSector)
                    })
                    .ToListAsync();

                rows = items
                    .Select(s => new DictionaryRow
                    {
                        Id = s.IdSector,
                        Title = s.Name,
                        Description = null,
                        IsActive = s.IsActive,
                        UsedCount = s.Used
                    })
                    .ToList();
            }

            if (specialties != _specialtiesMode)
                return;

            DictionaryList.ItemsSource = rows;

            ResultsText.Text = rows.Count == 0
                ? "Значений пока нет. Нажмите «Добавить»."
                : "Всего значений: " + rows.Count;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить справочник. " +
                "Проверьте подключение к базе и откройте раздел заново.",
                true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        OpenForm(null);
    }

    private void EditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not Button { Tag: DictionaryRow row })
            return;

        OpenForm(row);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        CloseForm();
    }

    private void OpenForm(DictionaryRow? row)
    {
        HideMessage();
        NameError.IsVisible = false;
        DescriptionError.IsVisible = false;

        _editingId = row?.Id;

        bool specialties = _specialtiesMode;

        NameLabel.Text = specialties ? "Аббревиатура *" : "Наименование *";
        NameTextBox.MaxLength = specialties ? 20 : 100;
        DescriptionPanel.IsVisible = specialties;

        if (row == null)
        {
            FormTitle.Text = specialties ? "Новая специальность" : "Новый сектор";
            NameTextBox.Text = "";
            DescriptionTextBox.Text = "";
        }
        else
        {
            FormTitle.Text = specialties
                ? "Редактирование специальности"
                : "Редактирование сектора";

            NameTextBox.Text = row.Title;
            DescriptionTextBox.Text = row.Description ?? "";
        }

        FormBorder.IsVisible = true;
        FormBorder.BringIntoView();
    }

    private void CloseForm()
    {
        _editingId = null;
        FormBorder.IsVisible = false;
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        NameError.IsVisible = false;
        DescriptionError.IsVisible = false;
        HideMessage();

        bool specialties = _specialtiesMode;
        int? editingId = _editingId;

        string name = NameTextBox.Text?.Trim() ?? "";
        string description = DescriptionTextBox.Text?.Trim() ?? "";

        bool valid = true;

        if (specialties)
        {
            if (!Regex.IsMatch(name, @"\A[A-Za-zА-Яа-яЁё0-9-]{1,20}\z"))
            {
                ShowError(
                    NameError,
                    "Аббревиатура: от 1 до 20 символов, только буквы, цифры " +
                    "и дефис, без пробелов.");

                valid = false;
            }

            if (description.Length > 500)
            {
                ShowError(
                    DescriptionError,
                    "Описание не должно быть длиннее 500 символов.");

                valid = false;
            }
        }
        else if (name.Length < 1 || name.Length > 100)
        {
            ShowError(NameError, "Наименование должно содержать от 1 до 100 символов.");
            valid = false;
        }

        if (!valid)
            return;

        bool saved = false;
        string? error = null;

        SetBusy(true);

        try
        {
            if (specialties)
                await SaveSpecialtyAsync(editingId, name, description);
            else
                await SaveSectorAsync(editingId, name);

            saved = true;
        }
        catch (UnauthorizedAccessException)
        {
            AdminGuard.ReturnToLogin(this);
            return;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            ShowError(
                NameError,
                specialties
                    ? "Такая аббревиатура уже есть."
                    : "Такой сектор уже есть.");
        }
        catch (DictionaryException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось сохранить. " +
                "Проверьте подключение к базе и повторите попытку.";
        }
        finally
        {
            SetBusy(false);
        }

        if (saved)
        {
            CloseForm();
            await LoadAsync();
            ShowMessage("Сохранено.", false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
    }

    private static async Task SaveSpecialtyAsync(
        int? id,
        string abbreviation,
        string description)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        int excludeId = id ?? 0;
        string lowered = abbreviation.ToLower();

        bool exists = await db.Specialties.AnyAsync(s =>
            s.IdSpecialty != excludeId
            && s.Abbreviation.ToLower() == lowered);

        if (exists)
            throw new DictionaryException("Такая аббревиатура уже есть.");

        DateTime now = DateTime.UtcNow;
        string? descriptionValue = string.IsNullOrWhiteSpace(description)
            ? null
            : description;

        if (id == null)
        {
            var specialty = new Specialty
            {
                Abbreviation = abbreviation,
                Description = descriptionValue,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Specialties.Add(specialty);
            await db.SaveChangesAsync();

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = UserSession.IdUser,
                ActionType = "Создание специальности",
                ObjectType = "Специальность",
                ObjectId = specialty.IdSpecialty,
                OccurredAt = now,
                IsSuccess = true,
                Description = abbreviation
            });
        }
        else
        {
            var specialty = await db.Specialties
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM specialties
                                      WHERE id_specialty = {id}
                                      FOR UPDATE
                                      """)
                .SingleOrDefaultAsync();

            if (specialty == null)
                throw new DictionaryException("Специальность не найдена. Обновите список.");

            if (specialty.Abbreviation == abbreviation
                && specialty.Description == descriptionValue)
            {
                throw new DictionaryException("Изменений нет.");
            }

            string oldAbbreviation = specialty.Abbreviation;

            specialty.Abbreviation = abbreviation;
            specialty.Description = descriptionValue;
            specialty.UpdatedAt = now;

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = UserSession.IdUser,
                ActionType = "Изменение специальности",
                ObjectType = "Специальность",
                ObjectId = specialty.IdSpecialty,
                OccurredAt = now,
                IsSuccess = true,
                Description = oldAbbreviation == abbreviation
                    ? abbreviation + ": изменено описание"
                    : oldAbbreviation + " → " + abbreviation
            });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task SaveSectorAsync(int? id, string name)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        int excludeId = id ?? 0;
        string lowered = name.ToLower();

        bool exists = await db.Sectors.AnyAsync(s =>
            s.IdSector != excludeId
            && s.Name.ToLower() == lowered);

        if (exists)
            throw new DictionaryException("Такой сектор уже есть.");

        DateTime now = DateTime.UtcNow;

        if (id == null)
        {
            var sector = new Sector
            {
                Name = name,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Sectors.Add(sector);
            await db.SaveChangesAsync();

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = UserSession.IdUser,
                ActionType = "Создание сектора",
                ObjectType = "Сектор",
                ObjectId = sector.IdSector,
                OccurredAt = now,
                IsSuccess = true,
                Description = name
            });
        }
        else
        {
            var sector = await db.Sectors
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM sectors
                                      WHERE id_sector = {id}
                                      FOR UPDATE
                                      """)
                .SingleOrDefaultAsync();

            if (sector == null)
                throw new DictionaryException("Сектор не найден. Обновите список.");

            if (sector.Name == name)
                throw new DictionaryException("Изменений нет.");

            string oldName = sector.Name;

            sector.Name = name;
            sector.UpdatedAt = now;

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = UserSession.IdUser,
                ActionType = "Изменение сектора",
                ObjectType = "Сектор",
                ObjectId = sector.IdSector,
                OccurredAt = now,
                IsSuccess = true,
                Description = oldName + " → " + name
            });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async void ToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not Button { Tag: DictionaryRow row })
            return;

        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        bool specialties = _specialtiesMode;
        bool archive = row.IsActive;

        bool confirmed = await AdminDialogs.ConfirmAsync(
            owner,
            archive ? "Архивировать значение?" : "Восстановить значение?",
            archive
                ? "Значение «" + row.Title + "» нельзя будет выбрать при новой " +
                  "регистрации. В существующих анкетах и назначениях оно сохранится."
                : "Значение «" + row.Title + "» снова можно будет выбирать " +
                  "при регистрации.",
            archive ? "Архивировать" : "Восстановить",
            archive);

        if (!confirmed)
            return;

        bool done = false;
        string? error = null;

        SetBusy(true);

        try
        {
            await SetActiveAsync(specialties, row.Id, !archive);
            done = true;
        }
        catch (UnauthorizedAccessException)
        {
            AdminGuard.ReturnToLogin(this);
            return;
        }
        catch (DictionaryException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось изменить статус. " +
                "Проверьте подключение к базе и повторите попытку.";
        }
        finally
        {
            SetBusy(false);
        }

        if (done)
        {
            await LoadAsync();
            ShowMessage(archive ? "Значение архивировано." : "Значение восстановлено.", false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
    }

    private static async Task SetActiveAsync(bool specialties, int id, bool active)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        DateTime now = DateTime.UtcNow;

        string title;

        if (specialties)
        {
            var specialty = await db.Specialties
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM specialties
                                      WHERE id_specialty = {id}
                                      FOR UPDATE
                                      """)
                .SingleOrDefaultAsync();

            if (specialty == null)
                throw new DictionaryException("Специальность не найдена. Обновите список.");

            if (specialty.IsActive == active)
                throw new DictionaryException("Статус уже изменён. Данные обновлены.");

            if (!active)
            {
                bool otherActive = await db.Specialties.AnyAsync(s =>
                    s.IsActive && s.IdSpecialty != id);

                if (!otherActive)
                {
                    throw new DictionaryException(
                        "Нельзя архивировать последнюю активную специальность: " +
                        "регистрация станет невозможной.");
                }
            }

            specialty.IsActive = active;
            specialty.UpdatedAt = now;
            title = specialty.Abbreviation;
        }
        else
        {
            var sector = await db.Sectors
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM sectors
                                      WHERE id_sector = {id}
                                      FOR UPDATE
                                      """)
                .SingleOrDefaultAsync();

            if (sector == null)
                throw new DictionaryException("Сектор не найден. Обновите список.");

            if (sector.IsActive == active)
                throw new DictionaryException("Статус уже изменён. Данные обновлены.");

            if (!active)
            {
                bool otherActive = await db.Sectors.AnyAsync(s =>
                    s.IsActive && s.IdSector != id);

                if (!otherActive)
                {
                    throw new DictionaryException(
                        "Нельзя архивировать последний активный сектор: " +
                        "регистрация станет невозможной.");
                }
            }

            sector.IsActive = active;
            sector.UpdatedAt = now;
            title = sector.Name;
        }

        db.ActionLogs.Add(new ActionLog
        {
            IdUser = UserSession.IdUser,
            ActionType = (active ? "Восстановление " : "Архивация ") +
                         (specialties ? "специальности" : "сектора"),
            ObjectType = specialties ? "Специальность" : "Сектор",
            ObjectId = id,
            OccurredAt = now,
            IsSuccess = true,
            Description = title
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RootPanel.IsEnabled = !isBusy;
    }

    private static void ShowError(TextBlock block, string text)
    {
        block.Text = text;
        block.IsVisible = true;
    }

    private void HideMessage()
    {
        MessageText.IsVisible = false;
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = Brush.Parse(isError ? "#B52C2C" : "#278450");
        MessageText.IsVisible = true;
    }

    private sealed class DictionaryException : Exception
    {
        public DictionaryException(string message)
            : base(message)
        {
        }
    }
}

public class DictionaryRow
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int UsedCount { get; set; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string StatusText => IsActive ? "Активно" : "В архиве";

    public string UsedText => "Используется в анкетах: " + UsedCount;

    public string ToggleText => IsActive ? "Архивировать" : "Восстановить";

    public IBrush StatusBrush => IsActive
        ? Brush.Parse("#278450")
        : Brush.Parse("#747C90");
}