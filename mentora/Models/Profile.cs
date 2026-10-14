using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class Profile
{
    public int IdUser { get; set; }

    public string AcademicGoal { get; set; } = null!;

    public string Interest { get; set; } = null!;

    public int IdSpecialty { get; set; }

    public int IdSector { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Assignment? AssignmentIdCoachNavigation { get; set; }

    public virtual Assignment? AssignmentIdStudentNavigation { get; set; }

    public virtual Sector IdSectorNavigation { get; set; } = null!;

    public virtual Specialty IdSpecialtyNavigation { get; set; } = null!;

    public virtual User IdUserNavigation { get; set; } = null!;
}
