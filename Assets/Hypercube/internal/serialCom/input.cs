﻿using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;


//the main Hypercube input class, use this to access all physical input from Volume
//note that Volume stores its calibration inside the touchscreen circuit board.  This calibration is read by this class and sent to the castMesh.

//regarding the i/o of calibration data into the touchscreen pcb, it is stored on the board in 3 areas:
//area 0 = the basic config data. stored in text form native to the dataFileDict class.  This is < 1k of data containg things such as projector resolution, touch screen resolution, does this hardware use an fpga, etc.
//area 1 = the unsullied slices.  < 1k of calibration data that conforms to an ideal perfect undistorted format if the projector could perfectly project onto slices without perspective or distortion of any kind.
//area 2 = the sullied slices. About 50k of data. This is a calibrated output of the slices in vertex positions.  These are typically cut up into 33 x 9 vertices of articulation per slice.

namespace hypercube
{
    public class input : MonoBehaviour
    {
        //singleton pattern
        private static input instance = null;
        public static input _get() { return instance; }
        public static void _debugLog(string logText, bool newline = true) { if (!instance) return;  if (instance.debug) Debug.Log(logText); if (instance.debugText) { if (newline) instance.debugText.text += logText + "\n"; else instance.debugText.text += logText; } }
        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(this.gameObject);
            //end singleton

            touchPanel = null;

            if (!searchForSerialComs())
                _debugLog("<color=orange>Can't get input from Volume because no ports were detected! Confirm that Volume is connected via USB.</color>");
        }

        public int baudRate = 57600;
        public int reconnectionDelay = 500;
        public int maxUnreadMessage = 5;
        public int maxAllowedFailure = 3;
        public bool debug = false;
        public UnityEngine.UI.Text debugText = null;
        public static bool _debug
        {
            get
            {
                if (!instance)
                    return false;
                return instance.debug;
            }
        }

        public float touchPanelFirmwareVersion { get; private set; }
        public static touchScreenInputManager touchPanel { get; private set;}   
#if HYPERCUBE_INPUT
        serialPortFinder[] portSearches; //we wait for a handshake to know which serial port is which.
        List<string> badSerialPorts = new List<string>();  //ports we already know are not what we are looking for.
#endif

        //these keep track of all touchScreen targets, and hence the in input system can send them user input data as it is received.
        static HashSet<touchScreenTarget> eventTargets = new HashSet<touchScreenTarget>();
        public static void _setTouchScreenTarget(touchScreenTarget t, bool addRemove)
        {
            if (addRemove)
                eventTargets.Add(t);
            else
                eventTargets.Remove(t);
        }


        //use this instead of Start(),  that way we know we have our hardware settings info ready before we begin receiving data
        public static void init(dataFileDict d)
        {
            if (!d)
            {
                Debug.LogError("Input was passed bad hardware dataFileDict!");
                return;
            }

            if (!instance)
                return;

#if HYPERCUBE_INPUT

            if (touchPanel != null)
                touchPanel.setTouchScreenDims(d);
#endif
        }

#if HYPERCUBE_INPUT

        public static void _processTouchScreenEvent(touch t)
        {
            if (t == null)
            {
                Debug.LogWarning("Please report a bug in hypercube input. A null touch event was sent for processing.");
                return;
            }

            if (eventTargets.Count == 0)
                return;

            if (t.state == touch.activationState.TOUCHDOWN)
            {
                foreach (touchScreenTarget target in eventTargets)
                    target.onTouchDown(t);
            }
            else if (t.state == touch.activationState.ACTIVE)
            {
                foreach (touchScreenTarget target in eventTargets)
                    target.onTouchMoved(t);
            }
            else if (t.state == touch.activationState.TOUCHUP)
            {
                foreach (touchScreenTarget target in eventTargets)
                    target.onTouchUp(t);
            }
        }

        bool searchForSerialComs()
        {
            if (getIsStillSearchingForSerial()) //we are still searching.
                return false;

            string[] allNames = getPortNames();

            if (allNames.Length == 0)
                return false;

            //filter out ports we already know are or are not ours
            List<string> names = new List<string>();
            foreach (string n in allNames)
            {
                bool good = true;
                foreach(string b in badSerialPorts)
                {
                    if (n == b)
                    {
                        good = false;
                        break;
                    }
                }

                if (!good)
                    continue;

                if (touchPanel != null && n == touchPanel.serial.portName)
                    continue;

                names.Add(n);
            }

            portSearches = new serialPortFinder[names.Count];
            for (int i = 0; i < portSearches.Length; i++)
            {     
                portSearches[i] = new serialPortFinder();
                portSearches[i].identifyPort(createInputSerialPort(names[i])); //add a component that manages every port, and set off to identify what it is.
            }
                
            return true;
        }

