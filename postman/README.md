# Postman collections

Import all eight service collections and `tamagotchi-go.postman_environment.json`.
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
