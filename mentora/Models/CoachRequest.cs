using System;

namespace mentora.Models;

public partial class CoachRequest
{
    public const short Pending = 1;
    public const short Accepted = 2;
    public const short Rejected = 3;

    public int IdCoachRequest { get; set; }

    public int IdStudent { get; set; }

    public int IdCoach { get; set; }

    public short Status { get; set; } = Pending;

    public DateTime CreatedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public int? DecidedBy { get; set; }
}