        //are we still looking for serial comms and such?
        bool getIsStillSearchingForSerial()
        {
            if (portSearches == null || portSearches.Length == 0) 
                return false;

            for (int i = 0; i < portSearches.Length; i++)
            {
                if (portSearches[i] != null)
                    return true;
            }

            return false;
        }


        float connectTimer = 0f;
        void Update()
        {

            if (touchPanel != null && touchPanel.serial.readDataAsString) //we are still getting config and calibration from pcb (or are being forced to by forceStringRead)
            {
                updateGetSettingsFromPCB();
            }
            else if (touchPanel != null) //normal path
            {
                touchPanel.update();
            }
            else //still searching for serial ports.
            {                   
                connectTimer += Time.deltaTime;
                if (getIsStillSearchingForSerial())
                    updateSerialComSearch(Time.deltaTime);
                else if (connectTimer > 1f)
                {
                    searchForSerialComs(); //try searching again.
                    connectTimer = 0f;
                }
                return;
            }
        }

        //handle PCB during period where we are just getting config data from it.
        //float repingForDataTime = 1f;
        void updateGetSettingsFromPCB()
        {
            if (touchPanel == null || !touchPanel.serial.readDataAsString)
            { 
                Debug.LogError("Bug in this code!");
                return;
            }

            string data = touchPanel.serial.ReadSerialMessage();

            //if (data == null || data == "") //backup.. in case message was missed? This is def needed on osx
            //{
            //    repingForDataTime -= Time.deltaTime;
            //    if (repingForDataTime <= 0f)
            //    {
            //        touchPanel.serial.SendSerialMessage("read0"); //we seem to have missed the message... try again?
            //        repingForDataTime = 1f; //timer
            //    }
            //}

            while (data != null && data != "")
            {
                data = data.Trim();
                if (data == null || data == "")
                    break;
                
                hypercube.input._debugLog("IN: " + data);

                if (data.StartsWith("data0::") && data.EndsWith("::done"))
                {
                    input._debugLog("<color=#00ff00>Touch Panel config received.\nAsking for calibration...</color>");
                    string[] toks = data.Split(new string[] { "::" }, System.StringSplitOptions.None);
                    if (castMesh.canvas)
                        castMesh.canvas.setPCBbasicSettings(toks[1]); //store it in the castMesh... it will use it if needed, ignore it if it already has USB settings.
                    if (toks[1].Contains("useFPGA=True"))
                        touchPanel.serial.SendSerialMessage("read1"); //give us the perfect slices.  If it uses an FPGA
                    else
                        touchPanel.serial.SendSerialMessage("read2"); //ask for the calibrated slices.
                    return; //return is important here, to avoid calling readMessage() again, in case calling methods want to change what we do once we have what we want.
                }
                else if (data.StartsWith("data") && data.EndsWith("::done")) //we got a calibration
                {
                    string[] toks = data.Split(new string[] { "::" }, System.StringSplitOptions.None);
                    Vector2[,,] verts = null;
                    if (utils.bin2Vert(toks[1], out verts))
                    {
                        castMesh cm = input._get().GetComponent<castMesh>();
                        if (!cm.hasCalibration) //don't push it through if we already have usb calibration
                        {
                            cm._setCalibration(verts);
#if HYPERCUBE_DEV
                            if (cm.calibratorV) cm.calibratorV.setLoadedVertices(verts, false); //if we are calibrating, the calibrator needs to know about previous calibrations                                       
#endif
                        }
#if HYPERCUBE_DEV                    
                        else if (cm.calibratorBasic) cm.calibratorBasic.pcbText.color = Color.green;  //let the dev know the pcb has viable data, even though we didn't use it.
#endif
                        input._debugLog("<color=#00ff00>Calibration received: VALID</color>");
                    }
                    else if (data == "data1::::0::done" || data == "data2::::0::done")
                        input._debugLog("<color=#00ff00>Calibration received from TP PCB:</color> EMPTY");
                    else if (data.StartsWith("data1::") )
                        input._debugLog("<color=#00ff00>Calibration received from TP PCB:</color> <color=#ff0000>INVALID</color>"); //perfect
                    else if ( data.StartsWith("data2::"))
                        input._debugLog("<color=#00ff00>Calibration received from TP PCB:</color> <color=#ff0000>INVALID</color>");  //calibrated
                    
                         
                        
                    touchPanel.serial.readDataAsString = false;//we have what we want, now we only need to handle our normal touch data from here
                    return;
                }
#if HYPERCUBE_DEV
                else if (data.StartsWith("mode::recording::"))
                {
                    _recordingMode = true;
                    return;
                }
                else if (data.StartsWith("recording::done"))
                {
                    _recordingMode = false;
                    return;
                }
#endif
                data = touchPanel.serial.ReadSerialMessage();
            }

        }


