using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Frame capture is read-only. Optional Tab-map settings have a separate,
// narrowly scoped writer in GameTools.cs; no frames or input are recorded.
internal sealed class GameFrame : IDisposable
{
    readonly IntPtr handle;
    readonly uint image;
    readonly int[] colors = new int[65536];
    byte[] source;
    readonly FrameMailbox mailbox;
    readonly NativeLayerMask layers;
    readonly WorldZoomCapture worldCapture;
    internal volatile bool WorldZoomRequested,ExtraWorldRequested,StandardMiniMapRequested,WorldUnderlayRequested;
    internal bool TileGridEnabled;
    internal int GridPixels {get{return worldCapture.GridPixels;}}
    internal string GridStatus {get{return worldCapture.GridStatus;}}
    internal MapScene Scene=MapScene.Empty;
    internal readonly TabMapLayer TabLayer=new TabMapLayer();
    byte[] worldSource;
    internal int[] WorldPixels;
    GCHandle worldPin;
    internal Bitmap WorldBitmap;
    internal Rectangle WorldRectangle;
    internal NativeWheelScene WheelScene=NativeWheelScene.Empty;
    internal bool WorldReady;
    internal volatile bool FogLayersRequested;
    internal byte[] ProtectedPixels;
    internal bool LayersReady;
    internal Rectangle TravelMap;
    internal readonly bool HasFrameSequence;
    internal int LayerMisses;
    readonly byte[] previewPixels;
    Rectangle previewRectangle;
    uint previewMap;
    long nextPreview;
    internal string LayerFailure;
    internal int[] NativePixels {get{return pixels;}}
    readonly ManualResetEvent captureStop=new ManualResetEvent(false);
    Thread captureThread;
    Exception captureError;
    int skippedFrames;
    internal int SkippedFrames {get{return skippedFrames;}}
    internal int CapturedFrames, MaxCaptureGapMilliseconds;
    internal volatile bool GuardRedraws;
    internal volatile bool Paused;
    internal string ReadStatus {get{return captureError==null?null:captureError.Message;}}
    int[] pixels;
    GCHandle pin;
    public readonly int Width, Height, ProcessId;
    public readonly IntPtr Window;
    public Bitmap Bitmap;
    public bool InWorld, Compact;
    internal bool IsInWorldNow {get{return U32(image+0x33d960)!=0;}}
    public int Frames;
    internal GameFrame(Process process)
    {
        Program.VerifyHostedGame(process.MainModule.FileName);
        ProcessId = process.Id;
        image = unchecked((uint)process.MainModule.BaseAddress.ToInt32());
        handle = HostNative.OpenProcess(0x0410, false, process.Id);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            // Revision 10 installs an ASLR-safe sequence counter around world
            // update and final composition. Earlier clients retain flag checks.
            byte[] call=new byte[5];Read(image+0x1540d3,call,5);
            HasFrameSequence=call[0]==0xe8 && 0x1540d8+BitConverter.ToInt32(call,1)>=0x268680 &&
                0x1540d8+BitConverter.ToInt32(call,1)<0x268800;
            uint video = U32(image + 0x2da3a4);
            if (video == 0) throw new InvalidOperationException("The client is still starting.");
            Width = checked((int)U32(video + 0x1c));
            Height = checked((int)U32(video + 0x20));
            if (Width < 640 || Width > 1920 || Height < 480 || Height > 1080)
                throw new InvalidOperationException("Unexpected canvas dimensions.");
            uint canvas = U32(video + 0x24);
            uint mode = canvas == 0 ? 0 : U32(canvas + 0x6c);
            if (mode != 4 && mode != 3)
                throw new InvalidOperationException("Unexpected main canvas backing.");
            Window = new IntPtr(unchecked((int)U32(video + 0x3c)));
            uint owner;
            HostNative.GetWindowThreadProcessId(Window, out owner);
            if (owner != process.Id) throw new InvalidOperationException("Game window does not match its process.");
            bool rgb565 = U32(image + 0x2d8fdc) != 0;
            for (int i = 0; i < colors.Length; ++i)
            {
                int r = (i >> (rgb565 ? 11 : 10)) & 31, g = (i >> 5) & (rgb565 ? 63 : 31), b = i & 31;
                colors[i] = unchecked((int)0xff000000) | (r * 255 / 31 << 16) |
                    (g * 255 / (rgb565 ? 63 : 31) << 8) | b * 255 / 31;
            }
            source = new byte[Width * Height * 4];
            mailbox = new FrameMailbox(source.Length);
            layers = new NativeLayerMask(Read,image,Width,Height);
            worldCapture=new WorldZoomCapture(Read,image,Width,Height);
            worldSource=new byte[Width*Height*2];WorldPixels=new int[Width*Height];
            previewPixels=new byte[worldSource.Length];
            worldPin=GCHandle.Alloc(WorldPixels,GCHandleType.Pinned);
            WorldBitmap=new Bitmap(Width,Height,Width*4,PixelFormat.Format32bppRgb,worldPin.AddrOfPinnedObject());
            ProtectedPixels=new byte[Width*Height];
            pixels = new int[Width * Height];
            pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            Bitmap = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject());
            // The UI never waits for DA to finish a redraw. A separate reader
            // catches completed frames between redraws and publishes the latest.
            captureThread=new Thread(CaptureLoop){IsBackground=true,Name="DA frame reader"};
            captureThread.Start();
        }
        catch { Dispose(); throw; }
    }
    internal uint U32(uint address)
    {
        byte[] value = new byte[4];
        Read(address, value, 4);
        return BitConverter.ToUInt32(value, 0);
    }
    internal void Read(uint address, byte[] buffer, int count)
    {
        if (address < 0x10000 || count < 1 || count > 1920 * 1080 * 4)
            throw new InvalidOperationException("Invalid frame read.");
        IntPtr read;
        if (!HostNative.ReadProcessMemory(handle, new IntPtr(unchecked((int)address)), buffer, new IntPtr(count), out read) ||
            read.ToInt32() != count) throw new InvalidOperationException("The client frame is temporarily unavailable.");
    }
    void CaptureLoop()
    {
        var clock=Stopwatch.StartNew();long previous=0;
        while(!captureStop.WaitOne(0))
        {
            if(Paused){captureStop.WaitOne(100);previous=clock.ElapsedMilliseconds;continue;}
            bool captured=false;
            try
            {
                captured=Capture();
                if(captured)
                {
                    long now=clock.ElapsedMilliseconds;
                    MaxCaptureGapMilliseconds=Math.Max(MaxCaptureGapMilliseconds,(int)(now-previous));previous=now;
                    CapturedFrames++;
                }
                captureError=null;
            }
            catch(Exception ex){captureError=ex;}
            // Retry briefly on the worker, never spin or sleep on the UI thread.
            if(captureStop.WaitOne(captured?8:1))break;
        }
    }
    bool Capture()
    {
        // ScreenPane brackets dirty-tree drawing (5540EF..554143). A single
        // bounded copy avoids the old two-identical-frames starvation while
        // retaining before/after drawing and backing checks.
        bool extrasRequested=ExtraWorldRequested&&HasFrameSequence;
        // Minimap coordinates do not require reconstructing the whole world.
        // In particular, a moving cache must never reject a normal game frame
        // just because an optional minimap sample could not be read.
        bool zoomRequested=(WorldZoomRequested||WorldUnderlayRequested||TileGridEnabled)&&HasFrameSequence;
        bool guard=GuardRedraws||zoomRequested||FogLayersRequested;
        uint sequence=guard&&HasFrameSequence?U32(image+0x4e241c):0;
        if((sequence&1)!=0){Interlocked.Increment(ref skippedFrames);return false;}
        if(guard && U32(image+0x367b70)!=0){Interlocked.Increment(ref skippedFrames);return false;}
        // Login, map transitions and surface recovery may replace the canvas.
        // Resolve it on every sample and recheck the whole ownership chain.
        uint video=U32(image+0x2da3a4),canvas=U32(video+0x24);
        uint mode=U32(canvas+0x6c);
        if(mode!=3 && mode!=4)throw new InvalidOperationException("The main canvas is still initializing.");
        int pitch = checked((int)U32(canvas + (mode == 4 ? 0x9cu : 0xccu)));
        if (pitch < Width * 2 || pitch > Width * 4 || pitch % 4 != 0 || U32(canvas + 0xe8) != Width || U32(canvas + 0xec) != Height)
            throw new InvalidOperationException("Waiting for canvas: "+U32(canvas+0xe8)+"x"+U32(canvas+0xec)+", pitch "+pitch+" (expected "+Width+"x"+Height+")");
        // render_canvas_lock_backing (0x44B820) skips the allocation's
        // 16-byte leading guard area for mode 4, retaining +0x9C as pitch.
        // Mode 3 retains the last successful surface lock pointer and pitch.
        // Copy only the validated frame extent; discard if backing changes.
        uint address = mode == 4 ? U32(canvas + 0x98) + 0x10 : U32(canvas + 0xc8);
        bool stable=FrameSnapshot.TryRead(mailbox.Writing,pitch*Height,
            delegate{return guard && U32(image+0x367b70)!=0;},
            delegate(byte[] target,int length){Read(address,target,length);},
            delegate{return U32(image+0x2da3a4)==video && U32(video+0x24)==canvas && U32(canvas+0xe8)==Width && U32(canvas+0xec)==Height && U32(canvas+0x6c)==mode &&
                (mode==4?U32(canvas+0x98)+0x10==address && U32(canvas+0x9c)==pitch:
                         U32(canvas+0xc8)==address && U32(canvas+0xcc)==pitch);});
        if(!stable){Interlocked.Increment(ref skippedFrames);return false;}
        if(guard && (U32(image+0x367b70)!=0 || (HasFrameSequence&&!FrameSnapshot.SequenceStable(sequence,U32(image+0x4e241c)))))
        {Interlocked.Increment(ref skippedFrames);return false;}
        bool world=U32(image+0x33d960)!=0;
        bool requested=(FogLayersRequested||zoomRequested) && world;
        uint currentMap=world?U32(image+0x33d960):0;
        if(currentMap!=previewMap){previewMap=currentMap;previewRectangle=Rectangle.Empty;}
        bool previewDue=extrasRequested && !zoomRequested && world && DateTime.UtcNow.Ticks>=nextPreview;
        if(previewDue)nextPreview=DateTime.UtcNow.AddMilliseconds(100).Ticks;
        bool protectedLayers=false;
        mailbox.TravelMapWriting=Rectangle.Empty;
        mailbox.WorldMapWriting=Rectangle.Empty;
        mailbox.WheelWriting=NativeWheelScene.Empty;mailbox.SceneWriting=MapScene.Empty;
        if(extrasRequested && !zoomRequested && !previewRectangle.IsEmpty)
        {Buffer.BlockCopy(previewPixels,0,mailbox.WorldWriting,0,previewPixels.Length);mailbox.WorldMapWriting=previewRectangle;}
        if(U32(image+0x33d944)!=0)
        {
            try {protectedLayers=layers.Capture(mailbox.ProtectionWriting,requested||previewDue,WorldUnderlayRequested);}
            catch(Exception ex) {protectedLayers=false;LayerFailure=ex.Message;}
            if(!protectedLayers)
            {
                LayerMisses++;if(layers.Failure!=null)LayerFailure=layers.Failure;
                // A failed mask is NOT an unfogged frame. Keep the last complete
                // frame/mask pair and retry on the worker after 1 ms. This never
                // waits for the world renderer or blocks the host/input thread.
                if(requested)return false;
            }
            if(protectedLayers){mailbox.TravelMapWriting=layers.TravelMap;
            mailbox.WheelWriting=new NativeWheelScene(layers.WorldArea,layers.InputRegions.ToArray());}
            if((zoomRequested||previewDue)&&protectedLayers&&layers.WorldVisible&&layers.TravelMap.IsEmpty)
            {
                if(!worldCapture.Capture(mailbox.WorldWriting,mailbox.Writing,pitch,mailbox.ProtectionWriting,extrasRequested,StandardMiniMapRequested,TileGridEnabled))
                {LayerMisses++;LayerFailure="world backing changed";if(zoomRequested)return false;
                    if(!previewRectangle.IsEmpty)Buffer.BlockCopy(previewPixels,0,mailbox.WorldWriting,0,previewPixels.Length);}
                else {
                    mailbox.WorldMapWriting=worldCapture.Map;mailbox.SceneWriting=worldCapture.Scene;
                    if(previewDue){Buffer.BlockCopy(mailbox.WorldWriting,0,previewPixels,0,previewPixels.Length);previewRectangle=worldCapture.Map;}
                }
            }
        }
        if(requested && guard && (U32(image+0x367b70)!=0 || (HasFrameSequence&&!FrameSnapshot.SequenceStable(sequence,U32(image+0x4e241c)))))
        {Interlocked.Increment(ref skippedFrames);return false;}
        if(extrasRequested && !zoomRequested && world)
        {
            // This bounded metadata read contains the game's own interpolation
            // offsets. Failure only omits this minimap sample.
            try { mailbox.SceneWriting=worldCapture.CaptureScene(); }
            catch { mailbox.SceneWriting=MapScene.Empty; }
        }
        return mailbox.PublishComplete(pitch,requested,protectedLayers,protectedLayers&&layers.WorldVisible);
    }
    internal void Update()
    {
        int pitch;
        if(!mailbox.Take(ref source,ref ProtectedPixels,ref worldSource,out pitch,out LayersReady))
        {
            // Keep the session and the last valid frame during a temporary
            // backing transition; never detach a game because a read missed.
            return;
        }
        FramePixels.Convert(source,pitch,Width,Height,colors,pixels);
        WorldRectangle=mailbox.WorldMapReading;WorldReady=!WorldRectangle.IsEmpty;
        if(WorldReady)FramePixels.Convert(worldSource,Width*2,Width,Height,colors,WorldPixels);
        Scene=mailbox.SceneReading;
        TabLayer.Extract(pixels,WorldPixels,ProtectedPixels,Width,Height,WorldReady&&LayersReady);
        // Grid pixels are now prepared against the native ground cache on the
        // capture worker; never paint a grid over the completed foreground.
        TravelMap=mailbox.TravelMapReading;
        WheelScene=mailbox.WheelReading;
        if(!TravelMap.IsEmpty)FramePixels.BlackOutside(pixels,Width,Height,TravelMap);
        uint map = U32(image + 0x33d960);
        InWorld = map != 0;
        if (InWorld)
        {
            uint table = U32(map);
            if (U32(table + 0x4c) != image + 0x1f9b20) throw new InvalidOperationException("Unexpected world interface.");
            uint world = map - 0x2e8;
            int contentHeight = unchecked((int)U32(world + 0x2d8)) - unchecked((int)U32(world + 0x2d0));
            Compact = contentHeight == Height - 56;
        }
        Frames++;
    }
    public void Dispose()
    {
        captureStop.Set();
        if(captureThread!=null){captureThread.Join();captureThread=null;}
        TabLayer.Dispose();
        if(WorldBitmap!=null){WorldBitmap.Dispose();WorldBitmap=null;}if(worldPin.IsAllocated)worldPin.Free();
        if (Bitmap != null) { Bitmap.Dispose(); Bitmap = null; }
        if (pin.IsAllocated) pin.Free();
        if (handle != IntPtr.Zero) HostNative.CloseHandle(handle);
    }
}

