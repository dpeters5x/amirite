-- Migration 0001: Initial schema

CREATE TABLE IF NOT EXISTS players (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    email        TEXT    UNIQUE NOT NULL,
    nickname     TEXT,
    fcm_token    TEXT,
    created_at   DATETIME NOT NULL DEFAULT (datetime('now')),
    last_seen_at DATETIME
);

CREATE TABLE IF NOT EXISTS sessions (
    id                  TEXT    PRIMARY KEY,
    organizer_id        INTEGER REFERENCES players(id),
    organizer_email     TEXT    NOT NULL,
    player1_id          INTEGER REFERENCES players(id),
    player2_id          INTEGER REFERENCES players(id),
    status              TEXT    NOT NULL DEFAULT 'pending_join',
    questions_per_round INTEGER NOT NULL DEFAULT 2,
    decoy_count         INTEGER NOT NULL DEFAULT 3,
    join_expires_at     DATETIME NOT NULL,
    created_at          DATETIME NOT NULL DEFAULT (datetime('now')),
    ended_at            DATETIME,
    archived_at         DATETIME,
    archived_by         TEXT
);

CREATE TABLE IF NOT EXISTS session_players (
    session_id  TEXT    NOT NULL REFERENCES sessions(id),
    player_id   INTEGER NOT NULL REFERENCES players(id),
    token       TEXT    UNIQUE NOT NULL,
    nickname    TEXT,
    joined_at   DATETIME,
    final_round INTEGER,
    PRIMARY KEY (session_id, player_id)
);

