using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using Newtonsoft.Json;
using System.Net;

namespace Server_Queue
{
    public class Server_Queue : BaseScript
    {
        internal static string resourceName = API.GetCurrentResourceName();
        private static readonly string jsonRoot = $"resources/{resourceName}/JSON";
        private static readonly string fileSession = $"{jsonRoot}/session.json";
        private static string jsonSession;
        internal static List<SessionAccount> sessionAccounts = new List<SessionAccount>();
        private static Queue<SessionAccount> newSessions = new Queue<SessionAccount>();
        private static Queue<SessionAccount> droppedSessions = new Queue<SessionAccount>();
        private static Queue<SessionAccount> activeSessions = new Queue<SessionAccount>();
        private static int maxSessions;
        private static int loadTimeLimit;
        private static int graceTimeLimit;
        private static int reservedTypeOneSlots;
        private static int reservedTypeTwoSlots;
        private static int reservedTypeThreeSlots;
        private static bool whitelistonly;
        private static int dots = 3;
        private static bool ready = false;

        public Server_Queue()
        {
            try
            {
                GetConfig();
                if (reservedTypeOneSlots + reservedTypeTwoSlots + reservedTypeThreeSlots > maxSessions)
                {
                    Debug.WriteLine($"\n\n{new string('#', 50)}\n[{resourceName} CONFIGURATION : FATAL] - Error in configuration.cfg\nSum of all reserved slot types (Current: {reservedTypeOneSlots + reservedTypeTwoSlots + reservedTypeThreeSlots}) must be less than or equal to max sessions (Current: {maxSessions}).\n{resourceName} will not start. Fix configuration.cfg and restart server\n{new string('#', 50)}\n\n");
                    return;
                }
                EventHandlers["playerConnecting"] += new Action<Player, string, CallbackDelegate, ExpandoObject>(PlayerConnecting);
                EventHandlers["playerDropped"] += new Action<Player, string>(PlayerDropped);
                EventHandlers["fivemqueue: playerConnected"] += new Action<Player>(PlayerActivated);
                API.RegisterCommand("q_restart", new Action<int, List<object>, string>(QueueRestart), true);
                API.RegisterCommand("q_session", new Action<int, List<object>, string>(QueueSession), true);
                API.RegisterCommand("exitgame", new Action<int, List<object>, string>(ExitSession), false);
                Server_Banned.Start();
                Server_Priority.Start();
                Server_Reserved.Start();
                CreateSession(fileSession);
                StopHardcap();
                Tick += SessionProcessing;
                ready = true;
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - FATAL ERROR] - Server_Queue()");
            }
        }

        private void GetConfig()
        {
            try
            {
                API.ExecuteCommand($"exec resources/{resourceName}/__configuration.cfg");
                maxSessions = API.GetConvarInt("q_max_session_slots", 32);
                loadTimeLimit = API.GetConvarInt("q_loading_time_limit", 5);
                graceTimeLimit = API.GetConvarInt("q_reconnect_grace_time_limit", 5);
                reservedTypeOneSlots = API.GetConvarInt("q_reserved_type_1_slots", 0);
                reservedTypeTwoSlots = API.GetConvarInt("q_reserved_type_2_slots", 0);
                reservedTypeThreeSlots = API.GetConvarInt("q_reserved_type_3_slots", 0);
                whitelistonly = API.GetConvar("q_whitelist_only", "false") == "true";
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - GetConfig()");
            }
        }