internal static class FrameSnapshot
{
    internal static bool SequenceStable(uint before,uint after){return (before&1)==0 && before==after;}
    internal static bool TryRead(byte[] target,int count,Func<bool> busy,Action<byte[],int> read,Func<bool> sameBacking)
    {
        if(count<1 || count>target.Length)throw new ArgumentOutOfRangeException("count");
        if(busy())return false;
        read(target,count);
        return !busy() && sameBacking();
    }
}

// Three buffers: worker-owned writing, pending, and UI-owned reading. Exchange
// references under a tiny lock; never read/convert/paint while holding the lock.
internal sealed class FrameMailbox
{
    internal NativeWheelScene WheelWriting=NativeWheelScene.Empty,WheelReading=NativeWheelScene.Empty;
    NativeWheelScene pendingWheel=NativeWheelScene.Empty;
    internal MapScene SceneWriting=MapScene.Empty,SceneReading=MapScene.Empty;
    MapScene pendingScene=MapScene.Empty;
    internal byte[] Writing;
    internal byte[] ProtectionWriting;
    byte[] pendingProtection;
    bool pendingLayers;
    internal Rectangle TravelMapWriting,TravelMapReading;
    internal byte[] WorldWriting;
    byte[] pendingWorld;
    internal Rectangle WorldMapWriting,WorldMapReading;
    Rectangle pendingWorldMap;
    Rectangle pendingMap;
    byte[] pending;
    bool ready;
    int pendingPitch;
    readonly object gate=new object();
    internal FrameMailbox(int length){Writing=new byte[length];pending=new byte[length];ProtectionWriting=new byte[length/4];pendingProtection=new byte[length/4];WorldWriting=new byte[length/2];pendingWorld=new byte[length/2];}
    internal bool PublishComplete(int pitch,bool requested,bool valid,bool worldVisible)
    {
        if(requested&&!valid)return false;
        Publish(pitch,requested&&worldVisible);return true;
    }
    internal void Publish(int pitch,bool layers=false)
    {
        lock(gate){byte[] old=pending;pending=Writing;Writing=old;old=pendingProtection;pendingProtection=ProtectionWriting;ProtectionWriting=old;old=pendingWorld;pendingWorld=WorldWriting;WorldWriting=old;pendingWorldMap=WorldMapWriting;pendingLayers=layers;pendingPitch=pitch;pendingMap=TravelMapWriting;pendingWheel=WheelWriting;pendingScene=SceneWriting;ready=true;}
    }
    internal bool Take(ref byte[] target,ref byte[] protection,ref byte[] world,out int pitch,out bool layers)
    {
        lock(gate)
        {
            pitch=pendingPitch;layers=pendingLayers;if(!ready)return false;
            TravelMapReading=pendingMap;WorldMapReading=pendingWorldMap;WheelReading=pendingWheel;SceneReading=pendingScene;
            byte[] old=target;target=pending;pending=old;old=protection;protection=pendingProtection;pendingProtection=old;
            old=world;world=pendingWorld;pendingWorld=old;ready=false;return true;
        }
    }
    internal bool Take(ref byte[] target,ref byte[] protection,out int pitch,out bool layers)
    {
        lock(gate)
        {
            pitch=pendingPitch;layers=pendingLayers;if(!ready)return false;
            TravelMapReading=pendingMap;WheelReading=pendingWheel;SceneReading=pendingScene;
            byte[] old=target;target=pending;pending=old;old=protection;protection=pendingProtection;pendingProtection=old;ready=false;return true;
        }
    }
    internal bool Take(ref byte[] target,out int pitch)
    {
        lock(gate)
        {
            pitch=pendingPitch;if(!ready)return false;
            WheelReading=pendingWheel;SceneReading=pendingScene;byte[] old=target;target=pending;pending=old;ready=false;return true;
        }
    }
}

