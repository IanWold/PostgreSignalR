-- To set up pg_cron, see:
-- https://github.com/citusdata/pg_cron?tab=readme-ov-file#installing-pg_cron
CREATE EXTENSION pg_cron;

CREATE TABLE custom_payloads(
    id BIGSERIAL PRIMARY KEY,
    payload BYTEA NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ON custom_payloads (created_at);

-- Run every hour, delete payloads older than 5 minutes
SELECT cron.schedule(
    'backplane_cleanup',
    '0 * * * *',
    $$DELETE FROM custom_payloads WHERE created_at < now() - interval '5 minutes';$$
);
