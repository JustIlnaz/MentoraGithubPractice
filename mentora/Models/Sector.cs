using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class Sector
{
    public int IdSector { get; set; }

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}
