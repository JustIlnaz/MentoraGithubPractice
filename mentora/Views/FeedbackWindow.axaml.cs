using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views;

public partial class FeedbackWindow : Window
{
    private readonly int _meetingId;
    private readonly Button[] _stars;

    private int _rating;
    private bool _isSaving;

    public FeedbackWindow() : this(0, "")
    {
    }

    public FeedbackWindow(int meetingId, string topic)
    {
        InitializeComponent();

        _meetingId = meetingId;
        _stars = new[] { Star1, Star2, Star3, Star4, Star5 };

        TopicText.Text = "Встреча: " + topic;
    }

    private void Star_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int value))
        {
            _rating = value;

            for (int i = 0; i < _stars.Length; i++)
            {
                _stars[i].Foreground = Brush.Parse(
                    i < _rating ? "#F5A623" : "#C5CAE0");
            }

            RatingText.Text = "Ваша оценка: " + _rating + " из 5";
            ErrorText.IsVisible = false;
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void SubmitButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        if (_rating < 1 || _rating > 5)
        {
            ShowError("Поставьте оценку от 1 до 5.");
            return;
        }

        if (!UserSession.IsAuthorized || UserSession.IdRole != 1)
        {
            Close(false);
            return;
        }

        int studentId = UserSession.IdUser;
        string comment = (CommentBox.Text ?? "").Trim();

        _isSaving = true;
        SubmitButton.IsEnabled = false;

        try
        {
            await using var db = new MentoraContext();

            var meeting = await db.Meetings
                .AsNoTracking()
                .Where(m => m.IdMeeting == _meetingId && !m.IsArchived)
                .Select(m => new
                {
                    m.MeetingDate,
                    StudentId = m.IdAssignmentNavigation.IdStudent,
                    CoachId = m.IdAssignmentNavigation.IdCoach
                })
                .SingleOrDefaultAsync();

            if (meeting == null || meeting.StudentId != studentId)
            {
                ShowError("Встреча не найдена.");
                return;
            }

            if (meeting.MeetingDate > DateTime.UtcNow)
            {
                ShowError("Отзыв можно оставить только после встречи.");
                return;
            }

            bool alreadyExists = await db.MeetingFeedbacks
                .AnyAsync(f => f.IdMeeting == _meetingId);

            if (alreadyExists)
            {
                ShowError("Вы уже оставили отзыв на эту встречу.");
                return;
            }

            db.MeetingFeedbacks.Add(new MeetingFeedback
            {
                IdMeeting = _meetingId,
                IdStudent = studentId,
                IdCoach = meeting.CoachId,
                Rating = _rating,
                Comment = comment.Length == 0 ? null : comment
            });

            await db.SaveChangesAsync();

            Close(true);
        }
        catch (DbUpdateException)
        {
            ShowError(
                "Не удалось сохранить отзыв. " +
                "Возможно, на эту встречу он уже оставлен.");
        }
        catch (Exception)
        {
            ShowError(
                "Не удалось сохранить отзыв. " +
                "Проверьте подключение к базе.");
        }
        finally
        {
            _isSaving = false;
            SubmitButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}