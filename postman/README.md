# Postman collections

Import the eight service test collections, optional demo collection and `tamagotchi-go.postman_environment.json`.
Select the **Tamagotchi Go Local** environment. Application base URLs use
Gateway on port 3000 with service prefixes. Map/Raid probes use their separate
direct URLs on ports 3006/3007.

Map uses two real dedicated test accounts: set `map_user_id`, `map_credential`,
`map_neighbor_id` and `map_neighbor_credential` to matching user IDs and bearer
tokens. Location setup calls authenticate as each owner and carry command keys.
The unknown-location request deletes the viewer's location first. These checks
change only those accounts' locations. Refresh expired tokens before running.

Run Monster Raid's domain requests in order. Set `raid_leader_id`,
`raid_leader_credential`, `raid_guild_id` and `raid_occurrence_id` from real
dependencies. Use a fresh occurrence not previously used by this guild, active
throughout the run, and an available primary creature. Choose a boss that
survives the first attack so cancellation can be tested. Creation records the
Raid ID and initial HP; attack checks credited HP loss rather than mock damage.
The requests no longer generate nonexistent Guilds or Registry occurrences.

Both collections use bearer authentication through Gateway and required
Idempotency-Key headers. They test identical command replay and missing keys;
Raid creation/attack replay uses the original recorded key and response. Forged
plain headers and direct REST impersonation are refused. Domain prerequisite
scripts use asynchronous authenticated setup;
probes require no bearer token. Refusal requests assert expected status/codes.
Run other collections in order when they depend on earlier state. Their owner
must reconcile remaining implementation differences before full-stack acceptance.

For Package Registry admin requests, use a real User Management token carrying
`admin`. On a local stack, its supported command is `docker compose exec
user-management dotnet UserManagement.Api.dll grant-role <test-email> admin`;
this requires explicit approval for the role grant. Log in again afterwards.
Registry verifies assertion roles; `REGISTRY_ADMIN_USER_IDS` does not grant access
in the current image. Occurrence requests use a fresh two-hour availability window.

Existing databases need the owning service's migrations before domain checks.
Health/readiness alone does not prove schema compatibility. Preserve existing
volumes; do not rerun first-start SQL or unrelated standalone seed commands.
Use dedicated registered users and active Registry packages for shared fixtures.
Keep bearer credentials in an untracked local environment; exported Newman
reports can contain tokens and must also stay private.

Guild and Registry collections require real fixture IDs matching their supplied
credentials. Set Guild leader, officer-to-be, decliner, revokee, kicked-user and
outsider IDs; set Registry developer, moderator and outsider IDs. Probes use
guild_probe_url and registry_probe_url directly. Registry configuration reads use
a Monster Raid token with registry:read-config; occurrence reads also require
registry:read-occurrences. Do not add Battle grants for these collection calls.

Battle requires real `challenger_id`, `opponent_id` and `outsider_id`, with matching
`challenger_credential`, `opponent_credential` and `outsider_credential`. Supply
`challenger_primary_id`, `challenger_secondary_id`, `opponent_primary_id` and
`opponent_secondary_id`: distinct held creatures with no pending challenge or
engagement. Run requests in order for challenge replay, acceptance, attack and
forfeit. Direct probes use `battle_probe_url`; domain calls use Gateway.

User Management registers fresh dedicated users and logs them in through Gateway.
Supply real pkg1_id/pkg2_id and um_config_version. Package 1 must define FEED=10,
PLAY=5 and daily_currency_cap=30 at that immutable configuration version. This
is real Registry configuration, not a mock. Supply narrowly scoped service tokens:
um_global_credential (Monster Raid, users:credit-global), um_local_credential
(Tamagotchi, users:credit-local), um_relationship_credential (Guild,
users:check-relationship), um_membership_credential (Guild, users:read-membership),
and um_battle_credential (Battle, users:consume-boost/users:settle-battle).
The collection never requests broader grants. Direct probes use
user_management_probe_url. Fresh-stranger version is asserted against the shared
contract; a failing owner candidate remains a failing check.

## Demo and independent requests

Import demo.postman_collection.json. Set demo_credential to a fresh user token
matching demo_user_id. Reads, probes and refusal requests run independently.
Use the process-probe folder without credentials. Probes go directly to services;
all application calls use Gateway. Each request adds and logs a correlation ID.

The Map log scenario refreshes the dedicated viewer location, then performs a
nearby query with a nested relationship call. Run those two requests in order.
Friend-request delivery uses demo_other_credential as sender and demo_credential
as recipient. Use unrelated registered actors with no pending request; reruns
need fresh actors or an explicitly resolved previous request. Notification history
is polled every 500 ms for at most 15 seconds. Persistence is not device-push proof.

Guild negotiation uses demo_guild_id containing the authenticated user. Open the
returned URL in a WebSocket tab and send {"type":"auth","ticket":"<ticket>"}
within five seconds. Tickets are single-use with a 30-second lifetime.

Refresh local bearer tokens before rehearsal. Use ordinary Postman Send for the
demo; use Runner/Newman for the canonical regression collections. Keep exported
environments and raw reports private. A failing owner contract stays a failing
assertion until the corrected candidate is independently checked.

Notification and Tamagotchi event-dependent reads poll within the request itself,
every 500 ms for at most 15 seconds. They work with Send as well as Runner, and
fail visibly on transport, authentication or missing effects. No mutation is
automatically retried.

For the Tamagotchi scenario, supply `tamagotchi_fixture_package_id` for an active
package with a configured starter and the documented care rules (FEED adds
20 hunger and 5 XP). Do not select a package merely because it is active;
Registry edit scenarios may leave active packages without starter definitions.
