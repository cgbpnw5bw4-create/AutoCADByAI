# ErrorDiagnosis Module

Analyzes failures, identifies likely causes and proposes repair paths.

Boundaries:

- Agents classify failure source and suggest next actions.
- Skills normalize logs and error reports.
- Workers may later run diagnostic tools.
- Output should be structured as `ErrorReport` and remediation notes.
