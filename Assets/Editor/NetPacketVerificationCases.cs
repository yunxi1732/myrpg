using System;
using System.IO;
using System.Text;
using Rpg.Networking;

// Pure stream cases: also runnable outside Unity without substituting its JSON serializer.
public static class NetPacketVerificationCases
{
    public static void Run()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"text\":\"中文🙂\"}");
        short id = unchecked((short)0x8123);
        byte[] packet = NetPacket.Build(id, json);
        Require(packet[0] == json.Length + 2 && packet[1] == 0 && packet[2] == 0 && packet[3] == 0,
            "Little endian body length includes ID and UTF-8 bytes");
        Require(packet[4] == 0x23 && packet[5] == 0x81, "Little endian signed int16 ID");

        // Each split boundary, including every length-header boundary and UTF-8 byte boundary.
        for (int split = 1; split < packet.Length; split++)
        {
            using (var stream = new FragmentedStream(packet, split, 1))
            {
                Require(NetPacket.TryRead(stream, out short readId, out byte[] payload), "Fragmented read");
                Require(readId == id && Equal(payload, json), "Fragmented payload preserved");
                Require(!NetPacket.TryRead(stream, out readId, out payload) && readId == 0 && payload == null,
                    "Clean EOF leaves no message");
                Require(stream.CanRead, "Reader does not own stream");
            }
        }

        var consecutive = new byte[packet.Length * 3];
        for (int i = 0; i < 3; i++) Buffer.BlockCopy(packet, 0, consecutive, i * packet.Length, packet.Length);
        using (var stream = new MemoryStream(consecutive))
        {
            for (int i = 0; i < 3; i++)
                Require(NetPacket.TryRead(stream, out short readId, out byte[] payload) && readId == id && Equal(payload, json),
                    "Consecutive packets remain separate");
            Require(!NetPacket.TryRead(stream, out _, out _), "Consecutive packets clean EOF");
        }

        for (int size = 1; size < packet.Length; size++)
        {
            using (var stream = new MemoryStream(packet, 0, size))
                Expect<EndOfStreamException>(() => NetPacket.TryRead(stream, out _, out _));
        }
        using (var stream = new MemoryStream())
            Require(!NetPacket.TryRead(stream, out _, out _), "Empty stream is clean EOF");

        foreach (int length in new[] { int.MinValue, -1, 0, 1, NetPacket.MaxBodyLength + 1, int.MaxValue })
        {
            using (var stream = new MemoryStream(Header(length)))
                Expect<InvalidDataException>(() => NetPacket.TryRead(stream, out _, out _));
        }
        // Minimum transport frame is valid; the protocol decoder later rejects empty JSON.
        using (var stream = new MemoryStream(new byte[] { 2, 0, 0, 0, 1, 0 }))
            Require(NetPacket.TryRead(stream, out short readId, out byte[] payload) && readId == 1 && payload.Length == 0,
                "Minimum transport length");

        var maximum = new byte[NetPacket.MaxBodyLength - 2];
        maximum[maximum.Length - 1] = 0x7F;
        byte[] maximumPacket = NetPacket.Build(id, maximum);
        Require(maximumPacket.Length == NetPacket.MaxBodyLength + 4 && maximumPacket[2] == 0x10,
            "Maximum inclusive body size");
        using (var stream = new MemoryStream(maximumPacket))
            Require(NetPacket.TryRead(stream, out _, out byte[] payload) && Equal(payload, maximum), "Maximum receive");
        Expect<InvalidDataException>(() => NetPacket.Build(id, new byte[NetPacket.MaxBodyLength - 1]));
        Expect<ArgumentNullException>(() => NetPacket.Build(id, null));
        Expect<ArgumentNullException>(() => NetPacket.TryRead(null, out _, out _));
        using (var stream = new FailingStream())
            Expect<IOException>(() => NetPacket.TryRead(stream, out _, out _));
    }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[NetPacket] FAIL: " + message);
    }

    public static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static byte[] Header(int length)
    {
        return new[] { (byte)length, (byte)(length >> 8), (byte)(length >> 16), (byte)(length >> 24) };
    }

    private sealed class FragmentedStream : MemoryStream
    {
        private readonly int split;
        private readonly int chunk;
        public FragmentedStream(byte[] bytes, int split, int chunk) : base(bytes)
        {
            this.split = split;
            this.chunk = chunk;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int limit = Position < split ? split - (int)Position : chunk;
            return base.Read(buffer, offset, Math.Min(count, limit));
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) { throw new IOException("Simulated I/O error"); }
    }
}
