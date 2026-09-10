using UnityEngine;

namespace Pid
{
    //Custom distance fog implementation to mimic the look and feel of the orignal from PID
    //Also controls how far you can see, for gameplay reasons
    [DisallowMultipleComponent]
    public class PidShade : MonoBehaviour
    {
        public const int Unlit = 3;
        public const int Flashlight = 5;
        public const int Goggles = 7;

        static readonly int ViewId = Shader.PropertyToID("_PidView14");

        public bool flashlightOn;
        public bool gogglesOn;

        int applied = -1;

        void OnEnable() { applied = -1; Apply(); }
        void Update() { Apply(); }
        void OnValidate() { applied = -1; Apply(); }

        void Apply()
        {
            int v = View14;
            if (v == applied) return;
            applied = v;
            Shader.SetGlobalFloat(ViewId, v);
        }

        public int View14 => gogglesOn ? Goggles : flashlightOn ? Flashlight : Unlit;

        public float DarknessRange => View14 * PidConst.SectorSize;

        //Palette gets rebuilt every time you change floor, so level is part of the key
        public static string LutName(int level, int resource, int variation)
            => $"lut_L{level:D2}_r{resource}_v{variation}";
    }
}