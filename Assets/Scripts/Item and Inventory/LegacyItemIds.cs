using System;
using System.Collections.Generic;

// Compatibility for resources built before itemId was serialized into the 16 authored items.
// New items must carry their own serialized GUID and do not belong in this table.
internal static class LegacyItemIds
{
    private static readonly Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "Amulet of God", "9d436364b307e7b48b5651e33d4626c3" },
        { "Golden ring", "4983af24db2a3784fb2275cce6745226" },
        { "The book", "639fa5620d2b27e42a7b92940b994bbb" },
        { "Blue wolf Armor", "5fbea46ec8b0e444e983745a0594b210" },
        { "Plate Armor", "e7599f6e79628a24a81a84acba9827a5" },
        { "Rogue's Jacket", "7e9b292a88d44b5438cb21260c79a524" },
        { "Armor Flask", "ce6ca20392bf7d545abf56b98817b810" },
        { "Fire Flask", "9364afbb7720dc444a5d383fb1a00c97" },
        { "Healing Flask", "371a2e9e02b88e348a2d855fcc047c86" },
        { "Dracula's Sword", "0c76cf42010dea54c826702cc8c25f8d" },
        { "Ice And Fire", "7b96e82df073069419a0f8271ecb5690" },
        { "Stolen Sword", "54b700e10cb23c34c9dddb2c19bbd313" },
        { "Thunder Claw", "f6f49ceeea8ebd745b273a0cc81b744e" },
        { "Wooden Sword", "86022e92c97e2874cb150576a1fb4dec" },
        { "Animal Skin", "3a73f9cffd1296d4d84a286a9a394b49" },
        { "Iron", "0dce44a12f3fb5843821562be830ef31" },
    };

    public static bool TryResolve(string assetName, out string id) => byName.TryGetValue(assetName, out id);
}
