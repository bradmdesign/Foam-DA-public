using System;
using System.Drawing;
using System.Drawing.Imaging;

internal static class WorldZoomChecks
{
    static void Check(bool value,string message){if(!value)throw new Exception("Play zoom: "+message);}
    internal static int Run()
    {
        int count=0;
        Check(WorldZoomCapture.LightPixel(0xf800,0,16,true)==0x7800,"565 night intensity");count++;
        Check(WorldZoomCapture.LightPixel(0x7c00,0,16,false)==0x3c00,"555 night intensity");count++;
        Check(WorldZoomCapture.LightPixel(0xf800,0x001f,16,true)==0x780f,"separate native color truncation");count++;
        Check(WorldZoomCapture.LightPixel(0xf800,0x001f,0,true)==0x001f&&WorldZoomCapture.LightPixel(0xf800,0x001f,32,true)==0xf800,"lighting endpoints");count++;
        foreach(Size canvas in new[]{new Size(960,600),new Size(1088,640),new Size(1152,680)})
        foreach(bool compact in new[]{false,true})
        foreach(int zoom in new[]{100,125,150,175,200})
        foreach(Size display in new[]{canvas,new Size(1715,1009),new Size(1920,1000)})
        {
            var map=new Rectangle(3,3,canvas.Width-24,canvas.Height-(compact?56:172));
            var mask=new byte[canvas.Width*canvas.Height];RectangleF crop=WorldZoomGeometry.Crop(map,zoom);
            Check(crop.Left>=map.Left&&crop.Top>=map.Top&&crop.Right<=map.Right&&crop.Bottom<=map.Bottom,"crop escapes rendered world");
            Check(Math.Abs(crop.Width/crop.Height-(float)map.Width/map.Height)<.0001,"aspect ratio changed");
            Point native;Rectangle screen=WorldZoomGeometry.ScreenMap(map,canvas,display);
            Point center=new Point(screen.Left+screen.Width/2,screen.Top+screen.Height/2);
            Check(WorldZoomGeometry.TryMap(center,display,canvas,map,zoom,mask,out native)&&Math.Abs(native.X-map.Left-map.Width/2)<=1&&Math.Abs(native.Y-map.Top-map.Height/2)<=1,"center targeting shifted");
            Point hud=new Point(display.Width/4,display.Height-10);
            Check(WorldZoomGeometry.TryMap(hud,display,canvas,map,zoom,mask,out native)&&native==ViewGeometry.ToGame(hud,new Rectangle(Point.Empty,display),canvas),"HUD input was transformed");
            Check(WorldZoomGeometry.TryMap(screen.Location,display,canvas,map,zoom,mask,out native)&&Math.Abs(native.X-crop.Left)<=1&&Math.Abs(native.Y-crop.Top)<=1,"crop edge targeting");
            count++;
        }
        var canvasSize=new Size(16,12);var area=new Rectangle(2,2,12,8);var protect=new byte[16*12];Point point;
        protect[6*16+8]=1;
        Check(WorldZoomGeometry.TryMap(new Point(8,6),canvasSize,canvasSize,area,200,protect,out point)&&point==new Point(8,6),"popup input must remain at its original location");count++;
        Check(!WorldZoomGeometry.TryMap(new Point(9,6),canvasSize,canvasSize,area,200,protect,out point),"terrain must not click a native popup after inverse mapping");count++;
        protect[6*16+8]=2;
        Check(WorldZoomGeometry.TryMap(new Point(9,6),canvasSize,canvasSize,area,200,protect,out point)&&point==new Point(8,6),"notification glyph must remain click-through after inverse targeting");count++;
        protect[6*16+8]=1;
        using(var baseFrame=new Bitmap(32,24,PixelFormat.Format32bppRgb))
        using(var world=new Bitmap(16,12,PixelFormat.Format32bppRgb))
        using(var painter=new WorldZoomPainter())
        {
            using(var g=Graphics.FromImage(baseFrame))g.Clear(Color.Lime);
            using(var g=Graphics.FromImage(world))g.Clear(Color.Blue);
            foreach(FrameFilter filter in new[]{FrameFilter.PixelEdges,FrameFilter.IntegerPixels,FrameFilter.Bilinear,FrameFilter.Bicubic})
            {
                Bitmap result=painter.Paint(baseFrame,world,area,150,protect,filter);
                Check(result.GetPixel(0,0).ToArgb()==Color.Lime.ToArgb()&&result.GetPixel(16,12).ToArgb()==Color.Lime.ToArgb(),"HUD / popup pixels changed during zoom");
                Check(result.GetPixel(10,10).ToArgb()==Color.Blue.ToArgb(),"clean world did not replace the old terrain");count++;
            }
            using(var native=new Bitmap(16,12,PixelFormat.Format32bppRgb))
            {
                using(var g=Graphics.FromImage(native))g.Clear(Color.Lime);
                Bitmap result=painter.PaintNative(native,world,area,150,protect,new Size(32,24));
                Check(result.GetPixel(0,0).ToArgb()==Color.Lime.ToArgb()&&result.GetPixel(16,12).ToArgb()==Color.Lime.ToArgb()&&result.GetPixel(10,10).ToArgb()==Color.Blue.ToArgb(),"single-pass world and HUD composition");count++;
            }
        }
        using(var art=new Bitmap(4,2,PixelFormat.Format32bppRgb))using(var scaler=new PixelEdgeScaler())
        {
            using(var g=Graphics.FromImage(art)){g.Clear(Color.Red);g.FillRectangle(Brushes.Blue,2,0,2,2);}
            Bitmap scaled=scaler.Scale(art,4,2,new RectangleF(1,0,2,1));
            Check(scaled.GetPixel(0,0).ToArgb()==Color.Red.ToArgb()&&scaled.GetPixel(1,0).ToArgb()==Color.Red.ToArgb()&&scaled.GetPixel(2,0).ToArgb()==Color.Blue.ToArgb(),"exact integer crop edges");count++;
            scaled=scaler.Scale(art,4,2,new RectangleF(1.25f,0,2,1));
            Color mix=scaled.GetPixel(1,0);Check(Math.Abs(mix.R-127)<=1&&Math.Abs(mix.B-127)<=1&&scaled.GetPixel(0,0).R==255,"fractional crop only blends the pixel boundary");count++;
        }
        var mailbox=new FrameMailbox(48);var pixels=new byte[48];var maskBuffer=new byte[12];var worldBuffer=new byte[24];int pitch;bool ready;
        mailbox.Writing[0]=5;mailbox.ProtectionWriting[0]=1;mailbox.WorldWriting[0]=7;mailbox.WorldMapWriting=new Rectangle(1,1,2,1);mailbox.Publish(8,true);
        Check(mailbox.Take(ref pixels,ref maskBuffer,ref worldBuffer,out pitch,out ready)&&pixels[0]==5&&maskBuffer[0]==1&&worldBuffer[0]==7&&mailbox.WorldMapReading.Width==2,"world / mask / frame must publish together");count++;
        mailbox.WorldWriting[0]=8;Check(worldBuffer[0]==7,"capture worker changed a displayed world buffer");count++;
        mailbox.WorldMapWriting=Rectangle.Empty;mailbox.Publish(8,false);mailbox.Take(ref pixels,ref maskBuffer,ref worldBuffer,out pitch,out ready);
        Check(mailbox.WorldMapReading.IsEmpty,"travel transition retained an old zoomable world");count++;
        count+=CaptureChecks();
        count+=WheelChecks();
        var wheelUi=new[]{new Rectangle(87,492,444,108),new Rectangle(913,168,153,303)};
        var wheelScene=new NativeWheelScene(new Rectangle(3,3,1063,468),wheelUi);
        foreach(Size display in new[]{new Size(1088,640),new Size(1715,1009),new Size(1920,1080)})
        {
            foreach(Point uiPoint in new[]{new Point(100,520),new Point(990,230)})
            {
                var view=new Rectangle(37,19,display.Width,display.Height);
                var screen=new Point(view.Left+(int)Math.Ceiling(uiPoint.X*(double)display.Width/1088),view.Top+(int)Math.Ceiling(uiPoint.Y*(double)display.Height/640));
                var logical=ViewGeometry.ToGame(screen,view,new Size(1088,640));
                Check(!wheelScene.IsWorld(logical),"UI wheel hit test must use native coordinates in every display mode");count++;
            }
        }
        Check(wheelScene.IsWorld(new Point(600,200))&&!wheelScene.IsWorld(new Point(1070,200)),"Only terrain inside the viewport can zoom");count++;
        Check(!NativeWheelScene.Empty.IsWorld(new Point(10,10)),"Missing or transitioning scene must not trigger zoom");count++;
        Check((short)(NativeWheelScene.WheelParameter(-120,8)>>16)==-120&&(NativeWheelScene.WheelParameter(-120,8)&65535)==8,"Native wheel packet must retain signed direction and modifiers");count++;
        mailbox.WheelWriting=wheelScene;mailbox.Publish(8,false);mailbox.WheelWriting=NativeWheelScene.Empty;
        mailbox.Take(ref pixels,ref maskBuffer,ref worldBuffer,out pitch,out ready);
        Check(mailbox.WheelReading.IsWorld(new Point(600,200)),"Wheel regions must publish atomically with their displayed frame");count++;
        mailbox.Publish(8,false);mailbox.Take(ref pixels,ref maskBuffer,ref worldBuffer,out pitch,out ready);
        Check(!mailbox.WheelReading.IsWorld(new Point(600,200)),"Scene changes must not retain stale wheel targets");count++;
        using(var options=new MapPanel())
        {
            Check(options.PlayZoom.Value==100&&options.PlayZoom.Minimum==100&&options.PlayZoom.Maximum==200,"zoom defaults and limits");count++;
            options.Size=new Size(400,134);options.CreateControl();options.PerformLayout();
            foreach(System.Windows.Forms.Control row in options.Controls[0].Controls)row.PerformLayout();
            Check(options.CustomZoom.Parent==options.ZoomScale.Parent&&options.Fog.Parent==options.Range.Parent&&options.Fog.Parent==options.Shape.Parent&&options.Fog.Parent==options.Opacity.Parent,"map controls must share their requested rows");count++;
            Check(!options.VerticalScroll.Visible&&!options.HorizontalScroll.Visible&&options.Controls[0].Height<=options.ClientSize.Height,"map controls must fit the native recommended companion without scrolling");count++;
            foreach(System.Windows.Forms.Control row in options.Controls[0].Controls)
            {
                if(row.Controls.Count==0)continue;
                foreach(System.Windows.Forms.Control control in row.Controls)
                    Check(control.Width>=18&&control.Right<=row.ClientSize.Width&&control.Bottom<=row.ClientSize.Height,"map control clipped in its row: "+control.Text);
            }
            using(var image=new Bitmap(options.Width,options.Height)){options.DrawToBitmap(image,options.ClientRectangle);image.Save(System.IO.Path.Combine(Program.Root,"build","map-controls-check.png"));}
            count++;
        }
        return count;
    }
    static int WheelChecks()
    {
        var wheel=new ZoomWheelInput();bool play;
        Check(wheel.Step(120,false,false,0,out play)==1&&play,"plain wheel must zoom terrain without Tab");
        Check(wheel.Step(-240,false,true,1,out play)==-2&&play,"plain wheel must still zoom terrain with Tab open");
        Check(wheel.Step(120,true,false,2,out play)==0&&!play,"Ctrl wheel with no Tab map must not zoom terrain");
        Check(wheel.Step(600,true,true,10,out play)==1&&!play,"multi-notch map input must advance only one step");
        Check(wheel.Step(-120,true,true,259,out play)==0,"map cooldown must include 249 ms");
        Check(wheel.Step(-360,true,true,260,out play)==-1,"map cooldown must end at exactly 250 ms");
        Check(wheel.Step(60,true,true,300,out play)==0&&wheel.Step(0,true,true,510,out play)==0,"discard throttled partial movement without delayed steps");
        Check(wheel.Step(60,true,true,511,out play)==0&&wheel.Step(60,true,true,512,out play)==1,"high-resolution wheel must accumulate one map notch outside cooldown");
        Check(wheel.Step(60,false,false,520,out play)==0&&wheel.Step(60,true,true,800,out play)==0,"modifier change must not reuse the other zoom's partial notch");
        wheel.ResetRemainder();Check(wheel.Step(60,true,true,801,out play)==0,"leaving the surface must discard partial notches");
        Check(wheel.Step(120,false,true,810,out play)==1&&play,"map rate limit must never throttle play zoom");
        return 11;
    }
    static int CaptureChecks()
    {
        const uint image=0x400000,pane=0x1000,backing=0x2000,lightObject=0x4000,lightBacking=0x5000;
        var memory=new byte[0x340000];var root=new byte[96];var output=new byte[96];var protect=new byte[48];bool replace=false;
        Action<uint,uint> put=(at,value)=>Array.Copy(BitConverter.GetBytes(value),0,memory,(int)at,4);
        put(0x33d960,image+pane+0x2e8);put(0x2d8fdc,1);put(pane,image+0x28c054);put(pane+0x6c,4);put(pane+0xe8,64);put(pane+0xec,34);put(pane+0x9c,128);put(pane+0x98,image+backing);
        put(pane+0x12c,unchecked((uint)-28));put(pane+0x128,unchecked((uint)-14));put(pane+0x1e4,image+lightObject);put(pane+0x200,32);
        put(pane+0x2e4,unchecked((uint)-28));put(pane+0x2e0,unchecked((uint)-14));put(pane+0x2d4,28);put(pane+0x2d0,14);put(pane+0x2dc,36);put(pane+0x2d8,20);
        put(lightObject+8,image+lightBacking);put(lightObject+0x10,64);put(lightObject+0x14,34);put(lightObject+0x18,64);
        for(int i=0;i<48;i++){root[i*2]=31;output[i*2]=123;}
        for(int i=0;i<64*34;i++){memory[backing+16+i*2+1]=248;memory[lightBacking+i]=16;}
        protect[18]=1;
        var capture=new WorldZoomCapture((at,buffer,length)=>{Array.Copy(memory,(int)(at-image),buffer,0,length);if(replace&&at==image+backing+16)put(pane+0x98,image+backing+8);},image,8,6);
        Check(capture.Capture(output,root,16,protect)&&capture.Map==new Rectangle(0,0,8,6),"world canvas margins / source coordinates");
        Check(output[36]==0&&output[37]==248&&output[0]==31&&output[1]==0,"UI must be removed from underlay while completed world effects remain exact");
        put(pane+0x1e8,1);put(pane+0x200,16);
        Check(capture.Capture(output,root,16,protect)&&output[37]==120,"night light plane behind UI");
        put(pane+0x12c,unchecked((uint)-25));put(pane+0x128,unchecked((uint)-11));
        Check(capture.Capture(output,root,16,protect)&&capture.Map==new Rectangle(0,0,8,6),"walking must not move the zoom rectangle");
        replace=true;Check(!capture.Capture(output,root,16,protect)&&capture.Map.IsEmpty,"changing world backing must reject the zoom frame");
        return 5;
    }
    internal static void Probe()
    {
        var candidates=GameSessions.Running();if(candidates.Count==0)throw new InvalidOperationException("No isolated client to inspect.");
        for(int i=1;i<candidates.Count;i++)candidates[i].Dispose();
        using(var process=candidates[0])using(var frame=new GameFrame(process))
        using(var fog=new FogComposition())using(var worldFog=new FogComposition())
        using(var scale=new PixelEdgeScaler())using(var painter=new WorldZoomPainter())
        {
            frame.WorldZoomRequested=true;frame.GuardRedraws=true;
            var watch=System.Diagnostics.Stopwatch.StartNew();int previous=0,drawn=0;double total=0,max=0;
            while(watch.ElapsedMilliseconds<5000)
            {
                frame.Update();
                if(frame.Frames!=previous&&frame.WorldReady)
                {
                    previous=frame.Frames;var timing=System.Diagnostics.Stopwatch.StartNew();
                    painter.PaintNative(fog.Apply(frame,12,true,68,true),worldFog.Apply(frame,12,true,68,true,true),frame.WorldRectangle,150,frame.ProtectedPixels,new Size(1715,1009));
                    timing.Stop();drawn++;total+=timing.Elapsed.TotalMilliseconds;max=Math.Max(max,timing.Elapsed.TotalMilliseconds);
                }
                System.Threading.Thread.Sleep(5);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Program.Root,"build","zoom-probe.json"),Program.Json.Serialize(new {
                status=drawn>0?"passed":"no_world_frame",frames=drawn,captured=frame.CapturedFrames,world_ready=frame.WorldReady,
                map=new[]{frame.WorldRectangle.X,frame.WorldRectangle.Y,frame.WorldRectangle.Width,frame.WorldRectangle.Height},
                composition_mean_ms=drawn==0?0:Math.Round(total/drawn,2),composition_max_ms=Math.Round(max,2),
                misses=frame.LayerMisses,capture_error=frame.ReadStatus,last_retry=frame.LayerFailure,
                note="Read-only 150% world zoom with 68% fading fog; no frames or input saved."}));
            if(drawn==0)throw new InvalidOperationException("No stable world frame was available for the zoom probe.");
        }
    }
}
