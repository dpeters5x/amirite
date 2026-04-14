The specification for a new game I want to work on appears below. Please create a solution in the current folder, set up version control with git (and github if you can do that as well), and then follow the process described in section 18, in sequence, and one step at a time. 

The doc folder contains a file, "questions-2026-04-14-clean.txt" That has the initial pool of questions formatted as #categories\nQuestion\n\n.  Each #categories line has one or more categories, separated by commas, which indivate the categories to which the following question belongs.

Please ask any questions before proceeding.



# AmIRite — Full Game Specification
**Version 1.9 — Pre-build reference document**

---

## 1. Concept Summary

AmIRite is a turn-based guessing game for exactly two players. Each round, both players independently answer one or more questions about themselves. Once both have answered, the system presents each player with the other player's answer mixed in with LLM-generated decoys. Each player guesses which answer belongs to their opponent. Points are awarded for correct guesses. The game ends when the question pool is exhausted or either player declares a final round. An LLM-generated analysis is produced at the end.

---

## 2. Tech Stack

| Concern | Choice |
|---|---|
| Language | C# 12, .NET 10 |
| Runtime model | .NET Generic Host with BackgroundService workers |
| Web framework | ASP.NET Core Minimal APIs + HTMX frontend |
| Frontend style | Server-rendered HTML partials; HTMX for dynamic updates; SSE for real-time waiting states |
| Database | SQLite via Dapper (no EF Core) |
| LLM provider | Anthropic Claude (default); OpenAI (configurable via appsettings) |
| Push notifications | Firebase Cloud Messaging (FCM) via FirebaseAdmin SDK |
| Email | SMTP (Gmail) via System.Net.Mail.SmtpClient |
| Static files | wwwroot/ via app.UseStaticFiles() |
| Deployment | Fly.io (single machine, always-on) |
| Auth | OTP via email (6-digit code, short expiry); session cookie per player |
| Admin auth | HTTP Basic Auth; credentials in environment variables |

---

## 3. Route Map

### Public / Player Routes

| Method | Path | Purpose |
|---|---|---|
| GET | `/` | Landing page — marketing copy, CTA, how it works |
| GET/POST | `/signup` | Organizer enters two email addresses to start a game |
| GET | `/lobby/{sessionId}` | Post-signup waiting room — shows join status, resend invitation |
| GET | `/join/{token}` | Player lands here from invite email; sets nickname + category preferences |
| GET/POST | `/auth/otp` | OTP entry screen |
| GET | `/play/{token}` | Main game page — answers, then guesses; SSE-connected |
| GET | `/results/{sessionId}` | End-of-game summary page (accessible indefinitely) |
| GET | `/profile` | Authenticated player's profile page |

### Admin Routes (Basic Auth protected)

| Method | Path | Purpose |
|---|---|---|
| GET | `/admin` | Dashboard — active games, flag queue summary, recent broadcasts |
| GET | `/admin/questions` | List all questions |
| POST | `/admin/questions` | Create a question |
| GET/POST | `/admin/questions/{id}` | Edit a question |
| DELETE | `/admin/questions/{id}` | Delete a question |
| POST | `/admin/questions/bulk` | Bulk activate / deactivate / reassign categories |
| POST | `/admin/questions/import` | CSV bulk import |
| GET | `/admin/questions/flagged` | Flag review queue |
| POST | `/admin/questions/{id}/flag-resolution` | Resolve a flag |
| GET/POST | `/admin/categories` | Manage categories |
| GET/POST | `/admin/presets` | Manage presets and category assignments |
| GET | `/admin/games` | List all non-archived games |
| GET | `/admin/games/archived` | List archived games |
| GET | `/admin/games/{sessionId}` | Game detail — player links, round state, scores |
| POST | `/admin/games/{sessionId}/end` | Force-end game + trigger LLM summary |
| POST | `/admin/games/{sessionId}/archive` | Soft-delete a finished game |
| POST | `/admin/games/{sessionId}/unarchive` | Restore an archived game |
| GET | `/admin/broadcast` | Broadcast composer + recipient picker |
| POST | `/admin/broadcast` | Send a broadcast |
| GET | `/admin/broadcast/history` | Log of all past broadcasts |
| GET | `/admin/broadcast/{id}` | Detail view for a past broadcast and its recipients |
| GET | `/admin/players` | List all registered players |
| GET | `/admin/players/{id}` | View a player's profile, stats, and achievements |
| POST | `/admin/players/{id}/achievement` | Manually award or revoke an achievement (admin override) |