        //we haven't found all of our ports, keep trying.
        void updateSerialComSearch(float deltaTime)
        {
            for (int i = 0; i < portSearches.Length; i++)
            {
                if (portSearches[i] == null)
                    continue;

                serialPortType t = portSearches[i].update(deltaTime);
                if (t == serialPortType.SERIAL_UNKNOWN) //a timeout or some other problem.  This is likely not a port related to us.
                {
                    //input._debugLog("<color=orange>Failed to connect to "+ portSearches[i].getSerialInput().portName +" </color>");
                    GameObject.Destroy(portSearches[i].getSerialInput());
                    badSerialPorts.Add(portSearches[i].getSerialInput().portName);
                    portSearches[i] = null;
                }
                else if (t == serialPortType.SERIAL_TOUCHPANEL)
                {
                    //note that the serialController is still readAsString = true here

                    input._debugLog("<color=#00ff00>Touch Panel PCB identified.</color>");
                    touchPanelFirmwareVersion = portSearches[i].firmwareVersion;

                    portSearches[i].getSerialInput().SendSerialMessage("read0");//send for the config asap.

                    //also give it to the touchpanel, this will let other methods call input.touchpanel without getting a null, 
                    //but it wont receive updates until we get a calibration.            
                    touchPanel = new touchScreenInputManager(portSearches[i].getSerialInput());

#if HYPERCUBE_DEV
                    castMesh cm = input._get().GetComponent<castMesh>();
                    if (cm.calibratorBasic)
                    {
                        cm.calibratorBasic.fwVersionNumber.text = "Firmware\nv" + touchPanelFirmwareVersion.ToString();
                        cm.calibratorBasic.pcbText.color = Color.yellow;  //let the dev know that we have found the pcb.
                    }   
#endif

                    portSearches[i] = null; //stop checking this port for relevance.                   

                    Debug.Log("Hypercube: Successfully connected to Volume Touch Panel running firmware v" + touchPanelFirmwareVersion);
                    
                    //TEMP:this version of the tools only knows how to use touchpanel serial port. we are done.
                    //if we ever need to find other ports, this should be removed so it can continue searching.
                    endPortSearch(); 
                }
            }
        }


        void endPortSearch()
        {
            for (int i = 0; i < portSearches.Length; i++)
            {
                if (portSearches[i] != null)
                {
                    badSerialPorts.Add(portSearches[i].getSerialInput().portName); 
                    GameObject.Destroy(portSearches[i].getSerialInput());
                    portSearches[i] = null;
                }
            }
        }

        static string[] getPortNames()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return System.IO.Ports.SerialPort.GetPortNames();
#else
            //this code is from http://answers.unity3d.com/questions/643078/serialportsgetportnames-error.html
            int p = (int)Environment.OSVersion.Platform;
            List<string> serial_ports = new List<string>();

            // Are we on Unix?
            if (p == 4 || p == 128 || p == 6)
            {
                string[] ttys = System.IO.Directory.GetFiles("/dev/", "tty.*");  //In the GetPortNames function, it looks for ports that begin with "/dev/ttyS" or "/dev/ttyUSB" . However, OS X ports begin with "/dev/tty.".
                foreach (string dev in ttys)
                {
                    if (dev.StartsWith("/dev/tty."))
                        serial_ports.Add(dev);
                }
            }

