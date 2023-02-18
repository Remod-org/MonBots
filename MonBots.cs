#region License (GPL v2)
/*
    MonBots - NPC Players that protect monuments, sort of
    Copyright (c) 2021-2023 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.0.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v2)
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
    [Info("MonBots", "RFC1920", "1.0.19")]
    [Description("Adds interactive NPCs at various monuments")]
    internal class MonBots : RustPlugin
    {
        #region vars
        [PluginReference]
        private readonly Plugin Kits, ZoneManager;

        private ConfigData configData;
        private const string sci = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab";
        private List<ulong> isopen = new List<ulong>();

        //private const string permNPCGuiUse = "monbot.use";
        private const string NPCGUI = "monbot.editor";
        private const string NPCGUK = "monbot.kitselect";
        private const string NPCGUM = "monbot.monselect";
        private const string NPCGUN = "monbot.kitsetnames";
        private const string NPCGUP = "monbot.newprofile";
        private readonly List<string> guis = new List<string>() { NPCGUI, NPCGUK, NPCGUM, NPCGUN, NPCGUP };

        private bool newsave;
        private bool do1017;

        public static MonBots Instance;
        public SortedDictionary<string, SpawnProfile> spawnpoints = new SortedDictionary<string, SpawnProfile>();
//        private Dictionary<string, MonBotsZoneMap> zonemaps = new Dictionary<string, MonBotsZoneMap>();
        private Dictionary<ulong, MonBotPlayer> hpcacheid = new Dictionary<ulong, MonBotPlayer>();

        private static SortedDictionary<string, Vector3> monPos = new SortedDictionary<string, Vector3>();
        private static SortedDictionary<string, Vector3> monSize = new SortedDictionary<string, Vector3>();
        private static SortedDictionary<string, Vector3> cavePos = new SortedDictionary<string, Vector3>();
        private Dictionary<ulong, Inventories> InvCache = new Dictionary<ulong, Inventories>();

        private readonly static int playerMask = LayerMask.GetMask("Player (Server)");
        #endregion vars

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
        #endregion Message

        #region global
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["add"] = "Add",
                ["addhere"] = "Add Here",
                ["at"] = "at",
                ["cancel"] = "Cancel",
                ["close"] = "Close",
                ["debug"] = "Debug set to {0}",
                ["delete"] = "DELETE",
                ["detectrange"] = "Detect Range",
                ["dropweapon"] = "DropWeapon",
                ["edit"] = "Edit",
                ["editing"] = "Editing",
                ["end"] = "End",
                ["gotospawn"] = "Go There",
                ["hostile"] = "Hostile",
                ["silent"] = "Silence Effects",
                ["invulnerable"] = "Invulnerable",
                ["kit(s)"] = "Kit(s)",
                ["lootable"] = "Lootable",
                ["wipeclothing"] = "Wipe clothing",
                ["wipebelt"] = "Wipe belt",
                ["wipemain"] = "Wipe main",
                ["wipecorpsemain"] = "Wipe corpse main",
                ["monbots"] = "MonBots",
                ["movehere"] = "Move Here",
                ["name"] = "Name",
                ["name(s)"] = "Name(s)",
                ["needselect"] = "Select NPC",
                ["new"] = "Create New",
                ["none"] = "None",
                ["npcgui"] = "MonBot GUI",
                ["npcguikit"] = "MonBot GUI Kit Select",
                ["npcguimon"] = "MonBot GUI Profile Select",
                ["npcguinames"] = "MonBot GUI Bot Names",
                ["profile"] = "Profile",
                ["respawn"] = "Respawn",
                ["respawntime"] = "Respawn Time",
                ["roamrange"] = "Roam Range",
                ["select"] = "Select",
                ["spawncount"] = "Spawn Count",
                ["spawnrange"] = "Spawn Range",
                ["start"] = "Start"
            }, this);
        }

        private void OnServerInitialized()
        {
            LoadConfigVariables();
            Instance = this;

            AddCovalenceCommand("mb", "cmdMB");

            FindMonuments();
            LoadData();
            LoadBots();

            foreach (KeyValuePair<string, Vector3> mon in monPos)
            {
                if (!spawnpoints.ContainsKey(mon.Key))
                {
                    AddProfile(mon.Value, mon.Key);
                }
            }

            //SaveData();

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

        private void OnNewSave() => newsave = true;

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
                    UnityEngine.Object.Destroy(bot);
                }
            }

            foreach (KeyValuePair<string, SpawnProfile> x in new Dictionary<string, SpawnProfile>(spawnpoints))
            {
                x.Value.pos = new List<Vector3>();
                x.Value.ids = new List<ulong>();
            }
            SaveData();
        }

        #region commands
        [Command("mb")]
        private void cmdMB(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsAdmin) return;
            BasePlayer player = iplayer.Object as BasePlayer;
            if (args.Length > 0)
            {
                DoLog(string.Join(",", args));
                switch (args[0])
                {
                    // The b version of the commands here are for the GUI, the non-B are left behind for command line people, if any
                    case "bsc":
                        {
                            if (args.Length > 2)
                            {
                                int intval = int.Parse(args[2]);
                                string monname = Base64Decode(args[1]);
                                spawnpoints[monname].spawnCount = intval;
                                SaveData();
                                NPCProfileEditGUI(player, monname);
                            }
                        }
                        break;
                    case "sc":
                        {
                            int intval = int.Parse(args.Last());
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(newarg.Count - 1);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].spawnCount = intval;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "bsr":
                        {
                            if (args.Length > 2)
                            {
                                int intval = int.Parse(args[2]);
                                string monname = Base64Decode(args[1]);
                                spawnpoints[monname].spawnRange = intval;
                                SaveData();
                                NPCProfileEditGUI(player, monname);
                            }
                        }
                        break;
                    case "sr":
                        {
                            int intval = int.Parse(args.Last());
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(newarg.Count - 1);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].spawnRange = intval;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "brt":
                        {
                            if (args.Length > 2)
                            {
                                int intval = int.Parse(args[2]);
                                string monname = Base64Decode(args[1]);
                                spawnpoints[monname].respawnTime = intval;
                                SaveData();
                                NPCProfileEditGUI(player, monname);
                            }
                        }
                        break;
                    case "rt":
                        {
                            int intval = int.Parse(args.Last());
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(newarg.Count - 1);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].respawnTime = intval;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "bdr":
                        {
                            if (args.Length > 2)
                            {
                                float fval = float.Parse(args[2]);
                                string monname = Base64Decode(args[1]);
                                spawnpoints[monname].detectRange = fval;
                                SaveData();
                                NPCProfileEditGUI(player, monname);
                            }
                        }
                        break;
                    case "dr":
                        {
                            float fval = float.Parse(args.Last());
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(newarg.Count - 1);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].detectRange = fval;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "brr":
                        {
                            if (args.Length > 2)
                            {
                                float fval = float.Parse(args[2]);
                                string monname = Base64Decode(args[1]);
                                spawnpoints[monname].roamRange = fval;
                                SaveData();
                                NPCProfileEditGUI(player, monname);
                            }
                        }
                        break;
                    case "rr":
                        {
                            float fval = float.Parse(args.Last());
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(newarg.Count - 1);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].roamRange = fval;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "rs":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].respawn = !spawnpoints[monname].respawn;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "inv":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].invulnerable = !spawnpoints[monname].invulnerable;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "loot":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].lootable = !spawnpoints[monname].lootable;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "wipec":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].wipeClothing = !spawnpoints[monname].wipeClothing;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "wipeb":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].wipeBelt = !spawnpoints[monname].wipeBelt;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "wipem":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].wipeMain = !spawnpoints[monname].wipeMain;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "wipecm":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].wipeCorpseMain = !spawnpoints[monname].wipeCorpseMain;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "drop":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].dropWeapon = !spawnpoints[monname].dropWeapon;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "hostile":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].hostile = !spawnpoints[monname].hostile;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "silent":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].silent = !spawnpoints[monname].silent;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "names":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);

                            NPCNamesGUI(player, monname);
                        }
                        break;
                    case "name":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(newarg.Count - 1);
                            string monname = string.Join(" ", newarg);
                            SpawnProfile sp = spawnpoints[monname];
                            string botname = args[1];
                            string newname = args.Last();

                            if (sp.names == null || sp.names.Count == 0)
                            {
                                sp.names = new List<string>();
                            }
                            if (botname == "NEW_NAME" && !sp.names.Contains(newname))
                            {
                                sp.names.Add(newname);
                            }
                            else if (sp.names.Contains(botname))
                            {
                                sp.names.Remove(botname);
                                if (newname != "DELETE_ME")
                                {
                                    sp.names.Add(newname);
                                }
                            }
                            SaveData();
                            NPCNamesGUI(player, monname);
                        }
                        break;
                    case "delete":
                        {
                            CuiHelper.DestroyUi(player, NPCGUI);
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);

                            foreach (MonBotPlayer bot in UnityEngine.Object.FindObjectsOfType<MonBotPlayer>())
                            {
                                if (bot.info.monument == monname)
                                {
                                    DoLog($"Killing bot {bot.info.displayName} at {monname}");
                                    UnityEngine.Object.Destroy(bot);
                                }
                            }

                            spawnpoints.Remove(monname);
                            SaveData();
                            NPCProfileSelectGUI(player);
                        }
                        break;
                    case "gothere":
                        {
                            CuiHelper.DestroyUi(player, NPCGUI);
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            Vector3 newpos = spawnpoints[monname].monpos;
                            newpos.y = TerrainMeta.HeightMap.GetHeight(newpos);
                            Teleport(player, newpos);
                        }
                        break;
                    case "spawnhere":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            spawnpoints[monname].monpos = player.transform.position;
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "respawn":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);

                            foreach (MonBotPlayer bot in UnityEngine.Object.FindObjectsOfType<MonBotPlayer>())
                            {
                                if (bot.info.monument == monname)
                                {
                                    DoLog($"Killing bot {bot.info.displayName} at {monname}");
                                    UnityEngine.Object.Destroy(bot);
                                }
                            }
                            LoadBots(monname);

                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "selkit":
                        {
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            NPCKitSelectGUI(player, monname);
                        }
                        break;
                    case "kitsel":
                        if (args.Length > 2)
                        {
                            CuiHelper.DestroyUi(player, NPCGUK);
                            string kit = args[1];
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            DoLog($"Checking kits for '{monname}'");
                            if (spawnpoints[monname].kits == null || spawnpoints[monname].kits.Count == 0)
                            {
                                spawnpoints[monname].kits = new List<string>();
                            }
                            if (spawnpoints[monname].kits.Contains(kit))
                            {
                                spawnpoints[monname].kits.Remove(kit);
                            }
                            else
                            {
                                spawnpoints[monname].kits.Add(kit);
                            }
                            SaveData();
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "addprofile":
                        {
                            NPCNewProfileGUI(player);
                        }
                        break;
                    case "newprofile":
                        {
                            CuiHelper.DestroyUi(player, NPCGUP);
                            AddProfile(player.transform.position, args[1]);
                            NPCProfileEditGUI(player, args[1]);
                        }
                        break;
                    case "monsel":
                        if (args.Length > 1)
                        {
                            CuiHelper.DestroyUi(player, NPCGUM);
                            List<string> newarg = new List<string>(args);
                            newarg.RemoveAt(0);
                            string monname = string.Join(" ", newarg);
                            NPCProfileEditGUI(player, monname);
                        }
                        break;
                    case "newprofileclose":
                        CuiHelper.DestroyUi(player, NPCGUP);
                        break;
                    case "newprofilecancel":
                        CuiHelper.DestroyUi(player, NPCGUP);
                        NPCProfileSelectGUI(player);
                        break;
                    case "namesclose":
                        CuiHelper.DestroyUi(player, NPCGUN);
                        break;
                    case "selkitclose":
                        CuiHelper.DestroyUi(player, NPCGUK);
                        break;
                    case "selmonclose":
                        CuiHelper.DestroyUi(player, NPCGUM);
                        break;
                    case "close":
                        IsOpen(player.userID, false);
                        CuiHelper.DestroyUi(player, NPCGUI);
                        CuiHelper.DestroyUi(player, NPCGUK);
                        CuiHelper.DestroyUi(player, NPCGUM);
                        break;
                }
            }
            else
            {
                foreach (string gui in guis)
                {
                    CuiHelper.DestroyUi(player, gui);
                }
                NPCProfileSelectGUI(player);
            }
        }
        #endregion commands

        private void SpawnBot(ulong userid)
        {
            // Used to identify bot for respawn on death
            if (hpcacheid.ContainsKey(userid))
            {
                DoLog($"{userid} found in cached ids!");
                MonBotPlayer botinfo = hpcacheid[userid];
                SpawnProfile sp = spawnpoints[botinfo.info.monument];
                DoLog($"Respawning bot {botinfo.info.displayName} at {sp.monname}");
                SpawnBot(sp, botinfo.spawnPos, botinfo.info.kit);
            }
            else
            {
                DoLog($"{userid} not found in cached ids :(");
            }
        }

        private void SpawnBot(SpawnProfile sp, Vector3 pos, string kit)
        {
            // Used for initial spawn by LoadBots()
            if (pos == default(Vector3)) return;
            pos = AdjustSpawnPoint(pos, sp.roamRange / 2);
            spawnpoints[sp.monname].pos.Add(pos);
            DoLog($"Spawning bot at {pos}");
            global::HumanNPC bot = (global::HumanNPC)GameManager.server.CreateEntity(sci, pos, new Quaternion(), true);
            bot.Spawn();
            spawnpoints[sp.monname].ids.Add(bot.userID);

            string botname = "Bot";
            if (sp.names != null)
            {
                botname = GetBotName(sp.names.ToArray());
            }

            NextTick(() =>
            {
                DoLog($"Adding Mono to bot {botname} at {sp.monname} ({pos})");
                try
                {
                    MonBotPlayer mono = bot.gameObject.AddComponent<MonBotPlayer>();
                    mono.spawnPos = pos;

                    DoLog("Setting info object");
                    mono.info = new MonBotInfo(bot.userID, bot.transform.position, bot.transform.rotation)
                    {
                        displayName = botname,
                        userid = bot.userID,
                        monument = sp.monname,
                        lootable = sp.lootable,
                        wipeClothing = sp.wipeClothing,
                        wipeBelt = sp.wipeBelt,
                        wipeMain = sp.wipeMain,
                        wipeCorpseMain = sp.wipeCorpseMain,
                        health = sp.startHealth,
                        invulnerable = sp.invulnerable,
                        hostile = sp.hostile,
                        silent = sp.silent,
                        dropWeapon = sp.dropWeapon,
                        detectRange = sp.detectRange,
                        roamRange = sp.roamRange,
                        respawnTimer = sp.respawnTime,
                        respawn = sp.respawn,
                        loc = pos,
                        kit = kit
                    };

                    //mono.UpdateHealth(mono.info);
                    bot.startHealth = sp.startHealth;

                    DoLog("Setting brain object");
                    bot.Brain.Navigator.Agent.agentTypeID = -1372625422;
                    bot.Brain.Navigator.DefaultArea = "Walkable";
                    bot.Brain.Navigator.Init(bot, bot.Brain.Navigator.Agent);
                    bot.Brain.ForceSetAge(0);
                    bot.Brain.TargetLostRange = mono.info.detectRange;
                    bot.Brain.HostileTargetsOnly = !mono.info.hostile;
                    bot.Brain.Navigator.BestCoverPointMaxDistance = mono.info.roamRange / 2;
                    bot.Brain.Navigator.BestRoamPointMaxDistance = mono.info.roamRange;
                    bot.Brain.Navigator.MaxRoamDistanceFromHome = mono.info.roamRange;
                    bot.Brain.Senses.Init(bot, bot.Brain, 5f, mono.info.roamRange, mono.info.detectRange, -1f, true, false, true, mono.info.detectRange, !mono.info.hostile, false, true, EntityType.Player, false);

                    DoLog("Setting name and inventory");
                    bot.displayName = botname;
                    hpcacheid.Add(bot.userID, mono);
                    if (kit.Length > 0)
                    {
                        bot.inventory.Strip();
                        Kits?.Call("GiveKit", bot, kit);
                    }

                    if (mono.info.silent)
                    {
                        DoLog("Silencing effects");
                        ScientistNPC npc = bot as ScientistNPC;
                        npc.DeathEffects = new GameObjectRef[0];
                        npc.RadioChatterEffects = new GameObjectRef[0];
                        npc.radioChatterType = ScientistNPC.RadioChatterType.NONE;
                        npc.SetChatterType(ScientistNPC.RadioChatterType.NONE);
                    }
                    timer.Once(5f, () => mono.activeItem = bot.GetActiveItem());
                }
                catch //(Exception ex)
                {
                    DoLog($"Unable to setup bot {botname} at {sp.monname} ({pos}) - Possible navmesh issue.");
                }
            });

            if (bot.IsHeadUnderwater())
            {
                bot.Kill();
            }
        }

        private void LoadBots(string profile = "", int quantity = 0)
        {
            DoLog("LoadBots called");
            foreach (KeyValuePair<string, SpawnProfile> sp in new Dictionary<string, SpawnProfile>(spawnpoints))
            {
                if (profile.Length > 0 && !sp.Key.Equals(profile)) continue;

                Vector3 spawnPos = sp.Value.monpos;
                if (newsave && monPos.ContainsKey(sp.Key))
                {
                    DoLog($"Changing {sp.Key} location due to wipe to {spawnPos}");
                    spawnPos = monPos[sp.Key];
                }
                else if (newsave && !monPos.ContainsKey(sp.Key) && sp.Value.spawnCount > 0)
                {
                    Puts($"Server wipe was detected.  Will not spawn bots for profile '{sp.Key}' since it was either custom or the monument does not exist.");
                    continue;
                }

                int spawnqty = quantity > 0 ? quantity : sp.Value.spawnCount;
                if (spawnqty > 0)
                {
                    DoLog($"Working on spawn at {sp.Key}");
                    for (int i = 0; i < spawnqty; i++)
                    {
                        string kit;
                        if (sp.Value.kits == null)
                        {
                            kit = "";
                        }
                        else if (sp.Value.kits.Count == 1)
                        {
                            kit = sp.Value.kits[0];
                        }
                        else
                        {
                            kit = sp.Value.kits.GetRandom();
                        }

                        SpawnBot(sp.Value, spawnPos, kit);
                        SaveData();
                    }
                }
            }
        }

        private void LoadBots(Vector3 location, string profile, string group, int quantity = 0)
        {
            DoLog("LoadBots by location called");
            foreach (KeyValuePair<string, SpawnProfile> sp in new Dictionary<string, SpawnProfile>(spawnpoints))
            {
                if (!sp.Key.Equals(profile)) continue;

                sp.Value.zonemap.Add(group);
                Vector3 spawnPos = location;

                if (quantity > 0)
                {
                    DoLog($"Working on location spawn at {sp.Key}");
                    for (int i = 0; i < quantity; i++)
                    {
                        string kit;
                        if (sp.Value.kits == null)
                        {
                            kit = "";
                        }
                        else if (sp.Value.kits.Count == 1)
                        {
                            kit = sp.Value.kits[0];
                        }
                        else
                        {
                            kit = sp.Value.kits.GetRandom();
                        }

                        SpawnBot(sp.Value, spawnPos, kit);
                        SaveData();
                    }
                }
            }
        }

        private bool BadLocation(Vector3 location)
        {
            // Avoid placing in a rock or foundation, water, etc.
            int layerMask = LayerMask.GetMask("Construction", "World", "Water");
            RaycastHit hit;
            if (Physics.Raycast(new Ray(location, Vector3.down), out hit, 6f, layerMask))
            {
                return true;
            }
            else if (Physics.Raycast(new Ray(location, Vector3.up), out hit, 6f, layerMask))
            {
                return true;
            }
            else if (Physics.Raycast(new Ray(location, Vector3.forward), out hit, 6f, layerMask))
            {
                return true;
            }
            return false;
            //return (TerrainMeta.HeightMap.GetHeight(location) - TerrainMeta.WaterMap.GetHeight(location)) >= 0;
        }

        private Vector3 AdjustSpawnPoint(Vector3 pos, float radius)
        {
            Vector3 newpos = new Vector3() { x = pos.x, y = pos.y, z = pos.z };
            Vector2 rand;
            bool ok = false;
            int i = 0;
            const int max = 250;

            while (!ok)
            {
                i++;
                rand = UnityEngine.Random.insideUnitCircle * radius;
                newpos = pos + new Vector3(rand.x, 0, rand.y);
                ok = !BadLocation(newpos);

                if (ok || i >= max)
                {
                    newpos.y = TerrainMeta.HeightMap.GetHeight(newpos);
                    if (i >= max)
                    {
                        DoLog($"Found meh spawn position after maxing out on checks at {max}");
                    }
                    else
                    {
                        DoLog($"Found adequate spawn position after {i} check(s)");
                    }
                    newpos.y = TerrainMeta.HeightMap.GetHeight(newpos);
                    return newpos;
                }
            }
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            return pos;
        }

        private string GetBotName(string[] names)
        {
            if (names.Length == 1 && !string.IsNullOrEmpty(names[0]))
            {
                return names[0];
            }
            else if (names.Length > 1)
            {
                return names.GetRandom();
            }
            return "Bot";
        }
        #endregion global

        #region Oxide Hooks
        private void LoadData()
        {
            spawnpoints = Interface.Oxide.DataFileSystem.ReadObject<SortedDictionary<string, SpawnProfile>>(Name + "/spawnpoints");

            foreach (KeyValuePair<string, SpawnProfile> sp in spawnpoints)
            {
                DoLog($"Loaded profile {sp.Key}");
                if (monPos.ContainsKey(sp.Key))
                {
                    sp.Value.monpos = monPos[sp.Key];
                }
                if (do1017)
                {
                    DoLog("Patching profile for 1.0.17 upgrade");
                    sp.Value.wipeClothing = true;
                    sp.Value.wipeBelt = true;
                }
            }
            do1017 = false;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/spawnpoints", spawnpoints);
        }

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

        private void OnEntityDeath(global::HumanNPC humannpc, HitInfo hitinfo)
        {
            if (humannpc == null) return;
            MonBotPlayer npc = humannpc.GetComponent<MonBotPlayer>();
            if (npc == null) return;
            DoLog("OnEntityDeath: Found MonBot player");

            hpcacheid.Remove(humannpc.net.ID);

            if (npc.info.dropWeapon)
            {
                DoLog("OnEntityDeath: Attempting to drop weapon");
                if (npc.activeItem != null)
                {
                    DoLog($"Dropping {npc.info.displayName}'s activeItem: {npc.activeItem.info.shortname}");
                    Vector3 vector3 = new Vector3(UnityEngine.Random.Range(-2f, 2f), 0.2f, UnityEngine.Random.Range(-2f, 2f));
                    npc.activeItem.Drop(humannpc.GetDropPosition(), humannpc.GetInheritedDropVelocity() + (vector3.normalized * 3f));
                    humannpc.svActiveItemID = 0;
                }
            }

            DoLog($"Populating inventory cache for {npc.info.userid}");
            ItemContainer[] sourceinv = { humannpc.inventory.containerMain, humannpc.inventory.containerWear, humannpc.inventory.containerBelt };
            InvCache.Add(humannpc.userID, new Inventories());
            for (int i = 0; i < sourceinv.Length; i++)
            {
                foreach (Item item in sourceinv[i].itemList)
                {
                    InvCache[humannpc.userID].inventory[i].Add(new ItemInfo
                    {
                        ID = item.info.itemid,
                        amount = item.amount,
                        skinID = item.skin
                    });
                }
            }
            DoLog($"DONE populating inventory cache for {npc.info.userid}");

            DoLog("Checking respawn variable");
            if (npc?.info.respawn == true)
            {
                DoLog($"Setting {npc.info.respawnTimer} second respawn timer for {npc.info.displayName} ({npc.info.userid})");
                timer.Once(npc.info.respawnTimer, () => SpawnBot(npc.info.userid));
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

                timer.Once(0.1f, () =>
                {
                    DoLog("Checking corpse lootable flag");
                    if (npc.info.lootable && InvCache.ContainsKey(userid))
                    {
                        for (int i = 0; i < InvCache[userid].inventory.Length; i++)
                        {
                            // For main, if wipe is true, the corpse will get a default loot collection rather than what the NPC had
                            if ((corpse.containers[i].capacity == 24 && !npc.info.wipeMain) ||
                                (corpse.containers[i].capacity == 7 && !npc.info.wipeClothing) ||
                                (corpse.containers[i].capacity == 6 && !npc.info.wipeBelt))
                            {
                                DoLog($"Copying cached items back from live NPC for container {i}");
                                foreach (ItemInfo item in InvCache[userid].inventory[i])
                                {
                                    Item giveItem = ItemManager.CreateByItemID(item.ID, item.amount, item.skinID);
                                    DoLog($"Copying {item.amount} of item {giveItem.info.displayName.english}");
                                    if (!giveItem.MoveToContainer(corpse.containers[i], -1, true))
                                    {
                                        DoLog("Failed to move item :(");
                                        giveItem.Remove();
                                    }
                                }
                            }
                        }
                        if (npc.info.wipeCorpseMain)
                        {
                            DoLog("Wiping corpse main inventory");
                            corpse.containers[0].Clear();
                        }
                        timer.Once(5f, () => InvCache.Remove(userid));
                        ItemManager.DoRemoves();
                        return;
                    }
                    for (int i = 0; i < corpse.containers.Length; i++)
                    {
                        DoLog($"Clearing corpse container {i}");
                        corpse.containers[i].Clear();
                    }
                    ItemManager.DoRemoves();
                });
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

                DoLog($"Player {player.displayName}:{player.UserIDString} looting MonBot {corpse.name}:{corpse.playerSteamID}");
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
        #endregion Oxide Hooks

        #region Our Inbound Hooks
        private bool IsMonBot(ScientistNPC player) => player.GetComponentInParent<MonBotPlayer>() != null;

        // For DynamicPVP, etc.
        private string[] AddGroupSpawn(Vector3 location, string profileName, string group)
        {
            if (location == new Vector3() || profileName == null || group == null)
            {
                return new string[] { "error", "Null parameter" };
            }

            KeyValuePair<string, SpawnProfile> profile = spawnpoints.FirstOrDefault(x => x.Value.zonemap.Contains(group));
            if (profile.Key != null && string.Equals(profile.Key, profileName, StringComparison.CurrentCultureIgnoreCase))
            {
                if (profile.Value.spawnCount == 0)
                {
                    return new string[] { "false", "Target spawn amount is zero.}" };
                }

                timer.Repeat(1f, profile.Value.spawnCount, () => LoadBots(location, profile.Key, group.ToLower(), profile.Value.spawnCount));
                return new string[] { "true", "Group successfully added" };
            }
            return new string[] { "false", "Group add failed - Check profile name and try again" };
        }

        private string[] RemoveGroupSpawn(string group)
        {
            if (group == null)
            {
                return new string[] { "error", "No group specified." };
            }

            bool flag = false;
            KeyValuePair<string, SpawnProfile> profile = spawnpoints.FirstOrDefault(x => x.Value.zonemap.Contains(group));
            if (profile.Key != null)
            {
                foreach (ulong botid in profile.Value.ids)
                {
                    flag = true;
                    BaseNetworkable.serverEntities.Find((uint)botid)?.Kill();
                }
            }
            profile.Value.zonemap.Remove(group);
            SaveData();
            return flag ? new string[] { "true", $"Group {group} was destroyed." } : new string[] { "true", $"There are no bots in group {group}" };
        }

        private string[] _AddGroupSpawn(Vector3 location, string profileName, string group, int quantity)
        {
            if (!spawnpoints.ContainsKey(group))
            {
                quantity = quantity > 0 ? quantity : 5;
                AddProfile(location, profileName, group, quantity);
            }
            LoadBots(group, quantity);
            string[] ids = new string[quantity];
            int i = 0;
            foreach (ulong id in spawnpoints[group].ids)
            {
                ids[i] = id.ToString();
                i++;
            }
            return ids;
        }

        // For DynamicPVP
        private string[] _RemoveGroupSpawn(string group)
        {
            if (!spawnpoints.ContainsKey(group))
            {
                return null;
            }
            string[] ids = new string[spawnpoints[group].ids.Count];
            int i = 0;
            foreach (ulong id in spawnpoints[group].ids)
            {
                // Remove the bots
                BaseNetworkable.serverEntities.Find((uint)id)?.Kill();
                ids[i] = id.ToString();
                i++;
            }

            spawnpoints.Remove(group);
            return ids;
        }

        private string GetMonBotName(ulong npcid)
        {
            DoLog($"Looking for monbot: {npcid}");
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
            DoLog($"SetMonBotInfo called for {npcid} {toset},{data}");
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
                case "wipeclothing":
                    hp.info.wipeClothing = !GetBoolValue(data);
                    break;
                case "wipebelt":
                    hp.info.wipeBelt = !GetBoolValue(data);
                    break;
                case "wipemain":
                    hp.info.wipeMain = !GetBoolValue(data);
                    break;
                case "wipecorpsemain":
                    hp.info.wipeCorpseMain = !GetBoolValue(data);
                    break;
                case "hostile":
                    hp.info.hostile = !GetBoolValue(data);
                    break;
                case "silent":
                    hp.info.silent = !GetBoolValue(data);
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
                    hp.info.respawn = !GetBoolValue(data);
                    break;
                case "respawnTimer":
                case "respawntimer":
                    hp.info.respawnTimer = Convert.ToSingle(data);
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
            //RespawnNPC(hp.player);
        }

        private void GiveMonBot(global::HumanNPC bot, string itemname, string loc = "wear", ulong skinid = 0, int count = 1)
        {
            DoLog($"GiveMonBot called: {bot.displayName}, {itemname}, {loc}");
            MonBotPlayer npc = bot.GetComponent<MonBotPlayer>();
            if (npc != null)
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
        #endregion Our Inbound Hooks

        #region GUI
        private void NPCProfileSelectGUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, NPCGUM);

            string description = Lang("npcguimon");
            CuiElementContainer container = UI.Container(NPCGUM, UI.Color("242424", 1f), "0.1 0.1", "0.9 0.9", true, "Overlay");
            UI.Label(ref container, NPCGUM, UI.Color("#ffffff", 1f), description, 18, "0.23 0.92", "0.7 1");
            UI.Button(ref container, NPCGUM, UI.Color("#d85540", 1f), Lang("close"), 12, "0.92 0.95", "0.985 0.98", "mb selmonclose");

            int col = 0; int row = 0;

            float[] posb = GetButtonPositionP(row, col);
            if (spawnpoints.Count > 0)
            {
                foreach (string profile in spawnpoints.Keys)
                {
                    if (row > 13)
                    {
                        row = 0;
                        col++;
                    }

                    posb = GetButtonPositionP(row, col);
                    string color = "#d85540";
                    if (!monPos.ContainsKey(profile))
                    {
                        color = "#5540d8";
                    }
                    else if (spawnpoints[profile].spawnCount > 0)
                    {
                        color = "#55d840";
                    }
                    else if (spawnpoints[profile].names != null && spawnpoints[profile].kits != null)
                    {
                        color = "#555555";
                    }

                    UI.Button(ref container, NPCGUM, UI.Color(color, 1f), profile, 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb monsel {profile}");

                    row++;
                }
            }
            // Add
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Button(ref container, NPCGUM, UI.Color("#ff4040", 1f), Lang("addhere"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", "mb addprofile");

            CuiHelper.AddUi(player, container);
        }

        private void NPCProfileEditGUI(BasePlayer player, string profile)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, NPCGUI);

            if (!spawnpoints.ContainsKey(profile)) return;
            SpawnProfile sp = spawnpoints[profile];
            string description = Lang("npcgui") + ": " + profile + " " + Lang("profile");
            CuiElementContainer container = UI.Container(NPCGUI, UI.Color("242424", 1f), "0.1 0.1", "0.9 0.9", true, "Overlay");
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), description, 18, "0.23 0.92", "0.7 1");
            //if (!monPos.ContainsKey(profile))
            //{
            UI.Button(ref container, NPCGUI, UI.Color("#ff4040", 1f), Lang("delete"), 12, "0.71 0.95", "0.77 0.98", $"mb delete {profile}");
            UI.Button(ref container, NPCGUI, UI.Color("#4055d8", 1f), Lang("movehere"), 12, "0.78 0.02", "0.84 0.06", $"mb spawnhere {profile}");
            UI.Button(ref container, NPCGUI, UI.Color("#40d855", 1f), Lang("gotospawn"), 12, "0.85 0.02", "0.91 0.06", $"mb gothere {profile}");
            //}
            UI.Button(ref container, NPCGUI, UI.Color("#ff4040", 1f), Lang("respawn"), 12, "0.78 0.95", "0.84 0.98", $"mb respawn {profile}");
            UI.Button(ref container, NPCGUI, UI.Color("#5540d8", 1f), Lang("select"), 12, "0.85 0.95", "0.91 0.98", "mb");
            UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), Lang("close"), 12, "0.92 0.95", "0.985 0.98", "mb close");

            int col = 0;
            int row = 0;
            float[] posb = GetButtonPositionP(row, col);

            string bprofile = Base64Encode(profile);

            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("spawncount"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("spawnrange"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("respawn"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("respawntime"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("detectrange"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("roamrange"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("invulnerable"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("lootable"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");

            row++; col++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("wipeclothing"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            col += 2;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("wipebelt"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            col += 2;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("wipemain"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");

            row++;
            posb = GetButtonPositionP(row, 5);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("wipecorpsemain"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");

            row++; col = 0;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("dropweapon"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("hostile"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");

            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("silent"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");

            col = 3; row = 0;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("name(s)"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), Lang("kit(s)"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");

            col = 1; row = 0;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#535353", 1f), sp.spawnCount.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            UI.Input(ref container, NPCGUI, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb bsc {bprofile} ");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#535353", 1f), sp.spawnRange.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            UI.Input(ref container, NPCGUI, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb bsr {bprofile} ");
            row++;
            posb = GetButtonPositionP(row, col);
            if (sp.respawn)
            {
                UI.Button(ref container, NPCGUI, UI.Color("#55d840", 1f), sp.respawn.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb brs {bprofile}");
            }
            else
            {
                UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.respawn.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb brs {bprofile}");
            }
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#535353", 1f), sp.respawnTime.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            UI.Input(ref container, NPCGUI, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb brt {bprofile} ");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#535353", 1f), sp.detectRange.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            UI.Input(ref container, NPCGUI, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb bdr {bprofile} ");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Label(ref container, NPCGUI, UI.Color("#535353", 1f), sp.roamRange.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            UI.Input(ref container, NPCGUI, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb brr {bprofile} ");
            row++;
            posb = GetButtonPositionP(row, col);
            if (sp.invulnerable)
            {
                UI.Button(ref container, NPCGUI, UI.Color("#55d840", 1f), sp.invulnerable.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb inv {profile}");
            }
            else
            {
                UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.invulnerable.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb inv {profile}");
            }
            row++;
            posb = GetButtonPositionP(row, col);
            if (sp.lootable)
            {
                UI.Button(ref container, NPCGUI, UI.Color("#55d840", 1f), sp.lootable.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb loot {profile}");
                int newcol = col + 1;
                row++;
                posb = GetButtonPositionP(row, newcol);
                string newcolor = sp.wipeClothing ? "#55d840" : "#d85540";
                UI.Button(ref container, NPCGUI, UI.Color(newcolor, 1f), sp.wipeClothing.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb wipec {profile}");
                newcol += 2;
                posb = GetButtonPositionP(row, newcol);
                newcolor = sp.wipeBelt ? "#55d840" : "#d85540";
                UI.Button(ref container, NPCGUI, UI.Color(newcolor, 1f), sp.wipeBelt.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb wipeb {profile}");
                newcol += 2;
                posb = GetButtonPositionP(row, newcol);
                newcolor = sp.wipeMain ? "#55d840" : "#d85540";
                UI.Button(ref container, NPCGUI, UI.Color(newcolor, 1f), sp.wipeMain.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb wipem {profile}");
                row++;
                posb = GetButtonPositionP(row, 6);
                newcolor = sp.wipeCorpseMain ? "#55d840" : "#d85540";
                UI.Button(ref container, NPCGUI, UI.Color(newcolor, 1f), sp.wipeCorpseMain.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb wipecm {profile}");
            }
            else
            {
                UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.lootable.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb loot {profile}");

                int newcol = col + 1;
                row++;
                posb = GetButtonPositionP(row, newcol);
                UI.Label(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.wipeClothing.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
                newcol += 2;
                posb = GetButtonPositionP(row, newcol);
                UI.Label(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.wipeBelt.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
                newcol += 2;
                posb = GetButtonPositionP(row, newcol);
                UI.Label(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.wipeMain.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
                row++;
                posb = GetButtonPositionP(row, 6);
                UI.Label(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.wipeCorpseMain.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            }
            row++;
            posb = GetButtonPositionP(row, col);
            if (sp.dropWeapon)
            {
                UI.Button(ref container, NPCGUI, UI.Color("#55d840", 1f), sp.dropWeapon.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb drop {profile}");
            }
            else
            {
                UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.dropWeapon.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb drop {profile}");
            }
            row++;
            posb = GetButtonPositionP(row, col);
            if (sp.hostile)
            {
                UI.Button(ref container, NPCGUI, UI.Color("#55d840", 1f), sp.hostile.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb hostile {profile}");
            }
            else
            {
                UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.hostile.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb hostile {profile}");
            }
            row++;
            posb = GetButtonPositionP(row, col);
            if (sp.silent)
            {
                UI.Button(ref container, NPCGUI, UI.Color("#55d840", 1f), sp.silent.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb silent {profile}");
            }
            else
            {
                UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), sp.silent.ToString(), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb silent {profile}");
            }

            col = 4;
            row = 0;

            posb = GetButtonPositionP(row, col);
            UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), Lang("edit"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb names {profile}");
            row++;
            posb = GetButtonPositionP(row, col);
            UI.Button(ref container, NPCGUI, UI.Color("#d85540", 1f), Lang("edit"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb selkit {profile}");

            col++; row = 0;
            posb = GetButtonPositionP(row, col);
            if (sp.names != null)
            {
                string names = string.Join(" ", sp.names);
                UI.Label(ref container, NPCGUI, UI.Color("#535353", 1f), names, 12, $"{posb[0]} {posb[1]}", $"{posb[0] + posb[2] - posb[0]} {posb[3]}");
            }

            row++;
            posb = GetButtonPositionP(row, col);
            if (sp.kits != null)
            {
                string kits = string.Join(" ", sp.kits);
                UI.Label(ref container, NPCGUI, UI.Color("#535353", 1f), kits, 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            }

            string footer = Lang("editing") + " " + profile + " " + Lang("at") + " " + sp.monpos.ToString();
            UI.Label(ref container, NPCGUI, UI.Color("#ffffff", 1f), footer, 12, "0.23 0.02", "0.77 0.06");

            CuiHelper.AddUi(player, container);
        }

        private void NPCNewProfileGUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, NPCGUP);

            string description = Lang("npcgui");
            CuiElementContainer container = UI.Container(NPCGUP, UI.Color("242424", 1f), "0.1 0.1", "0.9 0.9", true, "Overlay");
            UI.Label(ref container, NPCGUP, UI.Color("#ffffff", 1f), description, 18, "0.23 0.92", "0.7 1");
            UI.Button(ref container, NPCGUP, UI.Color("#d85540", 1f), Lang("cancel"), 12, "0.86 0.95", "0.91 0.98", "mb newprofilecancel");
            UI.Button(ref container, NPCGUP, UI.Color("#d85540", 1f), Lang("close"), 12, "0.92 0.95", "0.985 0.98", "mb newprofileclose");

            const int col = 0; const int row = 0;
            float[] posb = GetButtonPositionP(row, col);

            UI.Label(ref container, NPCGUP, UI.Color("#535353", 1f), Lang("new"),12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            UI.Input(ref container, NPCGUP, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", "mb newprofile ");

            CuiHelper.AddUi(player, container);
        }

        private void NPCNamesGUI(BasePlayer player, string profile)
        {
            CuiHelper.DestroyUi(player, NPCGUN);

            string description = Lang("npcguinames") + ": " + profile;
            CuiElementContainer container = UI.Container(NPCGUN, UI.Color("242424", 1f), "0.1 0.1", "0.9 0.9", true, "Overlay");
            UI.Label(ref container, NPCGUN, UI.Color("#ffffff", 1f), description, 18, "0.23 0.92", "0.7 1");
            UI.Button(ref container, NPCGUN, UI.Color("#d85540", 1f), Lang("close"), 12, "0.92 0.95", "0.985 0.98", "mb namesclose");

            SpawnProfile sp = spawnpoints[profile];
            int col = 0; int row = 0;

            float[] posb = GetButtonPositionP(row, col);
            if (sp.names != null)
            {
                foreach (string nom in sp.names)
                {
                    if (row > 10)
                    {
                        row = 0;
                        col++; col++;
                    }
                    posb = GetButtonPositionP(row, col);

                    UI.Label(ref container, NPCGUN, UI.Color("#535353", 1f), nom, 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
                    UI.Input(ref container, NPCGUN, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb name {nom} {profile} ");
                    col++;
                    posb = GetButtonPositionP(row, col);
                    UI.Button(ref container, NPCGUN, UI.Color("#ff4040", 1f), Lang("delete"), 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb name {nom} {profile} DELETE_ME");
                    col--;
                    row++;
                }

                row++;
                posb = GetButtonPositionP(row, col);
            }
            UI.Label(ref container, NPCGUN, UI.Color("#535353", 1f), Lang("new"),12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}");
            UI.Input(ref container, NPCGUN, UI.Color("#ffffff", 1f), "", 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb name NEW_NAME {profile} ");

            CuiHelper.AddUi(player, container);
        }

        private void NPCKitSelectGUI(BasePlayer player, string profile)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, NPCGUK);

            string description = Lang("npcguikit") + ": " + profile;
            CuiElementContainer container = UI.Container(NPCGUK, UI.Color("242424", 1f), "0.1 0.1", "0.9 0.9", true, "Overlay");
            UI.Label(ref container, NPCGUK, UI.Color("#ffffff", 1f), description, 18, "0.23 0.92", "0.7 1");
            UI.Button(ref container, NPCGUK, UI.Color("#d85540", 1f), Lang("close"), 12, "0.92 0.95", "0.985 0.98", "mb selkitclose");

            SpawnProfile sp = spawnpoints[profile];
            int col = 0;
            int row = 0;

            List<string> kits = new List<string>();
            Kits?.CallHook("GetKitNames", kits);
            foreach (string kitinfo in kits)
            {
                if (row > 10)
                {
                    row = 0;
                    col++;
                }
                float[] posb = GetButtonPositionP(row, col);

                if (sp.kits?.Contains(kitinfo) == true)
                {
                    UI.Button(ref container, NPCGUK, UI.Color("#55d840", 1f), kitinfo, 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb kitsel {kitinfo} {profile}");
                }
                else
                {
                    UI.Button(ref container, NPCGUK, UI.Color("#424242", 1f), kitinfo, 12, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"mb kitsel {kitinfo} {profile}");
                }
                row++;
            }

            CuiHelper.AddUi(player, container);
        }

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
        #endregion GUI

        #region utility
        private MonBotPlayer FindMonBotByID(ulong userid, bool playerid = false)
        {
            MonBotPlayer hp;
            if (hpcacheid.TryGetValue(userid, out hp))
            {
                DoLog($"Found matching NPC for userid {userid} in cache");
                return hp;
            }
            foreach (MonBotPlayer humanplayer in Resources.FindObjectsOfTypeAll<MonBotPlayer>())
            {
                DoLog($"Is {humanplayer.player.displayName} a MonBot?");
                if (humanplayer.player.userID != userid)
                {
                    continue;
                }

                DoLog($"Found matching NPC for userid {userid}");
                return hpcacheid[userid];
            }
            return null;
        }

        private BasePlayer FindPlayerByID(ulong userid)
        {
            DoLog($"Searching for player object with userid {userid}");
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

        private void AddProfile(Vector3 location, string profile, string mongroupname="", int quantity=0)
        {
            // monname could be a monument/profile name or the string value of a ZoneManager zoneid, etc., if mongroupname is set.
            // Er... maybe?
            // Or, profile should be the mongroupname if set, defaulting to profile name if not set.
            spawnpoints.Add(profile, new SpawnProfile()
            {
                monname = mongroupname.Length > 0 ? mongroupname : profile,
                monpos = location,
                respawn = true,
                respawnTime = configData.Options.respawnTimer,
                startHealth = configData.Options.defaultHealth,
                lootable = true,
                wipeClothing = true,
                wipeBelt = true,
                wipeMain = false,
                wipeCorpseMain = false,
                hostile = false,
                silent = true,
                invulnerable = false,
                dropWeapon = false,
                spawnCount = quantity,
                spawnRange = 30,
                detectRange = 60f,
                roamRange = 140f,
                names = new List<string>(),
                ids = new List<ulong>(),
                pos = new List<Vector3>()
            });
            SaveData();
        }
        #endregion utility

        #region classes
        public class Inventories
        {
            // Used to store inventory on death for selective restoration into the corpse
            public List<ItemInfo>[] inventory = { new List<ItemInfo>(), new List<ItemInfo>(), new List<ItemInfo>() };
        }

        public class ItemInfo
        {
            // Used by Inventories to store item info for selective recreation in the corpse
            public int ID;
            public int amount;
            public ulong skinID;
        }

        public class SpawnProfile
        {
            public string monname;
            public Vector3 monpos;
            public int spawnCount;
            public int spawnRange;
            public float respawnTime;
            public float detectRange;
            public float roamRange;
            public float startHealth;
            public bool invulnerable;
            public bool respawn;
            public bool lootable;
            public bool wipeClothing;
            public bool wipeBelt;
            public bool wipeMain;
            public bool wipeCorpseMain;
            public bool dropWeapon;
            public bool hostile;
            public bool silent;
            public List<string> kits;
            public List<string> names;
            public List<ulong> ids;
            public List<Vector3> pos;
            public List<string> zonemap = new List<string>();
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
            public bool silent;
            public bool dropWeapon;
            public bool invulnerable;
            public bool lootable;
            public bool wipeClothing;
            public bool wipeBelt;
            public bool wipeMain;
            public bool wipeCorpseMain;
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
            public float damageDistance;
            public float damageAmount;

            public MonBotInfo(ulong uid, Vector3 position, Quaternion rotation)
            {
                //displayName = Instance.configData.Options.defaultName.Length > 0 ? Instance.configData.Options.defaultName : "Bot";
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
            public global::HumanNPC player;
            public BaseMelee melee;

            public Vector3 spawnPos;
            public Item activeItem;

            public bool inmelee;
            public float oldDamageScale;
            public float oldEffectiveRange;
            public float oldStoppingDistance;

            public float triggerDelay;
            public bool canHurt = true;

            private void Start()
            {
                player = GetComponent<global::HumanNPC>();
                melee = GetComponent<BaseMelee>();
                InvokeRepeating("GoHome", 1f, 1f);
                InvokeRepeating("Silence", 10f, 19f);
            }

            private void FixedUpdate()
            {
                AttackEntity ent = player.GetAttackEntity();
                if (ent == null) return;
                BaseProjectile weapon = ent as BaseProjectile;
                melee = ent as BaseMelee;

                if (weapon == null && !inmelee)
                {
                    if (melee == null) return;
                    if (!IsInvoking("DoTriggerDown"))
                    {
                        triggerDelay = melee?.aiStrikeDelay ?? 0.2f;
                        InvokeRepeating("DoTriggerDown", 0, 1f);
                        inmelee = true;
                    }
                }
                else if (weapon != null)
                {
                    inmelee = false;
                    CancelInvoke("DoTriggerDown");
                    player.Brain.states[AIState.Roam].StateEnter(player.Brain, player);
                }
            }

            private void DoTriggerDown()
            {
                BasePlayer attackPlayer = player.Brain.Senses.Players.FirstOrDefault()?.ToPlayer();

                if (attackPlayer != null)
                {
                    player.Brain.Navigator.SetDestination(attackPlayer.transform.position, BaseNavigator.NavigationSpeed.Fast, 0f, 0f);

                    if (Vector3.Distance(player.transform.position, attackPlayer.transform.position) <= melee.maxDistance)
                    {
                        if (canHurt)
                        {
                            Instance.DoLog($"TriggerDown on {attackPlayer.displayName}");
                            //player.TriggerDown();
                            melee.ServerUse(player.damageScale, null);

                            Instance.timer.Once(triggerDelay, () =>
                            {
                                Instance.DoLog($"Trigger delay: {triggerDelay}");
                                attackPlayer.Hurt(melee.TotalDamage(), DamageType.Slash, player, true);
                                Effect.server.Run("assets/bundled/prefabs/fx/headshot.prefab", attackPlayer.transform.position);
                                canHurt = false;
                                Invoke("ResetCanHurt", 0.5f);
                            });
                        }
                    }
                    return;
                }
                ResetCanHurt();
            }

            private void ResetCanHurt()
            {
                canHurt = true;
            }

            private void Silence()
            {
                if (!info.silent) return;
                if (player?.IsDestroyed != false) return;
                Instance.DoLog($"Silencing effects for {player?.UserIDString}");
                ScientistNPC npc = player as ScientistNPC;
                npc.SetChatterType(ScientistNPC.RadioChatterType.NONE);
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
        #endregion classes

        #region Helpers
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

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
            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (monument.name.Contains("power_sub"))
                {
                    continue;
                }

                float realWidth = 0f;
                string name = null;

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
                    string newname = name.Remove(name.Length - 1, 1) + "1";
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
                    monPos.Add(name.Trim(), monument.transform.position);
                    monSize.Add(name.Trim(), extents);
                    //DoLog($"Found monument {name} at {monument.transform.position.ToString()}");
                }
            }
        }
        #endregion Helpers

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
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < new VersionNumber(1, 0, 17))
            {
                do1017 = true;
            }

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
        #endregion config
    }
}
