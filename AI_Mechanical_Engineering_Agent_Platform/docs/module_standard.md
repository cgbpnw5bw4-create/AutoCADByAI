# Module Standard

A Module is a complete capability board. It is not a script folder.

Each module should contain:

- `README.md`: responsibility, boundaries and current maturity
- `module.yaml`: name, version, capabilities and registered components
- `agents/`: role implementations or adapters owned by this module
- `skills/`: structured transformations and helper capabilities
- `workers/`: execution adapters only when the module owns an external execution surface
- `validators/`: deterministic checks
- `reviewers/`: review logic that produces `ReviewReport`
- `schemas/`: module-specific schema extensions
- `tests/`: module-level behavior and contract tests

Module code should communicate through platform contracts and DomainSchemas. Avoid hidden natural-language-only handoffs for core task data.
