using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Models;

namespace NCS.CBT.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Exam> Exams { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<ExamSession> ExamSessions { get; set; }
    public DbSet<StudentAnswer> StudentAnswers { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ExamSession>()
            .HasOne(es => es.Student)
            .WithMany(u => u.ExamSessions)
            .HasForeignKey(es => es.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudentAnswer>()
            .HasOne(sa => sa.ExamSession)
            .WithMany(es => es.Answers)
            .HasForeignKey(sa => sa.ExamSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudentAnswer>()
            .HasOne(sa => sa.Question)
            .WithMany(q => q.StudentAnswers)
            .HasForeignKey(sa => sa.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.StudentNumber)
            .IsUnique()
            .HasFilter("[StudentNumber] IS NOT NULL");

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.AssignedExam)
            .WithMany()
            .HasForeignKey(u => u.AssignedExamId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
