using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

// Geometry follows native render_world projection at 5D4730. Coordinates
// and buffers are published as one completed frame, never polled by painting.
internal sealed class MapScene
{
    internal static readonly MapScene Empty=new MapScene();
    internal bool Ready;
    internal Point Origin,Player;
    internal int X,Y,Direction=-1;
    internal int MapWidth,MapHeight;internal uint MapId;
    internal uint ListIdentity;
    internal MapMarker[] Markers=new MapMarker[0];
    internal bool MarkersComplete;
    internal static Point Project(Point origin,int baseX,int baseY,int x,int y)
    {return new Point(origin.X+28*((x-baseX)-(y-baseY)),origin.Y+14*((x-baseX)+(y-baseY)));}
    internal static Point Facing(int direction)
    {switch(direction){case 0:return new Point(28,-14);case 1:return new Point(28,14);case 2:return new Point(-28,14);case 3:return new Point(-28,-14);default:return Point.Empty;}}
}
internal enum MapMarkerKind {Monster,Npc,Player}
internal struct MapMarker
{
    internal Point Position;
    internal MapMarkerKind Kind;
    internal MapMarker(Point position,MapMarkerKind kind){Position=position;Kind=kind;}
}

internal static class MapSceneReader
{
    static int I(byte[] b,int n){return BitConverter.ToInt32(b,n);}
    static uint U(byte[] b,int n){return BitConverter.ToUInt32(b,n);}
    internal static MapScene Read(Action<uint,byte[],int> read,uint image,uint pane,byte[] header,bool includeEntities=false)
    {
        var word=new byte[4];Func<uint,uint> get=at=>{read(at,word,4);return U(word,0);};
        uint context=U(header,0x20c);if(context<0x10000)return MapScene.Empty;
        var projection=new byte[0x24];read(context,projection,projection.Length);
        if(U(projection,8)!=pane)return MapScene.Empty;
        int x=I(header,0x23c),y=I(header,0x238),bx=I(projection,0x20),by=I(projection,0x1c);
        var origin=new Point(I(projection,0x18)+I(header,0x12c),I(projection,0x14)+I(header,0x128));
        if(x<0||y<0||x>4096||y>4096||bx<0||by<0||bx>4096||by>4096||Math.Abs((long)origin.X)>8192||Math.Abs((long)origin.Y)>8192)return MapScene.Empty;
        var scene=new MapScene{Ready=true,Origin=origin,X=x,Y=y,Player=MapScene.Project(origin,bx,by,x,y),MapWidth=I(header,0x1c4),MapHeight=I(header,0x1c8),MapId=U(header,0x26c)};
        // WorldObjectList's bounded ordered ID tree (5E73B0). Read only the
        // local character, never inventory, other characters or packets.
        uint list=U(header,0x194),id=U(header,0x214);scene.ListIdentity=list;
        if(list>=0x10000&&get(list)==image+0x28ba70&&id!=0)
        {
            uint head=get(list+0x20),node=head>=0x10000?get(head+4):0;
            var entry=new byte[0x16];
            for(int n=0;n<64&&node>=0x10000&&node!=head;n++)
            {
                read(node,entry,entry.Length);if(entry[0x15]!=0)break;
                uint key=U(entry,0xc);
                if(key==id)
                {
                    uint obj=U(entry,0x10);if(obj<0x10000)break;
                    var body=new byte[0x194];read(obj,body,body.Length);
                    uint vt=U(body,0);
                    if(vt>=image+0x268000&&vt<image+0x2d0000&&body[0x192]<4)
                    {
                        scene.Direction=body[0x192];
                        // Native render displacements during walking are y,x.
                        int dy=I(body,0x38),dx=I(body,0x3c);
                        if(Math.Abs(dx)<=56&&Math.Abs(dy)<=28)scene.Player.Offset(dx,dy);
                    }
                    break;
                }
                uint next=U(entry,id<key?0:8);if(next==node)break;node=next;
            }
        }
        // Cached objects can include invisible characters. Never enumerate
        // them or infer visibility from their presence in client memory.
        // Both minimaps show the local player and rendered/cached map only.
        scene.MarkersComplete=true;
        if(get(pane+0x20c)!=context)return MapScene.Empty;
        for(int n=0x14;n<=0x20;n+=4)if(get(context+(uint)n)!=U(projection,n))return MapScene.Empty;
        return scene;
    }
}

