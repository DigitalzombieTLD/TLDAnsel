
// # This script _must_ be attached to the main camera which renders 3D scene
// # Default coordinate system is left handed. If your project is using different coordinate system please configuration data in Start method accordingly
// # Use property Ansel.IsAvailable to adjust UI and other items which allow user to interact with Ansel
// # Use property Ansel.IsSessionActive to adjust game logic (game should be paused and camera parameters (position, orientation, FOV, view/projection matrices etc) _must_ not be changed elsewhere in script)
// # Use property Ansel.IsCaptureActive to disable effects (e.g. motion blur) which can cause Ansel not to work correctly during capture
// # Use Ansel.ConfigureSession to enable/disable Ansel sessions and features (only when session is not active)
// # Key parameters are exposed as properties so they can be edited directly in the editor. Other parameters should be changed only in rare scenarios.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Assertions;
using System.Runtime.InteropServices;
using System.IO;
using System;
//using System.Numerics;
using System.Reflection;
using UnhollowerRuntimeLib;
using UnhollowerBaseLib.Attributes;
using AnselMod;

namespace NVIDIA
{
	public class Ansel : MonoBehaviour
	{
		public static HudDisplayMode hudmode = HudDisplayMode.Normal;
		public static float tscale;
		public static Vector3 m_OriginalPos;
		public static Quaternion m_OriginalRot;
		public static PlayerControlMode controlMode;

		public static bool hudEnabled;
		public static bool vignetteEnabled;
		public static bool dofEnabled;
		public static CursorLockMode cursorLock;
		public static PanViewCamera panViewCamera;
		public static Camera fpsCam;


		//[HideFromIl2Cpp]
		public Ansel(IntPtr obj0) : base(obj0)
		{
		}

		//[HideFromIl2Cpp]
		public Ansel() : base(ClassInjector.DerivedConstructorPointer<Ansel>()) => ClassInjector.DerivedConstructorBody(this);

		[StructLayout(LayoutKind.Sequential)]
		public struct ConfigData
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
			public float[] forward;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
			public float[] up;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
			public float[] right;

			// The speed at which camera moves in the world
			public float translationalSpeedInWorldUnitsPerSecond;
			// The speed at which camera rotates 
			public float rotationalSpeedInDegreesPerSecond;
			// How many frames it takes for camera update to be reflected in a rendered frame
			public uint captureLatency;
			// How many frames we must wait for a new frame to settle - i.e. temporal AA and similar
			// effects to stabilize after the camera has been adjusted
			public uint captureSettleLatency;
			// Game scale, the size of a world unit measured in meters
			public float metersInWorldUnit;
			// Integration will support Camera::screenOriginXOffset/screenOriginYOffset
			[MarshalAs(UnmanagedType.I1)]
			public bool isCameraOffcenteredProjectionSupported;
			// Integration will support Camera::position
			[MarshalAs(UnmanagedType.I1)]
			public bool isCameraTranslationSupported;
			// Integration will support Camera::rotation
			[MarshalAs(UnmanagedType.I1)]
			public bool isCameraRotationSupported;
			// Integration will support Camera::horizontalFov
			[MarshalAs(UnmanagedType.I1)]
			public bool isCameraFovSupported;
		};

		[StructLayout(LayoutKind.Sequential)]
		public struct CameraData
		{
			public float fov; // degrees
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
			public float[] projectionOffset;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
			public float[] position;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public float[] rotation;
		};

		[StructLayout(LayoutKind.Sequential)]
		public struct SessionData
		{
			[MarshalAs(UnmanagedType.I1)]
			public bool isAnselAllowed; // if set to false none of the below parameters is relevant
			[MarshalAs(UnmanagedType.I1)]
			public bool is360MonoAllowed;
			[MarshalAs(UnmanagedType.I1)]
			public bool is360StereoAllowed;
			[MarshalAs(UnmanagedType.I1)]
			public bool isFovChangeAllowed;
			[MarshalAs(UnmanagedType.I1)]
			public bool isHighresAllowed;
			[MarshalAs(UnmanagedType.I1)]
			public bool isPauseAllowed;
			[MarshalAs(UnmanagedType.I1)]
			public bool isRotationAllowed;
			[MarshalAs(UnmanagedType.I1)]
			public bool isTranslationAllowed;
		};

		// Buffer hints for Ansel
		public enum HintBufferType
		{
			kBufferTypeHDR = 0,
			kBufferTypeDepth,
			kBufferTypeHUDless,
			kBufferTypeCount
		};

		// User control status
		public enum UserControlStatus
		{
			kUserControlOk = 0,
			kUserControlIdAlreadyExists,
			kUserControlInvalidId,
			kUserControlInvalidType,
			kUserControlInvalidLabel,
			kUserControlNameTooLong,
			kUserControlInvalidValue,
			kUserControlInvalidLocale,
			kUserControlInvalidCallback
		};

