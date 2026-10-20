using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Captures are deliberately visible beside the launcher, shared by all tabs.
// Settings, character profiles and browser data retain their private locations.
internal static class CaptureStorage
{
    static readonly object Gate=new object();
    static readonly HashSet<string> Imported=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal static string Root {get{return Program.InstallRoot??Program.Root;}}
    internal static string Screenshots {get{return Path.Combine(Root,"screenshots");}}
    internal static string Skulled {get{return Path.Combine(Root,"skulled");}}
    internal static string Ensure(string name)
    {
        if(name!="screenshots"&&name!="skulled")throw new ArgumentException("Invalid capture folder.");
        string target=Path.Combine(Root,name);Directory.CreateDirectory(target);
        lock(Gate)
        {
            if(!Imported.Contains(name))
            {
                // Keep the old copies as a backup; never overwrite a local capture.
                Import(Path.Combine(LocalProfile.Root,name),target,name=="skulled");
                Imported.Add(name);
            }
        }
        return target;
    }
    internal static void Import(string source,string target,bool skull)
    {
        if(!Directory.Exists(source)||String.Equals(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))return;
        Directory.CreateDirectory(target);
        foreach(string path in Directory.GetFiles(source,"*.png"))
        {
            string destination=Path.Combine(target,Path.GetFileName(path));
            try{if(!File.Exists(destination))File.Copy(path,destination,false);}catch(IOException){}
            if(!skull)continue;
            // A skull entry needs both files, and must not point to a different
            // image already present under the same name.
            string metadata=Path.ChangeExtension(path,".json"),to=Path.ChangeExtension(destination,".json");
            if(!File.Exists(metadata)||File.Exists(to)||!SameFile(path,destination))continue;
            try{File.Copy(metadata,to,false);}catch(IOException){}
        }
    }
    static bool SameFile(string a,string b)
    {
        try
        {
            if(new FileInfo(a).Length!=new FileInfo(b).Length)return false;
            using(var hash=System.Security.Cryptography.SHA256.Create())
            using(var left=File.OpenRead(a))using(var right=File.OpenRead(b))
                return hash.ComputeHash(left).SequenceEqual(hash.ComputeHash(right));
        }
        catch(IOException){return false;}
    }
}
