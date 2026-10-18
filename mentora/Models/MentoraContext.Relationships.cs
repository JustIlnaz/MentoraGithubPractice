using Microsoft.EntityFrameworkCore;

namespace mentora.Models;

public partial class MentoraContext
{
    public virtual DbSet<MeetingFeedback> MeetingFeedbacks { get; set; }

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

        modelBuilder.Entity<MeetingFeedback>(entity =>
        {
            entity.HasKey(e => e.IdFeedback).HasName("meeting_feedback_pkey");

            entity.ToTable("meeting_feedback");

            entity.HasIndex(e => e.IdMeeting, "meeting_feedback_id_meeting_key")
                .IsUnique();

            entity.HasIndex(e => e.IdCoach, "ix_meeting_feedback_coach");

            entity.Property(e => e.IdFeedback).HasColumnName("id_feedback");
            entity.Property(e => e.IdMeeting).HasColumnName("id_meeting");
            entity.Property(e => e.IdStudent).HasColumnName("id_student");
            entity.Property(e => e.IdCoach).HasColumnName("id_coach");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.Comment)
                .HasMaxLength(500)
                .HasColumnName("comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");

            entity.HasOne(d => d.IdMeetingNavigation).WithMany()
                .HasForeignKey(d => d.IdMeeting)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("meeting_feedback_id_meeting_fkey");

            entity.HasOne(d => d.IdStudentNavigation).WithMany()
                .HasForeignKey(d => d.IdStudent)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("meeting_feedback_id_student_fkey");

            entity.HasOne(d => d.IdCoachNavigation).WithMany()
                .HasForeignKey(d => d.IdCoach)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("meeting_feedback_id_coach_fkey");
        });
    }
}