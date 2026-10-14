using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class Meeting
{
    public int IdMeeting { get; set; }

    public Guid RequestId { get; set; }

    public int IdAssignment { get; set; }

    public DateTime MeetingDate { get; set; }

    public string Topic { get; set; } = null!;

    public string Summary { get; set; } = null!;

    public string? CoachComment { get; set; }

    public int CreatedBy { get; set; }

    public int UpdatedBy { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual Assignment IdAssignmentNavigation { get; set; } = null!;

    public virtual User UpdatedByNavigation { get; set; } = null!;
}
