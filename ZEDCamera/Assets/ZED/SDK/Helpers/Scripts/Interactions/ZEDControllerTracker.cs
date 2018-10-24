﻿using UnityEngine;
using UnityEngine.VR;
using System.Collections.Generic;
#if ZED_STEAM_VR
using Valve.VR;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Causes the GameObject it's attached to to position itself where a tracked VR object is, such as 
/// a Touch controller or Vive Tracker, but compensates for the ZED's latency. This way, virtual
/// controllers don't move ahead of its real-world image. 
/// This is done by logging position data from the VR SDK in use (Oculus or OpenVR/SteamVR) each frame, but only
/// applying that position data to this transform after the delay in the latencyCompensation field. 
/// Used in the ZED GreenScreen, Drone Shooter, Movie Screen, Planetarium and VR Plane Detection example scenes. 
/// </summary>
public class ZEDControllerTracker : MonoBehaviour
{
    /// <summary>
    /// Type of VR SDK loaded. 'Oculus', 'OpenVR' or empty.
    /// </summary>
	private string loadeddevice = "";

#if ZED_STEAM_VR //Only enabled if the SteamVR Unity plugin is detected. 

    /// <summary>
    /// Enumerated version of the uint index SteamVR assigns to each device. 
    /// Converted from OpenVR.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole). 
    /// </summary>
    public enum EIndex
    {
        None = -1,
        Hmd = (int)OpenVR.k_unTrackedDeviceIndex_Hmd,
        Device1,
        Device2,
        Device3,
        Device4,
        Device5,
        Device6,
        Device7,
        Device8,
        Device9,
        Device10,
        Device11,
        Device12,
        Device13,
        Device14,
        Device15
    }
    [HideInInspector]
    public EIndex index = EIndex.None;

    /// <summary>
    /// How long since we've last checked OpenVR for the specified device. 
    /// Incremented by Time.deltaTime each frame and reset when it reached timerMaxSteamVR. 
    /// </summary>
    private float timerSteamVR = 0.0f;

    /// <summary>
    /// How many seconds to wait between checking if the specified device is present in OpenVR.
    /// The check is performed when timerSteamVR reaches this number, unless we've already retrieved the device index. 
    /// </summary>
    private float timerMaxSteamVR = 1.0f;
    private Devices oldDevice;
#endif

    /// <summary>
    /// Per each tracked object ID, contains a list of their recent positions.
    /// Used to look up where OpenVR says a tracked object was in the past, for latency compensation. 
    /// </summary>
    public Dictionary<int, List<TimedPoseData>> poseData = new Dictionary<int, List<TimedPoseData>>();
    
    /// <summary>
    /// Types of tracked devices. 
    /// </summary>
    public enum Devices
    {
        RightController,
        LeftController,

#if ZED_STEAM_VR
        ViveTracker,
#endif
        Hmd,
    };

    /// <summary>
    /// Type of trackable device that should be tracked.
    /// </summary>
    [Tooltip("Type of trackable device that should be tracked.")]
    public Devices deviceToTrack;

    /// <summary>
    /// Latency in milliseconds to be applied on the movement of this tracked object, so that virtual controllers don't
    /// move ahead of their real-world image.
    /// </summary>
    [Tooltip("Latency in milliseconds to be applied on the movement of this tracked object, so that virtual controllers don't" + 
        " move ahead of their real-world image.")]
    [Range(0, 200)]
    public int latencyCompensation = 78;

    /// <summary>
    /// The Serial number of the controller/tracker to be tracked. 
    /// If specified, it will override the device returned using the 'Device to Track' selection. 
    /// Useful for forcing a specific device to be tracked, instead of the first left/right/Tracker object found.
    /// If Null, then there's no calibration to be applied to this script.
    /// If NONE, the ZEDControllerOffset failed to find any calibration file.
    /// If S/N is present, then this script will calibrate itself to track the correct device, if that's not the case already.
    /// Note that ZEDOffsetController will load this number from a GreenScreen calibration file, if present. 
    /// </summary>
    [Tooltip("The Serial number of the controller/tracker to be tracked." +
        " If specified, overrides the 'Device to Track' selection.")]
    public string SNHolder = "";

    /// <summary>
    /// Cached transform that represents the ZED's head, retrieved from ZEDManager.GetZedRootTransform(). 
    /// Used to find the offset between the HMD and tracked transform to compensate for drift. 
    /// </summary>
    private Transform zedRigRoot;

