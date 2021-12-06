#region License (GPL v3)
/*
    MonBots - NPC Players that protect monuments, sort of
    Copyright (c) 2021 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v3)
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MonBots", "RFC1920", "1.0.1")]
    [Description("Adds interactive NPCs at various monuments")]
    internal class MonBots : RustPlugin
    {
        #region vars
        [PluginReference]
        private readonly Plugin Kits;

        private ConfigData configData;
        private const string sci = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab";
        private List<ulong> isopen = new List<ulong>();

        private const string permNPCGuiUse = "monbot.use";
        private const string NPCGUI = "npc.editor";
        private const string NPCGUK = "npc.kitselect";
        private const string NPCGUL = "npc.locoselect";
        private const string NPCGUM = "npc.monselect";
        private const string NPCGUN = "npc.kitsetnum";
        private const string NPCGUR = "npc.roadselect";
        private const string NPCGUS = "npc.select";
        private const string NPCGUV = "npc.setval";
        private readonly List<string> guis = new List<string>() { NPCGUI, NPCGUK, NPCGUL, NPCGUM, NPCGUN, NPCGUR, NPCGUS, NPCGUV };

        private bool newsave;

        public static MonBots Instance;
        public Dictionary<string, SpawnPoints> spawnpoints = new Dictionary<string, SpawnPoints>();
        private Dictionary<ulong, MonBotPlayer>  hpcacheid = new Dictionary<ulong, MonBotPlayer>();

        private static SortedDictionary<string, Vector3> monPos = new SortedDictionary<string, Vector3>();
        private static SortedDictionary<string, Vector3> monSize = new SortedDictionary<string, Vector3>();
        private static SortedDictionary<string, Vector3> cavePos = new SortedDictionary<string, Vector3>();

        private readonly static int playerMask = LayerMask.GetMask("Player (Server)");
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));

        private void DoLog(string message)
        {
            if (configData.Options.debug)
            {
                Interface.Oxide.LogInfo(message);
            }
        }
        #endregion

        #region global
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["npcgui"] = "MonBot GUI",
                ["npcguisel"] = "MonBotGUI NPC Select ",
                ["npcguikit"] = "MonBotGUI Kit Select",
                ["npcguimon"] = "MonBotGUI Monument Select",
                ["npcguiroad"] = "MonBotGUI Road Select",
                ["npcguiloco"] = "MonBotGUI LocoMode Select",
                ["close"] = "Close",
                ["none"] = "None",
                ["noid"] = "noid",
                ["start"] = "Start",
                ["end"] = "End",
                ["debug"] = "Debug set to {0}",
                ["monbots"] = "MonBots",
                ["needselect"] = "Select NPC",
                ["select"] = "Select",
                ["editing"] = "Editing",
                ["mustselect"] = "Please press 'Select' to choose an NPC.",
                ["guihelp1"] = "For blue buttons, click to toggle true/false.",
                ["guihelp2"] = "For all values above in gray, you may type a new value and press enter.",
                ["guihelp3"] = "For kit, press the button to select a kit.",
                ["add"] = "Add",
                ["new"] = "Create New",
                ["remove"] = "Remove",
                ["spawnhere"] = "Spawn Here",
                ["tpto"] = "Teleport to NPC",
                ["name"] = "Name",
                ["online"] = "Online",
                ["offline"] = "Offline",
                ["deauthall"] = "DeAuthAll",
                ["remove"] = "Remove"
            }, this);
        }

        private void OnServerInitialized()
        {
            LoadConfigVariables();
            Instance = this;

            LoadData();
            FindMonuments();
            LoadBots();
            SaveData();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                foreach (string gui in guis)
                {
                    CuiHelper.DestroyUi(player, gui);
                }

                if (isopen.Contains(player.userID))
                {
                    isopen.Remove(player.userID);
                }
            }
        }

        private void OnEntitySpawned(NPCPlayerCorpse corpse)
        {
            if (corpse == null) return;
            ulong userid = corpse?.playerSteamID ?? 0;
            if (hpcacheid.ContainsKey(userid))
            {
                MonBotPlayer npc = hpcacheid[userid];
                DoLog($"Setting corpse loot panel name to {npc.info.displayName}");
                corpse._playerName = npc.info.displayName;
                corpse.lootPanelName = npc.info.displayName;

                DoLog("Checking lootable flag");
                if (!npc.info.lootable)
                {
                    for (int i = 0; i < corpse.containers.Length; i++)
                    {
                        DoLog($"Clearing corpse container {i.ToString()}");
                        corpse.containers[i].Clear();
                    }
                    ItemManager.DoRemoves();
                }
            }
        }

        private void SpawnBot(ulong userid)
        {
            // Used to identify bot for respawn on death
            if (hpcacheid.ContainsKey(userid))
            {
                DoLog($"{userid.ToString()} found in cached ids!");
                MonBotPlayer botinfo = hpcacheid[userid];
                SpawnPoints sp = spawnpoints[botinfo.info.monument];
                DoLog($"Respawning bot {botinfo.info.displayName} at {sp.monname}");
                SpawnBot(sp, botinfo.spawnPos, botinfo.info.kit);
            }
            else
            {
                DoLog($"{userid.ToString()} not found in cached ids :(");
            }
        }

        private void SpawnBot(SpawnPoints sp, Vector3 pos, string kit)
        {
            // Used for initial spawn by LoadBots()
            HumanNPC bot = (HumanNPC)GameManager.server.CreateEntity(sci, pos, new Quaternion(), true);
            bot.Spawn();

            NextTick(() =>
            {
                string botname = GetBotName(sp.names.ToArray());
                DoLog($"Spawning bot {botname} at {sp.monname}");
                MonBotPlayer mono = bot.gameObject.AddComponent<MonBotPlayer>();
                mono.spawnPos = pos;
                mono.info = new MonBotInfo(bot.userID, bot.transform.position, bot.transform.rotation)
                {
                    displayName = botname,
                    userid = bot.userID,
                    monument = sp.monname,
                    lootable = sp.lootable,
                    health = sp.startHealth,
                    invulnerable = sp.invulnerable,
                    hostile = sp.hostile,
                    dropWeapon = sp.dropWeapon,
                    detectRange = sp.detectRange,
                    roamRange = sp.roamRange,
                    respawnTimer = sp.respawnTime,
                    respawn = true,
                    loc = pos,
                    kit = kit
                };

                //mono.UpdateHealth(mono.info);
                bot.startHealth = sp.startHealth;

                bot.Brain.Navigator.Agent.agentTypeID = -1372625422;
                bot.Brain.Navigator.DefaultArea = "Walkable";
                bot.Brain.Navigator.Init(bot, bot.Brain.Navigator.Agent);
                bot.Brain.ForceSetAge(0);
                bot.Brain.TargetLostRange = mono.info.detectRange;
                bot.Brain.HostileTargetsOnly = !mono.info.hostile;
                bot.Brain.Navigator.BestCoverPointMaxDistance = 20f;//0
                bot.Brain.Navigator.BestRoamPointMaxDistance = mono.info.roamRange;//0
                bot.Brain.Navigator.MaxRoamDistanceFromHome = mono.info.roamRange;
                bot.Brain.Senses.Init(bot, 5f, mono.info.roamRange, mono.info.detectRange, -1f, true, false, true, mono.info.detectRange, !mono.info.hostile, false, true, EntityType.Player, false);

                bot.displayName = botname;
                hpcacheid.Add(bot.userID, mono);
                bot.inventory.Strip();
                Kits?.Call("GiveKit", bot, kit);

                ScientistNPC npc = bot as ScientistNPC;
                npc.DeathEffects = new GameObjectRef[0];
                npc.RadioChatterEffects = new GameObjectRef[0];
                npc.radioChatterType = ScientistNPC.RadioChatterType.NONE;
            });

            if (bot.IsHeadUnderwater())
            {
                bot.Kill();
            }
        }

        private void LoadBots(string monument = "")
        {
            DoLog("LoadBots called");
            Dictionary<string, SpawnPoints> newpoints = new Dictionary<string, SpawnPoints>(spawnpoints);
            foreach (KeyValuePair<string, SpawnPoints> sp in spawnpoints)
            {
                if (monument.Length > 0 && !sp.Key.Equals(monument)) continue;

                DoLog($"Working on spawn at {sp.Key}");
                int amount = sp.Value.spawnCount;
                string kit = "";
                for (int i = 0; i < amount; i++)
                {
                    if (sp.Value.kits.Count == 1)
                    {
                        kit = sp.Value.kits.FirstOrDefault();
                    }
                    else
                    {
                        int j = UnityEngine.Random.Range(0, sp.Value.kits.Count);
                        kit = sp.Value.kits[j];
                    }

                    Vector3 pos = monPos[sp.Key];
                    int x = UnityEngine.Random.Range(-15, 15);
                    int z = UnityEngine.Random.Range(-15, 15);
                    pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                    pos += new Vector3(x, 0, z);

                    SpawnBot(sp.Value, pos, kit);

                    newpoints[sp.Key].pos.Add(pos);
                    SaveData();
                }
            }
        }

        private string GetBotName(string[] names)
        {
            if (names.Length == 1)
            {
                return names[0];
            }
            else if (names.Length > 1)
            {
                int index = UnityEngine.Random.Range(0, names.Length);
                return names[index];
            }
            return "Bot";
        }

        private void OnNewSave()
        {
            newsave = true;
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList.Where(x => x.IsAdmin))
            {
                foreach (string gui in guis)
                {
                    CuiHelper.DestroyUi(player, gui);
                }

                if (isopen.Contains(player.userID))
                {
                    isopen.Remove(player.userID);
                }
            }

            MonBotPlayer[] bots = UnityEngine.Object.FindObjectsOfType<MonBotPlayer>();
            if (bots != null)
            {
                foreach (MonBotPlayer bot in bots)
                {
                    //UnityEngine.Object.Destroy(bot.player);
                    UnityEngine.Object.Destroy(bot);
                }
            }

            foreach (KeyValuePair<string, SpawnPoints> x in new Dictionary<string, SpawnPoints>(spawnpoints))
            {
                x.Value.pos = new List<Vector3>();
            }
            SaveData();
        }

        private void LoadData()
        {
            spawnpoints = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, SpawnPoints>>(Name + "/spawnpoints");
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/spawnpoints", spawnpoints);
        }
        #endregion

        #region Oxide Hooks
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            if (!input.WasJustPressed(BUTTON.USE)) return;

            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 3f, playerMask))
            {
                BasePlayer pl = hit.GetEntity().ToPlayer();
                MonBotPlayer hp = pl.GetComponent<MonBotPlayer>();

                if (hp == null) return;

                Interface.Oxide.CallHook("OnUseNPC", hp.player, player);
                SaveData();
            }
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo hitinfo)
        {
            if (entity == null) return;

            global::HumanNPC npc = entity as global::HumanNPC;
            if (npc == null) return;

            MonBotPlayer hp = npc.GetComponent<MonBotPlayer>();
            if (hp == null) return;

            //if (hp.info.dropWeapon)
            //{
            //    Item activeItem = hp.player.GetActiveItem();
            //    if (activeItem != null)
            //    {
            //        DoLog($"Dropping {hp.info.displayName}'s activeItem: {activeItem.info.shortname}");
            //        activeItem.Drop(npc.eyes.position, new Vector3(), new Quaternion());
            //        npc.svActiveItemID = 0;
            //        npc.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            //    }
            //}

            DoLog("Checking respawn variable");
            if (hp?.info.respawn == true)
            {
                DoLog($"Setting {hp.info.respawnTimer.ToString()} second respawn timer for {hp.info.displayName} ({hp.info.userid})");
                timer.Once(hp.info.respawnTimer, () => SpawnBot(hp.info.userid));
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitinfo)
        {
            if (entity == null) return null;
            if (hitinfo == null) return null;

            MonBotPlayer hp = entity.GetComponent<MonBotPlayer>();
            if (hp != null)
            {
                Interface.Oxide.CallHook("OnHitNPC", entity.GetComponent<BaseCombatEntity>(), hitinfo);
                if (hp.info.invulnerable)
                {
                    //hitinfo.damageTypes = new DamageTypeList();
                    //hitinfo.DoHitEffects = false;
                    //hitinfo.HitMaterial = 0;
                    return true;
                }
                //else
                //{
                //    hp.protection.Scale(hitinfo.damageTypes);
                //}
            }
            return null;
        }

        private object CanLootPlayer(global::HumanNPC target, BasePlayer player)
        {
            if (player == null || target == null)
            {
                return null;
            }

            MonBotPlayer hp = target.GetComponentInParent<MonBotPlayer>();
            if (hp?.info.lootable == false)
            {
                DoLog($"Player {player.displayName}:{player.UserIDString} looting MonBot {hp.info.displayName}");
                NextTick(player.EndLooting);
                return true;
            }
            return null;
        }

        private object CanLootEntity(BasePlayer player, NPCPlayerCorpse corpse)
        {
            if (player == null || corpse == null)
            {
                return null;
            }

            if (hpcacheid.ContainsKey(corpse.playerSteamID))
            {
                MonBotPlayer hp = hpcacheid[corpse.playerSteamID];
                if (hp == null)
                {
                    return null;
                }

                DoLog($"Player {player.displayName}:{player.UserIDString} looting MonBot {corpse.name}:{corpse.playerSteamID.ToString()}");
                if (!hp.info.lootable)
                {
                    NextTick(player.EndLooting);
                    return true;
                }
            }

            return null;
        }

        //private void OnLootPlayer(BasePlayer looter, BasePlayer target)
        //{
        //    if (npcs.ContainsKey(target.userID))
        //    {
        //        Interface.Oxide.CallHook("OnLootNPC", looter.inventory.loot, target, target.userID);
        //    }
        //}

        //private void OnLootEntity(BasePlayer looter, BaseEntity entity)
        //{
        //    if (looter == null || !(entity is PlayerCorpse)) return;
        //    ulong userId = ((PlayerCorpse)entity).playerSteamID;
        //    MonBotInfo hi = null;
        //    npcs.TryGetValue(userId, out hi);
        //    if (hi != null)
        //    {
        //        Interface.Oxide.CallHook("OnLootNPC", looter.inventory.loot, entity, userId);
        //    }
        //}
        #endregion

        #region Our Inbound Hooks
        private bool IsMonBot(BasePlayer player) => player.GetComponentInParent<MonBotPlayer>() != null;

        private string GetMonBotName(ulong npcid)
        {
            DoLog($"Looking for monbot: {npcid.ToString()}");
            MonBotPlayer hp = FindMonBotByID(npcid);
            if (hp == null)
            {
                return null;
            }

            DoLog($"Found monbot: {hp.player.displayName}");
            return hp.info.displayName;
        }

        private void SetMonBotInfo(ulong npcid, string toset, string data, string rot = null)
        {
            DoLog($"SetMonBotInfo called for {npcid.ToString()} {toset},{data}");
            MonBotPlayer hp = FindMonBotByID(npcid);
            if (hp == null)
            {
                return;
            }

            switch (toset)
            {
                case "kit":
                    hp.info.kit = data;
                    break;
                case "entrypause":
                    hp.info.entrypause = !GetBoolValue(data);
                    break;
                case "entrypausetime":
                    hp.info.entrypausetime = Convert.ToSingle(data);
                    break;
                case "health":
                    hp.info.health = Convert.ToSingle(data);
                    hp.UpdateHealth(hp.info);
                    break;
                case "name":
                case "displayName":
                    hp.info.displayName = data;
                    break;
                case "invulnerable":
                case "invulnerability":
                    hp.info.invulnerable = !GetBoolValue(data);
                    break;
                case "lootable":
                    hp.info.lootable = !GetBoolValue(data);
                    break;
                case "hostile":
                    hp.info.hostile = !GetBoolValue(data);
                    break;
                case "canmove":
                    hp.info.canmove = !GetBoolValue(data);
                    break;
                case "allowsit":
                case "cansit":
                    hp.info.cansit = !GetBoolValue(data);
                    hp.info.canmove = hp.info.cansit;
                    break;
                case "canride":
                    hp.info.canride = !GetBoolValue(data);
                    hp.info.canmove = hp.info.canride;
                    break;
                case "dropWeapon":
                    hp.info.dropWeapon = !GetBoolValue(data);
                    break;
                case "respawn":
                    hp.info.respawn= !GetBoolValue(data);
                    break;
                case "respawnTimer":
                case "respawntimer":
                    hp.info.respawnTimer = Convert.ToSingle(data);
                    break;
                case "attackdistance":
                    hp.info.attackDistance = Convert.ToSingle(data);
                    break;
                case "maxdistance":
                    hp.info.maxDistance = Convert.ToSingle(data);
                    break;
                case "damagedistance":
                    hp.info.damageDistance = Convert.ToSingle(data);
                    break;
                case "speed":
                    hp.info.speed = Convert.ToSingle(data);
                    break;
                case "spawn":
                case "loc":
                    hp.info.loc = StringToVector3(data);
                    break;
                case "rot":
                    break;
            }
            DoLog("Saving Data");
            SaveData();
            DoLog("Respawning");
            //RespawnNPC(hp.player);
        }

        private void GiveMonBot(HumanNPC bot, string itemname, string loc = "wear", ulong skinid = 0, int count = 1)
        {
            DoLog($"GiveMonBot called: {bot.displayName}, {itemname}, {loc}");
            MonBotPlayer npc = bot.GetComponent<MonBotPlayer>();
            if (npc != null)
            {
                switch (loc)
                {
                    case "kit":
                        Puts("Setting info kit");
                        npc.info.kit = itemname;
                        Puts("Updating inventory");
                        UpdateInventory(npc);
                        break;
                    case "belt":
                        {
                            Item item = ItemManager.CreateByName(itemname, 1, skinid);
                            item.MoveToContainer(bot.inventory.containerBelt, -1, true);
                        }
                        break;
                    case "main":
                        {
                            // e.g. for ammo
                            Item item = ItemManager.CreateByName(itemname, count, skinid);
                            item.MoveToContainer(bot.inventory.containerMain, -1, true);
                        }
                        break;
                    default:
                        {
                            Item item = ItemManager.CreateByName(itemname, 1, skinid);
                            item.MoveToContainer(bot.inventory.containerWear, -1, true);
                        }
                        break;
                }
                bot.inventory.ServerUpdate(0f);
            }
        }

        private void GiveMonBot(ulong npcid, string itemname, string loc = "wear", ulong skinid = 0, int count = 1)
        {
            MonBotPlayer npc = FindMonBotByID(npcid);
            DoLog($"GiveMonBot called: {npc.player.displayName}, {itemname}, {loc}");
            if (npc.player != null)
            {
                switch (loc)
                {
                    case "kit":
                        npc.info.kit = itemname;
                        UpdateInventory(npc);
                        break;
                    case "belt":
                        {
                            Item item = ItemManager.CreateByName(itemname, 1, skinid);
                            item.MoveToContainer(npc.player.inventory.containerBelt, -1, true);
                        }
                        break;
                    case "main":
                        {
                            // e.g. for ammo
                            Item item = ItemManager.CreateByName(itemname, count, skinid);
                            item.MoveToContainer(npc.player.inventory.containerMain, -1, true);
                        }
                        break;
                    default:
                        {
                            Item item = ItemManager.CreateByName(itemname, 1, skinid);
                            item.MoveToContainer(npc.player.inventory.containerWear, -1, true);
                        }
                        break;
                }
                npc.player.inventory.ServerUpdate(0f);
            }
        }
        #endregion

        #region GUI
        // Determine open GUI to limit interruptions
        private void IsOpen(ulong uid, bool set=false)
        {
            if (set)
            {
                DoLog($"Setting isopen for {uid}");
                if (!isopen.Contains(uid))
                {
                    isopen.Add(uid);
                }

                return;
            }
            DoLog($"Clearing isopen for {uid}");
            isopen.Remove(uid);
        }
        #endregion

        #region utility
        public static IEnumerable<TValue> RandomValues<TKey, TValue>(IDictionary<TKey, TValue> dict)
        {
            System.Random rand = new System.Random();
            List<TValue> values = dict.Values.ToList();
            int size = dict.Count;
            while (true)
            {
                yield return values[rand.Next(size)];
            }
        }

        private MonBotPlayer FindMonBotByID(ulong userid, bool playerid = false)
        {
            MonBotPlayer hp;
            if (hpcacheid.TryGetValue(userid, out hp))
            {
                DoLog($"Found matching NPC for userid {userid.ToString()} in cache");
                return hp;
            }
            foreach (MonBotPlayer humanplayer in Resources.FindObjectsOfTypeAll<MonBotPlayer>())
            {
                DoLog($"Is {humanplayer.player.displayName} a MonBot?");
                if (humanplayer.player.userID != userid)
                {
                    continue;
                }

                DoLog($"Found matching NPC for userid {userid.ToString()}");
                return hpcacheid[userid];
            }
            return null;
        }

        private BasePlayer FindPlayerByID(ulong userid)
        {
            DoLog($"Searching for player object with userid {userid.ToString()}");
            foreach (BasePlayer player in Resources.FindObjectsOfTypeAll<BasePlayer>())
            {
                if (player.userID == userid)
                {
                    DoLog("..found one!");
                    return player;
                }
            }
            DoLog("..found NONE");
            return null;
        }

        private void UpdateInventory(MonBotPlayer hp)
        {
            DoLog("UpdateInventory called...");
            //if (hp.player.inventory == null) return;
            if (hp.info == null)
            {
                return;
            }
            DoLog("Destroying inventory");
            hp.player.inventory.Strip();
            //hp.player.inventory.DoDestroy();
            //hp.player.inventory.ServerInit(hp.player);

            if (hp.info.kit.Length > 0)
            {
                //DoLog($"  Trying to give kit '{hp.info.kit}' to {hp.player.userID}");
                Kits?.Call("GiveKit", hp.player, hp.info.kit);
            }
            //hp.player.SV_ClothingChanged();
            //if (hp.info.protections != null)
            //{
            //    hp.player.baseProtection.Clear();
            //    foreach (KeyValuePair<DamageType, float> protection in hp.info.protections)
            //    {
            //        hp.player.baseProtection.Add(protection.Key, protection.Value);
            //    }
            //}
            //hp.player.inventory.ServerUpdate(0f);
        }
        #endregion

        #region classes
        public class SpawnPoints
        {
            public string monname;
            public int spawnCount;
            public float respawnTime;
            public float detectRange;
            public float roamRange;
            public float startHealth;
            public bool invulnerable;
            public bool lootable;
            public bool dropWeapon;
            public bool hostile;
            public List<string> kits;
            public List<string> names;
            public List<Vector3> pos;
        }

        public class MonBotInfo
        {
            // Basic
            public ulong userid;
            public string displayName;
            public string kit;
            public Dictionary<DamageType, float> protections = new Dictionary<DamageType, float>();
            public Timer pausetimer;

            // Logic
            public bool enable = true;
            public bool canmove;
            public bool cansit;
            public bool canride;
            public bool canfly;

            public bool ephemeral;
            public bool hostile;
            public bool dropWeapon;
            public bool invulnerable;
            public bool lootable;
            public bool entrypause;
            public bool respawn;

            // Location and movement
            public float population;
            public float detectRange;
            public float roamRange;
            public string monument;
            public float respawnTimer;

            public float speed;
            public Vector3 spawnloc;
            public Vector3 loc;
            public Quaternion rot;
            public Vector3 targetloc;
            public float health;
            public float maxDistance;
            public float attackDistance;
            public float damageDistance;
            public float damageAmount;
            public float followTime;
            public float entrypausetime;
            public string waypoint;
            public bool holdingWeapon;

            public MonBotInfo(ulong uid, Vector3 position, Quaternion rotation)
            {
                //displayName = Instance.configData.Options.defaultName.Length > 0 ? Instance.configData.Options.defaultName : "Noid";
                //enable = true;
                //invulnerable = true;
                //entrypause = true;
                //respawn = true;
                //population = 1;
                //speed = 3f;
                //loc = position;
                //rot = rotation;

                //health = Instance.configData.Options.defaultHealth > 0 ? Instance.configData.Options.defaultHealth : 50f;
                //maxDistance = 100f;
                //attackDistance = 30f;
                //damageDistance = 20f;
                //damageAmount = 1f;
                //followTime = 30f;
                //entrypausetime = 5f;
                //respawnTimer = Instance.configData.Options.respawnTimer > 0 ? Instance.configData.Options.respawnTimer : 30f;

                //for (int i = 0; i < (int)DamageType.LAST; i++)
                //{
                //    protections[(DamageType)i] = 0f;
                //}
            }
        }

        public class MonBotPlayer : MonoBehaviour
        {
            public MonBotInfo info;
            public ProtectionProperties protection;
            public HumanNPC player;

            public Vector3 spawnPos;

            private void Start()
            {
                player = GetComponent<HumanNPC>();
                //protection = ScriptableObject.CreateInstance<ProtectionProperties>();
                //GestureConfig gestureConfig = ScriptableObject.CreateInstance<GestureConfig>();
                //gestureConfig.actionType = new GestureConfig.GestureActionType();
                InvokeRepeating("GoHome", 1f, 1f);
            }

            private void GoHome()
            {
                if (player?.IsDestroyed != false || player.isMounted)
                {
                    return;
                }

                if (!player.HasBrain)
                {
                    return;
                }

                if (player.Distance(spawnPos) >= 60f)
                {
                    player.Brain.Senses.Memory.Targets.Clear();
                    player.Brain.Senses.Memory.Threats.Clear();
                }

                if (player.Brain.Senses.Memory.Targets.Count != 0)
                {
                    return;
                }

                if (player.Brain.Navigator.Agent?.isOnNavMesh != true)
                {
                    player.Brain.Navigator.Destination = spawnPos;
                    player.SetDestination(spawnPos);
                }
                else
                {
                    player.Brain.Navigator.SetDestination(spawnPos);
                }
            }

            public void UpdateHealth(MonBotInfo info)
            {
                player.InitializeHealth(info.health, info.health);
                player.health = info.health;
            }

            private void OnDestroy()
            {
                if (player?.IsDestroyed == false)
                {
                    player.Kill();
                }
                CancelInvoke();
            }
        }

        public static class UI
        {
            public static CuiElementContainer Container(string panel, string color, string min, string max, bool useCursor = false, string parent = "Overlay")
            {
                return new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = {AnchorMin = min, AnchorMax = max},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panel
                    }
                };
            }

            public static void Panel(ref CuiElementContainer container, string panel, string color, string min, string max, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    CursorEnabled = cursor
                },
                panel);
            }

            public static void Label(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { Color = color, FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = min, AnchorMax = max }
                },
                panel);
            }

            public static void Button(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                panel);
            }

            public static void Input(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = align,
                            CharsLimit = 30,
                            Color = color,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text
                        },
                        new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max },
                        new CuiNeedsCursorComponent()
                    }
                });
            }

            public static void Icon(ref CuiElementContainer container, string panel, string color, string imageurl, string min, string max)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent
                        {
                            Url = imageurl,
                            Sprite = "assets/content/textures/generic/fulltransparent.tga",
                            Color = color
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = min,
                            AnchorMax = max
                        }
                    }
                });
            }

            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                {
                    hexColor = hexColor.Substring(1);
                }
                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }
        #endregion

        #region Helpers
        private static bool GetBoolValue(string value)
        {
            if (value == null)
            {
                return false;
            }

            value = value.Trim().ToLower();
            switch (value)
            {
                case "on":
                case "true":
                case "yes":
                case "1":
                case "t":
                case "y":
                    return true;
                default:
                    return false;
            }
        }

        private int RowNumber(int max, int count) => Mathf.FloorToInt(count / max);

        private float[] GetButtonPosition(int rowNumber, int columnNumber)
        {
            float offsetX = 0.05f + (0.096f * columnNumber);
            float offsetY = (0.80f - (rowNumber * 0.064f));

            return new float[] { offsetX, offsetY, offsetX + 0.196f, offsetY + 0.03f };
        }

        private float[] GetButtonPositionP(int rowNumber, int columnNumber)
        {
            float offsetX = 0.05f + (0.126f * columnNumber);
            float offsetY = (0.87f - (rowNumber * 0.064f));

            return new float[] { offsetX, offsetY, offsetX + 0.226f, offsetY + 0.03f };
        }

        private bool TryGetPlayerView(BasePlayer player, out Quaternion viewAngle)
        {
            viewAngle = new Quaternion(0f, 0f, 0f, 0f);
            if (player.serverInput?.current == null)
            {
                return false;
            }

            viewAngle = Quaternion.Euler(player.serverInput.current.aimAngles);
            return true;
        }

        public void Teleport(BasePlayer player, Vector3 position)
        {
            if (player.net?.connection != null)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            }

            player.SetParent(null, true, true);
            player.EnsureDismounted();
            player.Teleport(position);
            player.UpdateNetworkGroup();
            player.StartSleeping();
            player.SendNetworkUpdateImmediate(false);

            if (player.net?.connection != null)
            {
                player.ClientRPCPlayer(null, player, "StartLoading");
            }
        }

//        private void StartSleeping(BasePlayer player)
//        {
//            if (player.IsSleeping()) return;
//            player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
//            if (!BasePlayer.sleepingPlayerList.Contains(player)) BasePlayer.sleepingPlayerList.Add(player);
//            player.CancelInvoke("InventoryUpdate");
//        }

        public static Vector3 StringToVector3(string sVector)
        {
            // Remove the parentheses
            if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            {
                sVector = sVector.Substring(1, sVector.Length - 2);
            }

            // split the items
            string[] sArray = sVector.Split(',');

            return new Vector3(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2])
            );
        }

        private string RandomString()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            List<char> charList = chars.ToList();

            string random = "";

            for (int i = 0; i <= UnityEngine.Random.Range(5, 10); i++)
            {
                random += charList[UnityEngine.Random.Range(0, charList.Count - 1)];
            }
            return random;
        }

        private void FindMonuments()
        {
            Vector3 extents = Vector3.zero;
            float realWidth = 0f;
            string name = "";

            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (monument.name.Contains("power_sub"))
                {
                    continue;
                }

                realWidth = 0f;
                name = null;

                if (monument.name == "OilrigAI")
                {
                    name = "Small Oilrig";
                    realWidth = 100f;
                }
                else if (monument.name == "OilrigAI2")
                {
                    name = "Large Oilrig";
                    realWidth = 200f;
                }
                else
                {
                    name = Regex.Match(monument.name, @"\w{6}\/(.+\/)(.+)\.(.+)").Groups[2].Value.Replace("_", " ").Replace(" 1", "").Titleize() + " 0";
                }
                if (monPos.ContainsKey(name))
                {
                    if (monPos[name] == monument.transform.position) continue;

                    string newname = name.Remove(name.Length -1, 1) + "1";
                    if (monPos.ContainsKey(newname))
                    {
                        newname = name.Remove(name.Length - 1, 1) + "2";
                    }
                    if (monPos.ContainsKey(newname))
                    {
                        continue;
                    }
                    name = newname;
                }

                if (cavePos.ContainsKey(name))
                {
                    name += RandomString();
                }

                extents = monument.Bounds.extents;
                if (realWidth > 0f)
                {
                    extents.z = realWidth;
                }

                if (monument.name.Contains("cave"))
                {
                    cavePos.Add(name, monument.transform.position);
                }
                else
                {
                    if (extents.z < 1)
                    {
                        extents.z = 50f;
                    }
                    monPos.Add(name, monument.transform.position);
                    monSize.Add(name, extents);
                    //DoLog($"Found monument {name} at {monument.transform.position.ToString()}");
                }
            }
        }
        #endregion

        #region config
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                Options = new Options()
                {
                    defaultHealth = 200f,
                    respawnTimer = 30f,
                    debug = false
                },
                Version = Version
            };
            SaveConfig(config);

            spawnpoints = new Dictionary<string, SpawnPoints>();
            FindMonuments();
            foreach (var mon in monPos)
            {
                spawnpoints.Add(mon.Key, new SpawnPoints()
                {
                    monname = mon.Key,
                    dropWeapon = false,
                    startHealth = 200f,
                    lootable = true,
                    hostile = false,
                    spawnCount = 0,
                    respawnTime = 60f,
                    detectRange = 60f,
                    roamRange = 140f
                });
            }
            SaveData();
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        public class ConfigData
        {
            public Options Options = new Options();
            public VersionNumber Version;
        }

        public class Options
        {
            [JsonProperty(PropertyName = "Default Health")]
            public float defaultHealth;

            [JsonProperty(PropertyName = "Default Respawn Timer")]
            public float respawnTimer;

            public bool debug;
        }
        #endregion
    }
}
