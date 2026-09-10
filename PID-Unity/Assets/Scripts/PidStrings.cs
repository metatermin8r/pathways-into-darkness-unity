using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Pid
{
    //All the text the game uses for UI
    //Every word the game shows you lives in a resource, because Macs are just that way and we're mimicing it
    //Hardcoded for now but will pull from actual game strings in final release
    public class PidStrings
    {
        readonly Dictionary<int, List<string>> tables = new Dictionary<int, List<string>>();

        public void Add(int id, params string[] entries) => tables[id] = new List<string>(entries);

        public string Get(int table, int index)
        {
            if (tables.TryGetValue(table, out var list) &&
                index >= 0 && index < list.Count) return list[index];
            return $"<{table}[{index}]>";
        }

        public int Count(int table) => tables.TryGetValue(table, out var l) ? l.Count : 0;

        //Placeholder text so the panels have something to draw
        public static PidStrings Defaults()
        {
            var s = new PidStrings();

            //Four headings, two versions of the progress text (depending on where you are in the pyramid)
            s.Add(2013,
                "Health",
                "Power",
                "Progress",
                "Weapon Proficiencies",
                "You are %d.%dm above ground.  You have scored %d of %d points and recovered $%d.%d%s in treasure.",
                "You are %d.%dm below ground.  You have scored %d of %d points and recovered $%d.%d%s in treasure.");

            //M16 is cosmetic, and the Colt also seems to be but also crashes the original game if you mod in ammo to use it? Both keep their
            //rows for parity with the original game regardless
            s.Add(2006,
                "Melee Combat", "Colt .45 Pistol", "Walther P4 Pistol",
                "MP-41 Submachine Gun", "M-16 Rifle", "AK-47 Assault Rifle",
                "M-79 Grenade Launcher", "");

            s.Add(2007, "Beginner", "Novice", "Expert");

            // These get tacked onto the end of an item name showing its state
            s.Add(2008, " (empty)", " (in hand)", " (ready)", " (on wrist)",
                        " (worn)", " (on)");

            //Days of the week
            s.Add(2009, "Sunday", "Monday", "Tuesday", "Wednesday",
                        "Thursday", "Friday", "Saturday");

            //UI text
            s.Add(2010, "REST", "SEARCH", "MAP");
            s.Add(2011, "EXAMINE", "DROP");
            s.Add(2016, "Total Weight: %3.2f kg.", "Weight: %3.2f kg.");
            return s;
        }
    }

    //The clock in the messages window.
    public static class PidClock
    {
        public const long Minute = 3600;
        public const long Hour = 216000;
        public const long Day = 5184000;
        public const long DisplayAddend = 1126800;

        public static string Format(long ticks, PidStrings str)
        {
            long t = ticks + DisplayAddend;

            //The +1 hour here is the day of the week's own, not the display one
            int day = (int)(((t + Hour) / Day) % 7);

            long intoDay = t % Day;
            int hour24 = (int)(intoDay / Hour);
            int minute = (int)((intoDay % Hour) / Minute);

            //Reads wrong but matches, because the hour shown is hour24 + 1
            bool pm = hour24 >= 11 && hour24 < 23;

            int hour12 = (hour24 % 12) + 1;
            int shown24 = hour24 == 23 ? 0 : hour24 + 1;

            return $"{str.Get(2009, day)}, {shown24:00}{minute:00} " +
                   $"({hour12}:{minute:00} {(pm ? "PM" : "AM")})";
        }
    }

    //Progress blurb in UI
    public static class PidProgress
    {
        public const int ScoreDenominator = 41;

        public static string Format(PidPlayer p, PidLevel level, PidStrings str)
        {
            int h = level != null ? level.height10 : 0;
            string fmt = str.Get(2013, h >= 0 ? 4 : 5);

            int habs = Mathf.Abs(h);
            int hWhole = habs / 10, hTenth = habs % 10;

            //Treasure switches to millions at 10000...because PID I guess. Thank you Jason Jones, very cool.
            bool mega = p.treasure >= 10000;
            int div = mega ? 10000 : 10;
            int tWhole = p.treasure / div, tTenth = (p.treasure % div) * 10 / div;

            return string.Format(fmt.Replace("%d.%dm", "{0}.{1}m")
                                    .Replace("%d of %d", "{2} of {3}")
                                    .Replace("$%d.%d%s", "${4}.{5}{6}"),
                                 hWhole, hTenth, p.score, ScoreDenominator,
                                 tWhole, tTenth, mega ? "M" : "K");
        }
    }

    //Health reads "6 of 6" on a round number and "5.9 of 6" otherwise
    public static class PidHealthText
    {
        public static string Format(int current, int max)
        {
            int c = current / 10, rem = current % 10, m = max / 10;
            return rem == 0 ? $"{c} of {m}" : $"{c}.{rem} of {m}";
        }
    }
}