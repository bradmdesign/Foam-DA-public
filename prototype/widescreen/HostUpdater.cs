using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class HostUpdater : IDisposable
{
    readonly Form owner;readonly Action<string> report;readonly Action restart;
    readonly Timer timer=new Timer();bool running;internal string LastStatus="Updates have not been checked yet.";
    static string Store {get{return Path.Combine(Program.Root,"updates");}}
    internal static string Worker {get{return Path.Combine(Program.Root,"updater","worker.mjs");}}
    internal static string Node {
        get {
            var paths=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"deps","host-runtime.json")));
            string node=(string)paths["node"];return Path.IsPathRooted(node)?node:Path.GetFullPath(Path.Combine(Program.Root,node));
        }
    }
    internal HostUpdater(Form owner,Action<string> report,Action restart)
    {
        this.owner=owner;this.report=report;this.restart=restart;
        timer.Interval=60*60*1000;timer.Tick+=async delegate{if(Automatic)await Check(false);};
    }
    internal bool Automatic {
        get {
            try {
                string file=Path.Combine(Store,"preferences.json");if(!File.Exists(file))file=Path.Combine(Program.Root,"updater","channel.json");
                var settings=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));object value;
                return settings.TryGetValue("automatic",out value)&&value is bool&&(bool)value;
            }catch{return false;}
        }
        set {
            Directory.CreateDirectory(Store);string file=Path.Combine(Store,"preferences.json"),temp=file+".tmp";
            File.WriteAllText(temp,Program.Json.Serialize(new {automatic=value}));
            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
        }
    }
    internal async void Start(){timer.Start();if(Automatic)await Check(false);}
    static ProcessStartInfo Command(string args) {
        return new ProcessStartInfo(Node,"\""+Worker+"\" "+args){WorkingDirectory=Program.Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    }
    internal async Task Check(bool interactive)
    {
        if(running)return;running=true;LastStatus="Checking for signed Library, ad and app updates…";
        try {
            string output=await Task.Run(()=>{
                using(var process=Process.Start(Command("check"))){
                    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(15*60*1000)){try{process.Kill();}catch{}throw new IOException("The update check timed out. Try again later.");}
                    Task.WaitAll(stdout,stderr);if(stdout.Result.Length>65536)throw new IOException("Invalid updater response.");return stdout.Result;
                }
            });
            var result=Program.Json.Deserialize<Dictionary<string,object>>(output);object message,ok;
            LastStatus=result!=null&&result.TryGetValue("message",out message)?Convert.ToString(message):"No update status was returned.";
            if(result!=null&&result.TryGetValue("ok",out ok)&&ok is bool&&(bool)ok&&File.Exists(Path.Combine(Store,"app-pending.json")))LastStatus+=" App update ready: open Settings → Updates to install.";
            object changed;var changes=result!=null&&result.TryGetValue("changed",out changed)?changed as System.Collections.ICollection:null;
            if(!owner.IsDisposed&&(interactive||(changes!=null&&changes.Count>0)))report(LastStatus);
        }catch(Exception ex){LastStatus="Could not check updates. Your local Library remains available. "+ex.Message;if(interactive&&!owner.IsDisposed)report(LastStatus);}
        finally{running=false;}
    }
    internal static bool RecoverBeforeLaunch()
    {
        if(!File.Exists(Path.Combine(Store,"app-installing.json")))return false;
        var start=Command("repair "+Process.GetCurrentProcess().Id);start.RedirectStandardOutput=false;start.RedirectStandardError=false;
        Process.Start(start);return true;
    }
    internal void Show(string checkImage=null)
    {
        using(var form=new Form{Text="Foam DA updates",ClientSize=new Size(600,270),StartPosition=FormStartPosition.CenterParent,BackColor=DATheme.Dark,ForeColor=DATheme.Gold,MinimizeBox=false,MaximizeBox=false,FormBorderStyle=FormBorderStyle.FixedDialog,Font=DATheme.Font(14)})
        {
            var text=new Label{Location=new Point(16,16),Size=new Size(565,64),Text="Library records and sponsored messages update separately from the app. Your game stays open. App updates install only when you choose to restart the host."};
            var automatic=new DACheckBox{Location=new Point(16,86),Size=new Size(565,25),Text="Check at startup and hourly (shared by all characters)",Checked=Automatic};
            var state=new Label{Location=new Point(16,121),Size=new Size(565,76),Text=LastStatus};
            var check=new DAButton{Location=new Point(16,218),Size=new Size(135,30),Text="Check now"};
            var install=new DAButton{Location=new Point(160,218),Size=new Size(235,30),Text="Install app update + restart",Enabled=File.Exists(Path.Combine(Store,"app-pending.json"))};
            var close=new DAButton{Location=new Point(470,218),Size=new Size(110,30),Text="Close",DialogResult=DialogResult.Cancel};
            automatic.CheckedChanged+=delegate{try{Automatic=automatic.Checked;}catch{state.Text="Update preferences could not be saved. Check that the app folder is writable.";}};
            check.Click+=async delegate{check.Enabled=false;state.Text="Checking…";await Check(true);if(!form.IsDisposed){state.Text=LastStatus;check.Enabled=true;install.Enabled=File.Exists(Path.Combine(Store,"app-pending.json"));}};
            install.Click+=delegate{
                try {
                    if(running){state.Text="Wait for the update check to finish.";return;}
                    var start=Command("install "+Process.GetCurrentProcess().Id);start.RedirectStandardOutput=false;start.RedirectStandardError=false;
                    Process.Start(start);form.Close();restart();
                }catch(Exception ex){state.Text="Could not start installation: "+ex.Message;}
            };
            form.Controls.AddRange(new Control[]{text,automatic,state,check,install,close});DATheme.StyleCompanion(form);form.CancelButton=close;
            if(checkImage!=null)form.Shown+=delegate {
                try {
                    foreach(Control control in form.Controls)if(!form.ClientRectangle.Contains(control.Bounds))throw new InvalidOperationException("Update controls extend outside the window.");
                    form.Refresh();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(checkImage,System.Drawing.Imaging.ImageFormat.Png);}
                }finally{form.Close();}
            };
            form.ShowDialog(owner);
        }
    }
    public void Dispose(){timer.Dispose();}
}
