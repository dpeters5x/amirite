using System.Net;
using System.Net.Mail;
using AmIRite.Web.Models;

namespace AmIRite.Web.Services;

public class EmailService(EmailOptions options, ILogger<EmailService> logger)
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(options.FromAddress, options.AppPassword)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(to);

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To} with subject {Subject}", to, subject);
            throw;
        }
    }

    public Task SendOtpAsync(string to, string code) =>
        SendAsync(to, "Your AmIRite login code",
            $"""
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto">
              <h2 style="color:#5b4fcf">AmIRite</h2>
              <p>Your one-time login code is:</p>
              <div style="font-size:2.5rem;font-weight:700;letter-spacing:0.3em;color:#1a1a1a;padding:16px 0">{code}</div>
              <p style="color:#666;font-size:0.875rem">This code expires in 10 minutes. If you didn't request this, you can ignore this email.</p>
            </div>
            """);

    public Task SendInvitationAsync(string to, string joinUrl, string organizerEmail) =>
        SendAsync(to, "You've been invited to play AmIRite!",
            $"""
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto">
              <h2 style="color:#5b4fcf">AmIRite</h2>
              <p>{organizerEmail} has invited you to play AmIRite — a two-player guessing game!</p>
              <p><a href="{joinUrl}" style="display:inline-block;background:#5b4fcf;color:#fff;padding:12px 24px;border-radius:8px;text-decoration:none;font-weight:600">Join the game</a></p>
              <p style="color:#666;font-size:0.875rem">This link expires in 7 days.</p>
            </div>
            """);

    public Task SendGameCancelledAsync(string to, string opponentNickname) =>
        SendAsync(to, "Your AmIRite game has been cancelled",
            $"""
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto">
              <h2 style="color:#5b4fcf">AmIRite</h2>
              <p>Your game was cancelled because not all players joined in time.</p>
              <p style="color:#666;font-size:0.875rem">You can start a new game at any time.</p>
            </div>
            """);

    public Task SendFinalResultsAsync(string to, string resultsUrl, string player1Nickname, string player2Nickname) =>
        SendAsync(to, "AmIRite — Game over! See your results",
            $"""
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto">
              <h2 style="color:#5b4fcf">AmIRite</h2>
              <p>Your game between <strong>{player1Nickname}</strong> and <strong>{player2Nickname}</strong> is complete!</p>
              <p><a href="{resultsUrl}" style="display:inline-block;background:#5b4fcf;color:#fff;padding:12px 24px;border-radius:8px;text-decoration:none;font-weight:600">See Results</a></p>
            </div>
            """);

    public Task SendAdminAlertAsync(string adminEmail, string subject, string body) =>
        SendAsync(adminEmail, $"[AmIRite Admin] {subject}", $"<pre>{body}</pre>");
}
