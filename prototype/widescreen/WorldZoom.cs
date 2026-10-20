using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Read-only world underlay. Most pixels come from the completed root so all
// native lighting/effects remain exact. Behind UI we reconstruct the world's
// own RGB16 canvas using its light plane (5CE350 / 603930 / 603DA0 in 7.41).
internal sealed class WorldZoomCapture
{
    readonly Action<uint,byte[],int> read;
    readonly uint image;
    readonly int width,height;
    readonly byte[] header=new byte[0x300],lightHeader=new byte[32],word=new byte[4];
    byte[] raw=new byte[0],light=new byte[0],groundRaw=new byte[0];
    readonly byte[] groundHeader=new byte[0xf0];
    internal Rectangle Map;
    internal MapScene Scene=MapScene.Empty;
    internal int GridPixels;internal string GridStatus;
    internal WorldZoomCapture(Action<uint,byte[],int> reader,uint module,int w,int h)
    {read=reader;image=module;width=w;height=h;}
    uint U(uint at){read(at,word,4);return BitConverter.ToUInt32(word,0);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static int I(byte[] b,int at){return BitConverter.ToInt32(b,at);}
    internal MapScene CaptureScene()
    {
        uint map=U(image+0x33d960);if(map<0x102e8)return MapScene.Empty;
        uint pane=map-0x2e8;read(pane,header,header.Length);
        if(U(header,0)!=image+0x28c054)return MapScene.Empty;
        var scene=MapSceneReader.Read(read,image,pane,header);
        return U(image+0x33d960)==map?scene:MapScene.Empty;
    }
    internal bool Capture(byte[] output,byte[] root,int rootPitch,byte[] protection,bool sceneRequested=false,bool entitiesRequested=false,bool gridRequested=false)
    {
        Map=Rectangle.Empty;Scene=MapScene.Empty;GridPixels=0;GridStatus=null;
        uint map=U(image+0x33d960);if(map<0x102e8)return false;
        uint pane=map-0x2e8;read(pane,header,header.Length);
        if(U(header,0)!=image+0x28c054||U(header,0x6c)!=4)return false;
        int w=I(header,0xe8),h=I(header,0xec),pitch=I(header,0x9c),ox=I(header,0x12c),oy=I(header,0x128);
        uint backing=U(header,0x98);
        if(w<56||h<28||w>1984||h>1144||pitch<w*2||pitch>w*2+64||backing<0x10000||Math.Abs((long)ox)>8192||Math.Abs((long)oy)>8192)return false;
        // +128/+12C move during smooth walking; +2E0/+2E4 retain the layout
        // origin. Anchor zoom to the layout, not the moving cache window.
        Rectangle rect=Rectangle.Intersect(new Rectangle(0,0,width,height),new Rectangle(
            I(header,0x2e4)+I(header,0x2d4),I(header,0x2e0)+I(header,0x2d0),
            I(header,0x2dc)-I(header,0x2d4),I(header,0x2d8)-I(header,0x2d0)));
        if(rect.IsEmpty)return false;
        if(rect.Left<ox||rect.Top<oy||rect.Right>ox+w||rect.Bottom>oy+h)return false;
        int length=checked(pitch*h);if(raw.Length<length)raw=new byte[length];read(backing+16,raw,length);
        bool lit=header[0x1e8]!=0&&header[0x200]<32;
        int lightPitch=0;uint lightObject=U(header,0x1e4),lightBacking=0;
        int shade=BitConverter.ToUInt16(header,0x202);bool rgb565=U(image+0x2d8fdc)!=0;
        if(lit)
        {
            if(lightObject<0x10000)return false;read(lightObject,lightHeader,32);
            lightBacking=U(lightHeader,8);lightPitch=I(lightHeader,0x18);
            if(I(lightHeader,0x10)!=w||I(lightHeader,0x14)!=h||lightPitch<w||lightPitch>w+64||lightBacking<0x10000)return false;
            length=checked(lightPitch*h);if(light.Length<length)light=new byte[length];read(lightBacking,light,length);
        }
        for(int y=rect.Top;y<rect.Bottom;y++)for(int x=rect.Left;x<rect.Right;x++)
        {
            int to=(y*width+x)*2;
            if(protection[y*width+x]==0)
            {int from=y*rootPitch+x*2;output[to]=root[from];output[to+1]=root[from+1];continue;}
            int at=(y-oy)*pitch+(x-ox)*2,c=raw[at]|raw[at+1]<<8;
            if(lit){int weight=light[(y-oy)*lightPitch+x-ox];if(weight>32)return false;c=LightPixel(c,shade,weight,rgb565);}
            output[to]=(byte)c;output[to+1]=(byte)(c>>8);
        }
        if(U(image+0x33d960)!=map||U(pane)!=image+0x28c054||U(pane+0x98)!=backing||U(pane+0xe8)!=w||U(pane+0xec)!=h||
            U(pane+0x128)!=U(header,0x128)||U(pane+0x12c)!=U(header,0x12c)||U(pane+0x1e8)!=U(header,0x1e8)||U(pane+0x200)!=U(header,0x200))return false;
        if(lit&&(U(pane+0x1e4)!=lightObject||U(lightObject+8)!=lightBacking||U(lightObject+0x18)!=lightPitch))return false;
        for(uint offset=0x2d0;offset<=0x2e4;offset+=4)if(U(pane+offset)!=U(header,(int)offset))return false;
        if(sceneRequested)try
        {
            Scene=MapSceneReader.Read(read,image,pane,header);
        }catch{Scene=MapScene.Empty;}
        if(gridRequested&&Scene.Ready)try
        {
            // 5D401A updates the ground cache and 5D4062 copies its Canvas
            // at +54 directly into the world Canvas, before walls/objects.
            // Compare unlit source pixels in that identical coordinate space.
            uint context=U(header,0x20c),cache=U(context+0xc),canvas=U(cache+0x54);
            read(canvas,groundHeader,groundHeader.Length);
            int gp=I(groundHeader,0x9c);uint gb=U(groundHeader,0x98);
            if(U(groundHeader,0x6c)==4&&I(groundHeader,0xe8)==w&&I(groundHeader,0xec)==h&&gp>=w*2&&gp<=w*2+64&&gb>=0x10000)
            {
                int gl=checked(gp*h);if(groundRaw.Length<gl)groundRaw=new byte[gl];read(gb+16,groundRaw,gl);
                if(U(context+0xc)==cache&&U(cache+0x54)==canvas&&U(canvas+0x98)==gb&&U(pane+0x98)==backing)
                    GridPixels=TileGrid.ApplyGround(output,root,protection,width,rootPitch,rect,Scene.Origin,raw,pitch,groundRaw,gp,w,h,new Point(ox,oy),rgb565);
            }
        }
        catch(Exception ex) { GridStatus=ex.GetType().Name; /* Omit grid while cache changes. */ }
        Map=rect;return true;
    }
    internal static int LightPixel(int pixel,int color,int weight,bool rgb565)
    {
        if(weight<0||weight>32)throw new ArgumentOutOfRangeException("weight");
        int shift=rgb565?11:10,green=rgb565?63:31,other=32-weight;
        // Match the client's four-pixel MMX loop: truncate each contribution
        // separately before adding. Its short scalar tail can differ by 1 LSB.
        return ((((pixel>>shift)&31)*weight/32+((color>>shift)&31)*other/32)<<shift)|
            ((((pixel>>5)&green)*weight/32+((color>>5)&green)*other/32)<<5)|
            ((pixel&31)*weight/32+(color&31)*other/32);
    }
}

// Immutable native hit regions published with the frame they describe.
internal sealed class NativeWheelScene
{
    internal static readonly NativeWheelScene Empty=new NativeWheelScene(Rectangle.Empty,new Rectangle[0]);
    readonly Rectangle world;
    readonly Rectangle[] ui;
    internal NativeWheelScene(Rectangle world,Rectangle[] ui){this.world=world;this.ui=ui;}
    internal bool IsWorld(Point point)
    {
        if(!world.Contains(point))return false;
        foreach(Rectangle rect in ui)if(rect.Contains(point))return false;
        return true;
    }
    internal static int WheelParameter(int delta,int mouseFlags){return unchecked((delta<<16)|(mouseFlags&65535));}
    internal static int PositionParameter(Point point){return unchecked((point.Y<<16)|(point.X&65535));}
}

// Wheel notches for the two independent zoom controls. Dropped map notches
// are never queued for later: one direction step at most per 250 ms.
internal sealed class ZoomWheelInput
{
    int remainder;bool previousMap,haveMode,haveMapStep;long lastMapStep;
    internal void ResetRemainder(){remainder=0;haveMode=false;}
    internal int Step(int delta,bool control,bool mapVisible,long milliseconds,out bool play)
    {
        play=!control;
        if(control&&!mapVisible){ResetRemainder();return 0;}
        if(!haveMode||previousMap!=control){remainder=0;previousMap=control;haveMode=true;}
        if(control&&haveMapStep&&milliseconds-lastMapStep<250){remainder=0;return 0;}
        remainder+=delta;int steps=remainder/120;remainder%=120;
        if(!control||steps==0)return steps;
        remainder=0;lastMapStep=milliseconds;haveMapStep=true;return Math.Sign(steps);
    }
}

internal static class WorldZoomGeometry
{
    internal static RectangleF Crop(Rectangle map,int percent)
    {
        double z=Math.Max(100,Math.Min(200,percent))/100.0;
        // A fixed center retains the camera's existing player placement.
        return new RectangleF((float)(map.Left+map.Width*(1-1/z)/2),
            (float)(map.Top+map.Height*(1-1/z)/2),(float)(map.Width/z),(float)(map.Height/z));
    }
    internal static Rectangle ScreenMap(Rectangle map,Size canvas,Size display)
    {
        return Rectangle.FromLTRB((int)Math.Ceiling(map.Left*(double)display.Width/canvas.Width),
            (int)Math.Ceiling(map.Top*(double)display.Height/canvas.Height),
            (int)Math.Floor(map.Right*(double)display.Width/canvas.Width),
            (int)Math.Floor(map.Bottom*(double)display.Height/canvas.Height));
    }
    internal static bool TryMap(Point screen,Size display,Size canvas,Rectangle map,int percent,byte[] protection,out Point native)
    {
        native=ViewGeometry.ToGame(screen,new Rectangle(Point.Empty,display),canvas);
        Rectangle target=ScreenMap(map,canvas,display);
        if(percent<=100||!target.Contains(screen))return true;
        int endX=Math.Min(canvas.Width-1,((screen.X+1)*canvas.Width-1)/display.Width),endY=Math.Min(canvas.Height-1,((screen.Y+1)*canvas.Height-1)/display.Height);
        for(int y=native.Y;y<=endY;y++)for(int x=native.X;x<=endX;x++)if(protection[y*canvas.Width+x]==1)return true;
        RectangleF crop=Crop(map,percent);
        native=new Point(Math.Max(map.Left,Math.Min(map.Right-1,(int)Math.Floor(crop.Left+(screen.X-target.Left+.5)*crop.Width/target.Width))),
            Math.Max(map.Top,Math.Min(map.Bottom-1,(int)Math.Floor(crop.Top+(screen.Y-target.Top+.5)*crop.Height/target.Height))));
        // Never redirect a terrain click into a native popup or HUD control.
        return protection[native.Y*canvas.Width+native.X]!=1;
    }
}

// The world is enlarged directly to display pixels, avoiding a blurry second
// rescale. Original HUD/popup pixels are retained at the normal display scale.
internal sealed class WorldZoomPainter : IDisposable
{
    readonly PixelEdgeScaler scaler=new PixelEdgeScaler();
    Bitmap output,worldScaled;
    string axesKey;
    int[] ax,bx,wx,ay,by,wy,zax,zbx,zwx,zay,zby,zwy;
    internal unsafe Bitmap PaintNative(Bitmap normal,Bitmap world,Rectangle map,int percent,byte[] protection,Size display)
    {
        int w=normal.Width,h=normal.Height,ow=display.Width,oh=display.Height;
        if(ow<w||oh<h)throw new ArgumentException("Direct pixel-edge composition requires enlargement.");
        if(output==null||output.Size!=display){if(output!=null)output.Dispose();output=new Bitmap(ow,oh,PixelFormat.Format32bppRgb);}
        Rectangle dest=WorldZoomGeometry.ScreenMap(map,normal.Size,display);RectangleF crop=WorldZoomGeometry.Crop(map,percent);
        string key=w+":"+h+":"+ow+":"+oh+":"+map+":"+percent;
        if(axesKey!=key)
        {
            PixelEdgeScaler.Axis(w,ow,out ax,out bx,out wx);PixelEdgeScaler.Axis(h,oh,out ay,out by,out wy);
            PixelEdgeScaler.CropAxis(w,dest.Width,crop.Left,crop.Width,out zax,out zbx,out zwx);
            PixelEdgeScaler.CropAxis(h,dest.Height,crop.Top,crop.Height,out zay,out zby,out zwy);axesKey=key;
        }
        BitmapData root=null,terrain=null,target=null;
        try
        {
            root=normal.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
            terrain=world.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
            target=output.LockBits(new Rectangle(0,0,ow,oh),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
            for(int y=0;y<oh;y++)
            {
                uint* r0=(uint*)((byte*)root.Scan0+ay[y]*root.Stride),r1=(uint*)((byte*)root.Scan0+by[y]*root.Stride);
                uint* destRow=(uint*)((byte*)target.Scan0+y*target.Stride);
                bool worldY=y>=dest.Top&&y<dest.Bottom;int zy=y-dest.Top;
                uint* t0=worldY?(uint*)((byte*)terrain.Scan0+zay[zy]*terrain.Stride):null;
                uint* t1=worldY?(uint*)((byte*)terrain.Scan0+zby[zy]*terrain.Stride):null;
                int mask0=ay[y]*w,mask1=by[y]*w;
                for(int x=0;x<ow;x++)
                {
                    bool useWorld=worldY&&x>=dest.Left&&x<dest.Right&&
                        protection[mask0+ax[x]]==0&&protection[mask0+bx[x]]==0&&protection[mask1+ax[x]]==0&&protection[mask1+bx[x]]==0;
                    int ix=useWorld?x-dest.Left:x,x0=useWorld?zax[ix]:ax[ix],x1=useWorld?zbx[ix]:bx[ix],weightX=useWorld?zwx[ix]:wx[ix],weightY=useWorld?zwy[zy]:wy[y];
                    uint* top=useWorld?t0:r0;uint* bottom=useWorld?t1:r1;
                    uint a=weightX==0?top[x0]:PixelEdgeScaler.Mix(top[x0],top[x1],weightX);
                    if(weightY==0)destRow[x]=a;
                    else {uint b=weightX==0?bottom[x0]:PixelEdgeScaler.Mix(bottom[x0],bottom[x1],weightX);destRow[x]=PixelEdgeScaler.Mix(a,b,weightY);}
                }
            }
        }
        finally{if(root!=null)normal.UnlockBits(root);if(terrain!=null)world.UnlockBits(terrain);if(target!=null)output.UnlockBits(target);}
        return output;
    }
    internal Bitmap Paint(Bitmap normal,Bitmap world,Rectangle map,int percent,byte[] protection,FrameFilter filter)
    {
        if(output==null||output.Size!=normal.Size)
        {if(output!=null)output.Dispose();output=new Bitmap(normal.Width,normal.Height,PixelFormat.Format32bppRgb);}
        Rectangle dest=WorldZoomGeometry.ScreenMap(map,world.Size,normal.Size);
        RectangleF crop=WorldZoomGeometry.Crop(map,percent);
        if(dest.Width<1||dest.Height<1)return normal;
        Bitmap enlarged;
        if((filter==FrameFilter.PixelEdges||filter==FrameFilter.IntegerPixels)&&dest.Width>=crop.Width&&dest.Height>=crop.Height)
            enlarged=scaler.Scale(world,dest.Width,dest.Height,crop);
        else
        {
            if(worldScaled==null||worldScaled.Size!=dest.Size){if(worldScaled!=null)worldScaled.Dispose();worldScaled=new Bitmap(dest.Width,dest.Height,PixelFormat.Format32bppRgb);}
            using(var g=Graphics.FromImage(worldScaled))FramePainter.Draw(g,world,new Rectangle(Point.Empty,dest.Size),filter,crop);
            enlarged=worldScaled;
        }
        using(var g=Graphics.FromImage(output))g.DrawImageUnscaled(normal,0,0);
        RestoreWorld(output,enlarged,dest,world.Size,protection);return output;
    }
    internal static unsafe void RestoreWorld(Bitmap output,Bitmap world,Rectangle dest,Size canvas,byte[] protection)
    {
        BitmapData a=null,b=null;
        int outputWidth=output.Width,outputHeight=output.Height;
        try
        {
            a=output.LockBits(new Rectangle(Point.Empty,output.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppRgb);
            b=world.LockBits(new Rectangle(Point.Empty,world.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
            for(int y=0;y<dest.Height;y++)
            {
                int py=dest.Top+y,sy=py*canvas.Height/outputHeight,ey=Math.Min(canvas.Height-1,((py+1)*canvas.Height-1)/outputHeight);
                uint* target=(uint*)((byte*)a.Scan0+py*a.Stride)+dest.Left;
                uint* source=(uint*)((byte*)b.Scan0+y*b.Stride);
                for(int x=0;x<dest.Width;x++)
                {
                    int px=dest.Left+x,sx=px*canvas.Width/outputWidth,ex=Math.Min(canvas.Width-1,((px+1)*canvas.Width-1)/outputWidth);
                    bool ui=false;for(int yy=sy;yy<=ey&&!ui;yy++)for(int xx=sx;xx<=ex;xx++)if(protection[yy*canvas.Width+xx]!=0){ui=true;break;}
                    if(!ui)target[x]=source[x];
                }
            }
        }
        finally{if(a!=null)output.UnlockBits(a);if(b!=null)world.UnlockBits(b);}
    }
    public void Dispose(){scaler.Dispose();if(output!=null){output.Dispose();output=null;}if(worldScaled!=null){worldScaled.Dispose();worldScaled=null;}}
}
