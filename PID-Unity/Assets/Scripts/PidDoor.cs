// PidDoor.cs
// A single door panel. Built at runtime by PidWorld; not placed by hand.
//
// GEOMETRY, derived from the map rather than assumed:
//
//   A door sector sits in a one-tile gap. Two of its four neighbours are Void and two
//   are walkable. The TRAVEL AXIS is the one joining the two walkable neighbours - on
//   Ground Floor, east-west for the doors at (11,6) and (21,6), north-south for (16,20).
//
//   The panel must block travel, so it lies in the plane PERPENDICULAR to the travel
//   axis, spanning the full 3 m width of the tile and the full 2.6 m height.
//
//   door_list.direction does NOT name that plane. It names the edge the panel RETRACTS
//   INTO - always one of the two Void sides, always perpendicular to travel. That is a
//   pocket door: the panel slides sideways into the jamb. The two void-facing edges of
//   a door tile carry the 128/13 jamb walls; the edges you walk through carry
//   wall_type 0 and no geometry at all, which is why the panel has to be synthesized.
//
//   Slide distance is one sector, so the open panel sits entirely inside the jamb.
//
// UNVERIFIED: that PID's doors slide rather than swing, dilate or drop. Sliding is the
// reading the data supports - a direction pointing at a solid neighbour is a pocket -
// but nobody has watched one open. Check in Infinite Mac and correct here.

using UnityEngine;

namespace Pid
{
    public class PidDoor : MonoBehaviour
    {
        public int sectorX, sectorY;
        public int doorIndex;
        public int textureId;
        public bool travelIsNorthSouth;   // false = east-west

        [Tooltip("Seconds for a full open or close.")]
        public float slideTime = 0.6f;

        Vector3 closedPos, openPos;
        float t;            // 0 = closed, 1 = open
        bool wantOpen;

        public bool IsOpen => t >= 1f;

        public void Configure(Vector3 closed, Vector3 slideOffset)
        {
            closedPos = closed;
            openPos = closed + slideOffset;
            transform.position = closedPos;
        }

        public void SetWanted(bool open) => wantOpen = open;

        void Update()
        {
            float target = wantOpen ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;

            t = Mathf.MoveTowards(t, target, Time.deltaTime / Mathf.Max(0.01f, slideTime));
            transform.position = Vector3.Lerp(closedPos, openPos, t);
        }
    }
}