		// The speed at which camera moves in the world
		//[SerializeField]
		public float TranslationalSpeedInWorldUnitsPerSecond = 8.0f;
		// The speed at which camera rotates 
		//[SerializeField]
		public float RotationalSpeedInDegreesPerSecond = 60.0f;
		// How many frames it takes for camera update to be reflected in a rendered frame
		//[SerializeField]
		public uint CaptureLatency = 0;
		// How many frames we must wait for a new frame to settle - i.e. temporal AA and similar
		// effects to stabilize after the camera has been adjusted
		//[SerializeField]
		public uint CaptureSettleLatency = 10;
		// Game scale, the size of a world unit measured in meters
		//[SerializeField]
		public float MetersInWorldUnit = 1.0f;

		public static bool IsSessionActive
		{
			get
			{
				return sessionActive;
			}
		}

		public static bool IsCaptureActive
		{
			get
			{
				return captureActive;
			}
		}

		public static bool IsAvailable
		{
			get
			{
				return anselIsAvailable();
			}
		}

		// --------------------------------------------------------------------------------
		private void Awake()
		{
			hintBufferPreBindCBs = new CommandBuffer[(int)HintBufferType.kBufferTypeCount];
			hintBufferPostBindCBs = new CommandBuffer[(int)HintBufferType.kBufferTypeCount];

			for (int i = 0; i < (int)HintBufferType.kBufferTypeCount; i++)
			{
				hintBufferPreBindCBs[i] = new CommandBuffer();
				hintBufferPreBindCBs[i].IssuePluginEvent(GetMarkBufferPreBindRenderEventFunc(), i);
				hintBufferPostBindCBs[i] = new CommandBuffer();
				hintBufferPostBindCBs[i].IssuePluginEvent(GetMarkBufferPostBindRenderEventFunc(), i);
			}
		}

		public void Start()
		{
			Initialize();
		}

		// --------------------------------------------------------------------------------

		private void Initialize()
		{
			if (initialized)
			{
				return;
			}

			if (!Application.isFocused)
			{
				return;
			}

			if (!IsAvailable)
			{
				MelonLoader.MelonLogger.Log("Ansel is not available or enabled on this platform. Did you forget to whitelist your executable?");
				return;
			}


			// Get our camera (this script _must_ be attached to the main camera which renders the 3D scene)
			mainCam = GetComponent<UnityEngine.Camera>();
			

			// Hint example, call this right before setting HDR render target active. This will notify Ansel that HDR buffer is about to bind.
			// Use that if Ansel is incorrectly determining HDR buffer to be used. (by default it is not the case). 
			// This is only the example, consider trying different CameraEvents in the rendering pipeline.
			/*
			mainCam.AddCommandBuffer(CameraEvent.BeforeImageEffects, hintBufferPreBindCBs[(int)HintBufferType.kBufferTypeHDR]);
			*/

			anselConfig = new ConfigData();

			// Default coordinate system is left handed.
			// If your project is using different coordinate system please adjust accordingly
			anselConfig.right = new float[3] { 1, 0, 0 };
			anselConfig.up = new float[3] { 0, 1, 0 };
			anselConfig.forward = new float[3] { 0, 0, 1 };
			// Can be set by user from the editor
			anselConfig.translationalSpeedInWorldUnitsPerSecond = TranslationalSpeedInWorldUnitsPerSecond;
			anselConfig.rotationalSpeedInDegreesPerSecond = RotationalSpeedInDegreesPerSecond;
			anselConfig.captureLatency = CaptureLatency;
			anselConfig.captureSettleLatency = CaptureSettleLatency;
			anselConfig.metersInWorldUnit = MetersInWorldUnit;
			// These should always be true unless there is some special scenario
			anselConfig.isCameraOffcenteredProjectionSupported = true;
			anselConfig.isCameraRotationSupported = true;
			anselConfig.isCameraTranslationSupported = true;
			anselConfig.isCameraFovSupported = true;

			anselInit(ref anselConfig);

			// Ansel will return camera parameters here
			anselCam = new CameraData();

			// Default session configuration which allows everything.
			// Game can reconfigure session anytime session is not active by calling ConfigureSession.
			SessionData ses = new SessionData();
			ses.isAnselAllowed = true; // if false none of the below parameters is relevant
			ses.isFovChangeAllowed = true;
			ses.isHighresAllowed = true;
			ses.isPauseAllowed = true;
			ses.isRotationAllowed = true;
			ses.isTranslationAllowed = true;
			ses.is360StereoAllowed = true;
			ses.is360MonoAllowed = true;

			anselConfigureSession(ref ses);

			// Custom Control example
			// Add your own slider or boolean toggle into the Ansel UI. You can then poll values at any time, but
			// it makes sense to do it only when anselIsSessionOn() is true. It is a good way to add the control 
			// over game specific settings like for instance day/night switch or bloom intensity.
			/*
			{
			anselAddUserControlSlider(1, "My Custom Slider", 0.2f));
			float myValue = anselGetUserControlSliderValue(2)
			}
			*/

			if (!IsAvailable)
			{
				MelonLoader.MelonLogger.Log("Ansel failed to configure session. Please check Ansel log for more details.");
			}
			else
			{
				MelonLoader.MelonLogger.Log("Ansel is initialized and ready to use");
			}

			initialized = true;
		}

