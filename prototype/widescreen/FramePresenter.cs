using System;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Schedule against a monotonic clock, independently of WM_TIMER's low-priority
// delivery. Only one callback may be queued; a busy UI never accumulates frames.
internal sealed class FramePresenter : IDisposable
{
    readonly Control owner;
    readonly AutoResetEvent changed=new AutoResetEvent(false);
    Thread worker;
    volatile bool running,disposed;
    int interval=16,pending;
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint milliseconds);
    internal event EventHandler Tick;
    internal FramePresenter(Control control){owner=control;}
    internal int Interval {get{return interval;}set{if(value<1||value>1000)throw new ArgumentOutOfRangeException("value");interval=value;changed.Set();}}
    internal void Start(){if(disposed)return;running=true;if(worker==null){worker=new Thread(Loop){IsBackground=true,Name="DA presentation clock"};worker.Start();}changed.Set();}
    internal void Stop(){running=false;changed.Set();}
    void Loop()
    {
        long next=0;bool precise=false;
        try{while(!disposed){
            if(!running){if(precise){timeEndPeriod(1);precise=false;}changed.WaitOne();next=0;continue;}
            if(!precise)precise=timeBeginPeriod(1)==0;
            long now=Stopwatch.GetTimestamp();
            if(now<next){changed.WaitOne(Math.Max(1,(int)((next-now)*1000/Stopwatch.Frequency)));continue;}
            // Use the exact requested frame period for the three UI choices.
            double period=interval==16?1000.0/60:interval==33?1000.0/30:interval;
            long step=(long)(Stopwatch.Frequency*period/1000);
            next=next==0||now-next>step?now+step:next+step;
            if(Interlocked.CompareExchange(ref pending,1,0)!=0)continue;
            try{owner.BeginInvoke((Action)delegate{
                try{if(!disposed&&running&&!owner.IsDisposed&&Tick!=null)Tick(this,EventArgs.Empty);}
                finally{Interlocked.Exchange(ref pending,0);}
            });}catch(InvalidOperationException){Interlocked.Exchange(ref pending,0);}
        }}finally{if(precise)timeEndPeriod(1);}
    }
    public void Dispose(){if(disposed)return;disposed=true;running=false;changed.Set();if(worker!=null)worker.Join();changed.Dispose();Tick=null;}
}
