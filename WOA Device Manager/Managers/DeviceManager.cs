﻿using FastBoot;
using SAPTeam.AndroCtrl.Adb;
using SAPTeam.AndroCtrl.Adb.Receivers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Windows.Devices.Enumeration;
using WOADeviceManager.Helpers;

namespace WOADeviceManager.Managers
{
    public class DeviceManager
    {
        private readonly DeviceWatcher watcher;

        private static DeviceManager _instance;
        public static DeviceManager Instance
        {
            get
            {
                _instance ??= new DeviceManager();
                return _instance;
            }
        }

        private static Device device;
        public static Device Device
        {
            get
            {
                device ??= new Device();
                return device;
            }
            private set => device = value;
        }

        public delegate void DeviceFoundEventHandler(object sender, Device device);
        public delegate void DeviceConnectedEventHandler(object sender, Device device);
        public delegate void DeviceDisconnectedEventHandler(object sender, Device device);
        public static event DeviceFoundEventHandler DeviceFoundEvent;
        public static event DeviceConnectedEventHandler DeviceConnectedEvent;
        public static event DeviceDisconnectedEventHandler DeviceDisconnectedEvent;

        private DeviceManager()
        {
            device ??= new Device();

            watcher = DeviceInformation.CreateWatcher();
            watcher.Added += DeviceAdded;
            watcher.Removed += DeviceRemoved;
            watcher.Updated += Watcher_Updated;
            watcher.Start();
        }

        private void Watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            _ = args.Properties.TryGetValue("System.Devices.InterfaceEnabled", out object? IsInterfaceEnabledObjectValue);
            bool IsInterfaceEnabled = (bool?)IsInterfaceEnabledObjectValue ?? false;

            if (!IsInterfaceEnabled && args.Id == Device.ID)
            {
                Device.State = Device.DeviceStateEnum.DISCONNECTED;
                Device.ID = null;
                Device.Name = null;
                Device.Variant = null;
                // TODO: Device.Product = Device.Product;

                DeviceDisconnectedEvent?.Invoke(this, device);
                return;
            }

