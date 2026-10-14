using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class UserStatus
{
    public int IdUserStatus { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
