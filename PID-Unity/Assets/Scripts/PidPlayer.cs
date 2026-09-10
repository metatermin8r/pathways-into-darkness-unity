using System.Collections.Generic;
using UnityEngine;

namespace Pid
{
    //Player state, stored the way PID stored it. HP is 60, not 6.0, for example, despite PID doing some math and displaying it as such
    //Keeping it that way since every formula for damage, healing, weight, ect. works in these units, and so do PID save files
    public class PidPlayer
    {
        //**********Player location**********
        //World units stored as x/y
        public int xRaw, yRaw;

        //0 west, 128 north, 256 east, 384 south
        public int facing;

        //0 is Ground Floor, 1 is the first floor, ect.
        public int dungeon;

        //**********Health**********
        //Actual health value is always ten times what the UI shows
        public int hp = 60;
        public int maxHp = 60; //Grows with your score

        //**********Score**********
        //Shown as "X of 41"
        //Interestingly, the 41 is hardcoded and the items add up to 44, so I guess we keep that for authenticity even though it seems wrong
        public int score;

        //**********Treasure**********
        //treasure
        public int treasure;

        //**********Clock**********
        //Ticks since the game started, 60 a second, running at real time according to the system clock
        //PidClock turns this into the date and time (i.e. Sunday 6:13 AM) the game actually displays in the UI
        public long ticks;

        //**********Crystal**********
        //Crystal charge, and which crystal it belongs to
        //Bar shows charge against that crystal's own capacity
        public int crystalCharge;
        public int crystalSlot = 0xFFFF;

        //**********Equipment**********
        public bool goggles;
        public bool watch;
        public bool redCloak;
        public bool flashlight;
        public bool rubyRing;
        public bool amethystRing;

        //True if carrying the alien gemstone without it being it the lead box, drains your health and stops you resting
        public bool gemstone;

        //**********Weapons**********
        //Which weapon is in your hand. $FFFF for none.
        public int readiedSlot = 0xFFFF;

        public int rofTimer; //Counts down between shots
        public int fireState; //0 idle, 1 firing, 2 just reloaded

        //XP Rank 0 means the weapon isn't displayed on the panel at all, but it appears as Beginner
        //the first time you land a hit with taht weapon
        public readonly int[] profRank = new int[8];
        public readonly long[] profXp = new long[8];

        //Shots fired and landed, for the accuracy readout
        public readonly int[] shots = new int[5];
        public readonly int[] hits = new int[5];

        //**********Inventory**********
        //Items can hold other items, so this needs to be handled like a tree
        public readonly PidInvRecord[] inventory = new PidInvRecord[256];
        public int inventoryHead = 0xFFFF;

        public PidPlayer()
        {
            for (int i = 0; i < inventory.Length; i++)
                inventory[i] = new PidInvRecord();
        }

        //Goggles win over the flashlight, code handling this is in PidShade
        public int View14 => goggles ? 7 : flashlight ? 5 : 3;

        //This is a cosmetic value kept to make sure everything displays and functions accurately to the original
        //Actual PID stores weigth for every item, and a total inventory weight, but it does nothing in actual gameplay
        public int WeightRaw() => 0;
    }

    //One item.
    public class PidInvRecord
    {
        //$FFFF means this slot is empty
        public int id = 0xFFFF;

        //Bit 0 is equipped, bit 1 is open, the other 14 go unused
        public int state;

        //This means different things per item (?): first thing inside a container, rounds left in a magazine, charge a crystal needs, flashlight battery, ect.
        public int word2;

        //Next item in the inventory that is alongside the current one, with $FFFF being the end
        public int word3 = 0xFFFF;

        public bool Equipped => (state & 1) != 0;

        //One flag covers "open" and "expanded on the panel"
        public bool Open => (state & 2) != 0;
    }
}