using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using mentora.Models;

namespace mentora.Views
{
    public class GoalRow
    {
        public int IdGoal { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
        public string DueText { get; set; } = "";
        public bool HasDue => !string.IsNullOrWhiteSpace(DueText);
        public List<GoalStepRow> Steps { get; set; } = new();

        public int Total => Steps.Count;
        public int Done => Steps.Count(s => s.IsDone);
        public double Percent => Total == 0 ? 0 : Done * 100.0 / Total;
        public string ProgressText => "Выполнено шагов: " + Done + " из " + Total;

        public static async Task<List<GoalRow>> LoadAsync(IQueryable<Goal> query)
        {
            var goals = await query
                .Include(g => g.GoalSteps)
                .OrderBy(g => g.CreatedAt)
                .ToListAsync();

            return goals.Select(g => new GoalRow
            {
                IdGoal = g.IdGoal,
                Title = g.Title,
                Description = g.Description ?? "",
                DueText = g.DueDate.HasValue ? "Срок: " + g.DueDate.Value.ToLocalTime().ToString("dd.MM.yyyy") : "",
                Steps = g.GoalSteps.OrderBy(s => s.CreatedAt).Select(s => new GoalStepRow
                {
                    IdGoalStep = s.IdGoalStep,
                    Title = s.Title,
                    IsDone = s.IsDone,
                    DoneText = s.IsDone && s.DoneAt.HasValue ? "Выполнено: " + s.DoneAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "",
                    Mark = s.IsDone ? "✓" : "○",
                    MarkBrush = s.IsDone ? Brush.Parse("#278450") : Brush.Parse("#747C90")
                }).ToList()
            }).ToList();
        }
    }

    public class GoalStepRow
    {
        public int IdGoalStep { get; set; }
        public string Title { get; set; } = "";
        public bool IsDone { get; set; }
        public string DoneText { get; set; } = "";
        public string Mark { get; set; } = "";
        public IBrush MarkBrush { get; set; } = Brushes.Transparent;
    }
}