    /// <summary>
    /// Sets up the timed pose dictionary and identifies the VR SDK being used. 
    /// </summary>
    void Awake()
    {
        poseData.Clear(); //Reset the dictionary.
        poseData.Add(1, new List<TimedPoseData>()); //Create the list within the dictionary with its key and value.
        //Looking for the loaded device
        loadeddevice = UnityEngine.VR.VRSettings.loadedDeviceName;

        zedRigRoot = ZEDManager.Instance.GetZedRootTansform();
    }

    /// <summary>
    /// Update is called every frame.
    /// For SteamVR plugin this is where the device Index is set up.
    /// For Oculus plugin this is where the tracking is done.
    /// </summary>
    void Update()
    {

#if ZED_OCULUS //Used only if the Oculus Integration plugin is detected. 
        //Check if the VR headset is connected.
        if (OVRManager.isHmdPresent && loadeddevice == "Oculus")
        {
            if (OVRInput.GetConnectedControllers().ToString() == "Touch")
            {
                //Depending on which tracked device we are looking for, start tracking it.
                if (deviceToTrack == Devices.LeftController) //Track the Left Oculus Controller.
                    RegisterPosition(1, OVRInput.GetLocalControllerPosition(OVRInput.Controller.LTouch), OVRInput.GetLocalControllerRotation(OVRInput.Controller.LTouch));
                if (deviceToTrack == Devices.RightController) //Track the Right Oculus Controller.
                    RegisterPosition(1, OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch), OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch));

                if (deviceToTrack == Devices.Hmd) //Track the Oculus Hmd.
                    RegisterPosition(1, UnityEngine.VR.InputTracking.GetLocalPosition(UnityEngine.VR.VRNode.CenterEye), UnityEngine.VR.InputTracking.GetLocalRotation(UnityEngine.VR.VRNode.CenterEye));

                //Use our saved positions to apply a delay before assigning it to this object's Transform.
                if (poseData.Count > 0)
                {
                    sl.Pose p;

                    //Delay the saved values inside GetValuePosition() by a factor of latencyCompensation in milliseconds.
                    p = GetValuePosition(1, (float)(latencyCompensation / 1000.0f));
                    transform.position = p.translation; //Assign new delayed Position
                    transform.rotation = p.rotation; //Assign new delayed Rotation.
                }
            }
        }
        //Enable updating the internal state of OVRInput.
        OVRInput.Update();
