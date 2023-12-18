using BarRaider.SdTools;
using NAudio.Wave;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Visualizer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Uncomment this line of code to allow for debugging
            //while (!System.Diagnostics.Debugger.IsAttached) { System.Threading.Thread.Sleep(100); }

            //foreach (var color in Enum.GetNames(typeof(System.Drawing.KnownColor)))
            //{
            //    Console.WriteLine($"<option value=\"{color}\">{color}</option>");
            //}

            //using (var a = new AudioDeviceChangeNotifier())
            //{
            //    a.DefaultDeviceChanged += A_DefaultDeviceChanged;

            //}

            //while (true)
            //    System.Threading.Thread.Sleep(100);

            //var s = JObject.Parse($"{{'currentTime': '{DateTime.Now.ToString()}'}}");

            //for (int i = 0; i < WaveIn.DeviceCount; i++)
            //{
            //    Console.WriteLine(WaveIn.GetCapabilities(i).ProductName);
            //}

            SDWrapper.Run(args);
        }

        private static void A_DefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow dataFlow, NAudio.CoreAudioApi.Role deviceRole, string defaultDeviceId)
        {
            Console.WriteLine($"Yay: {defaultDeviceId}");
        }
    }
}
