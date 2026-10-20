using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

internal static class DATheme
{
    internal static readonly Color Gold=Color.FromArgb(219,190,142), Dark=Color.FromArgb(42,30,19);
    static readonly string Folder=Path.Combine(Program.Root,"theme");
    static readonly Bitmap Grain=new Bitmap(Path.Combine(Folder,"grain.png"));
    static readonly Bitmap Frame=new Bitmap(Path.Combine(Folder,"panel-frame.png"));
    static readonly Bitmap Input=new Bitmap(Path.Combine(Folder,"input-frame.png"));
    static readonly PrivateFontCollection Fonts=new PrivateFontCollection();
    static DATheme(){Fonts.AddFontFile(Path.Combine(Folder,"da-local.ttf"));}
    internal const int CompanionFontPixels=14;
    internal static void StyleCompanion(Control root)
    {
        root.Font=Font(CompanionFontPixels);
        StyleCompanionButtons(root);
    }
    static void StyleCompanionButtons(Control root)
    {
        foreach(Control child in root.Controls)
        {
            if(child is DAButton)child.Font=root.Font;
            StyleCompanionButtons(child);
        }
    }
    internal static Font Font(float pixels){return new Font(Fonts.Families[0],pixels,FontStyle.Regular,GraphicsUnit.Pixel);}
    internal static void Paint(Graphics g,Rectangle bounds,bool button)
    {
        if(bounds.Width<12||bounds.Height<12)return;
        using(var brush=new TextureBrush(Grain,WrapMode.Tile))g.FillRectangle(brush,bounds);
        Bitmap image=button?Input:Frame;
        const int edge=5;
        int[] sx={0,edge,image.Width-edge,image.Width},sy={0,edge,image.Height-edge,image.Height};
        int[] dx={bounds.Left,bounds.Left+edge,bounds.Right-edge,bounds.Right},dy={bounds.Top,bounds.Top+edge,bounds.Bottom-edge,bounds.Bottom};
        g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        for(int y=0;y<3;y++)for(int x=0;x<3;x++)
        {
            if(x==1&&y==1)continue;
            g.DrawImage(image,Rectangle.FromLTRB(dx[x],dy[y],dx[x+1],dy[y+1]),sx[x],sy[y],sx[x+1]-sx[x],sy[y+1]-sy[y],GraphicsUnit.Pixel);
        }
    }
}

internal sealed class DAButton : Button
{
    internal int ActionGlyph;
    bool over,down;
    internal DAButton()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        Font=DATheme.Font(12);ForeColor=DATheme.Gold;BackColor=DATheme.Dark;
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
    }
    protected override void OnMouseEnter(EventArgs e){over=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){over=false;down=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        DATheme.Paint(e.Graphics,ClientRectangle,true);
        if(over)using(var tint=new SolidBrush(Color.FromArgb(down?55:22,255,194,96)))e.Graphics.FillRectangle(tint,Rectangle.Inflate(ClientRectangle,-4,-4));
        if(ActionGlyph!=0){using(var ink=new SolidBrush(Enabled?ForeColor:Color.FromArgb(102,85,64))){int x=Width/2,y=Height/2;if(ActionGlyph==1)e.Graphics.FillPolygon(ink,new[]{new Point(x-6,y-7),new Point(x+8,y),new Point(x-6,y+7)});else e.Graphics.FillRectangle(ink,x-7,y-7,14,14);}return;}
        e.Graphics.TextRenderingHint=TextRenderingHint.SingleBitPerPixelGridFit;
        using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
        using(var text=new SolidBrush(Enabled?ForeColor:Color.FromArgb(126,104,75)))
        {
            Rectangle rect=ClientRectangle;if(down)rect.Offset(1,1);
            e.Graphics.DrawString(Text,Font,text,rect,format);
        }
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),ForeColor,BackColor);
    }
}

