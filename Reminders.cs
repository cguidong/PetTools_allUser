using System;

namespace OffWorkWidget
{
    // Count time only while the app is running inside a configured work session,
    // excluding the lunch break; counters restart at zero after lunch.
    public class ReminderSchedule
    {
        DateTime lastWall;
        TimeSpan lastElapsed;
        bool hasSample, wasActive;
        double waterSeconds, walkSeconds;

        public void Reset()
        {
            hasSample = false; wasActive = false; waterSeconds = walkSeconds = 0;
        }
        public int Tick(DateTime now, TimeSpan elapsed, Settings settings)
        {
            var hours = Logic.WorkHours(now, settings);
            bool workday = Logic.WorkingDay(now, settings);
            bool lunch = workday && Logic.LunchCovered(now, settings) && Logic.IsLunchTime(now, settings);
            bool active = workday && !lunch && now.TimeOfDay >= hours.Start && now.TimeOfDay < hours.End;
            double delta = hasSample ? (elapsed - lastElapsed).TotalSeconds : 0;
            double wallDelta = hasSample ? (now - lastWall).TotalSeconds : 0;
            bool continuous = hasSample && active && wasActive && now.Date == lastWall.Date &&
                delta >= 0 && delta <= 15 && wallDelta >= 0 && wallDelta <= 15;
            lastElapsed = elapsed; lastWall = now; hasSample = true; wasActive = active;
            if (!continuous) { waterSeconds = walkSeconds = 0; return 0; }
            waterSeconds = settings.WaterReminder ? waterSeconds + delta : 0;
            walkSeconds = settings.WalkReminder ? walkSeconds + delta : 0;
            int due = 0;
            if (settings.WaterReminder && waterSeconds >= settings.WaterMinutes * 60) { due |= 1; waterSeconds = 0; }
            if (settings.WalkReminder && walkSeconds >= settings.WalkMinutes * 60) { due |= 2; walkSeconds = 0; }
            return due;
        }
    }
}
