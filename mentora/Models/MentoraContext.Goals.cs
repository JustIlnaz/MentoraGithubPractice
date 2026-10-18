using Microsoft.EntityFrameworkCore;

namespace mentora.Models;

public partial class MentoraContext
{
    public virtual DbSet<Goal> Goals { get; set; }
    public virtual DbSet<GoalStep> GoalSteps { get; set; }

    private void ConfigureGoals(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Goal>(entity =>
        {
            entity.HasKey(e => e.IdGoal).HasName("goals_pkey");
            entity.ToTable("goals");
            entity.Property(e => e.IdGoal).HasColumnName("id_goal");
            entity.Property(e => e.IdAssignment).HasColumnName("id_assignment");
            entity.Property(e => e.Title).HasMaxLength(200).HasColumnName("title");
            entity.Property(e => e.Description).HasMaxLength(1000).HasColumnName("description");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("created_at");

            entity.HasOne(d => d.IdAssignmentNavigation).WithMany()
                .HasForeignKey(d => d.IdAssignment)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("goals_id_assignment_fkey");
        });

        modelBuilder.Entity<GoalStep>(entity =>
        {
            entity.HasKey(e => e.IdGoalStep).HasName("goal_steps_pkey");
            entity.ToTable("goal_steps");
            entity.Property(e => e.IdGoalStep).HasColumnName("id_goal_step");
            entity.Property(e => e.IdGoal).HasColumnName("id_goal"); // ИСПРАВЛЕНО
            entity.Property(e => e.Title).HasMaxLength(200).HasColumnName("title");
            entity.Property(e => e.IsDone).HasColumnName("is_done"); // ИСПРАВЛЕНО
            entity.Property(e => e.DoneAt).HasColumnName("done_at");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("created_at");

            entity.HasOne(d => d.IdGoalNavigation).WithMany(p => p.GoalSteps)
                .HasForeignKey(d => d.IdGoal)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("goal_steps_id_goal_fkey");
        });
    }
}