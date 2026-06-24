namespace QualityGate;

public sealed record BlindReviewPolicy(
    bool HideAuthor,
    bool RequireSecondReviewer,
    string Notes);
