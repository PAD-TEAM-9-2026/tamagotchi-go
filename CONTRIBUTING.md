# Contributing

## Work

1. Link an issue with the expected result and acceptance criteria.
2. Branch from `develop` using `feat/short-description`, `fix/short-description`, `docs/short-description`, or `chore/short-description`.
3. Keep commits focused and use `type(scope): description` for commit and PR titles.
4. Update the README and any affected definitions in `docs/field-types.md` when the shared communication contract changes. Request review from affected service owners.
5. Open a PR to `develop` using the PR template. One approval, resolved conversations, and a passing `ci` check are required.

Do not commit credentials, `.env` files, dependencies, or generated output. Update a service submodule pointer only to a commit available in its service repository.

## Checks

Run the checks relevant to the change and record their results in the PR:

```bash
python3 tools/check_contracts.py
```

For deployment changes, also validate Compose with a local `deploy/.env` and verify the affected services run. For Postman changes, run the affected requests against the deployed services. The contract checker validates documentation links and payload names; it does not test service behavior. Service repositories target at least 80% line coverage for independently running domain logic and cover concurrency and failure paths.

## Merge and release

Squash work branches into `develop`. Merge a tested release into `main` with a merge commit. Use a release branch from the agreed develop cut when later work must be excluded. Do not push directly to either shared branch.

Versions use `X.Y`: X identifies the integration milestone and Y the revision within it. Start a milestone at revision zero and increment for released fixes or small changes. This numbering does not imply API compatibility; document breaking changes and the tested service versions separately.

The shared repository receives an annotated `vX.Y` tag and a GitHub Release on the tested main merge commit. Service releases use the tested merge into main, without a separate Git tag or GitHub Release. Service image versions remain explicit and immutable.

Never overwrite existing tags or published versions. Keep shared Compose pinned to the tested image set. Merge release-only changes back into `develop` through a PR when needed.
