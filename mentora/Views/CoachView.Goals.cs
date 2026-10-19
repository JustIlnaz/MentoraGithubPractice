using Avalonia.Controls;
using Avalonia.Interactivity;
using mentora.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace mentora.Views
{
    public partial class CoachView
    {
        private int? _assignmentId;

        private async void GoalsButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_isBusy) return;

            ProfilePanel.IsVisible = false;
            SearchPanel.IsVisible = false;
            GoalsPanel.IsVisible = true;

            ProfileButton.Classes.Remove("selected");
            StudentsButton.Classes.Remove("selected");
            GoalsButton.Classes.Add("selected");

            PageTitleText.Text = "Цели студента";

            SetBusy(true);
            try
            {
                await LoadGoalsAsync();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task LoadGoalsAsync()
        {
            if (_assignmentId == null) return;

            try
            {
                await using var db = new MentoraContext();

                // ПРОВЕРКА ПРАВ: убеждаемся, что назначение принадлежит текущему коучу
                var assignment = await db.Assignments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.IdAssignment == _assignmentId && a.IdCoach == UserSession.IdUser);

                if (assignment == null || assignment.IdAssignmentStatus != 1)
                {
                    ShowMessage("Ошибка доступа: это назначение не принадлежит вам или уже закрыто.");
                    return;
                }

                var meetings = await db.Meetings
                    .Where(m => m.IdAssignment == _assignmentId && !m.IsArchived)
                    .OrderByDescending(m => m.MeetingDate)
                    .ToListAsync();

                MeetingPicker.ItemsSource = meetings;
                if (meetings.Any()) MeetingPicker.SelectedIndex = 0;

                var goalsQuery = db.Goals.Where(g => g.IdAssignment == _assignmentId);
                var goals = await GoalRow.LoadAsync(goalsQuery);

                GoalsList.ItemsSource = goals;
                NoGoalsText.IsVisible = goals.Count == 0;

                int total = goals.Sum(g => g.Total);
                int done = goals.Sum(g => g.Done);
                double percent = total == 0 ? 0 : done * 100.0 / total;

                GoalsSummaryBar.Value = percent;
                GoalsSummaryText.Text = "Выполнено шагов: " + done + " из " + total + " (" + Math.Round(percent) + "%)";
            }
            catch (Exception ex)
            {
                ShowMessage("Ошибка загрузки целей: " + ex.Message);
            }
        }

        private async void AddGoalButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_assignmentId == null || _isBusy) return;

            string title = NewGoalTitleBox.Text?.Trim() ?? "";
            string description = NewGoalDescriptionBox.Text?.Trim() ?? "";
            DateTimeOffset? due = NewGoalDuePicker.SelectedDate;

            if (title.Length < 3)
            {
                NewGoalError.Text = "Название должно содержать минимум 3 символа.";
                NewGoalError.IsVisible = true;
                return;
            }

            NewGoalError.IsVisible = false;
            SetBusy(true);

            try
            {
                await using var db = new MentoraContext();

                var goal = new Goal
                {
                    IdAssignment = _assignmentId.Value,
                    Title = title,
                    Description = string.IsNullOrWhiteSpace(description) ? null : description,
                    DueDate = due?.UtcDateTime,
                    CreatedAt = DateTime.UtcNow
                };
                db.Goals.Add(goal);
                await db.SaveChangesAsync();

                // ВОЗВРАЩАЕМ ЛОГИРОВАНИЕ
                db.ActionLogs.Add(new ActionLog
                {
                    IdUser = UserSession.IdUser,
                    ActionType = "Добавление цели",
                    ObjectType = "Цель",
                    ObjectId = goal.IdGoal,
                    OccurredAt = DateTime.UtcNow,
                    IsSuccess = true,
                    Description = "Коуч добавил цель: " + title
                });
                await db.SaveChangesAsync();

                NewGoalTitleBox.Text = "";
                NewGoalDescriptionBox.Text = "";
                NewGoalDuePicker.SelectedDate = null;

                await LoadGoalsAsync();
            }
            catch (Exception ex)
            {
                NewGoalError.Text = "Ошибка сохранения: " + ex.Message;
                NewGoalError.IsVisible = true;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void AddStepButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not GoalRow goalRow || _isBusy) return;

            if (button.Parent is not Grid grid || grid.Children[0] is not TextBox textBox) return;

            string title = textBox.Text?.Trim() ?? "";
            if (title.Length < 2)
            {
                ShowMessage("Шаг должен содержать минимум 2 символа.");
                return;
            }

            SetBusy(true);
            try
            {
                await using var db = new MentoraContext();

                // ПРОВЕРКА ПРАВ: цель должна принадлежать назначению этого коуча
                var goal = await db.Goals.Include(g => g.IdAssignmentNavigation)
                    .FirstOrDefaultAsync(g => g.IdGoal == goalRow.IdGoal && g.IdAssignmentNavigation.IdCoach == UserSession.IdUser);

                if (goal == null)
                    throw new Exception("Цель не найдена или принадлежит другому коучу.");

                var step = new GoalStep
                {
                    IdGoal = goalRow.IdGoal,
                    Title = title,
                    IsDone = false,
                    CreatedAt = DateTime.UtcNow
                };
                db.GoalSteps.Add(step);
                await db.SaveChangesAsync();

                // ЛОГИРОВАНИЕ
                db.ActionLogs.Add(new ActionLog
                {
                    IdUser = UserSession.IdUser,
                    ActionType = "Добавление шага",
                    ObjectType = "Шаг цели",
                    ObjectId = step.IdGoalStep,
                    OccurredAt = DateTime.UtcNow,
                    IsSuccess = true,
                    Description = "Коуч добавил шаг: " + title
                });
                await db.SaveChangesAsync();

                textBox.Text = "";
                await LoadGoalsAsync();
            }
            catch (Exception ex)
            {
                ShowMessage("Ошибка добавления шага: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void StepCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox || checkBox.Tag is not GoalStepRow stepRow || _isBusy) return;

            SetBusy(true);
            try
            {
                await using var db = new MentoraContext();

                // ПРОВЕРКА ПРАВ: шаг должен принадлежать цели, а цель - назначению этого коуча
                var step = await db.GoalSteps
                    .Include(s => s.IdGoalNavigation)
                    .ThenInclude(g => g.IdAssignmentNavigation)
                    .FirstOrDefaultAsync(s => s.IdGoalStep == stepRow.IdGoalStep
                                              && s.IdGoalNavigation.IdAssignmentNavigation.IdCoach == UserSession.IdUser);

                if (step != null)
                {
                    step.IsDone = !step.IsDone;
                    step.DoneAt = step.IsDone ? DateTime.UtcNow : null;
                    await db.SaveChangesAsync();

                    // ЛОГИРОВАНИЕ
                    db.ActionLogs.Add(new ActionLog
                    {
                        IdUser = UserSession.IdUser,
                        ActionType = step.IsDone ? "Шаг выполнен" : "Шаг отменен",
                        ObjectType = "Шаг цели",
                        ObjectId = step.IdGoalStep,
                        OccurredAt = DateTime.UtcNow,
                        IsSuccess = true,
                        Description = $"Коуч {(step.IsDone ? "отметил" : "снял отметку с")} шага: {step.Title}"
                    });
                    await db.SaveChangesAsync();

                    await LoadGoalsAsync();
                }
                else
                {
                    ShowMessage("Ошибка доступа: этот шаг вам не принадлежит.");
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Ошибка изменения статуса шага: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }
    }
}