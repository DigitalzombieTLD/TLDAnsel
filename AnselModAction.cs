using System.IO;
using System.Reflection;
using UnityEngine;
using MelonLoader;
using NVIDIA;

namespace AnselMod
{
    public class AnselModActionMain : MonoBehaviour
    {        
        public static bool anselattached = false;
        public static GameObject gotTheCam;
		public static Camera ownCam;

        public static void AnselModActionUpdate()
        {
            if (!anselattached && GameManager.GetVpFPSCamera() != null)
            {
				GameManager.GetVpFPSCamera().gameObject.AddComponent<Ansel>();
				
				anselattached = true;
            }
        }
    }
}