        private async void StopHardcap()
        {
            try
            {
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

        private async void QueueRestart(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 0)
                {
                    Debug.WriteLine($"This command takes no arguments.");
                    return;
                }
                Tick -= SessionProcessing;
                await Delay(1000);
                Tick += SessionProcessing;
                await Delay(1000);
                Debug.WriteLine("Restart complete.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - QueueRestart()");
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
                if (sessionAccounts.Count() == 0)
                {
                    Debug.WriteLine($"No accounts in session");
                    return;
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - QueueSession()");
            }
            try
            {
                Player player = Players.FirstOrDefault(k => k.Handle == source.ToString());
                player.TriggerEvent("FivemQueue: sessionResponse", JsonConvert.SerializeObject(sessionAccounts));
            }
            catch (Exception)
            {
                Debug.WriteLine($"| HANDLE | LICENSE | STEAM | NAME | RESERVED | RESERVED USED | PRIORITY | STATE |");
                sessionAccounts.ForEach(k =>
                {
                    Debug.WriteLine($"| {k.Handle} | {k.License} | {k.Steam} | {k.Name} | {Enum.GetName(typeof(Reserved), k.ReservedType)} | {Enum.GetName(typeof(Reserved), k.ReservedTypeUsed)} | {k.HasPriority} | {Enum.GetName(typeof(SessionState), k.State)} |");
                });
            }
            await Delay(1000);
        }

        private async void ExitSession(int source, List<object> args, string raw)
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
                    player.Drop("Exited");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - ExitSession()");
            }
            await Delay(1000);
        }

        private void CreateSession(string session)
        {
            try
            {
                if (!Directory.Exists(jsonRoot))
                {
                    Directory.CreateDirectory(jsonRoot);
                }
                if (!File.Exists(session))
                {
                    File.WriteAllText(session, JsonConvert.SerializeObject(sessionAccounts));
                }
                jsonSession = File.ReadAllText(session);
                sessionAccounts = JsonConvert.DeserializeObject<List<SessionAccount>>(jsonSession);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - CreateSession()");
            }
            try
            { 
            if (sessionAccounts.Count() > 0)
                {
                    sessionAccounts.ForEach(k =>
                    {
                        if (k.State == SessionState.Banned || k.State == SessionState.Exiting)
                        {
                            sessionAccounts.Remove(k);
                        }
                        else if (!k.IsQueued)
                        {
                            k.State = SessionState.Grace;
                            k.DropStartTime = DateTime.UtcNow;
                        }
                        else
                        {
                            k.State = SessionState.QueueGrace;
                            k.DropStartTime = DateTime.UtcNow;
                        }
                    });
                    sessionAccounts = sessionAccounts.OrderBy(k => k.State).ThenBy(k => k.QueueStartTime).ToList();
                    jsonSession = JsonConvert.SerializeObject(sessionAccounts);
                    File.WriteAllText(fileSession, jsonSession);
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - CreateSession() - Restore");
            }
        }

        private async Task SessionProcessing()
        {
            try
            {
                sessionAccounts.Where(k => k.State == SessionState.Queue && !k.IsConnected).ToList().ForEach(k =>
                {
                    k.DropStartTime = DateTime.UtcNow;
                    k.State = SessionState.QueueGrace;
                });
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Queue Grace");
            }
            try
            {
                sessionAccounts.Where(k => k.State == SessionState.Loading && k.LoadStartTime.AddMinutes(loadTimeLimit) < DateTime.UtcNow).ToList().ForEach(k =>
                {
                    Function.Call(Hash.DROP_PLAYER, k.Handle, "Froze during game load");
                });
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Drop Frozen Loading");
            }
            try
            {
                while (droppedSessions.Count > 0)
                {
                    SessionAccount droppedAccount = droppedSessions.Dequeue();
                    SessionAccount account = sessionAccounts.Find(k => k.License == droppedAccount.License && k.Steam == droppedAccount.Steam);
                    if (account != null)
                    {
                        if (!account.IsQueued)
                        {
                            account.State = SessionState.Grace;
                            continue;
                        }
                        account.State = SessionState.QueueGrace;
                        continue;
                    }
                    Debug.WriteLine($"[{resourceName} PROCESSING : WARNING] - Dropped Account Does Not Exist In Session : {droppedAccount.Steam} : {droppedAccount.License} : {droppedAccount.Name}");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Dropped Sessions");
            }
            try
            {
                while (activeSessions.Count > 0)
                {
                    SessionAccount activeAccount = activeSessions.Dequeue();
                    SessionAccount account = sessionAccounts.Find(k => k.License == activeAccount.License && k.Steam == activeAccount.Steam);
                    if (account != null)
                    {
                        account.Handle = activeAccount.Handle;
                        account.State = SessionState.Active;
                        continue;
                    }
                    Debug.WriteLine($"[{resourceName} PROCESSING : WARNING] - Active Account Does Not Exist In Session : {activeAccount.Steam} : {activeAccount.License} : {activeAccount.Name}");
                    Function.Call(Hash.DROP_PLAYER, activeAccount.Handle, "No Session Account Exists.");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Activated Sessions");
            }
            try
            {
                while (newSessions.Count > 0)
                {
                    SessionAccount newAccount = newSessions.Dequeue();
                    if (!newAccount.IsConnected)
                    {
                        continue;
                    }
                    SessionAccount account = sessionAccounts.Find(k => k.License == newAccount.License && k.Steam == newAccount.Steam);
                    if (account != null)
                    {
                        account.Deferrals = newAccount.Deferrals;
                        account.Name = newAccount.Name;
                        account.Handle = newAccount.Handle;
                        account.HasPriority = newAccount.HasPriority;
                        account.ReservedType = newAccount.ReservedType;
                        if (account.State == SessionState.Grace)
                        {
                            account.State = SessionState.Loading;
                            account.LoadStartTime = DateTime.UtcNow;
                            account.Deferrals.done();
                            continue;
                        }
                        if (account.State == SessionState.QueueGrace)
                        {
                            account.State = SessionState.Queue;
                            account.Deferrals.update("Original queue position restored.");
                            continue;
                        }
                        account.Deferrals.done("Connected too early. Try again."); continue;
                    }
                    account = newAccount;
                    sessionAccounts.Add(account);
                    newAccount.Deferrals.update("Added To Queue");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle New Sessions");
            }
            try
            {
                sessionAccounts.RemoveAll(k => ((k.State == SessionState.Grace || k.State == SessionState.QueueGrace) && k.DropStartTime.AddMinutes(graceTimeLimit) < DateTime.UtcNow) || k.State == SessionState.Banned || k.State == SessionState.Exiting);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Remove Expired Grace/Banned/Exited");
            }
            try
            {
                sessionAccounts = sessionAccounts.OrderBy(k => k.State).ThenBy(k => k.HasPriority == true).ThenBy(k => k.QueueStartTime).ToList();
                sessionAccounts.ForEach(k =>
                {
                    ReservedAccount reserved = Server_Reserved.accounts.Find(j => j.License == k.License && j.Steam == k.Steam);
                    if (reserved != null)
                    {
                        k.ReservedType = reserved.Reserve;
                    }
                    else
                    {
                        k.ReservedType = Reserved.Public;
                    }
                    ServerAccount priority = Server_Priority.accounts.Find(j => j.License == k.License && j.Steam == k.Steam);
                    if (priority != null)
                    {
                        k.HasPriority = true;
                    }
                    else
                    {
                        k.HasPriority = false;
                    }
                });
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Priority and Reserve Changes");
            }
            try
            {
                int openReservedSessionTypeOne = reservedTypeOneSlots - sessionAccounts.Count(k => !k.IsQueued && k.ReservedTypeUsed == Reserved.Reserved1);
                if (openReservedSessionTypeOne > 0)
                {
                    for (int i = 0; i < openReservedSessionTypeOne; i++)
                    {
                        SessionAccount account = sessionAccounts.FirstOrDefault(k => k.IsConnected && !k.IsQueued && k.ReservedType == Reserved.Reserved1 && k.ReservedTypeUsed != Reserved.Reserved1);
                        if (account != null)
                        {
                            account.ReservedTypeUsed = Reserved.Reserved1;
                            continue;
                        }
                        account = sessionAccounts.FirstOrDefault(k => k.IsConnected && k.State == SessionState.Queue && k.ReservedType == Reserved.Reserved1);
                        if (account != null)
                        {
                            account.State = SessionState.Loading;
                            account.ReservedTypeUsed = Reserved.Reserved1;
                            account.LoadStartTime = DateTime.UtcNow;
                            account.Deferrals.done();
                            continue;
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Reserved Type 1 Allocation/Connection");
            }
            try
            {
                int openReservedSessionTypeTwo = reservedTypeTwoSlots - sessionAccounts.Count(k => !k.IsQueued && k.ReservedTypeUsed == Reserved.Reserved2);
                if (openReservedSessionTypeTwo > 0)
                {
                    for (int i = 0; i < openReservedSessionTypeTwo; i++)
                    {
                        SessionAccount account = sessionAccounts.FirstOrDefault(k => k.IsConnected && !k.IsQueued && k.ReservedType == Reserved.Reserved2 && k.ReservedTypeUsed != Reserved.Reserved2);
                        if (account != null)
                        {
                            account.ReservedTypeUsed = Reserved.Reserved2;
                            continue;
                        }
                        account = sessionAccounts.FirstOrDefault(k => k.IsConnected && k.State == SessionState.Queue && (k.ReservedType == Reserved.Reserved2 || k.ReservedType == Reserved.Reserved1));
                        if (account != null)
                        {
                            account.State = SessionState.Loading;
                            account.ReservedTypeUsed = Reserved.Reserved2;
                            account.LoadStartTime = DateTime.UtcNow;
                            account.Deferrals.done();
                            continue;
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Reserved Type 2 Allocation/Connection");
            }
            try
            {
                int openReservedSessionTypeThree = reservedTypeThreeSlots - sessionAccounts.Count(k => !k.IsQueued && k.ReservedTypeUsed == Reserved.Reserved3);
                if (openReservedSessionTypeThree > 0)
                {
                    for (int i = 0; i < openReservedSessionTypeThree; i++)
                    {
                        SessionAccount account = sessionAccounts.FirstOrDefault(k => k.IsConnected && !k.IsQueued && k.ReservedType == Reserved.Reserved3 && k.ReservedTypeUsed != Reserved.Reserved3);
                        if (account != null)
                        {
                            account.ReservedTypeUsed = Reserved.Reserved3;
                            continue;
                        }
                        account = sessionAccounts.FirstOrDefault(k => k.IsConnected && k.State == SessionState.Queue && (k.ReservedType == Reserved.Reserved3 || k.ReservedType == Reserved.Reserved2 || k.ReservedType == Reserved.Reserved1));
                        if (account != null)
                        {
                            account.State = SessionState.Loading;
                            account.ReservedTypeUsed = Reserved.Reserved3;
                            account.LoadStartTime = DateTime.UtcNow;
                            account.Deferrals.done();
                            continue;
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Reserved Type 3 Allocation/Connection");
            }
            try
            {
                int openSessionCount = maxSessions - reservedTypeOneSlots - reservedTypeTwoSlots - reservedTypeThreeSlots - sessionAccounts.Count(k => !k.IsQueued && k.ReservedTypeUsed == Reserved.Public);
                if (openSessionCount > 0)
                {
                    for (int i = 0; i < openSessionCount; i++)
                    {
                        SessionAccount account = sessionAccounts.FirstOrDefault(k => k.IsConnected && !k.IsQueued && k.ReservedType == Reserved.Public && k.ReservedTypeUsed != Reserved.Public);
                        if (account != null)
                        {
                            account.ReservedTypeUsed = Reserved.Public;
                            continue;
                        }
                        account = sessionAccounts.FirstOrDefault(k => k.IsConnected && k.State == SessionState.Queue);
                        if (account != null)
                        {
                            account.State = SessionState.Loading;
                            account.ReservedTypeUsed = Reserved.Public;
                            account.LoadStartTime = DateTime.UtcNow;
                            account.Deferrals.done();
                            continue;
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Pulic Slot Connection");
            }
            try
            {
                string json = JsonConvert.SerializeObject(sessionAccounts);
                if (File.ReadAllText(fileSession) != json)
                {
                    File.WriteAllText(fileSession, json);
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Session File Update");
            }
            try
            {
                List<SessionAccount> queueAccounts = sessionAccounts.Where(k => (k.State == SessionState.Queue || k.State == SessionState.QueueGrace) && k.HasPriority == false).OrderBy(k => k.QueueStartTime).ToList();
                int inQueue = queueAccounts.Count();
                if (inQueue > 0)
                {
                    queueAccounts.ForEach(k =>
                    {
                        if (k.State == SessionState.Queue && k.IsConnected)
                        {
                            int index = queueAccounts.IndexOf(k) + 1;
                            k.Deferrals.update($"You are currently {index} of {inQueue} in queue. Please wait{new string('.', dots)} ");
                        }
                    });
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Deferral Updates for Queued Players");
            }
            try
            {
                List<SessionAccount> queuePriorityAccounts = sessionAccounts.Where(k => (k.State == SessionState.Queue || k.State == SessionState.QueueGrace) && k.HasPriority == true).OrderBy(k => k.QueueStartTime).ToList();
                int inQueuePriority = queuePriorityAccounts.Count();
                if (inQueuePriority > 0)
                {
                    queuePriorityAccounts.ForEach(k =>
                    {
                        if (k.State == SessionState.Queue && k.IsConnected)
                        {
                            int index = queuePriorityAccounts.IndexOf(k) + 1;
                            k.Deferrals.update($"You are currently {index} of {inQueuePriority} in priority queue. Please wait{new string('.', dots)} ");
                        }
                    });
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Deferral Updates for Queued Priority Players");
            }
            try
            {
                if (dots >= 3) { dots = 1; } else { dots++; }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - SessionProcessing() - Handle Dots Update");
            }
            await Delay(3000);
        }

        private async void PlayerConnecting([FromSource] Player source, string playerName, CallbackDelegate denyWithReason, dynamic deferrals)
        {
            try
            {
                var Defer = (CallbackDelegate)deferrals.defer;
                var Update = (CallbackDelegate)deferrals.update;
                var Done = (CallbackDelegate)deferrals.done;
                if (!ready)
                {
                    Done("Session is not fully loaded. Try again.");
                    return;
                }
                Defer();
                await Delay(1000);

                Update("Connecting...");
                string license = source.Identifiers["license"];
                string steam = source.Identifiers["steam"];
                if (license == null)
                {
                    Done("License is required.");
                    return;
                }
                if (steam == null)
                {
                    Done("Steam is required.");
                    return;
                }
                ServerAccount bannedAccount = Server_Banned.accounts.Find(k => k.Steam == steam && k.License == license);
                if (bannedAccount != null)
                {
                    Done("This account is banned.");
                    return;
                }
                else if (bannedAccount == null && (Server_Banned.accounts.Exists(k => k.Steam == steam) || Server_Banned.accounts.Exists(k => k.License == license)))
                {
                    bannedAccount = new ServerAccount(license, steam);
                    Server_Banned.AutoBan(bannedAccount);
                    Done("Changing accounts to avoid a ban is not allowed.");
                    return;
                }
                SessionAccount account = sessionAccounts.Find(k => k.License == license && k.Steam == steam);
                if (account != null)
                {
                    account.Source = source;
                    account.Name = source.Name;
                    account.Handle = source.Handle;
                    account.Deferrals = deferrals;
                }
                else
                {
                    account = new SessionAccount(source, license, steam, source.Name, source.Handle, deferrals, DateTime.UtcNow);
                }
                if (Server_Priority.accounts.Exists(k => k.License == account.License && k.Steam == account.Steam))
                {
                    account.HasPriority = true;
                }
                else
                {
                    account.HasPriority = false;
                }
                ReservedAccount reservedAccount = Server_Reserved.accounts.Find(k => k.Steam == steam && k.License == license);
                if (reservedAccount != null)
                {
                    account.ReservedType = reservedAccount.Reserve;
                }
                else
                {
                    ReservedAccount tempReserved = Server_Reserved.newwhitelist.Find(k => k.Steam == steam || k.License == license);
                    if (tempReserved == null)
                    {
                        account.ReservedType = Reserved.Public;
                    }
                    else
                    {
                        account.ReservedType = tempReserved.Reserve;
                        Server_Reserved.AutoWhitelist(new ReservedAccount(account.License, account.Steam, account.ReservedType));
                        Server_Reserved.newwhitelist.RemoveAll(k => k.Steam == account.Steam || k.License == account.License);
                    }
                }
                if (!account.IsConnected)
                {
                    Done("Dropped");
                    return;
                }
                if (whitelistonly && account.ReservedType == Reserved.Public)
                {
                    Done("You are not whitelisted.");
                    return;
                }
                newSessions.Enqueue(account);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - PlayerConnecting()");
            }
        }

        private async void PlayerDropped([FromSource] Player source, string message)
        {
            try
            {
                SessionAccount account = sessionAccounts.Find(k => k.Steam == source.Identifiers["steam"] && k.License == source.Identifiers["license"]);
                if (account != null)
                {
                    account.DropStartTime = DateTime.UtcNow;
                    if (message.Contains("Banned"))
                    {
                        Debug.WriteLine($"Banned player was dropped. ({source.Name}) ({source.Handle})"); account.State = SessionState.Banned;
                        return;
                    }
                    if (message.Contains("Exited"))
                    {
                        Debug.WriteLine($"Player exited was dropped. ({source.Name}) ({source.Handle})"); account.State = SessionState.Exiting;
                        return;
                    }
                    if (message.Contains("Kicked"))
                    {
                        Debug.WriteLine($"Player kicked kicked was dropped. ({source.Name}) ({source.Handle})"); account.State = SessionState.Exiting;
                        return;
                    }
                    droppedSessions.Enqueue(account);
                }
                else
                {
                    Debug.WriteLine($"[{resourceName} DROPPED : WARNING] - Unknown Player Dropped : {source.Identifiers["steam"]} : {source.Identifiers["license"]} : {source.Name}");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - PlayerDropped()");
            }
            await Delay(0);
        }

        private async void PlayerActivated([FromSource] Player source)
        {
            try
            {
                SessionAccount account = sessionAccounts.Find(k => k.License == source.Identifiers["license"] && k.Steam == source.Identifiers["steam"]);
                if (account != null)
                {
                    account.Handle = source.Handle;
                    activeSessions.Enqueue(account);
                }
                else
                {
                    Debug.WriteLine($"[{resourceName} ACTIVATED : WARNING] - Unknown Player Activated : {source.Identifiers["steam"]} : {source.Identifiers["license"]} : {source.Name}");
                    source.Drop("No Session Account Exists");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - ERROR] - PlayerActivated()");
            }
            await Delay(0);
        }
    }

    static class Server_Banned
    {
        static readonly string directory = $"resources/{Server_Queue.resourceName}/JSON/Banned";
        static List<FileInfo> files = new List<FileInfo>();
        internal static List<ServerAccount> accounts = new List<ServerAccount>();

        internal static void Start()
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
                    accounts.Add(JsonConvert.DeserializeObject<ServerAccount>(File.ReadAllText(k.FullName).ToString()));
                });
                API.RegisterCommand("q_addban", new Action<int, List<object>, string>(Add), true);
                API.RegisterCommand("q_removeban", new Action<int, List<object>, string>(Remove), true);
                API.RegisterCommand("q_kick", new Action<int, List<object>, string>(Kick), true);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.Start()");
            }
        }

        internal static async void Add(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 1)
                {
                    Debug.WriteLine($"This command requires one arguement. <Handle> OR <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                SessionAccount account = Server_Queue.sessionAccounts.Find(k => k.Handle == identifier || k.License == identifier || k.Steam == identifier);
                if (account == null)
                {
                    Debug.WriteLine($"No matching account in session for {identifier}, use session command to get an identifier.");
                    return;
                }
                if (accounts.Exists(k => k.License == account.License))
                {
                    Debug.WriteLine($"{identifier} is already banned.");
                    return;
                }
                ServerAccount banned = new ServerAccount(account.License, account.Steam);
                accounts.Add(banned);
                Function.Call(Hash.DROP_PLAYER, account.Handle, "Banned");
                string path = $"{directory}/{banned.License}-{banned.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(banned));
                Debug.WriteLine($"{identifier} was banned.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.Add()");
            }
            await BaseScript.Delay(1000);
            return;
        }

        internal static async void Remove(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 1)
                {
                    Debug.WriteLine($"This command requires one arguement. <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                ServerAccount account = accounts.Find(k => k.License == identifier || k.Steam == identifier);
                if (account == null)
                {
                    Debug.WriteLine($"No matching account in bans for {identifier}");
                    return;
                }
                accounts.RemoveAll(k => k.License == account.License && k.Steam == account.Steam);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.Delete(path);
                Debug.WriteLine($"{identifier} was removed from banned list.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.Remove()");
            }
            await BaseScript.Delay(1000);
            return;
        }

        internal static async void AutoBan(ServerAccount account)
        {
            try
            {
                accounts.Add(account);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(account));
                Debug.WriteLine($"{account.License}-{account.Steam} was auto banned.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.AutoBan()");
            }
            await BaseScript.Delay(1000);
        }

        internal static async void Kick(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 1)
                {
                    Debug.WriteLine($"This command requires one arguement. <Handle> OR <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                SessionAccount account = Server_Queue.sessionAccounts.Find(k => k.Handle == identifier || k.License == identifier || k.Steam == identifier);
                if (account == null)
                {
                    Debug.WriteLine($"No matching account in session for {identifier}, use session command to get an identifier.");
                    return;
                }
                Function.Call(Hash.DROP_PLAYER, account.Handle, "Kicked");
                Debug.WriteLine($"{identifier} was kicked.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Banned.Kick()");
            }
            await BaseScript.Delay(1000);
            return;
        }
    }

    static class Server_Priority
    {
        static readonly string directory = $"resources/{Server_Queue.resourceName}/JSON/Priority";
        static List<FileInfo> files = new List<FileInfo>();
        internal static List<ServerAccount> accounts = new List<ServerAccount>();

        internal static void Start()
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
                    accounts.Add(JsonConvert.DeserializeObject<ServerAccount>(File.ReadAllText(k.FullName).ToString()));
                });
                API.RegisterCommand("q_addpriority", new Action<int, List<object>, string>(Add), true);
                API.RegisterCommand("q_removepriority", new Action<int, List<object>, string>(Remove), true);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Priority.Start()");
            }
        }

        internal static async void Add(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 1)
                {
                    Debug.WriteLine($"This command requires one arguement. <Handle> OR <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                SessionAccount account = Server_Queue.sessionAccounts.Find(k => k.Handle == identifier || k.License == identifier || k.Steam == identifier);
                if (account == null)
                {
                    Debug.WriteLine($"No matching account in session for {identifier}, use session command to get an identifier.");
                    return;
                }
                if (accounts.Exists(k => k.License == account.License))
                {
                    Debug.WriteLine($"{identifier} already has priority.");
                    return;
                }
                ServerAccount priority = new ServerAccount(account.License, account.Steam);
                accounts.Add(priority);
                string path = $"{directory}/{priority.License}-{priority.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(priority));
                Debug.WriteLine($"{identifier} was granted priority.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Priority.Add()");
            }
            await BaseScript.Delay(1000);
            return;
        }

        internal static async void Remove(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 1)
                {
                    Debug.WriteLine($"This command requires one arguement. <Handle> OR <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                SessionAccount account = Server_Queue.sessionAccounts.Find(k => k.Handle == identifier || k.License == identifier || k.Steam == identifier);
                if (account == null)
                {
                    Debug.WriteLine($"No matching account in session for {identifier}, use session command to get an identifier.");
                    return;
                }
                if (!accounts.Exists(k => k.License == account.License))
                {
                    Debug.WriteLine($"{identifier} does not have priority.");
                    return;
                }
                accounts.RemoveAll(k => k.License == account.License && k.Steam == account.Steam);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.Delete(path);
                Debug.WriteLine($"{identifier} was removed from priority list.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Priority.Remove()");
            }
            await BaseScript.Delay(1000);
            return;
        }
    }

    static class Server_Reserved
    {
        static readonly string directory = $"resources/{Server_Queue.resourceName}/JSON/Reserved";
        static List<FileInfo> files = new List<FileInfo>();
        internal static List<ReservedAccount> accounts = new List<ReservedAccount>();
        internal static List<ReservedAccount> newwhitelist = new List<ReservedAccount>();

        internal static void Start()
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
                API.RegisterCommand("q_addreserve", new Action<int, List<object>, string>(Add), true);
                API.RegisterCommand("q_removereserve", new Action<int, List<object>, string>(Remove), true);
                API.RegisterCommand("q_offlinereserve", new Action<int, List<object>, string>(OfflineReserve), true);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.Start()");
            }
        }

        internal static async void Add(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 2)
                {
                    Debug.WriteLine($"This command requires two arguements. <Handle> OR <Steam> OR <License> AND <Slot Type> (1, 2, or 3)");
                    return;
                }
                string identifier = args[0].ToString();
                string type = args[1].ToString();
                bool valid = type == "1" || type == "2" || type == "3";
                if (!valid)
                {
                    Debug.WriteLine($"Reserved type must be between 1, 2, or 3");
                    return;
                }
                SessionAccount account = Server_Queue.sessionAccounts.Find(k => k.Handle == identifier || k.License == identifier || k.Steam == identifier);
                if (account == null)
                {
                    Debug.WriteLine($"No matching account in session for {identifier}, use session command to get an identifier.");
                    return;
                }
                ReservedAccount reserved = new ReservedAccount(account.License, account.Steam, (Reserved)int.Parse(type));
                if (!accounts.Exists(k => k.License == account.License && k.Steam == account.Steam))
                {
                    accounts.Add(reserved);
                }
                else
                {
                    accounts.Find(k => k.License == account.License && k.Steam == account.Steam).Reserve = (Reserved)int.Parse(type);
                }
                string path = $"{directory}/{reserved.License}-{reserved.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(reserved));
                Debug.WriteLine($"{identifier} was granted {Enum.GetName(typeof(Reserved), reserved.Reserve)} slot.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.Add()");
            }
            await BaseScript.Delay(1000);
            return;
        }

        internal static async void Remove(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 1)
                {
                    Debug.WriteLine($"This command requires one arguement. <Handle> OR <Steam> OR <License>");
                    return;
                }
                string identifier = args[0].ToString();
                SessionAccount account = Server_Queue.sessionAccounts.Find(k => k.Handle == identifier || k.License == identifier || k.Steam == identifier);
                if (account == null)
                {
                    Debug.WriteLine($"No matching account in session for {identifier}, use session command to get an identifier.");
                    return;
                }
                if (!accounts.Exists(k => k.License == account.License))
                {
                    Debug.WriteLine($"{identifier} does not have a reserved slot.");
                    return;
                }
                accounts.RemoveAll(k => k.License == account.License && k.Steam == account.Steam);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.Delete(path);
                Debug.WriteLine($"{identifier} was removed from reserved slot list.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.Remove()");
            }
            await BaseScript.Delay(1000);
            return;
        }

        internal static async void OfflineReserve(int source, List<object> args, string raw)
        {
            try
            {
                if (args.Count() != 2)
                {
                    Debug.WriteLine($"This command requires two arguements. <Steam> or <License> and 0 or 1 or 2 or 3.");
                    return;
                }
                string identifier = args[0].ToString();
                string type = args[1].ToString();
                bool valid = type == "0" || type == "1" || type == "2" || type == "3";
                if (!valid)
                {
                    if (!valid)
                    {
                        Debug.WriteLine($"Reserved type must be 0 or 1 or 2 or 3.");
                        return;
                    }
                }
                ReservedAccount account = newwhitelist.Find(k => k.License == identifier || k.Steam == identifier);
                if (account != null)
                {
                    account.Steam = identifier;
                    account.License = identifier;
                    Debug.WriteLine($"Successfully change {identifier} in the temporary reserved list. They need to join the queue before server restart.");
                    return;
                }
                newwhitelist.Add(new ReservedAccount(identifier, identifier, (Reserved)int.Parse(type)));
                Debug.WriteLine($"Successfully added {identifier} to the temporary reserved list. They need to join the queue before server restart.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.NewWhitelist()");
            }
            await BaseScript.Delay(1000);
        }

        internal static async void AutoWhitelist(ReservedAccount account)
        {
            try
            {
                accounts.Add(account);
                string path = $"{directory}/{account.License}-{account.Steam}.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(account));
                Debug.WriteLine($"{account.License}-{account.Steam} was auto reserved.");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{Server_Queue.resourceName} - ERROR] - Server_Reserved.AutoWhitelist()");
            }
            await BaseScript.Delay(1000);
        }
    }

    class ServerAccount
    {
        public string License { get; set; }
        public string Steam { get; set; }

        public ServerAccount(string license, string steam)
        {
            License = license;
            Steam = steam;
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

    class SessionAccount
    {
        [JsonIgnore] public Player Source { get; set; }
        public string License { get; set; }
        public string Steam { get; set; }
        public string Name { get; set; }
        public string Handle { get; set; }
        [JsonIgnore] public dynamic Deferrals { get; set; }
        public SessionState State { get; set; } = SessionState.Queue;
        public DateTime QueueStartTime { get; set; }
        [JsonIgnore] public DateTime LoadStartTime { get; set; }
        [JsonIgnore] public DateTime DropStartTime { get; set; }
        public bool HasPriority { get; set; } = false;
        public Reserved ReservedType { get; set; } = Reserved.Public;
        public Reserved ReservedTypeUsed { get; set; } = Reserved.Public;
        [JsonIgnore] public bool IsConnected { get { return Source?.EndPoint != null; } }
        [JsonIgnore] public bool IsQueued { get { return State != SessionState.Grace && State != SessionState.Loading && State != SessionState.Active; } }

        public SessionAccount(Player source, string license, string steam, string name, string handle, dynamic deferrals, DateTime queuestarttime)
        {
            Source = source;
            License = license;
            Steam = steam;
            Name = name;
            Handle = handle;
            Deferrals = deferrals;
            QueueStartTime = queuestarttime;
        }
    }

    class Timer
    {
        static DateTime StartTime;
        static DateTime StopTime;
        public int ElapsedTime { get; private set; }

        public void Start()
        {
            StartTime = DateTime.UtcNow;
        }

        public void Stop()
        {
            StopTime = DateTime.UtcNow;
            ElapsedTime = (StopTime - StartTime).Milliseconds;
        }

    }

    enum SessionState
    {
        Queue,
        QueueGrace,
        Banned,
        Exiting,
        Grace,
        Loading,
        Active
    }

    enum Reserved
    {
        Public,
        Reserved1,
        Reserved2,
        Reserved3
    }
}