internal static class FramePixels
{
    internal static void BlackOutside(int[] pixels,int width,int height,Rectangle map)
    {
        Rectangle keep=Rectangle.Intersect(new Rectangle(0,0,width,height),map);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            if(!keep.Contains(x,y))pixels[y*width+x]=unchecked((int)0xff000000);
    }
    internal static unsafe void Convert(byte[] source,int pitch,int width,int height,int[] colors,int[] pixels)
    {
        if(width<1 || height<1 || pitch<width*2 || source.Length<(long)pitch*height || pixels.Length<(long)width*height || colors.Length!=65536)
            throw new ArgumentException("Invalid frame conversion bounds.");
        fixed(byte* input=source)
        fixed(int* output=pixels)
        fixed(int* palette=colors)
        {
            // Software pixels stay 16-bit inside padded DirectDraw rows.
            // VideoSystem+0x30 describes desktop output, not these pixels.
            for(int y=0;y<height;++y) {
                ushort* row=(ushort*)(input+y*pitch);
                for(int x=0;x<width;++x)output[y*width+x]=palette[row[x]];
            }
        }
    }
}

internal static class ViewGeometry
{
    internal static Rectangle Fit(Size available, Size logical, bool stretch, bool actual, bool integer = false)
    {
        if (available.Width < 1 || available.Height < 1) return Rectangle.Empty;
        double scale = Math.Min((double)available.Width / logical.Width, (double)available.Height / logical.Height);
        if (actual) scale = Math.Min(1, scale);
        // Fractional nearest-neighbor repeats some columns more than others.
        // Whole-number enlargement keeps every source pixel the same size.
        if (integer && scale >= 1) scale = Math.Floor(scale);
        if (integer) stretch = false;
        int w = stretch ? available.Width : Math.Max(1, (int)Math.Floor(logical.Width * scale));
        int h = stretch ? available.Height : Math.Max(1, (int)Math.Floor(logical.Height * scale));
        return new Rectangle((available.Width - w) / 2, (available.Height - h) / 2, w, h);
    }
    internal static Point ToGame(Point point, Rectangle view, Size logical)
    {
        return new Point(Math.Max(0, Math.Min(logical.Width - 1, (point.X - view.X) * logical.Width / Math.Max(1, view.Width))),
                         Math.Max(0, Math.Min(logical.Height - 1, (point.Y - view.Y) * logical.Height / Math.Max(1, view.Height))));
    }
    internal static Rectangle Dock(Rectangle view, Size logical, bool compact)
    {
        int left = view.Left + (int)Math.Ceiling(640.0 * view.Width / logical.Width);
        int top = view.Top + (int)Math.Ceiling((logical.Height - (compact ? 72 : 172) + 3.0) * view.Height / logical.Height);
        return Rectangle.FromLTRB(left, top, view.Right, view.Bottom);
    }
}

