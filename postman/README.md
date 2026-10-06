# Postman collections

Import all eight service collections and `tamagotchi-go.postman_environment.json`.
Select the **Tamagotchi Go Local** environment. Base URLs use host ports 3001
through 3008.

Map and Monster Raid requests create their own fixtures. Run the other
collections in order when a request depends on earlier state. Refusal requests
assert their expected error status.

For Package Registry admin requests, set `REGISTRY_ADMIN_USER_IDS` in the
untracked deployment `.env` to the collection's `admin_id`, then recreate that
service. Seed Notification on an empty database before running its seeded
history requests. See the main README for seed commands.
