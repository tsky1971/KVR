﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;
using Microsoft.Win32;
//using Microsoft.Kinect;
using System.Diagnostics;
using System.Security.Permissions;
using KinectBase;

namespace KinectWithVRServer
{
    public partial class MainWindow : Window
    {
        internal string startupFile = "";
        internal bool verbose = false;
        internal bool startOnLaunch = false;
        internal ServerCore server;
        internal DateTime serverStartTime = DateTime.MaxValue;
        internal string ColorStreamUniqueID = "";
        internal string DepthStreamUniqueID = "";
        System.Timers.Timer uptimeUpdateTimer;
        internal ObservableCollection<AvailableKinectData> availableKinects = new ObservableCollection<AvailableKinectData>();
        internal ObservableCollection<ConfiguredServerData> configuredServers = new ObservableCollection<ConfiguredServerData>();
        private List<string> kinectsPageList = new List<string>(new string[] {"Available Kinects"});
        internal List<IKinectSettingsControl> kinectOptionGUIPages = new List<IKinectSettingsControl>();
        private string voiceRecogSourceUniqueID = "";
        private WriteableBitmap depthSource;
        private WriteableBitmap colorSource;
        private List<double> depthTimeIntervals = new List<double>();
        private List<double> colorTimeIntervals = new List<double>();
        private Int64 lastDepthTime = 0;
        private Int64 lastColorTime = 0;
        private volatile bool drawingColorSkeleton = false;
        private volatile bool drawingDepthSkeleton = false;
        private int lastSettingsTabIndex = 0;

        public MainWindow(bool isVerbose, bool isAutoStart, string startSettings = "")
        {
            verbose = isVerbose;
            startOnLaunch = isAutoStart;
            startupFile = startSettings;

            InitializeComponent();

            //Set the initial state of the verbose option checkbox
            verboseOutputCheckbox.IsChecked = isVerbose;
        }

        #region Menu Click Event Handlers
        private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDlg = new OpenFileDialog();
            openDlg.Filter = "XML File (*.xml)|*.xml|All Files|*.*";
            openDlg.FilterIndex = 0;
            openDlg.Multiselect = false;

            if ((bool)openDlg.ShowDialog())
            {
                try
                {
                    server.serverMasterOptions = HelperMethods.LoadSettings(openDlg.FileName);
                    UpdateGUISettings();
                }
                catch
                {
                    MessageBox.Show("Error: The settings file failed to open!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    HelperMethods.WriteToLog("Settings file (" + openDlg.FileName + ") failed to load.", this);
                }
            }
        }
        private void SaveSettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveDlg = new SaveFileDialog();
            saveDlg.Filter = "XML File (*.xml)|*.xml";

            if ((bool)saveDlg.ShowDialog())
            {
                try
                {
                    HelperMethods.SaveSettings(saveDlg.FileName, server.serverMasterOptions);
                }
                catch (Exception exc)
                {
                    Debug.WriteLine(exc.Message);
                    MessageBox.Show("Error: The settings file failed to save!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    HelperMethods.WriteToLog("Settings file (" + saveDlg.FileName + ") failed to save.", this);
                }
            }
        }
        private void SaveLogMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveDlg = new SaveFileDialog();
            saveDlg.Filter = "Text File (*.txt)|*.txt";

