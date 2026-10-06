CREATE TABLE guilds (
  guild_id    uuid PRIMARY KEY,
  name        text        NOT NULL,
  description text        NOT NULL DEFAULT '',
  leader_id   uuid        NOT NULL,
  version     integer     NOT NULL DEFAULT 1,
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT guilds_name_length CHECK (char_length(name) BETWEEN 3 AND 64),
  CONSTRAINT guilds_description_length CHECK (char_length(description) <= 500)
);

CREATE UNIQUE INDEX guilds_name_lower_key ON guilds (lower(name));

-- A user belongs to at most one guild, and a guild has exactly one leader.
CREATE TABLE guild_members (
  guild_id  uuid        NOT NULL REFERENCES guilds (guild_id) ON DELETE CASCADE,
  user_id   uuid        NOT NULL,
  role      text        NOT NULL CHECK (role IN ('LEADER', 'OFFICER', 'MEMBER')),
  joined_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (guild_id, user_id),
  CONSTRAINT guild_members_user_key UNIQUE (user_id)
);

CREATE UNIQUE INDEX guild_members_one_leader ON guild_members (guild_id) WHERE role = 'LEADER';

CREATE TABLE invitations (
  invitation_id      uuid PRIMARY KEY,
  guild_id           uuid        NOT NULL REFERENCES guilds (guild_id) ON DELETE CASCADE,
  invited_user_id    uuid        NOT NULL,
  invited_by_user_id uuid        NOT NULL,
  status             text        NOT NULL CHECK (status IN ('PENDING', 'ACCEPTED', 'DECLINED', 'REVOKED', 'EXPIRED')),
  expires_at         timestamptz NOT NULL,
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_at         timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX invitations_one_pending ON invitations (guild_id, invited_user_id) WHERE status = 'PENDING';
CREATE INDEX invitations_invited_user ON invitations (invited_user_id, invitation_id DESC);

CREATE TABLE messages (
  message_id        uuid PRIMARY KEY,
  guild_id          uuid        NOT NULL REFERENCES guilds (guild_id) ON DELETE CASCADE,
  author_id         uuid        NOT NULL,
  client_message_id uuid        NOT NULL,
  content           text        NOT NULL CHECK (char_length(content) BETWEEN 1 AND 2000),
  created_at        timestamptz NOT NULL DEFAULT now(),
  UNIQUE (guild_id, author_id, client_message_id)
);

CREATE INDEX messages_guild_recent ON messages (guild_id, message_id DESC);

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
