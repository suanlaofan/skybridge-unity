using UnityEngine;

namespace Skybridge.Presentation
{
    public static class SkybridgePalette
    {
        public static readonly Color Environment = Hex("ECEDE8");
        public static readonly Color EnvironmentShade = Hex("CFD4D2");
        public static readonly Color Road = Hex("FFC400");
        public static readonly Color RoadEdge = Hex("E4AA00");
        public static readonly Color Truss = Hex("00C8D7");
        public static readonly Color Accent = Hex("FF4B3A");
        public static readonly Color Mechanical = Hex("17191C");
        public static readonly Color Warning = Hex("FF8A00");
        public static readonly Color Void = Hex("090B0D");
        public static readonly Color Rock = Hex("4B5153");
        public static readonly Color Valid = Hex("2DE2E6");
        public static readonly Color Invalid = Hex("FF4B3A");
        // Match the Three.js scene background and fog color exactly.
        public static readonly Color SceneBackground = Hex("DFE5E4");
        public static readonly Color Surface = new(0.953f, 0.961f, 0.949f, 0.95f);

        public static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString($"#{value}", out var color) ? color : Color.magenta;
        }
    }
}
