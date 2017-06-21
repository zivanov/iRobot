﻿/*
 MIT License

Copyright (c) [2016] [Orlin Dimitrov]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial SerialPortions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.Collections.Generic;

using AForge.Video.DirectShow;

using iRobot.RoombaSharp;
using iRobot.Events;

using RoombaSharp.Video;
using RoombaSharp.Connectors;
using RoombaSharp.Adapters;

namespace RoombaSharp
{
    public partial class MainForm : Form
    {

        #region Variables

        /// <summary>
        /// Robot communicator.
        /// </summary>
        private Communicator roombaCommunicator;

        /// <summary>
        /// Robot
        /// </summary>
        private Roomba robot;

        /// <summary>
        /// Clean LED intensity.
        /// </summary>
        private byte cleanLedIntensity = 0;

        /// <summary>
        /// Clean LED color.
        /// </summary>
        private byte cleanLedColor = 0;

        /// <summary>
        /// Log messages sync lock object.
        /// </summary>
        private object syncLockLogs = new object();

        /// <summary>
        /// Video capture device.
        /// </summary>
        private VideoCaptureDevice videoDevice = null;

        /// <summary>
        /// 
        /// </summary>
        private VideoDevice[] videoDevices;

        /// <summary>
        /// Dump image.
        /// </summary>
        private Bitmap dumpImage;

        /// <summary>
        /// Send image timer.
        /// </summary>
        private System.Windows.Forms.Timer sendImageTimer;
        
        /// <summary>
        /// Connector
        /// </summary>
        private DataConnector mqttCommunicator;


        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Search for serial port.
        /// </summary>
        private void SearchForPorts()
        {
            this.tsmiConnect.DropDown.Items.Clear();

            string[] portNames = System.IO.Ports.SerialPort.GetPortNames();

            if (portNames.Length == 0)
            {
                return;
            }

            foreach (string item in portNames)
            {
                //store the each retrieved available prot names into the MenuItems...
                this.tsmiConnect.DropDown.Items.Add(item);
            }

            foreach (ToolStripMenuItem item in this.tsmiConnect.DropDown.Items)
            {
                item.Click += tsmiConnect_Click;
                item.Enabled = true;
                item.Checked = false;
            }
        }

        /// <summary>
        /// Log Message
        /// </summary>
        /// <param name="message">The message.</param>
        private void LogMessage(string message)
        {
            lock (this.syncLockLogs)
            {
                string dataTime = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss");

                if (this.tbConsole.InvokeRequired)
                {
                    this.tbConsole.BeginInvoke((MethodInvoker)delegate ()
                    {
                        this.tbConsole.AppendText(dataTime + " -> " + message + Environment.NewLine);
                    });
                }
                else
                {
                    this.tbConsole.AppendText(dataTime + " -> " + message + Environment.NewLine);
                }
            }
        }

        /// <summary>
        /// Fit image from one size to input size.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <param name="size">New size.</param>
        /// <returns>Resized image.</returns>
        public Bitmap FitImage(Bitmap image, Size size)
        {
            var ratioX = (double)size.Width / image.Width;
            var ratioY = (double)size.Height / image.Height;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            var newImage = new Bitmap(newWidth, newHeight);

            using (var graphics = Graphics.FromImage(newImage))
            {
                graphics.DrawImage(image, 0, 0, newWidth, newHeight);
            }

            return newImage;
        }

        /// <summary>
        /// Transform radians to degree.
        /// </summary>
        /// <param name="radians">Value</param>
        /// <returns>Transformed value.</returns>
        private float ToDegree(float radians)
        {
            return radians * 180.0f / (float)Math.PI;
        }

        #endregion

        #region Main Form

        private void MainForm_Load(object sender, EventArgs e)
        {
            this.SearchForPorts();
            this.SearchForCameras();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.DisconnectFromRobot();
            this.DisconnectFromCamera();
            this.StopSendImageTimer();
            this.DisconnectVisionSystemFromMqtt();
        }

        #endregion

        #region Tool Strip Menu Items

        #region Robot

        private void tsmiConnect_Click(object sender, EventArgs e)
        {
            this.DisconnectFromRobot();
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            this.ConnectToRobot(item.Text);
        }

        private void tsmiBeep_Click(object sender, EventArgs e)
        {
            // Create the Melodie thread.
            Thread worker = new Thread(
                new ThreadStart(
                    delegate ()
                    {
                        // Check the robot.
                        if (this.robot == null || !this.robot.IsConnected) return;

                        // Song number.
                        byte songNumber = 0;

                        // Song data.
                        List<byte> notes = new List<byte>();
                        for (byte i = 0; i <= 5; i++)
                        {
                            notes.Add(80);
                            notes.Add(32);
                        }

                        // Set song.
                        this.robot.Song(songNumber, notes.ToArray());

                        // Play song.
                        this.robot.Play(songNumber);
                    }
                )
            );

            // Start the Melodie thread.
            worker.Start();
        }

        #region LED

        private void tsmiLedCheckRobot_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiLedCheckRobot.Checked = !this.tsmiLedCheckRobot.Checked;

            this.robot.LEDs(this.tsmiLedCheckRobot.Checked, this.tsmiLedDock.Checked, this.tsmiLedSpot.Checked, this.tsmiLedDirtDetect.Checked, this.cleanLedColor, this.cleanLedIntensity);
        }


        private void tsmiLedSpot_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiLedSpot.Checked = !this.tsmiLedSpot.Checked;

            this.robot.LEDs(this.tsmiLedCheckRobot.Checked, this.tsmiLedDock.Checked, this.tsmiLedSpot.Checked, this.tsmiLedDirtDetect.Checked, this.cleanLedColor, this.cleanLedIntensity);
        }

        private void tsmiLedDock_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiLedDock.Checked = !this.tsmiLedDock.Checked;

            this.robot.LEDs(this.tsmiLedCheckRobot.Checked, this.tsmiLedDock.Checked, this.tsmiLedSpot.Checked, this.tsmiLedDirtDetect.Checked, this.cleanLedColor, this.cleanLedIntensity);
        }

        private void tsmiLedDirtDetect_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiLedDirtDetect.Checked = !this.tsmiLedDirtDetect.Checked;

            this.robot.LEDs(this.tsmiLedCheckRobot.Checked, this.tsmiLedDock.Checked, this.tsmiLedSpot.Checked, this.tsmiLedDirtDetect.Checked, this.cleanLedColor, this.cleanLedIntensity);
        }

        private void tsmiLedCleanOff_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiLedCleanOff.Checked = true;

            if (this.tsmiLedCleanOff.Checked)
            {
                this.tsmiLedCleanGreen.Checked = false;
                this.tsmiLedCleanRed.Checked = false;
            }

            this.robot.LEDs(this.tsmiLedCheckRobot.Checked, this.tsmiLedDock.Checked, this.tsmiLedSpot.Checked, this.tsmiLedDirtDetect.Checked, this.cleanLedColor, this.cleanLedIntensity);
        }

        private void tsmiLedCleanGreen_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiLedCleanGreen.Checked = true;

            if (this.tsmiLedCleanGreen.Checked)
            {
                this.tsmiLedCleanOff.Checked = false;
                this.tsmiLedCleanRed.Checked = false;
            }
            
            if (this.tsmiLedCleanGreen.Checked || this.tsmiLedCleanRed.Checked)
            {
                this.cleanLedIntensity = 255;
            }

            this.cleanLedColor = 0;

            this.robot.LEDs(this.tsmiLedCheckRobot.Checked, this.tsmiLedDock.Checked, this.tsmiLedSpot.Checked, this.tsmiLedDirtDetect.Checked, this.cleanLedColor, this.cleanLedIntensity);
        }

        private void tsmiLedCleanRed_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiLedCleanRed.Checked = false;

            if (this.tsmiLedCleanRed.Checked)
            {
                this.tsmiLedCleanOff.Checked = false;
                this.tsmiLedCleanGreen.Checked = false;
            }

            if (this.tsmiLedCleanGreen.Checked || this.tsmiLedCleanRed.Checked)
            {
                this.cleanLedIntensity = 255;
            }

            this.cleanLedColor = 255;

            this.robot.LEDs(this.tsmiLedCheckRobot.Checked, this.tsmiLedDock.Checked, this.tsmiLedSpot.Checked, this.tsmiLedDirtDetect.Checked, this.cleanLedColor, this.cleanLedIntensity);
        }


        #endregion

        #region Motors

        private void tsmiMainBrush_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiMainBrush.Checked = !this.tsmiMainBrush.Checked;

            this.robot.Motors(this.tsmiMainBrush.Checked, this.tsmiVacuum.Checked, this.tsmiSideBrush.Checked);
        }

        private void tsmiVacuum_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiVacuum.Checked = !this.tsmiVacuum.Checked;

            this.robot.Motors(this.tsmiMainBrush.Checked, this.tsmiVacuum.Checked, this.tsmiSideBrush.Checked);
        }

        private void tsmiSideBrush_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;

            this.tsmiSideBrush.Checked = !this.tsmiSideBrush.Checked;

            this.robot.Motors(this.tsmiMainBrush.Checked, this.tsmiVacuum.Checked, this.tsmiSideBrush.Checked);
        }

        #endregion

        #region Buttons

        private void tsmiBtnClean_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Clean();
        }

        private void tsmiBtnSpot_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Spot();
        }

        private void tsmiBtnDock_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.ForceSeekingDock();
        }

        private void tsmiBtnPower_Click(object sender, EventArgs e)
        {
            if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Power();
        }

        private void tsmiBtnMax_Click(object sender, EventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Max();
        }

        #endregion

        #region LED Display

        // TODO: put some methods.

        #endregion

        #endregion

        private void tsmiSettings_Click(object sender, EventArgs e)
        {
            using (Settings.SettingsForm sf = new Settings.SettingsForm())
            {
                sf.ShowDialog();
            }
        }

        private void tsmiExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        #region Camera

        private void tsmiStopCaptureeDevice_Click(object sender, EventArgs e)
        {
            this.DisconnectFromCamera();
            this.StopSendImageTimer();

            foreach (ToolStripMenuItem cameraItem in this.tsmiCameraCapture.DropDown.Items)
            {
                cameraItem.Enabled = true;
                cameraItem.Checked = false;
            }

            string message = "Video Capture: Stoped";
            this.LogMessage(message);
        }

        private void tsmiCaptureeDevice_Click(object sender, EventArgs e)
        {
            // Create instance of caller.
            ToolStripMenuItem item = (ToolStripMenuItem)sender;

            // Get device.
            VideoDevice videoDevice = (VideoDevice)item.Tag;

            foreach (ToolStripMenuItem mItem in this.tsmiCameraCapture.DropDown.Items)
            {
                item.Checked = false;
            }

            item.Checked = true;

            try
            {
               
                // Create camera.
                this.videoDevice = new VideoCaptureDevice(videoDevice.MonikerString);
                this.videoDevice.NewFrame += VideoDevice_NewFrame;

                // Stop if other stream was displaying.
                if (this.videoDevice.IsRunning)
                {
                    this.videoDevice.Stop();
                }

                // Start the new stream.
                this.videoDevice.Start();
            }
            catch (Exception exception)
            {
                this.LogMessage(exception.ToString());
                return;
            }

            string message = "Video Capture: Started";
            this.LogMessage(message);
        }

        #endregion

        #region MQTT

        private void tsmiMqttConnect_Click(object sender, EventArgs e)
        {
            this.ConnectVisionSystemViaMqtt();
            this.StartSendImageTimer();

            string message = "MQTT Connection: " + this.mqttCommunicator.IsConnected.ToString();
            this.LogMessage(message);
            this.tsslMQTTConnection.Text = message;
        }

        private void tsmiMqttDisconnect_Click(object sender, EventArgs e)
        {
            this.StopSendImageTimer();
            this.DisconnectVisionSystemFromMqtt();

            string message = "MQTT Connection: " + this.mqttCommunicator.IsConnected.ToString();
            this.LogMessage(message);
            this.tsslMQTTConnection.Text = message;
        }

        #endregion

        #endregion

        #region Buttons

        private void btnRight_MouseDown(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive((short)this.trbSpeed.Value, (short)-this.trbRadius.Value);
        }

        private void btnLeft_MouseDown(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive((short)this.trbSpeed.Value, (short)this.trbRadius.Value);
        }

        private void btnUp_MouseDown(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive((short)this.trbSpeed.Value, 0);
        }

        private void btnDown_MouseDown(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive((short)-this.trbSpeed.Value, 0);
        }



        private void btnStop_Click(object sender, EventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive(0, 0);
        }

        private void btnUp_MouseUp(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive(0, 0);
        }

        private void btnRight_MouseUp(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive(0, 0);
        }

        private void btnDown_MouseUp(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive(0, 0);
        }

        private void btnLeft_MouseUp(object sender, MouseEventArgs e)
        {
                        if (this.robot == null || !this.robot.IsConnected) return;
            this.robot.Drive(0, 0);
        }

        #endregion

        #region Track bar

        private void trbSpeed_ValueChanged(object sender, EventArgs e)
        {
            this.lblSpeed.Text = String.Format("Speed: {0:F3}[mm/s]", ((float)this.trbSpeed.Value / 5.0));
        }

        private void trbRadius_ValueChanged(object sender, EventArgs e)
        {
            this.lblRadius.Text = String.Format("Radius: {0}", this.trbRadius.Value);
        }



        #endregion

        #region Robot

        /// <summary>
        /// Connect to the robot.
        /// </summary>
        /// <param name="portName"></param>
        private void ConnectToRobot(string portName)
        {
            this.roombaCommunicator = new Communicator(portName);
            this.roombaCommunicator.OnMesage += this.robot_OnMesage;
            this.roombaCommunicator.OnConnect += Robot_OnConnect;
            this.roombaCommunicator.OnDisconnect += Robot_OnDisconnect;

            this.robot = new Roomba(roombaCommunicator);
            this.robot.Connect();
            this.robot.Start();
            this.robot.Start();
            this.robot.Safe();
            this.robot.Start();
            this.robot.Safe();

            this.tsslRobotConnection.Text = "Robot Connection: Connected@" + portName;
        }

        /// <summary>
        /// Disconnect from the robot.
        /// </summary>
        private void DisconnectFromRobot()
        {
            if (this.roombaCommunicator == null || !this.roombaCommunicator.IsConnected) return;

            this.robot.Drive(0, 0);
            this.roombaCommunicator.OnMesage -= this.robot_OnMesage;
            this.roombaCommunicator.OnConnect -= Robot_OnConnect;
            this.roombaCommunicator.OnDisconnect -= Robot_OnDisconnect;
            this.robot.Disconnect();

            this.tsslRobotConnection.Text = "Robot Connection: Disconnected";
        }

        private void Robot_OnDisconnect(object sender, EventArgs e)
        {
            this.LogMessage("Disconnected from robot.");
        }

        private void Robot_OnConnect(object sender, EventArgs e)
        {
            Communicator communicator = (Communicator)sender;
            this.LogMessage("Connected to robot port: " + communicator.PortName);
        }

        private void robot_OnMesage(object sender, MessageString e)
        {
            this.LogMessage("Robot: " + e.Message);
        }

        #endregion

        #region Camera

        private void SearchForCameras()
        {
            // Check to see what video inputs we have available.
            this.videoDevices = this.GetDevices();

            if (videoDevices.Length == 0)
            {
                DialogResult res = MessageBox.Show("A camera device was not detected. Do you want to exit?", "",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res == System.Windows.Forms.DialogResult.Yes)
                {
                    Application.Exit();
                }
            }

            // Add cameras to the menus.
            this.AddCameras(this.videoDevices, this.tsmiCameraCapture, this.tsmiCaptureeDevice_Click);
        }

        private void DisconnectFromCamera()
        {
            if (this.videoDevice == null) return;

            // Stop if other stream was displaying.
            if (this.videoDevice.IsRunning)
            {
                this.videoDevice.Stop();
            }

            this.videoDevice.NewFrame -= VideoDevice_NewFrame;

            this.videoDevice = null;
        }

        /// <summary>
        /// Get list of all available devices on the PC.
        /// </summary>
        /// <returns></returns>
        private VideoDevice[] GetDevices()
        {
            //Set up the capture method 
            //-> Find systems cameras with DirectShow.Net DLL, thanks to Charles Lorette.
            //DsDevice[] systemCamereas = DsDevice.GetDevicesOfCat(AForge.Video.DirectShow.FilterCategory.VideoInputDevice);

            // Enumerate video devices
            FilterInfoCollection systemCamereas = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            VideoDevice[] videoDevices = new VideoDevice[systemCamereas.Count];

            for (int index = 0; index < systemCamereas.Count; index++)
            {
                videoDevices[index] = new VideoDevice(index, systemCamereas[index].Name, systemCamereas[index].MonikerString);
            }

            return videoDevices;
        }

        /// <summary>
        /// Add video devices to the tool strip menu.
        /// </summary>
        /// <param name="videoDevices">List of cameras.</param>
        /// <param name="menu">Menu item.</param>
        /// <param name="callback">Callback</param>
        private void AddCameras(VideoDevice[] videoDevices, ToolStripMenuItem menu, EventHandler callback)
        {
            if (videoDevices.Length == 0)
            {
                return;
            }

            // Clear the list.
            menu.DropDown.Items.Clear();

            //
            ToolStripMenuItem stopItem = new ToolStripMenuItem();
            stopItem.Text = "Stop";
            stopItem.Enabled = true;
            stopItem.Checked = false;
            stopItem.Click += this.tsmiStopCaptureeDevice_Click;
            menu.DropDown.Items.Add(stopItem);

            // Add cameras.
            foreach (VideoDevice device in videoDevices)
            {
                // Store the each retrieved available capture device into the MenuItems.
                ToolStripMenuItem cameraItem = new ToolStripMenuItem();

                cameraItem.Text = String.Format("{0:D2} / {1}", device.Index, device.Name);
                cameraItem.Tag = device;
                cameraItem.Enabled = true;
                cameraItem.Checked = false;
                cameraItem.Click += callback;

                menu.DropDown.Items.Add(cameraItem);
            }

        }

        private void VideoDevice_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            try
            {
                if (this.dumpImage != null) this.dumpImage.Dispose();

                this.dumpImage = (Bitmap)eventArgs.Frame.Clone();// clone the bitmap

                if (this.dumpImage == null) return;

                this.ShowImage((Bitmap)this.dumpImage.Clone());
            }
            catch (Exception exception)
            {
                this.LogMessage(exception.ToString());
            }
        }

        /// <summary>
        /// Process rocks in the image.
        /// </summary>
        private void ShowImage(Bitmap inputImage)
        {
            if (this.pbSand.InvokeRequired)
            {
                this.pbSand.BeginInvoke((MethodInvoker)delegate ()
                {
                    // Show the new image.
                    this.pbSand.Image = this.FitImage(inputImage, this.pbSand.Size);
                });
            }
            else
            {
                // Show the new image.
                this.pbSand.Image = this.FitImage(inputImage, this.pbSand.Size);
            }
        }

        #endregion

        #region Send Image Timer

        private void StartSendImageTimer()
        {
            if (this.sendImageTimer == null)
            {
                this.sendImageTimer = new System.Windows.Forms.Timer();
                this.sendImageTimer.Stop();
                this.sendImageTimer.Tick += SendImageTimer_Tick;
                this.sendImageTimer.Interval = 1000;
            }

            if(!this.sendImageTimer.Enabled)
            {
                this.sendImageTimer.Start();
            }
        }

        private void StopSendImageTimer()
        {
            if (this.sendImageTimer == null) return;

            if (this.sendImageTimer.Enabled)
            {
                this.sendImageTimer.Stop();
            }

            this.sendImageTimer.Stop();
        }

        private void SendImageTimer_Tick(object sender, EventArgs e)
        {
            if (this.dumpImage == null) return;
            this.SendImageData(this.dumpImage);
        }

        #endregion

        #region Data Connector

        /// <summary>
        /// Connect vision system.
        /// </summary>
        private void ConnectVisionSystemViaMqtt()
        {
            try
            {
                this.mqttCommunicator = new DataConnector(new MqttAdapter(
                    Properties.Settings.Default.BrokerHost,
                    Properties.Settings.Default.BrokerPort,
                    Properties.Settings.Default.MqttInputTopic,
                    Properties.Settings.Default.MqttOutputTopic,
                    Properties.Settings.Default.MqttImageTopic));

                //this.robot.OnMessage += myRobot_OnMessage;
                //this.robot.OnSensors += myRobot_OnSensors;
                //this.robot.OnDistanceSensors += myRobot_OnDistanceSensors;
                //this.robot.OnGreatingsMessage += myRobot_OnGreatingsMessage;
                //this.robot.OnStoped += myRobot_OnStoped;
                //this.robot.OnPosition += myRobot_OnPosition;
                this.mqttCommunicator.OnMessage += Connector_OnMessage;
                this.mqttCommunicator.Connect();
                //this.robot.Reset();
            }
            catch (Exception exception)
            {
                this.LogMessage(exception.ToString());
            }
        }

        /// <summary>
        /// Disconnect vision system.
        /// </summary>
        private void DisconnectVisionSystemFromMqtt()
        {
            try
            {
                if (this.mqttCommunicator != null && this.mqttCommunicator.IsConnected)
                {
                    this.mqttCommunicator.OnMessage -= Connector_OnMessage;
                    this.mqttCommunicator.Disconnect();
                }
            }
            catch (Exception exception)
            {
                this.LogMessage(exception.ToString());
            }
        }

        private void SendImageData(Bitmap image)
        {
            if (this.mqttCommunicator == null || !this.mqttCommunicator.IsConnected) return;
            if (image == null) return;
            try
            {
                this.mqttCommunicator.SendImage(this.FitImage(image, Properties.Settings.Default.ImageSize));
            }
            catch
            { }
        }

        private void Connector_OnMessage(object sender, Events.StringEventArgs e)
        {
            this.LogMessage(e.Message);
        }

        #endregion

    }
}
