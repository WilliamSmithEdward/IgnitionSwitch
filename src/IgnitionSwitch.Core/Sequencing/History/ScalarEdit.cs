namespace IgnitionSwitch.Core.Sequencing.History;

internal readonly record struct ScalarEdit(
    HistoryProperty Property,
    int Track,
    double Before,
    double After
);
