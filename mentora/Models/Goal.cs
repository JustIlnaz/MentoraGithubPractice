using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mentora.Models
{
    public partial class Goal
    {
        public int IdGoal { get; set; }
        public int IdAssignment { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual Assignment IdAssignmentNavigation { get; set; } = null!;
        public virtual ICollection<GoalStep> GoalSteps { get; set; } = new List<GoalStep>();
    }
}
