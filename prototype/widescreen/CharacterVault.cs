using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal sealed class SavedCharacter
{
    public string Name {get;set;}
    public ServerProfile Server {get;set;}
    public DateTime LastOnlineUtc {get;set;}
    internal string Key {get{return LocalProfile.CharacterKey(Name,Server.Key);}}
    public override string ToString(){return Name+" · "+Server.Name;}
}
internal sealed class CharacterVault
{
    readonly string path;
    internal readonly List<SavedCharacter> Characters=new List<SavedCharacter>();
    internal string Error;
    internal const string Notice="Quick start skips the intro and agreement, then fills your character name. Enter your password yourself in the game. DA-Library does not collect or save passwords. Character names, server choices, and last-login times stay on this PC.";
    internal CharacterVault(string file=null)
    {
        path=file??Path.Combine(LocalProfile.Root,"private","characters.json");
        try
        {
            if(!File.Exists(path))return;
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException();
            string json=File.ReadAllText(path);
            var entries=Program.Json.Deserialize<List<SavedCharacter>>(json);
            if(entries==null||entries.Count>50)throw new InvalidDataException();
            var keys=new HashSet<string>();
            foreach(var entry in entries){Validate(entry);if(!keys.Add(entry.Key))throw new InvalidDataException();}
            Characters.AddRange(entries);
            // Older versions saved an opt-in encrypted credential. Rewrite the
            // validated file immediately with the name-only schema; never
            // decrypt or copy the legacy value into a new object.
            if(json.IndexOf("\"Secret\"",StringComparison.OrdinalIgnoreCase)>=0)Persist(Characters);
        }
        catch{Characters.Clear();Error="Saved characters could not be read or migrated. The existing file has been preserved.";}
    }
    internal static void Validate(SavedCharacter entry)
    {
        if(entry==null||!Regex.IsMatch(entry.Name??"","^[A-Za-z]{2,15}$")||entry.Server==null)throw new ArgumentException("Enter a character name of 2–15 letters.");
        if(!entry.Server.Official)ServerProfile.Create(entry.Server.Name,entry.Server.Address,entry.Server.Port);
    }
    internal void Save(SavedCharacter entry)
    {
        if(Error!=null)throw new InvalidOperationException(Error);
        Validate(entry);var old=Characters.Find(c=>c.Key==entry.Key);
        var value=new SavedCharacter{Name=entry.Name,Server=entry.Server,
            LastOnlineUtc=old==null?entry.LastOnlineUtc:old.LastOnlineUtc};
        var next=new List<SavedCharacter>(Characters);if(old!=null)next.Remove(old);else if(next.Count>=50)throw new InvalidOperationException("Use at most 50 saved characters.");
        next.Add(value);next.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name));Persist(next);
        Characters.Clear();Characters.AddRange(next);
    }
    internal bool RecordActivity(string name,string server,DateTime when)
    {
        if(Error!=null||String.IsNullOrEmpty(name))return false;
        var entry=Characters.Find(c=>String.Equals(c.Name,name,StringComparison.OrdinalIgnoreCase)&&c.Server.Key==server);
        if(entry==null||when<=entry.LastOnlineUtc)return false;
        DateTime previous=entry.LastOnlineUtc;entry.LastOnlineUtc=when;
        try{Persist(Characters);return true;}
        catch{entry.LastOnlineUtc=previous;return false;}
    }
    internal void Remove(SavedCharacter entry)
    {var next=new List<SavedCharacter>(Characters);next.RemoveAll(c=>c.Key==entry.Key);Persist(next);Characters.Clear();Characters.AddRange(next);}
    void Persist(List<SavedCharacter> entries)
    {
        if(Error!=null)throw new InvalidOperationException(Error);
        string folder=Path.GetDirectoryName(path);Directory.CreateDirectory(folder);
        var permissions=new DirectorySecurity();permissions.SetAccessRuleProtection(true,false);
        permissions.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.SetAccessControl(folder,permissions);
        LocalProfile.Write(path,Program.Json.Serialize(entries));
    }
}

