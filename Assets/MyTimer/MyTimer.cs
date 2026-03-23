using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class MyTimer : IDisposable
{
    private static double ticksPerSecond;
    private static double ticksPerMillisecond;
    private static double ticksPerMicrosecond;
    private static double ticksPerNanosecond;
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void Initialize()
    {
        ticksPerSecond = Stopwatch.Frequency;
        ticksPerMillisecond = ticksPerSecond / 1e3;
        ticksPerMicrosecond = ticksPerSecond / 1e6;
        ticksPerNanosecond = ticksPerSecond / 1e9;
    }
        
    private readonly string name;
    private readonly StringBuilder sb;
    private readonly Stopwatch watch;
    
    private long totalElapsedTicks = 0;

    public MyTimer(string name, StringBuilder outputSB = null)
    {
        ticksPerSecond = Stopwatch.Frequency;
        ticksPerMillisecond = ticksPerSecond / 1e3;
        ticksPerMicrosecond = ticksPerSecond / 1e6;
        ticksPerNanosecond = ticksPerSecond / 1e9;
        
        this.name = name;
        sb = outputSB;
        watch = new ();
        Unpause();
    }

    public void Pause()
    {
        watch.Stop();
        totalElapsedTicks += watch.ElapsedTicks;
    }

    public void Unpause()
    {
        watch.Restart();
    }
    
    public double elapsedSeconds()
    {
        if (watch.IsRunning)
            return (watch.ElapsedTicks + totalElapsedTicks) / ticksPerSecond;
        
        return totalElapsedTicks / ticksPerSecond;
    }

    public double elapsedMilliseconds()
    {
        if (watch.IsRunning)
            return (watch.ElapsedTicks + totalElapsedTicks) / ticksPerMillisecond;
        
        return totalElapsedTicks / ticksPerMillisecond;
    }
    
    public double elapsedMicroseconds()
    {
        if (watch.IsRunning)
            return (watch.ElapsedTicks + totalElapsedTicks) / ticksPerMicrosecond;
        
        return totalElapsedTicks / ticksPerMicrosecond;
    }
    
    public double elapsedNanoseconds()
    {
        if (watch.IsRunning)
            return (watch.ElapsedTicks + totalElapsedTicks) / ticksPerNanosecond;
        
        return totalElapsedTicks / ticksPerNanosecond;
    }

    public void Dispose()
    {
        this.Pause();

        if (sb == null)
            Debug.Log(name + "(ms):  " + elapsedMilliseconds());
        else
            sb.AppendLine(name + "(ms):  " + elapsedMilliseconds());
    }
}