CREATE TABLE packages (
  package_id           uuid PRIMARY KEY,
  name                 text        NOT NULL,
  description          text        NOT NULL DEFAULT '',
  version              text        NOT NULL,
  status               text        NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive')),
  developer_user_ids   uuid[]      NOT NULL,
  moderator_user_ids   uuid[]      NOT NULL,
  -- Optimistic-lock counter shared by PATCH /packages/{id} (If-Match) and
  -- PUT /packages/{id}/stats (expected_package_revision in the body).
  revision             integer     NOT NULL DEFAULT 1,
  -- Points at the current row in package_configs; null until the first PUT /stats.
  config_version        integer,
  created_at           timestamptz NOT NULL DEFAULT now(),
  updated_at           timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT packages_name_length CHECK (char_length(name) BETWEEN 3 AND 64),
  CONSTRAINT packages_description_length CHECK (char_length(description) <= 1000),
  CONSTRAINT packages_version_length CHECK (char_length(version) BETWEEN 1 AND 32),
  CONSTRAINT packages_developers_count CHECK (array_length(developer_user_ids, 1) BETWEEN 1 AND 20),
  CONSTRAINT packages_moderators_count CHECK (array_length(moderator_user_ids, 1) BETWEEN 1 AND 20)
);

-- Every PUT /stats writes a new immutable row here; packages.config_version points at the latest.
CREATE TABLE package_configs (
  package_id     uuid        NOT NULL REFERENCES packages (package_id) ON DELETE CASCADE,
  config_version integer     NOT NULL,
  definition     jsonb       NOT NULL,
  created_at     timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (package_id, config_version)
);

CREATE TABLE bosses (
  boss_id        uuid PRIMARY KEY,
  -- Also the ETag for GET/PUT: bumped on every PUT and used for If-Match.
  config_version integer     NOT NULL DEFAULT 1,
  created_at     timestamptz NOT NULL DEFAULT now(),
  updated_at     timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE boss_configs (
  boss_id        uuid        NOT NULL REFERENCES bosses (boss_id) ON DELETE CASCADE,
  config_version integer     NOT NULL,
  definition     jsonb       NOT NULL,
  created_at     timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (boss_id, config_version)
);

CREATE TABLE raid_occurrences (
  occurrence_id    uuid PRIMARY KEY,
  boss_id          uuid        NOT NULL REFERENCES bosses (boss_id),
  boss_version     integer     NOT NULL,
  available_from   timestamptz NOT NULL,
  available_until  timestamptz NOT NULL,
  status           text        NOT NULL CHECK (status IN ('scheduled', 'active', 'inactive', 'cancelled')),
  version          integer     NOT NULL DEFAULT 1,
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT raid_occurrences_window CHECK (available_until > available_from)
);

CREATE INDEX raid_occurrences_recent ON raid_occurrences (occurrence_id DESC);
CREATE INDEX raid_occurrences_by_boss ON raid_occurrences (boss_id);

-- Local copy of "which packages does this user belong to", kept current from
-- user.package_joined.v1 events, so eligibility-check and .../users never call User Management live.
CREATE TABLE membership_projection (
  user_id             uuid PRIMARY KEY,
  package_ids         uuid[]      NOT NULL,
  membership_version  integer     NOT NULL,
  updated_at          timestamptz NOT NULL DEFAULT now()
);

-- GIN index so "which users belong to package X" (GET /packages/{id}/users) is not a full scan.
CREATE INDEX membership_projection_packages ON membership_projection USING GIN (package_ids);

-- Response cache for the Idempotency-Key header.
CREATE TABLE idempotency_keys (
  scope           text        NOT NULL,
  key             text        NOT NULL,
  request_hash    text        NOT NULL,
  response_status integer,
  response_body   jsonb,
  created_at      timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (scope, key)
);

-- Transactional outbox: events are written in the same transaction as the state change.
CREATE TABLE outbox (
  event_id     uuid PRIMARY KEY,
  event_type   text        NOT NULL,
  payload      jsonb       NOT NULL,
  created_at   timestamptz NOT NULL DEFAULT now(),
  published_at timestamptz
);

CREATE INDEX outbox_unpublished ON outbox (created_at) WHERE published_at IS NULL;
