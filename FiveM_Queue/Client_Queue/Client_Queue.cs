using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using CitizenFX.Core;
using CitizenFX.Core.Native;

namespace Client_Queue
{
    public class Client_Queue : BaseScript
    {
        public Client_Queue()
        {
            Tick += Connected;
        }

        private async Task Connected()
        {
            try
            {
                while (!API.NetworkIsPlayerActive(API.PlayerId()))
                {
                    await Delay(1000);
                }
                TriggerServerEvent("fivemqueue: playerConnected");
                API.SendNuiMessage($@"{{ ""resname"" : ""{API.GetCurrentResourceName()}"" }}");
                Tick -= Connected;
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{API.GetCurrentResourceName()} - Client_Queue ERROR] - Connected()");
            }
        }
    }
}