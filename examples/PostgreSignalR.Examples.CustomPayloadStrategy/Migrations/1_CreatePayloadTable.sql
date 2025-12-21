-- To set up pg_cron, see:
-- https://github.com/citusdata/pg_cron?tab=readme-ov-file#installing-pg_cron
CREATE EXTENSION pg_cron;

CREATE TABLE backplane_notifications(
    id BIGSERIAL PRIMARY KEY,
    payload BYTEA NOT NULL,
    channel TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ON backplane_notifications (created_at);

-- Run every hour, delete payloads older than 5 minutes
SELECT cron.schedule(
    'backplane_cleanup',
    '0 * * * *',
    $$DELETE FROM backplane_notifications WHERE created_at < now() - interval '5 minutes';$$
);

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
