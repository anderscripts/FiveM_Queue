using System;
using System.Collections.Generic;
using System.Linq;
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
            EventHandlers["fivemqueue: sessionResponse"] += new Action<ExpandoObject>(SessionResponse);
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
                List<dynamic> sessionAccounts = JsonConvert.DeserializeObject<List<dynamic>>(session);
                string text = "";
                sessionAccounts.ForEach(k =>
                {
                    text = $"{text}<tr>" +
                    $"<td>{k["Handle"]}</td>" +
                    $"<td>{k["License"]}</td>" +
                    $"<td>{k["Steam"]}</td>" +
                    $"<td>{k["Name"]}</td>" +
                    $"<td>{Enum.GetName(typeof(Reserved), (int)k["Reserved"])}</td>" +
                    $"<td>{Enum.GetName(typeof(Reserved), (int)k["ReservedUsed"])}</td>" +
                    $"<td>{k["Priority"]}</td>" +
                    $"<td>{Enum.GetName(typeof(SessionState), (int)k["State"])}</td>" +
                    $"<td><button class=button onclick=Change('{k["License"]}')>Change</button></td>" +
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
                API.ExecuteCommand($"q_addban {data.License}");
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
                API.ExecuteCommand($"q_kick {data.License}");
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
                if (data.Value == "False")
                {
                    API.ExecuteCommand($"q_removepriority {data.License}");
                    return;
                }
                else
                {
                    API.ExecuteCommand($"q_addpriority {data.License} {int.Parse(data.Value.ToString())}");
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
                    API.ExecuteCommand($"q_removereserve {data.License}");
                }
                else
                {
                    API.ExecuteCommand($"q_addreserve {data.License} {value}");
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{resourceName} - Admin_Panel ERROR] - ChangeReserved()");
            }
        }
    }

    enum SessionState
    {
        Queue,
        Grace,
        Loading,
        Active
    }

    enum Reserved
    {
        Reserved1 = 1,
        Reserved2,
        Reserved3,
        Public
    }
}