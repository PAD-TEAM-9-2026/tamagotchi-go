CREATE TABLE IF NOT EXISTS devices (
  id            uuid PRIMARY KEY,
  user_id       uuid NOT NULL,
  fcm_token     text NOT NULL,
  platform      text NOT NULL,
  package_id    uuid NOT NULL,
  locale        text NOT NULL,
  registered_at timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS devices_registration_idx
  ON devices (user_id, package_id, platform);

CREATE INDEX IF NOT EXISTS devices_user_page_idx ON devices (user_id, id DESC);

CREATE TABLE IF NOT EXISTS notifications (
  id              uuid PRIMARY KEY,
  user_id         uuid NOT NULL,
  type            text NOT NULL,
  event_id        uuid NOT NULL,
  params          jsonb NOT NULL,
  created_at      timestamptz NOT NULL,
  read_at         timestamptz,
  delivery_status text NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS notifications_event_recipient_idx
  ON notifications (event_id, user_id);

CREATE INDEX IF NOT EXISTS notifications_user_page_idx ON notifications (user_id, id DESC);

CREATE INDEX IF NOT EXISTS notifications_unread_idx
  ON notifications (user_id, created_at) WHERE read_at IS NULL;

CREATE TABLE IF NOT EXISTS preferences (
  user_id          uuid PRIMARY KEY,
  muted_categories text[] NOT NULL,
  version          integer NOT NULL
);

CREATE TABLE IF NOT EXISTS idempotency_keys (
  endpoint     text NOT NULL,
  key          text NOT NULL,
  request_hash text NOT NULL,
  status_code  integer NOT NULL,
  body         jsonb NOT NULL,
  created_at   timestamptz NOT NULL,
  PRIMARY KEY (endpoint, key)
);
