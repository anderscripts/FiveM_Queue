using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Dynamic;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using Newtonsoft.Json;

namespace Client_Queue
{
    class Admin_Panel : BaseScript
    {
        internal static string resourceName = API.GetCurrentResourceName();

        public Admin_Panel()
        {
            EventHandlers["onResourceStop"] += new Action<string>(OnResourceStop);
            EventHandlers["fivemqueue: sessionResponse"] += new Action<dynamic>(SessionResponse);
            API.RegisterNuiCallbackType("ClosePanel");
            EventHandlers["__cfx_nui:ClosePanel"] += new Action<ExpandoObject>(ClosePanel);
            API.RegisterNuiCallbackType("RefreshPanel");
            EventHandlers["__cfx_nui:RefreshPanel"] += new Action<ExpandoObject>(RefreshPanel);
            API.RegisterNuiCallbackType("BanUser");
            EventHandlers["__cfx_nui:BanUser"] += new Action<ExpandoObject>(BanUser);
            API.RegisterNuiCallbackType("KickUser");
            EventHandlers["__cfx_nui:KickUser"] += new Action<ExpandoObject>(KickUser);
            API.RegisterNuiCallbackType("ChangePriority");
            EventHandlers["__cfx_nui:ChangePriority"] += new Action<ExpandoObject>(ChangePriority);
            API.RegisterNuiCallbackType("ChangeReserved");
            EventHandlers["__cfx_nui:ChangeReserved"] += new Action<ExpandoObject>(ChangeReserved);
        }

        private void OnResourceStop(string name)
        {
            try
            {
                if (name == resourceName)
                {
                    API.SetNuiFocus(false, false);
                    return;
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - OnResourceStop()");
            }
        }

        private void SessionResponse(dynamic session)
        {
            try
            {
                List<SessionAccount> sessionAccounts = JsonConvert.DeserializeObject<List<SessionAccount>>(session);
                sessionAccounts = sessionAccounts.OrderBy(k => k.State == SessionState.Active).ThenBy(k => k.Handle).ToList();
                string text = "";
                sessionAccounts.ForEach(k =>
                {
                    text = $"{text}<tr>" +
                    $"<td>{k.Handle}</td>" +
                    $"<td>{k.License}</td>" +
                    $"<td>{k.Steam}</td>" +
                    $"<td>{k.Name}</td>" +
                    $"<td>{Enum.GetName(typeof(Reserved), k.ReservedType)}</td>" +
                    $"<td>{Enum.GetName(typeof(Reserved), k.ReservedTypeUsed)}</td>" +
                    $"<td>{k.HasPriority}</td>" +
                    $"<td>{Enum.GetName(typeof(SessionState), k.State)}</td>" +
                    $"<td><button class=button onclick=Change('{k.Steam}')>Change</button></td>" +
                    $"</tr>";
                });
                API.SendNuiMessage($@"{{ ""sessionlist"" : ""{text}"" }}");
                API.SetNuiFocus(true, true);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - SessionResponse()");
            }
        }

        private void ClosePanel(dynamic data)
        {
            try
            {
                API.SetNuiFocus(false, false);
                API.SendNuiMessage($@"{{ ""panel"" : ""close"" }}");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - ClosePanel()");
            }
        }

        private void RefreshPanel(dynamic data)
        {
            try
            {
                API.SetNuiFocus(false, false);
                API.SendNuiMessage($@"{{ ""panel"" : ""close"" }}");
                API.ExecuteCommand("q_session");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - RefreshPanel()");
            }
        }

        private void BanUser(dynamic data)
        {
            try
            {
                API.ExecuteCommand($"q_addban {data.Steam}");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - BanUser()");
            }
        }

        private void KickUser(dynamic data)
        {
            try
            {
                API.ExecuteCommand($"q_kick {data.Steam}");
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - KickUser()");
            }
        }

        private void ChangePriority(dynamic data)
        {
            try
            {
                if (data.Value == "True")
                {
                    API.ExecuteCommand($"q_addpriority {data.Steam}");
                }
                else if (data.Value == "False")
                {
                    API.ExecuteCommand($"q_removepriority {data.Steam}");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - ChangePriority()");
            }
        }

        private void ChangeReserved(dynamic data)
        {
            try
            {
                int value = int.Parse(data.Value);
                if (value == 0)
                {
                    API.ExecuteCommand($"q_removereserve {data.Steam}");
                }
                else
                {
                    API.ExecuteCommand($"q_addreserve {data.Steam} {value}");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - ChangeReserved()");
            }
        }
    }

    class SessionAccount
    {
        public string License { get; set; }
        public string Steam { get; set; }
        public string Name { get; set; }
        public string Handle { get; set; }
        public SessionState State { get; set; }
        public DateTime QueueStartTime { get; set; }
        public bool HasPriority { get; set; }
        public Reserved ReservedType { get; set; }
        public Reserved ReservedTypeUsed { get; set; }

        public SessionAccount(string license, string steam, string name, string handle, SessionState state, DateTime queuestarttime, bool haspriority, Reserved reservedtype, Reserved reservedused)
        {
            License = license;
            Steam = steam;
            Name = name;
            Handle = handle;
            State = state;
            QueueStartTime = queuestarttime;
            HasPriority = haspriority;
            ReservedType = reservedtype;
            ReservedTypeUsed = reservedused;
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