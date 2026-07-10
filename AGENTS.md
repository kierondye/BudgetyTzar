# Agent Guidance

Use the repository's human-facing documentation as the source of truth:

- [README.md](README.md) gives the project overview and links.
- [SPECIFICATION.md](SPECIFICATION.md) defines product and system requirements.
- [docs/architecture.md](docs/architecture.md) explains where code belongs and why.
- [CONTRIBUTING.md](CONTRIBUTING.md) defines how to write code and tests in this
  repository.

Read the relevant sections before making a change. Do not duplicate their guidance
here. If implementation and specification disagree, treat the specification as
authoritative unless the task explicitly changes it.

When reviewing or implementing a change, ask:

- Is the behaviour tested through the public boundary where practical?
- Is any fake replacing the behaviour under test?
- Are types immutable and valid by construction where practical?
- Are expected failures explicit?
- Does each rule live in the boundary that owns the language?
- Are handlers shielded from persistence technology?
