using UnityEngine;

// Compatibility component so the existing LaptopUI prefab keeps its script reference.
// New code should depend on LaptopScreenUI, which this component now inherits.
[DisallowMultipleComponent]
public sealed class LaptopHackUI : LaptopScreenUI
{
}