internal sealed class DAGameTab : FlowLayoutPanel
{
    internal readonly Button SelectButton=new DAButton(),CloseButton=new DAButton();
    internal DAGameTab(string title,Action select,Action close)
    {
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;WrapContents=false;
        Margin=new Padding(2,0,2,0);Padding=Padding.Empty;BackColor=DATheme.Dark;
        SelectButton.AutoSize=true;SelectButton.Height=26;SelectButton.Margin=Padding.Empty;
        CloseButton.Text="×";CloseButton.Font=SystemFonts.MessageBoxFont;
        CloseButton.Size=new Size(26,26);CloseButton.Margin=Padding.Empty;
        CloseButton.AccessibleName="Close game tab";
        SelectButton.Click+=delegate{select();};CloseButton.Click+=delegate{close();};
        Controls.Add(SelectButton);Controls.Add(CloseButton);Text=title;
    }
    public override string Text
    {
        get{return SelectButton==null?base.Text:SelectButton.Text;}
        set{base.Text=value;if(SelectButton!=null){SelectButton.Text=value;SelectButton.AccessibleName=value;CloseButton.AccessibleDescription="Close "+value;}}
    }
}

internal class DAPanel : Panel
{
    internal DAPanel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=DATheme.Dark;Padding=new Padding(5);}
    protected override void OnPaintBackground(PaintEventArgs e){DATheme.Paint(e.Graphics,ClientRectangle,false);}
}

internal sealed class DACheckBox : CheckBox
{
    internal DACheckBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);ForeColor=DATheme.Gold;BackColor=DATheme.Dark;}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(BackColor);
        int top=Padding.Top+Math.Max(0,(Height-Padding.Vertical-13)/2);
        var box=new Rectangle(Padding.Left,top,12,12);
        using(var b=new SolidBrush(Checked?Color.FromArgb(107,70,37):Color.FromArgb(34,23,15)))g.FillRectangle(b,box);
        using(var p=new Pen(Enabled?Color.FromArgb(167,125,75):Color.FromArgb(86,67,43)))g.DrawRectangle(p,box);
        if(Checked)using(var p=new Pen(Enabled?DATheme.Gold:Color.FromArgb(112,91,59),2))g.DrawLines(p,new[]{new Point(box.X+2,box.Y+6),new Point(box.X+5,box.Y+9),new Point(box.X+10,box.Y+3)});
        TextRenderer.DrawText(g,Text,Font,new Rectangle(box.Right+5,Padding.Top,Math.Max(1,Width-box.Right-5),Height-Padding.Vertical),Enabled?ForeColor:Color.FromArgb(112,91,59),TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-1,-1),DATheme.Gold,BackColor);
    }
}

internal sealed class DASlider : Control
{
    int minimum,maximum=100,value;bool dragging;
    internal int Minimum {get{return minimum;}set{minimum=value;Value=this.value;Invalidate();}}
    internal int Maximum {get{return maximum;}set{maximum=value;Value=this.value;Invalidate();}}
    internal int SmallChange=1,LargeChange=5;
    internal int Value {get{return value;}set{int next=Math.Max(minimum,Math.Min(maximum,value));if(this.value==next)return;this.value=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);}}
    internal event EventHandler ValueChanged;
    internal DASlider(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);TabStop=true;Size=new Size(145,26);BackColor=DATheme.Dark;AccessibleRole=AccessibleRole.Slider;}
    void At(int x){Value=minimum+(int)Math.Round((maximum-minimum)*Math.Max(0,Math.Min(1,(x-7.0)/Math.Max(1,Width-15))));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();dragging=true;Capture=true;At(e.X);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)At(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left){dragging=false;Capture=false;}}
    protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)dragging=false;base.OnMouseCaptureChanged(e);}
    protected override bool IsInputKey(Keys key){Keys k=key&Keys.KeyCode;return k==Keys.Left||k==Keys.Right||k==Keys.Up||k==Keys.Down||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);switch(e.KeyCode){case Keys.Left:case Keys.Down:Value-=SmallChange;break;case Keys.Right:case Keys.Up:Value+=SmallChange;break;case Keys.PageDown:Value-=LargeChange;break;case Keys.PageUp:Value+=LargeChange;break;case Keys.Home:Value=minimum;break;case Keys.End:Value=maximum;break;default:return;}e.Handled=true;e.SuppressKeyPress=true;}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);Value+=Math.Sign(e.Delta)*SmallChange;}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(BackColor);int mid=Height/2;
        var track=new Rectangle(7,mid-3,Math.Max(1,Width-15),6);
        using(var b=new SolidBrush(Color.FromArgb(31,20,12)))g.FillRectangle(b,track);
        using(var p=new Pen(Color.FromArgb(135,93,49)))g.DrawRectangle(p,track);
        int x=7+(int)Math.Round((Width-15)*(value-minimum)/(double)Math.Max(1,maximum-minimum));
        using(var b=new SolidBrush(Color.FromArgb(131,85,40)))g.FillRectangle(b,7,mid-2,Math.Max(0,x-7),4);
        var thumb=new Rectangle(x-5,mid-8,10,16);
        using(var b=new LinearGradientBrush(thumb,Color.FromArgb(203,158,95),Color.FromArgb(94,56,27),90))g.FillRectangle(b,thumb);
        using(var p=new Pen(DATheme.Gold))g.DrawRectangle(p,thumb);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-1,-1),DATheme.Gold,BackColor);
    }
    protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccess(this);}
    sealed class SliderAccess : ControlAccessibleObject
    {
        readonly DASlider slider;
        internal SliderAccess(DASlider owner):base(owner){slider=owner;}
        public override string Value {get{return slider.Value.ToString();}set{int v;if(int.TryParse(value,out v))slider.Value=v;}}
    }
}

