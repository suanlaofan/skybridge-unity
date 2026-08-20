using UnityEngine;

namespace Skybridge.Presentation
{
    public enum PickKind
    {
        Piece,
        RoadTarget,
        FoundationTarget,
        TrussTarget,
        Joint,
    }

    public sealed class SkybridgePickable : MonoBehaviour
    {
        public PickKind Kind;
        public string Id = string.Empty;
        public int Index = -1;
        public string PieceType = string.Empty;
    }
}

