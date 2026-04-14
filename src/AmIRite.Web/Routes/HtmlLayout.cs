namespace AmIRite.Web.Routes;

/// <summary>
/// Shared HTML layout used by all player-facing pages.
/// </summary>
public static class HtmlLayout
{
    // Extracted as a field to avoid literal { in interpolated raw string
    private static readonly string ThemeScript =
        "(function(){" +
        "var t=localStorage.getItem('theme')||" +
        "(window.matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light');" +
        "document.documentElement.setAttribute('data-theme',t);" +
        "})();";

    public static IResult Page(string title, string body, string? headExtras = null) =>
        Results.Content(Render(title, body, headExtras), "text/html");

    public static string Render(string title, string body, string? headExtras = null) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>{title} — AmIRite</title>
          <link rel="preconnect" href="https://fonts.googleapis.com" />
          <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
          <link href="https://fonts.googleapis.com/css2?family=Nunito:wght@400;600;700;800&display=swap" rel="stylesheet" />
          <link rel="stylesheet" href="/css/app.css" />
          <script>{ThemeScript}</script>
          {headExtras ?? ""}
        </head>
        <body>
          {body}
          <script src="https://unpkg.com/htmx.org@2.0.3/dist/htmx.min.js" crossorigin="anonymous"></script>
          <script src="https://unpkg.com/htmx-ext-sse@2.2.3/sse.js" crossorigin="anonymous"></script>
          <script src="/js/app.js"></script>
        </body>
        </html>
        """;

    public static string NavBar(
        string? playerNickname = null,
        IEnumerable<(string Token, string Opponent, string Status)>? gameLinks = null)
    {
        var pebbles = string.Join("", (gameLinks ?? []).Select(g =>
            $"""<a href="/play/{g.Token}" class="game-pebble status-{g.Status}" title="{g.Opponent}">{g.Opponent}<span class="status-dot"></span></a>"""));

        var profileLink = playerNickname != null
            ? $"""<a href="/profile" class="nav-link">{playerNickname}</a>"""
            : "";

        return $"""
            <nav class="navbar">
              <a href="/" class="navbar-brand">AmIRite</a>
              <div class="navbar-games">{pebbles}</div>
              <div class="navbar-actions">
                {profileLink}
                <button class="icon-btn" onclick="toggleTheme()" title="Toggle theme" aria-label="Toggle theme">
                  <span class="theme-icon">&#9728;</span>
                </button>
              </div>
            </nav>
            """;
    }
}
