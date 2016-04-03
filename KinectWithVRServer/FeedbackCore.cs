﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Vrpn;

namespace KinectWithVRServer
{
    class FeedbackCore
    {
        private volatile bool forceStop = false;
        private volatile ServerRunState clientState = ServerRunState.Stopped;
        public ServerRunState ClientState
        {
            get { return clientState; }
        }
        public bool isRunning
        {
            get { return (clientState == ServerRunState.Running); }
        }
        private string serverName = "";
        private ServerCore server;
        private MainWindow parent;
        //private bool isGUI = false;
        internal bool isVerbose = false;
        private int watchedSensor;

        public FeedbackCore(bool verboseOutput, ServerCore serverCore, MainWindow thisParent = null)
        {
            isVerbose = verboseOutput;
            server = serverCore;

            if (thisParent != null)
            {
                //isGUI = true;
                parent = thisParent;
            }
        }

        public void StartFeedbackCore(string trackerName, int sensorNumber)
        {
            serverName = trackerName;
            watchedSensor = sensorNumber;

            forceStop = false;
            clientState = ServerRunState.Starting;
            RunFeedbackCoreDelegate feedbackDelegate = RunFeedbackCore;
            feedbackDelegate.BeginInvoke(null, null);
        }

        public void StopFeedbackCore()
        {
            forceStop = true;
            int count = 0;
            while (count < 50) //Wait for the core to stop
            {
                if (clientState == ServerRunState.Stopped)
                {
                    break;
                }
                Thread.Sleep(10);
            }
            if (count >= 30 && clientState != ServerRunState.Stopped)
            {
                throw new Exception("Could not stop feedback core!");
            }
        }

        private void RunFeedbackCore()
        {
            Connection clientConnection = Connection.GetConnectionByName(serverName);
            TrackerRemote client = new TrackerRemote(serverName, clientConnection);
            client.PositionChanged += client_PositionChanged;
            clientState = ServerRunState.Running;

            while (!forceStop)
            {
                clientConnection.Update();
                client.Update();
                Thread.Yield();
            }

            clientState = ServerRunState.Stopping;
            client.PositionChanged -= client_PositionChanged;
            client.Dispose();
            clientConnection.Dispose();
            clientState = ServerRunState.Stopped;
        }

        private void client_PositionChanged(object sender, TrackerChangeEventArgs e)
        {
            if (e.Sensor == watchedSensor)
            {
                //TODO: Does the feedback position need to be written to the GUI?
                server.feedbackPosition = (System.Windows.Media.Media3D.Point3D)e.Position;

                System.Diagnostics.Debug.WriteLine("Position: " + server.feedbackPosition.Value.X + ", " + server.feedbackPosition.Value.Y + ", " + server.feedbackPosition.Value.Z);
            }
        }

        private void updateAudioBeamAngles(System.Windows.Media.Media3D.Point3D feedbackPosition)
        {
            for (int i = 0; i < server.kinects.Count; i++)
            {
                if (server.kinects[i].version == KinectBase.KinectVersion.KinectV1)
                {
                    if (((KinectV1Wrapper.Settings)server.serverMasterOptions.kinectOptionsList[i]).audioTrackMode == KinectBase.AudioTrackingMode.Feedback)
                    {
                        ((KinectV1Wrapper.Core)server.kinects[i]).UpdateAudioAngle(feedbackPosition);
                    }
                }
                else if (server.kinects[i].version == KinectBase.KinectVersion.KinectV2)
                {
                    //TODO: Call the update method for the KinectV2 audio angle beamforming
                }
            }
        }

        private delegate void RunFeedbackCoreDelegate();
    }
}
