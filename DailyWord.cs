using System;
using System.Security.Cryptography;

namespace OffWorkWidget;

internal sealed record DailyWord(string Title, string BeforeEnd, string FreeTime)
{
    internal const int TakeABreak = 3;
    const int EverydayWordCount = 12;
    const int WrapUp = 9;
    const int DayOff = 13;
    const int Payday = 14;

    // IDs 1–12 also cover results saved by the original daily draw.
    static readonly DailyWord[] Words =
    {
        new("顺手", "先挑一件顺手的小事，做完就有个好开头。", "今天顺手做成的小事，也算一个收获。"),
        new("慢一点", "不用每件事都抢着完成，先把手上这件做完。", "把脚步放慢一点，接下来的时间归你。"),
        new("偷闲", "杯子都空了，起来倒杯水吧。", "倒杯喜欢喝的，找个舒服的地方坐一会儿。"),
        new("交卷", "想留给明天的事，写在一张纸上就好。", "纸条留下，脑子可以下班了。"),
        new("开窗", "抬头看看窗外，今天的天是什么颜色？", "有空就出去走走，看看天是怎么暗下来的。"),
        new("留白", "给下一件事留五分钟空档，喘口气也算安排。", "空出来的时间，想用来发呆也可以。"),
        new("摸鱼", "偷偷伸个懒腰，鱼会替你保密。", "现在可以大大方方地休息了。"),
        new("小奖", "想好下班后给自己的小奖励了吗？", "小奖励到了兑现时间，想吃的那一口可以安排了。"),
        new("收尾", "最后一件事做完，就别再开新的了。", "今天到此为止，剩下的明天见。"),
        new("靠岸", "把手上这件事停在一个明天好接手的地方。", "辛苦啦，今天可以停在这里。"),
        new("够了", "如果已经能用了，可以先放过这点小细节。", "做成的和没做完的，都可以先放一放。"),
        new("关灯", "临走前关掉几个标签页，给今天收个尾。", "把工作消息静音一会儿，听点自己喜欢的。"),
        new("放风", "休息日快到了，留一点时间给真正想做的事。", "空出来的时间，想出门就出门，想宅家也挺好。"),
        new("到账", "先想好第一口要吃什么。", "有想吃的就记下来，今晚也可以先犒劳自己一下。")
    };

    internal static int Pick(DateTime now, Settings settings)
    {
        const int ExtraWeight = 7;
        int untilRest = Logic.UntilRest(now, settings);
        TimeSpan? untilEnd = WorkEnd(now, settings) - now;
        bool nearRest = untilRest is 0 or 1;
        bool nearPayday = Logic.UntilPay(now, settings.PayDay) <= 2;
        bool nearEnd = untilEnd > TimeSpan.Zero && untilEnd <= TimeSpan.FromHours(2);
        int roll = RandomNumberGenerator.GetInt32(EverydayWordCount + (nearRest ? ExtraWeight : 0) +
            (nearPayday ? ExtraWeight : 0) + (nearEnd ? ExtraWeight : 0));
        if (roll < EverydayWordCount) return roll + 1;
        roll -= EverydayWordCount;
        if (nearRest)
        {
            if (roll < ExtraWeight) return DayOff;
            roll -= ExtraWeight;
        }
        return nearPayday && roll < ExtraWeight ? Payday : WrapUp;
    }

    internal static DateTime? WorkEnd(DateTime date, Settings settings) =>
        Logic.WorkingDay(date, settings) ? date.Date.Add(Logic.WorkHours(date, settings).End) : null;

    internal static DailyWord? Find(int id) => id > 0 && id <= Words.Length ? Words[id - 1] : null;

    internal string Message(DateTime now, Settings settings, bool freeTime)
    {
        string line = freeTime ? FreeTime : BeforeEnd;
        if (this != Words[Payday - 1]) return line;
        int days = Logic.UntilPay(now, settings.PayDay);
        return (days == 0 ? "今天是发薪日。" : $"离发薪还有 {days} 天。") + line;
    }
}
