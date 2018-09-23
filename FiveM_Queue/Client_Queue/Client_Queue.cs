using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Dynamic;
using Newtonsoft.Json;
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
                Tick -= Connected;
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{API.GetCurrentResourceName()} - Client_Queue ERROR] - Connected()");
            }
        }
    }
}
