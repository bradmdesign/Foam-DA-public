using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Exercise the learned-ability runner while game painting takes longer than
// the presentation interval. Input sinks are fake; no live ability is used.
internal static class MacroSchedulingChecks
{
    sealed class BusySurface : Control
    {
        internal int Paints;
        protected override void OnPaint(PaintEventArgs e){Paints++;Thread.Sleep(32);e.Graphics.Clear(Color.Black);}
    }
    sealed class Sample
    {
        public int Frames,Paints,MacroTicks,Activations;
    }
    static Sample Measure(bool posted)
    {
        var sample=new Sample();
        using(var form=new Form{Text="Foam DA · Macro scheduling check",ClientSize=new Size(240,120),StartPosition=FormStartPosition.Manual,Location=new Point(20,20),ShowInTaskbar=false})
        using(var view=new BusySurface{Dock=DockStyle.Fill})
        using(var frames=new FramePresenter(form))
        using(var heartbeat=new FramePresenter(form))
        using(var legacy=new System.Windows.Forms.Timer{Interval=50})
        {
            var context=new PlayContext{Root=0x100000,Character=0x110000,Map=1,Alive=true,Panel=3};
            var ability=new LearnedAbility{Slot=1,Name="Scheduling test",Argument=5};
            var runner=new MacroRunner{ReadContext=()=>context,ReadAbilities=()=>new List<LearnedAbility>{ability},Activate=a=>sample.Activations++};
            var watch=new Stopwatch();
            EventHandler tick=delegate{sample.MacroTicks++;runner.Tick(DateTime.UtcNow,true);};
            form.Controls.Add(view);frames.Interval=16;heartbeat.Interval=50;
            heartbeat.Tick+=tick;legacy.Tick+=tick;
            frames.Tick+=delegate{
                sample.Frames++;view.Invalidate();
                if((sample.Frames>=24&&watch.ElapsedMilliseconds>=1800)||watch.ElapsedMilliseconds>=15000)form.Close();
            };
            form.Shown+=delegate{
                watch.Start();runner.Start(new[]{new TrainingAction{Ability=ability,Seconds=1}},DateTime.UtcNow);
                frames.Start();if(posted)heartbeat.Start();else legacy.Start();
            };
            form.FormClosed+=delegate{frames.Stop();heartbeat.Stop();legacy.Stop();runner.Stop("Check finished.");};
            Application.Run(form);sample.Paints=view.Paints;
        }
        return sample;
    }
    internal static void Run()
    {
        var before=Measure(false);var after=Measure(true);
        bool passed=after.MacroTicks>=10&&after.Activations>=2&&after.Activations>before.Activations;
        Directory.CreateDirectory(Path.Combine(Program.Root,"build"));
        File.WriteAllText(Path.Combine(Program.Root,"build","macro-scheduling-checks.json"),Program.Json.Serialize(new{passed,before,after}));
        if(!passed)throw new InvalidOperationException("Learned macros did not continue under continuous game painting.");
    }
}
