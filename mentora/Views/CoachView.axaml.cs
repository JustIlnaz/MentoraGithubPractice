using System;
using System.Linq;
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

public partial class CoachView : UserControl
{
    private const int PageSize = 5;

    private bool _isBusy;
    private bool _hasStudent;
    private bool _requireSameSpecialty;
    private bool _searchOpen;

    private int _coachSpecialtyId;
    private int _currentPage = 1;
    private int _totalCount;

    public CoachView()
    {
        InitializeComponent();
        Loaded += CoachView_Loaded;
    }

    private async void CoachView_Loaded(object? sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_isBusy)
            return;

        if (!UserSession.IsAuthorized || UserSession.IdRole != 2)
        {
            ReturnToLogin();
            return;
        }

        int coachId = UserSession.IdUser;

        SetBusy(true);
        DataContainer.IsVisible = false;
        ShowMessage("Загрузка данных…", false);

        try
        {
            await using var db = new MentoraContext();

            var user = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.IdUser == coachId);

            if (user == null
                || user.IdRole != 2
                || user.IdUserStatus != 1
                || user.Course is not (2 or 3 or 4))
            {
                if (IsCurrentCoach(coachId))
                    ReturnToLogin();

                return;
            }

            var profile = await db.Profiles
                .AsNoTracking()
                .Include(p => p.IdSpecialtyNavigation)
                .Include(p => p.IdSectorNavigation)
                .SingleOrDefaultAsync(p => p.IdUser == coachId);

            if (profile == null)
            {
                ShowMessage("Анкета коуча не найдена.");
                return;
            }

            var assignment = await db.Assignments
                .AsNoTracking()
                .SingleOrDefaultAsync(a =>
                    a.IdCoach == coachId
                    && a.IdAssignmentStatus == 1);

            Profile? studentProfile = null;

            if (assignment != null)
            {
                studentProfile = await db.Profiles
                    .AsNoTracking()
                    .Include(p => p.IdUserNavigation)
                    .Include(p => p.IdSpecialtyNavigation)
                    .Include(p => p.IdSectorNavigation)
                    .SingleOrDefaultAsync(p =>
                        p.IdUser == assignment.IdStudent);

                if (studentProfile == null)
                {
                    ShowMessage("Анкета назначенного студента не найдена.");
                    return;
                }
            }

