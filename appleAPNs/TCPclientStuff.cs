using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Net.Sockets;

namespace appleAPNs
{
    //----------------------------------------------
    //     the tcp client happens in ANOTHER THREAD
    //----------------------------------------------
    public class TcpClientSide
    {
        public string displayStuff;
        public byte[] theNotification;
        public bool doQuit,doDisconnect,doConnect;
        public bool startWriting, stopReading, gotDisplayStuff;
        public bool tcpIsConnected;
        public TcpClient client;
        private SslStream ourSSLstream;
        public byte[] tcpInBuffer,tcpOutBuffer;
        public int tcpInCount,tcpInPos,tcpOutPos;
        public TcpClientSide()
        {
            theNotification = new byte[250];
            tcpInBuffer = new byte[500];
            tcpOutBuffer = new byte[400];
            tcpInPos = tcpInCount = tcpOutPos = 0;
            stopReading = true;
            gotDisplayStuff = startWriting = doQuit = doDisconnect = doConnect = false;
            ourSSLstream = null;
        }
        //
        // called back from RemoteCertificateValidationCallback
        //
        public static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None) // else Do not allow this client to communicate with unauthenticated servers. 
                return true;
            Console.WriteLine("Certificate error: {0}" + sslPolicyErrors);
            return false; 
        }
        //------------------------------------------------
        // connect to the apple APN
        //------------------------------------------------------
        public bool Connect()
        {
            NetworkStream aNS;
            string certifPath = "c:\\aps_development.cer"; //path to the actual file..this is the development certificate not the mpos certificate,
            // ..but the mpos certificate has been installed into the certificate path on the pc using mmc.exe & snap-in
            string certifPwd = "";// the pwd is blank
            X509Certificate2 myCertif = new X509Certificate2(certifPath, certifPwd);
            X509Certificate2Collection myCertifCollection = new X509Certificate2Collection(myCertif);
            try
            {
                if (ourSSLstream != null) 
                    ourSSLstream.Close();
                if (client != null) client.Close();
                client = new TcpClient();
                client.Connect("gateway.sandbox.push.apple.com", 2195);
                aNS = client.GetStream(); // only needed here, unlike in coms & decoder
                ourSSLstream = new SslStream(aNS,false,new RemoteCertificateValidationCallback (ValidateServerCertificate),null);
                ourSSLstream.AuthenticateAsClient("gateway.sandbox.push.apple.com",myCertifCollection,SslProtocols.Tls,true);
                if(ourSSLstream.IsMutuallyAuthenticated == false)
                {
                    gotDisplayStuff = true;
                    displayStuff = "authentication issue...not mutually authenticated\r\n";
                }
                tcpIsConnected = true;
                int lc = ourSSLstream.ReadTimeout;
                ourSSLstream.ReadTimeout = 100;
                return true;
            }
            catch (AuthenticationException e)
            {
                ourSSLstream.Close();
                ourSSLstream = null;
                client.Close();
                client = null;
                gotDisplayStuff = true;
                displayStuff = "authentication error: " + e.ToString();
            }
            doDisconnect = true;
            return false;
        }
        public void Disconnect()
        {
            tcpIsConnected = startWriting = false;
            stopReading = true;
            if (ourSSLstream != null)
            {
                ourSSLstream.Close();
                ourSSLstream = null;
            }
            if (client != null)
            {
                client.Close();
                client = null;
            }
            return;
        }
        public void Quit()
        {
            Disconnect();
        }
        public void ReadPort()
        {
            int read = 0;
            if (ourSSLstream == null || client == null)
            {
                gotDisplayStuff = stopReading = true;
                displayStuff = " SSL or client are Null so can't read\r\n";
                return;
            }
            if (ourSSLstream.CanRead == false || client.Connected == false) // client.connected can turn between here & the read!!!
            {
                gotDisplayStuff = stopReading = true;
                displayStuff = " Can't read from SSL or not connected, so cant get\r\n";
                return;
            }
            try
            {
                tcpInPos = tcpInCount;                    
                read = ourSSLstream.Read(tcpInBuffer, tcpInPos, 500);
                if (read > 0)
                {
                    for (int lc = 0; lc < read; lc++)
                    {
                        if (tcpInBuffer[lc] != 0)
                            gotDisplayStuff = true;
                    }
                    if (gotDisplayStuff)
                    {
                        for (int lc = 0; lc < read; lc++)
                        {
                            byte abyt = (byte)((tcpInBuffer[lc] >> 4) & 0x0f);
                            if (abyt >= 10)
                                abyt += 55;
                            else
                                abyt += 48;
                            displayStuff += Convert.ToChar(abyt);
                            abyt = (byte)(tcpInBuffer[lc] & 0x0f);
                            if (abyt >= 10)
                                abyt += 55;
                            else
                                abyt += 48;
                            displayStuff += Convert.ToChar(abyt);
                        }
                    }
                }
                return;
            }
            catch (TimeoutException)
            {
                return;
            }
            catch (SocketException e)
            {
//                if (e.NativeErrorCode.Equals(10035) == false) // 10035 == WSAEWOULDBLOCK if true means still connected
                {
                    gotDisplayStuff = true;
                    displayStuff = "...read (error): " + e.ToString();
                }
            }
            catch (IOException e)
            {
                if (read != 0)
                {
                    gotDisplayStuff = true;
                    displayStuff = "...read error: " + e.ToString();
                }
                return;
            }
        }
        public void WritePort()
        {
            startWriting = false;
            gotDisplayStuff = true;
            if (ourSSLstream == null || client == null)
            {
                displayStuff = " SSL or client are Null so can't send\r\n";
                return;
            }
            if (ourSSLstream.CanWrite == false || client.Connected == false)
            {
                displayStuff = " Can't write to SSL or not connected, so cant send\r\n";
                return;
            }
            try
            {
                ourSSLstream.Write(tcpOutBuffer, 0, tcpOutPos);
                ourSSLstream.Flush();
                displayStuff = "...Sent to Apple\r\n";
                return;
            }
            catch (IOException e)
            {
                displayStuff = "...write error: " + e.ToString();
                return;
            }
        }

        /*---------------------------------------------
         * new way of doing things
        -----------------------------------------------*/
        public void SendToApple(string theDeviceToken, string theMessage)
        {
            uint anUint;
            int dataLen;
            tcpOutBuffer[0] = 2; // command..fixed val 2 as per apple p54 local & remote notification progr guide
            tcpOutPos = 5;
            dataLen = 0;
              
            //*** the device token ***
            tcpOutBuffer[tcpOutPos++] = 1; // item ID of 1 is device Token
            tcpOutBuffer[tcpOutPos++] = 0; // msb of that 2byte word (of 32) is 0
            tcpOutBuffer[tcpOutPos++] = 32; // fixed length..LSB of 2byte word
            dataLen += 3;

            byte[] theDeviceTokenBytes = new byte[64];
            int lc = 0;
            byte bch,cch;
            foreach (char ach in theDeviceToken)
            {
                bch = (byte)ach;
                if (bch > 0x39)
                    bch -= (byte)0x37;
                else
                    bch -= (byte)0x30;
                theDeviceTokenBytes[lc++] = bch;
            }

            for (lc = 0; lc < 64;)
            {
                bch = (byte)(theDeviceTokenBytes[lc++] << 4);
                cch = theDeviceTokenBytes[lc++];
                tcpOutBuffer[tcpOutPos++] = (byte)(bch | cch);
            }
            dataLen += 32;

            //*** now the actual notification
            string payload = "{\"aps\":{\"alert\":\"" + theMessage + "\",\"badge\":10,\"sound\":\"default\"}}";
            //string payload = "{\"aps\":{\"alert\":\"hello moshe\",\"badge\":2,\"sound\":\"default\"}}";
            anUint = (uint)payload.Length;
            tcpOutBuffer[tcpOutPos++] = 2; // item ID
            tcpOutBuffer[tcpOutPos++] = (byte)(anUint >> 8); // msb of that 2byte word (of 1) is 0
            tcpOutBuffer[tcpOutPos++] = (byte)anUint; // fixed length..LSB of 2byte word
            foreach (char ach in payload)
                tcpOutBuffer[tcpOutPos++] = (byte)ach;
            dataLen += 3 + payload.Length;    

            //*** the notification number ***
            anUint = SecondsSince20150101();  // our notification ID is the num of secs since 1/01/2015
            tcpOutBuffer[tcpOutPos++] = 3; // item ID
            tcpOutBuffer[tcpOutPos++] = 0; // msb of that 2byte word (of 4) is 0
            tcpOutBuffer[tcpOutPos++] = 4; // fixed length..LSB of 2byte word
            OurConvertLittleEndianIntToBigEndianInByteArray(ref tcpOutBuffer, tcpOutPos, anUint);
            tcpOutPos += 4;
            dataLen += 7;
    
            //*** the expiration date ***
            tcpOutBuffer[tcpOutPos++] = 4; // item ID
            tcpOutBuffer[tcpOutPos++] = 0; // msb of that 2byte word (of 4) is 0
            tcpOutBuffer[tcpOutPos++] = (byte)4; // fixed length..LSB of 2byte word
            anUint = SecondsWhenNotifExpires();
            OurConvertLittleEndianIntToBigEndianInByteArray(ref tcpOutBuffer, tcpOutPos, anUint);
            tcpOutPos += 4;
            dataLen += 7;
                
            //*** the priority ***
            tcpOutBuffer[tcpOutPos++] = 5; // item ID
            tcpOutBuffer[tcpOutPos++] = 0; // msb of that 2byte word (of 1) is 0
            tcpOutBuffer[tcpOutPos++] = 1; // fixed length..LSB of 2byte word
            tcpOutBuffer[tcpOutPos++] = 10; //priority...10 or 5
            dataLen += 4;

            //*** the overall length excludes cmd & frame size...dont use 
            tcpOutBuffer[1] = tcpOutBuffer[2] = 0;
            tcpOutBuffer[3] = (byte)(dataLen >> 8);
            tcpOutBuffer[4] = (byte)dataLen;
//            OurConvertLittleEndianIntToBigEndianInByteArray(ref tcpOutBuffer, 1, (uint)dataLen);
            stopReading = false;
            startWriting = true;
            return;
        }
        /*---------------------------------------------
         * enhanced legacy way of doing things
        -----------------------------------------------*/
        public void enhancedlegacySendToApple(string theDeviceToken, string theMessage)
        {
            uint anUint;
            tcpOutBuffer[0] = 1; // command..fixed val 0 as per apple p54 local & remote notification progr guide
            tcpOutPos = 1;

            //*** the notification number ***
            anUint = SecondsSince20150101();  // our notification ID is the num of secs since 1/01/2015
            OurConvertLittleEndianIntToBigEndianInByteArray(ref tcpOutBuffer, tcpOutPos, anUint);
            tcpOutPos += 4;

            //*** the expiration date ***
            anUint = SecondsWhenNotifExpires();
            OurConvertLittleEndianIntToBigEndianInByteArray(ref tcpOutBuffer, tcpOutPos, anUint);
            tcpOutPos += 4;


            //*** the device token ***
            tcpOutBuffer[tcpOutPos++] = 0; // msb of that 2byte word (of 32) is 0
            tcpOutBuffer[tcpOutPos++] = (byte)32; // fixed length..LSB of 2byte word

            byte[] theDeviceTokenBytes = new byte[64];
            int lc = 0;
            byte bch, cch;
            foreach (char ach in theDeviceToken)
            {
                bch = (byte)ach;
                if (bch > 0x39)
                    bch -= (byte)0x37;
                else
                    bch -= (byte)0x30;
                theDeviceTokenBytes[lc++] = bch;
            }

            for (lc = 0; lc < 64; )
            {
                bch = (byte)(theDeviceTokenBytes[lc++] << 4);
                cch = theDeviceTokenBytes[lc++];
                tcpOutBuffer[tcpOutPos++] = (byte)(bch | cch);
            }

            //*** now the actual notification
            string payload = "{\"aps\":{\"alert\":\"" + theMessage + "\",\"badge\":10,\"sound\":\"default\"}}";
            //string payload = "{\"aps\":{\"alert\":\"hello moshe\",\"badge\":2,\"sound\":\"default\"}}";
            anUint = (uint)payload.Length;
            tcpOutBuffer[tcpOutPos++] = (byte)(anUint >> 8); // msb of that 2byte word (of 1) is 0
            tcpOutBuffer[tcpOutPos++] = (byte)anUint; // fixed length..LSB of 2byte word
            foreach (char ach in payload)
                tcpOutBuffer[tcpOutPos++] = (byte)ach;

            stopReading = false;
            startWriting = true;
            return;
        }
        /*---------------------------------------------
         * legacy way of doing things
        -----------------------------------------------*/
        public void oldSendToApple(string theDeviceToken, string theMessage)
        {
            uint anUint;
            tcpOutBuffer[0] = 0; // command..fixed val 0 as per apple p54 local & remote notification progr guide
            tcpOutPos = 1;

            //*** the device token ***
            tcpOutBuffer[tcpOutPos++] = 0; // msb of that 2byte word (of 32) is 0
            tcpOutBuffer[tcpOutPos++] = (byte)32; // fixed length..LSB of 2byte word

            byte[] theDeviceTokenBytes = new byte[64];
            int lc = 0;
            byte bch, cch;
            foreach (char ach in theDeviceToken)
            {
                bch = (byte)ach;
                if (bch > 0x39)
                    bch -= (byte)0x37;
                else
                    bch -= (byte)0x30;
                theDeviceTokenBytes[lc++] = bch;
            }

            for (lc = 0; lc < 64; )
            {
                bch = (byte)(theDeviceTokenBytes[lc++] << 4);
                cch = theDeviceTokenBytes[lc++];
                tcpOutBuffer[tcpOutPos++] = (byte)(bch | cch);
            }

            //*** now the actual notification
            string payload = "{\"aps\":{\"alert\":\"" + theMessage + "\",\"badge\":15,\"sound\":\"default\"}}";
//            string payload = "{\"aps\":{\"alert\":\"hello moshe\",\"badge\":2,\"sound\":\"default\"}}";
            anUint = (uint)payload.Length;
            tcpOutBuffer[tcpOutPos++] = (byte)(anUint >> 8); // msb of that 2byte word (of 1) is 0
            tcpOutBuffer[tcpOutPos++] = (byte)anUint; // fixed length..LSB of 2byte word
            foreach (char ach in payload)
                tcpOutBuffer[tcpOutPos++] = (byte)ach;

            stopReading = false;
            startWriting = true;
            return;
        }

        //------------------------------------------------------
        // convert intel little endian to network big endian and put result in the supplied array
        //-------------------------------------------------------
        public void OurConvertLittleEndianIntToBigEndianInByteArray(ref byte [] destArray,int offset,uint theIntToConvert)
        {
            destArray[offset++] = (byte)(theIntToConvert >> 24);
            destArray[offset++] = (byte)(theIntToConvert >> 16);
            destArray[offset++] = (byte)(theIntToConvert >> 8);
            destArray[offset++] = (byte)(theIntToConvert);
            return;
        }
        //------------------------------------------------------
        // get the number of secs since 1/1/2015
        //-------------------------------------------------------
        public uint SecondsSince20150101()
        {
            DateTime dt = new DateTime(2015,1,1);
            DateTime dtnow = DateTime.Now;
            TimeSpan res = dtnow.Subtract(dt);
            uint secs = Convert.ToUInt32(res.TotalSeconds);
            return secs;
        }
        public uint SecondsWhenNotifExpires()
        {
            DateTime dt = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc); //unix epoch time 0:0:0:0 on 01/01/70
            DateTime dtexpire = DateTime.UtcNow;
            dtexpire = dtexpire.AddHours(10.0);
            TimeSpan res = dtexpire.Subtract(dt);
            uint secs = Convert.ToUInt32(res.TotalSeconds);
            return secs;
        }
    }
}