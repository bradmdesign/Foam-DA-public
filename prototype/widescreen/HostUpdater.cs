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
        timer.Interval=60*60*1000;timer.Tick+=async delegate{await Check(false);};
    }
    internal async void Start(){timer.Start();await Check(false);}
    static ProcessStartInfo Command(string args) {
        return new ProcessStartInfo(Node,"\""+Worker+"\" "+args){WorkingDirectory=Program.Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    }
    static Dictionary<string,object> Run(string command) {
        using(var process=Process.Start(Command(command))){
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(15*60*1000)){try{process.Kill();}catch{}throw new IOException("The update check timed out. Try again.");}
            Task.WaitAll(stdout,stderr);if(stdout.Result.Length>65536)throw new IOException("Invalid update response.");
            var result=Program.Json.Deserialize<Dictionary<string,object>>(stdout.Result);
            if(process.ExitCode!=0||!Flag(result,"ok"))throw new IOException(Message(result));return result;
        }
    }
    static bool Flag(Dictionary<string,object> result,string name){object value;return result!=null&&result.TryGetValue(name,out value)&&value is bool&&(bool)value;}
    static string Message(Dictionary<string,object> result){object message;return result!=null&&result.TryGetValue("message",out message)?Convert.ToString(message):"The update could not be verified.";}
    static void InstallRequired(){var start=Command("install-required "+Process.GetCurrentProcess().Id);start.RedirectStandardOutput=false;start.RedirectStandardError=false;Process.Start(start);}
    internal static bool RequireCurrentBeforeLaunch()
    {
        Program.ConfigureUi();
        using(var form=new Form{Text="Foam DA · Updating",ClientSize=new Size(560,190),StartPosition=FormStartPosition.CenterScreen,BackColor=DATheme.Dark,ForeColor=DATheme.Gold,Font=DATheme.Font(14),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
            var state=new Label{Location=new Point(16,16),Size=new Size(528,105),Text="Checking for updates…"};
            var retry=new DAButton{Location=new Point(16,140),Size=new Size(125,30),Text="Retry",Enabled=false};
            var close=new DAButton{Location=new Point(420,140),Size=new Size(125,30),Text="Exit",DialogResult=DialogResult.Cancel};
            bool checking=false;
            Func<Task> check=async delegate {
                if(checking)return;checking=true;retry.Enabled=false;state.Text="Checking for updates…";
                try{
                    var result=await Task.Run(()=>Run("startup"));if(form.IsDisposed)return;
                    if(Flag(result,"app_pending")){state.Text="Installing the required update. Foam DA will reopen when it is ready.";InstallRequired();form.DialogResult=DialogResult.Cancel;form.Close();return;}
                    if(!Flag(result,"ready"))throw new IOException("An update is still being installed. Close other Foam DA hosts and retry.");
                    form.DialogResult=DialogResult.OK;form.Close();
                }catch(Exception ex){if(!form.IsDisposed){state.Text="Foam DA could not verify its current version.\r\n"+ex.Message+"\r\nRetry when the update source is available.";retry.Enabled=true;}}
                finally{checking=false;}
            };
            form.Controls.AddRange(new Control[]{state,retry,close});DATheme.StyleCompanion(form);form.CancelButton=close;
            form.Shown+=async delegate{await check();};retry.Click+=async delegate{await check();};
            return form.ShowDialog()==DialogResult.OK;
        }
    }
    internal async Task Check(bool interactive)
    {
        if(running)return;running=true;LastStatus="Checking for signed updates…";
        try {
            var result=await Task.Run(()=>Run("check"));LastStatus=Message(result);
            if(Flag(result,"app_pending")){LastStatus="Installing a required update. Foam DA will reopen when ready.";InstallRequired();restart();return;}
            object changed;var changes=result!=null&&result.TryGetValue("changed",out changed)?changed as System.Collections.ICollection:null;
            if(!owner.IsDisposed&&(interactive||(changes!=null&&changes.Count>0)))report(LastStatus);
        }catch(Exception ex){LastStatus="Could not check updates. "+ex.Message;if(File.Exists(Path.Combine(Store,"app-required.json"))||File.Exists(Path.Combine(Store,"app-pending.json"))){restart();return;}if(interactive&&!owner.IsDisposed)report(LastStatus);}
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
            var text=new Label{Location=new Point(16,16),Size=new Size(565,64),Text="Foam DA updates automatically. Required app updates finish before the host opens. Running game sessions stay open and are reattached after updating."};
            var automatic=new Label{Location=new Point(16,86),Size=new Size(565,25),Text="Automatic updates are always on."};
            var state=new Label{Location=new Point(16,121),Size=new Size(565,76),Text=LastStatus};
            var check=new DAButton{Location=new Point(16,218),Size=new Size(135,30),Text="Check now"};
            var close=new DAButton{Location=new Point(470,218),Size=new Size(110,30),Text="Close",DialogResult=DialogResult.Cancel};
            check.Click+=async delegate{check.Enabled=false;state.Text="Checking…";await Check(true);if(!form.IsDisposed){state.Text=LastStatus;check.Enabled=true;}};
            form.Controls.AddRange(new Control[]{text,automatic,state,check,close});DATheme.StyleCompanion(form);form.CancelButton=close;
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
