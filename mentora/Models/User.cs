using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class User
{
    public int IdUser { get; set; }

    public string Login { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string? FullName { get; set; }

    public int IdRole { get; set; }

    public short? Course { get; set; }

    public int IdUserStatus { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public virtual ICollection<ActionLog> ActionLogs { get; set; } = new List<ActionLog>();

    public virtual ICollection<Assignment> AssignmentClosedByNavigations { get; set; } = new List<Assignment>();

    public virtual ICollection<Assignment> AssignmentCreatedByNavigations { get; set; } = new List<Assignment>();

    public virtual Role IdRoleNavigation { get; set; } = null!;

    public virtual UserStatus IdUserStatusNavigation { get; set; } = null!;

    public virtual ICollection<Meeting> MeetingCreatedByNavigations { get; set; } = new List<Meeting>();

    public virtual ICollection<Meeting> MeetingUpdatedByNavigations { get; set; } = new List<Meeting>();

    public virtual Profile? Profile { get; set; }

    public virtual ICollection<ProgramSetting> ProgramSettings { get; set; } = new List<ProgramSetting>();
}
