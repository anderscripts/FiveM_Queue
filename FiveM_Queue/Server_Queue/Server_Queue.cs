using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Dynamic;
using Newtonsoft.Json;
using CitizenFX.Core;
using CitizenFX.Core.Native;

namespace Server
{
    public class Server_Queue : BaseScript
    {
        internal static string resourceName = API.GetCurrentResourceName();
        internal static string resourcePath = $"resources/{API.GetResourcePath(resourceName).Substring(API.GetResourcePath(resourceName).LastIndexOf("//") + 2)}";
        private ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        private ConcurrentQueue<string> newQueue = new ConcurrentQueue<string>();
        private ConcurrentQueue<string> pQueue = new ConcurrentQueue<string>();
        private ConcurrentQueue<string> newPQueue = new ConcurrentQueue<string>();
        private Dictionary<string, string> messages = new Dictionary<string, string>();
        private ConcurrentDictionary<string, SessionState> session = new ConcurrentDictionary<string, SessionState>();
        private ConcurrentDictionary<string, int> index = new ConcurrentDictionary<string, int>();
        private ConcurrentDictionary<string, DateTime> timer = new ConcurrentDictionary<string, DateTime>();
        private ConcurrentDictionary<string, Player> sentLoading = new ConcurrentDictionary<string, Player>();
        internal static ConcurrentDictionary<string, int> priority = new ConcurrentDictionary<string, int>();
        internal static ConcurrentDictionary<string, Reserved> reserved = new ConcurrentDictionary<string, Reserved>();
        internal static ConcurrentDictionary<string, Reserved> slotTaken = new ConcurrentDictionary<string, Reserved>();
        private string allowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ~`!@#$%^&*()_-+={[}]|:;<,>.?/\\";

        private bool allowSymbols = true;
        private bool queueCycleComplete = false;
        private int maxSession = 32;
        private int reservedTypeOneSlots = 0;
        private int reservedTypeTwoSlots = 0;
        private int reservedTypeThreeSlots = 0;
        private int publicTypeSlots = 0;
        private double queueGraceTime = 2;
        private double graceTime = 3;
        private double loadTime = 4;
        private int inQueue = 0;
        private int inPriorityQueue = 0;
        private string hostName = string.Empty;
        private int lastCount = 0;
        private bool whitelistonly = false;
        private bool serverQueueReady = false;
        private bool stateChangeMessages = false;
        internal static bool bannedReady = false;
        internal static bool reservedReady = false;
        internal static bool priorityReady = false;

        public Server_Queue()
        {
            LoadConfigs();
            EventHandlers["onResourceStop"] += new Action<string>(OnResourceStop);
            EventHandlers["playerConnecting"] += new Action<Player, string, CallbackDelegate, ExpandoObject>(PlayerConnecting);
            EventHandlers["playerDropped"] += new Action<Player, string>(PlayerDropped);
            EventHandlers["fivemqueue: playerConnected"] += new Action<Player>(PlayerActivated);
            API.RegisterCommand("q_session", new Action<int, List<object>, string>(QueueSession), true);
            API.RegisterCommand("q_changemax", new Action<int, List<object>, string>(QueueChangeMax), true);
            API.RegisterCommand("q_reloadconfig", new Action<int, List<object>, string>(ReloadConfig), true);
            API.RegisterCommand("q_kick", new Action<int, List<object>, string>(Kick), true);
            API.RegisterCommand("q_steamhexfromprofile", new Action<int, List<object>, string>(SteamProfileToHex), true);
            API.RegisterCommand("exitgame", new Action<int, List<object>, string>(ExitSession), false);
            API.RegisterCommand("q_count", new Action<int, List<object>, string>(QueueCheck), false);
            StopHardcap();
            Task.Run(QueueCycle);
            serverQueueReady = true;
        }

        private void LoadConfigs()
        {
            API.ExecuteCommand($"exec {resourcePath}/__configuration.cfg");
            if (hostName == string.Empty) { hostName = API.GetConvar("sv_hostname", string.Empty); }
            if (!File.Exists($"{resourcePath}/__messages.json")) { CreateMessagesJSON($"{resourcePath}/__messages.json"); }
            else { messages = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText($"{resourcePath}/__messages.json")); }
            maxSession = API.GetConvarInt("q_max_session_slots", 32);
            if (API.GetConvar("onesync_enabled", "false") == "true")
            {
                Debug.WriteLine($"[{resourceName} - INFO] - Server reports that OneSync is enabled. Ignoring regular 32 player limit and using your __configuration.cfg q_max_session_slots {maxSession} setting.");
            }
            else
            {
                if (maxSession > 32) { maxSession = 32; }
            }
            API.ExecuteCommand($"sv_maxclients {maxSession}");
            loadTime = API.GetConvarInt("q_loading_time_limit", 4);
            graceTime = API.GetConvarInt("q_reconnect_grace_time_limit", 3);
            queueGraceTime = API.GetConvarInt("q_queue_cancel_grace_time_limit", 2);
            reservedTypeOneSlots = API.GetConvarInt("q_reserved_type_1_slots", 0);
            reservedTypeTwoSlots = API.GetConvarInt("q_reserved_type_2_slots", 0);
            reservedTypeThreeSlots = API.GetConvarInt("q_reserved_type_3_slots", 0);
            publicTypeSlots = maxSession - reservedTypeOneSlots - reservedTypeTwoSlots - reservedTypeThreeSlots;
            whitelistonly = API.GetConvar("q_whitelist_only", "false") == "true";
            allowSymbols = API.GetConvar("q_allow_symbols_in_steam_name", "true") == "true";
            stateChangeMessages = API.GetConvar("q_enable_queue_state_changes_in_console", "true") == "true";
        }

        private void QueueCheck(int source, List<object> args, string raw)
        {
            Debug.WriteLine($"Queue: {queue.Count}");
            Debug.WriteLine($"Priority Queue: {pQueue.Count}");
            session.Where(k => k.Value == SessionState.Queue).ToList().ForEach(j => 
            {
                Debug.WriteLine($"{j.Key} is in queue. Timer: {timer.TryGetValue(j.Key, out DateTime oldTimer)} Priority: {priority.TryGetValue(j.Key, out int oldPriority)}");
            });
        }

        private void ReloadConfig(int source, List<object> args, string raw)
        {
            LoadConfigs();
        }

        private void CreateMessagesJSON(string path)
        {
            messages.Add("Gathering","Gathering queue information");
            messages.Add("License","License is required");
            messages.Add("Steam","Steam is required");
            messages.Add("Banned","You are banned");
            messages.Add("Whitelist","You are not whitelisted");
            messages.Add("Queue","You are in queue");
            messages.Add("PriorityQueue","You are in priority queue");
            messages.Add("Canceled","Canceled from queue");
            messages.Add("Error","An error prevented deferrals");
            messages.Add("Timeout", "Exceeded server owners maximum loading time threshold");
            messages.Add("QueueCount","[Queue: {0}]");
            messages.Add("Symbols","No symbols are allowed in your Steam name");
            File.WriteAllText(path, JsonConvert.SerializeObject(messages, Formatting.Indented));
        }

        private bool IsEverythingReady()
        {
            if (serverQueueReady && bannedReady && priorityReady && reservedReady)
            { return true; }
            return false;
        }

        private void ExitSession(int source, List<object> args, string raw)
        {
            try
            {
                if (source == 0)
                {
                    Debug.WriteLine($"This is not a console command");
                    return;
                }
                else
                {
                    Player player = Players.FirstOrDefault(k => k.Handle == source.ToString());
                    RemoveFrom(player.Identifiers["license"], true, true, true, true, true, true);
                    player.Drop("Exited");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - ExitSession()");
            }
        }

        private void Kick(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 1)
                {
                    Debug.WriteLine($"This command requires one arguement. <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                Player player = Players.FirstOrDefault(k => k.Identifiers["license"] == identifier || k.Identifiers["steam"] == identifier);
                if (player == null)
                {
                    Debug.WriteLine($"No matching account in session for {identifier}, use session command to get an identifier.");
                    return;
                }
                RemoveFrom(player.Identifiers["license"], true, true, true, true, true, true);
                player.Drop("Kicked");
                Debug.WriteLine($"{identifier} was kicked.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - Kick()");
            }
        }

        private void UpdateHostName()
        {
            try
            {
                if (hostName == string.Empty) { hostName = API.GetConvar("sv_hostname", string.Empty); }
                if (hostName == string.Empty) { return; }

                string concat = hostName;
                bool editHost = false;
                int count = inQueue + inPriorityQueue;
                if (API.GetConvar("q_add_queue_count_before_server_name", "false") == "true")
                {
                    editHost = true;
                    if (count > 0) { concat = string.Format($"{messages["QueueCount"]} {concat}", count); }
                    else { concat = hostName; }
                }
                if (API.GetConvar("q_add_queue_count_after_server_name", "false") == "true")
                {
                    editHost = true;
                    if (count > 0) { concat = string.Format($"{concat} {messages["QueueCount"]}", count); }
                    else { concat = hostName; }
                }
                if (lastCount != count && editHost)
                {
                    API.SetConvar("sv_hostname", concat);
                }
                lastCount = count;
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - UpdateHostName()");
            }
        }

        private int QueueCount()
        {
            try
            {
                int place = 0;
                ConcurrentQueue<string> temp = new ConcurrentQueue<string>();
                while (!queue.IsEmpty)
                {
                    queue.TryDequeue(out string license);
                    if (IsTimeUp(license, queueGraceTime))
                    {
                        RemoveFrom(license, true, true, true, true, true, true);
                        if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: CANCELED -> REMOVED -> {license}"); }
                        continue;
                    }
                    if (priority.TryGetValue(license, out int priorityAdded))
                    {
                        newPQueue.Enqueue(license);
                        continue;
                    }
                    if (!Loading(license))
                    {
                        place += 1;
                        UpdatePlace(license, place);
                        temp.Enqueue(license);
                    }
                }
                while (!newQueue.IsEmpty)
                {
                    newQueue.TryDequeue(out string license);
                    if (!Loading(license))
                    {
                        place += 1;
                        UpdatePlace(license, place);
                        temp.Enqueue(license);
                    }
                }
                queue = temp;
                return queue.Count;
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - QueueCount()"); return queue.Count;
            }
        }

        private int PriorityQueueCount()
        {
            try
            {
                List<KeyValuePair<string, int>> order = new List<KeyValuePair<string, int>>();
                while (!pQueue.IsEmpty)
                {
                    pQueue.TryDequeue(out string license);
                    if (IsTimeUp(license, queueGraceTime))
                    {
                        RemoveFrom(license, true, true, true, true, true, true);
                        if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: CANCELED -> REMOVED -> {license}"); }
                        continue;
                    }
                    if(!priority.TryGetValue(license, out int priorityNum))
                    {
                        newQueue.Enqueue(license);
                        continue;
                    }
                    order.Insert(order.FindLastIndex(k => k.Value <= priorityNum) + 1, new KeyValuePair<string, int>(license, priorityNum));
                }
                while (!newPQueue.IsEmpty)
                {
                    newPQueue.TryDequeue(out string license);
                    priority.TryGetValue(license, out int priorityNum);
                    order.Insert(order.FindLastIndex(k => k.Value >= priorityNum) + 1, new KeyValuePair<string, int>(license, priorityNum));
                }
                int place = 0;
                order.ForEach(k =>
                {
                    if (!Loading(k.Key))
                    {
                        place += 1;
                        UpdatePlace(k.Key, place);
                        pQueue.Enqueue(k.Key);
                    }
                });
                return pQueue.Count;
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - PriorityQueueCount()"); return pQueue.Count;
            }
        }

        private bool Loading(string license)
        {
            try
            {
                if (reserved.ContainsKey(license) && reserved[license] == Reserved.Reserved1 && slotTaken.Count(j => j.Value == Reserved.Reserved1) < reservedTypeOneSlots)
                { NewLoading(license, Reserved.Reserved1); return true; }
                else if (reserved.ContainsKey(license) && (reserved[license] == Reserved.Reserved1 || reserved[license] == Reserved.Reserved2) && slotTaken.Count(j => j.Value == Reserved.Reserved2) < reservedTypeTwoSlots)
                { NewLoading(license, Reserved.Reserved2); return true; }
                else if (reserved.ContainsKey(license) && (reserved[license] == Reserved.Reserved1 || reserved[license] == Reserved.Reserved2 || reserved[license] == Reserved.Reserved3) && slotTaken.Count(j => j.Value == Reserved.Reserved3) < reservedTypeThreeSlots)
                { NewLoading(license, Reserved.Reserved3); return true; }
                else if (session.Count(j => j.Value != SessionState.Queue) - slotTaken.Count(i => i.Value != Reserved.Public) < publicTypeSlots)
                { NewLoading(license, Reserved.Public); return true; }
                else { return false; }
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - Loading()"); return false;
            }
        }

        private void BalanceReserved()
        {
            try
            {
                var query = from license in session
                            join license2 in reserved on license.Key equals license2.Key
                            join license3 in slotTaken on license.Key equals license3.Key
                            where license.Value == SessionState.Active && license2.Value != license3.Value
                            select new { license.Key, license2.Value };

                query.ToList().ForEach(k =>
                {
                    int openReservedTypeOneSlots = reservedTypeOneSlots - slotTaken.Count(j => j.Value == Reserved.Reserved1);
                    int openReservedTypeTwoSlots = reservedTypeTwoSlots - slotTaken.Count(j => j.Value == Reserved.Reserved2);
                    int openReservedTypeThreeSlots = reservedTypeThreeSlots - slotTaken.Count(j => j.Value == Reserved.Reserved3);

                    switch (k.Value)
                    {
                        case Reserved.Reserved1:
                            if (openReservedTypeOneSlots > 0)
                            {
                                if (!slotTaken.TryAdd(k.Key, Reserved.Reserved1))
                                {
                                    slotTaken.TryGetValue(k.Key, out Reserved oldReserved);
                                    slotTaken.TryUpdate(k.Key, Reserved.Reserved1, oldReserved);
                                }
                                if (stateChangeMessages) { Debug.WriteLine($"Assigned {k.Key} to Reserved1"); }
                            }
                            else if (openReservedTypeTwoSlots > 0)
                            {
                                if (!slotTaken.TryAdd(k.Key, Reserved.Reserved2))
                                {
                                    slotTaken.TryGetValue(k.Key, out Reserved oldReserved);
                                    slotTaken.TryUpdate(k.Key, Reserved.Reserved2, oldReserved);
                                }
                                if (stateChangeMessages) { Debug.WriteLine($"Assigned {k.Key} to Reserved2"); }
                            }
                            else if (openReservedTypeThreeSlots > 0)
                            {
                                if (!slotTaken.TryAdd(k.Key, Reserved.Reserved3))
                                {
                                    slotTaken.TryGetValue(k.Key, out Reserved oldReserved);
                                    slotTaken.TryUpdate(k.Key, Reserved.Reserved3, oldReserved);
                                }
                                if (stateChangeMessages) { Debug.WriteLine($"Assigned {k.Key} to Reserved3"); }
                            }
                            break;

                        case Reserved.Reserved2:
                            if (openReservedTypeTwoSlots > 0)
                            {
                                if (!slotTaken.TryAdd(k.Key, Reserved.Reserved2))
                                {
                                    slotTaken.TryGetValue(k.Key, out Reserved oldReserved);
                                    slotTaken.TryUpdate(k.Key, Reserved.Reserved2, oldReserved);
                                }
                                if (stateChangeMessages) { Debug.WriteLine($"Assigned {k.Key} to Reserved2"); }
                            }
                            else if (openReservedTypeThreeSlots > 0)
                            {
                                if (!slotTaken.TryAdd(k.Key, Reserved.Reserved3))
                                {
                                    slotTaken.TryGetValue(k.Key, out Reserved oldReserved);
                                    slotTaken.TryUpdate(k.Key, Reserved.Reserved3, oldReserved);
                                }
                                if (stateChangeMessages) { Debug.WriteLine($"Assigned {k.Key} to Reserved3"); }
                            }
                            break;

                        case Reserved.Reserved3:
                            if (openReservedTypeThreeSlots > 0)
                            {
                                if (!slotTaken.TryAdd(k.Key, Reserved.Reserved3))
                                {
                                    slotTaken.TryGetValue(k.Key, out Reserved oldReserved);
                                    slotTaken.TryUpdate(k.Key, Reserved.Reserved3, oldReserved);
                                }
                                if (stateChangeMessages) { Debug.WriteLine($"Assigned {k.Key} to Reserved3"); }
                            }
                            break;
                    }
                });
            }
            catch (Exception) { Debug.WriteLine($"[{resourceName} - ERROR] - BalanceReserved()"); }
        }

        private void NewLoading(string license, Reserved slotType)
        {
            try
            {
                if (session.TryGetValue(license, out SessionState oldState))
                {
                    UpdateTimer(license);
                    RemoveFrom(license, false, true, false, false, false, false);
                    if (!slotTaken.TryAdd(license, slotType))
                    {
                        slotTaken.TryGetValue(license, out Reserved oldSlotType);
                        slotTaken.TryUpdate(license, slotType, oldSlotType);
                    }
                    session.TryUpdate(license, SessionState.Loading, oldState);
                    if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: QUEUE -> LOADING -> ({Enum.GetName(typeof(Reserved), slotType)}) {license}"); }
                }
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - NewLoading()");
            }
        }

        private bool IsTimeUp(string license, double time)
        {
            try
            {
                if (!timer.ContainsKey(license)) { return false; }
                return timer[license].AddMinutes(time) < DateTime.UtcNow;
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - IsTimeUp()"); return false;
            }
        }

        private void UpdatePlace(string license, int place)
        {
            try
            {
                if (!index.TryAdd(license, place))
                {
                    index.TryGetValue(license, out int oldPlace);
                    index.TryUpdate(license, place, oldPlace);
                }
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - UpdatePlace()");
            }
        }

        private void UpdateTimer(string license)
        {
            try
            {
                if (!timer.TryAdd(license, DateTime.UtcNow))
                {
                    timer.TryGetValue(license, out DateTime oldTime);
                    timer.TryUpdate(license, DateTime.UtcNow, oldTime);
                }
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - UpdateTimer()");
            }
        }

        private void UpdateStates()
        {
            try
            {
                session.Where(k => k.Value == SessionState.Loading || k.Value == SessionState.Grace).ToList().ForEach(j =>
                {
                    string license = j.Key;
                    SessionState state = j.Value;
                    switch (state)
                    {
                        case SessionState.Loading:
                            if (!timer.TryGetValue(license, out DateTime oldLoadTime))
                            {
                                UpdateTimer(license);
                                break;
                            }
                            if (IsTimeUp(license, loadTime))
                            {
                                if (Players.FirstOrDefault(i => i.Identifiers["license"] == license)?.EndPoint != null)
                                {
                                    Players.FirstOrDefault(i => i.Identifiers["license"] == license).Drop($"{messages["Timeout"]}");
                                }
                                session.TryGetValue(license, out SessionState oldState);
                                session.TryUpdate(license, SessionState.Grace, oldState);
                                UpdateTimer(license);
                                if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: LOADING -> GRACE -> {license}"); }
                            }
                            else
                            {
                                if (sentLoading.ContainsKey(license) && Players.FirstOrDefault(i => i.Identifiers["license"] == license) != null)
                                {
                                    TriggerEvent("fivemqueue: newloading", sentLoading[license]);
                                    sentLoading.TryRemove(license, out Player oldPlayer);
                                }
                            }
                            break;
                        case SessionState.Grace:
                            if (!timer.TryGetValue(license, out DateTime oldGraceTime))
                            {
                                UpdateTimer(license);
                                break;
                            }
                            if (IsTimeUp(license, graceTime))
                            {
                                if (Players.FirstOrDefault(i => i.Identifiers["license"] == license)?.EndPoint != null)
                                {
                                    if (!session.TryAdd(license, SessionState.Active))
                                    {
                                        session.TryGetValue(license, out SessionState oldState);
                                        session.TryUpdate(license, SessionState.Active, oldState);
                                    }
                                }
                                else
                                {
                                    RemoveFrom(license, true, true, true, true, true, true);
                                    if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: GRACE -> REMOVED -> {license}"); }
                                }
                            }
                            break;
                    }
                });
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - UpdateStates()");
            }
        }

        private void RemoveFrom(string license, bool doSession, bool doIndex, bool doTimer, bool doPriority, bool doReserved, bool doSlot)
        {
            try
            {
                if (doSession) { session.TryRemove(license, out SessionState oldState); }
                if (doIndex) { index.TryRemove(license, out int oldPosition); }
                if (doTimer) { timer.TryRemove(license, out DateTime oldTime); }
                if (doPriority) { priority.TryRemove(license, out int oldPriority); }
                if (doReserved) { reserved.TryRemove(license, out Reserved oldReserved); }
                if (doSlot) { slotTaken.TryRemove(license, out Reserved oldSlot); }
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - RemoveFrom()");
            }
        }

        private async void QueueSession(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 0)
                {
                    Debug.WriteLine($"This command takes no arguments.");
                    return;
                }
                if (session.Count == 0)
                {
                    Debug.WriteLine($"No accounts in session");
                    return;
                }
                if (source == 0)
                {
                    Debug.WriteLine($"| LICENSE | STATE | STEAM | PRIORITY | RESERVE | SLOT USED | HANDLE | NAME | ");
                    session.OrderByDescending(k => k.Value).ToList().ForEach(j =>
                    {
                        Player player = Players.FirstOrDefault(i => i.Identifiers["license"] == j.Key);
                        if (player == null)
                        { sentLoading.TryGetValue(j.Key, out player); }
                        if (!priority.TryGetValue(j.Key, out int oldPriority)) { oldPriority = 0; }
                        if (!reserved.TryGetValue(j.Key, out Reserved oldReserved)) { oldReserved = Reserved.Public; }
                        if (!slotTaken.TryGetValue(j.Key, out Reserved oldSlot)) { oldSlot = Reserved.Public; }
                        Debug.WriteLine($"| {j.Key} | {j.Value} | {player?.Identifiers["steam"]} | {oldPriority} | {oldReserved} | {oldSlot} | {player?.Handle} | {player?.Name} |");
                    });
                }
                else
                {
                    List<dynamic> sessionReturn = new List<dynamic>();
                    session.OrderByDescending(k => k.Value).ToList().ForEach(j =>
                    {
                        dynamic temp;
                        Player player = Players.FirstOrDefault(i => i.Identifiers["license"] == j.Key);
                        if (player == null)
                        { sentLoading.TryGetValue(j.Key, out player); }
                        if (!priority.TryGetValue(j.Key, out int oldPriority)) { oldPriority = 0; }
                        if (!reserved.TryGetValue(j.Key, out Reserved oldReserved)) { oldReserved = Reserved.Public; }
                        if (!slotTaken.TryGetValue(j.Key, out Reserved oldSlot)) { oldSlot = Reserved.Public; }
                        temp = new { License = j.Key, State = j.Value, Steam = player?.Identifiers["steam"], Priority = oldPriority, Reserved = oldReserved, ReservedUsed = oldSlot, Handle = player?.Handle, Name = player?.Name };
                        sessionReturn.Add(temp);
                    });
                    Player requested = Players.FirstOrDefault(k => k.Handle == source.ToString());
                    requested.TriggerEvent("fivemqueue: sessionResponse", JsonConvert.SerializeObject(sessionReturn));
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - QueueSession()");
            }
            await Delay(0);
        }

        private async void QueueChangeMax(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 1) { Debug.WriteLine($"Command needs 1 argument. Example: changemax 32"); return; }
                int newMax = int.Parse(args[0].ToString());
                if (newMax <= 0 || newMax > 64) { Debug.WriteLine($"changemax must be between 1 and 64"); return; }
                while (!queueCycleComplete)
                {
                    await Delay(0);
                }
                maxSession = newMax;
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - QueueChangeMax()");
            }
        }

        private async void StopHardcap()
        {
            try
            {
                API.ExecuteCommand($"sets fivemqueue Enabled");
                int attempts = 0;
                while (attempts < 7)
                {
                    attempts += 1;
                    string state = API.GetResourceState("hardcap");
                    if (state == "missing")
                    {
                        break;
                    }
                    else if (state == "started")
                    {
                        API.StopResource("hardcap");
                        break;
                    }
                    await Delay(5000);
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - StopHardcap()");
            }
        }

        private void OnResourceStop(string name)
        {
            try
            {
                if (name == resourceName)
                {
                    if (API.GetResourceState("hardcap") != "started")
                    {
                        API.StartResource("hardcap");
                        API.ExecuteCommand($"sets fivemqueue Disabled");
                    }
                    if (hostName != string.Empty) { API.SetConvar("sv_hostname", hostName); return; }
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - OnResourceStop()");
            }
        }

        private void PlayerDropped([FromSource] Player source, string message)
        {
            try
            {
                string license = source.Identifiers["license"];
                if (license == null)
                {
                    return;
                }
                if (!session.ContainsKey(license) || message == "Exited")
                {
                    return;
                }
                bool hasState = session.TryGetValue(license, out SessionState oldState);
                if (hasState && oldState != SessionState.Queue)
                {
                    session.TryUpdate(license, SessionState.Grace, oldState);
                    if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: {Enum.GetName(typeof(SessionState), oldState).ToUpper()} -> GRACE -> {license}"); }
                    UpdateTimer(license);
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - PlayerDropped()");
            }
        }

        private void PlayerActivated([FromSource] Player source)
        {
            try
            {
                string license = source.Identifiers["license"];
                if (!session.ContainsKey(license))
                {
                    session.TryAdd(license, SessionState.Active);
                    return;
                }
                session.TryGetValue(license, out SessionState oldState);
                session.TryUpdate(license, SessionState.Active, oldState);
                if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: {Enum.GetName(typeof(SessionState), oldState).ToUpper()} -> ACTIVE -> {license}"); }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - PlayerActivated()");
            }
        }

        private bool ValidName(string playerName)
        {
            char[] chars = playerName.ToCharArray();

            char lastCharacter = new char();
            foreach (char currentCharacter in chars)
            {
                if (!allowedChars.ToCharArray().Contains(currentCharacter)) { return false; }
                if (char.IsWhiteSpace(currentCharacter) && char.IsWhiteSpace(lastCharacter)) { return false; }
                lastCharacter = currentCharacter;
            }
            return true;
        }

        private async void PlayerConnecting([FromSource]Player source, string playerName, dynamic denyWithReason, dynamic deferrals)
        {
            try
            {
                deferrals.defer();
                await Delay(500);
                while (!IsEverythingReady()) { await Delay(0); }
                deferrals.update($"{messages["Gathering"]}");
                string license = source.Identifiers["license"];
                string steam = source.Identifiers["steam"];
                if (license == null) { deferrals.done($"{messages["License"]}"); return; }
                if (steam == null) { deferrals.done($"{messages["Steam"]}"); return; }

                if (!allowSymbols && !ValidName(playerName)) { deferrals.done($"{messages["Symbols"]}"); return; }

                bool banned = false;
                if (Server_Banned.accounts.Exists(k => k.License == license && k.Steam == steam))
                {
                    banned = true;
                }
                else if (Server_Banned.accounts.Exists(k => k.License == license || k.Steam == steam) || Server_Banned.newblacklist.Exists(k => k.License == license || k.Steam == steam))
                {
                    banned = true;
                    Server_Banned.AutoBlacklist(new BannedAccount(license, steam));
                }

                if (banned)
                {
                    deferrals.done($"{messages["Banned"]}");
                    RemoveFrom(license, true, true, true, true, true, true);
                    return;
                }

                if (sentLoading.ContainsKey(license))
                {
                    sentLoading.TryRemove(license, out Player oldPlayer);
                }
                sentLoading.TryAdd(license, source);

                if (Server_Reserved.newwhitelist.Exists(k => k.License == license || k.Steam == steam))
                {
                    Server_Reserved.AutoWhitelist(new ReservedAccount(license, steam, Server_Reserved.newwhitelist.FirstOrDefault(k => k.License == license || k.Steam == steam).Reserve));
                }

                if (Server_Reserved.accounts.Exists(k => k.License == license || k.Steam == steam))
                {
                    if (!reserved.TryAdd(license, Server_Reserved.accounts.FirstOrDefault(k => k.License == license).Reserve))
                    {
                        reserved.TryGetValue(license, out Reserved oldReserved);
                        reserved.TryUpdate(license, Server_Reserved.accounts.FirstOrDefault(k => k.License == license).Reserve, oldReserved);
                    }
                }
                else
                {
                    RemoveFrom(license, false, false, false, false, true, true);
                    if (whitelistonly)
                    {
                        deferrals.done($"{messages["Whitelist"]}");
                        return;
                    }
                }

                if (Server_Priority.newwhitelist.Exists(k => k.License == license || k.Steam == steam))
                {
                    Server_Priority.AutoWhitelist(new PriorityAccount(license, steam, Server_Priority.newwhitelist.FirstOrDefault(k => k.License == license || k.Steam == steam).Priority));
                }
                if (Server_Priority.accounts.Exists(k => k.License == license))
                {
                    if (!priority.TryAdd(license, Server_Priority.accounts.FirstOrDefault(k => k.License == license).Priority))
                    {
                        priority.TryGetValue(license, out int oldPriority);
                        priority.TryUpdate(license, Server_Priority.accounts.FirstOrDefault(k => k.License == license).Priority, oldPriority);
                    }
                }
                else
                {
                    RemoveFrom(license, false, false, false, true, false, false);
                }

                if (session.TryAdd(license, SessionState.Queue))
                {
                    if (!priority.ContainsKey(license))
                    {
                        newQueue.Enqueue(license);
                        if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: NEW -> QUEUE -> (Public) {license}"); }
                    }
                    else
                    {
                        newPQueue.Enqueue(license);
                        if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: NEW -> QUEUE -> (Priority) {license}"); }
                    }
                }

                if (!session[license].Equals(SessionState.Queue))
                {
                    UpdateTimer(license);
                    session.TryGetValue(license, out SessionState oldState);
                    session.TryUpdate(license, SessionState.Loading, oldState);
                    deferrals.done();
                    if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: {Enum.GetName(typeof(SessionState), oldState).ToUpper()} -> LOADING -> (Grace) {license}"); }
                    return;
                }

                bool inPriority = priority.ContainsKey(license);
                int dots = 0;

                while (session[license].Equals(SessionState.Queue))
                {
                    if (index.ContainsKey(license) && index.TryGetValue(license, out int position))
                    {
                        int count = inPriority ? inPriorityQueue : inQueue;
                        string message = inPriority ? $"{messages["PriorityQueue"]}" : $"{messages["Queue"]}";
                        deferrals.update($"{message} {position} / {count}{new string('.', dots)}");
                    }
                    dots = dots > 2 ? 0 : dots + 1;
                    if (source?.EndPoint == null)
                    {
                        UpdateTimer(license);
                        deferrals.done($"{messages["Canceled"]}");
                        if (stateChangeMessages) { Debug.WriteLine($"[{resourceName}]: QUEUE -> CANCELED -> {license}"); }
                        return;
                    }
                    RemoveFrom(license, false, false, true, false, false, false);
                    await Delay(5000);
                }
                await Delay(500);
                deferrals.done();
            }
            catch (Exception)
            {
                deferrals.done($"{messages["Error"]}"); return;
            }
        }

        private async Task QueueCycle()
        {
            while (true)
            {
                try
                {
                    inPriorityQueue = PriorityQueueCount();
                    await Delay(100);
                    inQueue = QueueCount();
                    await Delay(100);
                    UpdateHostName();
                    UpdateStates();
                    await Delay(100);
                    BalanceReserved();
                    await Delay(1000);
                }
                catch (Exception)
                {
                    Debug.WriteLine($"[{resourceName} - ERROR] - QueueCycle()");
                }
            }
        }

        private void SteamProfileToHex(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 1) { Debug.WriteLine($"Command requires 1 argument. <Steam Community Profile Number>"); }
                long SteamCommunityId = long.Parse(args[0].ToString());
                long quotient = Math.DivRem(SteamCommunityId, 16, out long remainder);
                Stack<long> hex = new Stack<long>();
                hex.Push(remainder);
                while (quotient != 0)
                {
                    quotient = Math.DivRem(quotient, 16, out remainder);
                    hex.Push(remainder);
                }
                string steamHex = string.Empty;
                while (hex.Count != 0)
                {
                    steamHex = string.Concat(steamHex, hex.Pop().ToString("x"));
                }
                Debug.WriteLine($"{steamHex}");
            }
            catch(Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SteamProfileToHex()");
            }
        }
    }

    class PriorityAccount
    {
        public string License { get; set; }
        public string Steam { get; set; }
        public int Priority { get; set; }

        public PriorityAccount(string license, string steam, int priority)
        {
            License = license;
            Steam = steam;
            Priority = priority;
        }
    }

    class Server_Priority : BaseScript
    {
        static readonly string directory = $"{Server_Queue.resourcePath}/JSON/Priority";
        static List<FileInfo> files = new List<FileInfo>();
        internal static List<PriorityAccount> accounts = new List<PriorityAccount>();
        internal static List<PriorityAccount> newwhitelist = new List<PriorityAccount>();

        public Server_Priority()
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                DirectoryInfo di = new DirectoryInfo(directory);
                files = di.GetFiles("*.json").ToList();
                files.ForEach(k =>
                {
                    accounts.Add(JsonConvert.DeserializeObject<PriorityAccount>(File.ReadAllText(k.FullName).ToString()));
                });
                accounts.ForEach(k =>
                {
                    if (k.Priority <= 0 || k.Priority > 100)
                    {
                        k.Priority = 100;
                        string path = $"{directory}/{k.License}-{k.Steam}.json";
                        File.WriteAllText(path, JsonConvert.SerializeObject(k));
                    }
                    Server_Queue.priority.TryAdd(k.License, k.Priority);
                });
                if (File.Exists($"{Server_Queue.resourcePath}/JSON/offlinepriority.json"))
                {
                    newwhitelist = JsonConvert.DeserializeObject<List<PriorityAccount>>(File.ReadAllText($"{Server_Queue.resourcePath}/JSON/offlinepriority.json").ToString());
                }
                API.RegisterCommand("q_addpriority", new Action<int, List<object>, string>(Add), true);
                API.RegisterCommand("q_removepriority", new Action<int, List<object>, string>(Remove), true);
                Server_Queue.priorityReady = true;
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Priority.Start()");
            }
        }

        internal void Add(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 2)
                {
                    Debug.WriteLine($"This command requires two arguments. <Steam> OR <License> AND <Priority> (1 - 100)");
                    return;
                }
                string identifier = args[0].ToString();
                int priority = int.Parse(args[1].ToString());
                if (priority <= 0 || priority > 100) { priority = 100; }
                Player player = Players.FirstOrDefault(k => k.Identifiers["license"] == identifier || k.Identifiers["steam"] == identifier);
                if (player != null)
                {
                    PriorityAccount account = new PriorityAccount(player.Identifiers["license"], player.Identifiers["steam"], priority);
                    accounts.Add(account);
                    if (!Server_Queue.priority.TryAdd(account.License, priority))
                    {
                        Server_Queue.priority.TryGetValue(account.License, out int oldPriority);
                        Server_Queue.priority.TryUpdate(account.License, priority, oldPriority);
                    }
                    string path = $"{directory}/{account.License}-{account.Steam}.json";
                    File.WriteAllText(path, JsonConvert.SerializeObject(account));
                    Debug.WriteLine($"{identifier} was granted priority.");
                }
                else
                {
                    Debug.WriteLine($"No account found in session for {identifier}, adding to offline priority list");
                    newwhitelist.Add(new PriorityAccount(identifier, identifier, priority));
                    string path = $"{Server_Queue.resourcePath}/JSON/offlinepriority.json";
                    File.WriteAllText(path, JsonConvert.SerializeObject(newwhitelist));
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Priority.Add()");
            }
            return;
        }

        internal static void Remove(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 1)
                {
                    Debug.WriteLine($"This command requires one argument. <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                newwhitelist.Where(k => k.License == identifier || k.Steam == identifier).ToList().ForEach(j =>
                {
                    newwhitelist.Remove(j);
                });
                string path = $"{Server_Queue.resourcePath}/JSON/offlinepriority.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(newwhitelist));
                accounts.Where(k => k.License == identifier || k.Steam == identifier).ToList().ForEach(j =>
                {
                    path = $"{directory}/{j.License}-{j.Steam}.json";
                    File.Delete(path);
                    accounts.Remove(j);
                });
                Server_Queue.priority.TryRemove(identifier, out int oldPriority);
                Debug.WriteLine($"{identifier} was removed from priority list.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Priority.Remove()");
            }
            return;
        }

        internal static void AutoWhitelist(PriorityAccount account)
        {
            try
            {
                accounts.Add(account);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(account));
                newwhitelist.RemoveAll(k => k.License == account.License || k.Steam == account.Steam);
                path = $"{Server_Queue.resourcePath}/JSON/offlinepriority.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(newwhitelist));
                Debug.WriteLine($"{account.License}-{account.Steam} was auto prioritized.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Priority.AutoWhitelist()");
            }
        }
    }

    class ReservedAccount
    {
        public string License { get; set; }
        public string Steam { get; set; }
        public Reserved Reserve { get; set; }

        public ReservedAccount(string license, string steam, Reserved reserve)
        {
            License = license;
            Steam = steam;
            Reserve = reserve;
        }
    }

    class Server_Reserved : BaseScript
    {
        static readonly string directory = $"{Server_Queue.resourcePath}/JSON/Reserved";
        static List<FileInfo> files = new List<FileInfo>();
        internal static List<ReservedAccount> accounts = new List<ReservedAccount>();
        internal static List<ReservedAccount> newwhitelist = new List<ReservedAccount>();

        public Server_Reserved()
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                DirectoryInfo di = new DirectoryInfo(directory);
                files = di.GetFiles("*.json").ToList();
                files.ForEach(k =>
                {
                    accounts.Add(JsonConvert.DeserializeObject<ReservedAccount>(File.ReadAllText(k.FullName).ToString()));
                });
                accounts.ForEach(k =>
                {
                    Server_Queue.reserved.TryAdd(k.License, k.Reserve);
                });
                if (File.Exists($"{Server_Queue.resourcePath}/JSON/offlinereserve.json"))
                {
                    newwhitelist = JsonConvert.DeserializeObject<List<ReservedAccount>>(File.ReadAllText($"{Server_Queue.resourcePath}/JSON/offlinereserve.json").ToString());
                }
                API.RegisterCommand("q_addreserve", new Action<int, List<object>, string>(Add), true);
                API.RegisterCommand("q_removereserve", new Action<int, List<object>, string>(Remove), true);
                Server_Queue.reservedReady = true;
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.Start()");
            }
        }

        internal void Add(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 2)
                {
                    Debug.WriteLine($"This command requires two arguments. <Steam> OR <License> AND <Reserved> (1, 2, or 3)");
                    return;
                }
                string identifier = args[0].ToString();
                int reserve = int.Parse(args[1].ToString());
                if (reserve <= 0 || reserve > 3) { Debug.WriteLine($"This command requires two arguments. <Steam> OR <License> AND <Reserved> (1, 2, or 3)"); return; }
                Player player = Players.FirstOrDefault(k => k.Identifiers["license"] == identifier || k.Identifiers["steam"] == identifier);
                if (player != null)
                {
                    ReservedAccount account = new ReservedAccount(player.Identifiers["license"], player.Identifiers["steam"], (Reserved)reserve);
                    accounts.Add(account);
                    if (!Server_Queue.reserved.TryAdd(account.License, account.Reserve))
                    {
                        Server_Queue.reserved.TryGetValue(account.License, out Reserved oldReserved);
                        Server_Queue.reserved.TryUpdate(account.License, account.Reserve, oldReserved);
                    }
                    string path = $"{directory}/{account.License}-{account.Steam}.json";
                    File.WriteAllText(path, JsonConvert.SerializeObject(account));
                    Debug.WriteLine($"{identifier} was granted reserved slot.");
                }
                else
                {
                    Debug.WriteLine($"No account found in session for {identifier}, adding to offline reserve list");
                    newwhitelist.Add(new ReservedAccount(identifier, identifier, (Reserved)reserve));
                    string path = $"{Server_Queue.resourcePath}/JSON/offlinereserve.json";
                    File.WriteAllText(path, JsonConvert.SerializeObject(newwhitelist));
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.Add()");
            }
            return;
        }

        internal static void Remove(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 1)
                {
                    Debug.WriteLine($"This command requires one argument. <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                newwhitelist.Where(k => k.License == identifier || k.Steam == identifier).ToList().ForEach(j =>
                {
                    newwhitelist.Remove(j);
                });
                string path = $"{Server_Queue.resourcePath}/JSON/offlinereserve.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(newwhitelist));
                accounts.Where(k => k.License == identifier || k.Steam == identifier).ToList().ForEach(j =>
                {
                    path = $"{directory}/{j.License}-{j.Steam}.json";
                    File.Delete(path);
                    accounts.Remove(j);
                });
                Server_Queue.reserved.TryRemove(identifier, out Reserved oldReserved);
                Debug.WriteLine($"{identifier} was removed from reserved list.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.Remove()");
            }
            return;
        }

        internal static void AutoWhitelist(ReservedAccount account)
        {
            try
            {
                accounts.Add(account);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(account));
                newwhitelist.RemoveAll(k => k.License == account.License || k.Steam == account.Steam);
                path = $"{Server_Queue.resourcePath}/JSON/offlinereserve.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(newwhitelist));
                Debug.WriteLine($"{account.License}-{account.Steam} was auto reserved.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.AutoWhitelist()");
            }
        }
    }

    class BannedAccount
    {
        public string License { get; set; }
        public string Steam { get; set; }

        public BannedAccount(string license, string steam)
        {
            License = license;
            Steam = steam;
        }
    }

    class Server_Banned : BaseScript
    {
        static readonly string directory = $"{Server_Queue.resourcePath}/JSON/Banned";
        static List<FileInfo> files = new List<FileInfo>();
        internal static List<BannedAccount> accounts = new List<BannedAccount>();
        internal static List<BannedAccount> newblacklist = new List<BannedAccount>();

        public Server_Banned()
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                DirectoryInfo di = new DirectoryInfo(directory);
                files = di.GetFiles("*.json").ToList();
                files.ForEach(k =>
                {
                    accounts.Add(JsonConvert.DeserializeObject<BannedAccount>(File.ReadAllText(k.FullName).ToString()));
                });
                if (File.Exists($"{Server_Queue.resourcePath}/JSON/offlinebans.json"))
                {
                    newblacklist = JsonConvert.DeserializeObject<List<BannedAccount>>(File.ReadAllText($"{Server_Queue.resourcePath}/JSON/offlinebans.json").ToString());
                }
                API.RegisterCommand("q_addban", new Action<int, List<object>, string>(Add), true);
                API.RegisterCommand("q_removeban", new Action<int, List<object>, string>(Remove), true);
                Server_Queue.bannedReady = true;
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.Start()");
            }
        }

        internal void Add(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 1)
                {
                    Debug.WriteLine($"This command requires one argument. <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                Player player = Players.FirstOrDefault(k => k.Identifiers["license"] == identifier || k.Identifiers["steam"] == identifier);
                if (player != null)
                {
                    BannedAccount account = new BannedAccount(player.Identifiers["license"], player.Identifiers["steam"]);
                    accounts.Add(account);
                    string path = $"{directory}/{account.License}-{account.Steam}.json";
                    File.WriteAllText(path, JsonConvert.SerializeObject(account));
                    Debug.WriteLine($"{identifier} was banned.");
                    API.ExecuteCommand($"q_kick {identifier}");
                }
                else
                {
                    Debug.WriteLine($"No account found in session for {identifier}, adding to offline ban list");
                    newblacklist.Add(new BannedAccount(identifier, identifier));
                    string path = $"{Server_Queue.resourcePath}/JSON/offlinebans.json";
                    File.WriteAllText(path, JsonConvert.SerializeObject(newblacklist));
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.Add()");
            }
            return;
        }

        internal static void Remove(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count != 1)
                {
                    Debug.WriteLine($"This command requires one argument. <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                newblacklist.Where(k => k.License == identifier || k.Steam == identifier).ToList().ForEach(j =>
                {
                    newblacklist.Remove(j);
                });
                string path = $"{Server_Queue.resourcePath}/JSON/offlinebans.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(newblacklist));
                accounts.Where(k => k.License == identifier || k.Steam == identifier).ToList().ForEach(j =>
                {
                    path = $"{directory}/{j.License}-{j.Steam}.json";
                    File.Delete(path);
                    accounts.Remove(j);
                });
                Debug.WriteLine($"{identifier} was removed from banned list.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.Remove()");
            }
            return;
        }

        internal static void AutoBlacklist(BannedAccount account)
        {
            try
            {
                accounts.Add(account);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(account));
                newblacklist.RemoveAll(k => k.License == account.License || k.Steam == account.Steam);
                path = $"{Server_Queue.resourcePath}/JSON/offlinebans.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(newblacklist));
                Debug.WriteLine($"{account.License}-{account.Steam} was auto banned.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.AutoBlacklist()");
            }
        }
    }

    enum SessionState
    {
        Queue,
        Grace,
        Loading,
        Active,
    }

    enum Reserved
    {
        Reserved1 = 1,
        Reserved2,
        Reserved3,
        Public
    }
}
