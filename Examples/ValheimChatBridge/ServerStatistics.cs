using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace ValheimDiscordChat
{
    internal sealed class ServerStatistics
    {
        internal sealed class Ledger
        {
            public int Schema = 1;
            public DateTime TrackingSinceUtc = DateTime.UtcNow;
            public long Deaths;
            public Dictionary<string, string> Players = new Dictionary<string, string>();
            public Dictionary<string, string> LastDeaths = new Dictionary<string, string>();
        }
        internal Ledger Data { get; private set; } = new Ledger();
        internal readonly Dictionary<ZNetPeer, DateTime> Sessions = new Dictionary<ZNetPeer, DateTime>();
        private string path;
        private bool dirty, readOnly;
        private bool lastSaveSucceeded = true;
        private DateTime nextSave;
        private readonly Action<string> warning;
        internal ServerStatistics(Action<string> warning) { this.warning = warning; }
        internal void Open(string file)
        {
            path = file;
            if (!File.Exists(path)) { dirty = true; return; }
            try
            {
                var data = JsonConvert.DeserializeObject<Ledger>(File.ReadAllText(path));
                if (data == null || data.Schema != 1 || data.Deaths < 0 || data.Players == null || data.LastDeaths == null)
                    throw new FormatException();
                Data = data;
            }
            catch (Exception ex)
            {
                readOnly = true;
                warning("Statistics file could not be loaded (" + ex.GetType().Name + "). It is preserved; totals are temporary until the file is repaired.");
            }
        }
        internal bool IsPersistent => !readOnly && lastSaveSucceeded;
        internal static string Identity(ZNet network, ZNetPeer peer)
        {
            var host = peer.m_socket.GetHostName();
            if (CommandPolicy.IsSteamId(host)) return "Steam_" + host;
            if (host != null && host.StartsWith("Steam_", StringComparison.Ordinal) && CommandPolicy.IsSteamId(host.Substring(6))) return host;
            foreach (var player in network.GetPlayerList())
                if (player.m_characterID == peer.m_characterID && !peer.m_characterID.IsNone() && player.m_userInfo.m_id.IsValid)
                    return player.m_userInfo.m_id.ToString();
            return peer.m_characterID.IsNone() ? null : host;
        }
        internal void Tick(ZNet network)
        {
            var live = new HashSet<ZNetPeer>();
            var world = network.GetWorld();
            if (world != null)
                foreach (var player in world.m_playerHistory)
                    if (player.m_id.IsValid && !Data.Players.ContainsKey(player.m_id.ToString())) Remember(player.m_id.ToString(), player.m_displayName);
            foreach (var peer in network.GetPeers())
            {
                if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
                live.Add(peer);
                if (!Sessions.ContainsKey(peer)) Sessions[peer] = DateTime.UtcNow;
                Remember(Identity(network, peer), peer.m_playerName);
            }
            foreach (var peer in new List<ZNetPeer>(Sessions.Keys)) if (!live.Contains(peer)) Sessions.Remove(peer);
            if (dirty && DateTime.UtcNow >= nextSave) Save();
        }
        private void Remember(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return;
            string previous;
            if (!Data.Players.TryGetValue(id, out previous) || previous != name) { Data.Players[id] = name; dirty = true; }
        }
        internal void Death(ZNet network, ZNetPeer peer)
        {
            var id = Identity(network, peer);
            if (string.IsNullOrEmpty(id)) return;
            string character = peer.m_characterID.ToString(), previous;
            if (Data.LastDeaths.TryGetValue(id, out previous) && previous == character) return;
            Data.LastDeaths[id] = character; Data.Deaths++; dirty = true;
            Save();
        }
        internal void Save()
        {
            if (!dirty || readOnly || path == null) return;
            nextSave = DateTime.UtcNow.AddSeconds(30);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(Data, Formatting.Indented));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                dirty = false; lastSaveSucceeded = true;
            }
            catch (Exception ex) { lastSaveSucceeded = false; warning("Statistics save failed (" + ex.GetType().Name + "). Back up the statistics directory and check write permissions."); }
        }
    }
}
