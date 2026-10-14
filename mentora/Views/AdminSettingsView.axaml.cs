using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class AdminSettingsView : UserControl
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private bool _isBusy;
    private bool _loaded;

    public AdminSettingsView()
    {
        InitializeComponent();

        Loaded += async (_, _) => await LoadAsync();
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
                AdminGuard.ReturnToLogin(this);
                return;
            }

            var settings = await db.ProgramSettings
                .AsNoTracking()
                .Where(s => s.IdSetting == 1)
                .Select(s => new
                {
                    s.RequireSameSpecialty,
                    s.CoachCanComplete,
                    s.UpdatedAt,
                    Login = s.UpdatedByNavigation != null ? s.UpdatedByNavigation.Login : null
                })
                .SingleOrDefaultAsync();

            if (settings == null)
            {
                SameSpecialtyCheck.IsChecked = false;
                CoachCompleteCheck.IsChecked = false;
                UpdatedText.Text = "Настройки ещё не сохранялись.";
            }
            else
            {
                SameSpecialtyCheck.IsChecked = settings.RequireSameSpecialty;
                CoachCompleteCheck.IsChecked = settings.CoachCanComplete;

                UpdatedText.Text = "Последнее изменение: " +
                                   settings.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", RussianCulture) +
                                   (settings.Login == null ? "" : ", " + settings.Login);
            }

            _loaded = true;
        }
        catch (Exception)
        {
            ShowMessage("Не удалось загрузить настройки. Проверьте подключение к базе.", true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || !_loaded)
            return;

        bool sameSpecialty = SameSpecialtyCheck.IsChecked == true;
        bool coachComplete = CoachCompleteCheck.IsChecked == true;

        string? error = null;
        bool saved = false;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                AdminGuard.ReturnToLogin(this);
                return;
            }

            var settings = await db.ProgramSettings
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM program_settings
                                      WHERE id_setting = 1
                                      FOR UPDATE
                                      """)
                .SingleOrDefaultAsync();

            DateTime now = DateTime.UtcNow;
            var changes = new List<string>();

            if (settings == null)
            {
                settings = new ProgramSetting
                {
                    IdSetting = 1,
                    RequireSameSpecialty = sameSpecialty,
                    CoachCanComplete = coachComplete,
                    UpdatedBy = UserSession.IdUser,
                    UpdatedAt = now
                };

                db.ProgramSettings.Add(settings);

                changes.Add("первичное сохранение настроек");
            }
            else
            {
                if (settings.RequireSameSpecialty != sameSpecialty)
                {
                    changes.Add("одна специальность: " + (sameSpecialty ? "вкл" : "выкл"));
                    settings.RequireSameSpecialty = sameSpecialty;
                }

                if (settings.CoachCanComplete != coachComplete)
                {
                    changes.Add("коуч завершает работу: " + (coachComplete ? "вкл" : "выкл"));
                    settings.CoachCanComplete = coachComplete;
                }

                if (changes.Count > 0)
                {
                    settings.UpdatedBy = UserSession.IdUser;
                    settings.UpdatedAt = now;
                }
            }

            if (changes.Count == 0)
            {
                error = "Изменений нет.";
            }
            else
            {
                db.ActionLogs.Add(new ActionLog
                {
                    IdUser = UserSession.IdUser,
                    ActionType = "Изменение настроек программы",
                    ObjectType = "Настройки",
                    ObjectId = 1,
                    OccurredAt = now,
                    IsSuccess = true,
                    Description = string.Join("; ", changes)
                });

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                saved = true;
            }
        }
        catch (Exception)
        {
            error = "Не удалось сохранить настройки. Проверьте подключение к базе.";
        }
        finally
        {
            SetBusy(false);
        }

        if (saved)
        {
            await LoadAsync();
            ShowMessage("Настройки сохранены.", false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
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