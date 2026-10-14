using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using mentora.Models;
using mentora.Views;

namespace mentora;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        UserSession.Clear();
        ShowLogin();

        Closed += (_, _) => UserSession.Clear();
    }

    public void ShowLogin(string registeredLogin = "")
    {
        UserSession.Clear();

        Title = "Mentora — Вход";

        var loginView = new LoginView();

        if (!string.IsNullOrWhiteSpace(registeredLogin))
        {
            loginView.ShowRegistrationSuccess(registeredLogin);
        }

        MainContent.Content = loginView;
    }

    public void ShowRegistration()
    {
        UserSession.Clear();

        Title = "Mentora — Регистрация";
        MainContent.Content = new RegistrationView();
    }

    public void ShowAuthorizedPage()
    {
        if (!UserSession.IsAuthorized)
        {
            ShowLogin();
            return;
        }

        switch (UserSession.IdRole)
        {
            case 1:
                Title = "Mentora — Личный кабинет студента";
                MainContent.Content = new StudentView();
                break;

            case 2:
                Title = "Mentora — Личный кабинет коуча";
                MainContent.Content = new CoachView();
                break;

            case 3:
                Title = "Mentora — Администратор";
                MainContent.Content = new AdminView();
                break;

            default:
                ShowLogin();
                break;
        }
    }
}