#endif
#if ZED_STEAM_VR

        timerSteamVR += Time.deltaTime; //Increment timer for checking on devices

        if (timerSteamVR <= timerMaxSteamVR)
            return;

        timerSteamVR = 0f;

        //Checks if a device has been assigned
        if (index == EIndex.None && loadeddevice == "OpenVR")
        {
            if (BIsManufacturerController("HTC") || BIsManufacturerController("Oculus"))
            {
                //We look for any device that has "tracker" in its 3D model mesh name.
                //We're doing this since the device ID changes based on how many devices are connected to SteamVR.
                //This way, if there's no controllers or just one, it's going to get the right ID for the Tracker.
                if (deviceToTrack == Devices.ViveTracker)
                {
                    var error = ETrackedPropertyError.TrackedProp_Success;
                    for (uint i = 0; i < 16; i++)
                    {
                        var result = new System.Text.StringBuilder((int)64);
                        OpenVR.System.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_RenderModelName_String, result, 64, ref error);
                        if (result.ToString().Contains("tracker"))
                        {
                            index = (EIndex)i;
                            break; //We break out of the loop, but we can use this to set up multiple Vive Trackers if we want to.
                        }
                    }
                }

                //Looks for a device with the role of a Right Hand.
                if (deviceToTrack == Devices.RightController)
                {
                    index = (EIndex)OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
                }
                //Looks for a device with the role of a Left Hand.
                if (deviceToTrack == Devices.LeftController)
                {
                    index = (EIndex)OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
                }

                //Assigns the HMD.
                if (deviceToTrack == Devices.Hmd)
                {
                    index = EIndex.Hmd;
                }
            }

            //Display a warning if there was supposed to be a calibration file, and none was found.
            if (SNHolder.Equals("NONE"))
            {
                Debug.LogWarning(ZEDLogMessage.Error2Str(ZEDLogMessage.ERROR.PAD_CAMERA_CALIBRATION_NOT_FOUND));
            }
            else if (SNHolder != null && index != EIndex.None) //
            {
                //If the Serial number of the Calibrated device isn't the same as the current tracked device by this script...
                if (!SteamVR.instance.GetStringProperty(Valve.VR.ETrackedDeviceProperty.Prop_SerialNumber_String, (uint)index).Contains(SNHolder))
                {
                    Debug.LogWarning(ZEDLogMessage.Error2Str(ZEDLogMessage.ERROR.PAD_CAMERA_CALIBRATION_MISMATCH) + " Serial Number: " + SNHolder);
                    //... then look for that device through all the connected devices.
                    for (int i = 0; i < 16; i++)
                    {
                        //If a device with the same Serial Number is found, then change the device to track of this script.
                        if(SteamVR.instance.GetStringProperty(Valve.VR.ETrackedDeviceProperty.Prop_SerialNumber_String, (uint)i).Contains(SNHolder))
                        {
                            index = (EIndex)i;
                            string deviceRole = OpenVR.System.GetControllerRoleForTrackedDeviceIndex((uint)index).ToString();
                            if (deviceRole.Equals("RightHand"))
                                deviceToTrack = Devices.RightController;
                            else if (deviceRole.Equals("LeftHand"))
                                deviceToTrack = Devices.LeftController;
                            else if (deviceRole.Equals("Invalid"))
                            {
                                var error = ETrackedPropertyError.TrackedProp_Success;
                                var result = new System.Text.StringBuilder((int)64);
                                OpenVR.System.GetStringTrackedDeviceProperty((uint)index, ETrackedDeviceProperty.Prop_RenderModelName_String, result, 64, ref error);
                                if (result.ToString().Contains("tracker"))
                                    deviceToTrack = Devices.ViveTracker;
                            }
                            Debug.Log("A connected device with the correct Serial Number was found, and assigned to " + this + " the correct device to track.");
                            break;
                        }
                    }
                }
            }
            oldDevice = deviceToTrack;
        }

        if (deviceToTrack != oldDevice)
            index = EIndex.None;

#endif
    }

#if ZED_STEAM_VR
    /// <summary>
    /// Whether a given set of poses is currently valid - contains at least one pose and attached to an actual device. 
    /// </summary>
    public bool isValid { get; private set; }

    /// <summary>
    /// Track the devices for SteamVR and applying a delay.
    /// <summary>
    private void OnNewPoses(TrackedDevicePose_t[] poses)
    {
        if (index == EIndex.None)
            return;

        var i = (int)index;

        isValid = false;

        if (poses.Length <= i)
            return;

        if (!poses[i].bDeviceIsConnected)
            return;

        if (!poses[i].bPoseIsValid)
            return;

        isValid = true;

        //Get the position and rotation of our tracked device.
        var pose = new SteamVR_Utils.RigidTransform(poses[i].mDeviceToAbsoluteTracking);
        //Saving those values.
        RegisterPosition(1, pose.pos, pose.rot);

        //Delay the saved values inside GetValuePosition() by a factor of latencyCompensation in milliseconds.
        sl.Pose p = GetValuePosition(1, (float)(latencyCompensation / 1000.0f));
        transform.localPosition = p.translation;
        transform.localRotation = p.rotation;
        
    }

    /// <summary>
    /// Reference to the Action that's called when new controller/tracker poses are available. 
    /// </summary>
    SteamVR_Events.Action newPosesAction;

    /// <summary>
    /// Constructor that makes sure newPosesAction gets assigned when this class is created. 
    /// </summary>
    ZEDControllerTracker()
    {
        newPosesAction = SteamVR_Events.NewPosesAction(OnNewPoses);
    }

    void OnEnable()
    {
        var render = SteamVR_Render.instance;
        if (render == null)
        {
            enabled = false;
            return;
        }

        newPosesAction.enabled = true;
    }

    void OnDisable()
    {
        newPosesAction.enabled = false;
        isValid = false;
    }

    /// <summary>
    /// Checks if the tracked controller is made by the provided company.
    /// Works by retrieving the manufacturer name from SteamVR and checking that it starts with the specified string. 
    /// </summary>
    /// <param name="name">Manufacturer's name. Example: "HTC"</param>
    /// <returns></returns>
    public bool BIsManufacturerController(string name)
    {
        System.Text.StringBuilder sbType = new System.Text.StringBuilder(1000);
        Valve.VR.ETrackedPropertyError err = Valve.VR.ETrackedPropertyError.TrackedProp_Success;
        SteamVR.instance.hmd.GetStringTrackedDeviceProperty((uint)0, Valve.VR.ETrackedDeviceProperty.Prop_ManufacturerName_String, sbType, 1000, ref err);
        return (err == Valve.VR.ETrackedPropertyError.TrackedProp_Success && sbType.ToString().StartsWith(name));
    }


