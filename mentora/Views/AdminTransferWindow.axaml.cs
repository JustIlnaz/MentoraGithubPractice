using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class AdminTransferWindow : Window
{
    private int _typing;
    private int _version;

    public int NewCoachId { get; private set; }

    public string Reason { get; private set; } = "";

    public AdminTransferWindow() : this("", "")
    {
    }

    public AdminTransferWindow(string studentLogin, string currentCoachLogin)
    {
        InitializeComponent();

        InfoText.Text = "Студент «" + studentLogin + "» сейчас закреплён за коучем «" +
                        currentCoachLogin + "». Текущее назначение будет закрыто " +
                        "как отменённое, а для нового коуча создано новое. " +
                        "История и встречи останутся в старом назначении.";

        Opened += async (_, _) => await ReloadCoachesAsync();
    }

    private async void CoachSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        int typing = ++_typing;

        await Task.Delay(300);

        if (typing != _typing)
            return;

        await ReloadCoachesAsync();
    }

    private async Task ReloadCoachesAsync()
    {
        int version = ++_version;
        int? selectedId = (CoachComboBox.SelectedItem as Candidate)?.Id;

        try
        {
            var items = await LoadCoachesAsync(CoachSearchBox.Text?.Trim() ?? "");

            if (version != _version)
                return;

            CoachComboBox.ItemsSource = items;
            CoachComboBox.SelectedItem = items.FirstOrDefault(c => c.Id == selectedId);
            CoachEmptyText.IsVisible = items.Count == 0;
        }
        catch (Exception)
        {
            MessageText.Text =
                "Не удалось загрузить коучей. Проверьте подключение к базе.";
            MessageText.IsVisible = true;
        }
    }

    private static async Task<List<Candidate>> LoadCoachesAsync(string search)
    {
        await using var db = new MentoraContext();

        IQueryable<User> query = db.Users
            .AsNoTracking()
            .Where(u =>
                u.IdRole == 2
                && u.IdUserStatus == 1
                && u.Course >= 2
                && u.Course <= 4
                && u.Profile != null
                && !db.Assignments.Any(a =>
                    a.IdAssignmentStatus == 1
                    && a.IdCoach == u.IdUser));

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = "%" + EscapeLike(search) + "%";

            query = query.Where(u =>
                EF.Functions.ILike(u.Login, pattern, "\\"));
        }

        var rows = await query
            .OrderBy(u => u.Login)
            .Take(50)
            .Select(u => new
            {
                u.IdUser,
                u.Login,
                u.Course,
                Specialty = u.Profile != null
                    ? u.Profile.IdSpecialtyNavigation.Abbreviation
                    : "",
                Sector = u.Profile != null
                    ? u.Profile.IdSectorNavigation.Name
                    : ""
            })
            .ToListAsync();

        return rows
            .Select(r => new Candidate(
                r.IdUser,
                r.Login + " · " + r.Course + " курс · " + r.Specialty + " · " + r.Sector))
            .ToList();
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        CoachError.IsVisible = false;
        ReasonError.IsVisible = false;

        bool valid = true;

        var coach = CoachComboBox.SelectedItem as Candidate;
        string reason = ReasonTextBox.Text?.Trim() ?? "";

        if (coach == null)
        {
            CoachError.Text = "Выберите коуча.";
            CoachError.IsVisible = true;
            valid = false;
        }

        if (reason.Length < 5 || reason.Length > 1000)
        {
            ReasonError.Text = "Причина должна содержать от 5 до 1000 символов.";
            ReasonError.IsVisible = true;
            valid = false;
        }

        if (!valid)
            return;

        NewCoachId = coach!.Id;
        Reason = reason;

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private sealed class Candidate
    {
        public Candidate(int id, string text)
        {
            Id = id;
            Text = text;
        }

        public int Id { get; }

        public string Text { get; }

        public override string ToString() => Text;
    }
}