		// --------------------------------------------------------------------------------
		private void OnApplicationFocus()
		{
			if (!initialized)
			{
				Initialize();
			}
		}

		// --------------------------------------------------------------------------------
		[HideFromIl2Cpp]
		public void UpdateCoordinateSystem(Vector3 right, Vector3 up, Vector3 forward)
		{
			if (anselIsSessionOn())
			{
				MelonLoader.MelonLogger.Log("Ansel coordinate system cannot be configured while session is active");
				return;
			}

			anselConfig.right = new float[3] { right.x, right.y, right.z };
			anselConfig.up = new float[3] { up.x, up.y, up.z };
			anselConfig.forward = new float[3] { forward.x, forward.y, forward.z };
			anselUpdateConfiguration(ref anselConfig);
		}

		// --------------------------------------------------------------------------------
		[HideFromIl2Cpp]
		public void ConfigureSession(SessionData ses)
		{
			if (!IsAvailable)
			{
				MelonLoader.MelonLogger.Log("Ansel is not available or enabled on this platform. Did you forget to whitelist your executable?");
				return;
			}

			if (anselIsSessionOn())
			{
				MelonLoader.MelonLogger.Log("Ansel session cannot be configured while session is active");
				return;
			}

			SessionData foo = new NVIDIA.Ansel.SessionData();
			foo.isAnselAllowed = true; // if false none of the below parameters is relevant
			foo.isFovChangeAllowed = true;
			foo.isHighresAllowed = true;
			foo.isPauseAllowed = true;
			foo.isRotationAllowed = true;
			foo.isTranslationAllowed = true;
			foo.is360StereoAllowed = true;
			foo.is360MonoAllowed = true;
			anselConfigureSession(ref foo);
		}

		// --------------------------------------------------------------------------------
		public void OnPreRender()
		{
			if (anselIsSessionOn())
			{
				// Ansel session is active (user pressed Alt+F2)
				if (!sessionActive)
				{
					sessionActive = true;
					// On first update after session is activated we need to store
					// camera and other parameters so they can be restored later on
					SaveState();
				}

				Animator anim = mainCam.GetComponent<Animator>();
				if (anim)
				{
					anim.enabled = false;
				}

				// Check if capture is active
				captureActive = anselIsCaptureOn();


				// Camera
				// Transform trans = mainCam.transform;
				// anselCam.fov = mainCam.fieldOfView;

				Transform trans = mainCam.transform;
				anselCam.fov = mainCam.fieldOfView;







				// Matrix
				anselCam.projectionOffset = new float[2] { 0.0f, 0 };
				anselCam.position = new float[3] { trans.position.x, trans.position.y, trans.position.z };
				anselCam.rotation = new float[4] { trans.rotation.x, trans.rotation.y, trans.rotation.z, trans.rotation.w };

				// Camera Update
				anselUpdateCamera(ref anselCam);

				// Reset projection matrix so that potential FOV changes below can take effect
				mainCam.ResetProjectionMatrix();

				mainCam.transform.position = new Vector3(anselCam.position[0], anselCam.position[1], anselCam.position[2]);
				mainCam.transform.rotation = new Quaternion(anselCam.rotation[0], anselCam.rotation[1], anselCam.rotation[2], anselCam.rotation[3]);

				// Camera FOV
				mainCam.fieldOfView = anselCam.fov;

				if (anselCam.projectionOffset[0] != 0 || anselCam.projectionOffset[1] != 0)
				{
					// Hi-res screen shots require projection matrix adjustment
					projectionMatrix = mainCam.projectionMatrix;

					float l = -1.0f + anselCam.projectionOffset[0];
					float r = l + 2.0f;
					float b = -1.0f + anselCam.projectionOffset[1];
					float t = b + 2.0f;

					projectionMatrix[0, 2] = (l + r) / (r - l);
					projectionMatrix[1, 2] = (t + b) / (t - b);
					mainCam.projectionMatrix = projectionMatrix;
				}
			}
			else
			{
				// Ansel session is no longer active
				if (sessionActive)
				{
					sessionActive = false;
					captureActive = false;
					RestoreState();
					MelonLoader.MelonLogger.Log("Stopped Ansel session");
					Animator anim = mainCam.GetComponent<Animator>();

					if (anim)
					{
						anim.enabled = true;
					}
				}
			}
		}

