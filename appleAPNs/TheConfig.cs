using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;

namespace appleAPNs
{
    class TheConfig
    {
    }
    /*******************************************************
     * the stuff to do with the configuration data for a device
     * ****************************************************/
    public class OurConfig
    {
        public string apnDev;
        public int apnDevPort;
        public byte[] deviceDataArray;
        public string urlForDeviceTokens;
        public string tcpAPNserverInfo;   // info to display in the text box for the remote APN server
        public string tcpOurServerAddress;   // info to display in the text box when we act as a server
        public string tcpOurServerClientAddr; // address of client connecting to us
        public string devicesDataFile,ourConfigFile;

        public OurConfig()
        {
            deviceDataArray = new byte[50];
            urlForDeviceTokens = "https://holisticsazure.blob.core.windows.net/devicetokens";
            ourConfigFile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "\\appleAPNsConfig.txt";

            ReadConfigData();
        }
        /*********************
         * write the config back to file
         * *******************************/
        public void WriteConfigData()
        {
            FileStream fs;
            BinaryWriter bw;

            if (File.Exists(ourConfigFile) == false)
                fs = new FileStream(ourConfigFile, FileMode.CreateNew);
            else
                fs = new FileStream(ourConfigFile, FileMode.Open, FileAccess.Write);// Create the reader for data.
            bw = new BinaryWriter(fs);
            bw.Write(tcpOurServerAddress);
            bw.Write(devicesDataFile);
            bw.Close();
            fs.Close();
            return;
        }
        /*********************
         * read the config from file
         * *******************************/
        public void ReadConfigData()
        {
            FileStream fs;
            BinaryReader br;
            if (File.Exists(ourConfigFile) == false)
            {
                tcpOurServerAddress = "192.168.1.4";
                devicesDataFile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "\\mPOSdevicesTokens.txt";
                return;
            }
            fs = new FileStream(ourConfigFile, FileMode.Open, FileAccess.Read);
            br = new BinaryReader(fs);
            tcpOurServerAddress = br.ReadString();
            if(tcpOurServerAddress == "")
                tcpOurServerAddress = "192.168.1.0";
            devicesDataFile = br.ReadString();
            if(devicesDataFile == "")
                devicesDataFile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "\\mPOSdevicesTokens.txt";
            fs.Close();
            return;
        }
        /****************************
         * save the data for a device
         * merch# , term# , deviceToken , deviceID
         * **********************************/
        public void StoreDeviceData(byte[] thePacket, int dataLen)
        {
            string  merchNum, termNum, deviceToken, deviceID;
            int[] poses;
            int lc,cl = 0;
            poses = new int[5];
            poses[0] = 1;
            for (cl = 1,lc = 0; lc < dataLen; lc++)
            {
                if(thePacket[lc] == ',')
                    poses[cl++] = lc;
            }
            if (cl != 4) // malformed packet
                return;
            poses[4] = lc;
            merchNum = Encoding.ASCII.GetString(thePacket, poses[0], poses[1] - poses[0]); 
            termNum = Encoding.ASCII.GetString(thePacket, poses[1] + 1, poses[2] - (poses[1] + 1)); // start 1 AFTER the ','
            deviceToken = Encoding.ASCII.GetString(thePacket, poses[2] + 1, poses[3] - (poses[2] + 1));
            deviceID = Encoding.ASCII.GetString(thePacket, poses[3] + 1, poses[4] - (poses[3] + 1));

            int currPos = 0;
            FileStream fs;
            BinaryWriter bw;
            bool repos = false;

            if (File.Exists(devicesDataFile) == false)
                fs = new FileStream(devicesDataFile, FileMode.CreateNew);
            else
            {
                string  aMerchNum, aTermNum, aDeviceToken, aDeviceID;
                BinaryReader br;
                fs = new FileStream(devicesDataFile, FileMode.Open, FileAccess.ReadWrite);// Create the reader for data.
                br = new BinaryReader(fs);
                bool contin = true;
                while (contin)
                {
                    currPos = (int)br.BaseStream.Position;
                    try
                    {
                        aMerchNum = br.ReadString();
                        aTermNum = br.ReadString();
                        aDeviceToken = br.ReadString();
                        aDeviceID = br.ReadString();
                        if (aDeviceID == deviceID) // so we got it already
                        {
                            if (aMerchNum == merchNum && aTermNum == termNum && aDeviceToken == deviceToken)
                            {
                                br.Close();
                                fs.Close();
                                return;
                            }
                            contin = false;
                            repos = true;
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        contin = repos = false;
                    }
                }
                br.Close(); // this also closes the stream
                fs.Close(); // displose of any resources
                if(repos)
                    fs = new FileStream(devicesDataFile, FileMode.Open, FileAccess.Write);
                else
                    fs = new FileStream(devicesDataFile, FileMode.Append);
            }
            bw = new BinaryWriter(fs);
            if (repos)
                bw.Seek((int)currPos, SeekOrigin.Begin); //bw.BaseStream.Position = currPos;
            bw.Write(merchNum);
            bw.Write(termNum);
            bw.Write(deviceToken);
            bw.Write(deviceID);
            bw.Close();
            fs.Close();

            return;
        }
        /****************************************
         * Get all the unique merchant nums in our devices file
         * *************************************/
        public string[] GetAllMerchantNums()
        {
            BinaryReader br;
            FileStream fs;
            string[] theMerches;
            string merchNum,termNum,deviceID,deviceToken;
            theMerches = new string [1];
            theMerches[0] = "All";

            if(File.Exists(devicesDataFile) == false)
            {
                MessageBox.Show("There is no device token file","Apple Notifications");
                theMerches[0] = "No data";
                return theMerches;
            }
            fs = new FileStream(devicesDataFile, FileMode.Open, FileAccess.ReadWrite);// Create the reader for data.
            br = new BinaryReader(fs);
            bool contin = true,gotIt;
            int asize = 1;
            while (contin)
            {
                try
                {
                    merchNum = br.ReadString();
                    termNum = br.ReadString();
                    deviceToken = br.ReadString();
                    deviceID = br.ReadString(); //must read a whole record
                    gotIt = false;
                    foreach(string astr in theMerches)
                    {
                        if(astr == merchNum) 
                        {
                            gotIt = true;
                            break;
                        }
                    }
                    if(gotIt == false) 
                    {
                        Array.Resize(ref theMerches,++asize);
                        theMerches[asize - 1] = merchNum;
                    }                        
                }
                catch (EndOfStreamException)
                {
                    contin = false;
                }
            }
            br.Close(); // this also closes the stream
            fs.Close(); // displose of any resources
            return theMerches;
        }
        /**************************************************
         * get all the terminals for a partic merch in our devices files
         * ***********************************************/
        public string[] GetAllTerminals(string forMerch)
        {
            BinaryReader br;
            FileStream fs;
            string[] theTerms;
            string merchNum, termNum, deviceID, deviceToken;
            theTerms = new string[1];
            theTerms[0] = "All";

            fs = new FileStream(devicesDataFile, FileMode.Open, FileAccess.ReadWrite);// Create the reader for data.
            br = new BinaryReader(fs);
            bool contin = true,gotIt;
            int asize = 1;
            while (contin)
            {
                try
                {
                    merchNum = br.ReadString();
                    termNum = br.ReadString();
                    deviceToken = br.ReadString();
                    deviceID = br.ReadString();     //must read a whole record
                    if (forMerch == "All" || merchNum == forMerch)  // only if same merch
                    {
                        gotIt = false;
                        foreach (string astr in theTerms)
                        {
                            if (astr == termNum)
                            {
                                gotIt = true;
                                break;
                            }
                        }
                        if (gotIt == false)
                        {
                            Array.Resize(ref theTerms, ++asize);
                            theTerms[asize - 1] = termNum;
                        }
                    }    
                }
                catch (EndOfStreamException)
                {
                    contin = false;
                }
            }
            br.Close(); // this also closes the stream
            fs.Close(); // displose of any resources
            return theTerms;
        }
        /**************************************************
         * get all the terminals for a partic merch in our devices files
         * ***********************************************/
        public string[] GetAllDevices(string forMerch, string forTerm)
        {
            BinaryReader br;
            FileStream fs;
            string[] theDevices;
            string merchNum, termNum, deviceID, deviceToken;
            theDevices = new string[1];
            theDevices[0] = "All";

            fs = new FileStream(devicesDataFile, FileMode.Open, FileAccess.ReadWrite);// Create the reader for data.
            br = new BinaryReader(fs);
            bool contin = true,gotIt;
            int asize = 1;
            while (contin)
            {
                try
                {
                    merchNum = br.ReadString();
                    termNum = br.ReadString();
                    deviceToken = br.ReadString();
                    deviceID = br.ReadString();     //must read a whole record
                    if (forMerch == "All" || (merchNum == forMerch && (forTerm == "All" || forTerm == termNum)))
                    { // only if same merch and all the terms for that merch or its for a specific term
                        gotIt = false;
                        foreach (string astr in theDevices)
                        {
                            if (astr == deviceID)
                            {
                                gotIt = true;
                                break;
                            }
                        }
                        if (gotIt == false)
                        {
                            Array.Resize(ref theDevices, ++asize);
                            theDevices[asize - 1] = deviceID;
                        }
                    }    

                }
                catch (EndOfStreamException)
                {
                    contin = false;
                }
            }
            br.Close(); // this also closes the stream
            fs.Close(); // displose of any resources
            return theDevices;
        }
        /********************************************
         * extract the device token given the deviceID
         * ************************************************/
        public string GetTheDeviceToken(string theDeviceID)
        {
            BinaryReader br;
            FileStream fs;
            string merchNum, termNum, deviceID, deviceToken;

            fs = new FileStream(devicesDataFile, FileMode.Open, FileAccess.ReadWrite);// Create the reader for data.
            br = new BinaryReader(fs);
            bool contin = true;
            deviceToken = "";
            while (contin)
            {
                try
                {
                    merchNum = br.ReadString();
                    termNum = br.ReadString();
                    deviceToken = br.ReadString();
                    deviceID = br.ReadString();     //must read a whole record
                    if (deviceID == theDeviceID)
                        contin = false;
                }
                catch (EndOfStreamException)
                {
                    contin = false;
                    deviceToken = "";
                }
            }
            br.Close(); // this also closes the stream
            fs.Close(); // displose of any resources

            return deviceToken;
        }
        /**************************
                 * get the specific device info requested
                 * **********************************/
        public bool ReadDeviceData(string deviceNumber)
        {
            int aret;

            try
            {
                HttpWebRequest theFileRequest = (HttpWebRequest)WebRequest.Create(urlForDeviceTokens + "/" + deviceNumber + ".txt");
                theFileRequest.Method = "GET";
                HttpWebResponse theFileResponse = (HttpWebResponse)theFileRequest.GetResponse();
                Stream theReader = theFileResponse.GetResponseStream(); // Retrieve response stream and wrap in StreamReader
                // if we were doing pure text use a StreamReader rdr = new StreamReader(respStream);
                aret = theReader.Read(deviceDataArray, 0, 50);
                theReader.Close();
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString(), "Apple Notifications");
                //MessageBox.Show("OO can't access the MyDocs folder. please Create it", "ComsAndDecoder");
                return true;
            }
        }
    }
}

