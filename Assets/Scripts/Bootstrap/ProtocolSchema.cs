namespace Rpg.Networking.Protocol
{
    public static class ProtocolSchema
    {
        // Sole version source. Increment for incompatible DTO or semantic changes.
        // Periodic monster attacks use a sparse list; player positions omit confirmed flask state and derived parry flags.
        public const int SchemaVersion = 62;
    }

}
