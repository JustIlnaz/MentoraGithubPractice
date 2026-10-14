using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using mentora.Models;

namespace mentora.Views;

public partial class AdminUserWindow : Window
{
    private static readonly CultureInfo RussianCulture =
        CultureInfo.GetCultureInfo("ru-RU");

    private readonly int _userId;
    private readonly bool _isOwnAccount;

    private bool _isBusy;
    private bool _loading;
    private bool _loaded;
    private int _roleId;
    private int _statusId;

    public AdminUserWindow() : this(0)
    {
    }

    public AdminUserWindow(int userId)
    {
        InitializeComponent();

        _userId = userId;
        _isOwnAccount = userId == UserSession.IdUser;

        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_isBusy)
            return;

        SetBusy(true);
        HideMessage();
        HideErrors();

        try
        {
            await using var db = new MentoraContext();

            if (!await AdminGuard.IsAdminAsync(db))
            {
                DenyAccess();
                return;
            }

            var user = await db.Users
                .AsNoTracking()
                .Include(u => u.IdRoleNavigation)
                .Include(u => u.IdUserStatusNavigation)
                .Include(u => u.Profile)
                .SingleOrDefaultAsync(u => u.IdUser == _userId);

            if (user == null)
            {
                ShowMessage("Пользователь не найден.", true);
                EditPanel.IsEnabled = false;
                return;
            }

            bool hasActiveAssignment = await db.Assignments.AnyAsync(a =>
                (a.IdStudent == _userId || a.IdCoach == _userId)
                && a.IdAssignmentStatus == 1);

            int? currentSpecialty = user.Profile?.IdSpecialty;
            int? currentSector = user.Profile?.IdSector;

            var specialties = await db.Specialties
                .AsNoTracking()
                .Where(s => s.IsActive || s.IdSpecialty == currentSpecialty)
                .OrderBy(s => s.Abbreviation)
                .ToListAsync();

            var sectors = await db.Sectors
                .AsNoTracking()
                .Where(s => s.IsActive || s.IdSector == currentSector)
                .OrderBy(s => s.Name)
                .ToListAsync();

            _loading = true;

            _roleId = user.IdRole;
            _statusId = user.IdUserStatus;

            TitleText.Text = user.Login;

            RoleValue.Text = user.Course == null
                ? user.IdRoleNavigation.Name
                : user.IdRoleNavigation.Name + " · " + user.Course + " курс";

            StatusValue.Text = user.IdUserStatusNavigation.Name;
            StatusValue.Foreground = StatusBrush(user.IdUserStatus);

            CreatedValue.Text = user.CreatedAt
                .ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm", RussianCulture);

            LastLoginValue.Text = user.LastLoginAt == null
                ? "Ещё не входил"
                : user.LastLoginAt.Value
                    .ToLocalTime()
                    .ToString("dd.MM.yyyy HH:mm", RussianCulture);

            AssignmentValue.Text = user.IdRole == 3
                ? "Не применяется"
                : hasActiveAssignment
                    ? "Есть активное назначение"
                    : "Активного назначения нет";

            LoginTextBox.Text = user.Login;

            bool isAdminRole = user.IdRole == 3;

            RoleCoursePanel.IsVisible = !isAdminRole;
            ProfilePanel.IsVisible = !isAdminRole && user.Profile != null;

            if (!isAdminRole)
            {
                var roles = new List<Option>
                {
                    new(1, "Студент"),
                    new(2, "Коуч")
                };

                RoleComboBox.ItemsSource = roles;
                RoleComboBox.SelectedItem = roles.FirstOrDefault(o => o.Id == user.IdRole);

                FillCourses(user.IdRole, user.Course);
            }

            if (user.Profile != null)
            {
                GoalTextBox.Text = user.Profile.AcademicGoal;
                InterestTextBox.Text = user.Profile.Interest;

                var specialtyItems = specialties
                    .Select(s => new Option(
                        s.IdSpecialty,
                        s.IsActive ? s.Abbreviation : s.Abbreviation + " (архив)"))
                    .ToList();

                SpecialtyComboBox.ItemsSource = specialtyItems;
                SpecialtyComboBox.SelectedItem = specialtyItems
                    .FirstOrDefault(o => o.Id == user.Profile.IdSpecialty);

                var sectorItems = sectors
                    .Select(s => new Option(
                        s.IdSector,
                        s.IsActive ? s.Name : s.Name + " (архив)"))
                    .ToList();

                SectorComboBox.ItemsSource = sectorItems;
                SectorComboBox.SelectedItem = sectorItems
                    .FirstOrDefault(o => o.Id == user.Profile.IdSector);
            }

            ActivateButton.IsVisible = _statusId != 1;
            BlockButton.IsVisible = !_isOwnAccount && _statusId == 1;
            ArchiveButton.IsVisible = !_isOwnAccount && _statusId != 3;

            EditPanel.IsEnabled = true;
            _loaded = true;
        }
        catch (Exception)
        {
            ShowMessage(
                "Не удалось загрузить данные. " +
                "Проверьте подключение к базе и откройте карточку заново.",
                true);
        }
        finally
        {
            _loading = false;
            SetBusy(false);
        }
    }

    private void FillCourses(int role, short? selectedCourse)
    {
        var items = role == 1
            ? new List<Option> { new(1, "1 курс") }
            : new List<Option>
            {
                new(2, "2 курс"),
                new(3, "3 курс"),
                new(4, "4 курс")
            };

        CourseComboBox.ItemsSource = items;

        var selected = items.FirstOrDefault(o => o.Id == selectedCourse);

        if (selected == null && role == 1)
            selected = items[0];

        CourseComboBox.SelectedItem = selected;
    }

    private void RoleComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        if (RoleComboBox.SelectedItem is Option option)
            FillCourses(option.Id, null);
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || !_loaded)
            return;

        HideErrors();
        HideMessage();

        string login = LoginTextBox.Text?.Trim() ?? "";

        bool isAdminRole = _roleId == 3;
        bool valid = true;

        int newRole = _roleId;
        short? newCourse = null;
        string goal = "";
        string interest = "";
        int sectorId = 0;
        int specialtyId = 0;

        if (!Regex.IsMatch(login, @"\A[A-Za-zА-Яа-яЁё0-9_.-]{4,50}\z"))
        {
            ShowError(
                LoginError,
                "От 4 до 50 символов: русские или латинские буквы, " +
                "цифры, точка, дефис и подчёркивание. Без пробелов.");

            valid = false;
        }

        if (!isAdminRole)
        {
            newRole = (RoleComboBox.SelectedItem as Option)?.Id ?? 0;
            int course = (CourseComboBox.SelectedItem as Option)?.Id ?? 0;

            bool roleCourseOk =
                (newRole == 1 && course == 1)
                || (newRole == 2 && course is >= 2 and <= 4);

            if (!roleCourseOk)
            {
                ShowError(
                    CourseError,
                    "Выберите курс, подходящий для роли: " +
                    "студент — 1 курс, коуч — 2, 3 или 4 курс.");

                valid = false;
            }
            else
            {
                newCourse = (short)course;
            }

            if (ProfilePanel.IsVisible)
            {
                goal = GoalTextBox.Text?.Trim() ?? "";
                interest = InterestTextBox.Text?.Trim() ?? "";

                if (goal.Length < 10 || goal.Length > 500)
                {
                    ShowError(
                        GoalError,
                        "Академическая цель должна содержать от 10 до 500 символов.");

                    valid = false;
                }

                if (interest.Length < 2 || interest.Length > 300)
                {
                    ShowError(
                        InterestError,
                        "Интерес должен содержать от 2 до 300 символов.");

                    valid = false;
                }

                if (SectorComboBox.SelectedItem is Option sector)
                {
                    sectorId = sector.Id;
                }
                else
                {
                    ShowError(SectorError, "Выберите сектор.");
                    valid = false;
                }

                if (SpecialtyComboBox.SelectedItem is Option specialty)
                {
                    specialtyId = specialty.Id;
                }
                else
                {
                    ShowError(SpecialtyError, "Выберите специальность.");
                    valid = false;
                }
            }
        }

        if (!valid)
            return;

        string? error = null;
        bool saved = false;

        SetBusy(true);

        try
        {
            await SaveAsync(
                login,
                newRole,
                newCourse,
                goal,
                interest,
                sectorId,
                specialtyId);

            saved = true;
        }
        catch (UnauthorizedAccessException)
        {
            DenyAccess();
            return;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            ShowError(LoginError, "Этот логин уже занят. Выберите другой.");
        }
        catch (CardException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось сохранить изменения. " +
                "Проверьте подключение к базе и повторите попытку.";
        }
        finally
        {
            SetBusy(false);
        }

        if (saved)
        {
            await LoadAsync();
            ShowMessage("Изменения сохранены.", false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
    }

    private async Task SaveAsync(
        string login,
        int newRole,
        short? newCourse,
        string goal,
        string interest,
        int sectorId,
        int specialtyId)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        var user = await db.Users
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM users
                                  WHERE id_user = {_userId}
                                  FOR UPDATE
                                  """)
            .SingleOrDefaultAsync();

        if (user == null)
            throw new CardException("Пользователь не найден.");

        bool loginTaken = await db.Users.AnyAsync(u =>
            u.IdUser != _userId
            && u.Login.ToLower() == login.ToLower());

        if (loginTaken)
            throw new CardException("Этот логин уже занят. Выберите другой.");

        DateTime now = DateTime.UtcNow;

        var changes = new List<string>();
        string? roleChange = null;

        if (!string.Equals(user.Login, login, StringComparison.Ordinal))
        {
            changes.Add("логин: " + user.Login + " → " + login);
            user.Login = login;
        }

        if (user.IdRole != 3)
        {
            if (user.IdRole != newRole || user.Course != newCourse)
            {
                bool hasActive = await db.Assignments.AnyAsync(a =>
                    (a.IdStudent == _userId || a.IdCoach == _userId)
                    && a.IdAssignmentStatus == 1);

                if (hasActive)
                {
                    throw new CardException(
                        "Нельзя менять роль или курс при активном назначении. " +
                        "Сначала завершите или отмените его.");
                }

                roleChange =
                    "роль: " + RoleName(user.IdRole) + " → " + RoleName(newRole) +
                    "; курс: " + user.Course + " → " + newCourse;

                user.IdRole = newRole;
                user.Course = newCourse;
            }

            var profile = await db.Profiles
                .SingleOrDefaultAsync(p => p.IdUser == _userId);

            if (profile != null)
            {
                bool profileChanged = false;

                if (profile.AcademicGoal != goal)
                {
                    changes.Add("академическая цель");
                    profile.AcademicGoal = goal;
                    profileChanged = true;
                }

                if (profile.Interest != interest)
                {
                    changes.Add("интерес");
                    profile.Interest = interest;
                    profileChanged = true;
                }

                if (profile.IdSpecialty != specialtyId)
                {
                    bool active = await db.Specialties.AnyAsync(s =>
                        s.IdSpecialty == specialtyId && s.IsActive);

                    if (!active)
                    {
                        throw new CardException(
                            "Выбранная специальность архивирована. " +
                            "Выберите актуальное значение.");
                    }

                    changes.Add("специальность");
                    profile.IdSpecialty = specialtyId;
                    profileChanged = true;
                }

                if (profile.IdSector != sectorId)
                {
                    bool active = await db.Sectors.AnyAsync(s =>
                        s.IdSector == sectorId && s.IsActive);

                    if (!active)
                    {
                        throw new CardException(
                            "Выбранный сектор архивирован. " +
                            "Выберите актуальное значение.");
                    }

                    changes.Add("сектор");
                    profile.IdSector = sectorId;
                    profileChanged = true;
                }

                if (profileChanged)
                    profile.UpdatedAt = now;
            }
        }

        if (changes.Count == 0 && roleChange == null)
            throw new CardException("Изменений нет.");

        user.UpdatedAt = now;

        int adminId = UserSession.IdUser;

        if (roleChange != null)
        {
            db.ActionLogs.Add(new ActionLog
            {
                IdUser = adminId,
                ActionType = "Изменение роли и курса",
                ObjectType = "Пользователь",
                ObjectId = _userId,
                OccurredAt = now,
                IsSuccess = true,
                Description = user.Login + ": " + roleChange
            });
        }

        if (changes.Count > 0)
        {
            db.ActionLogs.Add(new ActionLog
            {
                IdUser = adminId,
                ActionType = "Изменение пользователя",
                ObjectType = "Пользователь",
                ObjectId = _userId,
                OccurredAt = now,
                IsSuccess = true,
                Description = user.Login + ": " + string.Join("; ", changes)
            });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async void BlockButton_Click(object? sender, RoutedEventArgs e)
    {
        await ChangeStatusAsync(2);
    }

    private async void ArchiveButton_Click(object? sender, RoutedEventArgs e)
    {
        await ChangeStatusAsync(3);
    }

    private async void ActivateButton_Click(object? sender, RoutedEventArgs e)
    {
        await ChangeStatusAsync(1);
    }

    private async Task ChangeStatusAsync(int newStatus)
    {
        if (_isBusy || !_loaded)
            return;

        if (_isOwnAccount && newStatus != 1)
            return;

        string login = TitleText.Text ?? "";

        string title;
        string text;
        string confirmText;

        switch (newStatus)
        {
            case 2:
                title = "Заблокировать пользователя?";
                text = "Пользователь «" + login + "» не сможет войти в приложение. " +
                       "Данные и история сохранятся.";
                confirmText = "Заблокировать";
                break;

            case 3:
                title = "Архивировать пользователя?";
                text = "Пользователь «" + login + "» будет выведен из работы " +
                       "и не сможет участвовать в новых назначениях.";
                confirmText = "Архивировать";
                break;

            default:
                title = "Активировать пользователя?";
                text = "Пользователь «" + login + "» снова сможет входить " +
                       "и участвовать в программе.";
                confirmText = "Активировать";
                break;
        }

        bool confirmed = await ConfirmAsync(this, title, text, confirmText, newStatus != 1);

        if (!confirmed)
            return;

        string? error = null;
        bool done = false;

        SetBusy(true);

        try
        {
            await ChangeStatusCoreAsync(newStatus);
            done = true;
        }
        catch (UnauthorizedAccessException)
        {
            DenyAccess();
            return;
        }
        catch (CardException ex)
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
            ShowMessage("Статус изменён.", false);
        }
        else if (error != null)
        {
            ShowMessage(error, true);
        }
    }

    private async Task ChangeStatusCoreAsync(int newStatus)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        var user = await db.Users
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM users
                                  WHERE id_user = {_userId}
                                  FOR UPDATE
                                  """)
            .SingleOrDefaultAsync();

        if (user == null)
            throw new CardException("Пользователь не найден.");

        if (user.IdUserStatus == newStatus)
            throw new CardException("Статус уже изменён. Данные обновлены.");

        if (user.IdUser == UserSession.IdUser && newStatus != 1)
        {
            throw new CardException(
                "Нельзя заблокировать или архивировать свою учётную запись.");
        }

        if (newStatus != 1 && user.IdRole != 3)
        {
            bool hasActive = await db.Assignments.AnyAsync(a =>
                (a.IdStudent == _userId || a.IdCoach == _userId)
                && a.IdAssignmentStatus == 1);

            if (hasActive)
            {
                throw new CardException(
                    "У пользователя есть активное назначение. " +
                    "Сначала завершите или отмените его.");
            }
        }

        DateTime now = DateTime.UtcNow;
        int oldStatus = user.IdUserStatus;

        user.IdUserStatus = newStatus;
        user.UpdatedAt = now;

        if (newStatus == 1)
        {
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
        }

        string actionType = newStatus switch
        {
            2 => "Блокировка пользователя",
            3 => "Архивация пользователя",
            _ => oldStatus == 2
                ? "Разблокировка пользователя"
                : "Восстановление пользователя"
        };

        db.ActionLogs.Add(new ActionLog
        {
            IdUser = UserSession.IdUser,
            ActionType = actionType,
            ObjectType = "Пользователь",
            ObjectId = _userId,
            OccurredAt = now,
            IsSuccess = true,
            Description = user.Login
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async void ResetPasswordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || !_loaded)
            return;

        string? password = await AskPasswordAsync(this, TitleText.Text ?? "");

        if (password == null)
            return;

        string? error = null;
        bool done = false;

        SetBusy(true);

        try
        {
            string hash = await Task.Run(() =>
                BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12));

            await ResetPasswordCoreAsync(hash);
            done = true;
        }
        catch (UnauthorizedAccessException)
        {
            DenyAccess();
            return;
        }
        catch (CardException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось сбросить пароль. " +
                "Проверьте подключение к базе и повторите попытку.";
        }
        finally
        {
            SetBusy(false);
        }

        if (done)
            ShowMessage("Пароль изменён. Передайте новый пароль пользователю.", false);
        else if (error != null)
            ShowMessage(error, true);
    }

    private async Task ResetPasswordCoreAsync(string hash)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        if (!await AdminGuard.IsAdminAsync(db))
            throw new UnauthorizedAccessException();

        var user = await db.Users
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM users
                                  WHERE id_user = {_userId}
                                  FOR UPDATE
                                  """)
            .SingleOrDefaultAsync();

        if (user == null)
            throw new CardException("Пользователь не найден.");

        DateTime now = DateTime.UtcNow;

        user.PasswordHash = hash;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;

        db.ActionLogs.Add(new ActionLog
        {
            IdUser = UserSession.IdUser,
            ActionType = "Сброс пароля",
            ObjectType = "Пользователь",
            ObjectId = _userId,
            OccurredAt = now,
            IsSuccess = true,
            Description = user.Login
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DenyAccess()
    {
        if (Owner is MainWindow mainWindow)
            mainWindow.ShowLogin();

        Close();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        ContentPanel.IsEnabled = !isBusy;
    }

    private void HideErrors()
    {
        LoginError.IsVisible = false;
        CourseError.IsVisible = false;
        GoalError.IsVisible = false;
        InterestError.IsVisible = false;
        SectorError.IsVisible = false;
        SpecialtyError.IsVisible = false;
    }

    private static void ShowError(TextBlock block, string text)
    {
        block.Text = text;
        block.IsVisible = true;
    }

    private void HideMessage()
    {
        MessageBorder.IsVisible = false;
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = Brush.Parse(isError ? "#B52C2C" : "#278450");
        MessageBorder.IsVisible = true;
        MessageBorder.BringIntoView();
    }

    private static string RoleName(int id)
    {
        return id switch
        {
            1 => "Студент",
            2 => "Коуч",
            3 => "Администратор",
            _ => "?"
        };
    }

    private static IBrush StatusBrush(int statusId)
    {
        return statusId switch
        {
            1 => Brush.Parse("#278450"),
            2 => Brush.Parse("#B52C2C"),
            _ => Brush.Parse("#747C90")
        };
    }

    private static Window CreateDialog(string title)
    {
        return new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };
    }

    private static Button CreateDialogButton(string text, bool primary, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Height = 44,
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        if (primary)
        {
            button.Background = Brush.Parse(danger ? "#B52C2C" : "#5B4BFF");
            button.Foreground = Brushes.White;
            button.BorderThickness = new Thickness(0);
            button.FontWeight = FontWeight.SemiBold;
        }
        else
        {
            button.Background = Brushes.White;
            button.Foreground = Brush.Parse("#39446B");
            button.BorderBrush = Brush.Parse("#DDE0E8");
            button.BorderThickness = new Thickness(1);
        }

        return button;
    }

    private static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string text,
        string confirmText,
        bool danger)
    {
        var dialog = CreateDialog(title);

        var layout = new StackPanel
        {
            Margin = new Thickness(26),
            Spacing = 16
        };

        layout.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D"),
            TextWrapping = TextWrapping.Wrap
        });

        layout.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = Brush.Parse("#747C90"),
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*")
        };

        var cancelButton = CreateDialogButton("Отмена", false);
        var confirmButton = CreateDialogButton(confirmText, true, danger);

        cancelButton.Click += (_, _) => dialog.Close(false);
        confirmButton.Click += (_, _) => dialog.Close(true);

        Grid.SetColumn(cancelButton, 0);
        Grid.SetColumn(confirmButton, 2);

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        layout.Children.Add(buttons);

        dialog.Content = layout;

        return await dialog.ShowDialog<bool>(owner);
    }

    private static async Task<string?> AskPasswordAsync(Window owner, string login)
    {
        var dialog = CreateDialog("Сброс пароля");

        var layout = new StackPanel
        {
            Margin = new Thickness(26),
            Spacing = 12
        };

        layout.Children.Add(new TextBlock
        {
            Text = "Сброс пароля",
            FontSize = 21,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D")
        });

        layout.Children.Add(new TextBlock
        {
            Text = "Задайте новый пароль для «" + login + "». " +
                   "Минимум 8 символов, хотя бы одна буква и одна цифра.",
            FontSize = 14,
            Foreground = Brush.Parse("#747C90"),
            TextWrapping = TextWrapping.Wrap
        });

        var firstBox = new TextBox
        {
            PasswordChar = '●',
            Watermark = "Новый пароль",
            MinHeight = 42
        };

        var secondBox = new TextBox
        {
            PasswordChar = '●',
            Watermark = "Повторите пароль",
            MinHeight = 42
        };

        var errorText = new TextBlock
        {
            Foreground = Brush.Parse("#B52C2C"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        layout.Children.Add(firstBox);
        layout.Children.Add(secondBox);
        layout.Children.Add(errorText);

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var cancelButton = CreateDialogButton("Отмена", false);
        var confirmButton = CreateDialogButton("Сбросить", true);

        cancelButton.Click += (_, _) => dialog.Close(null);

        confirmButton.Click += (_, _) =>
        {
            string first = firstBox.Text ?? "";
            string second = secondBox.Text ?? "";

            string? problem = null;

            if (first.Length < 8 || !first.Any(char.IsLetter) || !first.Any(char.IsDigit))
            {
                problem =
                    "Пароль должен содержать минимум 8 символов, " +
                    "хотя бы одну букву и одну цифру.";
            }
            else if (Encoding.UTF8.GetByteCount(first) > 72)
            {
                problem = "Пароль слишком длинный. Максимум — 72 байта UTF-8.";
            }
            else if (first != second)
            {
                problem = "Пароли не совпадают.";
            }

            if (problem != null)
            {
                errorText.Text = problem;
                errorText.IsVisible = true;
                return;
            }

            dialog.Close(first);
        };

        Grid.SetColumn(cancelButton, 0);
        Grid.SetColumn(confirmButton, 2);

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        layout.Children.Add(buttons);

        dialog.Content = layout;

        return await dialog.ShowDialog<string?>(owner);
    }

    private sealed class Option
    {
        public Option(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }

    private sealed class CardException : Exception
    {
        public CardException(string message)
            : base(message)
        {
        }
    }
}