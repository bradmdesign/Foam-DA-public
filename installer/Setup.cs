using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class SetupFile { public string path {get;set;} public long bytes {get;set;} public string sha256 {get;set;} }
internal sealed class SetupReport { public int schema {get;set;} public string product {get;set;} public string version {get;set;} public string source_commit {get;set;} public List<SetupFile> files {get;set;} }
internal static class Setup
{
    internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=64*1024*1024,RecursionLimit=32};
    [STAThread]static int Main(string[] args)
    {
        try {
            using(var package=new SetupPackage(Assembly.GetExecutingAssembly().Location)) {
                if(args.Length==1&&args[0]=="--verify-package"){package.Verify(null);return 0;}
                if(args.Length==2&&args[0]=="--install-test"){package.Install(args[1],false,null);return 0;}
                if(args.Length==2&&args[0]=="--ui-check"){
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var form=new SetupForm(package)){form.Shown+=delegate{using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}form.Close();};Application.Run(form);}return 0;
                }
                if(args.Length!=0)throw new ArgumentException("Unknown setup option.");
                if(!Environment.Is64BitOperatingSystem)throw new IOException("Foam DA needs 64-bit Windows 10 or Windows 11.");
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupForm(package));return 0;
            }
        }catch(Exception e){if(args.Length==0)MessageBox.Show(e.Message,"Foam DA Setup",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
    internal static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
    internal static string SafePath(string root,string name)
    {
        if(String.IsNullOrEmpty(name)||name.Contains("\\")||name.Contains(":")||name.Split('/').Any(p=>p.Length==0||p=="."||p==".."||p.EndsWith(".")||p.EndsWith(" ")||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(p,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)))throw new InvalidDataException("Unsafe installer path.");
        string result=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
        if(!result.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Installer path escapes its folder.");return result;
    }
    internal static void NoLinks(string file)
    {for(string p=Path.GetFullPath(file);p!=null;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a folder without symbolic links or junctions.");}
    internal static bool WebViewInstalled()
    {
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})try {
            using(var root=RegistryKey.OpenBaseKey(hive,view))using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}")) {Version v;if(key!=null&&Version.TryParse(key.GetValue("pv") as string,out v)&&v.Major>0)return true;}
        }catch(System.Security.SecurityException){}return false;
    }
    internal static void Shortcut(string root)
    {
        Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=null,link=null;
        try{shell=Activator.CreateInstance(type);link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Foam DA.lnk")});var t=link.GetType();t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(root,"Foam-DA.exe")});t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{root});t.InvokeMember("Description",BindingFlags.SetProperty,null,link,new object[]{"Foam DA · Game and Library"});t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}
        finally{if(link!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);if(shell!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
    }
}
internal sealed class SetupPackage : IDisposable
{
    readonly FileStream file;readonly SetupSlice slice;readonly ZipArchive zip;readonly Dictionary<string,ZipArchiveEntry> entries;
    internal readonly SetupReport Report;readonly byte[] reportBytes,signature;
    internal SetupPackage(string exe)
    {
        file=File.OpenRead(exe);
        try {
            if(file.Length<1024)throw new InvalidDataException("Installer is incomplete.");file.Position=file.Length-64;
            var reader=new BinaryReader(file,Encoding.UTF8,true);long start=reader.ReadInt64(),length=reader.ReadInt64();byte[] expected=reader.ReadBytes(32),magic=reader.ReadBytes(16);
            if(Encoding.ASCII.GetString(magic)!="FOAM-SETUP-V1!\r\n"||start<512||length<22||start>file.Length-64||length!=file.Length-64-start)throw new InvalidDataException("Installer is damaged or incomplete.");
            slice=new SetupSlice(file,start,length);using(var sha=SHA256.Create())if(!sha.ComputeHash(slice).SequenceEqual(expected))throw new InvalidDataException("Installer download checksum failed.");slice.Position=0;
            zip=new ZipArchive(slice,ZipArchiveMode.Read,true);entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            if(zip.Entries.Count>200000)throw new InvalidDataException("Too many installer entries.");
            foreach(var e in zip.Entries){Setup.SafePath(Path.GetTempPath(),e.FullName);if(entries.ContainsKey(e.FullName)||((e.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Duplicate or linked installer file.");entries.Add(e.FullName,e);}
            reportBytes=Small("setup-report.json",64*1024*1024);signature=Small("setup-report.sig",4096);
            using(var rsa=new RSACryptoServiceProvider(new CspParameters(24))){rsa.PersistKeyInCsp=false;rsa.FromXmlString(SetupKey.PublicXml);using(var sha=SHA256.Create())if(!rsa.VerifyData(reportBytes,sha,signature))throw new InvalidDataException("Installer publisher signature failed.");}
            Report=Setup.Json.Deserialize<SetupReport>(Encoding.UTF8.GetString(reportBytes));Validate();
        }catch{Dispose();throw;}
    }
    byte[] Small(string name,int limit)
    {ZipArchiveEntry e;if(!entries.TryGetValue(name,out e)||e.Length>limit)throw new InvalidDataException("Missing installer metadata.");using(var s=e.Open())using(var m=new MemoryStream()){s.CopyTo(m);if(m.Length>limit)throw new InvalidDataException("Oversized installer metadata.");return m.ToArray();}}
    void Validate()
    {
        if(Report==null||Report.schema!=1||Report.product!="Foam-DA-Lite"||!Regex.IsMatch(Report.version??"",@"^\d+\.\d+\.\d+$")||!Regex.IsMatch(Report.source_commit??"",@"^[a-f0-9]{40}$")||Report.files==null||Report.files.Count+2!=entries.Count)throw new InvalidDataException("Invalid signed installer inventory.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var f in Report.files){Setup.SafePath(Path.GetTempPath(),f.path);ZipArchiveEntry e;if(!names.Add(f.path)||f.bytes<0||f.bytes>512L*1024*1024||!Regex.IsMatch(f.sha256??"",@"^[a-f0-9]{64}$")||!entries.TryGetValue(f.path,out e)||e.Length!=f.bytes)throw new InvalidDataException("Installer inventory does not match its files.");total=checked(total+f.bytes);
            if(f.path.Split('/').Any(p=>new[]{"runtime","sessions","characters","private","screenshots","skulled","webview-profile","prepared-profiles"}.Contains(p.ToLowerInvariant()))||f.path.EndsWith(".dat",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(f.path).Equals("Darkages.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Game or private data cannot be installed from this package.");}
        if(total>4L*1024*1024*1024||!names.Contains("Foam-DA.exe")||!names.Contains("deps/node.exe")||!names.Contains("updater/channel.json")||(!names.Contains("library/dist/client/index.html")&&!names.Contains("library/dist/client/index.html.gz")))throw new InvalidDataException("The complete Library or updater is missing.");
    }
    void Copy(SetupFile expected,string destination)
    {
        using(var input=entries[expected.path].Open())using(var output=destination==null?(Stream)Stream.Null:new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var sha=SHA256.Create()){
            byte[] buffer=new byte[1024*1024];int n;long total=0;while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>expected.bytes)throw new InvalidDataException("Oversized installer file.");output.Write(buffer,0,n);sha.TransformBlock(buffer,0,n,null,0);}sha.TransformFinalBlock(buffer,0,0);if(total!=expected.bytes||Setup.Hex(sha.Hash)!=expected.sha256)throw new InvalidDataException("Installer file checksum failed: "+expected.path);
        }
    }
    internal void Verify(Action<int,string> progress)
    {int i=0;foreach(var f in Report.files){Copy(f,null);if(progress!=null)progress(++i*100/Report.files.Count,"Verifying signed installer files…");}}
    internal void Install(string destination,bool runtime,Action<int,string> progress)
    {
        destination=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);Setup.NoLinks(destination);
        if(Path.GetPathRoot(destination).TrimEnd(Path.DirectorySeparatorChar)==destination)throw new IOException("Choose a new program folder, not a drive root.");
        if(File.Exists(destination)||(Directory.Exists(destination)&&Directory.EnumerateFileSystemEntries(destination).Any()))throw new IOException("Choose an empty folder. For an existing Foam DA installation, use Settings → Updates.");
        string parent=Path.GetDirectoryName(destination);Directory.CreateDirectory(parent);Setup.NoLinks(parent);
        long needed=Report.files.Sum(f=>f.bytes)+64L*1024*1024;if(new DriveInfo(Path.GetPathRoot(destination)).AvailableFreeSpace<needed)throw new IOException("There is not enough free space to install the complete Library safely.");
        string stage=Path.Combine(parent,".foam-setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);bool moved=false;
        try {
            int i=0;foreach(var f in Report.files){string target=Setup.SafePath(stage,f.path);Directory.CreateDirectory(Path.GetDirectoryName(target));Setup.NoLinks(target);Copy(f,target);if(progress!=null)progress(++i*95/Report.files.Count,"Installing Foam DA and the offline Library…");}
            File.WriteAllBytes(Path.Combine(stage,"setup-report.json"),reportBytes);File.WriteAllBytes(Path.Combine(stage,"setup-report.sig"),signature);
            if(runtime&&!Setup.WebViewInstalled()){
                if(progress!=null)progress(96,"Installing the included Microsoft WebView2 runtime…");
                string prerequisite=Path.Combine(stage,"prerequisites","MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
                if(!File.Exists(prerequisite))throw new IOException("Microsoft WebView2 is required and its included installer is missing.");
                using(var p=Process.Start(new ProcessStartInfo(prerequisite,"/silent /install"){UseShellExecute=false,CreateNoWindow=true})){if(!p.WaitForExit(10*60*1000))throw new IOException("WebView2 is still installing. Wait for it to finish and run setup again.");}
                if(!Setup.WebViewInstalled())throw new IOException("Microsoft WebView2 did not finish installing. Restart Windows and try setup again.");
            }
            if(Directory.Exists(destination))Directory.Delete(destination);Directory.Move(stage,destination);moved=true;if(progress!=null)progress(100,"Installed. Select your existing Dark Ages folder on first launch.");
        }finally{
            if(!moved&&Directory.Exists(stage)){
                if(Path.GetDirectoryName(stage)!=parent||!Regex.IsMatch(Path.GetFileName(stage),@"^\.foam-setup-[a-f0-9]{32}$"))throw new IOException("Invalid installer staging path.");
                Clean(stage);
            }
        }
    }
    static void Clean(string folder){Setup.NoLinks(folder);foreach(string f in Directory.GetFiles(folder)){Setup.NoLinks(f);File.Delete(f);}foreach(string d in Directory.GetDirectories(folder))Clean(d);Directory.Delete(folder);}
    public void Dispose(){if(zip!=null)zip.Dispose();if(slice!=null)slice.Dispose();if(file!=null)file.Dispose();}
}
internal sealed class SetupSlice : Stream
{
    readonly Stream source;readonly long start,length;long position;
    internal SetupSlice(Stream source,long start,long length){this.source=source;this.start=start;this.length=length;}
    public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}public override long Length{get{return length;}}
    public override long Position{get{return position;}set{Seek(value,SeekOrigin.Begin);}}
    public override int Read(byte[] buffer,int offset,int count){source.Position=start+position;int n=source.Read(buffer,offset,(int)Math.Min(count,length-position));position+=n;return n;}
    public override long Seek(long offset,SeekOrigin origin){long next=(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:length)+offset;if(next<0||next>length)throw new IOException("Installer read outside package bounds.");position=next;return position;}
    public override void Flush(){}public override void SetLength(long n){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
}
internal sealed class SetupForm : Form
{
    readonly SetupPackage package;readonly TextBox folder=new TextBox();readonly Button install=new Button(),browse=new Button();readonly CheckBox shortcut=new CheckBox(),launch=new CheckBox();readonly Label status=new Label();readonly ProgressBar progress=new ProgressBar();bool busy;
    internal SetupForm(SetupPackage package)
    {
        this.package=package;Text="Foam DA Setup";ClientSize=new Size(650,350);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;FormBorderStyle=FormBorderStyle.FixedDialog;
        Controls.Add(new Label{Text="Foam DA "+package.Report.version,Font=new Font("Segoe UI",20,FontStyle.Bold),Bounds=new Rectangle(20,15,610,42)});
        Controls.Add(new Label{Text="Game companion, complete offline Library and automatic updater.\r\nNo Python, Node, Git or .NET SDK installation needed.",Bounds=new Rectangle(20,65,610,48)});
        folder.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Games","Foam DA");folder.SetBounds(20,126,500,28);browse.Text="Browse…";browse.SetBounds(530,124,100,31);
        browse.Click+=delegate{using(var d=new FolderBrowserDialog{Description="Choose a new empty folder for Foam DA."}){if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;}};
        shortcut.Text="Create a desktop shortcut";shortcut.Checked=true;shortcut.SetBounds(20,170,610,25);launch.Text="Open Foam DA when finished";launch.Checked=true;launch.SetBounds(20,200,610,25);
        Controls.Add(new Label{Text="Dark Ages is installed separately. Choose your existing game folder on first launch.\r\nThis installer does not include the game or anyone’s account data.",Bounds=new Rectangle(20,235,610,42)});
        status.SetBounds(20,283,610,26);status.Text="Ready. Signed app files match the published source revision.";progress.SetBounds(20,320,475,17);install.Text="Install";install.SetBounds(510,311,120,31);install.Click+=Install;
        Controls.AddRange(new Control[]{folder,browse,shortcut,launch,status,progress,install});FormClosing+=delegate(object s,FormClosingEventArgs e){if(busy)e.Cancel=true;};
    }
    async void Install(object sender,EventArgs e)
    {
        busy=true;install.Enabled=browse.Enabled=folder.Enabled=shortcut.Enabled=launch.Enabled=false;
        try {
            string target=folder.Text;bool link=shortcut.Checked,open=launch.Checked;
            await Task.Run(()=>package.Install(target,true,(percent,message)=>{if(!IsDisposed)BeginInvoke(new Action(()=>{progress.Value=percent;status.Text=message;}));}));
            if(link)Setup.Shortcut(Path.GetFullPath(target));status.Text="Installed. Settings → Updates keeps Foam DA current.";
            if(open)using(var p=Process.Start(new ProcessStartInfo(Path.Combine(target,"Foam-DA.exe")){WorkingDirectory=target,UseShellExecute=true})){}install.Text="Close";install.Click-=Install;install.Click+=delegate{Close();};install.Enabled=true;
        }catch(Exception error){status.Text="Installation stopped safely.";MessageBox.Show(this,error.Message,"Foam DA Setup",MessageBoxButtons.OK,MessageBoxIcon.Error);install.Enabled=browse.Enabled=folder.Enabled=shortcut.Enabled=launch.Enabled=true;}
        finally{busy=false;}
    }
}