### API / Internal Routes

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/round/answer` | Player submits answers for the current round |
| POST | `/api/round/guess` | Player submits guesses for the current round |
| POST | `/api/round/declare-final` | Player marks this as their last round |
| POST | `/api/question/flag` | Player flags a question during gameplay |
| GET | `/api/sse/{token}` | SSE stream — pushes round state changes to client |
| POST | `/api/fcm/register` | Player registers FCM device token |
| POST | `/api/chat/send` | Player sends a chat message |
| POST | `/api/chat/read` | Player marks all messages as read (called when chat panel opens) |
| POST | `/api/question/feedback` | Player submits question feedback (replaces simple flag) |
| POST | `/api/game/resend-invitation` | Organizer resends invitation email to a player |
| POST | `/api/game/rematch` | Player initiates a rematch from profile game history |

---

## 4. Data Model

### `players`
```
id              INTEGER PK
email           TEXT UNIQUE NOT NULL
nickname        TEXT
fcm_token       TEXT
created_at      DATETIME
last_seen_at    DATETIME
```

### `sessions`
```
id                   TEXT PK                -- GUID
organizer_id         INTEGER FK players     -- may be null if third-party organizer has no account
organizer_email      TEXT                   -- always set; used to send lobby status updates
player1_id           INTEGER FK players
player2_id           INTEGER FK players
status               TEXT                   -- 'pending_join', 'active', 'paused', 'finished', 'cancelled'
questions_per_round  INTEGER
decoy_count          INTEGER
join_expires_at      DATETIME               -- configurable; game cancelled if not both joined by this time
created_at           DATETIME
ended_at             DATETIME
archived_at          DATETIME
archived_by          TEXT
```

### `session_players`
```
session_id      TEXT FK sessions
player_id       INTEGER FK players
token           TEXT UNIQUE
nickname        TEXT
joined_at       DATETIME
final_round     INTEGER
```

### `session_categories`
```
session_id      TEXT FK sessions
category_id     INTEGER FK categories
player1_vote    BOOLEAN
player2_vote    BOOLEAN
weight          REAL                   -- 0.0 / 0.3 / 0.7
```

### `categories`
```
id              INTEGER PK
name            TEXT NOT NULL
description     TEXT
active          BOOLEAN DEFAULT TRUE
```

### `presets`
```
id              INTEGER PK
name            TEXT NOT NULL
description     TEXT
sort_order      INTEGER
active          BOOLEAN DEFAULT TRUE
```

### `preset_categories`
```
preset_id       INTEGER FK presets
category_id     INTEGER FK categories
PRIMARY KEY (preset_id, category_id)
```

### `questions`
```
id              INTEGER PK
text            TEXT NOT NULL
active          BOOLEAN DEFAULT TRUE
created_at      DATETIME
updated_at      DATETIME
```

### `question_categories`
```
question_id     INTEGER FK questions
category_id     INTEGER FK categories
PRIMARY KEY (question_id, category_id)
```

### `question_feedback`
```
id                   INTEGER PK
question_id          INTEGER FK questions
player_id            INTEGER FK players
session_id           TEXT FK sessions
round_question_id    INTEGER FK round_questions  -- which specific instance triggered feedback
phase                TEXT                        -- 'answering' or 'guessing'
flag_inappropriate   BOOLEAN DEFAULT FALSE
flag_duplicate       BOOLEAN DEFAULT FALSE
quality_rating       INTEGER                     -- 1-5 scale; 3 = indifferent (default)
flag_poor_decoys     BOOLEAN DEFAULT FALSE
notes                TEXT                        -- optional free text
created_at           DATETIME
reviewed_at          DATETIME
reviewed_by          TEXT
resolution           TEXT                        -- 'dismissed', 'deactivated', 'edited', 'noted'
```

### `rounds`
```
id              INTEGER PK
session_id      TEXT FK sessions
round_number    INTEGER
status          TEXT                   -- 'answering', 'guessing', 'complete'
started_at      DATETIME
completed_at    DATETIME
```

### `round_questions`
```
id              INTEGER PK
round_id        INTEGER FK rounds
question_id     INTEGER FK questions
sort_order      INTEGER
```

### `answers`
```
id              INTEGER PK
round_question_id  INTEGER FK round_questions
player_id       INTEGER FK players
answer_text     TEXT NOT NULL
submitted_at    DATETIME
```

### `decoys`
```
id              INTEGER PK
round_question_id  INTEGER FK round_questions
target_player_id   INTEGER FK players
decoy_text      TEXT NOT NULL
```

### `guesses`
```
id              INTEGER PK
round_question_id     INTEGER FK round_questions
guessing_player_id    INTEGER FK players
chosen_answer_id      INTEGER            -- FK answers.id (nullable)
chosen_decoy_id       INTEGER            -- FK decoys.id (nullable)
is_correct            BOOLEAN
points_awarded        INTEGER
submitted_at          DATETIME
```

### `otp_codes`
```
id              INTEGER PK
email           TEXT NOT NULL
code            TEXT NOT NULL
expires_at      DATETIME
used            BOOLEAN DEFAULT FALSE
created_at      DATETIME
```

### `player_sessions`
```
id              TEXT PK
player_id       INTEGER FK players
created_at      DATETIME
expires_at      DATETIME
```

### `achievements`
```
id              INTEGER PK
key             TEXT UNIQUE NOT NULL   -- e.g. 'perfect_round', 'ten_games_played'
name            TEXT NOT NULL          -- display name
description     TEXT NOT NULL          -- how it is earned (shown in admin, not on profile)
icon            TEXT NOT NULL          -- filename of badge image in wwwroot/img/achievements/
active          BOOLEAN DEFAULT TRUE   -- inactive achievements are not evaluated or displayed
sort_order      INTEGER
```

### `player_achievements`
```
id              INTEGER PK
player_id       INTEGER FK players
achievement_id  INTEGER FK achievements
awarded_at      DATETIME
awarded_by      TEXT                   -- 'system' or admin username if manually overridden
session_id      TEXT FK sessions       -- the game that triggered the award (nullable for manual)
```

### `broadcasts`
```
id              INTEGER PK
subject         TEXT NOT NULL
body            TEXT NOT NULL
channel         TEXT NOT NULL          -- 'email', 'fcm', 'both'
sent_at         DATETIME
sent_by         TEXT                   -- admin username
recipient_count INTEGER
```

### `broadcast_recipients`
```
id              INTEGER PK
broadcast_id    INTEGER FK broadcasts
player_id       INTEGER FK players
email           TEXT NOT NULL          -- denormalised for audit trail
channel_used    TEXT
fcm_success     BOOLEAN
email_success   BOOLEAN
error_message   TEXT
```

### `chat_messages`
```
id              INTEGER PK
session_id      TEXT FK sessions
sender_id       INTEGER FK players
message_text    TEXT NOT NULL
sent_at         DATETIME
read_at         DATETIME               -- null until the recipient has viewed the chat panel
```

---

## 5. Configuration Parameters (appsettings.json / env overrides)

```json
{
  "Game": {
    "QuestionsPerRound": 2,
    "DecoyCount": 3,
    "CategoryWeightOneVote": 0.3,
    "CategoryWeightBothVotes": 0.7,
    "OtpExpiryMinutes": 10,
    "SessionCookieExpiryDays": 30,
    "JoinTokenExpiryDays": 7,
    "LlmRetryCount": 3,
    "RateLimitOtpPerHour": 5,
    "RateLimitSignupPerHour": 10,
    "RateLimitChatPerMinute": 10,
    "RateLimitApiPerMinute": 60
  },
  "Llm": {
    "Provider": "Anthropic",
    "AnthropicApiKey": "",
    "OpenAiApiKey": "",
    "Model": "claude-sonnet-4-20250514",
    "DecoyCountOverride": null
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "FromAddress": "",
    "FromName": "AmIRite",
    "AppPassword": ""
  },
  "Fcm": {
    "ProjectId": "",
    "CredentialsPath": ""
  },
  "Admin": {
    "Username": "",
    "Password": ""
  }
}
```

---

## 6. Core Game Flow (Detailed)

### 6.1 Game Setup

1. Organizer visits `/signup`, enters two player email addresses. The organizer may or may not be one of the players — the signup form does not require the organizer to identify themselves as a player.
2. System creates a `sessions` record (status: `pending_join`) with `join_expires_at` set to now + `JoinTokenExpiryDays`. Two `session_players` records are created, each with a unique token (GUID).
3. System sends each player an invitation email containing their `/join/{token}` link.
4. Organizer is redirected to `/lobby/{sessionId}` — a status page showing which players have joined and which have not, with a resend invitation button per player.
5. The lobby page polls (or uses SSE) to update join status in real time. Once both players have joined it shows a "Game has started" message and a link to the results page for later reference.

### 6.1.1 Join Token Expiry and Abandoned Games

- If `join_expires_at` passes and one or both players have not joined, a `BackgroundService` sets session status → `cancelled`.
- Cancelled sessions are retained in the database for admin visibility but are excluded from all player-facing views.
- Both the organizer and any players who had already joined receive an email notifying them the game was cancelled due to inactivity.

### 6.2 Player Join

1. Player visits `/join/{token}`.
2. If not authenticated: redirect to `/auth/otp`, pre-filled with the email tied to that token.
3. OTP flow: player enters email → 6-digit code emailed → player enters code → `amirite_session` HttpOnly cookie set.
4. Player returns to `/join/{token}` (now authenticated).
5. Player sets a nickname for this game.
6. Player selects question categories:
   - Preset tiles shown first. Selecting a preset pre-checks the relevant category checkboxes.
   - Player can freely adjust individual checkboxes after selecting a preset, or skip presets entirely.
   - Final submitted state is always the individual category selections.
7. Once both players have joined, system computes `session_categories` weights, creates Round 1, sets session status → `active`, and notifies both players.

### 6.3 Round — Answer Phase

1. Player visits `/play/{token}`. Page shows current round number and question(s).
2. A "Give Feedback" button appears alongside each question (available in both answer and guess phases — see §6.3.1).
3. Player types answers and submits (`POST /api/round/answer`).
4. Page transitions to waiting state via SSE.

### 6.3.1 Question Feedback Panel

Available in both the answer and guess phases via a "Give Feedback" button on each question. Opens a small panel with four independent options:

- **Flag inappropriate** — marks the question as potentially offensive or unsuitable (boolean).
- **Flag duplicate** — marks the question as a duplicate of another question the player has seen (boolean).
- **Rate question quality** — a 1–5 spectrum selector; default is 3 (indifferent). Players are not required to rate.
- **Flag poor decoy quality** — only shown during the guess phase; indicates the LLM-generated decoys were obviously fake or unhelpful (boolean).

All options are optional. Submitting with no changes is a no-op. Feedback is stored in `question_feedback` and surfaced in the admin flag review queue. The panel closes after submission without interrupting the game flow.

### 6.4 LLM Decoy Generation

1. Triggered once both players have submitted answers.
2. For each question, LLM called twice (once per player) to generate `decoy_count` style-matched decoys.
3. On failure, retried up to `LlmRetryCount` times (default 3) with exponential backoff.
4. If all retries fail: session status → `paused`; both players receive a notification ("We're having a technical issue — the game will resume shortly"); admin receives an alert email. A `BackgroundService` continues retrying on a 5-minute interval until successful, then resumes the round and notifies both players.
5. On success: decoys stored; round status → `guessing`; SSE pushes update to both clients.

### 6.5 Round — Guess Phase

1. Each player sees a shuffled list per question: opponent's real answer + decoys in opponent's style.
2. Player selects one answer per question and submits (`POST /api/round/guess`).
3. 1 point awarded per correct guess.
4. Once both players have submitted, round status → `complete`.

### 6.6 Round Results & Next Round

1. Both players see previous round results: answers revealed, correctness, cumulative scores.
2. If game continues, new round created and answer phase begins.
3. Notification sent unless SSE connection is live (see §9).

### 6.7 Game End

Triggered when either player's declared final round completes, or the question pool is exhausted.

1. Session status → `finished`, `ended_at` set.
2. LLM generates full game analysis (see §7.2).
3. Both players receive a final email with a link to `/results/{sessionId}`.

### 6.8 Admin Force-End

When an admin posts to `/admin/games/{sessionId}/end`:
1. Session status → `finished`, `ended_at` set immediately regardless of round state.
2. Triggers the exact same code path as natural game end — LLM summary generated, final email sent to both players.
3. Action logged with admin username and timestamp.

---

## 7. LLM Usage

### 7.1 Decoy Generation Prompt Contract

**System prompt:**
```
You are generating plausible fake answers to a personal question for a party game.
The real answer provided by the player is given below.
Generate exactly {decoy_count} alternative answers that:
- Are plausible responses to the question
- Match the approximate length, tone, and style of the real answer
- Are clearly distinct from the real answer and from each other
- Do not give away that they are fake
Return ONLY a JSON array of strings. No explanation, no preamble.
Example: ["answer one", "answer two", "answer three"]
```

**User message:**
```
Question: {question_text}
Real answer: {player_answer}
```

### 7.2 End-of-Game Analysis Prompt Contract

**System prompt:**
```
You are writing a fun, warm end-of-game summary for a two-player "how well do you know each other" game.
Be specific, reference the actual questions and answers, and keep a light tone.
Structure your response as:
1. A 2-3 sentence overall narrative about how well the players know each other.
2. Category-level callouts: which categories each player excelled at or struggled with.
3. Highlight 1-2 specific moments — e.g. "You both completely fooled each other on the childhood memories question."
4. A closing sentence that feels personal and warm.
```

**User message:** Full game data as JSON (both nicknames, all rounds with questions/answers/guesses/categories, final scores).

---

## 8. Question Selection Algorithm

**Eligibility:** `active = true`; at least one category with `weight > 0` for this session; not used in a prior round of this session.

**Per-question weight:** maximum weight among the question's session-active categories, multiplied by `recency_factor` (stub as `1.0` for v1).

**Category vote → weight:**

| P1 vote | P2 vote | Weight |
|---|---|---|
| No | No | 0.0 |
| Yes | No | 0.3 |
| No | Yes | 0.3 |
| Yes | Yes | 0.7 |

Selection is a weighted random draw without replacement for the round.

---

## 9. Notification Logic

**FCM (primary):** all mid-game round-advance notifications. Falls back to email if `fcm_token` is null or delivery fails.

**Email (always used for):** invitation, final results, and any FCM fallback.

**Presence detection via SSE:** server tracks live connections in `ConcurrentDictionary<token, SseClient>`. If connection is live when a round advances, push HTMX partial update and skip FCM/email.

---

## 10. Auth & Session Model

**OTP flow:** email → 6-digit code (10-minute TTL) → validated → `amirite_session` HttpOnly cookie (30-day expiry).

**Token binding:** after auth, server validates authenticated `player_id` matches `session_players.token`. A player can be in multiple concurrent games.

**Admin:** HTTP Basic Auth on all `/admin/*` routes; `ADMIN_USERNAME` and `ADMIN_PASSWORD` environment variables; no session.

---

## 11. Rate Limiting

All endpoints are rate limited using an in-memory sliding window counter keyed by IP address. Limits are configurable via appsettings:

| Endpoint group | Default limit |
|---|---|
| OTP request (`POST /auth/otp`) | 5 requests per hour per IP |
| Signup (`POST /signup`) | 10 requests per hour per IP |
| Chat send (`POST /api/chat/send`) | 10 messages per minute per player token |
| All other API endpoints | 60 requests per minute per IP |

Requests exceeding the limit receive a `429 Too Many Requests` response with a `Retry-After` header. No ban or lockout — limits reset on the sliding window. Admin routes are exempt from rate limiting.

---

## 12. Admin UI Scope

### Question Management
- List with category tags, active status, flag count, last updated.
- Create / edit / soft-delete / hard-delete (with confirmation).
- Bulk operations: activate, deactivate, reassign categories.
- CSV import (columns: `text`, `category_names` pipe-separated).

### Feedback Review Queue
- Replaces the simple flag queue; sourced from `question_feedback`.
- Sorted by a combined signal: inappropriate flags weighted highest, then poor decoy flags, then low quality ratings.
- Per entry: question text, flag counts by type, average quality rating, phase it was flagged in, games it appeared in.
- Actions: Dismiss, Deactivate, Edit inline, Note (add admin comment).
- All resolutions recorded with `reviewed_at` and `reviewed_by`.

### Category & Preset Management
- Categories: list, create, rename, deactivate.
- Presets: list, create, edit, deactivate; manage category membership per preset.

### Game Management
- **Default list** (`/admin/games`): all non-archived games showing player nicknames + emails, status, current round, direct links to each player's `/play/{token}` page, End button (active games only), Archive button (finished games only).
- **Archived list** (`/admin/games/archived`): same columns; Unarchive button.
- **Game detail** (`/admin/games/{sessionId}`): full round-by-round breakdown, questions, answers, guesses, scores; End and Archive actions.
- All admin actions logged with username and timestamp.

### Broadcast & Messaging
See §12.

---

## 13. Broadcast & Messaging

### Recipient Picker
- Full list of all registered players showing email and nickname, each with a checkbox.
- **Filter controls** (stackable, applied to visible list):
  - Text filter — substring match on email or nickname (case-insensitive)
  - Regex filter — applied to email or nickname
  - Idle filter — `last_seen_at` older than N days (admin enters N)
  - Game status filter — in active game / finished game / no game
  - FCM capability filter — has FCM token / does not have FCM token
- **Bulk selection controls** (operate on currently filtered list):
  - Select all visible
  - Deselect all visible
  - Invert selection
- Selected recipient count shown at all times.

### Broadcast Composer
- Fields: Subject, Body (free text), Channel (Email / FCM Push / Both).
- Players without an FCM token show a warning indicator when FCM or Both is selected.
- Confirmation step before send: "You are about to send to N recipients via [channel]. Confirm?"
- Sent immediately on confirmation (no scheduling in v1).

### Broadcast Log
- `/admin/broadcast/history`: all broadcasts with subject, channel, recipient count, sent-by, sent-at.
- `/admin/broadcast/{id}`: per-recipient delivery status (FCM success/fail, email success/fail, error messages).

---

## 14. Scoring

- 1 point per correct guess per question.
- Maximum per round = `questions_per_round`.
- Cumulative across all rounds.
- No speed bonus, no wager mechanic, no category weighting on points.

---

## 15. Results Page (`/results/{sessionId}`)

No auth required with the link; accessible indefinitely regardless of archive status.

1. Header: both nicknames, final scores.
2. LLM narrative analysis.
3. Per-category accuracy breakdown (% correct per player per category).
4. Round-by-round expandable log.
5. Chat transcript (collapsible, shown below the round log).
6. Share prompt.

---

## 16. Player Profile Page (`/profile`)

Requires OTP auth. Players can only view their own profile.

### Navigation Bar (all player-facing pages)
A persistent top nav bar contains:
- **AmIRite logo / home link** (left)
- **Game pebbles** (centre, scrollable horizontally on mobile): one small pill per active game showing the opponent's nickname and a status dot — green ("your turn"), amber ("waiting for opponent"), grey ("joining"). Tapping a pebble navigates directly to `/play/{token}` for that game.
- **Profile link** (right)
- **Theme toggle** (sun/moon icon, right)
- **Sound toggle** (speaker icon, right)

### Stats Panel
- Total games played
- Win / loss / draw record
- Average score per game
- Per-category accuracy: correct guess % per category across all games

### Achievements Panel
- Grid of earned achievement badges (icon + name only)
- Unearned achievements not shown
- Sorted by `achievements.sort_order`

### Game History
**Active games** (top section):
- Each active game shows opponent nickname, current round, status, and a "Continue" button linking to `/play/{token}`.

**Finished games** (history section):
- Chronological list: opponent nickname, date played, score vs opponent score, link to `/results/{sessionId}`, and a **Rematch** button.
- Rematch triggers `POST /api/game/rematch` which creates a new session with the same two players and redirects the initiating player to `/lobby/{sessionId}`. The opponent receives a new invitation email.

### Nicknames
- All distinct nicknames used across games, most recent first.

---

## 17. Achievement System

### How Achievements Are Evaluated
- Achievement evaluation is triggered at the end of every completed round and at game end.
- A `BackgroundService` worker processes a queue of evaluation jobs so award logic doesn't block the game flow.
- Each achievement has a corresponding evaluator class implementing a common interface (e.g. `IAchievementEvaluator`). New achievements are added by implementing the interface and registering the evaluator — no changes to core game logic required.
- Duplicate awards are prevented by checking `player_achievements` before inserting.

### Suggested Starter Achievements
These should be seeded into the `achievements` table at build time:

| Key | Name | Trigger |
|---|---|---|
| `first_game` | First Game | Complete your first game |
| `perfect_round` | Perfect Round | Guess all questions correctly in a single round |
| `perfect_game` | Perfect Game | Guess all questions correctly across an entire game |
| `ten_games` | Seasoned Player | Complete 10 games |
| `twenty_five_games` | Veteran | Complete 25 games |
| `sharp_eye` | Sharp Eye | Achieve 80%+ overall accuracy across any single game |
| `category_ace_{cat}` | Category Ace | Achieve 100% accuracy in a specific category across any game (one per category) |
| `fooled_them_all` | Fooled Them All | Opponent guesses none of your answers correctly in a single round |
| `mind_reader` | Mind Reader | Correctly guess opponent's answer on every question in 5 consecutive rounds |

### Admin Visibility
The admin player detail page (`/admin/players/{id}`) shows the player's full achievement list with awarded dates and the triggering session. Admins can manually revoke an achievement (recorded with admin username) but cannot manually award them — the system is the sole source of truth for automatic awards.

---

### `/play/{token}` States

| State | What player sees |
|---|---|
| Answering | Questions with free-text inputs; feedback button per question; "Declare final round" checkbox; Submit |
| Waiting (after answering) | "Waiting for [opponent]…" spinner; SSE-connected |
| Guessing | Shuffled answer choices per question; feedback button per question; Submit |
| Waiting (after guessing) | "Waiting for [opponent]…" spinner; SSE-connected |
| Round results | Answers revealed; scores; next round loads automatically |
| Paused (LLM failure) | "We're having a technical issue — hang tight" message; SSE-connected for resume notification |
| Game over | "Game Over" state with prominent "See Results" link to `/results/{sessionId}` |

---

## 18. Build Priority Order (for Claude Code)

1. **Database schema + migrations** — all tables.
2. **Auth + rate limiting** — OTP flow, session cookie, player creation, sliding window rate limiter.
3. **Signup + Lobby + Join flow** — `/signup`, invite emails, `/lobby/{sessionId}` status page with resend, `/join/{token}`, preset tiles + category selection, join token expiry + cancellation worker.
4. **Question selection algorithm** — weighted random draw.
5. **Round answer phase** — `/play/{token}` answering UI, feedback panel, `POST /api/round/answer`.
6. **LLM decoy generation** — Anthropic SDK, prompt, retry logic, paused state + admin alert on failure.
7. **Round guess phase** — guess UI, feedback panel (with poor decoys option), `POST /api/round/guess`, scoring.
8. **SSE presence + round advancement** — in-page updates, stateless reconnection, notification skip logic.
9. **FCM + email notifications** — register token, send on round advance, LLM pause notification.
10. **Round results view** — show previous round before new one begins.
11. **Game end + LLM analysis** — end conditions, summary, final email, game over state on play page.
12. **Results page** — `/results/{sessionId}`, stats + LLM narrative + chat transcript.
13. **Navigation bar** — game pebbles, profile link, theme toggle, sound toggle.
14. **Admin — questions** — CRUD, bulk ops, CSV import, feedback review queue.
15. **Admin — categories + presets** — CRUD, category-preset assignments.
16. **Admin — game management** — game list, detail, force-end, archive/unarchive, paused game handling.
17. **Admin — broadcast** — recipient picker with filters, composer, send, history log.
18. **Player profile page** — `/profile`, stats panel, active games with pebbles, game history with rematch, nicknames.
19. **Achievement system** — evaluator framework, starter achievements, background evaluation worker.
20. **In-game chat** — `chat_messages` table, bottom bar, slide-up panel, unread badge, SSE delivery.
21. **Fanfare + sound** — canvas-confetti integration, Web Audio API, consent banner, mute toggle.
22. **Polish** — landing page copy, edge cases, error states, mobile layout refinements.

---

## 19. SSE Implementation Notes

SSE is a simple, mature protocol and a natural fit for AmIRite's server-driven state model. The following notes capture known gotchas and implementation requirements for the .NET / Fly.io environment.

### Async hygiene (critical)

Each SSE handler must be a proper `async` endpoint that awaits on a `CancellationToken` rather than blocking a thread. Blocking (via `.Result` or `.Wait()`) would consume a thread pool thread per open connection, which is the classic ASP.NET resource exhaustion pattern. Done correctly, open connections are very cheap — just paused continuations.

```csharp
app.MapGet("/api/sse/{token}", async (string token, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.Headers["Content-Type"] = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    sseClients.TryAdd(token, ctx.Response);
    try
    {
        await Task.Delay(Timeout.Infinite, ct);
    }
    finally
    {
        sseClients.TryRemove(token, out _);
    }
});
```

### Cleanup on disconnect (critical)

The `finally` block in the handler above is non-negotiable. It ensures the `ConcurrentDictionary` entry is always removed when a connection closes — whether via normal disconnect, timeout, or server shutdown. Without it, stale entries accumulate in memory indefinitely.

### Heartbeat (required on Fly.io)

Fly.io's proxy will kill idle connections that haven't transmitted data within its timeout window. A `BackgroundService` should send a SSE comment (which is invisible to the client) to every registered connection every 30 seconds:

```
: heartbeat\n\n
```

This serves two purposes: it keeps the proxy from closing idle connections, and it provides a natural point to detect and evict stale entries — any connection that throws or is no longer writable during the heartbeat sweep should be removed from the dictionary.

### Response buffering (required on Fly.io)

The `X-Accel-Buffering: no` header (shown above) is essential. Without it, Fly.io's nginx layer will buffer SSE events and deliver them in batches rather than in real time, breaking the round-advancement flow.

### Simultaneous connections per player

If a player opens the game in two browser tabs, two SSE connections will attempt to register under the same token. Since the dictionary holds one entry per token, the first tab will silently stop receiving updates. Mitigation options: store a list of connections per token and broadcast to all of them, or detect the duplicate at registration time and close the older connection. The two-tab scenario is unlikely in practice but should be handled deliberately.

### File descriptors

Each SSE connection consumes one file descriptor. Linux process defaults (typically 1024) are fine for AmIRite at any realistic scale, but the limit should be explicitly raised in the Fly.io configuration as a precaution.

### HTMX SSE extension

Rather than writing JavaScript `EventSource` handlers, use the HTMX SSE extension (`hx-ext="sse"`). The server sends named events containing pre-rendered HTML partials; HTMX swaps them directly into the page. This keeps the frontend entirely server-driven and requires no client-side rendering logic.

```html
<div hx-ext="sse" sse-connect="/api/sse/{token}" sse-swap="round_advanced">
  <!-- replaced by server-rendered HTML when round_advanced event arrives -->
</div>
```

The server sends:
```
event: round_advanced
data: <div>...rendered HTML partial...</div>

```

### Connection lifecycle in context

AmIRite's connection profile is favorable: connections are only active during a round while a player is waiting for their opponent. They are not held open for hours. At realistic usage levels (tens of concurrent games rather than thousands) resource exhaustion is not a practical concern, provided the async hygiene and cleanup rules above are followed correctly.

---

## 20. UI Design Guidelines

### Theming

AmIRite supports light and dark themes implemented via CSS custom properties (design tokens) on `:root`, overridden under `[data-theme="dark"]` on the `<html>` element.

**Default behaviour:** respect the OS preference via `prefers-color-scheme` on first visit. The player can override and their preference is stored in `localStorage` so it persists across visits without requiring a server round-trip.

```javascript
// On page load (inline in <head> to avoid flash)
const tStored = localStorage.getItem('theme');
const tPreferred = tStored ?? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
document.documentElement.setAttribute('data-theme', tPreferred);
```

A theme toggle button (sun/moon icon) should appear in the top navigation bar on all pages. Clicking it flips the `data-theme` attribute and saves to `localStorage`.

**Token naming convention** — define all colours, backgrounds, and borders as tokens:
```css
:root {
  --color-bg:           #ffffff;
  --color-bg-surface:   #f5f5f5;
  --color-text:         #1a1a1a;
  --color-text-muted:   #666666;
  --color-primary:      #5b4fcf;
  --color-primary-hover:#4a3fbf;
  --color-correct:      #22c55e;
  --color-incorrect:    #ef4444;
  --color-border:       #e2e2e2;
  --shadow-card:        0 1px 3px rgba(0,0,0,0.08);
}

[data-theme="dark"] {
  --color-bg:           #0f0f0f;
  --color-bg-surface:   #1c1c1c;
  --color-text:         #f0f0f0;
  --color-text-muted:   #999999;
  --color-primary:      #7c6fe0;
  --color-primary-hover:#9080f0;
  --color-correct:      #4ade80;
  --color-incorrect:    #f87171;
  --color-border:       #2e2e2e;
  --shadow-card:        0 1px 3px rgba(0,0,0,0.4);
}
```

No component should hardcode a colour value — all colours must reference a token.

### Typography

**Font:** Nunito (Google Fonts), loaded via a single `<link>` in the base layout. Nunito is rounded and friendly, highly legible on mobile at all sizes, and works well as both a heading and body font without needing a second typeface.

**Fallback stack:** `'Nunito', system-ui, -apple-system, sans-serif`

**Scale:**
```css
--text-xs:   0.75rem;   /* labels, captions */
--text-sm:   0.875rem;  /* secondary body, helper text */
--text-base: 1rem;      /* primary body */
--text-lg:   1.125rem;  /* lead text, question text */
--text-xl:   1.25rem;   /* card headings */
--text-2xl:  1.5rem;    /* page headings */
--text-3xl:  1.875rem;  /* hero / game title */
```

Question text on the play page should use `--text-lg` minimum to ensure comfortable reading on small screens.

### Layout

**Mobile-first.** All layouts are designed for a single-column phone view and adapted upward with `min-width` breakpoints:
- `sm`: 640px — minor spacing adjustments
- `md`: 768px — two-column layouts where appropriate (e.g. results page)
- `lg`: 1024px — constrain max content width to ~720px and centre

Maximum content width: `720px`, centred with `margin: 0 auto` on a wrapper. This keeps the game readable on tablets and desktops without sprawling across wide screens.

### Animation and Transitions

Use moderate animation — enough to give the UI life and reward correct guesses, not so much that it delays gameplay.

**State transitions** (HTMX partial swaps): fade-in over 200ms using a CSS class applied on swap:
```css
.htmx-added { animation: fadeIn 200ms ease-out; }
@keyframes fadeIn { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: none; } }
```

**Celebratory moments:**
- Correct guess: brief green flash + checkmark animation on the chosen answer card (CSS keyframe, ~400ms).
- Perfect round: a subtle confetti burst (lightweight CSS-only or a small JS library — no heavy dependencies).
- Achievement unlocked: a toast notification slides in from the bottom, displaying the badge icon and name, auto-dismisses after 3 seconds.

**Loading states:** a simple CSS spinner (no image dependency) used during HTMX requests and LLM wait states.

**No transition** on theme toggle — instant swap avoids a jarring colour bleed effect.

See §21 for the full fanfare and sound effects specification.

### Score and Round Display

A persistent header strip is shown on all `/play/{token}` states. It displays:
- Current round number (e.g. "Round 3")
- Player's own score vs opponent's score (e.g. "You 4 — 3 Alex")

The same information is repeated more prominently on the round results screen after each round completes.

### Loading, Error, and Empty States

- **Loading:** CSS spinner component, centred, used consistently wherever the page is waiting (LLM generation, HTMX in-flight requests).
- **Errors:** styled error card with a brief human-readable message and a retry action where applicable. No raw exception text visible to players.
- **Empty states:** plain text only (e.g. "No games yet — start one!"). No illustrations in v1.
- **Toast notifications:** used for non-blocking feedback (achievement unlocked, message sent, flag submitted). Slide in from bottom-right on desktop, bottom-centre on mobile. Auto-dismiss after 3 seconds.

---

## 21. In-Game Chat

### Overview

Players can send each other short messages during a game in an SMS-style format. Chat is scoped to a single game session and persists in the database for the lifetime of the game.

### Bottom Bar

A persistent bottom bar is present on all `/play/{token}` states. It contains:
- **Chat button** (left): labelled with an icon and an unread message count badge (e.g. 🗨 3). Badge is hidden when count is zero.
- **Status area** (right of button, fills remaining width): displays the most recent game status message — e.g. "Waiting for Alex…", "Alex has submitted their answers", "Round 3 complete". Updated via SSE alongside other page updates.

The bottom bar sits above any browser chrome and does not scroll with the page.

### Chat Panel

Tapping the chat button opens a slide-up panel (full-width on mobile, max ~400px on desktop) that overlays the game content without navigating away. The panel contains:

- **Transcript area** (scrollable): messages displayed in SMS bubble style — player's own messages right-aligned, opponent's messages left-aligned. Each message shows the sender's nickname and timestamp.
- **Input row** (pinned to bottom of panel): a text input and a Send button. Submits via `POST /api/chat/send`; the response returns an updated transcript partial that HTMX swaps in.
- **Close button**: dismisses the panel, returning focus to the game.

Opening the panel triggers `POST /api/chat/read`, which sets `read_at` on all unread incoming messages and clears the unread badge.

### Unread Count

- Maintained server-side as a count of `chat_messages` where `sender_id != current_player_id` and `read_at IS NULL` for this session.
- When the opponent sends a message, the SSE connection pushes an updated bottom bar partial to the recipient, refreshing the badge count without requiring a full page update.
- The badge does not require the player to open the chat panel to dismiss — it clears automatically when the panel is opened.

### Data Model

`chat_messages` table (see §4):
- Messages are retained for the lifetime of the game session.
- No purge on game end in v1 — messages remain readable via the results page or profile game history if desired in a future version.

### Constraints

- Maximum message length: 500 characters (enforced client-side and server-side).
- No media attachments, reactions, or threading in v1.
- Chat is not available before both players have joined (session status must be `active`).

---

## 22. Fanfare, Animation, and Sound Effects

### Animation Library

Two animation mechanisms are used, chosen by moment size:

**canvas-confetti** (via CDN, ~3KB) — used for high-celebration moments. Fires configurable bursts of coloured particles on a transparent canvas overlay that sits above all page content. Requires no build step; loaded via a single `<script>` tag from `cdnjs.cloudflare.com`. Usage is approximately 10 lines of JS per trigger.

**CSS keyframes** — used for smaller, inline feedback moments on specific elements (answer cards, score counters, badges). No library dependency.

### Trigger Map

| Moment | Animation | Sound |
|---|---|---|
| Correct guess | Green flash + checkmark pulse on answer card (CSS) | — |
| Incorrect guess | Red flash + shake on answer card (CSS) | — |
| Perfect round | Canvas-confetti burst (moderate, centred) | — |
| Achievement unlocked | Canvas-confetti burst (celebratory, full-width) + achievement toast slides in | ✓ |
| Game over | Canvas-confetti burst (large, sustained) + score tallies animate up | ✓ |
| New chat message received | Chat button bounces once (CSS) + badge count increments | ✓ |
| Round complete | Score counter animates to new value (CSS counter transition) | — |

### Canvas-Confetti Implementation Notes

Load from CDN in the base layout:
```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/canvas-confetti/1.9.3/confetti.browser.min.js"></script>
```

A small `amirite-fx.js` file in `wwwroot/js/` wraps the triggers so HTMX partial swaps can fire them by dispatching a custom DOM event:

```javascript
// amirite-fx.js
document.addEventListener('amirite:celebrate', (e) => {
    const tLevel = e.detail?.level ?? 'moderate';
    if (tLevel === 'large') {
        confetti({ particleCount: 200, spread: 160, origin: { y: 0.5 } });
    } else if (tLevel === 'moderate') {
        confetti({ particleCount: 80, spread: 70, origin: { y: 0.6 } });
    } else {
        confetti({ particleCount: 40, spread: 50, origin: { y: 0.6 } });
    }
});
```

Server-rendered HTML partials trigger celebrations by including an inline script or an HTMX `hx-on` attribute that dispatches the event after swap:

```html
<!-- Included in the achievement toast partial -->
<div hx-on:htmx:after-swap="document.dispatchEvent(new CustomEvent('amirite:celebrate', {detail:{level:'large'}}))">
```

This keeps all animation logic client-side while allowing the server to declaratively trigger it via HTML.

### Sound Effects

**Files:** A small set of audio files stored in `wwwroot/audio/`. Use `.mp3` format with an `.ogg` fallback for maximum browser compatibility. Files should be short (under 2 seconds) and subtle in volume.

| File | Trigger |
|---|---|
| `achievement.mp3` | Achievement unlocked |
| `gameover.mp3` | Game over |
| `message.mp3` | New chat message received |

**Web Audio API:** all playback handled via the Web Audio API (no `<audio>` elements). A small `amirite-audio.js` module in `wwwroot/js/` manages the audio context and exposes a `playSound(key)` function.

**Browser autoplay policy:** browsers block audio until the user has interacted with the page. The audio context must be initialised (resumed) on the first user gesture. Implementation:

```javascript
// amirite-audio.js
let _context = null;
let _muted = localStorage.getItem('soundEnabled') !== 'false'; // default: enabled after consent

