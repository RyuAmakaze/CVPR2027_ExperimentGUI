using System;

namespace ExperimentUI
{
    // Schedules rest by the number of completed trials, independently of source IDs.
    public static class BreakSchedule
    {
        public const int ProductionInterval = 100;

        public static int Midpoint(int totalTrials)
        {
            if (totalTrials < 0) throw new ArgumentOutOfRangeException("totalTrials");
            return totalTrials >= 2 ? totalTrials / 2 : 0;
        }

        public static bool IsMidpointDue(int completedCount, int totalTrials)
        {
            int point = Midpoint(totalTrials);
            if (completedCount < 0 || completedCount > totalTrials)
                throw new ArgumentOutOfRangeException("completedCount");
            return point > 0 && completedCount == point;
        }

        public static bool IsDue(int completedCount, int totalTrials, int interval)
        {
            if (totalTrials < 0) throw new ArgumentOutOfRangeException("totalTrials");
            if (completedCount < 0 || completedCount > totalTrials)
                throw new ArgumentOutOfRangeException("completedCount");
            if (interval <= 0) throw new ArgumentOutOfRangeException("interval");
            return completedCount > 0 && completedCount < totalTrials && completedCount % interval == 0;
        }
    }
}
