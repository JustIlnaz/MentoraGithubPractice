using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using mentora.Models;

namespace mentora.Views;

public partial class RegistrationView : UserControl
{
    private int _selectedRole;
    private bool _isBusy;
    private bool _referencesLoaded;

    public RegistrationView()
    {
        InitializeComponent();
    }

    private async void StudentButton_Click(object? sender, RoutedEventArgs e)
    {
        await OpenFormAsync(1);
    }

    private async void CoachButton_Click(object? sender, RoutedEventArgs e)
    {
        await OpenFormAsync(2);
    }

    private async Task OpenFormAsync(int role)
    {
        if (_isBusy)
        {
            return;
        }

        _selectedRole = role;

        HideErrors();
        PasswordTextBox.Text = "";

        if (role == 1)
        {
            FormTitle.Text = "Регистрация студента";

            CourseComboBox.ItemsSource = new[] { "1 курс" };
            CourseComboBox.SelectedIndex = 0;
            CourseComboBox.IsEnabled = false;
        }
        else
        {
            FormTitle.Text = "Регистрация коуча";

            CourseComboBox.ItemsSource = new[]
            {
                "2 курс",
                "3 курс",
                "4 курс"
            };

            CourseComboBox.SelectedIndex = -1;
            CourseComboBox.IsEnabled = true;
        }

        RolePanel.IsVisible = false;
        FormPanel.IsVisible = true;

        await LoadReferencesAsync();
    }

    private async Task LoadReferencesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        int? oldSectorId = (SectorComboBox.SelectedItem as Sector)?.IdSector;

        int? oldSpecialtyId = (SpecialtyComboBox.SelectedItem as Specialty)?.IdSpecialty;

        _referencesLoaded = false;
        MessageBorder.IsVisible = false;

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();

            var sectors = await db.Sectors
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();