            // Normal:
            // Surface Duo Fastboot
            if (args.Id.Contains("USB#VID_045E&PID_0C2F#"))
            {
                try
                {
                    FastBootTransport fastBootTransport = new(args.Id);

                    bool result = fastBootTransport.GetVariable("product", out string productGetVar);
                    string ProductName = !result ? null : productGetVar;
                    result = fastBootTransport.GetVariable("is-userspace", out productGetVar);
                    string IsUserSpace = !result ? null : productGetVar;

                    switch (ProductName)
                    {
                        case "surfaceduo":
                        case "duo":
                            {
                                if (IsUserSpace == "yes")
                                {
                                    Device.State = Device.DeviceStateEnum.FASTBOOTD;
                                }
                                else
                                {
                                    Device.State = Device.DeviceStateEnum.BOOTLOADER;
                                }
                                Device.ID = args.Id;
                                Device.Name = "Surface Duo";
                                Device.Variant = "";
                                Device.Product = Device.DeviceProduct.Epsilon;

                                if (Device.FastBootTransport != null)
                                {
                                    Device.FastBootTransport.Dispose();
                                }
                                Device.FastBootTransport = fastBootTransport;

                                DeviceDisconnectedEvent?.Invoke(this, device);
                                return;
                            }
                        case "surfaceduo2":
                        case "duo2":
                            {
                                if (IsUserSpace == "yes")
                                {
                                    Device.State = Device.DeviceStateEnum.FASTBOOTD;
                                }
                                else
                                {
                                    Device.State = Device.DeviceStateEnum.BOOTLOADER;
                                }
                                Device.ID = args.Id;
                                Device.Name = "Surface Duo 2";
                                Device.Variant = "";
                                Device.Product = Device.DeviceProduct.Zeta;

                                if (Device.FastBootTransport != null)
                                {
                                    Device.FastBootTransport.Dispose();
                                }
                                Device.FastBootTransport = fastBootTransport;

                                DeviceDisconnectedEvent?.Invoke(this, device);
                                return;
                            }
                    }

                    fastBootTransport.Dispose();
                }
                catch { }
            }
            // Normal:
            // Surface Duo ADB
            // Surface Duo ADB Sideload
            // Surface Duo Composite ADB
            // Surface Duo Composite ADB Tether
            // Surface Duo Composite ADB File Transfer
            // Surface Duo Composite ADB PTP
            // Surface Duo Composite ADB MIDI
            //
            // Custom:
            // Surface Duo TWRP
            // Surface Duo 2 TWRP
            else if (args.Id.Contains("USB#VID_045E&PID_0C26#") ||
             args.Id.Contains("USB#VID_045E&PID_0C30#") ||
             args.Id.Contains("USB#VID_045E&PID_0C26&MI_01#") ||
             args.Id.Contains("USB#VID_045E&PID_0C28&MI_02#") ||
             args.Id.Contains("USB#VID_045E&PID_0C2A&MI_01#") ||
             args.Id.Contains("USB#VID_045E&PID_0C2C&MI_01#") ||
             args.Id.Contains("USB#VID_045E&PID_0C2E&MI_02#") ||
             args.Id.Contains("USB#VID_05C6&PID_9039&MI_00") ||
             args.Id.Contains("USB#VID_18D1&PID_D001"))
            {
                try
                {
                    DeviceData adbDeviceData = GetADBDeviceDataFromUSBID(args.Id);

                    if (args.Id.Contains("USB#VID_05C6&PID_9039&MI_00"))
                    {
                        Device.State = Device.DeviceStateEnum.TWRP;
                        Device.ID = args.Id;
                        Device.Name = "Surface Duo";
                        Device.Variant = "";
                        Device.Product = Device.DeviceProduct.Epsilon;

                        DeviceDisconnectedEvent?.Invoke(this, device);
                        return;
                    }
                    else if (args.Id.Contains("USB#VID_18D1&PID_D001"))
                    {
                        Device.State = Device.DeviceStateEnum.TWRP;
                        Device.ID = args.Id;
                        Device.Name = "Surface Duo 2";
                        Device.Variant = "";
                        Device.Product = Device.DeviceProduct.Zeta;

                        DeviceDisconnectedEvent?.Invoke(this, device);
                        return;
                    }
                    else
                    {
                        // TODO: Recovery differentiation

                        ConsoleOutputReceiver receiver = new();
                        try
                        {
                            ADBManager.Client.ExecuteRemoteCommand("getprop ro.product.device", adbDeviceData, receiver);
                        }
                        catch (Exception) { }
                        string ProductDevice = receiver.ToString().Trim();

                        switch (ProductDevice)
                        {
                            case "duo":
                                {
                                    Device.State = Device.DeviceStateEnum.ANDROID_ADB_ENABLED;
                                    Device.ID = args.Id;
                                    Device.Name = "Surface Duo";

                                    receiver = new();
                                    try
                                    {
                                        ADBManager.Client.ExecuteRemoteCommand("getprop ro.product.name", adbDeviceData, receiver);
                                    }
                                    catch (Exception) { }
                                    string ProductName = receiver.ToString().Trim();

                                    switch (ProductName)
                                    {
                                        case "duo":
                                            {
                                                Device.Variant = "GEN";
                                                break;
                                            }
                                        case "duo-att":
                                            {
                                                Device.Variant = "ATT";
                                                break;
                                            }
                                        case "duo-eu":
                                            {
                                                Device.Variant = "EEA";
                                                break;
                                            }
                                        default:
                                            {
                                                Device.Variant = ProductName;
                                                break;
                                            }
                                    }

                                    Device.Product = Device.DeviceProduct.Epsilon;

                                    DeviceDisconnectedEvent?.Invoke(this, device);
                                    return;
                                }
                            case "duo2":
                                {
                                    Device.State = Device.DeviceStateEnum.ANDROID_ADB_ENABLED;
                                    Device.ID = args.Id;
                                    Device.Name = "Surface Duo 2";
                                    Device.Variant = "";
                                    Device.Product = Device.DeviceProduct.Zeta;

                                    DeviceDisconnectedEvent?.Invoke(this, device);
                                    return;
                                }
                        }
                    }
                }
                catch { }
            }
            else if (args.Id.Contains("USB#VID_045E&PID_0C29"))
            {
                Device.State = Device.DeviceStateEnum.ANDROID;
                Device.ID = args.Id;
                Device.Name = "N/A";
                Device.Variant = "";
                // TODO: Device.Product = Device.Product;

                DeviceDisconnectedEvent?.Invoke(this, device);
            }
        }

