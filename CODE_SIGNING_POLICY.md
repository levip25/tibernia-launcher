# Code signing policy

## Scope

The policy covers the Windows installer `Tibernia-Setup.exe` produced from this repository. No game client, server, map or data archive is signed as part of this project.

## Trusted source and build

- Release source must come from this public GitHub repository.
- Release builds run on GitHub-hosted Windows runners from the workflow committed to the repository.
- Pull requests build and test the launcher but cannot request release signing.
- Release signing is restricted to tagged commits reachable from the protected `main` branch.
- The unsigned artifact is uploaded by the same workflow that submits it to SignPath, allowing origin verification.

## Approval and credentials

- SignPath Foundation is the intended certificate provider.
- The SignPath API token is stored only as an encrypted GitHub Actions secret.
- Organization and project identifiers are stored as GitHub Actions variables.
- Repository contributors never receive or export the private signing key.
- A maintainer reviews the source changes and generated checksum before publishing a release.

## Release verification

For every public release:

1. GitHub Actions builds and tests the exact tagged commit.
2. The unsigned installer is submitted to SignPath through its GitHub Actions connector.
3. The signed installer is returned to the workflow.
4. SHA-256 is calculated from the final signed installer.
5. The installer and checksum are attached to the matching GitHub release.

If signing is unavailable, an unsigned build may be published only when it is explicitly labelled unsigned and accompanied by its SHA-256 checksum.