CREATE TABLE IF NOT EXISTS categories (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    description TEXT,
    active      BOOLEAN NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS presets (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    description TEXT,
    sort_order  INTEGER NOT NULL DEFAULT 0,
    active      BOOLEAN NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS preset_categories (
    preset_id   INTEGER NOT NULL REFERENCES presets(id),
    category_id INTEGER NOT NULL REFERENCES categories(id),
    PRIMARY KEY (preset_id, category_id)
);

CREATE TABLE IF NOT EXISTS session_categories (
    session_id   TEXT    NOT NULL REFERENCES sessions(id),
    category_id  INTEGER NOT NULL REFERENCES categories(id),
    player1_vote BOOLEAN NOT NULL DEFAULT 0,
    player2_vote BOOLEAN NOT NULL DEFAULT 0,
    weight       REAL    NOT NULL DEFAULT 0.0,
    PRIMARY KEY (session_id, category_id)
);

CREATE TABLE IF NOT EXISTS questions (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    text       TEXT    NOT NULL,
    active     BOOLEAN NOT NULL DEFAULT 1,
    created_at DATETIME NOT NULL DEFAULT (datetime('now')),
    updated_at DATETIME NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS question_categories (
    question_id INTEGER NOT NULL REFERENCES questions(id),
    category_id INTEGER NOT NULL REFERENCES categories(id),
    PRIMARY KEY (question_id, category_id)
);

CREATE TABLE IF NOT EXISTS rounds (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id   TEXT    NOT NULL REFERENCES sessions(id),
    round_number INTEGER NOT NULL,
    status       TEXT    NOT NULL DEFAULT 'answering',
    started_at   DATETIME NOT NULL DEFAULT (datetime('now')),
    completed_at DATETIME
);

CREATE TABLE IF NOT EXISTS round_questions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    round_id    INTEGER NOT NULL REFERENCES rounds(id),
    question_id INTEGER NOT NULL REFERENCES questions(id),
    sort_order  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS answers (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    round_question_id INTEGER NOT NULL REFERENCES round_questions(id),
    player_id         INTEGER NOT NULL REFERENCES players(id),
    answer_text       TEXT    NOT NULL,
    submitted_at      DATETIME NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS decoys (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    round_question_id INTEGER NOT NULL REFERENCES round_questions(id),
    target_player_id  INTEGER NOT NULL REFERENCES players(id),
    decoy_text        TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS guesses (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    round_question_id  INTEGER NOT NULL REFERENCES round_questions(id),
    guessing_player_id INTEGER NOT NULL REFERENCES players(id),
    chosen_answer_id   INTEGER,
    chosen_decoy_id    INTEGER,
    is_correct         BOOLEAN NOT NULL DEFAULT 0,
    points_awarded     INTEGER NOT NULL DEFAULT 0,
    submitted_at       DATETIME NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS question_feedback (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    question_id       INTEGER NOT NULL REFERENCES questions(id),
    player_id         INTEGER NOT NULL REFERENCES players(id),
    session_id        TEXT    NOT NULL REFERENCES sessions(id),
    round_question_id INTEGER REFERENCES round_questions(id),
    phase             TEXT    NOT NULL,
    flag_inappropriate BOOLEAN NOT NULL DEFAULT 0,
    flag_duplicate    BOOLEAN NOT NULL DEFAULT 0,
    quality_rating    INTEGER,
    flag_poor_decoys  BOOLEAN NOT NULL DEFAULT 0,
    notes             TEXT,
    created_at        DATETIME NOT NULL DEFAULT (datetime('now')),
    reviewed_at       DATETIME,
    reviewed_by       TEXT,
    resolution        TEXT
);

CREATE TABLE IF NOT EXISTS otp_codes (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    email      TEXT    NOT NULL,
    code       TEXT    NOT NULL,
    expires_at DATETIME NOT NULL,
    used       BOOLEAN NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS player_sessions (
    id         TEXT    PRIMARY KEY,
    player_id  INTEGER NOT NULL REFERENCES players(id),
    created_at DATETIME NOT NULL DEFAULT (datetime('now')),
    expires_at DATETIME NOT NULL
);

CREATE TABLE IF NOT EXISTS achievements (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    key         TEXT    UNIQUE NOT NULL,
    name        TEXT    NOT NULL,
    description TEXT    NOT NULL,
    icon        TEXT    NOT NULL,
    active      BOOLEAN NOT NULL DEFAULT 1,
    sort_order  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS player_achievements (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    player_id      INTEGER NOT NULL REFERENCES players(id),
    achievement_id INTEGER NOT NULL REFERENCES achievements(id),
    awarded_at     DATETIME NOT NULL DEFAULT (datetime('now')),
    awarded_by     TEXT    NOT NULL DEFAULT 'system',
    session_id     TEXT    REFERENCES sessions(id)
);

CREATE TABLE IF NOT EXISTS broadcasts (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    subject         TEXT    NOT NULL,
    body            TEXT    NOT NULL,
    channel         TEXT    NOT NULL,
    sent_at         DATETIME NOT NULL DEFAULT (datetime('now')),
    sent_by         TEXT    NOT NULL,
    recipient_count INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS broadcast_recipients (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    broadcast_id  INTEGER NOT NULL REFERENCES broadcasts(id),
    player_id     INTEGER NOT NULL REFERENCES players(id),
    email         TEXT    NOT NULL,
    channel_used  TEXT,
    fcm_success   BOOLEAN,
    email_success BOOLEAN,
    error_message TEXT
);

CREATE TABLE IF NOT EXISTS chat_messages (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id   TEXT    NOT NULL REFERENCES sessions(id),
    sender_id    INTEGER NOT NULL REFERENCES players(id),
    message_text TEXT    NOT NULL,
    sent_at      DATETIME NOT NULL DEFAULT (datetime('now')),
    read_at      DATETIME
);

-- Indexes for common query patterns
CREATE INDEX IF NOT EXISTS idx_session_players_token      ON session_players(token);
CREATE INDEX IF NOT EXISTS idx_session_players_session    ON session_players(session_id);
CREATE INDEX IF NOT EXISTS idx_rounds_session             ON rounds(session_id);
CREATE INDEX IF NOT EXISTS idx_round_questions_round      ON round_questions(round_id);
CREATE INDEX IF NOT EXISTS idx_answers_round_question     ON answers(round_question_id);
CREATE INDEX IF NOT EXISTS idx_answers_player             ON answers(player_id);
CREATE INDEX IF NOT EXISTS idx_decoys_round_question      ON decoys(round_question_id);
CREATE INDEX IF NOT EXISTS idx_guesses_round_question     ON guesses(round_question_id);
CREATE INDEX IF NOT EXISTS idx_otp_codes_email            ON otp_codes(email);
CREATE INDEX IF NOT EXISTS idx_player_sessions_player     ON player_sessions(player_id);
CREATE INDEX IF NOT EXISTS idx_question_feedback_question ON question_feedback(question_id);
CREATE INDEX IF NOT EXISTS idx_chat_messages_session      ON chat_messages(session_id);
CREATE INDEX IF NOT EXISTS idx_player_achievements_player ON player_achievements(player_id);
CREATE INDEX IF NOT EXISTS idx_session_categories_session ON session_categories(session_id);
CREATE INDEX IF NOT EXISTS idx_question_categories_cat    ON question_categories(category_id);
