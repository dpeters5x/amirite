namespace AmIRite.Web.Models;

public class GameOptions
{
    public int QuestionsPerRound         { get; init; } = 2;
    public int DecoyCount                { get; init; } = 3;
    public double CategoryWeightOneVote  { get; init; } = 0.3;
    public double CategoryWeightBothVotes{ get; init; } = 0.7;
    public int OtpExpiryMinutes          { get; init; } = 10;
    public int SessionCookieExpiryDays   { get; init; } = 30;
    public int JoinTokenExpiryDays       { get; init; } = 7;
    public int LlmRetryCount             { get; init; } = 3;
    public int RateLimitOtpPerHour       { get; init; } = 5;
    public int RateLimitSignupPerHour    { get; init; } = 10;
    public int RateLimitChatPerMinute    { get; init; } = 10;
    public int RateLimitApiPerMinute     { get; init; } = 60;
}

public class LlmOptions
{
    public string Provider        { get; init; } = "Anthropic";
    public string AnthropicApiKey { get; init; } = "";
    public string OpenAiApiKey    { get; init; } = "";
    public string Model           { get; init; } = "claude-sonnet-4-6";
    public int?   DecoyCountOverride { get; init; }
}

public class EmailOptions
{
    public string SmtpHost    { get; init; } = "smtp.gmail.com";
    public int    SmtpPort    { get; init; } = 587;
    public string FromAddress { get; init; } = "";
    public string FromName    { get; init; } = "AmIRite";
    public string AppPassword { get; init; } = "";
}

public class FcmOptions
{
    public string ProjectId        { get; init; } = "";
    /// <summary>Full JSON content of the Firebase service account credentials file.</summary>
    public string CredentialsJson  { get; init; } = "";
}

public class AdminOptions
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
}