internal sealed class DATabButton : Button
{
    internal bool Selected;
    internal int Icon;
    internal DATabButton()
    {
        Size=new Size(32,32);Margin=new Padding(2);BackColor=DATheme.Dark;ForeColor=DATheme.Gold;
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(DATheme.Dark);g.SmoothingMode=SmoothingMode.AntiAlias;
        var ring=new Rectangle(2,2,Width-5,Height-5);
        using(var fill=new LinearGradientBrush(ring,Color.FromArgb(209,172,116),Color.FromArgb(72,43,24),90))g.FillEllipse(fill,ring);
        using(var pen=new Pen(Selected?Color.FromArgb(250,216,152):Color.FromArgb(94,57,27),Selected?2:1))g.DrawEllipse(pen,ring);
        var inner=Rectangle.Inflate(ring,-4,-4);
        using(var fill=new SolidBrush(Selected?Color.FromArgb(105,67,34):Color.FromArgb(58,40,24)))g.FillEllipse(fill,inner);
        if(Icon==2)
        {
            float cx=Width/2f,cy=Height/2f;
            var teeth=new PointF[32];
            for(int i=0;i<teeth.Length;i++){
                int phase=i%4,offset=phase==0?-18:phase==1?-12:phase==2?12:18;
                double angle=(i/4*45+offset)*Math.PI/180;
                float radius=i%4==1||i%4==2?8:6;
                teeth[i]=new PointF(cx+radius*(float)Math.Cos(angle),cy+radius*(float)Math.Sin(angle));
            }
            using(var pen=new Pen(ForeColor,1.4f)){g.DrawPolygon(pen,teeth);g.DrawEllipse(pen,cx-2.5f,cy-2.5f,5,5);}
        }
        else if(Icon==5)
        {
            float cx=Width/2f,cy=Height/2f;
            using(var pen=new Pen(ForeColor,1.5f))using(var brush=new SolidBrush(ForeColor))
                for(int row=-1;row<=1;row++){float y=cy+row*5;g.FillRectangle(brush,cx-7,y-1,2,2);g.DrawLine(pen,cx-3,y,cx+7,y);}
        }
        else if(Icon==4)
        {
            float cx=Width/2f,cy=Height/2f;
            using(var brush=new SolidBrush(ForeColor)){g.FillEllipse(brush,cx-3,cy-7,6,6);g.FillPie(brush,cx-7,cy-1,14,14,180,180);}
        }
        else using(var font=DATheme.Font(18))using(var brush=new SolidBrush(ForeColor))
            using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(Icon==0?"?":Icon==3?"#":"M",font,brush,ClientRectangle,format);
        // Keyboard focus follows the circular artwork; no square focus box.
        if(Focused && ShowFocusCues)using(var pen=new Pen(DATheme.Gold,1)){pen.DashStyle=DashStyle.Dot;g.DrawEllipse(pen,Rectangle.Inflate(ring,-2,-2));}
    }
}