internal enum FrameFilter { Bilinear, Bicubic, IntegerPixels, PixelEdges }

// Exact box coverage for enlargement: a destination pixel receives only the
// source pixel areas it actually covers. Interior colors remain untouched;
// only fractional boundaries blend. Unlike ordinary bilinear, the transition
// stays one screen pixel wide. This is a CPU pixel-art AA path, not a GPU shader.
internal sealed class PixelEdgeScaler : IDisposable
{
    Bitmap output;
    int sourceWidth, sourceHeight;
    RectangleF sourceArea;
    int[] x0,x1,xWeight,y0,y1,yWeight;
    internal static void Axis(int source,int target,out int[] a,out int[] b,out int[] weight)
    {
        a=new int[target];b=new int[target];weight=new int[target];
        for(int i=0;i<target;i++)
        {
            // Integer rational coordinates prevent drift at exact boundaries.
            long start=(long)i*source,end=(long)(i+1)*source;
            a[i]=(int)(start/target);b[i]=Math.Min(source-1,(int)((end-1)/target));
            weight[i]=a[i]==b[i]?0:(int)((end-(long)b[i]*target)*256/source);
        }
    }
    internal static uint Mix(uint a,uint b,int weight)
    {
        uint w=(uint)weight,other=256-w;
        return 0xff000000u | ((((a&0x00ff00ffu)*other+(b&0x00ff00ffu)*w)>>8)&0x00ff00ffu) |
            ((((a&0x0000ff00u)*other+(b&0x0000ff00u)*w)>>8)&0x0000ff00u);
    }
    internal static void CropAxis(int source,int target,double start,double length,out int[] a,out int[] b,out int[] weight)
    {
        a=new int[target];b=new int[target];weight=new int[target];
        for(int i=0;i<target;i++)
        {
            double left=start+i*length/target,right=start+(i+1)*length/target;
            a[i]=Math.Max(0,Math.Min(source-1,(int)Math.Floor(left)));
            b[i]=Math.Max(0,Math.Min(source-1,(int)Math.Ceiling(right)-1));
            weight[i]=a[i]==b[i]?0:Math.Max(0,Math.Min(256,(int)Math.Round((right-b[i])*256/(right-left))));
        }
    }
    internal Bitmap Scale(Bitmap source,int width,int height){return Scale(source,width,height,new RectangleF(0,0,source.Width,source.Height));}
    internal unsafe Bitmap Scale(Bitmap source,int width,int height,RectangleF area)
    {
        if(area.Width<=0||area.Height<=0||area.Left<0||area.Top<0||area.Right>source.Width||area.Bottom>source.Height||width<area.Width||height<area.Height)throw new ArgumentException("Pixel-edge coverage requires a valid enlarged crop.");
        if(output==null||output.Width!=width||output.Height!=height||sourceWidth!=source.Width||sourceHeight!=source.Height||sourceArea!=area)
        {
            Dispose();output=new Bitmap(width,height,PixelFormat.Format32bppRgb);
            sourceWidth=source.Width;sourceHeight=source.Height;sourceArea=area;
            if(area==new RectangleF(0,0,source.Width,source.Height))
            {Axis(sourceWidth,width,out x0,out x1,out xWeight);Axis(sourceHeight,height,out y0,out y1,out yWeight);}
            else {CropAxis(sourceWidth,width,area.Left,area.Width,out x0,out x1,out xWeight);CropAxis(sourceHeight,height,area.Top,area.Height,out y0,out y1,out yWeight);}
        }
        BitmapData input=null,result=null;
        try
        {
            input=source.LockBits(new Rectangle(0,0,source.Width,source.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
            result=output.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
            for(int y=0;y<height;y++)
            {
                uint* row0=(uint*)((byte*)input.Scan0+y0[y]*input.Stride),row1=(uint*)((byte*)input.Scan0+y1[y]*input.Stride);
                uint* dest=(uint*)((byte*)result.Scan0+y*result.Stride);
                int wy=yWeight[y];
                for(int x=0;x<width;x++)
                {
                    uint top=xWeight[x]==0?row0[x0[x]]:Mix(row0[x0[x]],row0[x1[x]],xWeight[x]);
                    if(wy==0)dest[x]=top;
                    else {
                        uint bottom=xWeight[x]==0?row1[x0[x]]:Mix(row1[x0[x]],row1[x1[x]],xWeight[x]);
                        dest[x]=Mix(top,bottom,wy);
                    }
                }
            }
        }
        finally {if(input!=null)source.UnlockBits(input);if(result!=null)output.UnlockBits(result);}
        return output;
    }
    public void Dispose(){if(output!=null){output.Dispose();output=null;}}
}

internal static class FramePainter
{
    internal static void Draw(Graphics graphics, Bitmap bitmap, Rectangle view, FrameFilter filter,RectangleF? crop=null)
    {
        bool wholePixels = !crop.HasValue && view.Width % bitmap.Width == 0 && view.Height % bitmap.Height == 0 &&
            view.Width / bitmap.Width == view.Height / bitmap.Height;
        // At native size or integer magnification no filtering is necessary.
        graphics.InterpolationMode = wholePixels ? InterpolationMode.NearestNeighbor :
            filter == FrameFilter.Bicubic ? InterpolationMode.HighQualityBicubic : InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using (var attributes = new ImageAttributes())
        {
            // Clamp the edge by mirroring, avoiding a dark fringe from sampling
            // transparent pixels just outside the source image.
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            RectangleF area=crop??new RectangleF(0,0,bitmap.Width,bitmap.Height);
            graphics.DrawImage(bitmap, view, area.Left,area.Top,area.Width,area.Height, GraphicsUnit.Pixel, attributes);
        }
    }
}

internal sealed class NativeGameCursor : IDisposable
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr LoadImageW(IntPtr instance,string path,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr cursor);
    IntPtr ownedHandle;
    internal readonly Cursor Value;
    internal bool Loaded { get { return ownedHandle!=IntPtr.Zero; } }
    internal NativeGameCursor(string path)
    {
        // Cursor(string) passes through the legacy monochrome decoder. Load a
        // native color HCURSOR instead, retaining the asset's size and hotspot.
        ownedHandle=File.Exists(path)?LoadImageW(IntPtr.Zero,path,2,0,0,0x10):IntPtr.Zero;
        Value=Loaded?new Cursor(ownedHandle):Cursors.Arrow;
    }
    public void Dispose()
    {
        if(ownedHandle==IntPtr.Zero)return;
        // Cursor(IntPtr) borrows the handle; LR_LOADFROMFILE gives us ownership.
        Value.Dispose();DestroyCursor(ownedHandle);ownedHandle=IntPtr.Zero;
    }
}

internal sealed class GameSurface : Control
{
    internal GameFrame Frame;
    internal Rectangle View;
    internal bool Stretch, Actual;
    internal bool DimFog;
    internal int FogRange=12;
    internal int FogOpacity=30;
    internal bool FogSquare=true, MapOverlayVisible;
    internal bool FogFade=true;
    internal int TabTransparency;
    readonly FogComposition fog=new FogComposition();
    readonly FogComposition worldFog=new FogComposition();
    readonly WorldZoomPainter worldPainter=new WorldZoomPainter();
    internal int WorldZoomPercent=100;
    internal int DisplayedWorldZoom {get{return paintedZoom;}}
    int paintedZoom=100,paintedPid;
    Rectangle paintedMap;
    NativeWheelScene paintedWheel=NativeWheelScene.Empty;
    Size paintedSize;
    byte[] inputProtection=new byte[0];
    Bitmap filteredBase;
    Point lastMousePoint;
    string presentationKey;
    internal bool UseGameCursor;
    readonly NativeGameCursor gameCursor=new NativeGameCursor(Path.Combine(Program.Root,"theme","game.cur"));
    readonly ZoomWheelInput zoomWheel=new ZoomWheelInput();
    readonly Stopwatch wheelClock=Stopwatch.StartNew();
    internal FrameFilter Filter = FrameFilter.Bilinear;
    readonly PixelEdgeScaler pixelEdges = new PixelEdgeScaler();
    Bitmap scaledFrame;
    int scaledNumber=-1;
    readonly Dictionary<int,int> heldKeys = new Dictionary<int,int>();
    bool screenshotHeld;
    MouseButtons heldMouse;
    bool changingCapture;
    internal int MouseDownCount, MouseUpCount, CaptureLossCount;
    internal event EventHandler GeometryChanged;
    internal event EventHandler ManualInput;
    internal event EventHandler ScreenshotRequested;
    internal Func<Keys,bool> TryHotkey;
    readonly HotkeyInputGate hotkeyInput=new HotkeyInputGate();
    internal bool ShortcutHeld {get{return hotkeyInput.Held || ModifierKeys!=Keys.None;}}
    internal event Action<int> MapZoom;
    internal event Action<int> PlayZoom;
    internal event EventHandler ToggleFog;
    internal GameSurface()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Arrow;
        BackColor = Color.FromArgb(15, 16, 19);
    }
    internal void Arrange()
    {
        scaledNumber=-1;
        Size size = PresentationSize;
        View = ViewGeometry.Fit(ClientSize, size, Frame!=null&&Frame.InWorld&&Stretch, Frame!=null&&Frame.InWorld&&Actual, Filter == FrameFilter.IntegerPixels);
        if (GeometryChanged != null) GeometryChanged(this, EventArgs.Empty);
        Invalidate();
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
    internal bool LockZoom;
    internal Size PresentationSize {get{return Frame==null||!Frame.InWorld?new Size(640,480):new Size(Frame.Width,Frame.Height);}}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Frame == null || Frame.Bitmap == null)
        {
            paintedZoom=100;paintedPid=0;paintedWheel=NativeWheelScene.Empty;
            TextRenderer.DrawText(e.Graphics, "Choose a viewport, then launch Dark Ages.\n\nDisplay modes and library placement can change while you play.\nChanging the world viewport restarts the test client.",
                Font, ClientRectangle, Color.Silver, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        if(!Frame.InWorld)
        {
            var area=new RectangleF(0,0,640,480);
            if(Filter==FrameFilter.PixelEdges&&View.Width>=640&&View.Height>=480)e.Graphics.DrawImageUnscaled(pixelEdges.Scale(Frame.Bitmap,View.Width,View.Height,area),View.Location);
            else FramePainter.Draw(e.Graphics,Frame.Bitmap,View,Filter,area);
            paintedZoom=100;paintedPid=Frame.ProcessId;paintedSize=View.Size;paintedWheel=NativeWheelScene.Empty;return;
        }
        bool dim=DimFog&&FogOpacity>0&&Frame.InWorld&&Frame.LayersReady&&Frame.TravelMap.IsEmpty;
        Frame.FogLayersRequested=DimFog&&FogOpacity>0;
        Frame.WorldZoomRequested=WorldZoomPercent>100;
        bool zoom=WorldZoomPercent>100&&Frame.InWorld&&Frame.WorldReady&&Frame.LayersReady&&Frame.TravelMap.IsEmpty;
        string key=Frame.ProcessId+":"+dim+":"+FogRange+":"+FogSquare+":"+FogOpacity+":"+FogFade+":"+WorldZoomPercent+":"+zoom;
        if(key!=presentationKey){scaledNumber=-1;presentationKey=key;}
        Bitmap displayImage=dim?fog.Apply(Frame,FogRange,FogSquare,FogOpacity,FogFade):Frame.Bitmap;
        if(zoom&&(Filter==FrameFilter.PixelEdges||Filter==FrameFilter.IntegerPixels)&&View.Width>=Frame.Width&&View.Height>=Frame.Height)
        {
            Bitmap terrain=dim?worldFog.Apply(Frame,FogRange,FogSquare,FogOpacity,FogFade,true):Frame.WorldBitmap;
            e.Graphics.DrawImageUnscaled(worldPainter.PaintNative(displayImage,terrain,Frame.WorldRectangle,WorldZoomPercent,Frame.ProtectedPixels,View.Size),View.Location);
        }
        else if(Filter==FrameFilter.PixelEdges && View.Width>=Frame.Width && View.Height>=Frame.Height)
        {
            if(scaledFrame==null || scaledNumber!=Frame.Frames || scaledFrame.Width!=View.Width || scaledFrame.Height!=View.Height)
            {scaledFrame=pixelEdges.Scale(displayImage,View.Width,View.Height);scaledNumber=Frame.Frames;}
            if(zoom)PaintZoom(e.Graphics,scaledFrame,dim);else e.Graphics.DrawImageUnscaled(scaledFrame,View.Location);
        }
        else if(zoom)
        {
            if(filteredBase==null||filteredBase.Size!=View.Size){if(filteredBase!=null)filteredBase.Dispose();filteredBase=new Bitmap(View.Width,View.Height,PixelFormat.Format32bppRgb);}
            using(var g=Graphics.FromImage(filteredBase))FramePainter.Draw(g,displayImage,new Rectangle(Point.Empty,View.Size),Filter);
            PaintZoom(e.Graphics,filteredBase,dim);
        }
        else FramePainter.Draw(e.Graphics, displayImage, View, Filter);
        Frame.TabLayer.Draw(e.Graphics,View,TabTransparency);
        paintedZoom=zoom?WorldZoomPercent:100;paintedPid=Frame.ProcessId;paintedMap=Frame.WorldRectangle;paintedSize=View.Size;paintedWheel=Frame.WheelScene;
        if(zoom)
        {if(inputProtection.Length!=Frame.ProtectedPixels.Length)inputProtection=new byte[Frame.ProtectedPixels.Length];Buffer.BlockCopy(Frame.ProtectedPixels,0,inputProtection,0,inputProtection.Length);}
    }
    void PaintZoom(Graphics graphics,Bitmap normal,bool dim)
    {
        Bitmap terrain=dim?worldFog.Apply(Frame,FogRange,FogSquare,FogOpacity,FogFade,true):Frame.WorldBitmap;
        graphics.DrawImageUnscaled(worldPainter.Paint(normal,terrain,Frame.WorldRectangle,WorldZoomPercent,Frame.ProtectedPixels,Filter),View.Location);
    }
    // Let Windows compose the pointer at its native size with its AND/XOR mask.
    // No painted cursor, forced 2x enlargement, transparent cursor or frame lag.
    internal void SetGameCursor(bool enabled){UseGameCursor=enabled;Cursor=enabled?gameCursor.Value:Cursors.Arrow;RefreshPointer();}
    internal Bitmap CaptureScreenshot()
    {
        if(Frame==null||Frame.Frames==0||ClientSize.Width<1||ClientSize.Height<1)throw new InvalidOperationException("The game view is not ready for a screenshot.");
        var image=new Bitmap(ClientSize.Width,ClientSize.Height,PixelFormat.Format32bppArgb);
        try{DrawToBitmap(image,new Rectangle(Point.Empty,ClientSize));return image;}
        catch{image.Dispose();throw;}
    }
    internal void RefreshPointer()
    {
        Form form=FindForm();
        if(form!=null)HostPointer.Restore(form,this);
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);RefreshPointer();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);RefreshPointer();}
    protected override void WndProc(ref Message m)
    {
        // Apply the native color cursor again after activation or a capture
        // transition, even when the Control.Cursor property never changed.
        if(m.Msg==0x20 && (m.LParam.ToInt64()&0xffff)==1)
        {Cursor.Current=Cursor;m.Result=new IntPtr(1);return;}
        base.WndProc(ref m);
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);zoomWheel.ResetRemainder();Invalidate();}
    protected override void Dispose(bool disposing){if(disposing){Cursor=Cursors.Arrow;pixelEdges.Dispose();fog.Dispose();worldFog.Dispose();worldPainter.Dispose();if(filteredBase!=null)filteredBase.Dispose();gameCursor.Dispose();}base.Dispose(disposing);}
    int MouseFlags()
    {
        int flags = 0;
        if ((MouseButtons & MouseButtons.Left) != 0) flags |= 1;
        if ((MouseButtons & MouseButtons.Right) != 0) flags |= 2;
        if ((ModifierKeys & Keys.Shift) != 0) flags |= 4;
        if ((ModifierKeys & Keys.Control) != 0) flags |= 8;
        if ((MouseButtons & MouseButtons.Middle) != 0) flags |= 16;
        return flags;
    }
    bool MapMouse(Point point,out Point p)
    {
        p=Point.Empty;if(Frame==null)return false;
        if(paintedZoom>100&&paintedPid==Frame.ProcessId&&paintedSize==View.Size)
            return WorldZoomGeometry.TryMap(new Point(point.X-View.X,point.Y-View.Y),View.Size,new Size(Frame.Width,Frame.Height),paintedMap,paintedZoom,inputProtection,out p);
        p=ViewGeometry.ToGame(point,View,PresentationSize);return true;
    }
    void MouseMessage(uint message, MouseEventArgs e)
    {
        if (Frame == null || (!View.Contains(e.Location) && !Capture)) return;
        Point p;if(!MapMouse(e.Location,out p))return;
        lastMousePoint=p;
        HostNative.PostMessage(Frame.Window, message, new IntPtr(MouseFlags()), new IntPtr((p.Y << 16) | (p.X & 65535)));
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); RefreshPointer(); MouseMessage(0x200, e); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if(Frame==null || !View.Contains(e.Location)){zoomWheel.ResetRemainder();return;}
        // Hit-test the unzoomed UI in the displayed frame, before applying any
        // terrain inverse transform. Chat/lists scroll instead of zooming.
        Point native=ViewGeometry.ToGame(e.Location,View,PresentationSize);
        if(!Frame.InWorld || !Frame.TravelMap.IsEmpty || paintedPid!=Frame.ProcessId || paintedSize!=View.Size || !paintedWheel.IsWorld(native))
        {
            zoomWheel.ResetRemainder();
            if(ManualInput!=null)ManualInput(this,EventArgs.Empty);
            // 7.41 dispatches wheel to EventMan's last WM_MOUSEMOVE point.
            // Its window procedure also treats LPARAM as logical client
            // coordinates, so parked-window screen coordinates are incorrect.
            int flags=MouseFlags();lastMousePoint=native;
            HostNative.PostMessage(Frame.Window,0x6,new IntPtr(1),IntPtr.Zero);
            HostNative.PostMessage(Frame.Window,0x200,new IntPtr(flags),new IntPtr(NativeWheelScene.PositionParameter(native)));
            HostNative.PostMessage(Frame.Window,0x20a,new IntPtr(NativeWheelScene.WheelParameter(e.Delta,flags)),new IntPtr(NativeWheelScene.PositionParameter(native)));
            return;
        }
        if(LockZoom){zoomWheel.ResetRemainder();return;}
        bool playZoom;int steps=zoomWheel.Step(e.Delta,(ModifierKeys&Keys.Control)!=0,MapOverlayVisible,wheelClock.ElapsedMilliseconds,out playZoom);
        if(playZoom){if(steps!=0&&PlayZoom!=null)PlayZoom(steps);return;}
        if(steps!=0 && MapZoom!=null){if(ManualInput!=null)ManualInput(this,EventArgs.Empty);MapZoom(steps);}
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Frame == null || !View.Contains(e.Location)) return;
        if(ManualInput!=null)ManualInput(this,EventArgs.Empty);
        if(e.Button==MouseButtons.Middle)
        {
            if(Frame.InWorld && ToggleFog!=null)ToggleFog(this,EventArgs.Empty);
            Focus();return;
        }
        Point mapped;if(!MapMouse(e.Location,out mapped))return;
        MouseDownCount++; heldMouse |= e.Button;
        Focus(); Capture = true;
        HostNative.PostMessage(Frame.Window, 0x6, new IntPtr(1), IntPtr.Zero);
        // EventMan updates its click location only for WM_MOUSEMOVE. The
        // Win32 cursor path alone does not update that logical event point.
        MouseMessage(0x200, e);
        MouseMessage(e.Button == MouseButtons.Right ? 0x204u : e.Button == MouseButtons.Middle ? 0x207u : 0x201u, e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if(e.Button==MouseButtons.Middle){base.OnMouseUp(e);return;}
        if((heldMouse&e.Button)==0){base.OnMouseUp(e);return;}
        MouseUpCount++; heldMouse &= ~e.Button;
        Point mapped;
        if(!MapMouse(e.Location,out mapped))
            HostNative.PostMessage(Frame.Window,e.Button==MouseButtons.Right?0x205u:0x202u,IntPtr.Zero,new IntPtr((lastMousePoint.Y<<16)|(lastMousePoint.X&65535)));
        else {MouseMessage(0x200, e);MouseMessage(e.Button == MouseButtons.Right ? 0x205u : 0x202u, e);}
        changingCapture=true;Capture = false;changingCapture=false; base.OnMouseUp(e);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if(changingCapture || Capture || Frame==null || !Focused || Form.ActiveForm!=FindForm())return;
        CaptureLossCount++;
        // DA's logical button handler calls SetCapture on its parked HWND.
        // Keep physical releases routed to this visible surface instead.
        changingCapture=true;
        Capture=true;
        if(heldMouse==MouseButtons.None)Capture=false;
        changingCapture=false;
    }
    // Forward actual user keystrokes only while the game surface has focus.
    // Library typing stays inside WebView2; no keyboard hooks or key logging.
    public override bool PreProcessMessage(ref Message msg)
    {
        if (Focused && Frame != null && msg.Msg >= 0x100 && msg.Msg <= 0x109)
        {
            if(IsHostedScreenshotKey(msg.Msg,msg.WParam.ToInt32(),ModifierKeys)||
                (screenshotHeld&&msg.WParam.ToInt32()==(int)Keys.F12&&(msg.Msg==0x100||msg.Msg==0x101)))
            {
                // The native screenshot path can stall when its HWND is parked
                // offscreen. Capture the displayed host view instead, once per
                // physical F12 press; never forward F12 to that game window.
                if(msg.Msg==0x100&&!screenshotHeld){screenshotHeld=true;if(ScreenshotRequested!=null)ScreenshotRequested(this,EventArgs.Empty);}
                if(msg.Msg==0x101)screenshotHeld=false;
                return true;
            }
            if (msg.Msg == 0x104 && msg.WParam.ToInt32() == (int)Keys.F4) return base.PreProcessMessage(ref msg);
            if(hotkeyInput.Handle(msg.Msg,msg.WParam.ToInt32(),ModifierKeys,TryHotkey,ReleaseHeldInput))return true;
            // Accessibility-generated key messages can omit scan-code bits.
            // DA decodes those bits rather than wParam; retain real hardware
            // scans and fill only the missing ones.
            if(msg.Msg==0x100 || msg.Msg==0x101 || msg.Msg==0x104 || msg.Msg==0x105)
                msg.LParam=new IntPtr(KeyScan.Ensure(msg.WParam.ToInt32(),msg.LParam.ToInt32()));
            if((msg.Msg==0x100 || msg.Msg==0x104) && ManualInput!=null)ManualInput(this,EventArgs.Empty);
            if (msg.Msg == 0x100 || msg.Msg == 0x104) heldKeys[msg.WParam.ToInt32()]=msg.LParam.ToInt32();
            if (msg.Msg == 0x101 || msg.Msg == 0x105) heldKeys.Remove(msg.WParam.ToInt32());
            HostNative.PostMessage(Frame.Window, (uint)msg.Msg, msg.WParam, msg.LParam);
            return true;
        }
        return base.PreProcessMessage(ref msg);
    }
    internal void ReleaseHeldInput()
    {
        screenshotHeld=false;
        if (Frame != null) foreach (var key in heldKeys)
            // The client translates scan-code bits, including extended arrows.
            // Preserve them when releasing a held key after focus changes.
            HostNative.PostMessage(Frame.Window, (key.Value & 0x20000000)!=0 || key.Key==(int)Keys.Menu ? 0x105u : 0x101u,
                new IntPtr(key.Key),new IntPtr(key.Value | unchecked((int)0xc0000000)));
        heldKeys.Clear();
        if(Frame!=null && heldMouse!=MouseButtons.None)
        {
            if((heldMouse&MouseButtons.Left)!=0)HostNative.PostMessage(Frame.Window,0x202,IntPtr.Zero,IntPtr.Zero);
            if((heldMouse&MouseButtons.Right)!=0)HostNative.PostMessage(Frame.Window,0x205,IntPtr.Zero,IntPtr.Zero);
        }
        heldMouse=MouseButtons.None;changingCapture=true;Capture=false;changingCapture=false;
    }
    protected override void OnLostFocus(EventArgs e)
    {
        ReleaseHeldInput();
        hotkeyInput.Clear();
        base.OnLostFocus(e);
    }
    internal static bool IsHostedScreenshotKey(int message,int key,Keys modifiers)
    {return (message==0x100||message==0x101)&&key==(int)Keys.F12&&(modifiers&(Keys.Shift|Keys.Alt|Keys.Control))==0;}
}

