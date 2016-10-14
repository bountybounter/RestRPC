﻿using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using WebScriptHook.Framework.Messages.Inputs;
using WebScriptHook.Framework.Messages.Outputs;
using WebScriptHook.Framework.Plugins;
using WebScriptHook.Framework.Serialization;
using WebSocketSharp;

namespace WebScriptHook.Framework
{
    public class WebScriptHookComponent
    {
        const int CHANNEL_SIZE = 50;

        static AutoResetEvent networkWaitHandle = new AutoResetEvent(false);
        WebSocket ws;
        Thread networkThread;
        DateTime lastPollTime = DateTime.Now;

        ConcurrentQueue<WebInput> inputQueue = new ConcurrentQueue<WebInput>();
        ConcurrentQueue<WebOutput> outputQueue = new ConcurrentQueue<WebOutput>();

        JsonSerializerSettings outSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new WritablePropertiesOnlyResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// Gets the name of this WebScriptHook component
        /// </summary>
        public string Name
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets information on the remote this component is connecting to
        /// </summary>
        public Uri RemoteUri
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the rate the component polls messages from server
        /// </summary>
        public TimeSpan PollingRate
        {
            get;
            private set;
        }

        public bool IsRunning
        {
            get;
            private set;
        } = false;

        public bool IsConnected
        {
            get { return ws != null && ws.IsAlive; }
        }

        public PluginManager PluginManager
        {
            get { return PluginManager.Instance; }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="componentName">The name of this WebScriptHook component</param>
        /// <param name="remoteUri">Remote server settings</param>
        /// <param name="pollingRate">Rate to poll messages from server. It is always bound by the update rate</param>
        public WebScriptHookComponent(string componentName, Uri remoteUri, TimeSpan pollingRate)
        : this(componentName, remoteUri, pollingRate, null, LogType.None)
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="componentName">The name of this WebScriptHook component</param>
        /// <param name="remoteUri">Remote server settings</param>
        /// <param name="logWriter">Log writer</param>
        /// <param name="logLevel">Level of logging</param>
        public WebScriptHookComponent(string componentName, Uri remoteUri, TimeSpan pollingRate, TextWriter logWriter, LogType logLevel)
        {
            this.Name = componentName;
            this.RemoteUri = remoteUri;
            this.PollingRate = pollingRate;

            Logger.Writer = logWriter;
            Logger.LogLevel = logLevel;

            // Set up network worker, which exchanges data between plugin and server
            ws = new WebSocket(remoteUri.ToString());
            ws.OnMessage += WS_OnMessage;
            ws.OnOpen += WS_OnOpen;
            // TODO: Add WebSocket authentication
            //ws.SetCredentials("username", "password", true);

            // Create plugin manager instance
            PluginManager.CreateInstance();
        }

        /// <summary>
        /// Creates connection to the remote server
        /// </summary>
        public void Start()
        {
            if (!IsRunning)
            {
                networkThread = new Thread(NetworkWorker_ThreadProc);
                networkThread.IsBackground = true;
                networkThread.Start();

                IsRunning = true;
                Logger.Log("WebScriptHook component \"" + Name + "\" started.", LogType.Info);
            }
        }

        /// <summary>
        /// Stops connection to the remote server. 
        /// All unprocessed inputs and unsent outputs will be discarded
        /// </summary>
        public void Stop()
        {
            if (IsRunning)
            {
                // Abort network thread and close ws connection
                networkThread.Abort();
                ws.Close(CloseStatusCode.Normal);
                // Clear queues
                inputQueue = new ConcurrentQueue<WebInput>();
                outputQueue = new ConcurrentQueue<WebOutput>();

                IsRunning = false;
            }
        }

        /// <summary>
        /// Updates WebScriptHookComponent. Should be called on every tick.
        /// </summary>
        public void Update()
        {
            // Signal network worker
            networkWaitHandle.Set();

            // Tick plugin manager
            PluginManager.Instance.Update();

            // Process input messages
            ProcessInputMessages();
        }

        private void ProcessInputMessages()
        {
            WebInput input;
            while (inputQueue.TryDequeue(out input))
            {
                try
                {
                    // Process this message
                    Logger.Log("Executing " + input.ToString(), LogType.Debug);
                    object retVal = PluginManager.Instance.Dispatch(input.Cmd, input.Args);
                    // Only return real values. Do not return NoOutput messages
                    if (retVal == null || retVal.GetType() != typeof(NoOutput))
                    {
                        outputQueue.Enqueue(new WebReturn(retVal, input.UID));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex.ToString(), LogType.Error);
                }
            }
        }

        private void NetworkWorker_ThreadProc()
        {
            while (true)
            {
                // Wait until a tick happens
                networkWaitHandle.WaitOne();

                try
                {
                    // Check if connection is alive. If not, attempt to connect to server
                    // WS doesn't throw exceptions when connection fails or unconnected
                    if (!ws.IsAlive) ws.Connect();

                    // Send a pulse to poll messages queued on the server
                    if (DateTime.Now - lastPollTime > PollingRate)
                    {
                        ws.Send(new byte[] { });
                        lastPollTime = DateTime.Now;
                    }

                    // Send output data
                    bool outputExists = outputQueue.Count > 0;
                    WebOutput output;
                    while (outputQueue.TryDequeue(out output))
                    {
                        // Serialize the object to JSON then send back to server.
                        try
                        {
                            ws.Send(JsonConvert.SerializeObject(output, outSerializerSettings));
                        }
                        catch (Exception sendExc)
                        {
                            Logger.Log(sendExc.ToString(), LogType.Error);
                        }
                    }
                }
                catch (Exception exc)
                {
                    Logger.Log(exc.ToString(), LogType.Error);
                }
            }
        }

        private void WS_OnMessage(object sender, MessageEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            WebInput input = JsonConvert.DeserializeObject<WebInput>(e.Data);
            if (input != null)
            {
                inputQueue.Enqueue(input);
            }
        }

        private void WS_OnOpen(object sender, EventArgs e)
        {
            // Component requests the server to create a channel for this component
            ws.Send(JsonConvert.SerializeObject(new ChannelRequest(Name, CHANNEL_SIZE)));
            Logger.Log("WebSocket connection established: " + ws.Url, LogType.Info);
        }
    }
}
