using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// Each tab has its own writable game directory and mutex. Assets come only
// from the selected local installation; character folders and credentials are not copied.
internal static class GameSessions
{
    internal static async Task CloseGame(int pid,IntPtr window,string expectedPath)
    {
        if(!IsGamePath(expectedPath))throw new InvalidOperationException("This game is outside DA-Library's session folders.");
        Process process;
        try{process=Process.GetProcessById(pid);}catch(ArgumentException){return;}
        using(process) {
            if(process.HasExited)return;
            uint owner;HostNative.GetWindowThreadProcessId(window,out owner);
            if(owner!=(uint)pid||!String.Equals(Path.GetFullPath(process.MainModule.FileName),Path.GetFullPath(expectedPath),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The game process no longer matches this tab.");
            HostNative.PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);
            for(int tick=0;tick<20&&!process.HasExited;tick++)await Task.Delay(100);
            // An unresponsive connection screen must still be closable. The
            // retained process handle and verified session path scope this to one game.
            if(!process.HasExited){process.Kill();for(int tick=0;tick<20&&!process.HasExited;tick++)await Task.Delay(100);}
            if(!process.HasExited)throw new InvalidOperationException("The game is still closing; its tab has been retained.");
        }
    }
    internal static readonly string Root=Path.Combine(Program.Root,"build","sessions");
    internal static bool IsGamePath(string path)
    {
        path=Path.GetFullPath(path);
        if(String.Equals(path,Program.Game,StringComparison.OrdinalIgnoreCase))return true;
        string folder=Path.GetDirectoryName(path),name=Path.GetFileName(folder);
        int slot;
        return String.Equals(Path.GetDirectoryName(folder),Root,StringComparison.OrdinalIgnoreCase) &&
            name.StartsWith("game-",StringComparison.Ordinal) && int.TryParse(name.Substring(5),out slot) && slot>=1 && slot<=8 &&
            name=="game-"+slot.ToString("0000") && String.Equals(Path.GetFileName(path),"Darkages.exe",StringComparison.OrdinalIgnoreCase);
    }
    internal static List<Process> Running()
    {
        var found=new List<Process>();
        foreach(var process in Process.GetProcessesByName("Darkages"))
        {
            try {if(IsGamePath(process.MainModule.FileName)){found.Add(process);continue;}}
            catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
            process.Dispose();
        }
        found.Sort((a,b)=>a.Id.CompareTo(b.Id));return found;
    }
    internal static string Prepare(string profile,string reuse=null,ServerProfile server=null)
    {
        if(server==null)server=ServerProfile.Kru();
        if(profile!="960x600" && profile!="1088x640" && profile!="1152x680")throw new InvalidOperationException("Unknown viewport.");
        string assets=GameInstallation.Validate(GameInstallation.Source);
        var occupied=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in Running()){try{occupied.Add(p.MainModule.FileName);}finally{p.Dispose();}}
        string game=null;int slot=0;
        if(reuse!=null && IsGamePath(reuse) && !String.Equals(reuse,Program.Game,StringComparison.OrdinalIgnoreCase))
        {if(ServerProfile.KeyAt(reuse)!=server.Key)throw new InvalidOperationException("A session folder cannot switch servers.");game=reuse;slot=int.Parse(Path.GetFileName(Path.GetDirectoryName(game)).Substring(5));}
        else for(int n=1;n<=8;n++)
        {string candidate=Path.Combine(Root,"game-"+n.ToString("0000"),"Darkages.exe");if(!occupied.Contains(candidate)&&(!Directory.Exists(Path.GetDirectoryName(candidate))||ServerProfile.KeyAt(candidate)==server.Key)){game=candidate;slot=n;break;}}
        if(game==null)throw new InvalidOperationException("All eight session folders are running or assigned to another server.");
        if(occupied.Contains(game))throw new InvalidOperationException("This game is still running.");
        string source=GameInstallation.PrepareProfile(profile,assets),folder=Path.GetDirectoryName(game);
        var manifest=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(source,"manifest.json")));
        if((string)manifest["source_client_sha256"]!=StartupPatch.ClientSha256 ||
            Program.HashFile(Path.Combine(source,"Darkages.exe"))!=(string)manifest["test_client_sha256"] ||
            Program.HashFile(Path.Combine(source,"setoa.dat"))!=(string)manifest["test_setoa_sha256"])
            throw new InvalidOperationException("The prepared viewport failed verification.");
        // Check before creating any output. A new tab copies sizeable assets;
        // leave headroom for the existing game and the host's browser cache.
        long required=AdditionalBytes(folder,source,assets)+128L*1024*1024;
        if(new DriveInfo(Path.GetPathRoot(folder)).AvailableFreeSpace<required)
            throw new IOException("This tab needs "+((required+1048575)/1048576)+" MB free, including working space.");
        bool created=!Directory.Exists(folder);
        bool refreshAssets=true;
        try{var previous=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"host-manifest.json")));object old;refreshAssets=!previous.TryGetValue("asset_source",out old)||!String.Equals(old as string,assets,StringComparison.OrdinalIgnoreCase);}catch{}
        Directory.CreateDirectory(folder);
        try
        {
        // Hold any old executable exclusively, so preparing cannot race a launch.
        using(var guard=new FileStream(game,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            foreach(string file in GameInstallation.AvailableAssets(assets))
            {
                string ext=Path.GetExtension(file).ToLowerInvariant(),name=Path.GetFileName(file);
                if(name.Equals("setoa.dat",StringComparison.OrdinalIgnoreCase))continue;
                if(ext==".dat"||ext==".dll"||ext==".bmp"||ext==".tbl"||ext==".nfo")
                {string target=Path.Combine(folder,name);if(refreshAssets||!File.Exists(target)||new FileInfo(file).Length!=new FileInfo(target).Length||File.GetLastWriteTimeUtc(file)!=File.GetLastWriteTimeUtc(target))File.Copy(file,target,true);}
            }
            foreach(string sub in new[]{"music","npc","maps","metafile"})CopyAssets(Path.Combine(assets,sub),Path.Combine(folder,sub),refreshAssets);
            File.Copy(Path.Combine(source,"setoa.dat"),Path.Combine(folder,"setoa.dat"),true);
            byte[] image=File.ReadAllBytes(Path.Combine(source,"Darkages.exe"));
            if(!server.Official)image=EndpointPatch.Apply(image);
            image=StartupPatch.SkipIntro(image);
            image=UniqueImage(image,slot);
            guard.SetLength(image.Length);guard.Position=0;guard.Write(image,0,image.Length);guard.Flush();
        }
        manifest["test_client_sha256"]=Program.HashFile(game);manifest["session_slot"]=slot;manifest["asset_source"]=assets;
        File.WriteAllText(Path.Combine(folder,"host-manifest.json"),Program.Json.Serialize(manifest));
        File.WriteAllText(Path.Combine(folder,"server-profile.json"),Program.Json.Serialize(server));
        return game;
        }
        catch
        {
            // Only a directory created by this preparation, at a validated
            // immediate session path, can be removed. Existing saves survive.
            if(created&&IsGamePath(game)&&String.Equals(Path.GetDirectoryName(Path.GetFullPath(folder)),Root,StringComparison.OrdinalIgnoreCase))
                try{Directory.Delete(folder,true);}catch{}
            throw;
        }
    }
    internal static long AdditionalBytes(string folder,string source,string installed=null)
    {
        long result=new FileInfo(Path.Combine(source,"setoa.dat")).Length+new FileInfo(Path.Combine(source,"Darkages.exe")).Length+512;
        string runtime=installed??GameInstallation.Source;
        foreach(string file in GameInstallation.AvailableAssets(runtime))
        {
            string name=Path.GetFileName(file),ext=Path.GetExtension(file).ToLowerInvariant();
            if(name.Equals("setoa.dat",StringComparison.OrdinalIgnoreCase))continue;
            if((ext==".dat"||ext==".dll"||ext==".bmp"||ext==".tbl"||ext==".nfo")&&!File.Exists(Path.Combine(folder,name)))result+=new FileInfo(file).Length;
        }
        foreach(string sub in new[]{"music","npc","maps","metafile"})
        {
            string dir=Path.Combine(runtime,sub);if(!Directory.Exists(dir))continue;
            foreach(string file in Directory.GetFiles(dir,"*",SearchOption.AllDirectories))
                if(!File.Exists(Path.Combine(folder,sub,file.Substring(dir.Length+1))))result+=new FileInfo(file).Length;
        }
        return result;
    }
    static void CopyAssets(string source,string target,bool refresh=false)
    {
        if(!Directory.Exists(source))return;
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("A game asset folder is a link. Select the installation containing the original assets.");
        Directory.CreateDirectory(target);
        foreach(string path in Directory.GetFiles(source)){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked game asset encountered.");string dst=Path.Combine(target,Path.GetFileName(path));if(refresh||!File.Exists(dst))File.Copy(path,dst,true);}
        foreach(string path in Directory.GetDirectories(source))CopyAssets(path,Path.Combine(target,Path.GetFileName(path)),refresh);
    }
    internal static byte[] UniqueImage(byte[] image,int slot)
    {
        if(slot<1||slot>8)throw new ArgumentOutOfRangeException("slot");
        byte[] marker=Encoding.ASCII.GetBytes("DALib.WidePrototype\0");int at=-1;
        for(int i=0;i<=image.Length-marker.Length;i++)
        {bool match=true;for(int n=0;n<marker.Length;n++)if(image[i+n]!=marker[n]){match=false;break;}if(match){if(at>=0)throw new InvalidOperationException("Ambiguous mutex.");at=i;}}
        if(at<0)throw new InvalidOperationException("Unexpected session mutex.");
        byte[] replacement=Encoding.ASCII.GetBytes(
            "DALib.Session"+slot.ToString("0000")
            +"\0");
        Array.Clear(image,at,marker.Length);Array.Copy(replacement,0,image,at,replacement.Length);
        int pe=BitConverter.ToInt32(image,0x3c),checksum=pe+24+64;
        if(pe<0 || checksum+4>image.Length)throw new InvalidOperationException("Invalid PE header.");
        Array.Clear(image,checksum,4);uint sum=0;
        for(int i=0;i<image.Length;i+=2){sum+=(uint)(image[i]+(i+1<image.Length?image[i+1]<<8:0));sum=(sum&65535)+(sum>>16);}
        sum=(sum&65535)+(sum>>16);sum+=(uint)image.Length;
        Array.Copy(BitConverter.GetBytes(sum),0,image,checksum,4);return image;
    }
}

internal sealed class HostedSession
{
    internal int Id;
    internal string GamePath,Profile,CharacterName;
    internal ServerProfile Server;
    internal GameFrame Frame;
    internal GameTools Tools;
    internal MacroPanel Macros;
    internal HotkeyPanel Hotkeys;
    internal QuickLogin Login;
    internal bool WorldObserved;
    internal bool ConnectionFailureShown;
    internal bool CharactersDismissed;
    internal readonly CharacterAppearanceRecorder Appearance=new CharacterAppearanceRecorder();
    internal readonly GainTracker Gains=new GainTracker();
    internal readonly SkullRecorder Skulls=new SkullRecorder();
    internal HostNative.Rect OldRect;
}
