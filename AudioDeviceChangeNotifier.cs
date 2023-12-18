using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Visualizer
{
    // Based on some sample code found here:
    // https://stackoverflow.com/questions/6163119/handling-changed-audio-device-event-in-c-sharp
    internal class AudioDeviceChangeNotifier : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient, IDisposable
    {
        private NAudio.CoreAudioApi.MMDeviceEnumerator _deviceEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();

        public AudioDeviceChangeNotifier()
        {
            if (System.Environment.OSVersion.Version.Major < 6)
                throw new NotSupportedException("This functionality is only supported on Windows Vista or newer.");

            _deviceEnum.RegisterEndpointNotificationCallback(this);
        }

        public delegate void DefaultDeviceChangedHandler(DataFlow dataFlow, Role deviceRole, string defaultDeviceId);
        /// <summary>
        /// Raised when the default audio device is changed. 
        /// </summary>
        public event DefaultDeviceChangedHandler DefaultDeviceChanged;

        /// <summary>
        ///  Triggered by NAudio.CoreAudioApi.MMDeviceEnumerator when the default device changes. 
        /// </summary>
        /// <param name="dataFlow"></param>
        /// <param name="deviceRole"></param>
        /// <param name="defaultDeviceId"></param>
        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role deviceRole, string defaultDeviceId)
        {
            Debug.WriteLine($"AudioDeviceChangeNotifier::OnDefaultDeviceChanged - dataFlow: {dataFlow}, deviceRole: {deviceRole}, defaultDeviceId: {defaultDeviceId}");

            if (DefaultDeviceChanged != null)
                DefaultDeviceChanged(dataFlow, deviceRole, defaultDeviceId);
        }

        public delegate void DeviceAddedHandler(string deviceId);
        /// <summary>
        /// Raised when a new audio device is added.
        /// </summary>
        public event DeviceAddedHandler DeviceAdded;

        /// <summary>
        /// Triggered by NAudio.CoreAudioApi.MMDeviceEnumerator when an audio device is added. 
        /// </summary>
        /// <param name="deviceId"></param>
        public void OnDeviceAdded(string deviceId)
        {
            Debug.WriteLine($"AudioDeviceChangeNotifier::OnDeviceAdded - deviceId: {deviceId}");

            if (DeviceAdded != null)
                DeviceAdded(deviceId);
        }

        public delegate void DeviceRemovedHandler(string deviceId);
        /// <summary>
        /// Raised when an audio device is removed.
        /// </summary>
        public event DeviceRemovedHandler DeviceRemoved;

        /// <summary>
        /// Triggered by NAudio.CoreAudioApi.MMDeviceEnumerator when an audio device is removed. 
        /// </summary>
        /// <param name="deviceId"></param>
        public void OnDeviceRemoved(string deviceId)
        {
            Debug.WriteLine($"AudioDeviceChangeNotifier::OnDeviceRemoved - deviceId: {deviceId}");

            if (DeviceAdded != null)
                DeviceAdded(deviceId);
        }

        public delegate void DeviceStateChangedHandler(string deviceId, DeviceState newState);
        /// <summary>
        /// Raised when an audio device's state is changed.
        /// </summary>
        public event DeviceStateChangedHandler DeviceStateChanged;

        /// <summary>
        /// Triggered by NAudio.CoreAudioApi.MMDeviceEnumerator when an audio device's state is changed. 
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="newState"></param>
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            Debug.WriteLine($"AudioDeviceChangeNotifier::OnDeviceStateChanged - deviceId: {deviceId}, newState: {newState}");

            if (DeviceStateChanged != null)
                DeviceStateChanged(deviceId, newState);
        }
        
        public delegate void PropertyValueChangedHandler(string deviceId);
        /// <summary>
        /// Raised when a property value is changed.
        /// </summary>
        public event PropertyValueChangedHandler PropertyValueChanged;

        /// <summary>
        /// Triggered by NAudio.CoreAudioApi.MMDeviceEnumerator when an audio device's property is changed. 
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="propertyKey"></param>
        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
        {
            Debug.WriteLine($"AudioDeviceChangeNotifier::OnPropertyValueChanged - deviceId: {deviceId}, propertyKey: {propertyKey}");

            if (PropertyValueChanged != null)
                PropertyValueChanged(deviceId);
        }

        public void Dispose()
        {
            _deviceEnum.UnregisterEndpointNotificationCallback(this);
        }
    }
}