            return serial_ports.ToArray();
#endif
        }



        SerialController createInputSerialPort(string comName)
        {
            SerialController sc = gameObject.AddComponent<SerialController>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            sc.portName = "\\\\.\\" + comName; //the crazy slashes are so the code can handle > com9 https://support.microsoft.com/en-us/help/115831/howto-specify-serial-ports-larger-than-com9
#else
            sc.portName = comName; 
#endif
            sc.baudRate = baudRate;
            sc.reconnectionDelay = reconnectionDelay;
            sc.maxUnreadMessages = maxUnreadMessage;
            sc.maxFailuresAllowed = maxAllowedFailure;
            sc.enabled = true;
            //sc.readDataAsString = true;
            return sc;
        }


        //code related to i/o of config and calibration data on the pcb
            #region IO to PCB


#if HYPERCUBE_DEV
        bool _recordingMode = false;
        float serialTimeoutIO = 5f;
        public enum pcbState
        {
            INVALID = 0,
            SUCCESS,
            FAIL,
            WORKING
        }
        public static pcbState pcbIoState = pcbState.INVALID;
        public IEnumerator _writeSettings(string settingsData)
        {
            float startTime = Time.timeSinceLevelLoad;
            if (touchPanel != null && touchPanel.serial.isConnected)
            {
                pcbIoState = pcbState.WORKING;
                _recordingMode = false;

                //prepare the pcb to accept our data
                touchPanel.serial.SendSerialMessage("write0"); //settings data

                while (!_recordingMode)
                {
                    if (_serialTimeOutCheck(startTime))
                        yield break;
                    yield return pcbIoState;
                }

                settingsData = dataFileDict.base64Encode(settingsData);
                touchPanel.serial.SendSerialMessage(settingsData);

                while (_recordingMode)//don't exit until we are done.
                {
                    if (_serialTimeOutCheck(startTime))
                        yield break;
                    yield return pcbIoState;
                }
                pcbIoState = pcbState.SUCCESS;
                yield break;
            }
            pcbIoState = pcbState.FAIL;
        }

        public IEnumerator _writeSlices(Vector2[,,] d, bool sullied)
        {

            float startTime = Time.timeSinceLevelLoad;
            if (touchPanel != null && touchPanel.serial.isConnected)
            {
                pcbIoState = pcbState.WORKING;
                _recordingMode = false;

                //prepare the pcb to accept our data
                if (sullied)
                    touchPanel.serial.SendSerialMessage("write2");
                else
                    touchPanel.serial.SendSerialMessage("write1"); //perfect slices

                while (!_recordingMode)
                {
                    if (_serialTimeOutCheck(startTime))
                        yield break;
                    yield return pcbIoState;
                }

                string saveData;
                utils.vert2Bin(d, out saveData);
                touchPanel.serial.SendSerialMessage(saveData);

                while (_recordingMode)//don't exit until we are done.
                {
                    if (_serialTimeOutCheck(startTime))
                        yield break;
                    yield return pcbIoState;
                }
                pcbIoState = pcbState.SUCCESS;
                yield break;
            }
            pcbIoState = pcbState.FAIL;
        }

        public bool _serialTimeOutCheck(float startTime)
        {
            if (Time.timeSinceLevelLoad - startTime > serialTimeoutIO)
            {
                pcbIoState = pcbState.FAIL;
                _recordingMode = false;
                return true;
            }

            return false;
        }
#endif

            #endregion



#else //We use HYPERCUBE_INPUT because I have to choose between this odd warning below, or immediately throwing a compile error for new users who happen to have the wrong settings (IO.Ports is not included in .Net 2.0 Subset).  This solution is odd, but much better than immediately failing to compile.
    
        bool searchForSerialComs()
        {
            printWarning();
			return false;
        }
		
    
        void Start () 
        {
            printWarning();
            this.enabled = false;
        }

        static void printWarning()
        {
            Debug.LogWarning("TO USE HYPERCUBE INPUT: \nHypercube > Load Volume friendly Unity prefs\n    - OR -\n1) Go To - Edit > Project Settings > Player    2) Set Api Compatability Level to '.Net 2.0'    3) Add HYPERCUBE_INPUT to Scripting Define Symbols (separate by semicolon, if there are others)");
        }

		public static void _processTouchScreenEvent(touch t)
		{
		}
#endif

        }


}
