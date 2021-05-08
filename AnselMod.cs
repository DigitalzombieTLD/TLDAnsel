using System;
using MelonLoader;
using Harmony;
using UnityEngine;
using System.Reflection;
using System.Globalization;
using UnhollowerRuntimeLib;
using NVIDIA;

namespace AnselMod
{
	public class AnselModMain : MelonMod
	{
		int levelload = 0;
		public static float anselTimer = 0;
		public static float l = 0f;
		public static float r = 0f;
		public static float magicFOV;
		public static float fovOffset = 0;

		public override void OnApplicationStart()
		{
			ClassInjector.RegisterTypeInIl2Cpp<Ansel>();
		}

		public override void OnLevelWasInitialized(int level)
		{
			levelload = level;
			AnselModActionMain.anselattached = false;
			anselTimer = 0;
		}

		public override void OnUpdate()
		{
			AnselModActionMain.AnselModActionUpdate();

			if (InputManager.GetKeyDown(InputManager.m_CurrentContext, KeyCode.Keypad1))
			{
				MelonLogger.Log("Offset: " + fovOffset);
				fovOffset++;
			}

			if (InputManager.GetKeyDown(InputManager.m_CurrentContext, KeyCode.Keypad3))
			{
				MelonLogger.Log("Offset: " + fovOffset);
				fovOffset--;
			}
		}

		[HarmonyPatch(typeof(vp_FPSCamera), "UpdateCameraRotation")]
        public class CAMPATCH
        {
            public static void Prefix()
            {
                //MelonLogger.Log("Override called!!!");
            }
        }
	}
}