#endif
    /// <summary>
    /// Compute the delayed position and rotation from the history stored in the poseData dictionary.
    /// </summary>
    /// <param name="keyindex"></param>
    /// <param name="timeDelay"></param>
    /// <returns></returns>
    private sl.Pose GetValuePosition(int keyindex, float timeDelay)
    {
        sl.Pose p = new sl.Pose();
        if (poseData.ContainsKey(keyindex))
        {
            //Get the saved position & rotation.
            p.translation = poseData[keyindex][poseData[keyindex].Count - 1].position;
            p.rotation = poseData[keyindex][poseData[keyindex].Count - 1].rotation;

            float idealTS = (Time.time - timeDelay);

            for (int i = 0; i < poseData[keyindex].Count; ++i)
            {
                if (poseData[keyindex][i].timestamp > idealTS)
                {
                    int currentIndex = i;
                    if (currentIndex > 0)
                    {
                        //Calculate the time between the pose and the delayed pose.
                        float timeBetween = poseData[keyindex][currentIndex].timestamp - poseData[keyindex][currentIndex - 1].timestamp;
                        float alpha = ((Time.time - poseData[keyindex][currentIndex - 1].timestamp) - timeDelay) / timeBetween;

                        //Lerp to the next position based on the time determined above.
                        Vector3 pos = Vector3.Lerp(poseData[keyindex][currentIndex - 1].position, poseData[keyindex][currentIndex].position, alpha);
                        Quaternion rot = Quaternion.Lerp(poseData[keyindex][currentIndex - 1].rotation, poseData[keyindex][currentIndex].rotation, alpha);

                        //Apply new values.
                        p = new sl.Pose();
                        p.translation = pos;
                        p.rotation = rot;

                        //Removes used elements from the dictionary.
                        poseData[keyindex].RemoveRange(0, currentIndex - 1);
                    }
                    return p;
                }
            }
        }
        return p;
    }

    /// <summary>
    /// Set the current tracking to a container (TimedPoseData) to be stored in poseData and retrieved/applied after the latency period.
    /// </summary>
    /// <param name="index">Key value in the dictionary.</param>
    /// <param name="position">Tracked object's position from the VR SDK.</param>
    /// <param name="rot">Tracked object's rotation from the VR SDK.</param>
    private void RegisterPosition(int keyindex, Vector3 position, Quaternion rot)
    {
        TimedPoseData currentPoseData = new TimedPoseData();
        currentPoseData.timestamp = Time.time;
        currentPoseData.rotation = rot;
        currentPoseData.position = position;

        //Compensate for positional drift by measuring the distance between HMD and ZED rig root (the head's center). 
        Vector3 zedhmdposoffset = zedRigRoot.position - InputTracking.GetLocalPosition(VRNode.Head);
        currentPoseData.position += zedhmdposoffset;

        poseData[keyindex].Add(currentPoseData);
    }

    /// <summary>
    /// Structure used to hold the pose of a controller at a given timestamp.
    /// This is stored in poseData with RegisterPosition() each time the VR SDK makes poses available.
    /// It's retrieved with GetValuePosition() in Update() each frame. 
    /// </summary>
    public struct TimedPoseData
    {
        /// <summary>
        /// Value from Time.time when the pose was collected. 
        /// </summary>
        public float timestamp;

        /// <summary>
        /// Rotation of the tracked object as provided by the VR SDK.
        /// </summary>
        public Quaternion rotation;

        /// <summary>
        /// Position of the tracked object as provided by the VR SDK. 
        /// </summary>
        public Vector3 position;
    }
}

#if UNITY_EDITOR
/// <summary>
/// Custom editor for ZEDControllerTracker. 
/// If no VR Unity plugin (Oculus Integration or SteamVR plugin) has been loaded by the ZED plugin but one is found, 
/// presents a button to create project defines that tell ZED scripts that this plugin is loaded.
/// These defines (ZED_STEAM_VR and ZED_OCULUS) are used to allow compiling parts of ZED scripts that depend on scripts in these VR plugins. 
/// Note that this detection will also be attempted any time an asset has been imported. See nested class AssetPostProcessZEDVR. 
/// </summary>
[CustomEditor(typeof(ZEDControllerTracker)), CanEditMultipleObjects]
public class ZEDVRDependencies : Editor
{
    [SerializeField]
    static string defineName;
    static string packageName;

