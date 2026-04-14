using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using AppFcmOptions = AmIRite.Web.Models.FcmOptions;

namespace AmIRite.Web.Services;

public class FcmService(AppFcmOptions options, ILogger<FcmService> logger)
{
    private FirebaseMessaging? _messaging;

    private FirebaseMessaging GetMessaging()
    {
        if (_messaging != null) return _messaging;

        if (FirebaseApp.DefaultInstance == null)
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromFile(options.CredentialsPath)
            });
        }
        _messaging = FirebaseMessaging.DefaultInstance;
        return _messaging;
    }

    /// <summary>
    /// Sends a push notification to a device FCM token.
    /// Returns true if the send was successful.
    /// </summary>
    public async Task<bool> SendAsync(string deviceToken, string title, string body)
    {
        if (string.IsNullOrEmpty(options.CredentialsPath) || !System.IO.File.Exists(options.CredentialsPath))
        {
            logger.LogWarning("FCM credentials not configured; skipping push notification");
            return false;
        }

        try
        {
            var message = new Message
            {
                Token = deviceToken,
                Notification = new Notification { Title = title, Body = body },
                Webpush = new WebpushConfig
                {
                    Notification = new WebpushNotification { Title = title, Body = body }
                }
            };

            await GetMessaging().SendAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FCM send failed for token {Token}", deviceToken[..8] + "...");
            return false;
        }
    }
}
