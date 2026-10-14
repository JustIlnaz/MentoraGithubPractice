using System;
using System.Collections.Generic;

namespace mentora.Models;

public partial class ActionLog
{
    public long IdActionLog { get; set; }

    public int? IdUser { get; set; }

    public string ActionType { get; set; } = null!;

    public string? ObjectType { get; set; }

    public long? ObjectId { get; set; }

    public DateTime OccurredAt { get; set; }

    public bool IsSuccess { get; set; }

    public string? Description { get; set; }

    public string? TechnicalInfo { get; set; }

    public virtual User? IdUserNavigation { get; set; }
}
