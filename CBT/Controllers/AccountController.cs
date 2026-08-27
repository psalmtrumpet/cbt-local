using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Models;
using NCS.CBT.ViewModels;

namespace NCS.CBT.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public AccountController(SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Admin"))
                return RedirectToAction("Dashboard", "Admin");
            if (User.IsInRole("Viewer"))
                return RedirectToAction("Dashboard", "Viewer");

            // Any other authenticated user (student) — sign out and show staff login
            await _signInManager.SignOutAsync();
        }

        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email.Trim());
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin"))
                return RedirectToAction("Dashboard", "Admin");
            if (roles.Contains("Viewer"))
                return RedirectToAction("Dashboard", "Viewer");
            return RedirectToLocal(model.ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked. Try again in a few minutes.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> StudentLogin()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Admin"))
                return RedirectToAction("Dashboard", "Admin");
            if (User.IsInRole("Viewer"))
                return RedirectToAction("Dashboard", "Viewer");

            // Sign out first, then redirect back — must be a fresh request so that
            // the antiforgery cookie and form token are generated together for the
            // anonymous session. Rendering in the same response causes a token mismatch (400).
            await _signInManager.SignOutAsync();
            return RedirectToAction("StudentLogin");
        }

        return View(new StudentLoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> StudentLogin(StudentLoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var matricNumber = model.StudentNumber.Trim();
        var surnameInput = model.Surname.Trim().ToUpperInvariant();

        var student = await _userManager.Users
            .FirstOrDefaultAsync(u => u.StudentNumber == matricNumber);

        if (student == null || (student.Surname ?? string.Empty).Trim().ToUpperInvariant() != surnameInput)
        {
            ModelState.AddModelError(string.Empty, "Invalid matric number or surname. Please check and try again.");
            return View(model);
        }

        // Non-student user — redirect them to the correct login page
        if (!(await _userManager.IsInRoleAsync(student, "Student")))
        {
            TempData["ErrorMessage"] = "This portal is for students only. Staff should log in at the staff portal.";
            return RedirectToAction("Login");
        }

        // Check for an existing active unsubmitted session (reconnection after disconnect)
        var existingSession = await _context.ExamSessions
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.StudentId == student.Id && s.IsActive && !s.IsSubmitted);

        if (existingSession != null)
        {
            // Reconnecting — resume the existing session
            // TakeExam will auto-submit if the time has since expired
            student.IsInSession = true;
            await _userManager.UpdateAsync(student);
            await _signInManager.SignInAsync(student, isPersistent: false);
            return RedirectToAction("TakeExam", "Student", new { sessionId = existingSession.Id });
        }

        // No existing session — block if already completed
        if (student.HasCompletedExam)
        {
            TempData["ErrorMessage"] = "You have already completed your exam. You cannot log in again.";
            TempData["ErrorType"] = "completed";
            return View(model);
        }

        // Find the student's assigned exam, or fall back to the most recent active exam
        var now = DateTime.UtcNow;
        Exam? activeExam = null;

        if (student.AssignedExamId.HasValue)
        {
            activeExam = await _context.Exams
                .Include(e => e.Questions)
                .FirstOrDefaultAsync(e => e.Id == student.AssignedExamId.Value && e.IsActive);

            if (activeExam == null)
            {
                TempData["ErrorMessage"] = "Your assigned exam is not currently active. Please contact the administrator.";
                TempData["ErrorType"] = "noexam";
                return View(model);
            }
        }
        else
        {
            activeExam = await _context.Exams
                .Include(e => e.Questions)
                .Where(e => e.IsActive)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync();
        }

        if (activeExam == null || !activeExam.Questions.Any())
        {
            TempData["ErrorMessage"] = "No active exam is currently available. Please contact the administrator.";
            TempData["ErrorType"] = "noexam";
            return View(model);
        }

        // Enforce scheduled window if set
        if (activeExam.ScheduledStart.HasValue && now < activeExam.ScheduledStart.Value)
        {
            var wat = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(activeExam.ScheduledStart.Value, wat).ToString("dd MMM yyyy h:mm tt");
            TempData["ErrorMessage"] = $"The exam has not started yet. It begins at {localStart} (Nigeria time).";
            TempData["ErrorType"] = "noexam";
            return View(model);
        }
        if (activeExam.ScheduledEnd.HasValue && now >= activeExam.ScheduledEnd.Value)
        {
            TempData["ErrorMessage"] = "The exam window has closed. Please contact the administrator.";
            TempData["ErrorType"] = "noexam";
            return View(model);
        }

        // Mark student as in session
        student.IsInSession = true;
        student.SessionStartTime = DateTime.UtcNow;
        await _userManager.UpdateAsync(student);

        // Create exam session
        var session = new ExamSession
        {
            StudentId = student.Id,
            ExamId = activeExam.Id,
            StartTime = DateTime.UtcNow,
            IsActive = true,
            IsSubmitted = false,
            TotalQuestions = activeExam.Questions.Count
        };
        _context.ExamSessions.Add(session);
        await _context.SaveChangesAsync();

        // Sign in the student
        await _signInManager.SignInAsync(student, isPersistent: false);

        return RedirectToAction("Rules", "Student", new { sessionId = session.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // If student, clear session flag
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Student"))
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                user.IsInSession = false;
                await _userManager.UpdateAsync(user);
            }
        }
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Login");
    }
}
