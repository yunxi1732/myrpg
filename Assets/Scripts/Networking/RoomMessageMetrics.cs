using System;
using System.Collections.Generic;
using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    // Optional main-thread measurement at the actual TCP send and validated receive boundaries.
    public static class RoomMessageMetrics
    {
        private sealed class Sample
        {
            public double WindowStart, LastTime, IntervalTotal;
            public int Count, Intervals, MinBytes = int.MaxValue, MaxBytes, MinEntities = int.MaxValue, MaxEntities;
            public long TotalBytes, TotalEntities;
        }

        private static readonly Dictionary<string, Sample> samples = new Dictionary<string, Sample>();
        private static bool enabled;
        public static bool Enabled
        {
            get => enabled;
            set { if (enabled == value) return; enabled = value; samples.Clear(); }
        }

        public static void Record(long connectionSessionId, bool sent, short protocolId, int serializedBytes, object dto)
        {
            if (!enabled) return;
            RoomMessageKind kind = ProtocolCodec.MessageKind(protocolId);
            string key = connectionSessionId + (sent ? ":send:" : ":receive:") + protocolId;
            double now = MonotonicClock.Now;
            if (!samples.TryGetValue(key, out Sample sample))
            {
                sample = new Sample { WindowStart = now };
                samples.Add(key, sample);
            }
            int entities = EntityCount(dto);
            if (sample.Count > 0) { sample.IntervalTotal += now - sample.LastTime; sample.Intervals++; }
            sample.LastTime = now;
            sample.Count++;
            sample.TotalBytes += serializedBytes;
            sample.MinBytes = Math.Min(sample.MinBytes, serializedBytes);
            sample.MaxBytes = Math.Max(sample.MaxBytes, serializedBytes);
            sample.TotalEntities += entities;
            sample.MinEntities = Math.Min(sample.MinEntities, entities);
            sample.MaxEntities = Math.Max(sample.MaxEntities, entities);
            if (now - sample.WindowStart < 5) return;
            Debug.Log($"[RoomMessages] conn={connectionSessionId} {(sent ? "send" : "receive")} {(ProtocolId)protocolId} kind={kind} count={sample.Count} jsonBytes(avg/min/max)={sample.TotalBytes / sample.Count}/{sample.MinBytes}/{sample.MaxBytes} intervalMs(avg)={(sample.Intervals > 0 ? sample.IntervalTotal * 1000 / sample.Intervals : 0):F1} entities(avg/min/max)={(double)sample.TotalEntities / sample.Count:F1}/{sample.MinEntities}/{sample.MaxEntities}");
            samples.Remove(key);
        }

        private static int EntityCount(object dto)
        {
            if (dto is PlayerPositionsDto players) return players.players.Length;
            if (dto is PlayerInitializationDto initialPlayers) return initialPlayers.players.Length;
            if (dto is MonsterStatesDto monsters) return monsters.monsters.Length;
            if (dto is MonsterInitializationDto initialMonsters) return initialMonsters.monsters.Length;
            if (dto is SwordStatesDto swords) return swords.swords.Length;
            if (dto is CrystalStatesDto crystals) return crystals.crystals.Length;
            if (dto is CloneStatesDto clones) return clones.clones.Length;
            if (dto is BlackholeStatesDto blackholes) return blackholes.blackholes.Length;
            if (dto is ElementStatesDto elements) return elements.effects.Length;
            return 0;
        }
    }
}
