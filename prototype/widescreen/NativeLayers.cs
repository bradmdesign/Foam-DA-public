using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Read-only masks from the client's own panes. No palette guessing against
// the finished world image, and no second input coordinate system.
internal sealed class NativeLayerMask
{
    readonly Action<uint,byte[],int> read;
    readonly uint image;
    readonly int width,height;
    readonly byte[] header=new byte[0x190],word=new byte[4];
    byte[] entries=new byte[0],scratch=new byte[0];
    readonly bool[] visible=new bool[64];
    readonly Point[] origins=new Point[64];
    readonly Rectangle[] clips=new Rectangle[64];
    internal string Failure;
    internal bool WorldVisible;
    internal Rectangle TravelMap;
    internal Rectangle WorldArea;
    internal readonly List<Rectangle> InputRegions=new List<Rectangle>();
    readonly byte[] worldBounds=new byte[24];
    bool Fail(string reason){Failure=reason;return false;}
    internal NativeLayerMask(Action<uint,byte[],int> reader,uint module,int w,int h){read=reader;image=module;width=w;height=h;}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    uint U(uint at){read(at,word,4);return U(word,0);}
    internal bool Capture(byte[] mask, bool protect=true,bool separateMap=false)
    {
        if(protect)Array.Clear(mask,0,mask.Length);
        Failure=null;
        WorldVisible=false;
        TravelMap=Rectangle.Empty;
        WorldArea=Rectangle.Empty;InputRegions.Clear();
        uint dispatcher=U(image+0x33d944);
        if(dispatcher<0x10000)return Fail("dispatcher");
        uint array=U(dispatcher+0x64),count=U(dispatcher+0x68),generation=U(dispatcher+0x1b4);
        if(count<1||count>2048||array<0x10000)return Fail("pane list");
        int bytes=checked((int)count*12);
        if(entries.Length<bytes)entries=new byte[bytes];
        read(array,entries,bytes);
        Rectangle screen=new Rectangle(0,0,width,height);bool foundWorld=false,foundHud=false;
        int previousDepth=-1;
        for(int i=0;i<count;i++)
        {
            int depth=checked((int)U(entries,i*12+4));uint pane=U(entries,i*12);
            if(depth>=64||depth>previousDepth+1)return Fail("tree depth");
            previousDepth=depth;visible[depth]=false;
            if(depth>0&&!visible[depth-1])continue;
            read(pane,header,header.Length);
            if(header[0x130]==0||(U(header,0x188)&2)==0)continue;
            visible[depth]=true;
            // Native Point/RECT storage is y,x (verified in this executable).
            Point origin=new Point(BitConverter.ToInt32(header,0x12c),BitConverter.ToInt32(header,0x128));
            if(depth>0)origin.Offset(origins[depth-1]);origins[depth]=origin;
            int w=BitConverter.ToInt32(header,0xe8),h=BitConverter.ToInt32(header,0xec);
            if(w<0||h<0||w>4096||h>4096||Math.Abs((long)origin.X)>8192||Math.Abs((long)origin.Y)>8192)return Fail("pane bounds");
            Rectangle rect=Rectangle.Intersect(depth>0?clips[depth-1]:screen,new Rectangle(origin.X,origin.Y,w,h));
            clips[depth]=rect;
            uint vt=U(header,0)-image;
            if(vt==0x274b44||vt==0x2868f4)TravelMap=rect;
            if(vt==0x28c054)
            {
                foundWorld=true;
                // Stable viewport geometry, independent of walking cache
                // origins and of whether fog/zoom pixels are being captured.
                read(pane+0x2d0,worldBounds,worldBounds.Length);
                int top=BitConverter.ToInt32(worldBounds,0),left=BitConverter.ToInt32(worldBounds,4);
                int bottom=BitConverter.ToInt32(worldBounds,8),right=BitConverter.ToInt32(worldBounds,12);
                int oy=BitConverter.ToInt32(worldBounds,16),ox=BitConverter.ToInt32(worldBounds,20);
                if(right>left&&bottom>top&&right-left<=width&&bottom-top<=height&&Math.Abs((long)ox)<8192&&Math.Abs((long)oy)<8192)
                    WorldArea=Rectangle.Intersect(screen,new Rectangle(ox+left,oy+top,right-left,bottom-top));
                continue;
            } // WorldPane_Impl
            if(vt==0x2835d4)continue; // ScreenPane (aliases final canvas)
            if(rect.IsEmpty||w==0||h==0)continue;
            // UI hit regions must be available even when fog and play zoom are
            // off. Exclude the transparent HUD container and Tab-map overlay;
            // their child controls still block zoom at their native positions.
            if(vt!=0x288a84&&vt!=0x28aeac)InputRegions.Add(rect);
            // NPCSession and BulletinSession are full-canvas layout/input
            // parents, not opaque pictures. Only their visible children
            // (dialogue/menu/portrait or mail/board windows) protect pixels.
            // Masking the parent makes every world pixel look like unzoomed UI
            // for the duration of a conversation. Keep its modal input region.
            if(vt==0x280a54||vt==0x26ebdc)continue;
            if(vt==0x288a84)foundHud=true;
            if(!protect)continue;
            if(vt==0x288a84||vt==0x28aeac)
            {
                // GUIBackPane and the Tab map both use transparent zero.
                // Their nonzero pixels include the HUD ornaments above MAP.
                if(vt==0x288a84)foundHud=true;
                uint mode=U(header,0x6c);
                if(mode!=4)return Fail("layer mode");
                int pitch=BitConverter.ToInt32(header,0x9c);uint backing=U(header,0x98);
                if(pitch<w*2||pitch>w*2+64||backing<0x10000||pitch*(long)h>1920*1080*4)return Fail("layer backing");
                int length=pitch*h;if(scratch.Length<length)scratch=new byte[length];
                read(backing+16,scratch,length);
                ProtectPixels(mask,width,height,scratch,pitch,w,h,origin,rect,separateMap&&vt==0x28aeac?(byte)3:(byte)1);
                if(U(pane+0x98)!=backing||U(pane+0x6c)!=mode||U(pane)!=image+vt)return Fail("layer replaced");
            }
            else
            {
                // All other visible UI panes protect their full clipped bounds,
                // including black interiors and the children of open dialogs.
                for(int y=rect.Top;y<rect.Bottom;y++)for(int x=rect.Left;x<rect.Right;x++)mask[y*width+x]=1;
            }
        }
        // GameMessagePane belongs only to the visual tree (flags +188 = 1),
        // so it is absent from the input dispatcher above. Its singleton is
        // installed by 47D290. Preserve its transparent, already-composited
        // glyphs at screen coordinates, including the native fade colours.
        if(protect&&foundWorld&&!CaptureMessages(mask))return false;
        if(U(image+0x33d944)!=dispatcher||U(dispatcher+0x64)!=array||U(dispatcher+0x68)!=count||U(dispatcher+0x1b4)!=generation)return Fail("tree changed");
        // A stable tree with no visible world is a legitimate login/travel
        // screen, not a failed read. It must continue publishing without fog.
        if(foundWorld&&!foundHud)return Fail("world/HUD missing");
        WorldVisible=foundWorld;
        return true;
    }
    bool CaptureMessages(byte[] mask)
    {
        uint pane=U(image+0x33baec);if(pane==0)return true;
        read(pane,header,header.Length);
        if(U(header,0)!=image+0x275314)return Fail("message pane");
        if(header[0x130]==0||(U(header,0x188)&1)==0)return true;
        // The timer at 47D010 leaves visibility and the glyph backing intact
        // after expiry. Copy mode 7 means no draw; 43 is the zero-weight end
        // of the 43..75 fade. Masking either would preserve terrain pixels in
        // stale letter shapes, since the completed root no longer has text.
        uint blend=U(header,0x134);
        if(blend==7||blend==43)return true;
        int w=BitConverter.ToInt32(header,0xe8),h=BitConverter.ToInt32(header,0xec),pitch=BitConverter.ToInt32(header,0x9c);
        Point origin=new Point(BitConverter.ToInt32(header,0x12c),BitConverter.ToInt32(header,0x128));
        uint backing=U(header,0x98),mode=U(header,0x6c),visibility=U(header,0x130);
        if(w<1||h<1||w>width||h>height||pitch<w*2||pitch>w*2+64||backing<0x10000||mode!=4||Math.Abs((long)origin.X)>8192||Math.Abs((long)origin.Y)>8192)return Fail("message backing");
        int length=checked(pitch*h);if(scratch.Length<length)scratch=new byte[length];read(backing+16,scratch,length);
        // Value 2 protects display pixels but keeps these notifications
        // click-through when mapping a mouse position into the zoomed world.
        ProtectPixels(mask,width,height,scratch,pitch,w,h,origin,new Rectangle(0,0,width,height),2);
        if(U(image+0x33baec)!=pane||U(pane)!=image+0x275314||U(pane+0x98)!=backing||U(pane+0x6c)!=mode||U(pane+0xe8)!=w||U(pane+0xec)!=h||U(pane+0x9c)!=pitch||
            U(pane+0x128)!=unchecked((uint)origin.Y)||U(pane+0x12c)!=unchecked((uint)origin.X)||U(pane+0x130)!=visibility||U(pane+0x134)!=blend)return Fail("message changed");
        return true;
    }
    internal static void ProtectPixels(byte[] mask,int width,int height,byte[] canvas,int pitch,int w,int h,Point origin,Rectangle clip,byte kind=1)
    {
        if(mask.Length<width*height||canvas.Length<(long)pitch*h||pitch<w*2)throw new ArgumentException("Invalid layer bounds.");
        Rectangle rect=Rectangle.Intersect(new Rectangle(0,0,width,height),Rectangle.Intersect(clip,new Rectangle(origin.X,origin.Y,w,h)));
        for(int y=rect.Top;y<rect.Bottom;y++)for(int x=rect.Left;x<rect.Right;x++)
        {int at=(y-origin.Y)*pitch+(x-origin.X)*2;if((canvas[at]|canvas[at+1])!=0&&(mask[y*width+x]==0||kind==1||(kind==2&&mask[y*width+x]==3)))mask[y*width+x]=kind;}
    }
}