async function _ensureContext() {
    if (!_context) {
        _context = new AudioContext();
    }
    if (_context.state === 'suspended') {
        await _context.resume();
    }
}

export async function playSound(pKey) {
    if (!_muted) { return; }
    await _ensureContext();
    // fetch and decode buffer, then play — implementation detail for Claude Code
}
```

**First-visit consent prompt:** on the first page load, a small unobtrusive banner appears at the top of the page:

> 🔊 Enable sound effects? [Yes] [No thanks]

Tapping either option stores the preference in `localStorage` (`soundEnabled: true/false`) and dismisses the banner permanently. This prompt only appears once and is never shown again after the player makes a choice.

**Mute toggle:** a speaker icon button in the top navigation bar (alongside the theme toggle) lets the player change their sound preference at any time. State stored in `localStorage`; no server round-trip needed.

**Mute during answer phase:** sound is automatically suppressed for the "message received" effect when the player is actively typing an answer, to avoid breaking concentration. Re-enabled once the answer is submitted.

### Colour Palette for Confetti

Confetti colours should use the brand palette to feel cohesive:
```javascript
const tColors = ['#5b4fcf', '#7c6fe0', '#22c55e', '#f59e0b', '#ef4444', '#ffffff'];
```

---

## 23. Coding Standards

All code in the AmIRite solution should follow these conventions. Claude Code should apply them consistently without needing to be reminded per-prompt.

### Naming

- **Method parameters:** `p` prefix — e.g. `pFilePath`, `pSessionId`.
- **Method-local variables:** `t` prefix — e.g. `tResult`, `tNow`. Exceptions: lambda parameters and loop iteration variables (e.g. `foreach (var item in items)`).
- **Private instance fields:** `_camelCase` — e.g. `_dbConnection`, `_logger`.
- **Async methods:** `Async` suffix — e.g. `GetPlayerAsync`, `SaveAnswerAsync`. Standard C# convention; retained because it conveys useful information at call sites.
- **Everything else** (classes, public members, constants): standard C# PascalCase.

### Types and Declarations

- **`var` for locals:** use `var` when the type is immediately obvious from the right-hand side (e.g. `var tPlayer = new Player()`). Use explicit types when the right-hand side does not make the type clear (e.g. `HttpResponseMessage tResponse = await client.SendAsync(tRequest)`).
- **Fields and properties:** use explicit type + target-typed `new()` — e.g. `private readonly List<string> _nicknames = new()`. Do not use `var` for field or property declarations.
- **Access modifiers:** always explicit — never rely on defaults. Every class member must declare `public`, `private`, `protected`, or `internal`.
- **Nullable reference types:** enabled solution-wide (`<Nullable>enable</Nullable>`). Annotate nullable types explicitly with `?`; do not suppress warnings with `!` unless unavoidable and commented.
- **Records:** use `record` (or `record class`) for DTOs, value objects, and any type whose identity is defined by its data rather than its reference. Use regular `class` for services, domain logic, and anything with meaningful mutable state.
- **Primary constructors:** use for simple service classes where the sole purpose is capturing injected dependencies into `readonly` fields. Use traditional constructors for any class with non-trivial initialization logic.
- **File-scoped namespaces:** always — e.g. `namespace AmIRite.Web;` — not block-scoped.
- **Target framework:** `net10.0`.

### Formatting

- **Indentation:** tabs, not spaces.
- **Braces:** always use braces for `if`, `else`, `for`, `while`, `foreach`, and `do` — even for single-statement bodies.
- **One type per file:** each class, record, interface, or enum in its own file, named to match the type.

### Patterns and Practices

- **No magic strings or numbers:** use named constants or configuration values.
- **No `.Result` or `.Wait()`** on async code — always `await`. This is especially critical in SSE handlers (see §18).
- **`CancellationToken` propagation:** pass `CancellationToken` through the full call chain from HTTP handler to database query wherever supported by Dapper or the LLM SDK.
- **Error handling:** structured exception handling at service boundaries; never swallow exceptions silently. Log with context (session ID, player ID, round ID) wherever applicable.
- **No EF Core:** all database access via Dapper with explicit SQL. Queries live in repository classes, not scattered across handlers.
- **Dependency injection:** register all services via the .NET DI container; no service locator pattern.

---

## 24. Category and Preset Seed Data

### Categories

The following categories should be seeded at database initialisation time. Each category has a colour used exclusively for UI chips/dots (category picker at join time, admin question list, results breakdown). Colours are stored in the `categories` table as a `color` column (hex string).

| Name | Description | Color |
|---|---|---|
| Favorites | Food, colours, seasons, and other simple preferences | #f59e0b |
| Preferences | Morning person, introvert/extrovert, and lifestyle tendencies | #8b5cf6 |
| Fun Facts | Superpowers, time travel, hidden talents, and imaginative self-description | #10b981 |
| Personal Values | What matters most in life, ideal days, core priorities | #3b82f6 |
| Relationship Questions | Shared values, relationship goals, and compatibility themes (non-intimate) | #ec4899 |
| Deep Questions | Fears, dreams, regrets, and reflective introspection | #6366f1 |
| Romantic | Love languages, intimacy, and relationship topics of a more sensitive nature | #ef4444 |
| Playful/Silly | Would you rather, random oddball preferences, absurd hypotheticals | #f97316 |
| Professional | Work style, career goals, and professional identity | #64748b |
| Other | Catchall for questions that don't fit elsewhere | #9ca3af |
| Origin Stories | Formative moments, turning points, and early influences | #b45309 |
| What Would You Do? | Thought experiments that reveal priorities and decision-making | #0d9488 |
| Who Are You? | How you see yourself vs. how others see you | #7c3aed |
| What's Your Tempo? | Pace, momentum, and life rhythm | #0284c7 |
| Adventure & Risk | Appetite for uncertainty, boldness, and the unknown | #dc2626 |
| Taste & Style | Design sensibility, atmosphere, and sensory identity | #db2777 |
| Memory Lane | Nostalgia, past selves, and formative memories | #ca8a04 |
| Social Life | Group behaviour, social dynamics, and influence patterns | #16a34a |

The `categories` table needs a `color` column added:
```
color    TEXT    -- hex colour string, e.g. '#f59e0b'
```

### Presets

Four presets should be seeded. The distinction between **Romantic** and **Relationship** should be surfaced clearly in the preset description shown to players at join time.

| Preset | Description | Categories Included |
|---|---|---|
| Fun | Light-hearted and playful — great for friends or anyone who wants to keep things fun | Favorites, Fun Facts, Playful/Silly, What Would You Do?, Adventure & Risk, Taste & Style |
| Personal | Thoughtful and revealing — good for people who want to understand each other more deeply | Preferences, Personal Values, Deep Questions, Origin Stories, Who Are You?, What's Your Tempo?, Memory Lane |
| Relationship | Connection-focused questions for couples or close friends — explores compatibility and shared values without sensitive content | Relationship Questions, Personal Values, Deep Questions, Who Are You?, Memory Lane, Social Life |
| Romantic | Everything in Relationship plus more intimate topics — intended for romantic partners comfortable with sensitive questions | Relationship Questions, Personal Values, Deep Questions, Who Are You?, Memory Lane, Social Life, Romantic |

**Implementation note:** the Romantic preset description should include a brief content note so players understand what they are opting into — e.g. *"Includes sensitive and intimate questions. Intended for romantic partners."* This should be visible on the preset tile at join time, not hidden in fine print.

---

## 25. Open Questions / Deferred Decisions

- **Cross-game recency factor:** Stub as `recency_factor = 1.0` for v1.
- **Achievements system:** Player identity supports cross-game stats; spec separately.
- **Multi-category question weighting:** v1 uses max weight across a question's categories; revisit if selection feels skewed.
- **Broadcast scheduling:** Not in v1; add if needed.
- **Source DB migration script:** Claude Code should inspect both schemas and generate a one-time import script.

---

*End of specification — Version 1.9*
