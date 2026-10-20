using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

internal sealed class MapNote
{
    public int X,Y;public string Kind,Text;
}
internal sealed class MapOverview : DAPanel
{
    readonly OverviewCanvas canvas=new OverviewCanvas();
    readonly Label caption=new Label();
    readonly ComboBox sizeChoice=new ComboBox();
    internal event EventHandler Closed;
    internal event EventHandler LayoutChanged;
    internal bool Full {get{return sizeChoice.SelectedIndex==1;}set{sizeChoice.SelectedIndex=value?1:0;}}
    internal MapOverview()
    {
        Font=DATheme.Font(DATheme.CompanionFontPixels);ForeColor=DATheme.Gold;
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=30,WrapContents=false};
        caption.Width=180;caption.Height=27;caption.TextAlign=ContentAlignment.MiddleLeft;bar.Controls.Add(caption);
        sizeChoice.DropDownStyle=ComboBoxStyle.DropDownList;sizeChoice.Items.AddRange(new object[]{"Basic","Full"});
        sizeChoice.Width=92;sizeChoice.Font=Font;sizeChoice.BackColor=DATheme.Dark;sizeChoice.ForeColor=DATheme.Gold;sizeChoice.FlatStyle=FlatStyle.Flat;sizeChoice.AccessibleName="Area map size";
        sizeChoice.SelectedIndex=1;sizeChoice.SelectedIndexChanged+=delegate{if(LayoutChanged!=null)LayoutChanged(this,EventArgs.Empty);canvas.Focus();};bar.Controls.Add(sizeChoice);
        foreach(string title in new[]{"−","+","Fit","Close"})
        {
            string action=title;var b=new DAButton{Text=title,Width=title.Length==1?32:55,Height=26,Margin=new Padding(1)};bar.Controls.Add(b);
            b.Click+=delegate{if(action=="Close")CloseMap();else canvas.SetZoom(action=="Fit"?1:canvas.Zoom*(action=="+"?1.25:.8));};
        }
        canvas.Dock=DockStyle.Fill;
        var note=new Label{Dock=DockStyle.Bottom,Height=35,Text="Wheel: map zoom · drag: pan · right-click: mark entrance, exit or note.\nGold: your position · saved notes are local to this computer."};
        Controls.Add(canvas);Controls.Add(note);Controls.Add(bar);
    }
    internal static Rectangle BoundsFor(Size area,bool full)
    {
        int margin=full?12:20,w=Math.Max(1,area.Width-2*margin),h=Math.Max(1,area.Height-2*margin);
        if(!full){w=Math.Min(900,w);h=Math.Min(680,h);}
        return new Rectangle(Math.Max(0,(area.Width-w)/2),Math.Max(0,(area.Height-h)/2),w,h);
    }
    void CloseMap(){Visible=false;if(Closed!=null)Closed(this,EventArgs.Empty);}
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {if(keyData==Keys.Escape){CloseMap();return true;}return base.ProcessCmdKey(ref msg,keyData);}
    internal void ShowMap(HostedSession session)
    {
        canvas.Open(session);caption.Text="Map "+session.Frame.Scene.MapId;Visible=true;BringToFront();canvas.Focus();
    }
    internal void RefreshScene(HostedSession session)
    {
        if(!Visible)return;
        if(session==null||!session.Frame.InWorld){Visible=false;return;}
        if(!canvas.IsMap(session)){Visible=false;return;}canvas.Invalidate();
    }
    protected override void Dispose(bool disposing){if(disposing)canvas.Dispose();base.Dispose(disposing);}
}