internal sealed class FogComposition : IDisposable
{
    Bitmap result;int[] pixels;byte[] alpha;GCHandle pin;
    string key;
    internal Bitmap Apply(GameFrame frame,int range,bool square,int opacity,bool fade,bool worldOnly=false)
    {
        string next=frame.Width+":"+frame.Height+":"+frame.Compact+":"+range+":"+square+":"+opacity+":"+fade;
        if(result==null||result.Width!=frame.Width||result.Height!=frame.Height)
        {
            Dispose();pixels=new int[frame.Width*frame.Height];pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
            result=new Bitmap(frame.Width,frame.Height,frame.Width*4,PixelFormat.Format32bppRgb,pin.AddrOfPinnedObject());key=null;
        }
        if(key!=next)
        {
            using(var overlay=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(overlay))FogOverlay.Draw(g,new Rectangle(Point.Empty,overlay.Size),overlay.Size,frame.Compact,range,square,opacity,fade);
                var data=overlay.LockBits(new Rectangle(Point.Empty,overlay.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try {var bytes=new byte[data.Stride*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);alpha=new byte[pixels.Length];for(int y=0;y<data.Height;y++)for(int x=0;x<data.Width;x++)alpha[y*data.Width+x]=bytes[y*data.Stride+x*4+3];}
                finally {overlay.UnlockBits(data);}
            }
            key=next;
        }
        Blend(worldOnly?frame.WorldPixels:frame.NativePixels,pixels,alpha,worldOnly?null:frame.ProtectedPixels);return result;
    }
    internal static void Blend(int[] source,int[] output,byte[] alpha,byte[] protect)
    {
        if(output.Length!=source.Length||alpha.Length!=source.Length||(protect!=null&&protect.Length!=source.Length))throw new ArgumentException("Invalid fog buffers.");
        for(int i=0;i<source.Length;i++)
        {
            int c=source[i],a=protect!=null&&protect[i]!=0?0:alpha[i];
            if(a==0){output[i]=c;continue;}int inverse=255-a;
            output[i]=unchecked((int)0xff000000)|((((c>>16)&255)*inverse+127)/255<<16)|((((c>>8)&255)*inverse+127)/255<<8)|(((c&255)*inverse+127)/255);
        }
    }
    public void Dispose(){if(result!=null){result.Dispose();result=null;}if(pin.IsAllocated)pin.Free();key=null;}
}
