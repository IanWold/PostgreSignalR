-- To set up pg_cron, see:
-- https://github.com/citusdata/pg_cron?tab=readme-ov-file#installing-pg_cron
CREATE EXTENSION pg_cron;

-- Because this is used by our own custom strategy, there are no requirements for this table.
-- It is most efficient to store the payload as a BYTEA in postgres,
--     but technically it could be transformed into any representation you want.
CREATE TABLE backplane_notifications(
    id BIGSERIAL PRIMARY KEY,
    payload BYTEA NOT NULL,
    channel TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ON backplane_notifications (created_at);

-- Implement custom cleanup strategy with pg_cron.
-- Run every hour, delete payloads older than 5 minutes.
SELECT cron.schedule(
    'backplane_cleanup',
    '0 * * * *',
    $$DELETE FROM backplane_notifications WHERE created_at < now() - interval '5 minutes';$$
);

-- Create a trigger to call pg_notify on insert to backplane_notifications.
CREATE OR REPLACE FUNCTION notify_backplane()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM pg_notify(n.channel, n.id::text)FROM new AS n;
    RETURN NULL;
END;
$$;

CREATE TRIGGER after_insert_backplane_notifications
AFTER INSERT ON backplane_notifications
REFERENCING NEW TABLE AS new
FOR EACH STATEMENT
EXECUTE FUNCTION notify_backplane();
