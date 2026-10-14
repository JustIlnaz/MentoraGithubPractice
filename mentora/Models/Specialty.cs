using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class Specialty
{
    public int IdSpecialty { get; set; }

    public string Abbreviation { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}
