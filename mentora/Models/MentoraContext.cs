using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace mentora.Models;

public partial class MentoraContext : DbContext
{
    public MentoraContext()
    {
    }

    public MentoraContext(DbContextOptions<MentoraContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ActionLog> ActionLogs { get; set; }

    public virtual DbSet<Assignment> Assignments { get; set; }

    public virtual DbSet<AssignmentStatus> AssignmentStatuses { get; set; }

    public virtual DbSet<Meeting> Meetings { get; set; }

    public virtual DbSet<Profile> Profiles { get; set; }

    public virtual DbSet<ProgramSetting> ProgramSettings { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Sector> Sectors { get; set; }

    public virtual DbSet<Specialty> Specialties { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserStatus> UserStatuses { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql(DbConfig.ConnectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<ActionLog>(entity =>
        {
            entity.HasKey(e => e.IdActionLog).HasName("action_logs_pkey");

            entity.ToTable("action_logs");

            entity.HasIndex(e => e.OccurredAt, "ix_action_logs_date");

            entity.HasIndex(e => new { e.ActionType, e.OccurredAt }, "ix_action_logs_type_date");

            entity.HasIndex(e => new { e.IdUser, e.OccurredAt }, "ix_action_logs_user_date");

            entity.Property(e => e.IdActionLog).HasColumnName("id_action_log");
            entity.Property(e => e.ActionType)
                .HasMaxLength(100)
                .HasColumnName("action_type");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IdUser).HasColumnName("id_user");
            entity.Property(e => e.IsSuccess).HasColumnName("is_success");
            entity.Property(e => e.ObjectId).HasColumnName("object_id");
            entity.Property(e => e.ObjectType)
                .HasMaxLength(50)
                .HasColumnName("object_type");
            entity.Property(e => e.OccurredAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("occurred_at");
            entity.Property(e => e.TechnicalInfo).HasColumnName("technical_info");

            entity.HasOne(d => d.IdUserNavigation).WithMany(p => p.ActionLogs)
                .HasForeignKey(d => d.IdUser)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("action_logs_id_user_fkey");
        });

        modelBuilder.Entity<Assignment>(entity =>
        {
            entity.HasKey(e => e.IdAssignment).HasName("assignments_pkey");

            entity.ToTable("assignments");

            entity.HasIndex(e => e.RequestId, "assignments_request_id_key").IsUnique();

            entity.HasIndex(e => e.IdCoach, "ix_assignments_coach");

            entity.HasIndex(e => new { e.IdAssignmentStatus, e.CreatedAt }, "ix_assignments_status_created");

            entity.HasIndex(e => e.IdStudent, "ix_assignments_student");

            entity.HasIndex(e => e.IdCoach, "ux_assignments_active_coach")
                .IsUnique()
                .HasFilter("(id_assignment_status = 1)");

            entity.HasIndex(e => e.IdStudent, "ux_assignments_active_student")
                .IsUnique()
                .HasFilter("(id_assignment_status = 1)");

            entity.Property(e => e.IdAssignment).HasColumnName("id_assignment");
            entity.Property(e => e.ClosedAt).HasColumnName("closed_at");
            entity.Property(e => e.ClosedBy).HasColumnName("closed_by");
            entity.Property(e => e.ClosingReason).HasColumnName("closing_reason");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.IdAssignmentStatus)
                .HasDefaultValue(1)
                .HasColumnName("id_assignment_status");
            entity.Property(e => e.IdCoach).HasColumnName("id_coach");
            entity.Property(e => e.IdStudent).HasColumnName("id_student");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ClosedByNavigation).WithMany(p => p.AssignmentClosedByNavigations)
                .HasForeignKey(d => d.ClosedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("assignments_closed_by_fkey");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.AssignmentCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("assignments_created_by_fkey");

            entity.HasOne(d => d.IdAssignmentStatusNavigation).WithMany(p => p.Assignments)
                .HasForeignKey(d => d.IdAssignmentStatus)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("assignments_id_assignment_status_fkey");

            entity.HasOne(d => d.IdCoachNavigation).WithOne(p => p.AssignmentIdCoachNavigation)
                .HasForeignKey<Assignment>(d => d.IdCoach)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("assignments_id_coach_fkey");

            entity.HasOne(d => d.IdStudentNavigation).WithOne(p => p.AssignmentIdStudentNavigation)
                .HasForeignKey<Assignment>(d => d.IdStudent)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("assignments_id_student_fkey");
        });

        modelBuilder.Entity<AssignmentStatus>(entity =>
        {
            entity.HasKey(e => e.IdAssignmentStatus).HasName("assignment_statuses_pkey");

            entity.ToTable("assignment_statuses");

            entity.HasIndex(e => e.Name, "assignment_statuses_name_key").IsUnique();

            entity.Property(e => e.IdAssignmentStatus).HasColumnName("id_assignment_status");
            entity.Property(e => e.Name)
                .HasMaxLength(30)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Meeting>(entity =>
        {
            entity.HasKey(e => e.IdMeeting).HasName("meetings_pkey");

            entity.ToTable("meetings");

            entity.HasIndex(e => new { e.IdAssignment, e.MeetingDate }, "ix_meetings_assignment_date");

            entity.HasIndex(e => e.MeetingDate, "ix_meetings_date").HasFilter("(is_archived = false)");

            entity.HasIndex(e => e.RequestId, "meetings_request_id_key").IsUnique();

            entity.Property(e => e.IdMeeting).HasColumnName("id_meeting");
            entity.Property(e => e.CoachComment).HasColumnName("coach_comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.IdAssignment).HasColumnName("id_assignment");
            entity.Property(e => e.IsArchived)
                .HasDefaultValue(false)
                .HasColumnName("is_archived");
            entity.Property(e => e.MeetingDate).HasColumnName("meeting_date");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.Summary)
                .HasMaxLength(1000)
                .HasColumnName("summary");
            entity.Property(e => e.Topic)
                .HasMaxLength(200)
                .HasColumnName("topic");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.MeetingCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("meetings_created_by_fkey");

            entity.HasOne(d => d.IdAssignmentNavigation).WithMany(p => p.Meetings)
                .HasForeignKey(d => d.IdAssignment)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("meetings_id_assignment_fkey");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.MeetingUpdatedByNavigations)
                .HasForeignKey(d => d.UpdatedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("meetings_updated_by_fkey");
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasKey(e => e.IdUser).HasName("profiles_pkey");

            entity.ToTable("profiles");

            entity.HasIndex(e => e.IdSector, "ix_profiles_sector");

            entity.HasIndex(e => new { e.IdSpecialty, e.IdSector }, "ix_profiles_specialty_sector");

            entity.Property(e => e.IdUser)
                .ValueGeneratedNever()
                .HasColumnName("id_user");
            entity.Property(e => e.AcademicGoal)
                .HasMaxLength(500)
                .HasColumnName("academic_goal");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IdSector).HasColumnName("id_sector");
            entity.Property(e => e.IdSpecialty).HasColumnName("id_specialty");
            entity.Property(e => e.Interest)
                .HasMaxLength(300)
                .HasColumnName("interest");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.IdSectorNavigation).WithMany(p => p.Profiles)
                .HasForeignKey(d => d.IdSector)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("profiles_id_sector_fkey");

            entity.HasOne(d => d.IdSpecialtyNavigation).WithMany(p => p.Profiles)
                .HasForeignKey(d => d.IdSpecialty)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("profiles_id_specialty_fkey");

            entity.HasOne(d => d.IdUserNavigation).WithOne(p => p.Profile)
                .HasForeignKey<Profile>(d => d.IdUser)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("profiles_id_user_fkey");
        });

        modelBuilder.Entity<ProgramSetting>(entity =>
        {
            entity.HasKey(e => e.IdSetting).HasName("program_settings_pkey");

            entity.ToTable("program_settings");

            entity.Property(e => e.IdSetting)
                .HasDefaultValue(1)
                .HasColumnName("id_setting");
            entity.Property(e => e.CoachCanComplete)
                .HasDefaultValue(false)
                .HasColumnName("coach_can_complete");
            entity.Property(e => e.RequireSameSpecialty)
                .HasDefaultValue(false)
                .HasColumnName("require_same_specialty");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");

            entity.HasOne(d => d.UpdatedByNavigation).WithMany(p => p.ProgramSettings)
                .HasForeignKey(d => d.UpdatedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("program_settings_updated_by_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.IdRole).HasName("roles_pkey");

            entity.ToTable("roles");

            entity.HasIndex(e => e.Name, "roles_name_key").IsUnique();

            entity.Property(e => e.IdRole).HasColumnName("id_role");
            entity.Property(e => e.Name)
                .HasMaxLength(30)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Sector>(entity =>
        {
            entity.HasKey(e => e.IdSector).HasName("sectors_pkey");

            entity.ToTable("sectors");

            entity.Property(e => e.IdSector).HasColumnName("id_sector");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Specialty>(entity =>
        {
            entity.HasKey(e => e.IdSpecialty).HasName("specialties_pkey");

            entity.ToTable("specialties");

            entity.Property(e => e.IdSpecialty).HasColumnName("id_specialty");
            entity.Property(e => e.Abbreviation)
                .HasMaxLength(20)
                .HasColumnName("abbreviation");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.IdUser).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => new { e.IdRole, e.IdUserStatus }, "ix_users_role_status");

            entity.Property(e => e.IdUser).HasColumnName("id_user");
            entity.Property(e => e.Course).HasColumnName("course");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.FailedLoginAttempts)
                .HasDefaultValue(0)
                .HasColumnName("failed_login_attempts");
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.IdRole).HasColumnName("id_role");
            entity.Property(e => e.IdUserStatus)
                .HasDefaultValue(1)
                .HasColumnName("id_user_status");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.Login)
                .HasMaxLength(50)
                .HasColumnName("login");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.IdRoleNavigation).WithMany(p => p.Users)
                .HasForeignKey(d => d.IdRole)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("users_id_role_fkey");

            entity.HasOne(d => d.IdUserStatusNavigation).WithMany(p => p.Users)
                .HasForeignKey(d => d.IdUserStatus)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("users_id_user_status_fkey");
        });

        modelBuilder.Entity<UserStatus>(entity =>
        {
            entity.HasKey(e => e.IdUserStatus).HasName("user_statuses_pkey");

            entity.ToTable("user_statuses");

            entity.HasIndex(e => e.Name, "user_statuses_name_key").IsUnique();

            entity.Property(e => e.IdUserStatus).HasColumnName("id_user_status");
            entity.Property(e => e.Name)
                .HasMaxLength(30)
                .HasColumnName("name");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
