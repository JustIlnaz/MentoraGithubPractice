using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class ProgramSetting
{
    public int IdSetting { get; set; }

    public bool RequireSameSpecialty { get; set; }

    public bool CoachCanComplete { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? UpdatedByNavigation { get; set; }
}
