using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Markup;

namespace Visualizer
{
    [PluginActionId("com.zaphop.visualizer.visualizer")]
    public class VisualizerPlugin : KeypadBase
    {
        private VisualizerMode _visualizerMode = VisualizerMode.GreenBars;
        private KnownColor _customColor = KnownColor.AliceBlue;
        private KnownColor _gradientColor = KnownColor.Coral;
        private string _targetDeviceID = String.Empty;
        private bool _autoPeakReset = false;

        private bool _wasEnabledOnLastTick = false;

        // https://stackoverflow.com/questions/18813112/naudio-fft-result-gives-intensity-on-all-frequencies-c-sharp/20414331#20414331

        private WasapiCapture _audioDevice = null;
        private double[] _audioValues;
        private double[] _fftValues;

        private bool _visualizerActive = false;

        private void UpdateSettings(JObject settings)
        {
            if (Enum.TryParse<VisualizerMode>(settings.Value<String>("visualizer_mode"), out _visualizerMode) == false)
                _visualizerMode = VisualizerMode.GreenBars;

            if (Enum.TryParse<KnownColor>(settings.Value<String>("custom_color"), out _customColor) == false)
                _customColor = KnownColor.AliceBlue;

            if (Enum.TryParse<KnownColor>(settings.Value<String>("gradient_color"), out _gradientColor) == false)
                _gradientColor = KnownColor.Coral;

            if (_targetDeviceID != settings.Value<String>("visualizer_device"))
            {
                _targetDeviceID = settings.Value<String>("visualizer_device");

                CaptureTargetAudioDevice();
            }

            _autoPeakReset = settings.Value<bool>("auto_peak_reset") == true;
        }

        public VisualizerPlugin(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            UpdateSettings(payload.Settings);

            _visualizerActive = true;

            
            Task.Run(() =>
            {
                using (var audioDeviceChangeNotifier = new AudioDeviceChangeNotifier())
                {
                    audioDeviceChangeNotifier.DefaultDeviceChanged += OnDefaultDeviceChanged;

                    while (_visualizerActive == true)
                    {
                        RenderData();
                        Thread.Sleep(20);
                    }
                }
            });

            connection.OnSendToPlugin += Connection_OnSendToPlugin;

            CaptureTargetAudioDevice();
            SendCurrentAudioDevices(connection);
        }

        private void Connection_OnSendToPlugin(object sender, SDEventReceivedEventArgs<SendToPlugin> e)
        {
            if(e.Event.Payload.TryGetValue("property_inspector", out var value) == true && value.Value<String>() == "propertyInspectorConnected")
                SendCurrentAudioDevices((SDConnection)sender);
        }

        private void SendCurrentAudioDevices(SDConnection connection)
        {
            NAudio.CoreAudioApi.MMDeviceEnumerator deviceEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();

            var devices = deviceEnum
                .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active | DeviceState.Unplugged)
                .Select(p => new { Name = p.FriendlyName, ID = p.ID });

            var deviceList = new { DeviceList = devices, SelectedDevice = _targetDeviceID }; 

            var jobjectDevices = JObject.FromObject(deviceList);

            connection.SendToPropertyInspectorAsync(jobjectDevices);
        }

        private void CaptureTargetAudioDevice()
        {
            if (_audioDevice != null)
                _audioDevice.StopRecording();

            if (String.IsNullOrWhiteSpace(_targetDeviceID) == true)
            {
                _audioDevice = new WasapiLoopbackCapture();
            }
            else
            {
                var targetDevice = GetTargetDevice(_targetDeviceID);

                if (targetDevice != null)
                {
                    if (targetDevice.DataFlow == DataFlow.Capture)
                        _audioDevice = new WasapiCapture(targetDevice);
                    else
                        _audioDevice = new WasapiLoopbackCapture(targetDevice);
                }
                else
                {
                    // If the target device could not be loaded, then just grab the default output device.
                    _audioDevice = new WasapiLoopbackCapture();
                }
            }

            WaveFormat fmt = _audioDevice.WaveFormat;
            _audioValues = new double[fmt.SampleRate / 100];
            double[] paddedAudio = FftSharp.Pad.ZeroPad(_audioValues);
            double[] fftMag = FftSharp.Transform.FFTpower(paddedAudio);
            _fftValues = new double[fftMag.Length];
            double fftPeriod = FftSharp.Transform.FFTfreqPeriod(fmt.SampleRate, fftMag.Length);

            _audioDevice.DataAvailable += OnDataAvailable;

            _audioDevice.StartRecording();
        }

        private MMDevice GetTargetDevice(string targetDeviceID)
        {
            NAudio.CoreAudioApi.MMDeviceEnumerator deviceEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();

            var targetDevice = deviceEnum
                .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active | DeviceState.Unplugged)
                .FirstOrDefault(p => p.ID == targetDeviceID);

