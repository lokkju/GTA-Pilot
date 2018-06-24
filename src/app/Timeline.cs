﻿using Emgu.CV.Structure;
using GTAPilot.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace GTAPilot
{
    public class CompassExtendedFrame
    {
        public double LastN = double.NaN;
        public double LastE = double.NaN;
        public double LastS = double.NaN;
        public double LastW = double.NaN;
        public double Bias = double.NaN;
        public double RollBias = double.NaN;
    }

    public class TimelineValue
    {
        public double Value = double.NaN;
        public double SecondsWhenComputed = double.NaN;

        public double OutputValue = double.NaN;
        public double InputValue = double.NaN;
        public double SetpointValue = double.NaN;
    }

    public class TimelineFrame
    {
        public int Id;
        public double Seconds;

        public TimelineValue Heading = new TimelineValue();
        public TimelineValue Speed = new TimelineValue();
        public TimelineValue Roll = new TimelineValue();
        public TimelineValue Pitch = new TimelineValue();
        public TimelineValue Altitude = new TimelineValue();

        public CompassExtendedFrame Extended = new CompassExtendedFrame();

        public PointF Location;

        internal bool IsComplete;

        internal bool LocationComplete;
        internal CircleF RollHint;
    }

    class Timeline
    {
        public static int LastFrameId;

        // TODO: static to save allocations but bad
        public static TimelineFrame[] Data = new TimelineFrame[950000];

        // location is los santos runway 3
        public static PointF StartLocation = new PointF(2030.2f, 4573.9f);
        public static PointF CurrentLocation;

        public static double Roll => Latest(f => f.Roll.Value);
        public static double Pitch => Latest(f => f.Pitch.Value);
        public static double RollAvg => LatestAvg(f => f.Roll.Value);
        public static double PitchAvg => LatestAvg(f => f.Pitch.Value);
        public static double Speed => Latest(f => f.Speed.Value);
        public static double Altitude => Latest(f => f.Altitude.Value);
        public static double Heading => Latest(f => f.Heading.Value);

        public static Stopwatch Duration;

        public static void Begin()
        {
            Duration = Stopwatch.StartNew();

            new Thread(() =>
            {
                int lastDoneFrame = -1;

                while (true)
                {
                    for (var i = lastDoneFrame + 1; i <= LastFrameId; i++)
                    {
                        if (Data[i] != null && Data[i].IsComplete)
                        {
                            CompleteFrame(i);
                            lastDoneFrame = i;
                        }
                        else
                        {
                            break; // bail on first non-complete frame, try next time
                        }
                    }
                    
                    Thread.Sleep(10);
                }
            }).Start();
        }

        private static double Latest(Func<TimelineFrame, double> finder)
        {
            for (var i = LastFrameId; i >= 0; i--)
            {
                if (Data[i] != null)
                {
                    if (!double.IsNaN(finder(Data[i])))
                    {
                        return finder(Data[i]);
                    }
                }
            }
            return double.NaN;
        }

        private static double LatestAvg(Func<TimelineFrame, double> finder)
        {
            List<double> ret = new List<double>();

            for (var i = LastFrameId; i >= 0; i--)
            {
                if (Data[i] != null)
                {
                    if (!double.IsNaN(finder(Data[i])))
                    {
                        ret.Add(finder(Data[i]));

                        if (ret.Count == 8) break;
                    }
                }
            }

            if (ret.Count == 0) return 0;

            return ret.Sum() / ret.Count;
        }

        public static TimelineFrame LatestFrame(Func<TimelineFrame, double> finder, int endId)
        {
            for (var i = endId - 1; i >= 0; i--)
            {
                if (Data[i] != null)
                {
                    if (!double.IsNaN(finder(Data[i])))
                    {
                        return Data[i];
                    }
                }
            }
            return null;
        }

        internal static void CompleteFrame(int id)
        {
            var newFrame = Data[id];
            if (id == 0)
            {
                newFrame.LocationComplete = true;
                newFrame.Location = StartLocation;
            }
            else
            {
                TimelineFrame oldFrame;
                if (!double.IsNaN(newFrame.Heading.Value) && !double.IsNaN(newFrame.Speed.Value))
                {
                    // Find a frame with valid speed and heading
                    oldFrame = LatestFrame((f) => f.Heading.Value + f.Speed.Value, id - 1);
                    if (oldFrame != null && oldFrame != newFrame)
                    {
                        var dx = ComputePositionChange(oldFrame, newFrame);
                        newFrame.Location = oldFrame.Location.Add(dx);
                        newFrame.LocationComplete = true;
                        CurrentLocation = newFrame.Location;

                        return;
                    }
                }

                oldFrame = LatestFrame((f) => f.Location == default(PointF) ? double.NaN : 0, id);
                newFrame.Location = oldFrame.Location;
                newFrame.LocationComplete = true;
                CurrentLocation = newFrame.Location;
            }
            
        }

        private static PointF ComputePositionChange(TimelineFrame oldFrame, TimelineFrame newFrame)
        {
            var timeDelta = newFrame.Seconds - oldFrame.Seconds;

            var speed = newFrame.Speed.Value;

            var knotsPerHour = speed;
            var KnotsPerSecondToMetersPerSecond = 0.51444444444;
            var MetersPerSecond = knotsPerHour * KnotsPerSecondToMetersPerSecond;
            var scale = 1 / 3.32;

            double heading = newFrame.Heading.Value;

            if (!double.IsNaN(newFrame.Extended.Bias) &&
                !double.IsNaN(newFrame.Extended.RollBias))
            {
                heading = heading - newFrame.Extended.Bias + (newFrame.Extended.RollBias + 1.4);
            }

            return new PointF((float)(Math.Sin(Math2.ToRad(heading)) * (scale * MetersPerSecond * timeDelta)),
                              (float)(Math.Cos(Math2.ToRad(heading)) * (scale * MetersPerSecond * timeDelta * -1)));
        }
    }
}
