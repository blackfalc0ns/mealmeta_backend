namespace MealMate.Domain.Enums;

/// <summary>
/// Approved mobile designs for the classification-selection screen. The numeric value is the
/// stable contract with the Flutter widget factory and the saved settings: append new designs
/// at the end only — never renumber, reuse, or retire an existing value.
/// </summary>
public enum TierThemeDesign
{
    Classic = 1,
    Aurora = 2,
}
