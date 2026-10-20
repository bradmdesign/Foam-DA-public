using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
internal static class MapFeatureChecks
{
    static void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    internal static int Run()
    {
        int count=0;
        Check(MapScene.Project(new Point(535,275),42,20,43,20)==new Point(563,289),"Native X tile projection");count++;
        Check(MapScene.Project(new Point(535,275),42,20,42,21)==new Point(507,289),"Native Y tile projection");count++;
        Check(MapScene.Facing(0)==new Point(28,-14)&&MapScene.Facing(1)==new Point(28,14)&&MapScene.Facing(2)==new Point(-28,14)&&MapScene.Facing(3)==new Point(-28,-14)&&MapScene.Facing(7)==Point.Empty,"Four directional marker vectors and unknown direction");count++;
        foreach(var origin in new[]{Point.Empty,new Point(535,275),new Point(-25,-11)})
        {
            Check(!TileGrid.Edge(origin.X,origin.Y,origin)&&TileGrid.Edge(origin.X+28,origin.Y,origin)&&TileGrid.Edge(origin.X,origin.Y+14,origin),"Grid must outline, not cross, tile centers");count++;
            Check(TileGrid.Edge(origin.X+28+28,origin.Y+14,origin)&&TileGrid.Edge(origin.X-28,origin.Y,origin),"Grid periodicity for both tile axes and negative coordinates");count++;
        }
        var root=new[]{Color.Red.ToArgb(),Color.White.ToArgb(),Color.Black.ToArgb(),Color.Blue.ToArgb()};
        var world=new[]{Color.Blue.ToArgb(),Color.Blue.ToArgb(),Color.Black.ToArgb(),Color.Blue.ToArgb()};
        var protect=new byte[]{3,1,0,0};
        using(var layer=new TabMapLayer())
        {
            layer.Extract(root,world,protect,2,2,true);
            Check(layer.Visible&&root[0]==Color.Blue.ToArgb()&&root[1]==Color.White.ToArgb()&&protect[0]==0&&protect[1]==1,"Strip only the Tab overlay, preserving underlying world and UI");count++;
            using(var target=new Bitmap(2,2,PixelFormat.Format32bppRgb))using(var g=Graphics.FromImage(target))
            foreach(int fade in new[]{0,50,95})
            {
                g.Clear(Color.Blue);layer.Draw(g,new Rectangle(0,0,2,2),fade);Color c=target.GetPixel(0,0);
                Check(Math.Abs(c.R-255*(100-fade)/100)<=2&&Math.Abs(c.B-255*fade/100)<=2,"Tab opacity composition "+fade);count++;
                Check(target.GetPixel(1,0).B==255,"Transparent map gaps must preserve the world");count++;
            }
            layer.Extract(root,world,new byte[4],2,2,false);Check(!layer.Visible,"Travel/login transition clears stale Tab overlay");count++;
        }
        byte[] mask={1,0,0,0};var mapRaw=new byte[]{31,0,31,0,0,0,31,0};
        NativeLayerMask.ProtectPixels(mask,2,2,mapRaw,4,2,2,Point.Empty,new Rectangle(0,0,2,2),3);
        Check(mask[0]==1&&mask[1]==3&&mask[2]==0,"Map separation must never overwrite HUD protection");count++;
        NativeLayerMask.ProtectPixels(mask,2,2,mapRaw,4,2,2,Point.Empty,new Rectangle(0,0,2,2),2);
        Check(mask[1]==2&&mask[0]==1,"Notifications remain above the faded Tab map");count++;
        root=new[]{Color.White.ToArgb(),Color.White.ToArgb(),Color.Black.ToArgb(),Color.White.ToArgb()};world=(int[])root.Clone();
        TileGrid.Apply(root,world,new byte[]{1,0,0,0},2,new Rectangle(0,0,2,2),new Point(-28,0));
        Check(root[0]==Color.White.ToArgb()&&world[0]!=root[0]&&root[2]==Color.Black.ToArgb(),"Grid preserves UI and black/off-map gaps");count++;
        // Validate the actual memory layout used for camera, coordinates and
        // local-character direction with a bounded synthetic native tree.
        const uint image=0x400000,pane=0x100000,ctx=0x200000,list=0x200100,head=0x200200,node=0x200300,obj=0x200400;
        var bytes=new byte[0x1000];var header=new byte[0x300];
        Action<byte[],int,uint> put=(b,at,v)=>Array.Copy(BitConverter.GetBytes(v),0,b,at,4);
        put(header,0x20c,ctx);put(header,0x23c,42);put(header,0x238,20);put(header,0x12c,unchecked((uint)-25));put(header,0x128,unchecked((uint)-11));put(header,0x194,list);put(header,0x214,77);
        put(bytes,8,pane);put(bytes,0x14,286);put(bytes,0x18,560);put(bytes,0x1c,20);put(bytes,0x20,42);
        put(bytes,0x100,image+0x28ba70);put(bytes,0x120,head);put(bytes,0x204,node);
        put(bytes,0x300,head);put(bytes,0x308,head);put(bytes,0x30c,77);put(bytes,0x310,obj);put(bytes,0x400,image+0x28b700);bytes[0x592]=2;
        Action<uint,byte[],int> read=(at,buffer,length)=>{if(at==pane+0x20c){put(buffer,0,ctx);return;}Array.Copy(bytes,(int)(at-ctx),buffer,0,length);};
        var scene=MapSceneReader.Read(read,image,pane,header);
        Check(scene.Ready&&scene.Origin==new Point(535,275)&&scene.X==42&&scene.Y==20&&scene.Direction==2&&scene.Player==scene.Origin,"Read native projection and local-character facing together");count++;
        put(bytes,0x300,ctx+0x900);put(bytes,0x308,ctx+0x600);
        put(bytes,0x600,head);put(bytes,0x608,head);put(bytes,0x60c,88);put(bytes,0x610,ctx+0x700);
        put(bytes,0x700,image+0x28b584);put(bytes,0x740,20);put(bytes,0x744,43);bytes[0x8ec]=2;
        put(bytes,0x900,head);put(bytes,0x908,head);put(bytes,0x90c,66);put(bytes,0x910,ctx+0xa00);
        put(bytes,0xa00,image+0x28b154);put(bytes,0xa40,19);put(bytes,0xa44,41);
        Action<uint,byte[],int> selfOnly=(at,buffer,length)=>{if(at>=ctx+0x600)throw new InvalidOperationException("Other entity memory must not be read");read(at,buffer,length);};
        scene=MapSceneReader.Read(selfOnly,image,pane,header,true);
        Check(scene.Ready&&scene.Markers.Length==0&&scene.Direction==2,"Even when old callers request markers, no other entity body or branch is read");count++;
        bytes[0x8ec]=0;scene=MapSceneReader.Read(selfOnly,image,pane,header,true);
        Check(scene.Markers.Length==0&&scene.MarkersComplete,"No monster, NPC or invisible player markers are inferred from cached objects");count++;
        put(bytes,0x30c,70);put(bytes,0x308,node);scene=MapSceneReader.Read(read,image,pane,header);
        Check(scene.Ready&&scene.Direction==-1,"Malformed/cyclic ID tree must terminate and never invent a facing");count++;
        using(var options=new MapPanel())
        {Check(options.MiniMap.Checked&&!options.Grid.Checked&&options.TabTransparency.Value==0&&options.TabTransparency.Maximum==95,"Map defaults and near-invisible transparency endpoint");count++;}
        return count;
    }
}
