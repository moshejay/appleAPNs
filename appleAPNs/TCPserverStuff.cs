using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms; // needed for messagebox

//using System.ComponentModel;
//using System.Data;
//using System.Drawing;



namespace appleAPNs
{
    //----------------------------------------------
    //     the tcp server happens in ANOTHER THREAD
    //----------------------------------------------
    public class TcpServerSide
    {
        public ServerStepToDo nextStep;
        public PacketStateReached packetState;
        
        /*** cmds for the tcp server ***/
        public TcpListener theListener;
        public TcpClient tcpClient;
        private NetworkStream networkStream;
        public byte[] tcpInBuffer, scratch, tcpPacket;
        public int maxTCPinBuffer;
        public int tcpInCount;
        public int tcpPacketPos;
        public int timerTick, timerTickSec;
        public string theErrorMessage;
        
        public TcpServerSide()
        {
            scratch = new byte[1];
            scratch[0] = 255;
            maxTCPinBuffer = 50;
            tcpInBuffer = new byte[maxTCPinBuffer];
            tcpPacket = new byte[1024];
            tcpInCount = tcpPacketPos = 0;
            networkStream = null;
            theListener = null;
            timerTick = timerTickSec = 0;
            nextStep = ServerStepToDo.Idle;
            return;
        }

        public bool SetUpListener(OurConfig theConfig)
        {
            IPAddress localAddr = IPAddress.Parse(theConfig.tcpOurServerAddress);
            bool aret = true;
            try
            {
                theListener = new TcpListener(localAddr, 14321);
            }
            catch (SocketException ee)
            {
                MessageBox.Show("Invalid Listener:\n" + theConfig.tcpOurServerAddress + ":14321", ee.Message);
                theListener = null;
                aret = false;
            }
            return aret;
        }

        public bool SetResetListener(OurConfig theConfig)
        {
            bool aret = true;
            try
            {
                theListener.Start(); // Start listening for client requests.
            }
            catch (SocketException ee)
            {
                theErrorMessage = "Invalid IP Address: " + theConfig.tcpOurServerAddress + ":14321" + ee.Message + "\r\n";
                theListener = null;
                aret = false;
            }
            return aret;
        }

        public bool CheckForConnections(OurConfig theConfig)
        {
            if (theListener == null) // is this possible?
                return false;
            if (!theListener.Pending()) 
                return false;
            tcpClient = theListener.AcceptTcpClient();
            tcpClient.Client.SendTimeout = 500; // allow 500ms
            networkStream = tcpClient.GetStream(); // Get a stream object for reading and writing
            theConfig.tcpOurServerClientAddr = tcpClient.Client.RemoteEndPoint.ToString();
            tcpInCount = timerTick = 0;  // reinit it
            return true;
        }

        public bool SendSocketData(string theMessage)
        {
            bool ans = true;
            if (networkStream == null || tcpClient == null) // so why did we come in here?
            {
                nextStep = ServerStepToDo.DisconnectClient;
                return false;
            }
            if (networkStream.CanWrite == false || tcpClient.Connected == false) // client.connected can turn between here & the read!!!
            {
                nextStep = ServerStepToDo.ClientDisconnected;
                return false;
            }
            try
            {
                byte [] sending = new byte[100];
                int lc = 0;
                sending[lc++] = 2;  // STX
                foreach(byte ach in theMessage) 
                    sending[lc++] = ach;
                sending[lc++] = 3;  //ETX
                tcpClient.Client.Blocking = false;
                tcpClient.Client.Send(sending,lc, SocketFlags.None); // if returns means still connected
            }
            catch (SocketException e)
            {
                if (e.NativeErrorCode.Equals(10035) == false) // 10035 == WSAEWOULDBLOCK if true means still connected
                {
                    nextStep = ServerStepToDo.ClientDisconnected;
                    ans = false;
                }
            }
            finally
            {
                tcpClient.Client.Blocking = true;
            }
            return ans;
        }

        public bool GetSocketData()
        {
            if (networkStream == null || tcpClient == null) // so why did we come in here?
            {
                nextStep = ServerStepToDo.DisconnectClient;
                return false;
            }
            if (networkStream.CanRead == false || tcpClient.Connected == false) // client.connected can turn between here & the read!!!
            {
                nextStep = ServerStepToDo.ClientDisconnected;
                return false;
            }
            if (networkStream.DataAvailable == false) //must use this else need to set a readtimeout..
            {                                       // but that will then throw an IOexception which we DON'T want
                if (timerTick == 0) // only do something once received data
                    return false;
                if (timerTick++ == 1) timerTickSec = 0;
                else if (timerTick > 0x000AAAAA)
                {
                    ++timerTickSec;
                    timerTick = 2;
                }
                if (timerTickSec < 3) return true;  // allow approx 3 secs so don't send too much
                timerTickSec = 1; // so next time is only 1 secs
                bool ans = true;
                try
                {
                    tcpClient.Client.Blocking = false;
                    tcpClient.Client.Send(scratch, 1, SocketFlags.None); // if returns means still connected
                }
                catch (SocketException e)
                {
                    if (e.NativeErrorCode.Equals(10035) == false) // 10035 == WSAEWOULDBLOCK if true means still connected
                    {
                        nextStep = ServerStepToDo.ClientDisconnected;
                        ans = false;
                    }
                }
                finally
                {
                    tcpClient.Client.Blocking = true;
                }
                return ans;
            }
            if (tcpInCount == maxTCPinBuffer) return true;
            timerTick = 1;
            try
            {
                tcpInCount = networkStream.Read(tcpInBuffer, tcpInCount, maxTCPinBuffer);
                for(int lc = 0;lc < tcpInCount;lc++) {
                    if(tcpInBuffer[lc] == 0x02)  // the start of a packet...regardless of current state
                    {  
                        packetState = PacketStateReached.GotStart;
                        tcpPacketPos = 0;
                        continue;
                    }
                    if(tcpInBuffer[lc] == 0x03 && packetState == PacketStateReached.GotStart) // the end of a packet..but only if we had a start
                    {
                        packetState =  PacketStateReached.GotEnd;
                        continue;
                    }
                    if(packetState == PacketStateReached.GotStart) 
                        tcpPacket[tcpPacketPos++] = tcpInBuffer[lc];
                }
                tcpInCount = 0;
                return true;
            }
            catch (SocketException e)
            {
                MessageBox.Show("SocketException: {0}", e.Message);
                nextStep = ServerStepToDo.DisconnectClient;
            }
            return false;
        }

        public void DisconnectRemoteClient()
        {
            if (networkStream != null)
            {
                networkStream.Close();
                networkStream = null;
            }
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
            return;
        }

        public void Quit()
        {
            DisconnectRemoteClient(); // just in case
            if (theListener != null)
            {
                theListener.Stop();
                theListener = null;  //disposes of it
            }
            return;
        }
    }
}