    public override void OnInspectorGUI() //Called when the Inspector is visible. 
    {
        if (CheckPackageExists("SteamVR"))
        {
            defineName = "ZED_STEAM_VR";
            packageName = "SteamVR";
        }
        else if (CheckPackageExists("Oculus") || CheckPackageExists("OVR"))
        {
            defineName = "ZED_OCULUS";
            packageName = "Oculus";
        }

        if (EditorPrefs.GetBool(packageName)) //Has it been set? 
        {
            DrawDefaultInspector();
        }
        else //No package loaded, but one has been detected. Present a button to load it. 
        {
            GUILayout.Space(20);
            if (GUILayout.Button("Load " + packageName + " data"))
            {
                if (CheckPackageExists(packageName))
                {
                    ActivateDefine();
                }
            }
            if (packageName == "SteamVR")
                EditorGUILayout.HelpBox(ZEDLogMessage.Error2Str(ZEDLogMessage.ERROR.STEAMVR_NOT_INSTALLED), MessageType.Warning);
            else if (packageName == "Oculus")
                EditorGUILayout.HelpBox(ZEDLogMessage.Error2Str(ZEDLogMessage.ERROR.OVR_NOT_INSTALLED), MessageType.Warning);
        }
    }

    /// <summary>
    /// Finds if a folder in the project exists with the specified name. 
    /// Used to check if a plugin has been imported, as the relevant plugins are placed
    /// in a folder named after the package. Example: "Assets/Oculus". 
    /// </summary>
    /// <param name="name">Package name.</param>
    /// <returns></returns>
    public static bool CheckPackageExists(string name)
    {
        string[] packages = AssetDatabase.FindAssets(name);
        return packages.Length != 0 && AssetDatabase.IsValidFolder("Assets/" + name);
    }

    /// <summary>
    /// Activates a define tag in the project. Used to enable compiling sections of scripts with that tag enabled. 
    /// For instance, parts of this script under a #if ZED_STEAM_VR statement will be ignored by the compiler unless ZED_STEAM_VR is enabled. 
    /// </summary>
    public static void ActivateDefine()
    {
        EditorPrefs.SetBool(packageName, true);

        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
        if (defines.Length != 0)
        {
            if (defineName != null && !defines.Contains(defineName))
            {
                defines += ";" + defineName;
            }
        }
        else
        {
            if (defineName != null && !defines.Contains(defineName))
            {
                defines += defineName;
            }
        }
        PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);
    }

    /// <summary>
    /// Removes a define tag from the project. 
    /// Called whenever a package is checked for but not found. 
    /// Removing the define tags will prevent compilation of code marked with that tag, like #if ZED_OCULUS.
    /// </summary>
    public static void DeactivateDefine()
    {
        EditorPrefs.SetBool(packageName, false);
        
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
        if (defines.Length != 0)
        {
			if (defineName != null &&  defines.Contains(defineName))
            {
                defines = defines.Remove(defines.IndexOf(defineName), defineName.Length);

                if (defines.LastIndexOf(";") == defines.Length - 1 && defines.Length != 0)
                {
                    defines.Remove(defines.LastIndexOf(";"), 1);
                }
            }
        }
        PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);
    }

    /// <summary>
    /// Inherits from UnityEditor.AssetPostProcessor to run ZED plugin-specific code whenever an asset is imported. 
    /// This code checks for the Oculus and SteamVR Unity packages, to activate or deactivate project define tags accordingly. 
    /// </summary>
    public class AssetPostProcessZEDVR : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {

            if (ZEDVRDependencies.CheckPackageExists("OVR") || ZEDVRDependencies.CheckPackageExists("Oculus"))
            {
                defineName = "ZED_OCULUS";
                packageName = "Oculus";
                ActivateDefine();
            }
            if (ZEDVRDependencies.CheckPackageExists("SteamVR"))
            {
                defineName = "ZED_STEAM_VR";
                packageName = "SteamVR";
                ActivateDefine();
            }
            else
            {
                DeactivateDefine();
            }
        }
    }
}

#endif