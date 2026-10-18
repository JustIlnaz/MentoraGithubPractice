CREATE TABLE IF NOT EXISTS meeting_feedback (
    id_feedback integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_meeting  integer NOT NULL REFERENCES meetings (id_meeting) ON DELETE RESTRICT,
    id_student  integer NOT NULL REFERENCES users (id_user) ON DELETE RESTRICT,
    id_coach    integer NOT NULL REFERENCES users (id_user) ON DELETE RESTRICT,
    rating      integer NOT NULL CHECK (rating BETWEEN 1 AND 5),
    comment     varchar(500),
    created_at  timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT meeting_feedback_id_meeting_key UNIQUE (id_meeting)
);

CREATE INDEX IF NOT EXISTS ix_meeting_feedback_coach
    ON meeting_feedback (id_coach);