internal sealed class TabMapLayer : IDisposable
{
    int[] pixels;GCHandle pin;
    internal Bitmap Bitmap;
    internal bool Visible;
    internal void Extract(int[] root,int[] world,byte[] protect,int width,int height,bool ready)
    {
        if(pixels==null||pixels.Length!=root.Length)
        {Dispose();pixels=new int[root.Length];pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);Bitmap=new Bitmap(width,height,width*4,PixelFormat.Format32bppArgb,pin.AddrOfPinnedObject());}
        Visible=false;Array.Clear(pixels,0,pixels.Length);if(!ready)return;
        for(int i=0;i<root.Length;i++)if(protect[i]==3)
        {
            if(!ready)throw new InvalidOperationException("Tab overlay has no matching world frame.");
            pixels[i]=root[i];root[i]=world[i];protect[i]=0;Visible=true;
        }
    }
    internal void Draw(Graphics g,Rectangle target,int transparency)
    {
        if(!Visible||Bitmap==null)return;
        var save=g.Save();try
        {
            g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
            using(var attributes=new ImageAttributes())
            {var matrix=new ColorMatrix();matrix.Matrix33=(100-Math.Max(0,Math.Min(95,transparency)))/100f;attributes.SetColorMatrix(matrix);g.DrawImage(Bitmap,target,0,0,Bitmap.Width,Bitmap.Height,GraphicsUnit.Pixel,attributes);}
        }
        finally{g.Restore(save);}
    }
    public void Dispose(){if(Bitmap!=null){Bitmap.Dispose();Bitmap=null;}if(pin.IsAllocated)pin.Free();pixels=null;Visible=false;}
}

internal static class TileGrid
{
    internal static bool GroundAt(byte[] world,int pitch,byte[] ground,int gp,int w,int h,int x,int y)
    {
        if(x<1||y<1||x>=w-1||y>=h-1)return false;
        // A one-pixel foreground margin prevents thin outlines from leaking
        // across wall and sprite edges when their colors match at one pixel.
        for(int yy=y-1;yy<=y+1;yy++)for(int xx=x-1;xx<=x+1;xx++)
        {int a=yy*pitch+xx*2,b=yy*gp+xx*2;if(world[a]!=ground[b]||world[a+1]!=ground[b+1])return false;}
        return true;
    }
    static void Tint16(byte[] pixels,int at,bool rgb565)
    {
        int c=pixels[at]|pixels[at+1]<<8,shift=rgb565?11:10,gm=rgb565?63:31;
        // Keep the outline readable on brown terrain, where a gold tint can
        // disappear into the ground texture. Dark floors get a light edge.
        int r=c>>shift&31,g=c>>5&gm,b=c&31;
        bool dark=r*2+g*62/gm+b<36;
        c=dark?(((r+24)/2)<<shift)|(((g+gm*3/4)/2)<<5)|((b+13)/2):((r/2)<<shift)|((g/2)<<5)|(b/2);
        pixels[at]=(byte)c;pixels[at+1]=(byte)(c>>8);
    }
    internal static int ApplyGround(byte[] output,byte[] root,byte[] protection,int width,int rootPitch,Rectangle rect,Point origin,byte[] raw,int pitch,byte[] ground,int gp,int w,int h,Point canvasOrigin,bool rgb565)
    {
        int count=0;
        for(int y=rect.Top;y<rect.Bottom;y++)for(int x=rect.Left;x<rect.Right;x++)
        {
            if(!Edge(x,y,origin))continue;
            int sx=x-canvasOrigin.X,sy=y-canvasOrigin.Y;
            if(!GroundAt(raw,pitch,ground,gp,w,h,sx,sy))continue;
            int to=(y*width+x)*2;if((output[to]|output[to+1])==0)continue;
            Tint16(output,to,rgb565);
            count++;
            if(protection[y*width+x]==0)Tint16(root,y*rootPitch+x*2,rgb565);
        }
        return count;
    }
    static int Mod(int n){int r=n%56;return r<0?r+56:r;}
    internal static bool Edge(int x,int y,Point origin)
    {int dx=x-origin.X,dy=y-origin.Y;return Mod(dx+2*dy+28)<2||Mod(dx-2*dy+28)<2;}
    internal static int Tint(int c)
    {return unchecked((int)0xff000000)|(((((c>>16)&255)*3+208)/4)<<16)|(((((c>>8)&255)*3+173)/4)<<8)|(((c&255)*3+108)/4);}
    internal static void Apply(int[] root,int[] world,byte[] protect,int width,Rectangle map,Point origin)
    {
        for(int y=map.Top;y<map.Bottom;y++)for(int x=map.Left;x<map.Right;x++)
        {
            int i=y*width+x;if(!Edge(x,y,origin))continue;
            if((world[i]&0xffffff)!=0)world[i]=Tint(world[i]);
            if(protect[i]==0&&(root[i]&0xffffff)!=0)root[i]=Tint(root[i]);
        }
    }
}

