namespace AmIRite.Web.Models;

public class Player
{
    public int      Id          { get; set; }
    public string   Email       { get; set; } = "";
    public string?  Nickname    { get; set; }
    public string?  FcmToken    { get; set; }
    public DateTime CreatedAt   { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public class Session
{
    public string   Id                 { get; set; } = "";
    public int?     OrganizerId        { get; set; }
    public string   OrganizerEmail     { get; set; } = "";
    public int?     Player1Id          { get; set; }
    public int?     Player2Id          { get; set; }
    public string   Status             { get; set; } = "pending_join";
    public int      QuestionsPerRound  { get; set; }
    public int      DecoyCount         { get; set; }
    public DateTime JoinExpiresAt      { get; set; }
    public DateTime CreatedAt          { get; set; }
    public DateTime? EndedAt           { get; set; }
    public DateTime? ArchivedAt        { get; set; }
    public string?  ArchivedBy         { get; set; }
}

public class SessionPlayer
{
    public string   SessionId  { get; set; } = "";
    public int      PlayerId   { get; set; }
    public string   Token      { get; set; } = "";
    public string?  Nickname   { get; set; }
    public DateTime? JoinedAt  { get; set; }
    public int?     FinalRound { get; set; }
}

public class Category
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = "";
    public string? Description{ get; set; }
    public bool   Active      { get; set; } = true;
}

public class Preset
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = "";
    public string? Description{ get; set; }
    public int    SortOrder   { get; set; }
    public bool   Active      { get; set; } = true;
}

public class SessionCategory
{
    public string SessionId  { get; set; } = "";
    public int    CategoryId { get; set; }
    public bool   Player1Vote{ get; set; }
    public bool   Player2Vote{ get; set; }
    public double Weight     { get; set; }
}

public class Question
{
    public int      Id        { get; set; }
    public string   Text      { get; set; } = "";
    public bool     Active    { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Round
{
    public int      Id          { get; set; }
    public string   SessionId   { get; set; } = "";
    public int      RoundNumber { get; set; }
    public string   Status      { get; set; } = "answering";
    public DateTime StartedAt   { get; set; }
    public DateTime? CompletedAt{ get; set; }
}

public class RoundQuestion
{
    public int Id         { get; set; }
    public int RoundId    { get; set; }
    public int QuestionId { get; set; }
    public int SortOrder  { get; set; }
}

public class Answer
{
    public int      Id               { get; set; }
    public int      RoundQuestionId  { get; set; }
    public int      PlayerId         { get; set; }
    public string   AnswerText       { get; set; } = "";
    public DateTime SubmittedAt      { get; set; }
}

public class Decoy
{
    public int    Id               { get; set; }
    public int    RoundQuestionId  { get; set; }
    public int    TargetPlayerId   { get; set; }
    public string DecoyText        { get; set; } = "";
}

public class Guess
{
    public int      Id               { get; set; }
    public int      RoundQuestionId  { get; set; }
    public int      GuessingPlayerId { get; set; }
    public int?     ChosenAnswerId   { get; set; }
    public int?     ChosenDecoyId    { get; set; }
    public bool     IsCorrect        { get; set; }
    public int      PointsAwarded    { get; set; }
    public DateTime SubmittedAt      { get; set; }
}

public class QuestionFeedback
{
    public int      Id               { get; set; }
    public int      QuestionId       { get; set; }
    public int      PlayerId         { get; set; }
    public string   SessionId        { get; set; } = "";
    public int?     RoundQuestionId  { get; set; }
    public string   Phase            { get; set; } = "";
    public bool     FlagInappropriate{ get; set; }
    public bool     FlagDuplicate    { get; set; }
    public int?     QualityRating    { get; set; }
    public bool     FlagPoorDecoys   { get; set; }
    public string?  Notes            { get; set; }
    public DateTime CreatedAt        { get; set; }
    public DateTime? ReviewedAt      { get; set; }
    public string?  ReviewedBy       { get; set; }
    public string?  Resolution       { get; set; }
}

public class OtpCode
{
    public int      Id        { get; set; }
    public string   Email     { get; set; } = "";
    public string   Code      { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool     Used      { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PlayerSession
{
    public string   Id        { get; set; } = "";
    public int      PlayerId  { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class Achievement
{
    public int    Id          { get; set; }
    public string Key         { get; set; } = "";
    public string Name        { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon        { get; set; } = "";
    public bool   Active      { get; set; } = true;
    public int    SortOrder   { get; set; }
}

public class PlayerAchievement
{
    public int      Id            { get; set; }
    public int      PlayerId      { get; set; }
    public int      AchievementId { get; set; }
    public DateTime AwardedAt     { get; set; }
    public string   AwardedBy     { get; set; } = "system";
    public string?  SessionId     { get; set; }
}

public class Broadcast
{
    public int      Id             { get; set; }
    public string   Subject        { get; set; } = "";
    public string   Body           { get; set; } = "";
    public string   Channel        { get; set; } = "";
    public DateTime SentAt         { get; set; }
    public string   SentBy         { get; set; } = "";
    public int      RecipientCount { get; set; }
}

public class BroadcastRecipient
{
    public int     Id           { get; set; }
    public int     BroadcastId  { get; set; }
    public int     PlayerId     { get; set; }
    public string  Email        { get; set; } = "";
    public string? ChannelUsed  { get; set; }
    public bool?   FcmSuccess   { get; set; }
    public bool?   EmailSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ChatMessage
{
    public int      Id          { get; set; }
    public string   SessionId   { get; set; } = "";
    public int      SenderId    { get; set; }
    public string   MessageText { get; set; } = "";
    public DateTime SentAt      { get; set; }
    public DateTime? ReadAt     { get; set; }
}
