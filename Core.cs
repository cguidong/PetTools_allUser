using System;
using System.Globalization;

namespace OffWorkWidget
{
    public class DateAdjustment
    {
        public string Date;
        public bool IsWorkday;
    }
    public class DayWorkTime
    {
        public int Day;
        public bool Enabled = false;
        public string Start = "09:00";
        public string End = "18:00";
    }
    public class WorkScheduleProfile
    {
        public string Mode = "";
        public string Start = "09:00";
        public string End = "18:00";
        public System.Collections.Generic.List<DayWorkTime> DayTimes = new System.Collections.Generic.List<DayWorkTime>();
        public bool SaturdayEnabled = false;
        public string SaturdayStart = "09:00";
        public string SaturdayEnd = "12:00";
    }
    public class Settings
    {
        public string Start = "09:00";
        public string End = "18:00";
        public string WorkScheduleMode = "";
        public System.Collections.Generic.List<WorkScheduleProfile> WorkSchedules = new System.Collections.Generic.List<WorkScheduleProfile>();
        public decimal Salary = 8000;
        public decimal WorkDays = 21.75m;
        public int PayDay = 10;
        public bool Topmost = true;
        public bool MouseClickThrough = false;
        public bool NightMode = false;
        public bool AutomaticAppearance = true;
        public string PetId = "cat";
        public bool UseChinaHolidays = true;
        internal ChinaHolidayCalendar Calendar;
        public int RestDays = 65; // Sunday = bit 0, Saturday = bit 6.
        public int SingleRestDays = 1;
        public int DoubleRestDays = 65;
        public bool AlternatingWeekends = false;
        public string DoubleRestWeekStart = "";
        public System.Collections.Generic.List<DateAdjustment> DateAdjustments = new System.Collections.Generic.List<DateAdjustment>();
        public bool HideAmount = true;
        public bool ShowSalaryLayout = false;
        public int AmountVisibleSeconds = 0;
        public bool WaterReminder = true;
        public bool WalkReminder = true;
        public int WaterMinutes = 60;
        public int WalkMinutes = 90;
        public double Scale = 1;
        public int ScaleReferenceVersion = 1;
        public double X = -1;
        public double Y = -1;
    }
    public static class Logic
    {
        public static TimeSpan Time(string value)
        {
            TimeSpan t;
            if (!TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out t) || t.TotalHours >= 24)
                throw new FormatException("时间请填写为 HH:mm，例如 09:00。");
            return t;
        }
        public static int RestDayCount(int mask)
        {
            int count = 0;
            for (int day = 0; day < 7; day++) if ((mask & (1 << day)) != 0) count++;
            return count;
        }
        public static string ScheduleMode(Settings s)
        {
            if (s.AlternatingWeekends) return "alternating";
            if (s.WorkScheduleMode == "custom") return "custom";
            int count = RestDayCount(s.RestDays);
            return count == 2 ? "double" : count == 1 ? "single" : "custom";
        }
        public static (TimeSpan Start, TimeSpan End) WorkHours(DateTime date, Settings s)
        {
            string mode = ScheduleMode(s);
            if (s.WorkSchedules != null) foreach (var profile in s.WorkSchedules)
            {
                if (profile == null || profile.Mode != mode) continue;
                var dayTime = mode == "double" ? null : DayTime(profile, (int)date.DayOfWeek);
                return dayTime != null && dayTime.Enabled
                    ? (Time(dayTime.Start), Time(dayTime.End))
                    : (Time(profile.Start), Time(profile.End));
            }
            return (Time(s.Start), Time(s.End));
        }
        public static DayWorkTime? DayTime(WorkScheduleProfile profile, int day)
        {
            if (profile.DayTimes != null) foreach (var entry in profile.DayTimes)
                if (entry != null && entry.Day == day) return entry;
            return day == 6 && profile.SaturdayEnabled
                ? new DayWorkTime { Day = 6, Enabled = true, Start = profile.SaturdayStart, End = profile.SaturdayEnd }
                : null;
        }
        public static bool WorkingDay(DateTime d, Settings s)
        {
            string date = d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            if(s.DateAdjustments != null) foreach(var adjustment in s.DateAdjustments) if(adjustment.Date == date) return adjustment.IsWorkday;
            bool workday; if(s.UseChinaHolidays && s.Calendar != null && s.Calendar.TryGetWorkday(d,out workday)) return workday;
            return (RestDaysForWeek(d, s) & (1 << (int)d.DayOfWeek)) == 0;
        }
        public static DateTime WeekStart(DateTime date) => date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7);
        public static int RestDaysForWeek(DateTime date, Settings s)
        {
            if (!s.AlternatingWeekends) return s.RestDays;
            DateTime anchor;
            if (!DateTime.TryParseExact(s.DoubleRestWeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out anchor))
                anchor = new DateTime(2024, 1, 1);
            int weeks = (WeekStart(date) - WeekStart(anchor)).Days / 7;
            return weeks % 2 == 0 ? 65 : 1;
        }
        public static bool RestStarted(DateTime now, Settings s, int days)
        {
            return days == 0 || (days == 1 && now.TimeOfDay >= WorkHours(now, s).End);
        }
        public static DateTime? NextWorkStart(DateTime now, Settings s)
        {
            DateTime? StartOn(DateTime date)
            {
                if (!WorkingDay(date, s)) return null;
                DateTime start = date.Date.Add(WorkHours(date, s).Start);
                return start >= now ? start : null;
            }
            if (s.RestDays == 127 && !s.AlternatingWeekends)
            {
                DateTime? nearest = null;
                var candidates = new System.Collections.Generic.List<DateTime>();
                if (s.DateAdjustments != null) foreach (var adjustment in s.DateAdjustments)
                    if (DateTime.TryParseExact(adjustment.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) && date >= now.Date) candidates.Add(date);
                if (s.UseChinaHolidays && s.Calendar != null) candidates.AddRange(s.Calendar.KnownDates);
                foreach (DateTime date in candidates)
                {
                    var start = StartOn(date);
                    if (start.HasValue && (!nearest.HasValue || start.Value < nearest.Value)) nearest = start;
                }
                return nearest;
            }
            int limit = 7 * (1 + (s.DateAdjustments?.Count ?? 0) + (s.UseChinaHolidays && s.Calendar != null ? s.Calendar.RuleCount : 0)) + 1;
            limit = Math.Min(limit, (DateTime.MaxValue.Date - now.Date).Days + 1);
            for (int i = 0; i < limit; i++)
            {
                var start = StartOn(now.Date.AddDays(i));
                if (start.HasValue) return start;
            }
            return null;
        }
        public static string RestDescription(DateTime now, Settings s, int days)
        {
            if(days < 0) return "尚未设置可用的休息日期";
            DateTime date = now.Date.AddDays(days);
            string reason = s.AlternatingWeekends ? "大小周休息日" : "每周休息日", key = date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            bool manual = false;
            if(s.DateAdjustments != null) foreach(var adjustment in s.DateAdjustments)
                if(adjustment.Date == key) { manual = true; reason = "手动调休"; break; }
            bool workday;
            if(!manual && s.UseChinaHolidays && s.Calendar != null && s.Calendar.TryGetWorkday(date,out workday) && !workday) reason = "中国节假日安排";
            return (days == 0 ? "今天休息" : RestStarted(now, s, days) ? "已进入休息，本轮休息日" : "下次休息") + "：" + key + "（" + reason + "）";
        }
        public static int UntilRest(DateTime now, Settings s)
        {
            if(s.RestDays == 0 && !s.AlternatingWeekends)
            {
                int nearest = int.MaxValue;
                var candidates = new System.Collections.Generic.List<DateTime>();
                if(s.DateAdjustments != null) foreach(var adjustment in s.DateAdjustments)
                {
                    DateTime date;
                    if(DateTime.TryParseExact(adjustment.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) candidates.Add(date);
                }
                if(s.UseChinaHolidays && s.Calendar != null) candidates.AddRange(s.Calendar.KnownDates);
                foreach(DateTime date in candidates) if(date >= now.Date && !WorkingDay(date,s)) nearest = Math.Min(nearest,(date - now.Date).Days);
                return nearest == int.MaxValue ? -1 : nearest;
            }            int limit = 7 * (1 + (s.DateAdjustments == null ? 0 : s.DateAdjustments.Count) + (s.UseChinaHolidays && s.Calendar != null ? s.Calendar.RuleCount : 0));
            limit = Math.Min(limit,(DateTime.MaxValue.Date - now.Date).Days + 1);
            for (int i = 0; i < limit; i++) if (!WorkingDay(now.Date.AddDays(i), s)) return i;
            return -1;
        }
        public static decimal Earned(DateTime now, Settings s)
        {
            if (!WorkingDay(now, s)) return 0;
            var hours = WorkHours(now, s);
            double start = hours.Start.TotalSeconds, end = hours.End.TotalSeconds;
            double ratio = Math.Max(0, Math.Min(1, (now.TimeOfDay.TotalSeconds - start) / (end - start)));
            return s.Salary / s.WorkDays * (decimal)ratio;
        }
        public static int UntilPay(DateTime now, int day)
        {
            DateTime pay = new DateTime(now.Year, now.Month, Math.Min(day, DateTime.DaysInMonth(now.Year, now.Month)));
            if (pay < now.Date) { DateTime next = now.AddMonths(1); pay = new DateTime(next.Year, next.Month, Math.Min(day, DateTime.DaysInMonth(next.Year, next.Month))); }
            return (pay - now.Date).Days;
        }
        public static void Validate(Settings s)
        {
            if (Time(s.End) <= Time(s.Start)) throw new FormatException("下班时间须晚于上班时间，当前版本支持当天班次。");
            var modes = new System.Collections.Generic.HashSet<string>();
            if (s.WorkScheduleMode != "" && s.WorkScheduleMode != "double" && s.WorkScheduleMode != "single" && s.WorkScheduleMode != "alternating" && s.WorkScheduleMode != "custom")
                throw new FormatException("工作时间所属休息规则无效。");
            if (s.WorkSchedules != null) foreach (var profile in s.WorkSchedules)
            {
                if (profile == null || (profile.Mode != "double" && profile.Mode != "single" && profile.Mode != "alternating" && profile.Mode != "custom") || !modes.Add(profile.Mode))
                    throw new FormatException("工作时间所属休息规则无效或重复。");
                string label = profile.Mode == "double" ? "双休" : profile.Mode == "single" ? "单休" : profile.Mode == "alternating" ? "大小周" : "自定义";
                try
                {
                    if (Time(profile.End) <= Time(profile.Start)) throw new FormatException();
                }
                catch (FormatException) { throw new FormatException(label + "下班时间须晚于上班时间，格式为 HH:mm。"); }
                var days = new System.Collections.Generic.HashSet<int>();
                if (profile.DayTimes != null) foreach (var entry in profile.DayTimes)
                {
                    if (entry == null || entry.Day < 0 || entry.Day > 6 || !days.Add(entry.Day))
                        throw new FormatException(label + "独立时段的星期无效或重复。");
                    if (!entry.Enabled) continue;
                    string dayLabel = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[entry.Day];
                    try
                    {
                        if (Time(entry.End) <= Time(entry.Start)) throw new FormatException();
                    }
                    catch (FormatException) { throw new FormatException(label + dayLabel + "下班时间须晚于上班时间，格式为 HH:mm。"); }
                }
                if (profile.SaturdayEnabled)
                {
                    try
                    {
                        if (Time(profile.SaturdayEnd) <= Time(profile.SaturdayStart)) throw new FormatException();
                    }
                    catch (FormatException) { throw new FormatException(label + "周六下班时间须晚于上班时间，格式为 HH:mm。"); }
                }
            }
            if (s.Salary < 0 || s.Salary > 100000000 || s.WorkDays <= 0 || s.WorkDays > 31 || s.PayDay < 1 || s.PayDay > 31)
                throw new FormatException("请检查月薪、月计薪天数（大于 0 且不超过 31）和发薪日（1–31）。");
            try { _ = 100000000m / s.WorkDays; }
            catch (OverflowException) { throw new FormatException("计薪天数过小，超出薪资计算范围，请增大计薪天数。"); }
            if (s.WaterMinutes < 1 || s.WaterMinutes > 720 || s.WalkMinutes < 1 || s.WalkMinutes > 720) throw new FormatException("提醒间隔须为 1–720 分钟。");
            if (s.AmountVisibleSeconds < 0 || s.AmountVisibleSeconds > 86400) throw new FormatException("金额显示时长须为 0–86400 秒，0 表示一直显示。");
            if (s.RestDays < 0 || s.RestDays > 127) throw new FormatException("休息日设置无效。");
            if (s.SingleRestDays < 0 || s.SingleRestDays > 127 || RestDayCount(s.SingleRestDays) != 1)
                throw new FormatException("单休须设置 1 个休息日。");
            if (s.DoubleRestDays < 0 || s.DoubleRestDays > 127 || RestDayCount(s.DoubleRestDays) != 2)
                throw new FormatException("双休须设置 2 个休息日。");
            if (s.AlternatingWeekends && (!DateTime.TryParseExact(s.DoubleRestWeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime anchor) || anchor.DayOfWeek != DayOfWeek.Monday))
                throw new FormatException("大小周起始日期须为有效的周一。");
            var dates = new System.Collections.Generic.HashSet<string>();
            if(s.DateAdjustments != null) foreach(var adjustment in s.DateAdjustments)
            {
                DateTime date;
                if(adjustment == null || !DateTime.TryParseExact(adjustment.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date) || !dates.Add(adjustment.Date))
                    throw new FormatException("调休日期无效或重复。");
            }
            if (s.Scale < .5 || s.Scale > 1.5 || double.IsNaN(s.Scale) || double.IsInfinity(s.Scale)) throw new FormatException("缩放范围为 0.5–1.5。");
        }
    }
}