internal static class KeyScan
{
    internal static int Ensure(int key,int value)
    {
        if((value&0xff0000)!=0)return value;
        uint scan=ToolNative.MapVirtualKey((uint)key,4);
        if(scan==0)scan=ToolNative.MapVirtualKey((uint)key,0);
        bool extended=(scan&0xff00)!=0 || (key>=(int)Keys.PageUp && key<=(int)Keys.Down) || key==(int)Keys.Insert || key==(int)Keys.Delete || key==(int)Keys.Divide || key==(int)Keys.RControlKey || key==(int)Keys.RMenu;
        return value | 1 | ((int)(scan&255)<<16) | (extended?0x1000000:0);
    }
}

internal static class HostNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] data, IntPtr length, out IntPtr read);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError=true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int show);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

internal static class HostPointer
{
    [StructLayout(LayoutKind.Sequential)] struct Info
    {internal int Size;internal uint Flags;internal IntPtr Cursor;internal Point Position;}
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref Info info);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    internal static int Recoveries;
    internal static bool NeedsRestore(bool active,bool owned,uint flags,IntPtr actual,IntPtr expected,bool game)
    {
        // Respect touch/pen suppression. Never change another app's cursor or
        // repeatedly increment the ShowCursor display counter.
        return active&&owned&&(flags&2)==0&&expected!=IntPtr.Zero&&
            ((flags&1)==0||actual==IntPtr.Zero||(game&&actual!=expected));
    }
    internal static void Restore(Form host,GameSurface surface)
    {
        if(host.IsDisposed||!host.IsHandleCreated||GetForegroundWindow()!=host.Handle)return;
        var info=new Info {Size=Marshal.SizeOf(typeof(Info))};
        if(!GetCursorInfo(ref info))return;
        IntPtr hit=WindowFromPoint(info.Position);
        // Use the control actually under the mouse: an overlay can cover the
        // game without generating another game MouseEnter. Browser child HWNDs
        // keep their own I-beam/link cursor and are not controlled here.
        Control control=Control.FromHandle(hit);
        if(control==null||control.FindForm()!=host)return;
        Cursor expected=control.Cursor??Cursors.Arrow;
        bool game=control==surface;
        if(!NeedsRestore(true,true,info.Flags,info.Cursor,expected.Handle,game))return;
        Cursor.Current=expected;Recoveries++;
    }
}

