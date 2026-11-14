# Security policy

HostGuard exists to refuse hostile input, so a way to get bad input past it is a security bug.

## Reporting

Please report vulnerabilities privately through GitHub's **Report a vulnerability** button (Security tab → Advisories),
not in a public issue. Include the smallest input that shows the problem and what you expected to happen.

You should get a reply within 7 days. Fixes are released as a new version with a note in the changelog.

## Supported versions

Only the latest release gets fixes while the project is at 0.x.

## Scope

See the threat model and non-goals in the [README](README.md#threat-model). Reports about things listed as non-goals
(for example, that `ContentHash` is not cryptographic) are documented limits, not vulnerabilities.
