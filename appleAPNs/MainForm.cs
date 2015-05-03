using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Net;
using System.Management;


namespace appleAPNs
{
    //QuitListening = quitListeningThread
    // ClientDisconnected = clientHasClosed
    public enum ServerStepToDo
    {
        Setup = 0, StartListening,QuitListening, Idle, CloseListener,
        CheckForConnections, CheckForData, DisplayData,SendData,
        DisconnectAtWaiting,DisconnectClient, ClientDisconnected
    };

    public enum PacketStateReached
    {
        None = 0,GotStart,GotEnd
    };

    public partial class MainForm : Form
    {
        private bool gotTextForClientBox;
        private Thread ourTcpClient;
        private Thread ourTcpServer;
        public TcpClientSide theTcpClient = null;
        public TcpServerSide theTcpServer = null;
        public OurConfig ourConfig;
        public Feedback appleFeedback;
        public string textToWrite,textForButton,textForClientBox;
        delegate void SetTCPThreadCallback();

        public MainForm()
        {
            InitializeComponent();

            ourConfig = new OurConfig();
            LoadTheDeviceTokenFile();

            ourLocalAddresstb.Text = ourConfig.tcpOurServerAddress;
            theTcpServer = new TcpServerSide();
            theTcpClient = new TcpClientSide();

            appleFeedback = new Feedback();

            deviceTokenURLtb.Text = ourConfig.devicesDataFile; // ourConfig.urlForDeviceTokens;
            ourTcpClient = new Thread(new ThreadStart(StartTcpSessionThread)); // create the TCPw thread
            ourTcpServer = new Thread (new ThreadStart (StartTcpServerSessionThread));
            ourTcpClient.Start();
            ourTcpServer.Start();
            return;
        }
        //----------------------
        //*** closing
        //------------------------
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ourTcpClient != null)
            {
                if (ourTcpClient.ThreadState == ThreadState.Running)
                {
                    theTcpClient.stopReading = true;
                    theTcpClient.doDisconnect = true;
                    while (theTcpClient.tcpIsConnected == true) Thread.Sleep(50);
                    theTcpClient.doQuit = true;
                    while (ourTcpClient.ThreadState == ThreadState.Running) Thread.Sleep(50);
                }
            }
            if (ourTcpServer != null)
            {
                if (ourTcpServer.ThreadState == ThreadState.WaitSleepJoin)
                {
                    ourTcpServer.Interrupt();
                    Thread.Sleep(50);
                }
                if (ourTcpServer.ThreadState == ThreadState.Running)
                {
                    theTcpServer.nextStep =  ServerStepToDo.QuitListening;
                    while (ourTcpServer.ThreadState == ThreadState.Running) Thread.Sleep(50);
                }
            }
            return;
        }
        //--------------------------------------
        // load the device token file
        //--------------------------------------
        private void LoadTheDeviceTokenFile()
        {
            merchantNumCB.Items.Clear();
            string[] merches = ourConfig.GetAllMerchantNums();
            foreach (string astr in merches)
                merchantNumCB.Items.Add(astr);

            merchantNumCB.SelectedIndex = 0; // keep separate cos there are callbacks as soon as these change
            terminalNumCB.SelectedIndex = 0;
            deviceIDCB.SelectedIndex = 0;

            terminalNumCB.Enabled = deviceIDCB.Enabled = false;
            return;
        }
        //--------------------------------------
        // check if got a valid local ip address
        //--------------------------------------
        private void ourLocalAddresstb_Leave(object sender, EventArgs e)
        {
            int anum;
            string theString = ourLocalAddresstb.Text.Trim();
            char[] theDelimiter = new char[1];
            theDelimiter[0] = '.';
            string[] theParts = theString.Split(theDelimiter);
            for (int lc = 0; lc < 4; lc++)
            {
                try
                {
                    string anElt = theParts[lc];
                    if (anElt == null || anElt == "")
                    {
                        lc++;
                        MessageBox.Show("Missing element " + lc.ToString(), "Apple Notifications");
                        ourLocalAddresstb.Focus();
                        return;
                    }
                    anum = Convert.ToInt16(anElt);
                    if (anum > 250 || anum < 0)
                    {
                        lc++;
                        MessageBox.Show("Element " + lc.ToString() + " is invalid", "Apple Notifications");
                        ourLocalAddresstb.Focus();
                        return;
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    lc++;
                    MessageBox.Show("Missing element " + lc.ToString(), "Apple Notifications");
                    ourLocalAddresstb.Focus();
                    return;
                }
            }
            ourConfig.tcpOurServerAddress = theString;
            return;
        }
        //-----------------------------------------------
        // start or stop our server thread
        //---------------------------------------------
        private void tcpServerStartStopBtn_Click(object sender, EventArgs e)
        {
            switch(theTcpServer.nextStep)
            {
                case ServerStepToDo.Idle:
                    theTcpServer.nextStep = ServerStepToDo.Setup;
                    break;
                case ServerStepToDo.CheckForConnections:
                    theTcpServer.nextStep = ServerStepToDo.DisconnectAtWaiting;
                    break;
                case ServerStepToDo.CheckForData:
                    theTcpServer.nextStep = ServerStepToDo.DisconnectClient;
                    break;
                case ServerStepToDo.Setup:
                case ServerStepToDo.StartListening:
                case ServerStepToDo.DisconnectClient:
                case ServerStepToDo.ClientDisconnected:
                case ServerStepToDo.QuitListening:
                default:
                    break;
            }

        }
        //-----------------------------------------------
        // save the current local address
        //---------------------------------------------
        private void saveTcpServerBtn_Click(object sender, EventArgs e)
        {
            ourConfig.WriteConfigData();
            return;
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void deviceTokenURLtb_TextChanged(object sender, EventArgs e)
        {

        }
        //---------------------------------------------------------
        // send a notification to the specified device
        //---------------------------------------------------------
        private void sendNotificationBtn_Click(object sender, EventArgs e)
        {
            string theDeviceToken = ourConfig.GetTheDeviceToken(deviceIDCB.SelectedItem.ToString());
            theTcpClient.SendToApple(theDeviceToken, messageToSendTB.Text); // just makes up the packet
            displayRTB.Text += ">Sending to Apple\r\n";
            return;
        }
        #region appleAPN tcp client side
        //---------------------------------------------------------
        // if we are connected to apple APN then disconnect & vice versa
        //------------------------------------------------------------
        private void TCPconnectDisconnectBtn_Click(object sender, EventArgs e)
        {
            if (theTcpClient.tcpIsConnected == false)
            {
                theTcpClient.doConnect = true;
                TCPconnectDisconnectBtn.Text = "Disconnect";
            }
            else
            {
                theTcpClient.doDisconnect = true;
                TCPconnectDisconnectBtn.Text = "Connect";
            }
        }
        //-----------------------------------------------------------------------------------
        // we can't access any windows controls in the main thread from another thread
        // hence a callback
        //-----------------------------------------------------------------------------------
        private void CallBackFromTCPclientThread()
        {
            displayRTB.Text += "\nFrom TCP " + theTcpClient.displayStuff + "\n";
            theTcpClient.displayStuff = "";
            theTcpClient.gotDisplayStuff = false;
            return;
        }
        /*******************************************
        * separate thread
        * note issue with canInvoke for the lbl control...there needs to be an intervening window screen event before this can be
        * called again..see coms & decoder for full original code
        * *****************************************/
        private void StartTcpSessionThread()
        {
            bool contin = true;
            Thread.Sleep(100); // time for the window to be up
            SetTCPThreadCallback tcpCallBack = new SetTCPThreadCallback(CallBackFromTCPclientThread);
          
            while (contin) // do forever
            {
                if (theTcpClient.gotDisplayStuff)
                {
                    Invoke(tcpCallBack, new object[] { });  // for some reason this can't be invoked twice
                }
                if (theTcpClient.doConnect) // connect to the APN...we want to keep the session alive
                {
                    theTcpClient.Connect();
                    theTcpClient.doConnect = false;
                }
                if (theTcpClient.stopReading == false)
                    theTcpClient.ReadPort();
                if (theTcpClient.startWriting)
                    theTcpClient.WritePort();

                if (theTcpClient.doDisconnect)
                {
                    theTcpClient.Disconnect();
                    theTcpClient.doDisconnect = false;
                }
                if (theTcpClient.doQuit)
                {
                    theTcpClient.Quit();
                    theTcpClient.doQuit = false;
                    contin = false;
                }
            }
            return;
        }
        #endregion
        //***********************************************************************
        #region us acting as a tcp server for iOS devices
        /********************************************************
         * remote client dropped the connection so close the listener and display the pic
         * closing listener is performed in the thread
         * **********************************************************/
        private void DisplayForListener()
        {
            if(textToWrite != "")
                displayRTB.AppendText(textToWrite);
            if(textForButton != "")
                tcpServerStartStopBtn.Text = textForButton;
            if (gotTextForClientBox)
            {
                remoteClientLbl.Text = textForClientBox;
                gotTextForClientBox = false;
            }
            return;
        }
        /*******************************************
        * separate thread
        * note issue with canInvoke for the lbl control...there needs to be an intervening window screen event 
        * before this can be called again
        * *****************************************/
        private void StartTcpServerSessionThread()
        {
            bool contin = true;
            Thread.Sleep(100); // time for the window to be up
            SetTCPThreadCallback gg = new SetTCPThreadCallback(DisplayForListener);
            textToWrite = "";
            while (contin)
            {
                switch(theTcpServer.nextStep)
                {
                    case ServerStepToDo.Setup:
                        if (theTcpServer.SetUpListener(ourConfig) == true) // resets startListening
                        {
                            textForButton = "Stop Listener";
                            textToWrite = "";
                            theTcpServer.nextStep = ServerStepToDo.StartListening;
                        }
                        else
                        {
                            Thread.Sleep(200);
                            textToWrite = " Cannot set up listener\r\n";
                            textForButton = "Start Listener";
                            theTcpServer.nextStep = ServerStepToDo.CloseListener;
                        }
                        Invoke(gg, new object[] { });
                        break;
                    case ServerStepToDo.StartListening:
                        if (theTcpServer.SetResetListener(ourConfig) == true)
                        {
                            textToWrite = "";
                            textForClientBox = "Waiting for Connections";
                            gotTextForClientBox = true;
                            theTcpServer.nextStep = ServerStepToDo.CheckForConnections;
                        }
                        else
                        {
                            textToWrite = theTcpServer.theErrorMessage;
                            textForButton = "Start Listener";
                            theTcpServer.nextStep = ServerStepToDo.CloseListener;
                        }
                        Invoke(gg, new object[] { });
                        break;
                    case ServerStepToDo.CheckForConnections:
                        if (theTcpServer.CheckForConnections(ourConfig) == true)
                        {
                            textToWrite = " New Connection:\r\n";
                            textForClientBox = ourConfig.tcpOurServerClientAddr;
                            gotTextForClientBox = true;
                            textForButton = "Close Connection";
                            gotTextForClientBox = true;
                            Invoke(gg, new object[] { });
                            theTcpServer.nextStep = ServerStepToDo.CheckForData;
                        }
                        break;
                    case ServerStepToDo.CheckForData:
                        if (theTcpServer.GetSocketData() == true)
                        {
                            if(theTcpServer.packetState == PacketStateReached.GotEnd)
                            {
                                textToWrite = "<-" + Encoding.ASCII.GetString(theTcpServer.tcpPacket, 1, theTcpServer.tcpPacketPos - 1) + "\r\n";
                                Invoke(gg, new object[] { });
                                ourConfig.StoreDeviceData(theTcpServer.tcpPacket, theTcpServer.tcpPacketPos);
                                theTcpServer.packetState = PacketStateReached.None;
                                if(theTcpServer.tcpPacket[0] == 7)
                                {
                                    theTcpServer.nextStep = ServerStepToDo.SendData;
                                }
                            }
/*
                            if (theTcpServer.tcpInCount > 0)
                            {
                                textToWrite = "<-" + Encoding.ASCII.GetString(theTcpServer.tcpInBuffer, 0, theTcpServer.tcpInCount);
                                theTcpServer.tcpInCount = 0;
                                Invoke(gg, new object[] { });
                            }
 */ 
                        }
                        break;
                    case ServerStepToDo.SendData:
                        if (theTcpServer.SendSocketData(missedNotificationTB.Text) == true)
                        {
                            textToWrite = "-> Sent notification\r\n";

                        }
                        else
                            textToWrite = "-> error in sending\r\n";
                        Invoke(gg, new object[] { });
                        theTcpServer.nextStep = ServerStepToDo.CheckForData;
                        break;
                    case ServerStepToDo.DisconnectClient:
                    case ServerStepToDo.DisconnectAtWaiting:
                    case ServerStepToDo.ClientDisconnected:
                        if (theTcpServer.nextStep == ServerStepToDo.DisconnectClient)
                            textToWrite = " Closed Client\r\n";
                        else if (theTcpServer.nextStep == ServerStepToDo.ClientDisconnected)
                            textToWrite = " Client has Closed\r\n";
                        else
                            textToWrite = "";
                        textForButton = "Stop Listening";
                        textForClientBox = "";
                        gotTextForClientBox = true;
                        Invoke(gg, new object[] { });
                        theTcpServer.DisconnectRemoteClient();
                        theTcpServer.nextStep = ServerStepToDo.StartListening;
                        break;
                    case ServerStepToDo.CloseListener:
                        theTcpServer.Quit();
                        theTcpServer.nextStep = ServerStepToDo.Idle;
                        break;
                    case ServerStepToDo.QuitListening:
                        theTcpServer.Quit();
                        contin = false;
                        break;
                    default: // idle
                        break;
                }
            }
            return;
        }

         private void merchantNumCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            terminalNumCB.Items.Clear();
            deviceIDCB.Items.Clear();

            deviceIDCB.Items.Add("All");
            deviceIDCB.Enabled = false;

             string bstr = merchantNumCB.SelectedItem.ToString();
             if (merchantNumCB.SelectedIndex != 0)  // 0 is All
             {
                 string[] terms = ourConfig.GetAllTerminals(bstr);
                 foreach (string astr in terms)
                     terminalNumCB.Items.Add(astr);
                 terminalNumCB.Enabled = true;
             }
             else
             {
                 terminalNumCB.Items.Add("All");
                 terminalNumCB.Enabled = false;
             }
             terminalNumCB.SelectedIndex = 0; // keep separate cos of callbacks
             deviceIDCB.SelectedIndex = 0;
            return;
        }

         private void merchantNumCB_Enter(object sender, EventArgs e)
         {
             merchantNumCB.BackColor = Color.Azure;
             return;
         }

         private void merchantNumCB_Leave(object sender, EventArgs e)
         {
             merchantNumCB.BackColor = Color.WhiteSmoke;
             return;
         }

         private void terminalNumCB_SelectedIndexChanged(object sender, EventArgs e)
         {
             deviceIDCB.Items.Clear();

             string bstr = terminalNumCB.SelectedItem.ToString();
             string cstr = merchantNumCB.SelectedItem.ToString();
             if (terminalNumCB.SelectedIndex != 0)  // 0 is All
             {
                 string[] devs = ourConfig.GetAllDevices(cstr, bstr);
                 foreach (string astr in devs)
                     deviceIDCB.Items.Add(astr);
                 deviceIDCB.Enabled = true;
             }
             else
             {
                 deviceIDCB.Items.Add("All");
                 deviceIDCB.Enabled = false;
             }
             deviceIDCB.SelectedIndex = 0;
             return;
         }

         private void terminalNumCB_Enter(object sender, EventArgs e)
         {
             terminalNumCB.BackColor = Color.Azure;
             return;
         }

         private void terminalNumCB_Leave(object sender, EventArgs e)
         {
             terminalNumCB.BackColor = Color.WhiteSmoke;
             return;
         }

         private void deviceIDCB_SelectedIndexChanged(object sender, EventArgs e)
         {
             return;
         }

         private void deviceIDCB_Enter(object sender, EventArgs e)
         {
             deviceIDCB.BackColor = Color.Azure;
             return;
         }

         private void deviceIDCB_Leave(object sender, EventArgs e)
         {
             deviceIDCB.BackColor = Color.WhiteSmoke;
             return;
         }

         private void deviceTokenURLtb_Leave(object sender, EventArgs e)
         {
             ourConfig.devicesDataFile = deviceTokenURLtb.Text;
         }

         private void  reloadTokenFileBtn_Click(object sender, EventArgs e)
         {
             LoadTheDeviceTokenFile();
         }

         private void button1_Click(object sender, EventArgs e)
         {
            if (appleFeedback.isConnected)
            {
                button1.Text = "Connect to Feedback";
                appleFeedback.Disconnect();
                return;
            }
            appleFeedback.Connect();
            if (appleFeedback.isConnected)
            {
                button1.Text = "Disconnect Feedback";
                ReadFeedback();
            }
         }

         private void button2_Click(object sender, EventArgs e)
         {
             ReadFeedback();
         }

         private void ReadFeedback()
         {
             bool contin = true;
             displayRTB.Text += "";
             while (contin)
             {
                 appleFeedback.ReadPort();
                 if (appleFeedback.gotDisplayStuff)
                 {
                     displayRTB.Text += appleFeedback.displayStuff;
                     appleFeedback.displayStuff = "";
                     appleFeedback.gotDisplayStuff = false;
                     if (appleFeedback.isConnected == false) // ahh...NOT connected
                     {
                         contin = false;
                         button1.Text = "Connect to Feedback";
                         appleFeedback.Disconnect();
                     }
                 }
                 else
                     contin = false;
             }
         }
    }
#endregion

}
