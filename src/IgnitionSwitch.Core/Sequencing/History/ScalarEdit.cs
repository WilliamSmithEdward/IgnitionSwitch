using IgnitionSwitch.Core.Domain;

namespace IgnitionSwitch.Core.Sequencing.History;

internal readonly record struct ScalarEdit(ParameterAddress Address, double Before, double After);
