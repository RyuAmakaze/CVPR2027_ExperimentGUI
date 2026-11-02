using System;
using ExperimentUI;

internal static class BreakScheduleTests
{
    private static int passed;

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        passed++;
        Console.WriteLine("PASS: " + description);
    }

    private static void Reject(int completed, int total, int interval, string description)
    {
        bool failed = false;
        try { BreakSchedule.IsDue(completed, total, interval); }
        catch (ArgumentOutOfRangeException) { failed = true; }
        Assert(failed, description);
    }

    public static int Main()
    {
        try
        {
            Assert(BreakSchedule.ProductionInterval == 100, "production interval remains 100");
            Assert(!BreakSchedule.IsDue(0, 250, 100), "no rest before the first completed trial");
            Assert(!BreakSchedule.IsDue(99, 250, 100), "99 completed: no rest");
            Assert(BreakSchedule.IsDue(100, 250, 100), "100 completed: rest");
            Assert(!BreakSchedule.IsDue(101, 250, 100), "101 completed: no rest");
            Assert(BreakSchedule.IsDue(200, 250, 100), "200 completed: second rest");
            Assert(!BreakSchedule.IsDue(100, 100, 100), "exactly 100 total: no rest after final trial");
            Assert(!BreakSchedule.IsDue(200, 200, 100), "exactly 200 total: no final rest");
            Assert(!BreakSchedule.IsDue(99, 99, 100), "fewer than 100 total: no final rest");
            Assert(BreakSchedule.IsDue(100, 101, 100), "100 of 101: rest before the last trial");
            Assert(BreakSchedule.Midpoint(8) == 4, "test: eight trials have midpoint four");
            int eightTrialBreaks = 0;
            for (int count = 0; count <= 8; count++)
            {
                bool due = BreakSchedule.IsMidpointDue(count, 8);
                Assert(due == (count == 4), "test eight trials: rest only at four, completed=" + count);
                if (due) eightTrialBreaks++;
            }
            Assert(eightTrialBreaks == 1, "test: exactly one rest across eight completed-count boundaries");
            Assert(BreakSchedule.Midpoint(5) == 2 && BreakSchedule.IsMidpointDue(2, 5), "test: odd five trials rest after two");
            Assert(!BreakSchedule.IsMidpointDue(3, 5) && !BreakSchedule.IsMidpointDue(4, 5) && !BreakSchedule.IsMidpointDue(5, 5), "test: odd dataset has no later or final rest");
            Assert(BreakSchedule.Midpoint(0) == 0 && !BreakSchedule.IsMidpointDue(0, 0), "test: empty dataset has no rest");
            Assert(BreakSchedule.Midpoint(1) == 0 && !BreakSchedule.IsMidpointDue(0, 1) && !BreakSchedule.IsMidpointDue(1, 1), "test: one trial has no rest");
            Assert(BreakSchedule.Midpoint(2) == 1 && BreakSchedule.IsMidpointDue(1, 2) && !BreakSchedule.IsMidpointDue(2, 2), "test: two trials rest after one and skip final boundary");
            Assert(BreakSchedule.Midpoint(Int32.MaxValue) == Int32.MaxValue / 2, "midpoint handles large odd totals without overflow");
            bool negativeTotalRejected = false;
            try { BreakSchedule.Midpoint(-1); } catch (ArgumentOutOfRangeException) { negativeTotalRejected = true; }
            Assert(negativeTotalRejected, "midpoint rejects negative total");
            foreach (int invalid in new int[] { -1, 9 })
            {
                bool rejected = false;
                try { BreakSchedule.IsMidpointDue(invalid, 8); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Assert(rejected, "midpoint rejects completed count outside dataset: " + invalid);
            }
            Assert(!BreakSchedule.IsDue(0, 0, 100), "empty sequence has no rest");
            Assert(BreakSchedule.IsDue(1, 2, 1) && !BreakSchedule.IsDue(2, 2, 1), "interval 1 still skips final trial");
            Assert(BreakSchedule.IsDue(Int32.MaxValue - 1, Int32.MaxValue, 2), "large completed counts do not overflow");
            Assert(!BreakSchedule.IsDue(Int32.MaxValue, Int32.MaxValue, 1), "large final total has no rest");
            Reject(-1, 100, 100, "negative completed count rejected");
            Reject(0, -1, 100, "negative total rejected");
            Reject(101, 100, 100, "completed count beyond total rejected");
            Reject(0, 100, 0, "zero interval rejected");
            Reject(0, 100, -3, "negative interval rejected");
            Console.WriteLine("ALL " + passed + " CHECKS PASSED");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
