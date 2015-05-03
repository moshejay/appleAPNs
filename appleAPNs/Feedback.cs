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
    public class Feedback
    {
        public string displayStuff;
        public TcpClient client;
        private SslStream ourSSLstream;
        public byte[] tcpInBuffer;
        public bool gotDisplayStuff,isConnected;
        public Feedback()
        {
            tcpInBuffer = new byte[500];
            ourSSLstream = null;
            gotDisplayStuff = isConnected = false;
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
                client.Connect("gateway.sandbox.push.apple.com", 2196);
                aNS = client.GetStream(); // only needed here, unlike in coms & decoder
                ourSSLstream = new SslStream(aNS, false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                ourSSLstream.AuthenticateAsClient("feedback.sandbox.push.apple.com", myCertifCollection, SslProtocols.Tls, true);
                if (ourSSLstream.IsMutuallyAuthenticated == false)
                {
                    gotDisplayStuff = true;
                    displayStuff = "authentication issue...not mutually authenticated\r\n";
                    return false;
                }
                int lc = ourSSLstream.ReadTimeout;
                ourSSLstream.ReadTimeout = 100;
                isConnected = true;
                return true;
            }
            catch (AuthenticationException e)
            {
                ourSSLstream.Close();
                ourSSLstream = null;
                client.Close();
                client = null;
                gotDisplayStuff = true;
                isConnected = false;
                displayStuff = "authentication error: " + e.ToString();
            }
            return false;
        }
        public void Disconnect()
        {
            if (ourSSLstream != null)
            {
                ourSSLstream.Close();
                ourSSLstream = null;
                isConnected = false;
            }
            if (client != null)
            {
                client.Close();
                client = null;
                isConnected = false;
            }
            return;
        }

        public void ReadPort()
        {
            int read = 0;
            if (ourSSLstream == null || client == null)
            {
                gotDisplayStuff = true;
                displayStuff = " SSL or client are Null so can't read\r\n";
                isConnected = false;
                return;
            }
            if (ourSSLstream.CanRead == false || client.Connected == false) // client.connected can turn between here & the read!!!
            {
                gotDisplayStuff = true;
                displayStuff = " Can't read from SSL or not connected, so cant get\r\n";
                isConnected = false;
                return;
            }
            try
            {
                read = ourSSLstream.Read(tcpInBuffer, 0, 38);
                if (read > 0)
                {
                    gotDisplayStuff = true;
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
    }
}
