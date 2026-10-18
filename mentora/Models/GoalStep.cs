using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mentora.Models
{
    public partial class GoalStep
    {
        public int IdGoalStep { get; set; }
        public int IdGoal { get; set; }
        public string Title { get; set; } = null!;
        public bool IsDone { get; set; }
        public DateTime? DoneAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual Goal IdGoalNavigation { get; set; } = null!;
    }
}