internal sealed class CharacterPanel : DAPanel
{
    readonly CharacterVault vault;
    readonly string appearanceFolder;
    readonly ListBox list=new ListBox();
    readonly Label message=new Label();
    readonly Label heading=new Label(),privacy=new Label();
    readonly DAButton back=new DAButton{Text="Back to game",Width=110,Height=28,Visible=false};
    readonly FlowLayoutPanel buttons=new FlowLayoutPanel();
    readonly CharacterPortraitView portrait=new CharacterPortraitView();
    readonly HearthSupport support=new HearthSupport();
    readonly Dictionary<string,Bitmap> portraits=new Dictionary<string,Bitmap>();
    bool layingOut;
    internal Func<ServerProfile> SelectedServer;
    internal event Action<SavedCharacter> Launch;
    internal event EventHandler Changed;
    internal event EventHandler Dismissed;
    internal bool CanDismiss {get{return back.Visible;}set{back.Visible=value;LayoutCharacters();}}
    internal CharacterPanel(CharacterVault value,string portraitFolder=null)
    {
        vault=value;appearanceFolder=portraitFolder;Font=DATheme.Font(DATheme.CompanionFontPixels);ForeColor=DATheme.Gold;
        AutoScroll=true;heading.Text="My characters";heading.Font=DATheme.Font(22);heading.ForeColor=DATheme.Gold;
        list.IntegralHeight=false;list.BackColor=DATheme.Dark;list.ForeColor=DATheme.Gold;list.BorderStyle=BorderStyle.None;list.AccessibleName="Saved characters";
        list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=128;list.DrawItem+=DrawCharacter;list.SelectedIndexChanged+=delegate{SelectPortrait();};
        buttons.WrapContents=true;buttons.Margin=Padding.Empty;
        foreach(string title in new[]{"Quick start","New character","Remove"})
        {
            string action=title;var button=new DAButton{Text=title,AutoSize=true,Height=26,Margin=new Padding(1)};buttons.Controls.Add(button);
            button.Click+=delegate
            {
                var selected=list.SelectedItem as SavedCharacter;
                if(action=="New character"){Edit(null);return;}
                if(selected==null){message.Text="Select a character first.";return;}
                if(action=="Quick start"){if(Launch!=null)Launch(selected);}
                else if(MessageBox.Show(this,"Remove this local character entry? This does not delete the game character.","Remove saved character",MessageBoxButtons.YesNo)==DialogResult.Yes)
                {try{vault.Remove(selected);RefreshEntries();}catch{message.Text="The saved character could not be removed.";}}
            };
        }
        privacy.Text=CharacterVault.Notice;privacy.ForeColor=DATheme.Gold;
        back.Click+=delegate{if(Dismissed!=null)Dismissed(this,EventArgs.Empty);};
        Controls.AddRange(new Control[]{heading,back,portrait,list,buttons,support,privacy,message});Resize+=delegate{LayoutCharacters();};RefreshEntries();LayoutCharacters();
    }
    internal void RefreshEntries()
    {
        string key=list.SelectedItem is SavedCharacter?((SavedCharacter)list.SelectedItem).Key:null;
        list.Items.Clear();list.Items.AddRange(vault.Characters.ToArray());
        for(int i=0;i<list.Items.Count;i++)if(((SavedCharacter)list.Items[i]).Key==key)list.SelectedIndex=i;
        if(list.SelectedIndex<0&&list.Items.Count>0)list.SelectedIndex=0;
        RefreshPortraits();message.Text=vault.Error??"Quick start uses an open signed-out tab for that server, or opens one if needed.";if(Changed!=null)Changed(this,EventArgs.Empty);
    }
    internal void RefreshPortraits()
    {
        portrait.Portrait=null;foreach(var image in portraits.Values)image.Dispose();portraits.Clear();
        foreach(var entry in vault.Characters)
        {
            var saved=CharacterAppearance.Load(entry.Key,appearanceFolder);if(saved==null)continue;
            try{portraits[entry.Key]=saved.Image();}catch{}
        }
        SelectPortrait();list.Invalidate();
    }
    void SelectPortrait()
    {
        var entry=list.SelectedItem as SavedCharacter;Bitmap image=null;
        if(entry!=null)portraits.TryGetValue(entry.Key,out image);
        portrait.Portrait=image;portrait.Character=entry==null?"Your characters":entry.Name+" · "+entry.Server.Name;
        portrait.Detail=entry==null?"Select a character.":entry.LastOnlineUtc==DateTime.MinValue?
            "Last logged in · not recorded yet":"Last logged in · "+entry.LastOnlineUtc.ToLocalTime().ToString("MMM d, h:mm tt");portrait.Invalidate();
    }
    void DrawCharacter(object sender,DrawItemEventArgs e)
    {
        if(e.Index<0||e.Index>=list.Items.Count)return;var entry=(SavedCharacter)list.Items[e.Index];var bounds=Rectangle.Inflate(e.Bounds,-2,-3);
        DATheme.Paint(e.Graphics,bounds,false);
        if((e.State&DrawItemState.Selected)!=0)using(var brush=new SolidBrush(Color.FromArgb(44,240,190,100)))e.Graphics.FillRectangle(brush,Rectangle.Inflate(bounds,-4,-4));
        Bitmap image;if(portraits.TryGetValue(entry.Key,out image))CharacterPortraitView.DrawSprite(e.Graphics,image,new Rectangle(bounds.Left+6,bounds.Top+6,100,bounds.Height-12));
        else TextRenderer.DrawText(e.Graphics,"?",Font,new Rectangle(bounds.Left+12,bounds.Top+34,84,32),DATheme.Gold,TextFormatFlags.HorizontalCenter);
        int left=bounds.Left+116,width=Math.Max(1,bounds.Width-124);
        TextRenderer.DrawText(e.Graphics,entry.Name,Font,new Rectangle(left,bounds.Top+18,width,24),DATheme.Gold,TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics,entry.Server.Name+
            "\nPassword entered in game"
            ,Font,new Rectangle(left,bounds.Top+44,width,42),DATheme.Gold,TextFormatFlags.EndEllipsis);
        if((e.State&DrawItemState.Focus)!=0)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(bounds,-5,-5),DATheme.Gold,DATheme.Dark);
    }
    void LayoutCharacters()
    {
        if(layingOut)return;layingOut=true;SuspendLayout();try{
        int width=Math.Max(300,ClientSize.Width-Padding.Horizontal-4),height=Math.Max(660,ClientSize.Height-Padding.Vertical-4),gap=12,inside=width-2*gap;
        AutoScrollMinSize=new Size(0,660);heading.SetBounds(gap,gap,Math.Max(1,inside-(back.Visible?122:0)),32);back.SetBounds(width-gap-110,gap,110,28);
        int supportHeight=support.HeightFor(inside);
        int privacyHeight=TextRenderer.MeasureText(privacy.Text,Font,new Size(inside,1000),TextFormatFlags.WordBreak).Height+4;
        int footer=height-Math.Max(238,68+supportHeight+6+privacyHeight+6+40),bodyTop=54,bodyHeight=footer-bodyTop-10;
        if(width>=760)
        {int hero=Math.Min(360,width*2/5);portrait.SetBounds(gap,bodyTop,hero,bodyHeight);list.SetBounds(gap+hero+gap,bodyTop,inside-hero-gap,bodyHeight);}
        else
        {int hero=Math.Min(260,Math.Max(234,bodyHeight/2));portrait.SetBounds(gap,bodyTop,inside,hero);list.SetBounds(gap,bodyTop+hero+gap,inside,Math.Max(80,bodyHeight-hero-gap));}
        buttons.SetBounds(gap,footer,inside,64);support.SetBounds(gap,footer+68,inside,supportHeight);
        privacy.SetBounds(gap,footer+74+supportHeight,inside,privacyHeight);message.SetBounds(gap,height-44,inside,40);
        }finally{ResumeLayout(false);layingOut=false;}
    }
    protected override void OnLayout(LayoutEventArgs e){if(portrait!=null)LayoutCharacters();base.OnLayout(e);}
    protected override void Dispose(bool disposing)
    {if(disposing){portrait.Portrait=null;foreach(var image in portraits.Values)image.Dispose();portraits.Clear();heading.Font.Dispose();}base.Dispose(disposing);}
    internal void SetStatus(string text){message.Text=text;}
    internal void NewCharacter(){Edit(null);}
    void Edit(SavedCharacter existing)
    {
        using(var form=new Form{Text="Add character",StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(470,220),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=DATheme.Dark,ForeColor=DATheme.Gold,Font=Font})
        {
            var name=new TextBox{Bounds=new Rectangle(145,14,305,25),MaxLength=15};
            var server=SelectedServer==null?ServerProfile.Kru():SelectedServer();
            var save=new DAButton{Text="Save character",Bounds=new Rectangle(245,183,205,28)};
            form.Controls.AddRange(new Control[]{new Label{Text="Character",Bounds=new Rectangle(14,16,130,24)},name,new Label{Text="Server: "+server.Name+(server.Official?"":" ("+server.Key+")"),Bounds=new Rectangle(14,47,430,25)},new Label{Text=CharacterVault.Notice,Bounds=new Rectangle(14,79,436,96)},save});
            save.Click+=delegate
            {
                try
                {
                    vault.Save(new SavedCharacter{Name=name.Text.Trim(),Server=server});form.DialogResult=DialogResult.OK;
                }
                catch(ArgumentException ex){MessageBox.Show(form,ex.Message,"Character details");}
                catch{MessageBox.Show(form,"The character could not be saved. The existing entry is unchanged.","Local character storage");}
            };
            form.AcceptButton=save;if(form.ShowDialog(this)==DialogResult.OK)RefreshEntries();
        }
    }
}
