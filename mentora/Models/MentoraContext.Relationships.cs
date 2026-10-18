using Microsoft.EntityFrameworkCore;

namespace mentora.Models;

public partial class MentoraContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>()
            .Ignore(p => p.AssignmentIdCoachNavigation);

        modelBuilder.Entity<Profile>()
            .Ignore(p => p.AssignmentIdStudentNavigation);

        modelBuilder.Entity<Assignment>()
            .HasOne(a => a.IdCoachNavigation)
            .WithMany()
            .HasForeignKey(a => a.IdCoach)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Assignment>()
            .HasOne(a => a.IdStudentNavigation)
            .WithMany()
            .HasForeignKey(a => a.IdStudent)
            .OnDelete(DeleteBehavior.Restrict);

        ConfigureGoals(modelBuilder);
    }
}