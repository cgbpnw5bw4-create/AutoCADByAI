# DrawingReview Module

Reviews drawings, PDFs, dimensions, required views and title block completeness.

Boundaries:

- Agents coordinate drawing review.
- Validators perform deterministic checks where possible.
- Reviewers produce `ReviewReport`.
- Gatekeeper consumes the report and decides pass, reject, fail or human approval.
