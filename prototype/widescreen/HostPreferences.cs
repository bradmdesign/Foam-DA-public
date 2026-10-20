using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

internal static class LocalProfile
{
    internal static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DA-Library"
        );}}
    internal static string CharacterKey(string name,string server)
    {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(server+"\n"+name.ToLowerInvariant()))).Replace("-","");}
    internal static void Write(string path,string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
        File.WriteAllText(temp,text,Encoding.UTF8);
        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
    }
}

internal sealed class HostPreferences
{
    public int Version=1,Display=1,Filter=3,Fps=0,Viewport=1,PlayZoom=100,MapZoom=3,FogOpacity=30,FogRange=12,TabFade=0,MiniStyle=0,MiniMapZoom=100;
    public bool Guard=true,CustomMap=false,Fog=false,Square=true,Fade=true,MiniMap=true,Grid=false,LockZoom=false,ExpRate=false,AbRate=false;
    public bool OverviewFull=true;
    internal static string FilePath {get{return Path.Combine(LocalProfile.Root,"display-settings.json");}}
    internal static HostPreferences Load()
    {try{if(File.Exists(FilePath)){var value=Program.Json.Deserialize<HostPreferences>(File.ReadAllText(FilePath));if(value!=null&&value.Version==1)return value;}}catch{}return new HostPreferences();}
    internal void Save(){LocalProfile.Write(FilePath,Program.Json.Serialize(this));}
    internal static int Clamp(int n,int lo,int hi){return Math.Max(lo,Math.Min(hi,n));}
    internal void Apply(MapPanel map)
    {
        map.PlayZoom.Value=Clamp(PlayZoom,100,200);map.ZoomScale.Value=Clamp(MapZoom,1,10);map.Opacity.Value=Clamp(FogOpacity,0,100);map.Range.Value=Clamp(FogRange,6,24);
        map.TabTransparency.Value=Clamp(TabFade,0,95);map.MiniMapStyle.SelectedIndex=Clamp(MiniStyle,0,1);
        map.CustomZoom.Checked=CustomMap;map.Fog.Checked=Fog;map.Shape.SelectedIndex=Square?1:0;map.Fade.Checked=Fade;map.MiniMap.Checked=MiniMap;
        map.Grid.Checked=Grid;map.LockZoom.Checked=LockZoom;map.ExperienceRate.Checked=ExpRate;map.AbilityRate.Checked=AbRate;
    }
    internal void Capture(MapPanel map)
    {
        PlayZoom=map.PlayZoom.Value;MapZoom=(int)map.ZoomScale.Value;FogOpacity=map.Opacity.Value;FogRange=(int)map.Range.Value;TabFade=map.TabTransparency.Value;MiniStyle=map.MiniMapStyle.SelectedIndex;
        CustomMap=map.CustomZoom.Checked;Fog=map.Fog.Checked;Square=map.Shape.SelectedIndex==1;Fade=map.Fade.Checked;MiniMap=map.MiniMap.Checked;Grid=map.Grid.Checked;LockZoom=map.LockZoom.Checked;
        ExpRate=map.ExperienceRate.Checked;AbRate=map.AbilityRate.Checked;
    }
}

internal sealed class ProgressSample
{
    internal string Identity;
    internal uint Experience,Ability;
}
internal sealed class GainTracker
{
    ProgressSample previous;DateTime sampled;double seconds;string identity;
    internal ulong Experience,Ability;
    internal void Sample(ProgressSample next,DateTime now)
    {
        if(next==null){previous=null;return;}
        if(identity!=next.Identity){previous=null;Experience=Ability=0;seconds=0;identity=next.Identity;}
        if(previous!=null&&previous.Identity==next.Identity)
        {
            double elapsed=(now-sampled).TotalSeconds;
            if(elapsed>0&&elapsed<10)
            {
                seconds+=elapsed;
                // These are cumulative game counters. Spending experience or a
                // server reset rebases the sample; it is not negative income.
                if(next.Experience>=previous.Experience)Experience+=next.Experience-previous.Experience;
                if(next.Ability>=previous.Ability)Ability+=next.Ability-previous.Ability;
            }
        }
        else if(previous!=null){Experience=Ability=0;seconds=0;}
        previous=next;sampled=now;
    }
    internal double ExpPerHour {get{return seconds>=1?Experience*3600.0/seconds:0;}}
    internal double AbPerHour {get{return seconds>=1?Ability*3600.0/seconds:0;}}
    internal string Text(bool exp,bool ability)
    {return (exp?"Exp/h  "+ExpPerHour.ToString("N0"):"")+(exp&&ability?"\n":"")+(ability?"Ability/h  "+AbPerHour.ToString("N0"):"");}
}
internal sealed class GainPanel : Control
{
    internal GainPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);Font=DATheme.Font(DATheme.CompanionFontPixels);ForeColor=DATheme.Gold;TabStop=false;}
    protected override void OnPaint(PaintEventArgs e){DATheme.Paint(e.Graphics,ClientRectangle,false);TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(7,5,Width-14,Height-10),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);}
}
