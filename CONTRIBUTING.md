# Contributing

## Branching

- Follow the [gitflow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow) branching policy.
- Feature branch names must include the ticket number, in the format `#<ticket>_short_description`, e.g. `#1_short_description`.

## Releases

Use `scripts\release.ps1` (see the "Creating a release" section in `README.md`). It publishes to the Scoop bucket and Homebrew tap from the templates in `packaging\`.