            if ((bool)saveDlg.ShowDialog())
            {
                using (FileStream file = new FileStream(saveDlg.FileName, FileMode.Create))
                {
                    StreamWriter writer = new StreamWriter(file);

                    try
                    {
                        writer.Write(LogTextBox.Text.ToCharArray());
                    }
                    catch
                    {
                        MessageBox.Show("Error: The log file failed to save!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        HelperMethods.WriteToLog("Log file (" + saveDlg.FileName + ") failed to save.");
                    }

                    writer.Close();
                    writer.Dispose();
                }
            }
        }
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Kinect with VR (KVR) Server\r\nCreated at the Virtual Reality Applications Center\r\nIowa State University\r\nBy Patrick Carlson, Diana Jarrell, and Tim Morgan.\r\nCopyright 2015", "About KVR", MessageBoxButton.OK);
        }
        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            //TODO: Add help
            MessageBox.Show("Warning: Help feature has not been implemented yet.  Sorry.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        #endregion

        #region Window Events
        private void Window_Initialized(object sender, EventArgs e)
        {
            //Setup the timer to update the GUI with the server runtime
            uptimeUpdateTimer = new System.Timers.Timer();
            uptimeUpdateTimer.Interval = 500;
            uptimeUpdateTimer.Elapsed += new System.Timers.ElapsedEventHandler(uptimeUpdateTimer_Elapsed);

            KinectBase.MasterSettings tempSettings = new KinectBase.MasterSettings();

            //Create the server core (this does NOT start the server)
            server = new ServerCore(verbose, tempSettings, this);

            //Set all the data for the data grids
            VoiceButtonDataGrid.ItemsSource = server.serverMasterOptions.voiceButtonCommands;
            VoiceTextDataGrid.ItemsSource = server.serverMasterOptions.voiceTextCommands;

            if (startupFile != null && startupFile != "")
            {
                try
                {
                    server.serverMasterOptions = HelperMethods.LoadSettings(startupFile);
                    UpdateGUISettings();
                }
                catch
                {
                    MessageBox.Show("Error: The startup settings file failed to load!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    HelperMethods.WriteToLog("Startup settings (" + startupFile + ") failed to load.");
                }
            }

            //TODO: Handle starting Kinects based on the loaded settings file
            //Initialize the data for the available Kinect v1s
            KinectV1Core.KinectV1StatusEventArgs[] currentStatuses = KinectV1Core.KinectV1StatusHelper.GetAllKinectsStatus();
            for (int i = 0; i < currentStatuses.Length; i++)
            {
                AvailableKinectData tempData = new AvailableKinectData();

                tempData.UniqueID = currentStatuses[i].UniqueKinectID;
                tempData.Status = currentStatuses[i].Status;
                tempData.KinectType = GetKinectTypeString(currentStatuses[i].Status, currentStatuses[i].isXBox360Kinect);
                
                if (i == 0 && tempData.Status == KinectStatus.Connected)
                {
                    tempData.UseKinect = true;
                    tempData.KinectID = 0;
                    server.serverMasterOptions.kinectOptionsList.Add((IKinectSettings)(new KinectV1Wrapper.Settings(tempData.UniqueID, (int)tempData.KinectID)));
                    server.kinects.Add((new KinectV1Wrapper.Core(ref server.serverMasterOptions, true, (int)tempData.KinectID)));
                    tempData.ServerStatus = "Running";
                }
                else
                {
                    tempData.UseKinect = false;
                    tempData.KinectID = null;
                }
                tempData.PropertyChanged += useKinect_PropertyChanged;
                availableKinects.Add(tempData);
            }

#if Kinect2
            //Initialize the data for the available Kinect v2s
            KinectV2Core.KinectV2StatusEventArgs[] currentStatuses2 = KinectV2Core.KinectV2StatusHelper.GetAllKinectsStatus();
            for (int i = 0; i < currentStatuses2.Length; i++)
            {
                AvailableKinectData tempData = new AvailableKinectData();

                tempData.UniqueID = currentStatuses2[i].UniqueKinectID;
                tempData.Status = currentStatuses2[i].Status;
                //Note: Unlike the Kinect v1, we don't automatically launch a Kinect v2
                //TODO: Do we want to automatically launch a Kinect v2 here?
                tempData.UseKinect = false;
                tempData.KinectID = null;
                tempData.PropertyChanged += useKinect_PropertyChanged;
                availableKinects.Add(tempData);
            }
#endif

            KinectStatusBlock.Text = availableKinects.Count.ToString();
            kinectsAvailableDataGrid.ItemsSource = availableKinects;
            UpdatePageListing();
            GenerateImageSourcePickerLists();
            //Subscribe to the v1 status changed event
            KinectV1Core.KinectV1StatusHelper v1StatusHelper = new KinectV1Core.KinectV1StatusHelper();
            v1StatusHelper.KinectV1StatusChanged += v1StatusHelper_KinectV1StatusChanged;
#if Kinect2
            //Subscribe to the v2 status changed event
            KinectV2Core.KinectV2StatusHelper v2StatusHelper = new KinectV2Core.KinectV2StatusHelper();
            v2StatusHelper.KinectV2StatusChanged += v2StatusHelper_KinectV2StatusChanged;
#endif

            //Populate the skeleton data and set the binding for the data grid
            GenerateSkeletonDataGridData();
            SkeletonSettingsDataGrid.ItemsSource = server.serverMasterOptions.mergedSkeletonOptions.individualSkeletons;

            //Populate and setup the voice recognition lists
            GenerateVoiceRecogEngineList();
            GenerateAudioSourceList();
            VoiceKinectComboBox.SelectedIndex = 0;

            //Set defaults where needed
            FeedbackJointTypeComboBox.SelectedIndex = 0;
            SkelSortModeComboBox.SelectedIndex = 5;

            //Set the items source for the servers display grid
            ServersDataGrid.ItemsSource = configuredServers;

            if (startOnLaunch)
            {
                startServerButton_Click(this, new RoutedEventArgs());
            }
        }

#if Kinect2
        void v2StatusHelper_KinectV2StatusChanged(object sender, KinectV2Core.KinectV2StatusEventArgs e)
        {
            bool kinectFound = false;

            for (int i = 0; i < availableKinects.Count; i++)
            {
                if (availableKinects[i].UniqueID == e.UniqueKinectID)
                {
                    if (e.Status != KinectStatus.Disconnected)
                    {
                        availableKinects[i].Status = e.Status;
                        if (e.Status != KinectStatus.Connected)
                        {
                            availableKinects[i].UseKinect = false;
                        }
                    }
                    else
                    {
                        availableKinects[i].PropertyChanged -= useKinect_PropertyChanged;
                        availableKinects.RemoveAt(i);

                        renumberKinectIDs();
                    }
                    kinectsAvailableDataGrid.Items.Refresh();
                    kinectFound = true;
                }
            }

            //Update the number of Kinects attached to the computer
            KinectStatusBlock.Text = availableKinects.Count.ToString();

            if (!kinectFound)
            {
                AvailableKinectData tempData = new AvailableKinectData();
                tempData.UniqueID = e.UniqueKinectID;
                tempData.KinectID = null;
                tempData.UseKinect = false;
                tempData.PropertyChanged += useKinect_PropertyChanged;
                tempData.Status = e.Status;
                availableKinects.Add(tempData);
                kinectsAvailableDataGrid.Items.Refresh();
            }
        }
#endif

        void v1StatusHelper_KinectV1StatusChanged(object sender, KinectV1Core.KinectV1StatusEventArgs e)
        {
            bool kinectFound = false;

            for (int i = 0; i < availableKinects.Count; i++)
            {
                if (availableKinects[i].UniqueID == e.UniqueKinectID)
                {
                    if (e.Status != KinectStatus.Disconnected)
                    {
                        availableKinects[i].Status = e.Status;
                        availableKinects[i].KinectType = GetKinectTypeString(e.Status, e.isXBox360Kinect);

                        if (e.Status != KinectStatus.Connected)
                        {
                            availableKinects[i].UseKinect = false;
                        }
                    }
                    else
                    {
                        availableKinects[i].UseKinect = false;
                        availableKinects[i].PropertyChanged -= useKinect_PropertyChanged;
                        availableKinects.RemoveAt(i);

                        renumberKinectIDs();
                    }
                    kinectsAvailableDataGrid.Items.Refresh();
                    kinectFound = true;
                }
            }

            //Update the number of Kinects attached to the computer
            KinectStatusBlock.Text = availableKinects.Count.ToString();

            if (!kinectFound)
            {
                AvailableKinectData tempData = new AvailableKinectData();
                tempData.KinectID = null;
                tempData.UseKinect = false;
                tempData.KinectType = GetKinectTypeString(e.Status, e.isXBox360Kinect);
                tempData.PropertyChanged += useKinect_PropertyChanged;
                tempData.Status = e.Status;
                tempData.UniqueID = e.UniqueKinectID;
                availableKinects.Add(tempData);
                kinectsAvailableDataGrid.Items.Refresh();
            }
        }
        private string GetKinectTypeString(KinectStatus status, bool isXBox360Kinect)
        {
            string tempString = "Unknown";

            if (status == KinectStatus.Connected)
            {
                if (isXBox360Kinect)
                {
                    tempString = "Kinect v1 (Xbox)";
                }
                else
                {
                    tempString = "Kinect v1";
                }
            }

            return tempString;
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (server.isRunning)
            {
                server.stopServer();
            }

            for (int i = 0; i < server.kinects.Count; i++)
            {
                server.kinects[i].ShutdownSensor();
            }
        }
        #endregion

        #region Status Tab GUI Stuff
        private void startServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (server != null)
            {
                if (server.isRunning)
                {
                    startServerButton.Content = "...Stopping";
                    ServerStatusItem.Content = "Server Stopping...";
                    ServerStatusTextBlock.Text = "Stopping...";

                    //Force the status tab to redraw before stopping the server
                    StatusTab.InvalidateVisual();
                    ForceGUIUpdate();
                    System.Threading.Thread.Sleep(100);

                    uptimeUpdateTimer.Stop();
                    server.stopServer();

                    startServerButton.Content = "Start";
                    ServerStatusItem.Content = "Server Stopped";
                    ServerStatusTextBlock.Text = "Stopped";
                    RunTimeTextBlock.Text = "0";
                }
                else
                {
                    //The server doesn't actually start until the callback from the voice core
                    startServerButton.IsEnabled = false;
                    startServerButton.Content = "...Starting"; //For some screwy reason, it reverses where the periods are on the button, which is why they are first here
                    ServerStatusItem.Content = "Server Starting...";
                    ServerStatusTextBlock.Text = "Starting...";

                    server.launchServer();

                    serverStartTime = DateTime.Now;
                    uptimeUpdateTimer.Start();
                }
            }
        }
        void uptimeUpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //This is on another thread...
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                this.RunTimeTextBlock.Text = ((TimeSpan)(DateTime.Now - serverStartTime)).ToString(@"dd\:hh\:mm\:ss");
            }), null
            );
        }
        private static double CalculateFrameRate(Int64 currentTimeStamp, ref Int64 lastTimeStamp, ref List<double> oldIntervals)
        {
            double newInterval = (double)(currentTimeStamp - lastTimeStamp);
            lastTimeStamp = currentTimeStamp;

            if (oldIntervals.Count >= 10) //Computes a running average of 10 frames for stability
            {
                oldIntervals.RemoveAt(0);
            }
            oldIntervals.Add(newInterval);

            return (1.0 / oldIntervals.Average() * 1000.0);
        }
        #endregion

        #region Kinect Tab GUI Stuff
        //Renumbers the Kinect IDs
        private void renumberKinectIDs()
        {
            int sensorNum = 0;
            for (int j = 0; j < availableKinects.Count; j++)
            {
                if (availableKinects[j].UseKinect)
                {
                    availableKinects[j].KinectID = sensorNum;
                    sensorNum++;
                }
                else
                {
                    availableKinects[j].KinectID = null;
                }
            }
        }
        //Updates the items in the Kinect settings tab listbox, this list is used to pick what is shown in the area next to the list box
        void UpdatePageListing()
        {
            kinectsPageList.RemoveRange(1, kinectsPageList.Count - 1); //Clear all but the first page, which we will always show

            for (int i = 0; i < availableKinects.Count; i++)
            {
                if (availableKinects[i].UseKinect)
                {
                    //Check if the GUI page exists, and create it if it doesn't exist
                    bool exists = false;
                    for (int j = 0; j < kinectOptionGUIPages.Count; j++)
                    {
                        if (((KinectV1Wrapper.SettingsControl)kinectOptionGUIPages[j]).uniqueKinectID == availableKinects[i].UniqueID)
                        {
                            exists = true;
                            ((KinectV1Wrapper.SettingsControl)kinectOptionGUIPages[j]).kinectID = availableKinects[i].KinectID.Value;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        //IKinectSettingsControl tempControl = new KinectV1Wrapper.SettingsControl(availableKinects[i].KinectID.Value, ref server.serverMasterOptions, server.kinects[availableKinects[i].KinectID.Value]);
                        IKinectSettingsControl tempControl = new KinectV1Wrapper.SettingsControl(availableKinects[i].KinectID.Value, ref server.serverMasterOptions, server.kinects[availableKinects[i].KinectID.Value]);
                        kinectOptionGUIPages.Add(tempControl);
                        KinectTabMasterGrid.Children.Add((UserControl)tempControl);
                    }
                    kinectsPageList.Add("Kinect " + availableKinects[i].KinectID.ToString());
                }
                else
                {
                    //Check if the GUI exist for the one we are removing, and set the Kinect ID to null so it will go to the end
                    for (int j = 0; j < kinectOptionGUIPages.Count; j++)
                    {
                        //TODO: Figure out a way to perserve the settings and the acceleration updating
                        if (((KinectV1Wrapper.SettingsControl)kinectOptionGUIPages[j]).uniqueKinectID == availableKinects[i].UniqueID)
                        {
                            //((KinectV1Wrapper.SettingsControl)kinectOptionGUIPages[j]).kinectID = null;  //This will cause the page to be hidden, but not destroyed (which saves the settings on the GUI, but breaks acceleration updating)
                            kinectOptionGUIPages.RemoveAt(j);  //This will destroy the page and cause it to be recreated when the Kinect is set to be used again (which saves the acceleration updating, but losses all the settings)
                            break;
                        }
                    }
                }
            }

            kinectOptionGUIPages.Sort(new KinectSettingsControlComparer());
            kinectTabListBox.ItemsSource = kinectsPageList;
            kinectTabListBox.Items.Refresh();
        }
        //Changes which Kinect settings page is in view based on the selection in the list box
        private void kinectTabListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (kinectTabListBox.SelectedIndex >= 0 && kinectTabListBox.SelectedIndex <= kinectOptionGUIPages.Count)
            {
                if (kinectTabListBox.SelectedIndex == 0)
                {
                    for (int i = 0; i < kinectOptionGUIPages.Count; i++)
                    {
                        ((UserControl)kinectOptionGUIPages[i]).Visibility = System.Windows.Visibility.Collapsed;
                    }
                    kinectsAvailableDataGrid.Visibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    kinectsAvailableDataGrid.Visibility = System.Windows.Visibility.Collapsed;
                    for (int i = 0; i < kinectOptionGUIPages.Count; i++)
                    {
                        if (kinectTabListBox.SelectedIndex - 1 == i)
                        {
                            ((UserControl)kinectOptionGUIPages[i]).Visibility = System.Windows.Visibility.Visible;
                        }
                        else
                        {
                            ((UserControl)kinectOptionGUIPages[i]).Visibility = System.Windows.Visibility.Collapsed;
                        }
                    }
                }
            }
        }
        //Updates the Kinect list when the property is changed
        void useKinect_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "UseKinect")
            {
                //TODO: Handle if a Kinect is unplugged from the system (which doesn't call this method but can break things badly)
                //Note, there is some logic to the order these are called in, do not rearrange without understanding that logic!
                renumberKinectIDs();
                reorderKinectSettings();
                launchAndKillKinects();
                UpdatePageListing();
                GenerateImageSourcePickerLists();
                GenerateAudioSourceList();
                GenerateSkeletonDataGridData();
                //FOR DEBUGGING ONLY!!!!
                //WriteOutKinectOrders();
            }
        }
        //Reorder the Kinect Settings
        private void reorderKinectSettings()
        {
            for (int i = 0; i < availableKinects.Count; i++)
            {
                if (availableKinects[i].UseKinect)
                {
                    bool found = false;
                    for (int j = 0; j < server.serverMasterOptions.kinectOptionsList.Count; j++)
                    {
                        if (availableKinects[i].UniqueID == server.serverMasterOptions.kinectOptionsList[j].uniqueKinectID)
                        {
                            server.serverMasterOptions.kinectOptionsList[j].kinectID = (int)availableKinects[i].KinectID;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        server.serverMasterOptions.kinectOptionsList.Add((IKinectSettings)(new KinectV1Wrapper.Settings(availableKinects[i].UniqueID, (int)availableKinects[i].KinectID)));
                    }
                }
                else
                {
                    for (int j = 0; j < server.serverMasterOptions.kinectOptionsList.Count; j++)
                    {
                        if (availableKinects[i].UniqueID == server.serverMasterOptions.kinectOptionsList[j].uniqueKinectID)
                        {
                            server.serverMasterOptions.kinectOptionsList.RemoveAt(j);
                        }
                    }
                }
            }

            server.serverMasterOptions.kinectOptionsList.Sort(new KinectSettingsComparer());
        }
        //Updates which Kinects are running based on the selections in the available Kinects data grid
        private void launchAndKillKinects()
        {
            for (int i = 0; i < availableKinects.Count; i++)
            {
                if (availableKinects[i].UseKinect)
                {
                    //If the Kinect is to be used, check and see if it exists, and launch it if it doesn't
                    bool kinectFound = false;
                    for (int j = 0; j < server.kinects.Count; j++)
                    {
                        if (server.kinects[j].uniqueKinectID == availableKinects[i].UniqueID)
                        {
                            server.kinects[j].kinectID = (int)availableKinects[i].KinectID;
                            kinectFound = true;
                            break;
                        }
                    }
                    if (!kinectFound)
                    {
                        availableKinects[i].ServerStatus = "Starting";
                        kinectsAvailableDataGrid.Items.Refresh();
                        kinectsAvailableDataGrid.InvalidateVisual();
                        System.Threading.Thread.Sleep(10); //Yes, it is a dirty hack, but it is the only way I can find to get the GUI to update reliably
                        ForceGUIUpdate();
                        server.kinects.Add(new KinectV1Wrapper.Core(ref server.serverMasterOptions, true, availableKinects[i].KinectID));
                        availableKinects[i].ServerStatus = "Running";
                    }
                }
                else
                {
                    //If the Kinect is not to be used, check and see if it exists, and destroy it if it does
                    for (int j = 0; j < server.kinects.Count; j++)
                    {
                        if (server.kinects[j].uniqueKinectID == availableKinects[i].UniqueID)
                        {
                            availableKinects[i].ServerStatus = "Stopping";
                            kinectsAvailableDataGrid.Items.Refresh();
                            kinectsAvailableDataGrid.UpdateLayout();
                            System.Threading.Thread.Sleep(10);
                            ForceGUIUpdate();
                            server.kinects[j].ShutdownSensor();
                            server.kinects.RemoveAt(j);
                            availableKinects[i].ServerStatus = "Stopped";
                            break;
                        }
                    }
                }
            }
            server.kinects.Sort(new KinectCoreComparer());
        }
        //Forces the GUI to update before launching the Kinect so the user knows what is going on
        private void ForceGUIUpdate()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, new DispatcherOperationCallback(delegate(object parameter)
            {
                frame.Continue = false;
                return null;
            }), null);

            Dispatcher.PushFrame(frame);
        }
        //TODO: REMOVE (FOR DEBUGGING ONLY)
        private void WriteOutKinectOrders()
        {
            Debug.WriteLine("Available Kinects:");
            for (int i = 0; i < availableKinects.Count; i++)
            {
                Debug.WriteLine(availableKinects[i].KinectID.ToString() + ":   " + availableKinects[i].UniqueID);
            }

            Debug.WriteLine("Running Kinects:");
            for (int i = 0; i < server.kinects.Count; i++)
            {
                Debug.WriteLine(server.kinects[i].kinectID.ToString() + ":   " + server.kinects[i].uniqueKinectID);
            }

            Debug.WriteLine("Kinect Setting:");
            for (int i = 0; i < server.serverMasterOptions.kinectOptionsList.Count; i++)
            {
                Debug.WriteLine(server.serverMasterOptions.kinectOptionsList[i].kinectID.ToString() + ":   " + server.serverMasterOptions.kinectOptionsList[i].uniqueKinectID);
            }

            Debug.WriteLine("GUI Pages:");
            for (int i = 0; i < kinectOptionGUIPages.Count; i++)
            {
                Debug.WriteLine(((KinectV1Wrapper.SettingsControl)kinectOptionGUIPages[i]).kinectID.ToString() + ":   " + ((KinectV1Wrapper.SettingsControl)kinectOptionGUIPages[i]).uniqueKinectID);
            }
        }
        //Handles the linking of the connection status hyperlinks to the help messages
        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            KinectStatus status = availableKinects[kinectsAvailableDataGrid.SelectedIndex].Status;
            switch (status)
            {
                case KinectStatus.Connected:
                {
                    //No help is needed if the Kinect is connected properly
                    break;
                }
                case KinectStatus.DeviceNotGenuine:
                {
                    MessageBox.Show("The connected device is not genuine.  Please attach a genuine Kinect.", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.DeviceNotSupported:
                {
                    MessageBox.Show("A Kinect for Xbox is connected.  Please attach a Kinect for Windows sensor or install the Kinect SDK instead of the Kinect runtime.", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.Disconnected:
                {
                    MessageBox.Show("The Kinect device is disconnected.  Please reconnect the Kinect sensor.", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.Error:
                {
                    MessageBox.Show("An unknown error has occured with the Kinect.  Try restarting the computer?", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.Initializing:
                {
                    MessageBox.Show("The Kinect is initializing.  Please wait, the Kinect should be operational momentarily.", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.InsufficientBandwidth:
                {
                    MessageBox.Show("There is not enough bandwidth available on this USB port for the Kinect.  Please move the Kinect to a different USB root node.", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.NotPowered:
                {
                    MessageBox.Show("The Kinect is not connected to AC power.  Please plug in the Kinect's AC adapter.", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.NotReady:
                {
                    MessageBox.Show("The Kinect is not ready to run.  Please wait, the Kinect should be operational momentarily.  If not, try restarting the computer?", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                case KinectStatus.Undefined:
                {
                    MessageBox.Show("An unknown error has occured with the Kinect.  Try restarting the computer?", "Connection Help", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
            }
        }
        #endregion

        #region Skeleton Tab GUI Stuff
        private void SkeletonTab_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            //Remove any kinects that aren't in use anymore (doesn't check the first tab since that is always the merged skeletons
            for (int i = SkeletonsTabControl.Items.Count - 1; i > 0; i--)
            {
                bool kinectFound = false;

                for (int j = 0; j < server.kinects.Count; j++)
                {
                    if (((TabItem)SkeletonsTabControl.Items[i]).Header.ToString() == "Kinect " + server.kinects[j].kinectID.ToString())
                    {
                        if (((KinectV1Wrapper.Settings)server.serverMasterOptions.kinectOptionsList[j]).sendRawSkeletons)
                        {
                            kinectFound = true;
                            break;
                        }
                    }
                }

                if (!kinectFound)
                {
                    SkeletonsTabControl.Items.RemoveAt(i);
                }
            }

            //Add the controls for Kinects that aren't on the list but need to be
            for (int i = 0; i < server.kinects.Count; i++)
            {
                if (server.kinects[i].version == KinectVersion.KinectV1)
                {
                    if (((KinectV1Wrapper.Settings)server.serverMasterOptions.kinectOptionsList[i]).sendRawSkeletons)
                    {
                        bool controlFound = false;

                        for (int j = 0; j < SkeletonsTabControl.Items.Count; j++)
                        {
                            if (((TabItem)SkeletonsTabControl.Items[j]).Header.ToString() == "Kinect " + server.kinects[i].kinectID.ToString())
                            {
                                controlFound = true;
                            }
                        }

                        if (!controlFound)
                        {
                            TabItem newTabItem = new TabItem();
                            newTabItem.Header = "Kinect " + server.kinects[i].kinectID.ToString();
                            newTabItem.Content = ((KinectV1Wrapper.SettingsControl)kinectOptionGUIPages[i]).skeletonUserControl;
                            SkeletonsTabControl.Items.Add(newTabItem);
                        }
                    }
                }
                else if (server.kinects[i].version == KinectVersion.KinectV2)
                {
                    //TODO: Add the code for the Kinect v2 skeleton user control here
                }
            }

            //TODO: Add sorting method for the skeleton controls so it always lists 0 to X
        }
        #endregion

        #region Voice Recognition GUI Stuff
        private void VoiceButtonDataGrid_LostFocus(object sender, RoutedEventArgs e)
        {
            VoiceButtonDataGrid.SelectedIndex = -1;
        }
        private void VoiceTextDataGrid_LostFocus(object sender, RoutedEventArgs e)
        {
            VoiceTextDataGrid.SelectedIndex = -1;
        }
        private void VoiceKinectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceKinectComboBox.SelectedIndex == VoiceKinectComboBox.Items.Count - 1)
            {
                server.serverMasterOptions.audioOptions.sourceID = -1;
                voiceRecogSourceUniqueID = "";
            }
            else
            {
                server.serverMasterOptions.audioOptions.sourceID = VoiceKinectComboBox.SelectedIndex;
                voiceRecogSourceUniqueID = server.serverMasterOptions.kinectOptionsList[VoiceKinectComboBox.SelectedIndex].uniqueKinectID;
            }
        }
        private void VoiceRecognitionEngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ReadOnlyCollection<Microsoft.Speech.Recognition.RecognizerInfo> allRecognizers = Microsoft.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers();
            server.serverMasterOptions.audioOptions.recognizerEngineID = allRecognizers[VoiceRecognitionEngineComboBox.SelectedIndex].Id;
        }
        #endregion

        #region Preview Image Methods
        private void ColorSourcePickerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorSourcePickerComboBox.SelectedItem != null)
            {
                //Remove the events from the previous selection
                if (server != null)
                {
                    for (int i = 0; i < server.kinects.Count; i++)
                    {
                        if (server.kinects[i].uniqueKinectID == ColorStreamUniqueID)
                        {
                            server.kinects[i].ColorFrameReceived -= MainWindow_ColorFrameReceived;
                            server.kinects[i].SkeletonChanged -= MainWindow_SkeletonChangedColor;
                            ColorStreamUniqueID = "";
                        }
                    }
                }

                //Add the new frame event
                if (ColorSourcePickerComboBox.SelectedItem.ToString().ToLower() == "none")
                {
                    ColorStreamUniqueID = "";
                    ColorImage.Visibility = System.Windows.Visibility.Hidden;
                }
                else
                {
                    string temp = ColorSourcePickerComboBox.SelectedItem.ToString().ToLower().Replace("kinect ", "");
                    int kinectIndex = -1;
                    if (int.TryParse(temp, out kinectIndex))
                    {
                        ColorStreamUniqueID = server.kinects[kinectIndex].uniqueKinectID;
                        ColorImage.Visibility = System.Windows.Visibility.Visible;
                        server.kinects[kinectIndex].ColorFrameReceived += MainWindow_ColorFrameReceived;
                        server.kinects[kinectIndex].SkeletonChanged += MainWindow_SkeletonChangedColor;
                    }
                }
            }
        }
        private void DepthSourcePickerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DepthSourcePickerComboBox.SelectedItem != null)
            {
                //Remove the event from the previous selection
                if (server != null)
                {
                    for (int i = 0; i < server.kinects.Count; i++)
                    {
                        if (server.kinects[i].uniqueKinectID == DepthStreamUniqueID)
                        {
                            server.kinects[i].DepthFrameReceived -= MainWindow_DepthFrameReceived;
                            server.kinects[i].SkeletonChanged -= MainWindow_SkeletonChangedDepth;
                            DepthStreamUniqueID = "";
                        }
                    }
                }

                //Add the new frame event
                if (DepthSourcePickerComboBox.SelectedItem.ToString().ToLower() == "none")
                {
                    DepthStreamUniqueID = "";
                    DepthImage.Visibility = System.Windows.Visibility.Hidden;
                }
                else
                {
                    string temp = DepthSourcePickerComboBox.SelectedItem.ToString().ToLower().Replace("kinect ", "");
                    int kinectIndex = -1;
                    if (int.TryParse(temp, out kinectIndex))
                    {
                        DepthStreamUniqueID = server.kinects[kinectIndex].uniqueKinectID;
                        DepthImage.Visibility = System.Windows.Visibility.Visible;
                        server.kinects[kinectIndex].DepthFrameReceived += MainWindow_DepthFrameReceived;
                        server.kinects[kinectIndex].SkeletonChanged += MainWindow_SkeletonChangedDepth;
                    }
                }
            }
        }
        void MainWindow_ColorFrameReceived(object sender, ColorFrameEventArgs e)
        {
            if (colorSource == null)
            {
                colorSource = new WriteableBitmap(e.width, e.height, 96.0, 96.0, e.pixelFormat, null);
                ColorImage.Source = colorSource;
            }
            else if (colorSource.PixelWidth != e.width || colorSource.PixelHeight != e.height || colorSource.Format != e.pixelFormat)
            {
                colorSource = null;
                colorSource = new WriteableBitmap(e.width, e.height, 96.0, 96.0, e.pixelFormat, null);
                ColorImage.Source = colorSource;
            }

            colorSource.WritePixels(new Int32Rect(0, 0, e.width, e.height), e.image, e.width * e.bytesPerPixel, 0);

            //Calculate and display the frame rate
            double tempFPS = CalculateFrameRate(e.timeStamp, ref lastColorTime, ref colorTimeIntervals);
            ColorFPSTextBlock.Text = tempFPS.ToString("F1");
        }
        void MainWindow_DepthFrameReceived(object sender, DepthFrameEventArgs e)
        {
            if (depthSource == null)
            {
                depthSource = new WriteableBitmap(e.width, e.height, 96.0, 96.0, e.pixelFormat, null);
                DepthImage.Source = depthSource;
            }
            else if (depthSource.PixelWidth != e.width || depthSource.PixelHeight != e.height || depthSource.Format != e.pixelFormat)
            {
                depthSource = null;
                depthSource = new WriteableBitmap(e.width, e.height, 96.0, 96.0, e.pixelFormat, null);
                DepthImage.Source = depthSource;
            }

            depthSource.WritePixels(new Int32Rect(0, 0, e.width, e.height), e.image, e.width * e.bytesPerPixel, 0);

            //Calculate the depth frame rate and display it
            double tempFPS = CalculateFrameRate(e.timeStamp, ref lastDepthTime, ref depthTimeIntervals);
            DepthFPSTextBlock.Text = tempFPS.ToString("F1");
        }
        void MainWindow_SkeletonChangedColor(object sender, SkeletonEventArgs e)
        {
            if (!drawingColorSkeleton)
            {                
                drawingColorSkeleton = true;

                ColorImageCanvas.Children.Clear();
                for (int i = 0; i < e.skeletons.Length; i++)
                {
                    //TODO: Map the rendering color to the merged skeleton colors?
                    RenderSkeletonOnColor(e.skeletons[i], AutoPickSkeletonRenderColor(i), e.kinectID);
                }
            }
        }
        void MainWindow_SkeletonChangedDepth(object sender, SkeletonEventArgs e)
        {
            if (!drawingDepthSkeleton)
            {
                drawingDepthSkeleton = true;

                DepthImageCanvas.Children.Clear();
                for (int i = 0; i < e.skeletons.Length; i++)
                {
                    //TODO: Map the rendering color to the merged skeleton colors?
                    RenderSkeletonOnDepth(e.skeletons[i], AutoPickSkeletonRenderColor(i), e.kinectID);
                }
            }
        }
        #endregion

        #region Other Methods
        //Refreshes all the data on the GUI after a new settings file is loaded
        private void UpdateGUISettings()
        {
            for (int i = 0; i < server.serverMasterOptions.kinectOptionsList.Count; i++)
            {
                for (int j = 0; j < kinectOptionGUIPages.Count; j++)
                {
                    if (kinectOptionGUIPages[j].version == server.serverMasterOptions.kinectOptionsList[i].version &&
                        kinectOptionGUIPages[j].uniqueKinectID == server.serverMasterOptions.kinectOptionsList[i].uniqueKinectID)
                    {
                        kinectOptionGUIPages[j].kinectID = server.serverMasterOptions.kinectOptionsList[i].kinectID;
                        kinectOptionGUIPages[j].UpdateGUI(server.serverMasterOptions);
                        break;
                    }
                }
            }
            //TODO: Delete all kinect options pages with a null Kinect ID

            VoiceTextDataGrid.ItemsSource = server.serverMasterOptions.voiceTextCommands;
            VoiceButtonDataGrid.ItemsSource = server.serverMasterOptions.voiceButtonCommands;

            //TODO: Add the rest of the GUI updates here.
        }
        private void GenerateImageSourcePickerLists()
        {
            ColorSourcePickerComboBox.Items.Clear();
            DepthSourcePickerComboBox.Items.Clear();
            ColorSourcePickerComboBox.Items.Add("None");
            DepthSourcePickerComboBox.Items.Add("None");

            bool colorFound = false;
            bool depthFound = false;
            for (int i = 0; i < server.kinects.Count; i++)
            {
                if (server.kinects[i].ColorStreamEnabled)
                {
                    ColorSourcePickerComboBox.Items.Add("Kinect " + server.kinects[i].kinectID);
                    if (server.kinects[i].uniqueKinectID == ColorStreamUniqueID)
                    {
                        ColorSourcePickerComboBox.SelectedIndex = i + 1;
                        colorFound = true;
                    }
                }
                if (server.kinects[i].ColorStreamEnabled)
                {
                    DepthSourcePickerComboBox.Items.Add("Kinect " + server.kinects[i].kinectID);
                    if (server.kinects[i].uniqueKinectID == DepthStreamUniqueID)
                    {
                        DepthSourcePickerComboBox.SelectedIndex = i + 1;
                        depthFound = true;
                    }
                }
            }

            if (!colorFound)
            {
                ColorSourcePickerComboBox.SelectedIndex = 0;
            }
            if (!depthFound)
            {
                DepthSourcePickerComboBox.SelectedIndex = 0;
            }
        }
        private void GenerateAudioSourceList()
        {
            bool sourceFound = false;

            VoiceKinectComboBox.Items.Clear();

            for (int i = 0; i < server.kinects.Count; i++)
            {
                VoiceKinectComboBox.Items.Add("Kinect " + server.kinects[i].kinectID);
                if (server.kinects[i].uniqueKinectID == voiceRecogSourceUniqueID)
                {
                    VoiceKinectComboBox.SelectedIndex = i;
                    sourceFound = true;
                }
            }
            VoiceKinectComboBox.Items.Add("System Default");

            if (!sourceFound)
            {
                VoiceKinectComboBox.SelectedIndex = VoiceKinectComboBox.Items.Count - 1;
            }
        }
        private void GenerateVoiceRecogEngineList()
        {
            ReadOnlyCollection<Microsoft.Speech.Recognition.RecognizerInfo> allRecognizers = Microsoft.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers();
            VoiceRecognitionEngineComboBox.Items.Clear();
            for (int i = 0; i < allRecognizers.Count; i++)
            {
                VoiceRecognitionEngineComboBox.Items.Add(allRecognizers[i].Name);
                if (allRecognizers[i].Name.ToLower().Contains("kinect"))
                {
                    VoiceRecognitionEngineComboBox.SelectedIndex = i;
                }
            }
        }
        //Rejects any points that are not numbers or control characters or a period
        private void floatNumberTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!KinectBase.HelperMethods.NumberKeys.Contains(e.Key) && e.Key != Key.OemPeriod && e.Key != Key.Decimal)
            {
                e.Handled = true;
            }
        }
        //Rejects any points that are not numbers or control charactes
        private void intNumberTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!KinectBase.HelperMethods.NumberKeys.Contains(e.Key))
            {
                e.Handled = true;
            }
        }
        #endregion

        #region Skeleton Rendering Methods
        private void DrawBoneOnColor(Joint startJoint, Joint endJoint, Color boneColor, double thickness, Point offset, int kinectID)
        {
            if (startJoint.TrackingState != TrackingState.NotTracked && endJoint.TrackingState != TrackingState.NotTracked)
            {
                //Map the joint from the skeleton to the color image
                Point startPoint = server.kinects[kinectID].MapJointToColor(startJoint, false);
                Point endPoint = server.kinects[kinectID].MapJointToColor(endJoint, false);

                //Calculate the coordinates on the image (the offset of the image is added in the next section)
                Point imagePointStart = new Point(0.0, 0.0);
                imagePointStart.X = ((double)startPoint.X / colorSource.PixelWidth) * ColorImage.ActualWidth;
                imagePointStart.Y = ((double)startPoint.Y / colorSource.PixelHeight) * ColorImage.ActualHeight;
                Point imagePointEnd = new Point(0.0, 0.0);
                imagePointEnd.X = ((double)endPoint.X / colorSource.PixelWidth) * ColorImage.ActualWidth;
                imagePointEnd.Y = ((double)endPoint.Y / colorSource.PixelHeight) * ColorImage.ActualHeight;

                if (startJoint.TrackingState == TrackingState.Inferred || endJoint.TrackingState == TrackingState.Inferred)
                {
                    thickness = thickness / 2.0;  //If either end of the joint is inferred, use a thinner line
                }

                //Generate the line for the bone
                Line line = new Line();
                line.Stroke = new SolidColorBrush(boneColor);
                line.StrokeThickness = thickness;
                line.X1 = imagePointStart.X + offset.X;
                line.X2 = imagePointEnd.X + offset.X;
                line.Y1 = imagePointStart.Y + offset.Y;
                line.Y2 = imagePointEnd.Y + offset.Y;
                ColorImageCanvas.Children.Add(line);
            }
        }
        private void DrawBoneOnDepth(Joint startJoint, Joint endJoint, Color boneColor, double thickness, Point offset, int kinectID)
        {
            if (startJoint.TrackingState != TrackingState.NotTracked && endJoint.TrackingState != TrackingState.NotTracked)
            {
                //Map the joint from the skeleton to the depth image
                Point startPoint = server.kinects[kinectID].MapJointToDepth(startJoint, false);
                Point endPoint = server.kinects[kinectID].MapJointToDepth(endJoint, false);

                //Calculate the coordinates on the image (the offset of the image is added in the next section)
                Point imagePointStart = new Point(0.0, 0.0);
                imagePointStart.X = ((double)startPoint.X / depthSource.PixelWidth) * DepthImage.ActualWidth;
                imagePointStart.Y = ((double)startPoint.Y / depthSource.PixelHeight) * DepthImage.ActualHeight;
                Point imagePointEnd = new Point(0.0, 0.0);
                imagePointEnd.X = ((double)endPoint.X / depthSource.PixelWidth) * DepthImage.ActualWidth;
                imagePointEnd.Y = ((double)endPoint.Y / depthSource.PixelHeight) * DepthImage.ActualHeight;

                if (startJoint.TrackingState == TrackingState.Inferred || endJoint.TrackingState == TrackingState.Inferred)
                {
                    thickness = thickness / 2.0;  //If either end of the joint is inferred, use a thinner line
                }

                //Generate the line for the bone
                Line line = new Line();
                line.Stroke = new SolidColorBrush(boneColor);
                line.StrokeThickness = thickness;
                line.X1 = imagePointStart.X + offset.X;
                line.X2 = imagePointEnd.X + offset.X;
                line.Y1 = imagePointStart.Y + offset.Y;
                line.Y2 = imagePointEnd.Y + offset.Y;
                DepthImageCanvas.Children.Add(line);
            }
        }
        private void DrawJointPointOnColor(Joint joint, Color jointColor, double radius, Point offset, int kinectID)
        {
            if (joint.TrackingState != TrackingState.NotTracked)
            {
                //Map the joint from the skeleton to the color image
                Point point = server.kinects[kinectID].MapJointToColor(joint, false);

                //Calculate the coordinates on the image (the offset is also added in this section)
                Point imagePoint = new Point(0.0, 0.0);
                imagePoint.X = ((double)point.X / colorSource.PixelWidth) * ColorImage.ActualWidth + offset.X;
                imagePoint.Y = ((double)point.Y / colorSource.PixelHeight) * ColorImage.ActualHeight + offset.Y;

                if (joint.TrackingState == TrackingState.Inferred)
                {
                    radius = radius / 2.0; //If the joint is inferred, use a circle half the size
                }

                //Generate the circle for the joint
                Ellipse circle = new Ellipse();
                circle.Fill = new SolidColorBrush(jointColor);
                circle.StrokeThickness = 0.0;
                circle.Margin = new Thickness(imagePoint.X - radius, imagePoint.Y - radius, 0, 0);
                circle.HorizontalAlignment = HorizontalAlignment.Left;
                circle.VerticalAlignment = VerticalAlignment.Top;
                circle.Height = radius * 2;
                circle.Width = radius * 2;
                ColorImageCanvas.Children.Add(circle);
            }
        }
        private void DrawJointPointOnDepth(Joint joint, Color jointColor, double radius, Point offset, int kinectID)
        {
            if (joint.TrackingState != TrackingState.NotTracked)
            {
                //Map the joint from the skeleton to the depth image
                Point point = server.kinects[kinectID].MapJointToDepth(joint, false);

                //Calculate the coordinates on the image (the offset is also added in this section)
                Point imagePoint = new Point(0.0, 0.0);
                imagePoint.X = ((double)point.X / depthSource.PixelWidth) * DepthImage.ActualWidth + offset.X;
                imagePoint.Y = ((double)point.Y / depthSource.PixelHeight) * DepthImage.ActualHeight + offset.Y;

                if (joint.TrackingState == TrackingState.Inferred)
                {
                    radius = radius / 2.0; //If the joint is inferred, use a circle half the size
                }

                //Generate the circle for the joint
                Ellipse circle = new Ellipse();
                circle.Fill = new SolidColorBrush(jointColor);
                circle.StrokeThickness = 0.0;
                circle.Margin = new Thickness(imagePoint.X - radius, imagePoint.Y - radius, 0, 0);
                circle.HorizontalAlignment = HorizontalAlignment.Left;
                circle.VerticalAlignment = VerticalAlignment.Top;
                circle.Height = radius * 2;
                circle.Width = radius * 2;
                DepthImageCanvas.Children.Add(circle);
            }
        }
        private void RenderSkeletonOnColor(KinectSkeleton skeleton, Color renderColor, int kinectID)
        {
            if (colorSource != null)
            {
                //Calculate the offset
                Point offset = new Point(0.0, 0.0);
                if (ColorImageCanvas.ActualWidth != ColorImage.ActualWidth)
                {
                    offset.X = (ColorImageCanvas.ActualWidth - ColorImage.ActualWidth) / 2;
                }

                if (ColorImageCanvas.ActualHeight != ColorImage.ActualHeight)
                {
                    offset.Y = (ColorImageCanvas.ActualHeight - ColorImage.ActualHeight) / 2;
                }

                //Render all the bones (this can't be looped because the enum isn't ordered in order of bone connections)
                DrawBoneOnColor(skeleton.skeleton[JointType.Head], skeleton.skeleton[JointType.ShoulderCenter], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.ShoulderCenter], skeleton.skeleton[JointType.ShoulderLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.ShoulderLeft], skeleton.skeleton[JointType.ElbowLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.ElbowLeft], skeleton.skeleton[JointType.WristLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.WristLeft], skeleton.skeleton[JointType.HandLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.ShoulderCenter], skeleton.skeleton[JointType.ShoulderRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.ShoulderRight], skeleton.skeleton[JointType.ElbowRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.ElbowRight], skeleton.skeleton[JointType.WristRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.WristRight], skeleton.skeleton[JointType.HandRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.ShoulderCenter], skeleton.skeleton[JointType.Spine], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.Spine], skeleton.skeleton[JointType.HipCenter], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.HipCenter], skeleton.skeleton[JointType.HipLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.HipLeft], skeleton.skeleton[JointType.KneeLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.KneeLeft], skeleton.skeleton[JointType.AnkleLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.AnkleLeft], skeleton.skeleton[JointType.FootLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.HipCenter], skeleton.skeleton[JointType.HipRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.HipRight], skeleton.skeleton[JointType.KneeRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.KneeRight], skeleton.skeleton[JointType.AnkleRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnColor(skeleton.skeleton[JointType.AnkleRight], skeleton.skeleton[JointType.FootRight], renderColor, 2.0, offset, kinectID);

                for (int i = 0; i < skeleton.skeleton.Count; i++)
                {
                    DrawJointPointOnColor(skeleton.skeleton[i], renderColor, 2.0, offset, kinectID);
                }

                DrawHandStateOnColor(skeleton.skeleton[JointType.HandLeft], skeleton.leftHandClosed, 5.0, offset, kinectID);
                DrawHandStateOnColor(skeleton.skeleton[JointType.HandRight], skeleton.rightHandClosed, 5.0, offset, kinectID);
            }

            drawingColorSkeleton = false;
        }
        private void RenderSkeletonOnDepth(KinectSkeleton skeleton, Color renderColor, int kinectID)
        {
            if (depthSource != null)
            {
                //Calculate the offset
                Point offset = new Point(0.0, 0.0);
                if (DepthImageCanvas.ActualWidth != DepthImage.ActualWidth)
                {
                    offset.X = (DepthImageCanvas.ActualWidth - DepthImage.ActualWidth) / 2;
                }

                if (DepthImageCanvas.ActualHeight != DepthImage.ActualHeight)
                {
                    offset.Y = (DepthImageCanvas.ActualHeight - DepthImage.ActualHeight) / 2;
                }

                //Render all the bones (this can't be looped because the enum isn't ordered in order of bone connections)
                DrawBoneOnDepth(skeleton.skeleton[JointType.Head], skeleton.skeleton[JointType.ShoulderCenter], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.ShoulderCenter], skeleton.skeleton[JointType.ShoulderLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.ShoulderLeft], skeleton.skeleton[JointType.ElbowLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.ElbowLeft], skeleton.skeleton[JointType.WristLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.WristLeft], skeleton.skeleton[JointType.HandLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.ShoulderCenter], skeleton.skeleton[JointType.ShoulderRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.ShoulderRight], skeleton.skeleton[JointType.ElbowRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.ElbowRight], skeleton.skeleton[JointType.WristRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.WristRight], skeleton.skeleton[JointType.HandRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.ShoulderCenter], skeleton.skeleton[JointType.Spine], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.Spine], skeleton.skeleton[JointType.HipCenter], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.HipCenter], skeleton.skeleton[JointType.HipLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.HipLeft], skeleton.skeleton[JointType.KneeLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.KneeLeft], skeleton.skeleton[JointType.AnkleLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.AnkleLeft], skeleton.skeleton[JointType.FootLeft], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.HipCenter], skeleton.skeleton[JointType.HipRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.HipRight], skeleton.skeleton[JointType.KneeRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.KneeRight], skeleton.skeleton[JointType.AnkleRight], renderColor, 2.0, offset, kinectID);
                DrawBoneOnDepth(skeleton.skeleton[JointType.AnkleRight], skeleton.skeleton[JointType.FootRight], renderColor, 2.0, offset, kinectID);

                for (int i = 0; i < skeleton.skeleton.Count; i++)
                {
                    DrawJointPointOnDepth(skeleton.skeleton[i], renderColor, 2.0, offset, kinectID);
                }

                DrawHandStateOnDepth(skeleton.skeleton[JointType.HandLeft], skeleton.leftHandClosed, 5.0, offset, kinectID);
                DrawHandStateOnDepth(skeleton.skeleton[JointType.HandRight], skeleton.rightHandClosed, 5.0, offset, kinectID);
            }

            drawingDepthSkeleton = false;
        }
        private void DrawHandStateOnColor(Joint joint, bool handState, double radius, Point offset, int kinectID)
        {
            if (joint.TrackingState == TrackingState.Tracked)
            {
                //Map the joint from the skeleton to the depth image
                Point point = server.kinects[kinectID].MapJointToColor(joint, false);

                //Calculate the coordinates on the image (the offset is also added in this section)
                Point imagePoint = new Point(0.0, 0.0);
                imagePoint.X = ((double)point.X / colorSource.PixelWidth) * ColorImage.ActualWidth + offset.X;
                imagePoint.Y = ((double)point.Y / colorSource.PixelHeight) * ColorImage.ActualHeight + offset.Y;

                //Generate the circle for the hand
                Ellipse circle = new Ellipse();
                if (handState)
                {
                    circle.Stroke = new SolidColorBrush(Colors.Red);
                }
                else
                {
                    circle.Stroke = new SolidColorBrush(Colors.Green);
                }
                circle.Fill =  new SolidColorBrush(Colors.Transparent);
                circle.StrokeThickness = 1.0;
                circle.Margin = new Thickness(imagePoint.X - radius, imagePoint.Y - radius, 0, 0);
                circle.HorizontalAlignment = HorizontalAlignment.Left;
                circle.VerticalAlignment = VerticalAlignment.Top;
                circle.Height = radius * 2;
                circle.Width = radius * 2;
                ColorImageCanvas.Children.Add(circle);
            }
        }
        private void DrawHandStateOnDepth(Joint joint, bool handState, double radius, Point offset, int kinectID)
        {
            if (joint.TrackingState == TrackingState.Tracked)
            {
                //Map the joint from the skeleton to the depth image
                Point point = server.kinects[kinectID].MapJointToDepth(joint, false);

                //Calculate the coordinates on the image (the offset is also added in this section)
                Point imagePoint = new Point(0.0, 0.0);
                imagePoint.X = ((double)point.X / depthSource.PixelWidth) * DepthImage.ActualWidth + offset.X;
                imagePoint.Y = ((double)point.Y / depthSource.PixelHeight) * DepthImage.ActualHeight + offset.Y;

                //Generate the circle for the hand
                Ellipse circle = new Ellipse();
                if (handState)
                {
                    circle.Stroke = new SolidColorBrush(Colors.Red);
                }
                else
                {
                    circle.Stroke = new SolidColorBrush(Colors.Green);
                }
                circle.Fill = new SolidColorBrush(Colors.Transparent);
                circle.StrokeThickness = 1.0;
                circle.Margin = new Thickness(imagePoint.X - radius, imagePoint.Y - radius, 0, 0);
                circle.HorizontalAlignment = HorizontalAlignment.Left;
                circle.VerticalAlignment = VerticalAlignment.Top;
                circle.Height = radius * 2;
                circle.Width = radius * 2;
                DepthImageCanvas.Children.Add(circle);
            }
        }
        #endregion

        #region Skeleton GUI methods
        //Updates the data for the skeletons to reflect that a maximum of 6 times the number of kinects in use skeletons are available (only 1/2 of those skeletons support full skeleton tracking)
        private void GenerateSkeletonDataGridData()
        {
            int totalSkeletons = 0;

            for (int i = 0; i < server.kinects.Count; i++)
            {
                if (server.kinects[i].version == KinectVersion.KinectV1)
                {
                    if (((KinectV1Wrapper.Settings)server.serverMasterOptions.kinectOptionsList[i]).mergeSkeletons)
                    {
                        totalSkeletons += 6;
                    }
                }
                else if (server.kinects[i].version == KinectVersion.KinectV2)
                {
                    //TODO: Add the number of skeletons for the each used Kinect v2
                }
                else if (server.kinects[i].version == KinectVersion.NetworkKinect)
                {
                    //TODO: Add the number of skeletons for each used networked kinect
                }
            }

            if (server.kinects.Count * 6 > server.serverMasterOptions.mergedSkeletonOptions.individualSkeletons.Count) //Add skeleton settings
            {
                for (int i = server.serverMasterOptions.mergedSkeletonOptions.individualSkeletons.Count; i < server.kinects.Count * 6; i++)
                {
                    PerSkeletonSettings temp = new PerSkeletonSettings(); //Fill the skeleton information with the default settings
                    string tempServer = "Tracker" + i.ToString();
                    temp.skeletonNumber = i;
                    temp.serverName = tempServer;
                    temp.renderColor = AutoPickSkeletonRenderColor(i);
                    temp.useSkeleton = true;
                    temp.useRightHandGrip = true;
                    temp.rightGripServerName = tempServer;
                    temp.rightGripButtonNumber = 0;
                    temp.useLeftHandGrip = true;
                    temp.leftGripServerName = tempServer;
                    temp.leftGripButtonNumber = 1;
                    server.serverMasterOptions.mergedSkeletonOptions.individualSkeletons.Add(temp);
                }
            }
            else if (server.kinects.Count * 6 < server.serverMasterOptions.mergedSkeletonOptions.individualSkeletons.Count) //Remove skeleton settings
            {
                for (int i = server.serverMasterOptions.mergedSkeletonOptions.individualSkeletons.Count - 1; i >= server.kinects.Count * 6; i--)
                {
                    server.serverMasterOptions.mergedSkeletonOptions.individualSkeletons.RemoveAt(i);
                }
            }
            SkeletonSettingsDataGrid.Items.Refresh();
        }
        //Select a predefined render color based on the skeleton index
        private Color AutoPickSkeletonRenderColor(int index)
        {
            switch (index)
            {
                case 0:
                {
                    return Colors.Red;
                }
                case 1:
                {
                    return Colors.Blue;
                }
                case 2:
                {
                    return Colors.Green;
                }
                case 3:
                {
                    return Colors.Yellow;
                }
                case 4:
                {
                    return Colors.Cyan;
                }
                case 5:
                {
                    return Colors.Fuchsia;
                }
                case 6:
                {
                    return Colors.Orange;
                }
                case 7:
                {
                    return Colors.Brown;
                }
                case 8:
                {
                    return Colors.LightSkyBlue;
                }
                case 9:
                {
                    return Colors.LimeGreen;
                }
                case 10:
                {
                    return Colors.Purple;
                }
                case 11:
                {
                    return Colors.White;
                }
            }
            return Colors.Black;
        }
        //Controls which skeleton sorting mode is used
        private void SkelSortModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            server.serverMasterOptions.mergedSkeletonOptions.skeletonSortMode = (SkeletonSortMethod)SkelSortModeComboBox.SelectedIndex;
        }
        #endregion

        #region Feedback Tab GUI Methods
        //Changes if the options for feedback are enabled or not
        private void UseFeedbackCheckBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            server.serverMasterOptions.feedbackOptions.useFeedback = (bool)UseFeedbackCheckBox.IsChecked;
            FeedbackOptionsGroupBox.IsEnabled = (bool)UseFeedbackCheckBox.IsChecked;
        }
        //Changes the feedback sensor number in the options
        private void FeedbackSensorNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int temp = -1;
            if (int.TryParse(FeedbackSensorNumberTextBox.Text, out temp))
            {
                server.serverMasterOptions.feedbackOptions.feedbackSensorNumber = temp;
            }
        }
        //Changes the name of the feedback server in the options
        private void FeedbackServerNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            server.serverMasterOptions.feedbackOptions.feedbackServerName = FeedbackServerNameTextBox.Text;
        }

        private void FeedbackJointTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            server.serverMasterOptions.feedbackOptions.sensorJointType = (JointType)FeedbackJointTypeComboBox.SelectedIndex;
        }
        #endregion

        #region Server Tab GUI Methods
        private void SettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsTabControl.SelectedIndex == 5 && SettingsTabControl.SelectedIndex != lastSettingsTabIndex)
            {
                if (!server.isRunning)
                {
                    string error;
                    server.parseSettings(out error);
                }
                UpdateServersDisplayCollection();
            }

            lastSettingsTabIndex = SettingsTabControl.SelectedIndex;
        }
        private void UpdateServersDisplayCollection()
        {
            configuredServers.Clear();

            //Check the analog servers, since this is first, we know a server with the same name doesn't exist, so we don't have to check
            for (int i = 0; i < server.serverMasterOptions.analogServers.Count; i++)
            {
                ConfiguredServerData tempData = new ConfiguredServerData();
                tempData.ServerName = server.serverMasterOptions.analogServers[i].serverName;
                tempData.AnalogServer = true;
                tempData.AnalogChannels = server.serverMasterOptions.analogServers[i].trueChannelCount;
                configuredServers.Add(tempData);
            }

            //Check the button servers
            for (int i = 0; i < server.serverMasterOptions.buttonServers.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < configuredServers.Count; j++)
                {
                    if (configuredServers[j].ServerName == server.serverMasterOptions.buttonServers[i].serverName)
                    {
                        found = true;
                        configuredServers[j].ButtonServer = true;
                        configuredServers[j].ButtonChannels = server.serverMasterOptions.buttonServers[i].trueButtonCount;
                        break;
                    }
                }

                if (!found)
                {
                    ConfiguredServerData tempData = new ConfiguredServerData();
                    tempData.ServerName = server.serverMasterOptions.buttonServers[i].serverName;
                    tempData.ButtonServer = true;
                    tempData.ButtonChannels = server.serverMasterOptions.buttonServers[i].trueButtonCount;
                    configuredServers.Add(tempData);
                }
            }

            //Check the image servers
            for (int i = 0; i < server.serverMasterOptions.imagerServers.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < configuredServers.Count; j++)
                {
                    if (configuredServers[j].ServerName == server.serverMasterOptions.imagerServers[i].serverName)
                    {
                        found = true;
                        configuredServers[j].ImageServer = true;
                        break;
                    }
                }

                if (!found)
                {
                    ConfiguredServerData tempData = new ConfiguredServerData();
                    tempData.ServerName = server.serverMasterOptions.imagerServers[i].serverName;
                    tempData.ImageServer = true;
                    configuredServers.Add(tempData);
                }
            }

            //Check the text servers
            for (int i = 0; i < server.serverMasterOptions.textServers.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < configuredServers.Count; j++)
                {
                    if (configuredServers[j].ServerName == server.serverMasterOptions.textServers[i].serverName)
                    {
                        found = true;
                        configuredServers[j].TextServer = true;
                        break;
                    }
                }

                if (!found)
                {
                    ConfiguredServerData tempData = new ConfiguredServerData();
                    tempData.ServerName = server.serverMasterOptions.textServers[i].serverName;
                    tempData.TextServer = true;
                    configuredServers.Add(tempData);
                }
            }

            //Check the tracker servers
            for (int i = 0; i < server.serverMasterOptions.trackerServers.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < configuredServers.Count; j++)
                {
                    if (configuredServers[j].ServerName == server.serverMasterOptions.trackerServers[i].serverName)
                    {
                        found = true;
                        configuredServers[j].TrackerServer = true;
                        configuredServers[j].TrackerChannels = server.serverMasterOptions.trackerServers[i].sensorCount;
                        break;
                    }
                }

                if (!found)
                {
                    ConfiguredServerData tempData = new ConfiguredServerData();
                    tempData.ServerName = server.serverMasterOptions.trackerServers[i].serverName;
                    tempData.TrackerServer = true;
                    tempData.TrackerChannels = server.serverMasterOptions.trackerServers[i].sensorCount;
                    configuredServers.Add(tempData);
                }
            }

            ServersDataGrid.Items.Refresh();
        }
        #endregion

        #region Log Tab GUI Methods
        private void verboseOutputCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if (verboseOutputCheckbox.IsChecked != null)
            {
                bool verboseTemp = verboseOutputCheckbox.IsChecked.Value;
                verbose = verboseTemp;
                if (server != null)
                {
                    server.Verbose = verboseTemp;
                }
            }
        }
        #endregion
    }
}