        private void DeviceAdded(DeviceWatcher sender, DeviceInformation args)
        {
            // Adb Enabled Android example:
            //
            // Args.ID: \\?\USB#VID_045E&PID_0C26#0F0012E214600A#{dee824ef-729b-4a0e-9c14-b7117d33a817}
            //
            // SurfaceDuoUsb.inf list:
            //
            // %Fastboot%              = Fastboot_Install, USB\VID_045E&PID_0C2F
            // %Adb%                   = Adb_Install, USB\VID_045E&PID_0C26
            // %AdbSideload%           = AdbSideload_Install, USB\VID_045E&PID_0C30
            // %AdbComposite%          = AdbComposite_Install, USB\VID_045E&PID_0C26&MI_01
            // %AdbCompositeTether%    = AdbCompositeTether_Install, USB\VID_045E&PID_0C28&MI_02
            // %AdbCompositeFT%        = AdbCompositeFT_Install, USB\VID_045E&PID_0C2A&MI_01
            // %AdbCompositePTP%       = AdbCompositePTP_Install, USB\VID_045E&PID_0C2C&MI_01
            // %AdbCompositeMIDI%      = AdbCompositeMIDI_Install, USB\VID_045E&PID_0C2E&MI_02
            //
            // Fastboot           = "Surface Duo Fastboot"
            // Adb                = "Surface Duo ADB"
            // AdbSideload        = "Surface Duo ADB Sideload"
            // AdbComposite       = "Surface Duo Composite ADB"
            // AdbCompositeTether = "Surface Duo Composite ADB Tether"
            // AdbCompositeFT     = "Surface Duo Composite ADB File Transfer"
            // AdbCompositePTP    = "Surface Duo Composite ADB PTP"
            // AdbCompositeMIDI   = "Surface Duo Composite ADB MIDI"

            bool IsInterfaceEnabled = args.IsEnabled;
            if (!IsInterfaceEnabled)
            {
                return;
            }

            // Normal:
            // Surface Duo Fastboot
            if (args.Id.Contains("USB#VID_045E&PID_0C2F#"))
            {
                try
                {
                    FastBootTransport fastBootTransport = new(args.Id);

                    bool result = fastBootTransport.GetVariable("product", out string productGetVar);
                    string ProductName = !result ? null : productGetVar;
                    result = fastBootTransport.GetVariable("is-userspace", out productGetVar);
                    string IsUserSpace = !result ? null : productGetVar;

                    switch (ProductName)
                    {
                        case "surfaceduo":
                        case "duo":
                            {
                                if (IsUserSpace == "yes")
                                {
                                    Device.State = Device.DeviceStateEnum.FASTBOOTD;
                                }
                                else
                                {
                                    Device.State = Device.DeviceStateEnum.BOOTLOADER;
                                }
                                Device.ID = args.Id;
                                Device.Name = "Surface Duo";
                                Device.Variant = "";
                                Device.Product = Device.DeviceProduct.Epsilon;

                                if (Device.FastBootTransport != null)
                                {
                                    Device.FastBootTransport.Dispose();
                                }
                                Device.FastBootTransport = fastBootTransport;

                                DeviceDisconnectedEvent?.Invoke(this, device);
                                return;
                            }
                        case "surfaceduo2":
                        case "duo2":
                            {
                                if (IsUserSpace == "yes")
                                {
                                    Device.State = Device.DeviceStateEnum.FASTBOOTD;
                                }
                                else
                                {
                                    Device.State = Device.DeviceStateEnum.BOOTLOADER;
                                }
                                Device.ID = args.Id;
                                Device.Name = "Surface Duo 2";
                                Device.Variant = "";
                                Device.Product = Device.DeviceProduct.Zeta;

                                if (Device.FastBootTransport != null)
                                {
                                    Device.FastBootTransport.Dispose();
                                }
                                Device.FastBootTransport = fastBootTransport;

                                DeviceDisconnectedEvent?.Invoke(this, device);
                                return;
                            }
                    }
                }
                catch { }
            }
            // Normal:
            // Surface Duo ADB
            // Surface Duo ADB Sideload
            // Surface Duo Composite ADB
            // Surface Duo Composite ADB Tether
            // Surface Duo Composite ADB File Transfer
            // Surface Duo Composite ADB PTP
            // Surface Duo Composite ADB MIDI
            //
            // Custom:
            // Surface Duo TWRP
            // Surface Duo 2 TWRP
            else if (args.Id.Contains("USB#VID_045E&PID_0C26#") ||
             args.Id.Contains("USB#VID_045E&PID_0C30#") ||
             args.Id.Contains("USB#VID_045E&PID_0C26&MI_01#") ||
             args.Id.Contains("USB#VID_045E&PID_0C28&MI_02#") ||
             args.Id.Contains("USB#VID_045E&PID_0C2A&MI_01#") ||
             args.Id.Contains("USB#VID_045E&PID_0C2C&MI_01#") ||
             args.Id.Contains("USB#VID_045E&PID_0C2E&MI_02#") ||
             args.Id.Contains("USB#VID_05C6&PID_9039&MI_00") ||
             args.Id.Contains("USB#VID_18D1&PID_D001"))
            {
                try
                {
                    DeviceData adbDeviceData = GetADBDeviceDataFromUSBID(args.Id);

                    if (args.Id.Contains("USB#VID_05C6&PID_9039&MI_00"))
                    {
                        Device.State = Device.DeviceStateEnum.TWRP;
                        Device.ID = args.Id;
                        Device.Name = "Surface Duo";
                        Device.Variant = "";
                        Device.Product = Device.DeviceProduct.Epsilon;

                        DeviceDisconnectedEvent?.Invoke(this, device);
                        return;
                    }
                    else if (args.Id.Contains("USB#VID_18D1&PID_D001"))
                    {
                        Device.State = Device.DeviceStateEnum.TWRP;
                        Device.ID = args.Id;
                        Device.Name = "Surface Duo 2";
                        Device.Variant = "";
                        Device.Product = Device.DeviceProduct.Zeta;

                        DeviceDisconnectedEvent?.Invoke(this, device);
                        return;
                    }
                    else
                    {
                        // TODO: Recovery differentiation

                        ConsoleOutputReceiver receiver = new();
                        try
                        {
                            ADBManager.Client.ExecuteRemoteCommand("getprop ro.product.device", adbDeviceData, receiver);
                        }
                        catch (Exception) { }
                        string ProductDevice = receiver.ToString().Trim();

                        switch (ProductDevice)
                        {
                            case "duo":
                                {
                                    Device.State = Device.DeviceStateEnum.ANDROID_ADB_ENABLED;
                                    Device.ID = args.Id;
                                    Device.Name = "Surface Duo";

                                    receiver = new();
                                    try
                                    {
                                        ADBManager.Client.ExecuteRemoteCommand("getprop ro.product.name", adbDeviceData, receiver);
                                    }
                                    catch (Exception) { }
                                    string ProductName = receiver.ToString().Trim();

                                    switch (ProductName)
                                    {
                                        case "duo":
                                            {
                                                Device.Variant = "GEN";
                                                break;
                                            }
                                        case "duo-att":
                                            {
                                                Device.Variant = "ATT";
                                                break;
                                            }
                                        case "duo-eu":
                                            {
                                                Device.Variant = "EEA";
                                                break;
                                            }
                                        default:
                                            {
                                                Device.Variant = ProductName;
                                                break;
                                            }
                                    }

                                    Device.Product = Device.DeviceProduct.Epsilon;

                                    DeviceDisconnectedEvent?.Invoke(this, device);
                                    return;
                                }
                            case "duo2":
                                {
                                    Device.State = Device.DeviceStateEnum.ANDROID_ADB_ENABLED;
                                    Device.ID = args.Id;
                                    Device.Name = "Surface Duo 2";
                                    Device.Variant = "";
                                    Device.Product = Device.DeviceProduct.Zeta;

                                    DeviceDisconnectedEvent?.Invoke(this, device);
                                    return;
                                }
                        }
                    }
                }
                catch { }
            }
            else if (args.Id.Contains("USB#VID_045E&PID_0C29"))
            {
                Device.State = Device.DeviceStateEnum.ANDROID;
                Device.ID = args.Id;
                Device.Name = args.Name;
                Device.Variant = "";
                Device.Product = args.Name.Contains("Duo 2") ? Device.DeviceProduct.Zeta : Device.DeviceProduct.Epsilon;

                DeviceDisconnectedEvent?.Invoke(this, device);
            }
        }