		// --------------------------------------------------------------------------------
		private void SaveState()
		{
			// Camera
			Transform trans = mainCam.transform;
			trans.position = GameManager.GetVpFPSCamera().transform.position;
			
			cameraPos = trans.position;
			cameraRotation = trans.rotation;

			cameraFOV = mainCam.fieldOfView;

			GameManager.GetVpFPSCamera().m_PanViewCamera.m_IsDetachedFromPlayer = true;


			// Cursor
			InputManager.ShowCursor(false);

			// HUD
			hudmode = HUDManager.m_HudDisplayMode;

			bool hudEnabled = InterfaceManager.m_Panel_HUD.enabled;
			InterfaceManager.m_Panel_HUD.Enable(false);
			HUDManager.m_HudDisplayMode = HudDisplayMode.Off;

			// Timescale
			tscale = Time.timeScale;
			Time.timeScale = 0.0f;
			GameManager.m_IsPaused = true;

			// Effects
			dofEnabled = GameManager.GetCameraEffects().m_DepthOfField;
			GameManager.GetCameraEffects().DepthOfFieldTurnOff(true);

			vignetteEnabled = GameManager.GetCameraEffects().m_Vignette;
			GameManager.GetCameraEffects().VignettingEnable(false);

			GameManager.GetCameraEffects().BlurTurnOff();


			// Disable user input
			Input.ResetInputAxes();
		}

		// --------------------------------------------------------------------------------
		private void RestoreState()
		{
			// Matrix
			mainCam.ResetProjectionMatrix();

			// Camera
			mainCam.transform.position = cameraPos;
			mainCam.transform.rotation = cameraRotation;
			mainCam.fieldOfView = cameraFOV;
			GameManager.GetVpFPSCamera().m_PanViewCamera.m_IsDetachedFromPlayer = false;

			// Cursor
			InputManager.ShowCursor(true);

			// HUD
			InterfaceManager.m_Panel_HUD.Enable(hudEnabled);
			HUDManager.m_HudDisplayMode = hudmode;

			// Timescale
			Time.timeScale = tscale;
			GameManager.m_IsPaused = false;

			// Effects
			//GameManager.GetCameraEffects().DepthOfFieldTurnOff(dofEnabled);
			
			//GameManager.GetCameraEffects().VignettingEnable(vignetteEnabled);
		}

		#if (UNITY_64 || UNITY_EDITOR_64 || PLATFORM_ARCH_64)
				const string PLUGIN_DLL = "AnselPlugin64";
		#else
				const string PLUGIN_DLL = "AnselPlugin64";
		#endif

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern void anselInit(ref ConfigData conf);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern void anselUpdateConfiguration(ref ConfigData conf);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern void anselUpdateCamera(ref CameraData cam);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern void anselConfigureSession(ref SessionData ses);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern bool anselIsSessionOn();

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern bool anselIsCaptureOn();

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern bool anselIsAvailable();

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern void anselStartSession();

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern void anselStopSession();

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern System.IntPtr GetMarkBufferPreBindRenderEventFunc();

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern System.IntPtr GetMarkBufferPostBindRenderEventFunc();

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern UserControlStatus anselAddUserControlSlider(uint userControlId, string labelUtf8, float value);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern UserControlStatus anselSetUserControlSliderValue(uint userControlId, float value);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern float anselGetUserControlSliderValue(uint userControlId);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern UserControlStatus anselAddUserControlBoolean(uint userControlId, string labelUtf8, bool value);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern UserControlStatus anselSetUserControlBooleanValue(uint userControlId, bool value);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern bool anselGetUserControlBooleanValue(uint userControlId);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern UserControlStatus anselSetUserControlLabelLocalization(uint userControlId, string lang, string labelUtf8);

		[DllImport(PLUGIN_DLL, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
		private static extern UserControlStatus anselRemoveUserControl(uint userControlId);

		private static bool sessionActive = false;
		private static bool captureActive = false;

		private bool initialized = false;
		private bool cursorVisible = false;
		private Vector3 cameraPos;
		private Quaternion cameraRotation;
		private float cameraFOV;

		private ConfigData anselConfig;
		private CameraData anselCam;
		private Matrix4x4 projectionMatrix;
		private Camera mainCam;
		private CommandBuffer[] hintBufferPreBindCBs;
		private CommandBuffer[] hintBufferPostBindCBs;
	};
}