internal sealed class OverviewCanvas : Control
{
    HostedSession session;uint mapId;int columns,rows;Bitmap map;string notesPath,error;
    List<MapNote> notes=new List<MapNote>();
    internal double Zoom=1;
    PointF pan;Point drag;bool dragging;
    internal OverviewCanvas(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.Selectable,true);TabStop=true;BackColor=Color.Black;Cursor=Cursors.Hand;}
    internal static PointF Project(double x,double y,int height){return new PointF((float)((x-y+height)*4),(float)((x+y+1)*2));}
    internal static Point Tile(PointF point,int height)
    {double a=point.X/4-height,b=point.Y/2-1;return new Point((int)Math.Round((a+b)/2),(int)Math.Round((b-a)/2));}
    internal bool IsMap(HostedSession s){return session==s&&s.Frame.Scene.MapId==mapId;}
    internal void Open(HostedSession value)
    {
        session=value;var scene=value.Frame.Scene;mapId=scene.MapId;columns=scene.MapWidth;rows=scene.MapHeight;SetZoom(1);error=null;notes.Clear();
        if(map!=null){map.Dispose();map=null;}
        try
        {
            if(columns<1||rows<1||columns>512||rows>512)throw new InvalidDataException();
            string file=Path.Combine(Path.GetDirectoryName(value.GamePath),"maps","lod"+mapId+".map");
            var info=new FileInfo(file);if(!info.Exists||info.Length!=(long)columns*rows*6)throw new InvalidDataException();
            byte[] cells=File.ReadAllBytes(file);map=Build(cells,columns,rows);
            notesPath=Path.Combine(LocalProfile.Root,"map-notes",LocalProfile.CharacterKey("map"+mapId,value.Server.Key)+".json");
            if(File.Exists(notesPath)&&new FileInfo(notesPath).Length<128*1024)
            {
                var loaded=Program.Json.Deserialize<List<MapNote>>(File.ReadAllText(notesPath));
                if(loaded!=null&&loaded.Count<=200)notes=loaded.FindAll(n=>n!=null&&n.X>=0&&n.Y>=0&&n.X<columns&&n.Y<rows&&n.Text!=null&&n.Text.Length<=80);
            }
        }
        catch{error="The complete local map is unavailable. Re-enter this area to let the game cache it.";}
        Invalidate();
    }
    internal static Bitmap Build(byte[] cells,int w,int h)
    {
        if(w<1||h<1||w>512||h>512||cells.Length!=(long)w*h*6)throw new ArgumentException("Invalid map dimensions.");
        var bitmap=new Bitmap((w+h+2)*4,(w+h+2)*2);
        using(var g=Graphics.FromImage(bitmap))using(var floor=new SolidBrush(Color.FromArgb(80,83,59)))using(var wall=new SolidBrush(Color.FromArgb(147,120,81)))
        {
            g.Clear(Color.Black);g.SmoothingMode=SmoothingMode.None;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int at=(y*w+x)*6;bool foreground=(cells[at+2]|cells[at+3]|cells[at+4]|cells[at+5])!=0;
                PointF p=Project(x,y,h);
                g.FillPolygon(foreground?wall:floor,new[]{new PointF(p.X,p.Y-2),new PointF(p.X+4,p.Y),new PointF(p.X,p.Y+2),new PointF(p.X-4,p.Y)});
            }
        }
        return bitmap;
    }
    internal void SetZoom(double value){Zoom=Math.Max(.5,Math.Min(8,value));if(Zoom==1)pan=PointF.Empty;Invalidate();}
    RectangleF Destination()
    {
        if(map==null)return RectangleF.Empty;
        double fit=Math.Min((ClientSize.Width-24)/(double)map.Width,(ClientSize.Height-24)/(double)map.Height),scale=Math.Max(.01,fit*Zoom);
        return new RectangleF((float)((Width-map.Width*scale)/2+pan.X),(float)((Height-map.Height*scale)/2+pan.Y),(float)(map.Width*scale),(float)(map.Height*scale));
    }
    PointF Screen(double x,double y,RectangleF target)
    {PointF p=Project(x,y,rows);return new PointF(target.X+p.X*target.Width/map.Width,target.Y+p.Y*target.Height/map.Height);}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Black);
        if(error!=null||map==null){TextRenderer.DrawText(e.Graphics,error??"Loading map…",Font,ClientRectangle,DATheme.Gold,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);return;}
        var target=Destination();e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.DrawImage(map,target);
        var scene=session.Frame.Scene;if(!scene.Ready)return;
        foreach(var marker in scene.Markers)
        {
            // Invert the same camera projection used by the live marker.
            double dx=marker.Position.X-scene.Player.X,dy=marker.Position.Y-scene.Player.Y;
            PointF at=Screen(scene.X+dx/56+dy/28,scene.Y-dx/56+dy/28,target);
            using(var brush=new SolidBrush(marker.Kind==MapMarkerKind.Monster?Color.IndianRed:marker.Kind==MapMarkerKind.Npc?Color.CornflowerBlue:Color.LimeGreen))e.Graphics.FillEllipse(brush,at.X-3,at.Y-3,6,6);
        }
        foreach(var n in notes)
        {
            PointF at=Screen(n.X,n.Y,target);e.Graphics.DrawEllipse(Pens.Gold,at.X-5,at.Y-5,10,10);
            TextRenderer.DrawText(e.Graphics,n.Kind+": "+n.Text,Font,new Point((int)at.X+7,(int)at.Y-8),Color.Gold,Color.Black,TextFormatFlags.NoPrefix);
        }
        PointF me=Screen(scene.X,scene.Y,target);Point facing=MapScene.Facing(scene.Direction);double angle=Math.Atan2(facing.Y,facing.X);float fx=(float)Math.Cos(angle),fy=(float)Math.Sin(angle);
        var arrow=new[]{new PointF(me.X+fx*10,me.Y+fy*10),new PointF(me.X-fx*6-fy*6,me.Y-fy*6+fx*6),new PointF(me.X-fx*6+fy*6,me.Y-fy*6-fx*6)};
        if(facing==Point.Empty){e.Graphics.FillEllipse(Brushes.Gold,me.X-5,me.Y-5,10,10);e.Graphics.DrawEllipse(Pens.Black,me.X-5,me.Y-5,10,10);}
        else{e.Graphics.FillPolygon(Brushes.Gold,arrow);e.Graphics.DrawPolygon(Pens.Black,arrow);}
        TextRenderer.DrawText(e.Graphics,"You  "+scene.X+", "+scene.Y+"   ·   "+(Zoom*100).ToString("0")+"%\nOlive: ground · tan: map objects/walls · schematic map",Font,new Point(8,8),DATheme.Gold,Color.Black);
    }
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);SetZoom(Zoom*(e.Delta>0?1.25:.8));}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);Focus();if(map==null)return;
        if(e.Button==MouseButtons.Left){drag=e.Location;dragging=true;Capture=true;}
        if(e.Button==MouseButtons.Right)
        {
            RectangleF target=Destination();Point tile=Tile(new PointF((e.X-target.X)*map.Width/target.Width,(e.Y-target.Y)*map.Height/target.Height),rows);
            if(tile.X<0||tile.Y<0||tile.X>=columns||tile.Y>=rows)return;
            var menu=new ContextMenuStrip();
            foreach(string type in new[]{"Entrance","Exit","Note","Remove marker"})
            {
                string kind=type;menu.Items.Add(kind,null,delegate
                {
                    if(kind=="Remove marker"){notes.RemoveAll(n=>n.X==tile.X&&n.Y==tile.Y);SaveNotes();return;}
                    using(var form=new Form{Text=kind+" at "+tile.X+", "+tile.Y,ClientSize=new Size(350,90),StartPosition=FormStartPosition.CenterParent,BackColor=DATheme.Dark,ForeColor=DATheme.Gold})
                    {
                        var box=new TextBox{Bounds=new Rectangle(12,12,326,24),MaxLength=80};var save=new DAButton{Text="Save note",Bounds=new Rectangle(240,48,98,28),DialogResult=DialogResult.OK};form.Controls.Add(box);form.Controls.Add(save);form.AcceptButton=save;
                        if(form.ShowDialog(this)==DialogResult.OK&&!String.IsNullOrWhiteSpace(box.Text)&&notes.Count<200){notes.RemoveAll(n=>n.X==tile.X&&n.Y==tile.Y);notes.Add(new MapNote{X=tile.X,Y=tile.Y,Kind=kind,Text=box.Text.Trim()});SaveNotes();}
                    }
                });
            }
            menu.Closed+=delegate{BeginInvoke(new Action(menu.Dispose));};menu.Show(this,e.Location);
        }
    }
    void SaveNotes(){try{LocalProfile.Write(notesPath,Program.Json.Serialize(notes));Invalidate();}catch{MessageBox.Show(this,"The local map note could not be saved.");}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;pan.X+=e.X-drag.X;pan.Y+=e.Y-drag.Y;drag=e.Location;Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;}
    protected override void Dispose(bool disposing){if(disposing&&map!=null)map.Dispose();base.Dispose(disposing);}
}