        private void DeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            if (Device.ID != args.Id)
            {
                return;
            }

            Device.State = Device.DeviceStateEnum.DISCONNECTED;
            Device.ID = null;
            Device.Name = null;
            Device.Variant = null;
            // TODO: Device.Product = Device.Product;

            if (Device.FastBootTransport != null)
            {
                Device.FastBootTransport.Dispose();
                Device.FastBootTransport = null;
            }

            DeviceDisconnectedEvent?.Invoke(this, device);
        }

        internal static DeviceData GetADBDeviceDataFromUSBID(string USB)
        {
            string serialNumberFromUSBID = GetSerialNumberFromUSBID(USB);

            List<DeviceData> connectedDevices = ADBManager.Client.GetDevices();
            foreach (DeviceData connectedDevice in connectedDevices)
            {
                if (connectedDevice.Serial.Equals(serialNumberFromUSBID))
                {
                    return connectedDevice;
                }
            }

            Thread.Sleep(1000);

            connectedDevices = ADBManager.Client.GetDevices();
            foreach (DeviceData connectedDevice in connectedDevices)
            {
                if (connectedDevice.Serial.Equals(serialNumberFromUSBID))
                {
                    return connectedDevice;
                }
            }

            // Fall back...
            if (connectedDevices.Count == 1)
            {
                return connectedDevices[0];
            }

            throw new Exception("Device not found!");
        }

        private static string GetSerialNumberFromUSBID(string USB)
        {
            string pattern = @"#(\d+)#";
            Match match = Regex.Match(USB, pattern);
            return match.Groups[1].Value;
        }
    }
}