internal sealed class MiniMapControl : Control
{
    internal GameFrame Frame;
    internal bool Collapsed {get;private set;}
    internal event EventHandler CollapsedChanged;
    internal event EventHandler OverviewRequested;
    internal event EventHandler ZoomChanged;
    internal string MapName="Minimap";
    internal int PaintedFrames;
    readonly System.Diagnostics.Stopwatch liveClock=System.Diagnostics.Stopwatch.StartNew();
    long nextLivePaint;
    internal void PresentFrame()
    {
        if(!Visible||IsDisposed||!IsHandleCreated)return;
        // The minimap image is sampled at 10 Hz. Do not add a second 60 Hz
        // rendering workload to a large game view merely to show that sample.
        long now=liveClock.ElapsedMilliseconds;if(now<nextLivePaint)return;
        nextLivePaint=now+100;
        Invalidate();Update();
    }
    internal bool Standard;
    internal string GamePath;
    static readonly int[] ZoomLevels={10,25,50,75,100,125,150,175,200,225,250,275,300,325,350,375,400};
    int zoomPercent=100;
    Bitmap areaAtlas;uint areaAtlasId,requestedAtlasId;string areaAtlasPath,requestedAtlasPath;
    Task<Bitmap> atlasTask;DateTime atlasRetry=DateTime.MinValue;
    internal int ZoomPercent {get{return zoomPercent;}set{int next=Math.Max(10,Math.Min(400,value));if(next!=zoomPercent){zoomPercent=next;Invalidate();}}}
    internal Rectangle ZoomOutBounds {get{return new Rectangle(Width-83,2,25,21);}}
    internal Rectangle ZoomInBounds {get{return new Rectangle(Width-56,2,25,21);}}
    internal Rectangle CollapseBounds {get{return new Rectangle(Width-29,1,28,23);}}
    internal MiniMapControl()
    {SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Font=DATheme.Font(DATheme.CompanionFontPixels);Cursor=Cursors.Arrow;TabStop=false;AccessibleName="Minimap; plus and minus zoom, click map to open the area map, right arrow collapses";}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        HandleClick(e.Location);
    }
    internal void HandleClick(Point point)
    {
        if(point.Y<25)
        {
            if(CollapseBounds.Contains(point)){Collapsed=!Collapsed;if(CollapsedChanged!=null)CollapsedChanged(this,EventArgs.Empty);Invalidate();return;}
            int step=ZoomInBounds.Contains(point)?1:ZoomOutBounds.Contains(point)?-1:0;
            if(step!=0){int previous=ZoomPercent;ZoomPercent=NextZoom(ZoomPercent,step);if(ZoomPercent!=previous&&ZoomChanged!=null)ZoomChanged(this,EventArgs.Empty);return;}
        }
        if(!Collapsed&&OverviewRequested!=null)OverviewRequested(this,EventArgs.Empty);
    }
    internal static int NextZoom(int current,int direction)
    {
        if(direction>0){foreach(int level in ZoomLevels)if(level>current)return level;return ZoomLevels[ZoomLevels.Length-1];}
        for(int i=ZoomLevels.Length-1;i>=0;i--)if(ZoomLevels[i]<current)return ZoomLevels[i];return ZoomLevels[0];
    }
    bool EnsureAreaAtlas()
    {
        if(Frame==null||!Frame.Scene.Ready||String.IsNullOrEmpty(GamePath))return false;
        var scene=Frame.Scene;uint mapId=scene.MapId;
        if(scene.MapWidth<1||scene.MapHeight<1||scene.MapWidth>512||scene.MapHeight>512)return false;
        string path=Path.Combine(Path.GetDirectoryName(GamePath),"maps","lod"+mapId+".map");
        if(areaAtlas!=null&&areaAtlasId==mapId&&areaAtlasPath==path)return true;
        if(atlasTask!=null&&atlasTask.IsCompleted)
        {
            Bitmap loaded=null;try{loaded=atlasTask.Result;}catch{}
            atlasTask=null;
            if(loaded!=null&&requestedAtlasId==mapId&&requestedAtlasPath==path)
            {if(areaAtlas!=null)areaAtlas.Dispose();areaAtlas=loaded;areaAtlasId=mapId;areaAtlasPath=path;return true;}
            if(loaded!=null)loaded.Dispose();atlasRetry=DateTime.UtcNow.AddSeconds(5);
        }
        if(atlasTask!=null||DateTime.UtcNow<atlasRetry)return false;
        requestedAtlasId=mapId;requestedAtlasPath=path;
        int width=scene.MapWidth,height=scene.MapHeight;
        atlasTask=Task.Run(()=>
        {
            try
            {
                var file=new FileInfo(path);if(!file.Exists||file.Length!=(long)width*height*6)return null;
                return OverviewCanvas.Build(File.ReadAllBytes(path),width,height);
            }
            catch{return null;}
        });
        atlasTask.ContinueWith(t=>{try{if(IsHandleCreated&&!IsDisposed)BeginInvoke((Action)(()=>Invalidate()));}catch{}});
        return false;
    }
    bool DrawAreaAtlas(Graphics g,Rectangle area)
    {
        if(!EnsureAreaAtlas())return false;
        var scene=Frame.Scene;var live=Frame.WorldRectangle;
        if(live.Width<1||live.Height<1)return false;
        PointF player=OverviewCanvas.Project(scene.X,scene.Y,scene.MapHeight);
        var source=AreaSourceRectangle(areaAtlas.Size,live,player,ZoomPercent);if(source.IsEmpty)return false;
        float scale=Math.Min(area.Width/source.Width,area.Height/source.Height);
        var target=new RectangleF(area.X+(area.Width-source.Width*scale)/2,area.Y+(area.Height-source.Height*scale)/2,source.Width*scale,source.Height*scale);
        g.InterpolationMode=InterpolationMode.NearestNeighbor;
        g.DrawImage(areaAtlas,target,source,GraphicsUnit.Pixel);
        foreach(var marker in scene.Markers)
        {
            float dx=marker.Position.X-scene.Player.X,dy=marker.Position.Y-scene.Player.Y;
            double tileX=scene.X+(dx/28+dy/14)/2.0,tileY=scene.Y+(dy/14-dx/28)/2.0;
            PointF point=OverviewCanvas.Project(tileX,tileY,scene.MapHeight);
            float mx=target.X+(point.X-source.X)*scale,my=target.Y+(point.Y-source.Y)*scale;
            if(mx<area.Left||my<area.Top||mx>=area.Right||my>=area.Bottom)continue;
            Color color=marker.Kind==MapMarkerKind.Monster?Color.FromArgb(230,76,61):marker.Kind==MapMarkerKind.Npc?Color.FromArgb(86,156,245):Color.FromArgb(69,204,112);
            using(var fill=new SolidBrush(color))g.FillEllipse(fill,mx-3,my-3,6,6);
            g.DrawEllipse(Pens.Black,mx-3,my-3,6,6);
        }
        float px=target.X+(player.X-source.X)*scale,py=target.Y+(player.Y-source.Y)*scale;
        DrawPlayer(g,scene.Direction,px,py);
        return true;
    }
    internal static RectangleF AreaSourceRectangle(Size atlas,Rectangle live,PointF player,int zoomPercent)
    {
        if(atlas.Width<1||atlas.Height<1||live.Width<1||live.Height<1)return RectangleF.Empty;
        float scale=100f/Math.Max(10,Math.Min(100,zoomPercent));
        float width=Math.Max(1,Math.Min(atlas.Width,live.Width/7f*scale));
        float height=Math.Max(1,Math.Min(atlas.Height,live.Height/7f*scale));
        float x=Math.Max(0,Math.Min(atlas.Width-width,player.X-width/2));
        float y=Math.Max(0,Math.Min(atlas.Height-height,player.Y-height/2));
        return new RectangleF(x,y,width,height);
    }
    static void DrawPlayer(Graphics g,int direction,float px,float py)
    {
        Point facing=MapScene.Facing(direction);
        if(facing==Point.Empty){g.FillEllipse(Brushes.Gold,px-3,py-3,6,6);return;}
        double angle=Math.Atan2(facing.Y,facing.X);float dx=(float)Math.Cos(angle),dy=(float)Math.Sin(angle);
        var arrow=new[]{new PointF(px+dx*9,py+dy*9),new PointF(px-dx*5-dy*5,py-dy*5+dx*5),new PointF(px-dx*2,py-dy*2),new PointF(px-dx*5+dy*5,py-dy*5-dx*5)};
        using(var fill=new SolidBrush(Color.FromArgb(250,209,87)))g.FillPolygon(fill,arrow);g.DrawPolygon(Pens.Black,arrow);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){if(areaAtlas!=null){areaAtlas.Dispose();areaAtlas=null;}if(atlasTask!=null){var pending=atlasTask;atlasTask=null;pending.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion&&t.Result!=null)t.Result.Dispose();});}}
        base.Dispose(disposing);
    }
    internal static Rectangle TargetRectangle(Rectangle area,Rectangle map,Point player,int zoomPercent)
    {
        if(area.Width<=0||area.Height<=0||map.Width<=0||map.Height<=0)return Rectangle.Empty;
        double scale=Math.Min(area.Width/(double)map.Width,area.Height/(double)map.Height)*Math.Max(100,Math.Min(400,zoomPercent))/100.0;
        int width=Math.Max(1,(int)Math.Round(map.Width*scale)),height=Math.Max(1,(int)Math.Round(map.Height*scale));
        int x=area.X+(area.Width-width)/2,y=area.Y+(area.Height-height)/2;
        if(width>area.Width)x=Math.Max(area.Right-width,Math.Min(area.Left,area.Left+area.Width/2-(int)Math.Round((player.X-map.X)*scale)));
        if(height>area.Height)y=Math.Max(area.Bottom-height,Math.Min(area.Top,area.Top+area.Height/2-(int)Math.Round((player.Y-map.Y)*scale)));
        return new Rectangle(x,y,width,height);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        PaintedFrames++;var g=e.Graphics;DATheme.Paint(g,ClientRectangle,false);
        TextRenderer.DrawText(g,MapName,Font,new Rectangle(7,3,Math.Max(1,Width-93),20),DATheme.Gold,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        using(var border=new Pen(DATheme.Gold))
        {
            g.DrawRectangle(border,ZoomOutBounds);g.DrawRectangle(border,ZoomInBounds);
        }
        TextRenderer.DrawText(g,"-",Font,ZoomOutBounds,ZoomPercent>10?DATheme.Gold:Color.FromArgb(110,83,55),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g,"+",Font,ZoomInBounds,ZoomPercent<400?DATheme.Gold:Color.FromArgb(110,83,55),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        using(var pen=new Pen(DATheme.Gold,2))
        {int cx=Width-16,cy=12;g.DrawLines(pen,Collapsed?new[]{new Point(cx-5,cy-3),new Point(cx,cy+3),new Point(cx+5,cy-3)}:new[]{new Point(cx-5,cy+3),new Point(cx,cy-3),new Point(cx+5,cy+3)});}
        if(Collapsed)return;
        var area=new Rectangle(5,26,Width-10,Height-49);g.FillRectangle(Brushes.Black,area);
        if(Frame==null||!Frame.WorldReady||!Frame.Scene.Ready)return;
        var map=Frame.WorldRectangle;
        var target=TargetRectangle(area,map,Frame.Scene.Player,ZoomPercent);if(target.IsEmpty)return;
        float scaleX=target.Width/(float)map.Width,scaleY=target.Height/(float)map.Height;
        var clip=g.Save();g.SetClip(area);
        if(ZoomPercent<100&&DrawAreaAtlas(g,area))
        {
            g.Restore(clip);
            TextRenderer.DrawText(g,ZoomPercent+"%",Font,new Rectangle(6,Height-22,58,19),DATheme.Gold,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g,"X: "+Frame.Scene.X+"   Y: "+Frame.Scene.Y,Font,new Rectangle(5,Height-22,Width-10,19),DATheme.Gold,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            return;
        }
        g.InterpolationMode=Standard?InterpolationMode.Low:InterpolationMode.HighQualityBilinear;
        if(Standard)
        {
            using(var attributes=new ImageAttributes())
            {
                var matrix=new ColorMatrix(new float[][]{
                    new float[]{.34f,.28f,.18f,0,0},new float[]{.34f,.28f,.18f,0,0},new float[]{.34f,.28f,.18f,0,0},
                    new float[]{0,0,0,1,0},new float[]{0,0,0,0,1}});
                attributes.SetColorMatrix(matrix);g.DrawImage(Frame.WorldBitmap,target,map.X,map.Y,map.Width,map.Height,GraphicsUnit.Pixel,attributes);
            }
            foreach(var marker in Frame.Scene.Markers)
            {
                float mx=target.X+(marker.Position.X-map.X)*scaleX,my=target.Y+(marker.Position.Y-map.Y)*scaleY;
                if(mx<area.Left||my<area.Top||mx>=area.Right||my>=area.Bottom)continue;
                Color color=marker.Kind==MapMarkerKind.Monster?Color.FromArgb(230,76,61):marker.Kind==MapMarkerKind.Npc?Color.FromArgb(86,156,245):Color.FromArgb(69,204,112);
                using(var fill=new SolidBrush(color))g.FillEllipse(fill,mx-4,my-4,8,8);
                g.DrawEllipse(Pens.Black,mx-4,my-4,8,8);
                using(var edge=new Pen(color,1.4f))g.DrawArc(edge,mx-6,my+1,12,8,10,160);
            }
        }
        else g.DrawImage(Frame.WorldBitmap,target,map,GraphicsUnit.Pixel);
        float px=target.X+(Frame.Scene.Player.X-map.X)*scaleX,py=target.Y+(Frame.Scene.Player.Y-map.Y)*scaleY;
        DrawPlayer(g,Frame.Scene.Direction,px,py);
        g.Restore(clip);
        TextRenderer.DrawText(g,ZoomPercent+"%",Font,new Rectangle(6,Height-22,58,19),DATheme.Gold,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g,"X: "+Frame.Scene.X+"   Y: "+Frame.Scene.Y,Font,new Rectangle(5,Height-22,Width-10,19),DATheme.Gold,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
    }
}
