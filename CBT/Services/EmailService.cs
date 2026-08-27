using System.Net;
using System.Net.Mail;

namespace NCS.CBT.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendStudentCredentialsAsync(
        string toEmail, string studentName, string studentNumber,
        string surname, string? accessCode = null)
    {
        try
        {
            var examUrl = _config["Email:ExamUrl"] ?? "http://localhost:8080";
            using var client = BuildClient();
            using var message = new MailMessage
            {
                From      = new MailAddress(_config["Email:Username"]!, _config["Email:FromName"] ?? "NCS CBT"),
                Subject   = "CBT — Your Examination Login Details",
                IsBodyHtml = true,
                Body      = BuildBody(studentName, studentNumber, surname, examUrl)
            };
            message.To.Add(new MailAddress(toEmail, studentName));
            await client.SendMailAsync(message);
            _logger.LogInformation("Credentials email sent to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send credentials email to {Email}", toEmail);
            return false;
        }
    }

    public async Task<bool> SendProctorCredentialsAsync(
        string toEmail, string fullName, string password, string loginUrl)
    {
        try
        {
            using var client = BuildClient();
            using var message = new MailMessage
            {
                From       = new MailAddress(_config["Email:Username"]!, _config["Email:FromName"] ?? "NCS CBT"),
                Subject    = "NCS CBT — Your Proctor Login Details",
                IsBodyHtml = true,
                Body       = BuildProctorBody(fullName, toEmail, password, loginUrl)
            };
            message.To.Add(new MailAddress(toEmail, fullName));
            await client.SendMailAsync(message);
            _logger.LogInformation("Proctor credentials email sent to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send proctor credentials email to {Email}", toEmail);
            return false;
        }
    }

    private SmtpClient BuildClient()
    {
        var host     = _config["Email:Smtp"]     ?? "smtp.hostinger.com";
        var port     = int.Parse(_config["Email:Port"] ?? "587");
        var username = _config["Email:Username"]!;
        var password = _config["Email:Password"]!;

        return new SmtpClient(host, port)
        {
            EnableSsl   = true,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
    }

    private static string BuildProctorBody(
        string name, string email, string password, string loginUrl) => $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background:#f4f6f9;font-family:Arial,Helvetica,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f9;padding:30px 0;">
            <tr><td align="center">
              <table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;">

                <!-- Header -->
                <tr>
                  <td style="background:#1a3c5e;padding:30px 40px;border-radius:8px 8px 0 0;">
                    <h1 style="margin:0;color:#ffffff;font-size:22px;">
                      NCS CBT — Proctor Access
                    </h1>
                    <p style="margin:6px 0 0;color:#a8c4e0;font-size:13px;">
                      Nigeria Computer Society Computer-Based Test
                    </p>
                  </td>
                </tr>

                <!-- Body -->
                <tr>
                  <td style="background:#ffffff;padding:36px 40px;border:1px solid #e0e0e0;border-top:none;">
                    <p style="margin:0 0 16px;font-size:15px;color:#333;">
                      Dear <strong>{name}</strong>,
                    </p>
                    <p style="margin:0 0 24px;font-size:15px;color:#555;line-height:1.6;">
                      A proctor account has been created for you on the NCS CBT platform.
                      Use the details below to log in at the Staff Login page.
                    </p>

                    <!-- Login credentials -->
                    <table width="100%" cellpadding="0" cellspacing="0"
                           style="background:#f0f4f8;border-radius:8px;padding:20px;border:1px solid #dce3ea;">
                      <tr>
                        <td style="padding:8px 16px;color:#666;font-size:13px;width:40%;">Email</td>
                        <td style="padding:8px 16px;font-weight:bold;font-size:15px;color:#1a3c5e;">{email}</td>
                      </tr>
                      <tr>
                        <td style="padding:8px 16px;color:#666;font-size:13px;border-top:1px solid #dce3ea;">Password</td>
                        <td style="padding:8px 16px;border-top:1px solid #dce3ea;">
                          <span style="font-size:20px;font-weight:bold;letter-spacing:3px;color:#1a3c5e;
                                       background:#e8f0fe;padding:8px 16px;border-radius:6px;display:inline-block;">
                            {password}
                          </span>
                        </td>
                      </tr>
                    </table>

                    <!-- CTA button -->
                    <div style="margin:28px 0 20px;text-align:center;">
                      <a href="{loginUrl}/Account/Login" target="_blank"
                         style="background:#1a3c5e;color:#ffffff;padding:14px 36px;border-radius:6px;
                                text-decoration:none;font-size:15px;font-weight:bold;display:inline-block;">
                        Go to Staff Login
                      </a>
                    </div>

                    <p style="margin:0;font-size:13px;color:#999;text-align:center;line-height:1.6;">
                      Keep this email confidential. Do not share your password with anyone.<br>
                      If you did not expect this email, please contact your administrator.
                    </p>
                  </td>
                </tr>

                <!-- Footer -->
                <tr>
                  <td style="background:#f0f4f8;padding:16px 40px;border-radius:0 0 8px 8px;
                              border:1px solid #e0e0e0;border-top:none;text-align:center;">
                    <p style="margin:0;font-size:12px;color:#aaa;">
                      &copy; {DateTime.UtcNow.Year} Nigeria Computer Society (NCS) &mdash; {loginUrl}
                    </p>
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string BuildBody(
        string name, string number, string surname, string examUrl) => $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background:#f4f6f9;font-family:Arial,Helvetica,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f9;padding:30px 0;">
            <tr><td align="center">
              <table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;">

                <!-- Header -->
                <tr>
                  <td style="background:#1a3c5e;padding:30px 40px;border-radius:8px 8px 0 0;">
                    <h1 style="margin:0;color:#ffffff;font-size:22px;">
                      CBT — Examination Portal
                    </h1>
                    <p style="margin:6px 0 0;color:#a8c4e0;font-size:13px;">
                      Computer-Based Test
                    </p>
                  </td>
                </tr>

                <!-- Body -->
                <tr>
                  <td style="background:#ffffff;padding:36px 40px;border:1px solid #e0e0e0;border-top:none;">
                    <p style="margin:0 0 16px;font-size:15px;color:#333;">
                      Dear <strong>{name}</strong>,
                    </p>
                    <p style="margin:0 0 24px;font-size:15px;color:#555;line-height:1.6;">
                      Your examination account has been created. Use the details below to log in
                      on the examination day.
                    </p>

                    <!-- Login credentials -->
                    <table width="100%" cellpadding="0" cellspacing="0"
                           style="background:#f0f4f8;border-radius:8px;padding:20px;border:1px solid #dce3ea;">
                      <tr>
                        <td style="padding:8px 16px;color:#666;font-size:13px;width:40%;">Matric Number</td>
                        <td style="padding:8px 16px;font-weight:bold;font-size:18px;color:#1a3c5e;">{number}</td>
                      </tr>
                      <tr>
                        <td style="padding:8px 16px;color:#666;font-size:13px;border-top:1px solid #dce3ea;">Surname</td>
                        <td style="padding:8px 16px;border-top:1px solid #dce3ea;">
                          <span style="font-size:22px;font-weight:bold;letter-spacing:3px;color:#1a3c5e;
                                       background:#e8f0fe;padding:8px 16px;border-radius:6px;display:inline-block;">
                            {surname.ToUpper()}
                          </span>
                        </td>
                      </tr>
                    </table>

                    <!-- CTA button -->
                    <div style="margin:24px 0 20px;text-align:center;">
                      <a href="{examUrl}/Account/StudentLogin" target="_blank"
                         style="background:#1a3c5e;color:#ffffff;padding:14px 36px;border-radius:6px;
                                text-decoration:none;font-size:15px;font-weight:bold;display:inline-block;">
                        Access Examination Portal
                      </a>
                    </div>

                    <p style="margin:0;font-size:13px;color:#999;text-align:center;line-height:1.6;">
                      You will be asked for your Matric Number and Surname at the exam venue.<br>
                      If you did not expect this email, please contact your administrator.
                    </p>
                  </td>
                </tr>

                <!-- Footer -->
                <tr>
                  <td style="background:#f0f4f8;padding:16px 40px;border-radius:0 0 8px 8px;
                              border:1px solid #e0e0e0;border-top:none;text-align:center;">
                    <p style="margin:0;font-size:12px;color:#aaa;">
                      &copy; {DateTime.UtcNow.Year} &mdash; {examUrl}
                    </p>
                  </td>
                </tr>

              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
}
