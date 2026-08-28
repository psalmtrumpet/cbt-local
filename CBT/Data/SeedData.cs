using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Models;

namespace NCS.CBT.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var context = services.GetRequiredService<ApplicationDbContext>();

        // Apply any pending migrations automatically (creates DB if it doesn't exist)
        await context.Database.MigrateAsync();

        // Seed roles
        string[] roles = { "Admin", "Viewer", "Student" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed Admin
        if (await userManager.FindByNameAsync("admin") == null)
        {
            var admin = new ApplicationUser
            {
                UserName = "admin",
                Email = "admin@ncs.gov.ng",
                FullName = "System Administrator",
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, "Admin@1234");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "Admin");
        }

        // Seed Viewer
        if (await userManager.FindByNameAsync("viewer") == null)
        {
            var viewer = new ApplicationUser
            {
                UserName = "viewer",
                Email = "viewer@ncs.gov.ng",
                FullName = "Result Viewer",
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(viewer, "Viewer@1234");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(viewer, "Viewer");
        }

        // Seed Students
        // Default access code is "NCS@2026" — admin must change these before going live
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>();
        var students = new[]
        {
            new { StudentNumber = "NCS2026001", Surname = "OKAFOR", FullName = "Okafor Emmanuel", AccessCode = "NCS@2026" },
            new { StudentNumber = "NCS2026002", Surname = "ADEYEMI", FullName = "Adeyemi Blessing", AccessCode = "NCS@2026" },
            new { StudentNumber = "NCS2026003", Surname = "IBRAHIM", FullName = "Ibrahim Musa",     AccessCode = "NCS@2026" },
        };

        foreach (var s in students)
        {
            var existing = userManager.Users.FirstOrDefault(u => u.StudentNumber == s.StudentNumber);
            if (existing == null)
            {
                var student = new ApplicationUser
                {
                    UserName = s.StudentNumber,
                    Email = $"{s.StudentNumber.ToLower()}@ncs.cbt",
                    FullName = s.FullName,
                    StudentNumber = s.StudentNumber,
                    Surname = s.Surname,
                    EmailConfirmed = true
                };
                student.AccessCodeHash = hasher.HashPassword(student, s.AccessCode);
                var result = await userManager.CreateAsync(student, $"Student@{s.StudentNumber}");
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(student, "Student");
            }
            else if (string.IsNullOrEmpty(existing.AccessCodeHash))
            {
                // Back-fill access code for existing seed students that predate this fix
                existing.AccessCodeHash = hasher.HashPassword(existing, s.AccessCode);
                await userManager.UpdateAsync(existing);
            }
        }

        // Seed Exam and Questions
        if (!context.Exams.Any())
        {
            var exam = new Exam
            {
                Title = "NCS 2026 Computer-Based Test",
                Description = "Nigeria Computer Society Computer-Based Test for 2026 intake",
                DurationMinutes = 30,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedById = "system"
            };
            context.Exams.Add(exam);
            await context.SaveChangesAsync();

            var questions = new List<Question>
            {
                new Question { ExamId = exam.Id, OrderNumber = 1, Text = "What is the capital of Nigeria?", OptionA = "Lagos", OptionB = "Abuja", OptionC = "Kano", OptionD = "Port Harcourt", CorrectOption = "B" },
                new Question { ExamId = exam.Id, OrderNumber = 2, Text = "Which year was Nigeria's independence?", OptionA = "1955", OptionB = "1963", OptionC = "1960", OptionD = "1970", CorrectOption = "C" },
                new Question { ExamId = exam.Id, OrderNumber = 3, Text = "What does NCS stand for?", OptionA = "Nigeria Computer Society", OptionB = "Nigeria Custom Service", OptionC = "National Computer Society", OptionD = "Nigeria Civil Security", CorrectOption = "A" },
                new Question { ExamId = exam.Id, OrderNumber = 4, Text = "How many states are in Nigeria?", OptionA = "30", OptionB = "34", OptionC = "36", OptionD = "38", CorrectOption = "C" },
                new Question { ExamId = exam.Id, OrderNumber = 5, Text = "The Nigerian constitution was adopted in which year?", OptionA = "1989", OptionB = "1993", OptionC = "1999", OptionD = "2003", CorrectOption = "C" },
                new Question { ExamId = exam.Id, OrderNumber = 6, Text = "Which of these is a function of the civil service?", OptionA = "Making laws", OptionB = "Implementing government policies", OptionC = "Judging court cases", OptionD = "Electing the president", CorrectOption = "B" },
                new Question { ExamId = exam.Id, OrderNumber = 7, Text = "What is 15% of 200?", OptionA = "25", OptionB = "30", OptionC = "35", OptionD = "40", CorrectOption = "B" },
                new Question { ExamId = exam.Id, OrderNumber = 8, Text = "Which river is the longest in Nigeria?", OptionA = "Benue", OptionB = "Kaduna", OptionC = "Niger", OptionD = "Cross River", CorrectOption = "C" },
                new Question { ExamId = exam.Id, OrderNumber = 9, Text = "An employee who maintains confidentiality demonstrates which value?", OptionA = "Loyalty", OptionB = "Integrity", OptionC = "Diligence", OptionD = "Punctuality", CorrectOption = "B" },
                new Question { ExamId = exam.Id, OrderNumber = 10, Text = "What is the primary role of a supervisor?", OptionA = "Perform tasks alone", OptionB = "Guide and coordinate team work", OptionC = "Report to junior staff", OptionD = "Avoid decision making", CorrectOption = "B" },
            };

            context.Questions.AddRange(questions);
            await context.SaveChangesAsync();
        }
    }
}
