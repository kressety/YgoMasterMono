﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace YgoMaster
{
    partial class GameServer
    {
        bool DoesPlayerHaveAnyVisibleReplays(Player player, Player requestedBy)
        {
            try
            {
                string replaysDir = GetReplaysDirectory(player);
                if (!Directory.Exists(replaysDir))
                {
                    return false;
                }
                foreach (string file in Directory.GetFiles(replaysDir, "*.json"))
                {
                    try
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            DuelSettings duelSettings = new DuelSettings();
                            duelSettings.FromDictionary(data);

                            if (player != requestedBy && !duelSettings.open)
                            {
                                continue;
                            }

                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        void GetReplayStats(Player player, out int num, out int numLocked, out int max)
        {
            num = 0;
            numLocked = 0;
            max = DuelReplaySaveFileLimit;
            try
            {
                string replaysDir = GetReplaysDirectory(player);
                if (!Directory.Exists(replaysDir))
                {
                    return;
                }
                foreach (string file in Directory.GetFiles(replaysDir, "*.json"))
                {
                    try
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            DuelSettings duelSettings = new DuelSettings();
                            duelSettings.FromDictionary(data);

                            num++;
                            if (duelSettings.IsReplayLocked)
                            {
                                numLocked++;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        void Act_ReplayList(GameServerWebRequest request)
        {
            request.Player.RecentlyListedReplayFilesByDid.Clear();

            uint pcode = Utils.GetValue<uint>(request.ActParams, "pcode");
            bool isSelf = pcode == 0 || pcode == request.Player.Code;
            if (isSelf)
            {
                pcode = request.Player.Code;
            }

            Player player;
            if (MultiplayerEnabled)
            {
                lock (playersLock)
                {
                    if (!playersById.TryGetValue(pcode, out player))
                    {
                        return;
                    }
                }
            }
            else
            {
                player = request.Player;
            }

            if (isSelf)
            {
                request.Remove("User.replay");
            }
            else
            {
                request.Remove("Friend.replay");
            }

            Dictionary<string, object> userData = request.GetOrCreateDictionary(isSelf ? "User" : "Friend");
            Dictionary<string, object> allReplayData = Utils.GetOrCreateDictionary(userData, "replay");

            string replaysDir = GetReplaysDirectory(player);
            if (!Directory.Exists(replaysDir))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(replaysDir, "*.json"))
            {
                try
                {
                    Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                    if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                    {
                        DuelSettings duelSettings = new DuelSettings();
                        duelSettings.FromDictionary(data);

                        if (pcode != 0 && pcode != request.Player.Code && !duelSettings.open)
                        {
                            continue;
                        }

                        Player[] players = new Player[2];
                        lock (playersLock)
                        {
                            for (int i = 0; i < players.Length; i++)
                            {
                                playersById.TryGetValue((uint)duelSettings.pcode[i], out players[i]);
                            }
                        }

                        long did = duelSettings.DuelBeginTime;
                        while (request.Player.RecentlyListedReplayFilesByDid.ContainsKey(did))
                        {
                            did++;
                        }
                        duelSettings.did = did;
                        request.Player.RecentlyListedReplayFilesByDid[did] = file;
                        allReplayData[did.ToString()] = duelSettings.ToDictionaryForReplayList(players);
                    }
                }
                catch
                {
                }
            }

            // TODO: Hook tags up to Settings.json (NOTE: Last updated v2.1.0)
            request.Remove("Master.ReplayTag");
            List<int> replayTags = new List<int>();
            for (int i = 1; i <= 28; i++)
            {
                replayTags.Add(i);
            }
            Dictionary<string, object> masterData = request.GetOrCreateDictionary("Master");
            masterData["ReplayTag"] = replayTags;

            request.Player.IsViewingOwnReplays = isSelf;
        }

        void Act_ReplaySetOpen(GameServerWebRequest request)
        {
            if (!request.Player.IsViewingOwnReplays)
            {
                return;
            }
            long did = Utils.GetValue<long>(request.ActParams, "did");
            bool open = Utils.GetValue<bool>(request.ActParams, "open");
            string replayPath;
            if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
            {
                try
                {
                    if (File.Exists(replayPath))
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(replayPath)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            DuelSettings duelSettings = new DuelSettings();
                            duelSettings.FromDictionary(data);
                            if (duelSettings.open != open)
                            {
                                duelSettings.open = open;
                                File.WriteAllText(replayPath, MiniJSON.Json.Serialize(duelSettings.ToDictionary()));
                            }
                            Dictionary<string, object> userData = request.GetOrCreateDictionary("User");
                            Dictionary<string, object> allReplayData = Utils.GetOrCreateDictionary(userData, "replay");
                            Dictionary<string, object> replayData = Utils.GetOrCreateDictionary(allReplayData, did.ToString());
                            replayData["open"] = open;
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.LogWarning("Failed to update replay public status for deck " + did + " '" + replayPath + "'. Error: " + e);
                }
            }
        }

        void Act_ReplaySetLock(GameServerWebRequest request)
        {
            if (!request.Player.IsViewingOwnReplays)
            {
                return;
            }
            long did = Utils.GetValue<long>(request.ActParams, "did");
            bool lockReplay = Utils.GetValue<bool>(request.ActParams, "lock");
            string replayPath;
            if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
            {
                try
                {
                    if (File.Exists(replayPath))
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(replayPath)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            DuelSettings duelSettings = new DuelSettings();
                            duelSettings.FromDictionary(data);
                            if (duelSettings.IsReplayLocked != lockReplay)
                            {
                                duelSettings.IsReplayLocked = lockReplay;
                                File.WriteAllText(replayPath, MiniJSON.Json.Serialize(duelSettings.ToDictionary()));
                            }
                            Dictionary<string, object> userData = request.GetOrCreateDictionary("User");
                            Dictionary<string, object> allReplayData = Utils.GetOrCreateDictionary(userData, "replay");
                            Dictionary<string, object> replayData = Utils.GetOrCreateDictionary(allReplayData, did.ToString());
                            replayData["lock"] = lockReplay;
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.LogWarning("Failed to update replay public status for deck " + did + " '" + replayPath + "'. Error: " + e);
                }
            }
        }

        void Act_ReplaySetTags(GameServerWebRequest request)
        {
            if (!request.Player.IsViewingOwnReplays)
            {
                return;
            }
            long did = Utils.GetValue<long>(request.ActParams, "did");
            List<int> tags = Utils.GetIntList(request.ActParams, "tags");
            string replayPath;
            if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
            {
                try
                {
                    if (File.Exists(replayPath))
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(replayPath)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            DuelSettings duelSettings = new DuelSettings();
                            duelSettings.FromDictionary(data);
                            duelSettings.tags.Clear();
                            duelSettings.tags.AddRange(tags);
                            File.WriteAllText(replayPath, MiniJSON.Json.Serialize(duelSettings.ToDictionary()));
                            Dictionary<string, object> userData = request.GetOrCreateDictionary("User");
                            Dictionary<string, object> allReplayData = Utils.GetOrCreateDictionary(userData, "replay");
                            Dictionary<string, object> replayData = Utils.GetOrCreateDictionary(allReplayData, did.ToString());
                            replayData["tags"] = tags;
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.LogWarning("Failed to update replay public status for deck " + did + " '" + replayPath + "'. Error: " + e);
                }
            }
        }

        void Act_ReplaySetPickCards(GameServerWebRequest request)
        {
            if (!request.Player.IsViewingOwnReplays)
            {
                return;
            }
            long did = Utils.GetValue<long>(request.ActParams, "did");
            int myid = Utils.GetValue<int>(request.ActParams, "myid");
            CardCollection pcards = new CardCollection();
            pcards.FromIndexedDictionary(Utils.GetDictionary(request.ActParams, "pcards"));
            string replayPath;
            if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
            {
                try
                {
                    if (File.Exists(replayPath))
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(replayPath)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            DuelSettings duelSettings = new DuelSettings();
                            duelSettings.FromDictionary(data);
                            if (myid < duelSettings.Deck.Length)
                            {
                                duelSettings.Deck[myid].DisplayCards.CopyFrom(pcards);
                            }
                            File.WriteAllText(replayPath, MiniJSON.Json.Serialize(duelSettings.ToDictionary()));
                            Dictionary<string, object> userData = request.GetOrCreateDictionary("User");
                            Dictionary<string, object> allReplayData = Utils.GetOrCreateDictionary(userData, "replay");
                            Dictionary<string, object> replayData = Utils.GetOrCreateDictionary(allReplayData, did.ToString());
                            replayData["pcard"] = new List<object>()
                            {
                                duelSettings.Deck[0].DisplayCards.ToIndexDictionary(),
                                duelSettings.Deck[1].DisplayCards.ToIndexDictionary()
                            };
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.LogWarning("Failed to update replay public status for deck " + did + " '" + replayPath + "'. Error: " + e);
                }
            }
        }

        void Act_ReplayGetDeck(GameServerWebRequest request)
        {
            long did = Utils.GetValue<long>(request.ActParams, "did");
            string replayPath;
            if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
            {
                try
                {
                    if (File.Exists(replayPath))
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(replayPath)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            DuelSettings duelSettings = new DuelSettings();
                            duelSettings.FromDictionary(data);

                            DeckInfo deckInfo = duelSettings.Deck[duelSettings.MyID == 0 ? 1 : 0];
                            if (deckInfo != null)
                            {
                                Dictionary<string, object> allDeckListData = request.GetOrCreateDictionary("RDeckList");
                                allDeckListData[did.ToString()] = deckInfo.ToDictionary();
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.LogWarning("Failed to get opponents deck for replay " + did + " '" + replayPath + "'. Error: " + e);
                }
            }
        }

        void Act_ReplayRemove(GameServerWebRequest request)
        {
            if (!request.Player.IsViewingOwnReplays)
            {
                return;
            }
            long did = Utils.GetValue<long>(request.ActParams, "did");
            string replayPath;
            if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
            {
                try
                {
                    if (File.Exists(replayPath))
                    {
                        File.Delete(replayPath);
                        request.Player.RecentlyListedReplayFilesByDid.Remove(did);
                    }
                }
                catch (Exception e)
                {
                    Utils.LogWarning("Failed delete replay " + did + " '" + replayPath + "'. Error: " + e);
                }
            }
            request.Remove("User.replay." + did);

            int numReplays, numLockedReplays, maxReplays;
            GetReplayStats(request.Player, out numReplays, out numLockedReplays, out maxReplays);
            request.Response["ReplayInfo"] = new Dictionary<string, object>()
            {
                { "num", numReplays },
                { "lock", numLockedReplays },
                { "max", maxReplays }
            };
        }

        void Act_ReplayDuel(GameServerWebRequest request)
        {
            // TODO: Handle pcode (we currently assume the client can only request based on the previous file list)

            uint pcode = Utils.GetValue<uint>(request.ActParams, "pcode");
            long did = Utils.GetValue<long>(request.ActParams, "did");

            request.Remove("Response");
            bool loaded = false;

            string replayPath;
            if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
            {
                try
                {
                    if (File.Exists(replayPath))
                    {
                        Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(replayPath)) as Dictionary<string, object>;
                        if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                        {
                            //duel:push?GameMode=7&did=___DID___&pl_info={"___PCODE1___":{"is_same_os":false,"online_id":""},"___PCODE2___":{"is_same_os":false,"online_id":""}}&r_myid=1&is_db=1
                            Dictionary<string, object> responseData = request.GetOrCreateDictionary("Response");
                            responseData["UrlScheme"] = "duel:push?GameMode=" + (int)GameMode.Replay + "&did=" + did;

                            loaded = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.LogWarning("Failed play replay " + did + " '" + replayPath + "'. Error: " + e);
                }
            }

            if (!loaded)
            {
                request.ResultCode = (int)ResultCodes.PvPCode.NO_REPLAY_DATA;
            }
        }

        void Act_DuelBeginReplay(GameServerWebRequest request)
        {
            Dictionary<string, object> rule = Utils.GetDictionary(request.ActParams, "rule");
            long did = Utils.GetValue<long>(rule, "did");
            long roomReplayDid = Utils.GetValue<long>(rule, "roomReplayDid");// Custom

            request.Remove("Duel", "DuelResult", "Result");

            DuelSettings duelSettings = null;

            if (roomReplayDid > 0)
            {
                DuelRoom duelRoom = request.Player.DuelRoom;
                if (duelRoom != null)
                {
                    DuelRoomReplay replay;
                    lock (duelRoom.Replays)
                    {
                        duelRoom.ReplaysByDid.TryGetValue(did, out replay);
                    }
                    if (replay != null)
                    {
                        if (replay.Player1.did == roomReplayDid)
                        {
                            duelSettings = replay.Player1;
                        }
                        else if (replay.Player2.did == roomReplayDid)
                        {
                            duelSettings = replay.Player2;
                        }
                    }
                }
            }
            else
            {
                string replayPath;
                if (request.Player.RecentlyListedReplayFilesByDid.TryGetValue(did, out replayPath))
                {
                    try
                    {
                        if (File.Exists(replayPath))
                        {
                            Dictionary<string, object> data = MiniJSON.Json.Deserialize(File.ReadAllText(replayPath)) as Dictionary<string, object>;
                            if (data != null && data.ContainsKey(DuelSettings.ExpectedDuelDataKey))
                            {
                                duelSettings = new DuelSettings();
                                duelSettings.FromDictionary(data);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.LogWarning("Failed play replay " + did + " '" + replayPath + "'. Error: " + e);
                    }
                }
            }

            if (duelSettings != null)
            {
                duelSettings.MyType = (int)DuelPlayerType.Replay;
                duelSettings.chapter = 0;
                duelSettings.GameMode = (int)GameMode.Replay;

                Dictionary<string, object> dict = DuelSettings.FixupReplayRequirements(duelSettings, duelSettings.ToDictionary());
                dict["publicLevel"] = (int)DuelReplayCardVisibility;
                //dict["hand_open"] = true;
                //dict["blockRelativeCard"] = false;
                request.Response["Duel"] = dict;
            }
            else
            {
                request.ResultCode = (int)ResultCodes.PvPCode.NO_REPLAY_DATA;
            }
        }
    }
}