           return targetDevice;
        }

        private void OnDefaultDeviceChanged(DataFlow dataFlow, Role deviceRole, string defaultDeviceId)
        {
            CaptureTargetAudioDevice();
        }


        //private void Connection_OnSendToPlugin(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.SendToPlugin> e)
        //{
        //    var received = JsonConvert.SerializeObject(e);

        //    Console.WriteLine(received);
        //}

        private double[] max = new double[8];
        private double[] peaks = new double[8];
        private double[] limit = { 0.35, 0.02, 0.02, 0.02, 0.01, 0.01, 0.007, 0.005 };
        private void RenderData()
        {
            try
            {
                double[] paddedAudio = FftSharp.Pad.ZeroPad(_audioValues);
                double[] fftMag = FftSharp.Transform.FFTmagnitude(paddedAudio);
                Array.Copy(fftMag, _fftValues, fftMag.Length);

                double[] peakFreqs = new double[8];
                int interval = (int) Math.Floor((double) _fftValues.Length / peakFreqs.Length);


                for (int i = 0; i < peakFreqs.Length; i++)
                {
                    // Decay old peak every tick. 
                    peaks[i] -= max[i] / 20;
                    
                    // Decay max over a longer time so that a loud sound doesn't pin the levels too low over time.
                    if(_autoPeakReset == true)
                        max[i] -= max[i] / 2000;


                    double rangeMax = 0;
                    for (int j = 0; j < interval; j++)
                    {
                        if (rangeMax < _fftValues[i * interval + j])
                            rangeMax = _fftValues[i * interval + j];
                    }

                    if (peaks[i] < rangeMax)
                        peaks[i] = rangeMax;

                    if (max[i] < rangeMax)
                        max[i] = rangeMax;

                    peakFreqs[i] = rangeMax;
                }

                // find the frequency peak
                int peakIndex = 0;
                for (int i = 0; i < fftMag.Length; i++)
                {
                    if (fftMag[i] > fftMag[peakIndex])
                        peakIndex = i;
                }

                double fftPeriod = FftSharp.Transform.FFTfreqPeriod(_audioDevice.WaveFormat.SampleRate, fftMag.Length);
                double peakFrequency = fftPeriod * peakIndex;

                Debug.WriteLine($"peak frequency: {peakFrequency}");
                for (int i = 0; i < peakFreqs.Length; i++)
                {
                    Debug.Write($"{peakFreqs[i].ToString("F4")}:");
                }

                Debug.WriteLine("");

                Bitmap bitmap = new Bitmap(72, 72);
                Graphics graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Black);

                Brush green = new SolidBrush(Color.FromKnownColor(KnownColor.Green));
                Brush yellow = new SolidBrush(Color.FromKnownColor(KnownColor.Yellow));
                Brush red = new SolidBrush(Color.FromKnownColor(KnownColor.Red));
                Brush black = new SolidBrush(Color.FromKnownColor(KnownColor.Black));
                Brush custom = new SolidBrush(Color.FromKnownColor(_customColor));
                Brush gradient = new LinearGradientBrush(new RectangleF(0, 0, bitmap.Width, bitmap.Height), Color.FromKnownColor(_customColor), Color.FromKnownColor(_gradientColor), 90);

                switch (_visualizerMode)
                {
                    case VisualizerMode.GreenBars:
                        graphics.FillRectangle(red, new RectangleF(0, 0, bitmap.Width, bitmap.Height));
                        graphics.FillRectangle(yellow, new RectangleF(0, bitmap.Height - bitmap.Height * .9f, bitmap.Width, bitmap.Height));
                        graphics.FillRectangle(green, new RectangleF(0, bitmap.Height - bitmap.Height * .7f, bitmap.Width, bitmap.Height));
                        break;

                    case VisualizerMode.CustomColor:
                        graphics.FillRectangle(custom, new RectangleF(0, 0, bitmap.Width, bitmap.Height));
                        break;

                    case VisualizerMode.Gradient:
                        graphics.FillRectangle(gradient, new RectangleF(0, 0, bitmap.Width, bitmap.Height));
                        break;

                    default:
                        throw new NotSupportedException(_visualizerMode.ToString());
                }
                

                for (int i = 0; i < 8; i++)
                {
                    graphics.DrawLine(new Pen(black), i * bitmap.Width / 8, 0, i * bitmap.Width / 8, bitmap.Height);

                    
                }

                // Declare brush while specifying color
                //Pen pen = new Pen(Color.FromKnownColor(KnownColor.Green), 1);

                for (int i = 0; i < peaks.Length; i++)
                {
                    //float height = (float)(peakFreqs[i] / FftValues[peakIndex] * bitmap.Height);
                    float height = (float)(peaks[i] / max[i] * bitmap.Height);
                    float width = bitmap.Width / peaks.Length;
                    float radius = (width) / 2;
                    float midpoint = radius;


                    for (int j = 1; j < width; j++)
                    {
                        float x = j - midpoint;
                        var angle = Math.Acos(x / radius);
                        var curveHeight = (float) Math.Sin(angle) * radius;

                        graphics.DrawLine(new Pen(black), (i * bitmap.Width / 8) + j, 0, (i * bitmap.Width / 8) + j, bitmap.Height - height - curveHeight + bitmap.Height / 8 / 2);
                    }

                    //graphics.FillRectangle(black, new RectangleF(i * width, 0, width, bitmap.Height - height + bitmap.Height / 8 / 2));

                    //if (_visualizerMode == VisualizerMode.CustomColor)
                    //    graphics.FillEllipse(custom, new RectangleF(i * bitmap.Width / 8, bitmap.Height - height, bitmap.Width / 8, bitmap.Height / 8));

                    //if (_visualizerMode == VisualizerMode.NegativeBalls)
                    //    graphics.FillEllipse(red, new RectangleF(i * bitmap.Width / 8, height, bitmap.Width / 8, bitmap.Height / 8));

                }

                //bitmap.Save(@"c:\1\out.png", ImageFormat.Png);
                Connection.SetImageAsync(Tools.ImageToBase64(bitmap, true));

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

        }

        void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            int bytesPerSamplePerChannel = _audioDevice.WaveFormat.BitsPerSample / 8;
            int bytesPerSample = bytesPerSamplePerChannel * _audioDevice.WaveFormat.Channels;
            int bufferSampleCount = e.Buffer.Length / bytesPerSample;

            if (bufferSampleCount >= _audioValues.Length)
            {
                bufferSampleCount = _audioValues.Length;
            }

            if (bytesPerSamplePerChannel == 2 && _audioDevice.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                for (int i = 0; i < bufferSampleCount; i++)
                    _audioValues[i] = BitConverter.ToInt16(e.Buffer, i * bytesPerSample);
            }
            else if (bytesPerSamplePerChannel == 4 && _audioDevice.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                for (int i = 0; i < bufferSampleCount; i++)
                    _audioValues[i] = BitConverter.ToInt32(e.Buffer, i * bytesPerSample);
            }
            else if (bytesPerSamplePerChannel == 4 && _audioDevice.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < bufferSampleCount; i++)
                    _audioValues[i] = BitConverter.ToSingle(e.Buffer, i * bytesPerSample);
            }
            else
            {
                throw new NotSupportedException(_audioDevice.WaveFormat.ToString());
            }

            //if (this.InvokeRequired)
            //{
            //    this.BeginInvoke(new EventHandler<WaveInEventArgs>(OnDataAvailable), sender, e);
            //}
            //else
            //{
            //byte[] buffer = e.Buffer;
            //int bytesRecorded = e.BytesRecorded;
            //int bufferIncrement = waveIn.WaveFormat.BlockAlign;

            //for (int index = 0; index < bytesRecorded; index += bufferIncrement)
            //{
            //    float sample32 = BitConverter.ToSingle(buffer, index);
            //    sampleAggregator.Add(sample32);
            //}
            //}
        }

        //double[] data = new double[512];
        //void FftCalculated(object sender, FftEventArgs e)
        //{
        //    for (int j = 0; j < e.Result.Length / 1; j++)
        //    {
        //        double magnitude = Math.Sqrt(e.Result[j].X * e.Result[j].X + e.Result[j].Y * e.Result[j].Y);
        //        double dbValue = 20 * Math.Log10(magnitude);

        //        data[j] = dbValue;
        //    }

        //    double d = 0;

        //    for (int i = 20; i < 89; i++)
        //    {

        //        d += data[i];
        //    }

        //    double m = 0;

        //    for (int i = 150; i < 255; i++)
        //    {

        //        m += data[i];
        //    }

        //    double t = 0;

        //    for (int i = 300; i < 512; i++)
        //    {

        //        t += data[i];
        //    }

        //    double total = data.Sum(x => Math.Abs(x));
        //    d /= total;
        //    m /= total;
        //    t /= total;

        //    Debug.WriteLine("" + d + " |||| " + m + " |||| " + t);
        //}

        public override void Dispose()
        {
            _visualizerActive = false;
        }

        public override void KeyPressed(KeyPayload payload)
        {
        }

        public override void KeyReleased(KeyPayload payload) { }


        public override void OnTick()
        {
          
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            UpdateSettings(payload.Settings);

        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) 
        {
            UpdateSettings(payload.Settings);
        }


        private void UpdateToggleStatus()
        {
            var d = Connection.DeviceInfo();
            var g = Connection.GetGlobalSettingsAsync();
            g.Wait();
            var s = Connection.GetSettingsAsync();
            s.Wait();




            if (_wasEnabledOnLastTick == false)
            {
                Connection.SetImageAsync(Tools.FileToBase64("Images\\green72.png", true));
                _wasEnabledOnLastTick = true;
            }
            else
            {
                Connection.SetImageAsync(Tools.FileToBase64("Images\\gray72.png", true));
                _wasEnabledOnLastTick = false;
            }
        }
    }
}