            var settings = await db.ProgramSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.IdSetting == 1);

            if (settings == null)
            {
                ShowMessage("Настройки программы не найдены.");
                return;
            }

            var specialties = await db.Specialties
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Abbreviation)
                .ToListAsync();

            var sectors = await db.Sectors
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();

            if (!IsCurrentCoach(coachId))
                return;

            UserSession.SetUser(user);

            HeaderNameText.Text = UserSession.DisplayName;

            LoginValue.Text = user.Login;
            CourseValue.Text = user.Course + " курс";
            SpecialtyValue.Text = profile.IdSpecialtyNavigation.Abbreviation;
            SectorValue.Text = profile.IdSectorNavigation.Name;
            GoalValue.Text = profile.AcademicGoal;
            InterestValue.Text = profile.Interest;

            _coachSpecialtyId = profile.IdSpecialty;
            _requireSameSpecialty = settings.RequireSameSpecialty;
            _hasStudent = assignment != null;

            _assignmentId = assignment?.IdAssignment;

            NoStudentPanel.IsVisible = !_hasStudent;
            StudentCard.IsVisible = _hasStudent;
            StudentsButton.IsEnabled = !_hasStudent;

            if (studentProfile != null)
            {
                StudentLoginValue.Text = studentProfile.IdUserNavigation.Login;
                StudentSpecialtyValue.Text = studentProfile.IdSpecialtyNavigation.Abbreviation;
                StudentSectorValue.Text = studentProfile.IdSectorNavigation.Name;
                StudentGoalValue.Text = studentProfile.AcademicGoal;
                StudentInterestValue.Text = studentProfile.Interest;
            }

            SpecialtyFilter.ItemsSource = specialties;
            SpecialtyFilter.SelectedItem = specialties
                .FirstOrDefault(s => s.IdSpecialty == _coachSpecialtyId);

            SpecialtyFilter.IsEnabled = !_requireSameSpecialty;
            RestrictionText.IsVisible = _requireSameSpecialty;

            sectors.Insert(0, new Sector
            {
                IdSector = 0,
                Name = "Все секторы"
            });

            SectorFilter.ItemsSource = sectors;
            SectorFilter.SelectedIndex = 0;

            GoalFilter.Text = "";
            InterestFilter.Text = "";

            _currentPage = 1;
            StudentsList.ItemsSource = null;
            PaginationPanel.IsVisible = false;

            ShowProfile();

            MessageBorder.IsVisible = false;
            DataContainer.IsVisible = true;
        }
        catch (Exception)
        {
            if (IsCurrentCoach(coachId))
            {
                ShowMessage(
                    "Не удалось загрузить данные. " +
                    "Проверьте подключение и нажмите «Обновить».");
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ProfileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isBusy)
            ShowProfile();
    }

    private void ShowProfile()
    {
        _searchOpen = false;

        ProfilePanel.IsVisible = true;
        SearchPanel.IsVisible = false;

        ProfileButton.Classes.Add("selected");
        StudentsButton.Classes.Remove("selected");

        PageTitleText.Text = "Мой профиль";

        GoalsPanel.IsVisible = false;
        GoalsButton.Classes.Remove("selected");
    }

    private async void StudentsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _hasStudent)
            return;

        _searchOpen = true;

        ProfilePanel.IsVisible = false;
        SearchPanel.IsVisible = true;

        GoalsPanel.IsVisible = false;
        GoalsButton.Classes.Remove("selected");

        ProfileButton.Classes.Remove("selected");
        StudentsButton.Classes.Add("selected");

        PageTitleText.Text = "Поиск студентов";
        _currentPage = 1;

        await SearchAsync();
    }

    private async void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        _currentPage = 1;
        await SearchAsync();
    }

    private async void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        if (SpecialtyFilter.ItemsSource
            is System.Collections.Generic.IEnumerable<Specialty> specialties)
        {
            SpecialtyFilter.SelectedItem = specialties
                .FirstOrDefault(s =>
                    s.IdSpecialty == _coachSpecialtyId);
        }

        SectorFilter.SelectedIndex = 0;
        GoalFilter.Text = "";
        InterestFilter.Text = "";

        _currentPage = 1;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_isBusy || !_searchOpen)
            return;

        StudentsList.ItemsSource = null;
        PaginationPanel.IsVisible = false;
        ResultsText.Text = "";
        MessageBorder.IsVisible = false;

        if (SpecialtyFilter.SelectedItem is not Specialty selected)
        {
            ShowMessage(
                "Выберите специальность. Если ваша специальность " +
                "архивирована и выбор другой запрещён, " +
                "обратитесь к администратору.");

            return;
        }

        int coachId = UserSession.IdUser;
        int specialtyId = selected.IdSpecialty;

        int sectorId =
            (SectorFilter.SelectedItem as Sector)?.IdSector ?? 0;

        string goal = GoalFilter.Text?.Trim() ?? "";
        string interest = InterestFilter.Text?.Trim() ?? "";

        SetBusy(true);

        try
        {
            await using var db = new MentoraContext();

            var coach = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.IdUser == coachId);

            if (coach == null
                || coach.IdRole != 2
                || coach.IdUserStatus != 1
                || coach.Course is not (2 or 3 or 4))
            {
                if (IsCurrentCoach(coachId))
                    ReturnToLogin();

                return;
            }

            bool alreadyAssigned = await db.Assignments.AnyAsync(a =>
                a.IdCoach == coachId
                && a.IdAssignmentStatus == 1);

            if (alreadyAssigned)
            {
                _hasStudent = true;
                StudentsButton.IsEnabled = false;

                ShowMessage(
                    "У вас уже есть активный студент. " +
                    "Нажмите «Обновить», чтобы открыть его данные.");

                return;
            }

            var settings = await db.ProgramSettings
                .AsNoTracking()
                .SingleAsync(s => s.IdSetting == 1);

            var profile = await db.Profiles
                .AsNoTracking()
                .SingleAsync(p => p.IdUser == coachId);

            bool specialtyActive = await db.Specialties.AnyAsync(s =>
                s.IdSpecialty == specialtyId && s.IsActive);

            if (!specialtyActive)
            {
                ShowMessage(
                    "Специальность больше недоступна. Нажмите «Обновить».");

                return;
            }

            if (settings.RequireSameSpecialty
                && profile.IdSpecialty != specialtyId)
            {
                ShowMessage(
                    "Можно выбирать только студентов своей специальности. " +
                    "Нажмите «Обновить».");

                return;
            }

            var query = db.Profiles
                .AsNoTracking()
                .Where(p =>
                    p.IdSpecialty == specialtyId
                    && p.IdUserNavigation.IdRole == 1
                    && p.IdUserNavigation.Course == 1
                    && p.IdUserNavigation.IdUserStatus == 1
                    && !db.Assignments.Any(a =>
                        a.IdStudent == p.IdUser
                        && a.IdAssignmentStatus == 1));

            if (sectorId != 0)
                query = query.Where(p => p.IdSector == sectorId);

            if (!string.IsNullOrWhiteSpace(goal))
            {
                string pattern = "%" + EscapeLike(goal) + "%";

                query = query.Where(p =>
                    EF.Functions.ILike(
                        p.AcademicGoal, pattern, "\\"));
            }

            if (!string.IsNullOrWhiteSpace(interest))
            {
                string pattern = "%" + EscapeLike(interest) + "%";

                query = query.Where(p =>
                    EF.Functions.ILike(
                        p.Interest, pattern, "\\"));
            }

            int totalCount = await query.CountAsync();

            int totalPages = Math.Max(
                1,
                (int)Math.Ceiling(totalCount / (double)PageSize));

            int page = Math.Clamp(_currentPage, 1, totalPages);

            var students = await query
                .OrderBy(p => p.IdUserNavigation.Login)
                .ThenBy(p => p.IdUser)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Select(p => new CoachStudentRow
                {
                    IdUser = p.IdUser,
                    Login = p.IdUserNavigation.Login,
                    Specialty = p.IdSpecialtyNavigation.Abbreviation,
                    Sector = p.IdSectorNavigation.Name,
                    AcademicGoal = p.AcademicGoal,
                    Interest = p.Interest
                })
                .ToListAsync();

            if (!IsCurrentCoach(coachId))
                return;

            _totalCount = totalCount;
            _currentPage = page;

            StudentsList.ItemsSource = students;

            ResultsText.Text = totalCount == 0
                ? "Свободные студенты по выбранной специальности не найдены."
                : "Найдено студентов: " + totalCount;

            PaginationPanel.IsVisible = totalCount > PageSize;

            PreviousButton.IsEnabled = page > 1;
            NextButton.IsEnabled = page < totalPages;

            PageNumberText.Text = page + " / " + totalPages;
        }
        catch (Exception)
        {
            if (IsCurrentCoach(coachId))
            {
                ShowMessage(
                    "Не удалось выполнить поиск. " +
                    "Проверьте подключение и повторите попытку.");
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string EscapeLike(string value)
    {
        return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    private async void PreviousButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentPage <= 1)
            return;

        _currentPage--;
        await SearchAsync();
    }

    private async void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentPage * PageSize >= _totalCount)
            return;

        _currentPage++;
        await SearchAsync();
    }

    private async void OpenStudentButton_Click(object? sender, RoutedEventArgs e)
    {
        await SelectStudentAsync(sender, e);
    }

    private async void SelectStudentButton_Click(object? sender, RoutedEventArgs e)
    {
        await SelectStudentAsync(sender, e);
    }

    private async Task SelectStudentAsync(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        if (sender is not Button button
            || button.Tag is not CoachStudentRow student
            || TopLevel.GetTopLevel(this) is not MainWindow owner)
        {
            return;
        }

        int coachId = UserSession.IdUser;

        SetBusy(true);

        bool assigned = false;
        string? error = null;

        try
        {
            bool confirmed = await ConfirmSelectionAsync(owner, student);

            if (!confirmed || !IsCurrentCoach(coachId))
                return;

            await CreateAssignmentAsync(coachId, student.IdUser);
            assigned = true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: "23505"
            })
        {
            error =
                "Студент уже выбран другим коучем " +
                "или у вас уже появилось активное назначение.";
        }
        catch (AssignmentValidationException ex)
        {
            error = ex.Message;
        }
        catch (Exception)
        {
            error =
                "Не удалось подтвердить назначение. " +
                "Обновите данные перед повторной попыткой.";
        }
        finally
        {
            SetBusy(false);
        }

        if (!IsCurrentCoach(coachId))
            return;

        if (assigned)
        {
            await LoadDataAsync();

            if (IsCurrentCoach(coachId))
            {
                ShowMessage(
                    "Студент успешно закреплён за вами.",
                    false);
            }
        }
        else if (error != null)
        {
            await SearchAsync();

            if (IsCurrentCoach(coachId))
                ShowMessage(error);
        }
    }

    private async Task CreateAssignmentAsync(int coachId, int studentId)
    {
        await using var db = new MentoraContext();

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var settings = await db.ProgramSettings
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM program_settings
                                  WHERE id_setting = 1
                                  FOR SHARE
                                  """)
            .AsNoTracking()
            .SingleOrDefaultAsync();

        if (settings == null)
        {
            throw new AssignmentValidationException(
                "Настройки программы не найдены.");
        }

        var users = await db.Users
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM users
                                  WHERE id_user IN ({coachId}, {studentId})
                                  ORDER BY id_user
                                  FOR UPDATE
                                  """)
            .AsNoTracking()
            .ToListAsync();

        var coach = users.SingleOrDefault(u => u.IdUser == coachId);
        var student = users.SingleOrDefault(u => u.IdUser == studentId);

        if (coach == null
            || coach.IdRole != 2
            || coach.IdUserStatus != 1
            || coach.Course is not (2 or 3 or 4))
        {
            throw new AssignmentValidationException(
                "Ваша учётная запись больше не может выбирать студентов.");
        }

        if (student == null
            || student.IdRole != 1
            || student.IdUserStatus != 1
            || student.Course != 1)
        {
            throw new AssignmentValidationException(
                "Этот студент больше недоступен для назначения.");
        }

        var profiles = await db.Profiles
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM profiles
                                  WHERE id_user IN ({coachId}, {studentId})
                                  ORDER BY id_user
                                  FOR SHARE
                                  """)
            .AsNoTracking()
            .ToListAsync();

        var coachProfile =
            profiles.SingleOrDefault(p => p.IdUser == coachId);

        var studentProfile =
            profiles.SingleOrDefault(p => p.IdUser == studentId);

        if (coachProfile == null || studentProfile == null)
        {
            throw new AssignmentValidationException(
                "Анкета одного из участников не найдена.");
        }

        var specialty = await db.Specialties
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM specialties
                                  WHERE id_specialty = {studentProfile.IdSpecialty}
                                  FOR SHARE
                                  """)
            .AsNoTracking()
            .SingleOrDefaultAsync();

        if (specialty == null || !specialty.IsActive)
        {
            throw new AssignmentValidationException(
                "Специальность студента больше недоступна для выбора.");
        }

        if (settings.RequireSameSpecialty
            && coachProfile.IdSpecialty != studentProfile.IdSpecialty)
        {
            throw new AssignmentValidationException(
                "Разрешено выбирать только студентов своей специальности.");
        }

        bool coachBusy = await db.Assignments.AnyAsync(a =>
            a.IdCoach == coachId && a.IdAssignmentStatus == 1);

        if (coachBusy)
        {
            throw new AssignmentValidationException(
                "У вас уже есть активный студент.");
        }

        bool studentBusy = await db.Assignments.AnyAsync(a =>
            a.IdStudent == studentId && a.IdAssignmentStatus == 1);

        if (studentBusy)
        {
            throw new AssignmentValidationException(
                "Студент уже выбран другим коучем.");
        }

        DateTime now = DateTime.UtcNow;

        var assignment = new Assignment
        {
            RequestId = Guid.NewGuid(),
            IdCoach = coachId,
            IdStudent = studentId,
            IdAssignmentStatus = 1,
            CreatedBy = coachId,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        db.ActionLogs.Add(new ActionLog
        {
            IdUser = coachId,
            ActionType = "Создание назначения",
            ObjectType = "Назначение",
            ObjectId = assignment.IdAssignment,
            OccurredAt = now,
            IsSuccess = true,
            Description =
                "Коуч " + coach.Login +
                " выбрал студента " + student.Login + "."
        });

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task<bool> ConfirmSelectionAsync(Window owner, CoachStudentRow student)
    {
        var dialog = new Window
        {
            Title = "Подтверждение назначения",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };

        var layout = new StackPanel
        {
            Margin = new Thickness(26),
            Spacing = 18
        };

        var heading = new StackPanel
        {
            Spacing = 8
        };

        heading.Children.Add(new TextBlock
        {
            Text = "Выбрать студента?",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D"),
            TextWrapping = TextWrapping.Wrap
        });

        heading.Children.Add(new TextBlock
        {
            Text = "Студент будет закреплён за вами. " +
                   "Заменить его сможет администратор.",
            FontSize = 14,
            Foreground = Brush.Parse("#747C90"),
            TextWrapping = TextWrapping.Wrap
        });

        layout.Children.Add(heading);


        var studentInfo = new StackPanel
        {
            Spacing = 6
        };

        studentInfo.Children.Add(new TextBlock
        {
            Text = student.Login,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#19234D"),
            TextWrapping = TextWrapping.Wrap
        });

        studentInfo.Children.Add(new TextBlock
        {
            Text = student.Specialty + " · " + student.Sector,
            FontSize = 13,
            Foreground = Brush.Parse("#747C90"),
            TextWrapping = TextWrapping.Wrap
        });

        layout.Children.Add(new Border
        {
            Background = Brush.Parse("#F3F2FF"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = studentInfo
        });


        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "Отмена",
            Height = 44,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            Foreground = Brush.Parse("#39446B"),
            BorderBrush = Brush.Parse("#DDE0E8"),
            BorderThickness = new Thickness(1),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(
                Avalonia.Input.StandardCursorType.Hand)
        };

        var confirmButton = new Button
        {
            Content = "Подтвердить",
            Height = 44,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = Brush.Parse("#5B4BFF"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(
                Avalonia.Input.StandardCursorType.Hand)
        };

        cancelButton.Click += (_, _) => dialog.Close(false);
        confirmButton.Click += (_, _) => dialog.Close(true);

        Grid.SetColumn(cancelButton, 0);
        Grid.SetColumn(confirmButton, 2);

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        layout.Children.Add(buttons);

        dialog.Content = layout;

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                dialog.Close(false);
            }
        };

        return await dialog.ShowDialog<bool>(owner);
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;

        RefreshButton.IsEnabled = !isBusy;
        DataContainer.IsEnabled = !isBusy;
        ProfileButton.IsEnabled = !isBusy;
        StudentsButton.IsEnabled = !isBusy && !_hasStudent;

        GoalsButton.IsEnabled = !isBusy && _hasStudent;
    }

    private bool IsCurrentCoach(int coachId)
    {
        return UserSession.IsAuthorized
               && UserSession.IdRole == 2
               && UserSession.IdUser == coachId
               && TopLevel.GetTopLevel(this) is MainWindow;
    }

    private void ShowMessage(string text, bool isError = true)
    {
        MessageText.Text = text;
        MessageText.Foreground =
            Brush.Parse(isError ? "#B52C2C" : "#278450");

        MessageBorder.IsVisible = true;
        MessageBorder.BringIntoView();
    }

    private void LogoutButton_Click(object? sender, RoutedEventArgs e)
    {
        ReturnToLogin();
    }

    private void ReturnToLogin()
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window)
            window.ShowLogin();
    }

    private sealed class AssignmentValidationException : Exception
    {
        public AssignmentValidationException(string message)
            : base(message)
        {
        }
    }
}

public class CoachStudentRow
{
    public int IdUser { get; set; }

    public string Login { get; set; } = "";

    public string Specialty { get; set; } = "";

    public string Sector { get; set; } = "";

    public string AcademicGoal { get; set; } = "";

    public string Interest { get; set; } = "";

    public string Details => Specialty + " · " + Sector;
}