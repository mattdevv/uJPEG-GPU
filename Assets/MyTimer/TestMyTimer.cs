using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class TestMyTimer : MonoBehaviour
{
    void Start()
    {
        // timer starts automatically but can also stops automatically if in a 'using' statement
        using (MyTimer timer = new("TimerName"))
        {
            // code to be timed
            FindFirstObjectByType<MonoBehaviour>();
            
            // timer can be paused and unpaused
            timer.Pause();
            timer.Unpause();
            
            // the current elapsed time can always be read
            Debug.Log(timer.elapsedSeconds());
        }
        
        
        
        // alternative format
        MyTimer timer2 = new("TimerName");
        FindFirstObjectByType<MonoBehaviour>();
        timer2.Pause();
        timer2.Unpause();
        Debug.Log(timer2.elapsedSeconds());
        
        // call dispose when finished
        timer2.Dispose();
    }
}
