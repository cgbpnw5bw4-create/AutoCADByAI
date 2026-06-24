# DrawingGeneration Module

Plans engineering drawing generation from model artifacts and `DrawingSpec`.

Boundaries:

- Agents decide drawing intent and sheet requirements.
- Skills create `DrawingSpec`.
- Workers will later create drawings through CAD-specific execution layers.
- Review is delegated to DrawingReview.
