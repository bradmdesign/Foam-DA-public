using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Reproduce the actual failure: an expensive parent paint exceeds the frame
// clock period, continually postponing WM_PAINT for the minimap child.
internal static class MinimapRepaintChecks
{
    sealed class BusySurface : Control
    {
        internal int Paints;
        protected override void OnPaint(PaintEventArgs e)
        {Thread.Sleep(32);Paints++;e.Graphics.Clear(Color.Black);base.OnPaint(e);}
    }
    static int[] Measure(bool present)
    {
        using(var form=new Form{ClientSize=new Size(900,600),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(20,20)})
        using(var surface=new BusySurface{Dock=DockStyle.Fill})
        using(var mini=new MiniMapControl{Bounds=new Rectangle(600,10,244,164)})
        using(var clock=new FramePresenter(form))
        {
            form.Controls.Add(surface);surface.Controls.Add(mini);int ticks=0,visibleTicks=0,handleTicks=0,maxGap=0,starved=0;long lastPaint=0;var watch=new Stopwatch();
            clock.Tick+=delegate {
                ticks++;if(mini.Visible)visibleTicks++;if(mini.IsHandleCreated)handleTicks++;surface.Invalidate();mini.Invalidate();
                if(present){int previous=mini.PaintedFrames;bool due=watch.ElapsedMilliseconds-lastPaint>=150;mini.PresentFrame();if(mini.PaintedFrames!=previous){long now=watch.ElapsedMilliseconds;if(lastPaint>0)maxGap=Math.Max(maxGap,(int)(now-lastPaint));lastPaint=now;}else if(due)starved++;}
                // Count actual opportunities, so an unrelated busy export does
                // not make this a test of the whole machine's frame rate.
                if((ticks>=24&&watch.ElapsedMilliseconds>=1800)||watch.ElapsedMilliseconds>=15000){clock.Stop();form.Close();}
            };
            form.Shown+=delegate {watch.Start();clock.Start();};Application.Run(form);
            return new[]{ticks,surface.Paints,mini.PaintedFrames,visibleTicks,handleTicks,maxGap,starved};
        }
    }
    internal static void Run()
    {
        var before=Measure(false);var after=Measure(true);
        bool pass=before[0]>=12&&before[1]>=12&&before[2]<before[0]/2&&after[0]>=12&&after[2]>=4&&after[6]==0;
        File.WriteAllText(Path.Combine(Program.Root,"build","minimap-repaint-checks.json"),Program.Json.Serialize(new{
            passed=pass,before=new{ticks=before[0],game_paints=before[1],minimap_paints=before[2],visible_ticks=before[3],handle_ticks=before[4]},
            after=new{ticks=after[0],game_paints=after[1],minimap_paints=after[2],visible_ticks=after[3],handle_ticks=after[4],max_refresh_gap_ms=after[5],missed_refresh_opportunities=after[6]}}));
        if(!pass)throw new InvalidOperationException("Minimap repaint starvation regression failed.");
    }
}
