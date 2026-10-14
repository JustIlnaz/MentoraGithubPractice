using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class AssignmentStatus
{
    public int IdAssignmentStatus { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
