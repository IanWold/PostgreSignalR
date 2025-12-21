-- To set up pg_cron, see:
-- https://github.com/citusdata/pg_cron?tab=readme-ov-file#installing-pg_cron
CREATE EXTENSION pg_cron;

CREATE TABLE custom_payloads(
    -- id does not strictly need to be the primary key,
    -- but the strategy requires that it exists, it's a long, and uniquely identifies the table
    id BIGSERIAL PRIMARY KEY,

    -- payload must be BYTEA
    payload BYTEA NOT NULL,

    -- created_at only required if using the built-in automatic cleanup.
    -- This example is not (since it's implementing custom cleanup),
    -- however the custom cleanup is using this column (though it can be renamed)
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ON custom_payloads (created_at);

-- Custom cleanup: run every hour, delete payloads older than 5 minutes
SELECT cron.schedule(
    'backplane_cleanup',
    '0 * * * *',
    $$DELETE FROM custom_payloads WHERE created_at < now() - interval '5 minutes';$$
);
