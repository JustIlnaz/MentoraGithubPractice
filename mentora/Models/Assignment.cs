using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class Assignment
{
    public int IdAssignment { get; set; }

    public Guid RequestId { get; set; }

    public int IdCoach { get; set; }

    public int IdStudent { get; set; }

    public int IdAssignmentStatus { get; set; }

    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public int? ClosedBy { get; set; }

    public string? ClosingReason { get; set; }

    public virtual User? ClosedByNavigation { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual AssignmentStatus IdAssignmentStatusNavigation { get; set; } = null!;

    public virtual Profile IdCoachNavigation { get; set; } = null!;

    public virtual Profile IdStudentNavigation { get; set; } = null!;

    public virtual ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();
}
