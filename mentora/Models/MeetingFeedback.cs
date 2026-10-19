using System;

namespace mentora.Models;

public partial class MeetingFeedback
{
    public int IdFeedback { get; set; }

    public int IdMeeting { get; set; }

    public int IdStudent { get; set; }

    public int IdCoach { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Meeting IdMeetingNavigation { get; set; } = null!;

    public virtual User IdStudentNavigation { get; set; } = null!;

    public virtual User IdCoachNavigation { get; set; } = null!;
}