namespace LunaPanel.Core.Layouts;

/// <summary>Result of <see cref="CellSizeEstimator.Estimate"/>: the resulting per-cell size and its comfort verdict.</summary>
public readonly record struct CellSizeEstimate(double CellWidth, double CellHeight, ComfortVerdict Verdict);
