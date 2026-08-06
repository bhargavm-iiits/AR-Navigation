-- ---------------------------------------------------------------------------------------------
-- Tirumala AR Navigation — offline SQLite schema (System 12)
--
-- Applied by TirumalaAR.Database.Sqlite.SqliteSchema when the SQLite backend is enabled with the
-- TIRUMALA_SQLITE scripting define. The JSON backend is the default and needs none of this; the
-- schema exists so the SQLite path is a drop-in replacement rather than a rewrite.
--
-- Everything is local to the device. There is no sync, no server and no network access.
-- ---------------------------------------------------------------------------------------------

PRAGMA journal_mode = WAL;      -- survives process kill mid-walk without corrupting the file
PRAGMA synchronous = NORMAL;    -- WAL + NORMAL is durable enough here and far cheaper on flash
PRAGMA foreign_keys = ON;

-- The geodetic origin of the ENU frame. Exactly one row (id = 1) is ever present.
CREATE TABLE IF NOT EXISTS route_origin (
    id          INTEGER PRIMARY KEY CHECK (id = 1),
    latitude    REAL    NOT NULL,
    longitude   REAL    NOT NULL,
    altitude    REAL    NOT NULL DEFAULT 0,
    built_utc   TEXT    NOT NULL
);

-- Ordered route nodes produced by RouteBuilder from the GeoJSON extract.
CREATE TABLE IF NOT EXISTS waypoints (
    id                  INTEGER PRIMARY KEY,      -- index along the route, 0-based
    latitude            REAL    NOT NULL,
    longitude           REAL    NOT NULL,
    elevation           REAL    NOT NULL DEFAULT 0,
    enu_east            REAL    NOT NULL,
    enu_up              REAL    NOT NULL,
    enu_north           REAL    NOT NULL,
    bearing_degrees     REAL    NOT NULL DEFAULT 0,
    next_waypoint_id    INTEGER          DEFAULT NULL REFERENCES waypoints(id) ON DELETE SET NULL,
    distance_to_next    REAL    NOT NULL DEFAULT 0,
    cumulative_distance REAL    NOT NULL DEFAULT 0,
    is_stairs           INTEGER NOT NULL DEFAULT 0 CHECK (is_stairs IN (0, 1)),
    is_bridged          INTEGER NOT NULL DEFAULT 0 CHECK (is_bridged IN (0, 1))
);

-- Nearest-waypoint search runs every GPS tick; these two cover the hot queries.
CREATE INDEX IF NOT EXISTS idx_waypoints_cumulative ON waypoints (cumulative_distance);
CREATE INDEX IF NOT EXISTS idx_waypoints_latlon     ON waypoints (latitude, longitude);

-- Landmarks, mirroring StreamingAssets/Database/landmarks.json.
CREATE TABLE IF NOT EXISTS landmarks (
    id              INTEGER PRIMARY KEY,
    name            TEXT    NOT NULL,
    type            TEXT    NOT NULL DEFAULT 'Unknown',
    latitude        REAL    NOT NULL,
    longitude       REAL    NOT NULL,
    trigger_radius  REAL    NOT NULL DEFAULT 20,
    voice_text      TEXT,
    audio_path      TEXT,
    ar_prefab       TEXT,
    priority        INTEGER NOT NULL DEFAULT 1,
    nearest_waypoint_id INTEGER DEFAULT NULL REFERENCES waypoints(id) ON DELETE SET NULL,
    route_distance  REAL    NOT NULL DEFAULT 0,
    off_route_distance REAL NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_landmarks_route ON landmarks (route_distance);

-- Visit state is separate from the landmark definition so re-importing the JSON never
-- wipes a pilgrim's progress.
CREATE TABLE IF NOT EXISTS landmark_visits (
    landmark_id INTEGER PRIMARY KEY REFERENCES landmarks(id) ON DELETE CASCADE,
    visited_utc TEXT    NOT NULL,
    session_id  TEXT    REFERENCES navigation_sessions(session_id) ON DELETE SET NULL
);

-- Key/value user settings (voice on/off, dark mode, arrow spacing, ...).
CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- One row per walk, completed or abandoned.
CREATE TABLE IF NOT EXISTS navigation_sessions (
    session_id        TEXT    PRIMARY KEY,
    started_utc       TEXT    NOT NULL,
    ended_utc         TEXT,
    distance_covered  REAL    NOT NULL DEFAULT 0,
    duration_seconds  REAL    NOT NULL DEFAULT 0,
    landmarks_visited INTEGER NOT NULL DEFAULT 0,
    completed         INTEGER NOT NULL DEFAULT 0 CHECK (completed IN (0, 1))
);

CREATE INDEX IF NOT EXISTS idx_sessions_started ON navigation_sessions (started_utc DESC);

-- Breadcrumb trail: the fused pose, sampled once per second while navigating. This is what
-- makes the localisation accuracy claims reproducible for the research write-up.
CREATE TABLE IF NOT EXISTS navigation_trace (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id         TEXT    NOT NULL REFERENCES navigation_sessions(session_id) ON DELETE CASCADE,
    recorded_utc       TEXT    NOT NULL,
    latitude           REAL    NOT NULL,
    longitude          REAL    NOT NULL,
    heading_degrees    REAL    NOT NULL DEFAULT 0,
    speed_mps          REAL    NOT NULL DEFAULT 0,
    gps_accuracy       REAL    NOT NULL DEFAULT 0,
    nearest_waypoint_id INTEGER,
    correction_source  TEXT    NOT NULL DEFAULT 'None'
);

CREATE INDEX IF NOT EXISTS idx_trace_session ON navigation_trace (session_id, recorded_utc);

-- Convenience view for the progress panel and the post-walk summary.
CREATE VIEW IF NOT EXISTS v_session_summary AS
SELECT  s.session_id,
        s.started_utc,
        s.ended_utc,
        s.distance_covered,
        s.duration_seconds,
        s.completed,
        COUNT(DISTINCT v.landmark_id) AS landmarks_visited,
        CASE WHEN s.duration_seconds > 0
             THEN s.distance_covered / s.duration_seconds
             ELSE 0 END AS average_speed_mps
FROM        navigation_sessions s
LEFT JOIN   landmark_visits    v ON v.session_id = s.session_id
GROUP BY    s.session_id;
