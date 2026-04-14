using AmIRite.Web.Data;
using AmIRite.Web.Models;
using AmIRite.Web.Routes;
using AmIRite.Web.Services;
using AmIRite.Web.Workers;

var builder = WebApplication.CreateBuilder(args);

// -- Configuration --
var gameOptions  = builder.Configuration.GetSection("Game").Get<GameOptions>()!;
var llmOptions   = builder.Configuration.GetSection("Llm").Get<LlmOptions>()!;
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>()!;
var fcmOptions   = builder.Configuration.GetSection("Fcm").Get<FcmOptions>()!;
var adminOptions = builder.Configuration.GetSection("Admin").Get<AdminOptions>()!;

builder.Services.AddSingleton(gameOptions);
builder.Services.AddSingleton(llmOptions);
builder.Services.AddSingleton(emailOptions);
builder.Services.AddSingleton(fcmOptions);
builder.Services.AddSingleton(adminOptions);

// -- Database --
var connString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new DbConnectionFactory(connString));

// -- Core services --
builder.Services.AddSingleton<RateLimiterService>();
builder.Services.AddSingleton<SseService>();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<QuestionService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<LlmService>();
builder.Services.AddScoped<FcmService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<RoundService>();

// -- Achievement evaluators (each registered as IAchievementEvaluator) --
builder.Services.AddSingleton<IAchievementEvaluator, FirstGameEvaluator>();
builder.Services.AddSingleton<IAchievementEvaluator, PerfectRoundEvaluator>();
builder.Services.AddSingleton<IAchievementEvaluator, PerfectGameEvaluator>();
builder.Services.AddSingleton<IAchievementEvaluator, TenGamesEvaluator>();
builder.Services.AddSingleton<IAchievementEvaluator, TwentyFiveGamesEvaluator>();
builder.Services.AddSingleton<IAchievementEvaluator, SharpEyeEvaluator>();
builder.Services.AddSingleton<IAchievementEvaluator, FooledThemAllEvaluator>();
builder.Services.AddSingleton<IAchievementEvaluator, MindReaderEvaluator>();
builder.Services.AddScoped<AchievementService>();

// -- Background workers --
builder.Services.AddSingleton<AchievementWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AchievementWorker>());
builder.Services.AddHostedService<JoinExpiryWorker>();
builder.Services.AddHostedService<SseHeartbeatWorker>();
builder.Services.AddHostedService<LlmRetryWorker>();

// -- HTTP --
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Run migrations at startup
Database.RunMigrations(connString);

// Seed questions from file (no-op if already seeded)
using (var scope = app.Services.CreateScope())
{
    var questions = scope.ServiceProvider.GetRequiredService<QuestionService>();
    var questionsFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "doc", "questions-2026-04-14-clean.txt");
    if (!File.Exists(questionsFile))
        questionsFile = Path.Combine(Directory.GetCurrentDirectory(), "..","..","doc","questions-2026-04-14-clean.txt");
    await questions.SeedQuestionsFromFileAsync(questionsFile);
}

app.UseStaticFiles();

// -- Route registration --
app.MapPlayerRoutes();
app.MapAuthRoutes();
app.MapGameRoutes();
app.MapSseRoutes();
app.MapApiRoutes();
app.MapAdminRoutes();

app.Run();
