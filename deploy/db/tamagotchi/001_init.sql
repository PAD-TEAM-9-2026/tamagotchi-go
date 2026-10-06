CREATE TABLE IF NOT EXISTS tamagotchis (
  id                uuid PRIMARY KEY,
  name              text NOT NULL,
  origin_package_id uuid NOT NULL,
  config_version    integer NOT NULL,
  origin_owner_id   uuid NOT NULL,
  combat_type       text NOT NULL,
  level             integer NOT NULL,
  xp                bigint NOT NULL,
  sprite_ref        text NOT NULL,
  package_stats     jsonb NOT NULL,
  acquired_at       timestamptz NOT NULL,
  version           integer NOT NULL
);

CREATE INDEX IF NOT EXISTS tamagotchis_origin_package_idx ON tamagotchis (origin_package_id);
CREATE INDEX IF NOT EXISTS tamagotchis_combat_type_idx ON tamagotchis (combat_type);

CREATE TABLE IF NOT EXISTS tamagotchi_holders (
  tamagotchi_id uuid NOT NULL REFERENCES tamagotchis (id) ON DELETE CASCADE,
  user_id       uuid NOT NULL,
  position      integer NOT NULL,
  granted_at    timestamptz NOT NULL,
  PRIMARY KEY (tamagotchi_id, user_id),
  UNIQUE (tamagotchi_id, position)
);

CREATE INDEX IF NOT EXISTS tamagotchi_holders_user_idx ON tamagotchi_holders (user_id);

CREATE TABLE IF NOT EXISTS primary_selections (
  user_id       uuid PRIMARY KEY,
  tamagotchi_id uuid REFERENCES tamagotchis (id) ON DELETE SET NULL,
  version       integer NOT NULL
);

CREATE TABLE IF NOT EXISTS care_actions (
  care_action_id uuid PRIMARY KEY,
  tamagotchi_id  uuid NOT NULL REFERENCES tamagotchis (id) ON DELETE CASCADE,
  user_id        uuid NOT NULL,
  action         text NOT NULL,
  applied_at     timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS care_actions_cooldown_idx
  ON care_actions (tamagotchi_id, action, applied_at DESC);

CREATE TABLE IF NOT EXISTS engagements (
  engagement_id uuid PRIMARY KEY,
  source        text NOT NULL,
  reference_id  uuid NOT NULL UNIQUE,
  status        text NOT NULL,
  created_at    timestamptz NOT NULL,
  expires_at    timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS engagement_pets (
  engagement_id uuid NOT NULL REFERENCES engagements (engagement_id) ON DELETE CASCADE,
  tamagotchi_id uuid NOT NULL REFERENCES tamagotchis (id) ON DELETE CASCADE,
  user_id       uuid NOT NULL,
  slot          text NOT NULL,
  lineup_index  integer NOT NULL,
  active        boolean NOT NULL DEFAULT true,
  PRIMARY KEY (engagement_id, tamagotchi_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS engagement_pets_single_active_idx
  ON engagement_pets (tamagotchi_id) WHERE active;

CREATE TABLE IF NOT EXISTS idempotency_keys (
  endpoint     text NOT NULL,
  key          text NOT NULL,
  request_hash text NOT NULL,
  status_code  integer NOT NULL,
  body         jsonb NOT NULL,
  created_at   timestamptz NOT NULL,
  PRIMARY KEY (endpoint, key)
);