            var specialties = await db.Specialties
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Abbreviation)
                .ToListAsync();

            SectorComboBox.ItemsSource = sectors;
            SpecialtyComboBox.ItemsSource = specialties;

            SectorComboBox.SelectedItem = sectors.FirstOrDefault(s => s.IdSector == oldSectorId);

            SpecialtyComboBox.SelectedItem = specialties.FirstOrDefault(s => s.IdSpecialty == oldSpecialtyId);

            _referencesLoaded = sectors.Count > 0 && specialties.Count > 0;

            if (!_referencesLoaded)
            {
                ShowMessage("Нет доступных специальностей или секторов. " + "Обратитесь к администратору.");
            }
        }
        catch (Exception)
        {
            ShowMessage("Не удалось загрузить справочники. " + "Проверьте подключение к базе и нажмите " + "«Обновить списки специальностей и секторов».");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ReloadButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadReferencesAsync();
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || !_referencesLoaded)
        {
            return;
        }

        HideErrors();

        string login = LoginTextBox.Text?.Trim() ?? "";
        string password = PasswordTextBox.Text ?? "";
        string goal = GoalTextBox.Text?.Trim() ?? "";
        string interest = InterestTextBox.Text?.Trim() ?? "";

        LoginTextBox.Text = login;
        GoalTextBox.Text = goal;
        InterestTextBox.Text = interest;

        if (_selectedRole != 1 && _selectedRole != 2)
        {
            ShowMessage("Выберите роль студента или коуча.");
            return;
        }

        if (CourseComboBox.SelectedIndex < 0)
        {
            ShowFieldError(CourseError, "Выберите курс.");
            return;
        }

        short course = _selectedRole == 1
            ? (short)1
            : (short)(CourseComboBox.SelectedIndex + 2);

        if ((_selectedRole == 1 && course != 1) || (_selectedRole == 2 && (course < 2 || course > 4)))
        {
            ShowFieldError(CourseError, "Указан недопустимый курс.");
            return;
        }

        if (!Regex.IsMatch(login, @"\A[A-Za-zА-Яа-яЁё0-9_.-]{4,50}\z"))
        {
            ShowFieldError(
                LoginError,
                "От 4 до 50 символов: русские или латинские буквы, " +
                "цифры, точка, дефис и подчёркивание. Без пробелов.");

            return;
        }

        if (password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            ShowFieldError(
                PasswordError,
                "Пароль должен содержать минимум 8 символов, " +
                "хотя бы одну букву и одну цифру.");

            return;
        }

        if (Encoding.UTF8.GetByteCount(password) > 72)
        {
            ShowFieldError(
                PasswordError,
                "Пароль слишком длинный. Максимум — 72 байта UTF-8. " +
                "Сократите пароль.");

            return;
        }

        if (goal.Length < 10 || goal.Length > 500)
        {
            ShowFieldError(
                GoalError,
                "Академическая цель должна содержать от 10 до 500 символов.");

            return;
        }

        if (interest.Length < 2 || interest.Length > 300)
        {
            ShowFieldError(
                InterestError,
                "Укажите интерес длиной от 2 до 300 символов.");

            return;
        }

        if (SectorComboBox.SelectedItem is not Sector selectedSector)
        {
            ShowFieldError(SectorError, "Выберите сектор.");
            return;
        }

        if (SpecialtyComboBox.SelectedItem
            is not Specialty selectedSpecialty)
        {
            ShowFieldError(SpecialtyError, "Выберите специальность.");
            return;
        }

        if (TopLevel.GetTopLevel(this) is not MainWindow mainWindow)
        {
            ShowMessage("Не удалось открыть главное окно.");
            return;
        }

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();

            bool loginExists = await db.Users
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM users
                                      WHERE LOWER(login) = LOWER({login})
                                      """)
                .AsNoTracking()
                .AnyAsync();

            if (loginExists)
            {
                ShowFieldError(
                    LoginError,
                    "Этот логин уже занят. Выберите другой.");

                return;
            }

            string passwordHash = await Task.Run(
                () => BCrypt.Net.BCrypt.HashPassword(
                    password,
                    workFactor: 12));

            await using var transaction =
                await db.Database.BeginTransactionAsync();

            var specialty = await db.Specialties
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM specialties
                                      WHERE id_specialty = {selectedSpecialty.IdSpecialty}
                                      FOR SHARE
                                      """)
                .AsNoTracking()
                .SingleOrDefaultAsync();

            var sector = await db.Sectors
                .FromSqlInterpolated($"""
                                      SELECT *
                                      FROM sectors
                                      WHERE id_sector = {selectedSector.IdSector}
                                      FOR SHARE
                                      """)
                .AsNoTracking()
                .SingleOrDefaultAsync();

            if (specialty == null || !specialty.IsActive)
            {
                ShowFieldError(
                    SpecialtyError,
                    "Специальность больше недоступна. " +
                    "Обновите списки и выберите другую.");

                return;
            }

            if (sector == null || !sector.IsActive)
            {
                ShowFieldError(
                    SectorError,
                    "Сектор больше недоступен. " +
                    "Обновите списки и выберите другой.");

                return;
            }

            DateTime now = DateTime.UtcNow;

            var user = new User
            {
                Login = login,
                PasswordHash = passwordHash,
                IdRole = _selectedRole,
                Course = course,
                IdUserStatus = 1,
                CreatedAt = now,
                UpdatedAt = now,
                FailedLoginAttempts = 0
            };

            db.Users.Add(user);

            await db.SaveChangesAsync();

            var profile = new Profile
            {
                IdUser = user.IdUser,
                AcademicGoal = goal,
                Interest = interest,
                IdSpecialty = specialty.IdSpecialty,
                IdSector = sector.IdSector,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Profiles.Add(profile);

            db.ActionLogs.Add(new ActionLog
            {
                IdUser = user.IdUser,
                ActionType = "Регистрация",
                ObjectType = "Пользователь",
                ObjectId = user.IdUser,
                OccurredAt = now,
                IsSuccess = true,
                Description = _selectedRole == 1
                    ? "Зарегистрирован студент."
                    : "Зарегистрирован коуч."
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            mainWindow.ShowLogin(login);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
                  {
                      SqlState: "23505",
                      ConstraintName: "ux_users_login"
                  })
        {
            ShowFieldError(
                LoginError,
                "Этот логин уже занят. Выберите другой.");
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось завершить регистрацию. " +
                "Проверьте подключение к базе данных и повторите попытку.");
        }
        finally
        {
            PasswordTextBox.Text = "";
            SetBusy(false);
        }
    }

    private void ChangeRoleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        PasswordTextBox.Text = "";
        HideErrors();

        FormPanel.IsVisible = false;
        RolePanel.IsVisible = true;
    }

    private void BackToLogin_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.ShowLogin();
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;

        FormFields.IsEnabled = !isBusy;
        ReloadButton.IsEnabled = !isBusy;
        ChangeRoleButton.IsEnabled = !isBusy;
        BackButton.IsEnabled = !isBusy;

        SaveButton.IsEnabled = !isBusy && _referencesLoaded;

        SaveButton.Content = isBusy
            ? "Подождите…"
            : "Зарегистрироваться";
    }

    private void HideErrors()
    {
        CourseError.IsVisible = false;
        LoginError.IsVisible = false;
        PasswordError.IsVisible = false;
        GoalError.IsVisible = false;
        InterestError.IsVisible = false;
        SectorError.IsVisible = false;
        SpecialtyError.IsVisible = false;
        MessageBorder.IsVisible = false;
    }

    private void ShowFieldError(TextBlock errorBlock, string message)
    {
        PasswordTextBox.Text = "";

        errorBlock.Text = message;
        errorBlock.IsVisible = true;
        errorBlock.BringIntoView();
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageBorder.IsVisible = true;
        MessageBorder.BringIntoView();
    }
}