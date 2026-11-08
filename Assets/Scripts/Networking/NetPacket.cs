using System;
using System.IO;

namespace Rpg.Networking
{
    /// <summary>Length framing only; no JSON parsing or Unity calls on a receive thread.</summary>
    public static class NetPacket
    {
        public const int MaxBodyLength = 1024 * 1024;
        public const int ProtocolIdSize = 2;

        // Explicit little endian: int32 body length (including int16 protocol ID), then payload.
        public static byte[] Build(short protocolId, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaxBodyLength - ProtocolIdSize)
                throw new InvalidDataException("Packet body exceeds 1 MiB.");
            int length = ProtocolIdSize + payload.Length;
            var packet = new byte[4 + length];
            for (int i = 0; i < 4; i++) packet[i] = (byte)(length >> (8 * i));
            packet[4] = (byte)protocolId;
            packet[5] = (byte)(protocolId >> 8);
            Buffer.BlockCopy(payload, 0, packet, 6, payload.Length);
            return packet;
        }

        /// <summary>
        /// False is clean EOF before a new header. Truncation throws EndOfStreamException;
        /// invalid lengths throw InvalidDataException. Discard the connection on either error.
        /// Other I/O errors propagate. The caller owns the stream.
        /// </summary>
        public static bool TryRead(Stream stream, out short protocolId, out byte[] payload)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            protocolId = 0;
            payload = null;
            var header = new byte[4];
            if (!ReadExact(stream, header, true)) return false;
            int length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            // Check before allocating; body length excludes the four-byte prefix.
            if (length < ProtocolIdSize || length > MaxBodyLength)
                throw new InvalidDataException("Invalid packet body length: " + length);
            var body = new byte[length];
            ReadExact(stream, body, false);
            short decodedId = unchecked((short)(body[0] | (body[1] << 8)));
            var decodedPayload = new byte[length - ProtocolIdSize];
            Buffer.BlockCopy(body, ProtocolIdSize, decodedPayload, 0, decodedPayload.Length);
            protocolId = decodedId;
            payload = decodedPayload;
            return true;
        }

        private static bool ReadExact(Stream stream, byte[] buffer, bool allowCleanEof)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    if (allowCleanEof && offset == 0) return false;
                    throw new EndOfStreamException("Connection ended inside a packet.");
                }
                offset += read;
            }
            return true;
        }
    }
}