internal static class HostChecks
{
    internal static void Run()
    {
        int checks = 0;
        foreach (Size logical in new[] { new Size(960,600), new Size(1088,640), new Size(1152,680) })
        foreach (Size available in new[] { new Size(1920,1000), new Size(1370,900), new Size(700,500) })
        foreach (bool stretch in new[] { false, true })
        {
            Rectangle view = ViewGeometry.Fit(available, logical, stretch, false);
            if (view.Width > available.Width || view.Height > available.Height || view.Left < 0 || view.Top < 0) throw new Exception("View does not fit.");
            if (ViewGeometry.ToGame(view.Location, view, logical) != Point.Empty) throw new Exception("Top-left mapping failed.");
            Point last = ViewGeometry.ToGame(new Point(view.Right+20, view.Bottom+20), view, logical);
            if (last != new Point(logical.Width-1, logical.Height-1)) throw new Exception("Edge clamp failed.");
            Point middle = ViewGeometry.ToGame(new Point(view.Left+view.Width/2, view.Top+view.Height/2),view,logical);
            if (Math.Abs(middle.X-logical.Width/2)>2 || Math.Abs(middle.Y-logical.Height/2)>2) throw new Exception("Center mapping failed.");
            foreach (bool compact in new[] { false, true })
            {
                Rectangle dock=ViewGeometry.Dock(view,logical,compact);
                if (!view.Contains(dock) || dock.Left < view.Left+640.0*view.Width/logical.Width) throw new Exception("Dock covers original HUD.");
                checks++;
            }
        }
        int[] palette=new int[65536];for(int i=0;i<palette.Length;i++)palette[i]=i;
        int[] output=new int[4];
        // Two visible pixels per row; poisoned padding must never be drawn.
        FramePixels.Convert(new byte[]{1,0,2,0,255,255,255,255,3,0,4,0,255,255,255,255},8,2,2,palette,output);
        if(output[0]!=1 || output[1]!=2 || output[2]!=3 || output[3]!=4)throw new Exception("Padded row conversion failed.");
        bool rejected=false;try {FramePixels.Convert(new byte[4],8,2,2,palette,output);}catch(ArgumentException){rejected=true;}
        if(!rejected)throw new Exception("Short frame buffer was accepted.");
        int scaleChecks=0;
        foreach(Size available in new[]{new Size(1920,1000),new Size(2500,1500),new Size(800,500)})
        {
            Size logical=new Size(1088,640);
            Rectangle view=ViewGeometry.Fit(available,logical,false,false,true);
            if(view.Right>available.Width || view.Bottom>available.Height)throw new Exception("Integer view overflows.");
            if(available.Width>=logical.Width && available.Height>=logical.Height &&
                (view.Width%logical.Width!=0 || view.Height%logical.Height!=0 || view.Width/logical.Width!=view.Height/logical.Height))
                throw new Exception("Integer pixels have unequal sizes.");
            if(Math.Abs((double)view.Width/view.Height-(double)logical.Width/logical.Height)>.005)throw new Exception("Aspect ratio changed.");
            scaleChecks++;
        }
        using(var sourceImage=new Bitmap(3,3))
        {
            using(var graphics=Graphics.FromImage(sourceImage))graphics.Clear(Color.Black);
            sourceImage.SetPixel(1,1,Color.White);
            foreach(FrameFilter filter in new[]{FrameFilter.Bilinear,FrameFilter.Bicubic})
            using(var scaled=new Bitmap(8,8))
            {
                using(var graphics=Graphics.FromImage(scaled))FramePainter.Draw(graphics,sourceImage,new Rectangle(0,0,8,8),filter);
                bool blended=false;
                for(int y=0;y<8;y++)for(int x=0;x<8;x++) {int red=scaled.GetPixel(x,y).R;blended|=red>0&&red<255;}
                if(!blended)throw new Exception("Smooth filter is still nearest-neighbor.");
                scaleChecks++;
            }
            using(var scaled=new Bitmap(6,6))
            {
                using(var graphics=Graphics.FromImage(scaled))FramePainter.Draw(graphics,sourceImage,new Rectangle(0,0,6,6),FrameFilter.IntegerPixels);
                for(int y=0;y<6;y++)for(int x=0;x<6;x++)
                    if(scaled.GetPixel(x,y).ToArgb()!=sourceImage.GetPixel(x/2,y/2).ToArgb())throw new Exception("Integer blocks are uneven.");
                scaleChecks++;
            }
        }
        int edgeChecks=0;
        using(var scaler=new PixelEdgeScaler())
        using(var pattern=new Bitmap(2,2,PixelFormat.Format32bppRgb))
        {
            pattern.SetPixel(0,0,Color.Black);pattern.SetPixel(1,0,Color.White);
            pattern.SetPixel(0,1,Color.Black);pattern.SetPixel(1,1,Color.White);
            Bitmap integer=scaler.Scale(pattern,6,6);
            for(int y=0;y<6;y++)for(int x=0;x<6;x++)
                if(integer.GetPixel(x,y).R!=(x<3?0:255))throw new Exception("Pixel-edge integer scale is not exact.");
            edgeChecks++;
            Bitmap fractional=scaler.Scale(pattern,3,5);
            for(int y=0;y<5;y++)
                if(fractional.GetPixel(0,y).R!=0 || Math.Abs(fractional.GetPixel(1,y).R-127)>1 || fractional.GetPixel(2,y).R!=255)
                    throw new Exception("Fractional boundary coverage failed.");
            edgeChecks++;
            Bitmap enlarged=scaler.Scale(pattern,5,3);
            if(enlarged.GetPixel(1,1).R!=0||enlarged.GetPixel(3,1).R!=255)throw new Exception("Pixel interiors were blurred.");
            edgeChecks++;
            using(var solid=new Bitmap(7,5,PixelFormat.Format32bppRgb))
            {
                using(var g=Graphics.FromImage(solid))g.Clear(Color.FromArgb(117,83,201));
                Bitmap resized=scaler.Scale(solid,19,14);
                for(int y=0;y<14;y++)for(int x=0;x<19;x++)
                    if(resized.GetPixel(x,y).ToArgb()!=Color.FromArgb(117,83,201).ToArgb())throw new Exception("Uniform color changed.");
                edgeChecks++;
            }
        }
        double edgeMilliseconds;
        using(var scaler=new PixelEdgeScaler())
        using(var sample=new Bitmap(1088,640,PixelFormat.Format32bppRgb))
        {
            scaler.Scale(sample,1666,980);
            var timer=Stopwatch.StartNew();
            for(int i=0;i<5;i++)scaler.Scale(sample,1666,980);
            edgeMilliseconds=timer.Elapsed.TotalMilliseconds/5;
        }
        Directory.CreateDirectory(Path.Combine(Program.Root,"build"));
        int toolChecks=ToolChecks.Run();
        File.WriteAllText(Path.Combine(Program.Root,"build","host-checks.json"),Program.Json.Serialize(new {status="passed", geometry_cases=checks, pixel_cases=2, scaling_cases=scaleChecks, pixel_edge_cases=edgeChecks, companion_cases=toolChecks, pixel_edge_scale_ms=edgeMilliseconds}));
    }
}
