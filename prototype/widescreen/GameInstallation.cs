using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal sealed class ClientRecipe
{
    public int width {get;set;} public int height {get;set;}
    public string test_client_sha256 {get;set;} public string test_setoa_sha256 {get;set;}
    public string client_patch_sha256 {get;set;} public string layout_patch_sha256 {get;set;}
}
internal sealed class ClientCatalog
{
    public int version {get;set;}
    public string source_client_sha256 {get;set;} public string source_setoa_sha256 {get;set;}
    public string[] assets {get;set;}
    public Dictionary<string,ClientRecipe> profiles {get;set;}
}
internal sealed class InstalledGameSetting {public int Version=1;public string Folder;}

// Only the folder is remembered. Original game files are opened for reading.
// Widescreen images are reconstructed locally from the small shipped recipes.
internal static class GameInstallation
{
    internal static string SettingsFile {get{return Path.Combine(LocalProfile.Root,"foam-game-installation.json");}}
    internal static string Recipes {get{return Path.Combine(Program.Root,"client-patches");}}
    internal static string DefaultFolder {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"KRU","Dark Ages");}}
    internal static string RememberedFolder
    {
        get {try{var value=Program.Json.Deserialize<InstalledGameSetting>(File.ReadAllText(SettingsFile));return value!=null&&value.Version==1?value.Folder:null;}catch{return null;}}
    }
    internal static string Source {get{return RememberedFolder??DefaultFolder;}}
    internal static ClientCatalog Catalog()
    {
        var value=Program.Json.Deserialize<ClientCatalog>(File.ReadAllText(Path.Combine(Recipes,"catalog.json")));
        if(value==null||value.version!=1||value.profiles==null||value.assets==null||value.source_client_sha256!=StartupPatch.ClientSha256)
            throw new InvalidDataException("The local game preparation files are missing or incompatible. Reinstall Foam DA.");
        foreach(string name in value.assets)
            if(String.IsNullOrEmpty(name)||Path.GetFileName(name)!=name||name.IndexOfAny(new[]{'/', '\\', ':'})>=0||!new[]{".dat",".dll",".bmp",".tbl",".nfo"}.Contains(Path.GetExtension(name).ToLowerInvariant()))
                throw new InvalidDataException("Invalid game asset list.");
        return value;
    }
    internal static string Validate(string folder,ClientCatalog catalog=null)
    {
        if(String.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder))throw new IOException("Choose the folder where Dark Ages is installed.");
        folder=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(folder.StartsWith(Path.GetFullPath(Path.Combine(Program.Root,"build"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
            String.Equals(folder,Path.GetFullPath(Path.Combine(Program.Root,"runtime")),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose your original Dark Ages installation, rather than a Foam DA session.");
        catalog=catalog??Catalog();
        string exe=Path.Combine(folder,"Darkages.exe"),layout=Path.Combine(folder,"setoa.dat");
        if(!File.Exists(exe)||!File.Exists(layout))throw new IOException("That folder does not contain Darkages.exe and setoa.dat.");
        if(Program.HashFile(exe)!=catalog.source_client_sha256||Program.HashFile(layout)!=catalog.source_setoa_sha256)
            throw new IOException("This Dark Ages version is not supported. Select an original 7.41 installation matching this Foam DA release.");
        if(catalog.assets.Any(name=>!File.Exists(Path.Combine(folder,name))))throw new IOException("This Dark Ages installation is incomplete. Select the complete installed game folder.");
        return folder;
    }
    internal static bool Choose(IWin32Window owner,bool force=false)
    {
        string previous=RememberedFolder,reason=null;
        if(!force&&previous!=null)try{Validate(previous);return true;}catch(Exception e){reason=e.Message;}
        using(var picker=new FolderBrowserDialog{Description=(reason==null?"Select your installed Dark Ages folder (contains Darkages.exe). Foam DA prepares its widescreen files locally.":reason),SelectedPath=previous??DefaultFolder,ShowNewFolderButton=false})
        {
            while(picker.ShowDialog(owner)==DialogResult.OK)
            {
                try
                {
                    string selected=Validate(picker.SelectedPath);
                    LocalProfile.Write(SettingsFile,Program.Json.Serialize(new InstalledGameSetting{Folder=selected}));return true;
                }
                catch(Exception e){MessageBox.Show(owner,e.Message,"Dark Ages folder",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            }
        }
        return false;
    }
    internal static string ProfileFolder(string profile)
    {
        if(profile!="960x600"&&profile!="1088x640"&&profile!="1152x680")throw new InvalidOperationException("Unknown viewport.");
        return Path.Combine(Program.Root,"build","prepared-profiles",profile);
    }
    internal static string PrepareProfile(string profile,string installed=null,string destination=null)
    {
        var catalog=Catalog();ClientRecipe recipe;
        if(!catalog.profiles.TryGetValue(profile,out recipe))throw new InvalidDataException("This viewport recipe is missing.");
        string source=Validate(installed??Source,catalog),folder=destination??ProfileFolder(profile);
        Directory.CreateDirectory(folder);
        using(var guard=new FileStream(Path.Combine(folder,"preparation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            foreach(string name in new[]{"Darkages.exe","setoa.dat"})
            {
                bool image=name=="Darkages.exe";
                string path=Path.Combine(folder,name),expected=image?recipe.test_client_sha256:recipe.test_setoa_sha256;
                if(File.Exists(path)&&Program.HashFile(path)==expected)continue;
                string patch=Path.Combine(Recipes,profile,image?"client.patch.gz":"layout.patch.gz");
                if(Program.HashFile(patch)!=(image?recipe.client_patch_sha256:recipe.layout_patch_sha256))throw new InvalidDataException("A game preparation recipe changed. Reinstall Foam DA.");
                byte[] output=ClientDelta.Apply(File.ReadAllBytes(Path.Combine(source,name)),File.ReadAllBytes(patch),image?catalog.source_client_sha256:catalog.source_setoa_sha256,expected);
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllBytes(temp,output);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
            }
            LocalProfile.Write(Path.Combine(folder,"manifest.json"),Program.Json.Serialize(new{source_client_sha256=catalog.source_client_sha256,source_setoa_sha256=catalog.source_setoa_sha256,test_client_sha256=recipe.test_client_sha256,test_setoa_sha256=recipe.test_setoa_sha256,logical_size=new[]{recipe.width,recipe.height}}));
        }
        return folder;
    }
}

internal static class ClientDelta
{
    static string Hash(byte[] bytes){using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    internal static byte[] Apply(byte[] source,byte[] patch,string sourceHash,string targetHash)
    {
        if(Hash(source)!=sourceHash)throw new InvalidDataException("The installed game changed. Choose a compatible installation.");
        using(var compressed=new MemoryStream(patch,false))using(var gzip=new GZipStream(compressed,CompressionMode.Decompress))using(var reader=new BinaryReader(gzip))
        {
            if(Encoding.ASCII.GetString(Read(reader,4))!="FDP1")throw new InvalidDataException("Invalid game preparation recipe.");
            long sourceLength=reader.ReadInt64();string declaredSource=BitConverter.ToString(Read(reader,32)).Replace("-","").ToLowerInvariant();
            long targetLength=reader.ReadInt64();string declaredTarget=BitConverter.ToString(Read(reader,32)).Replace("-","").ToLowerInvariant();
            int count=reader.ReadInt32();
            if(sourceLength!=source.Length||declaredSource!=sourceHash||declaredTarget!=targetHash||targetLength<1||targetLength>128L*1024*1024||count<1||count>100000)
                throw new InvalidDataException("Incompatible or oversized game preparation recipe.");
            var output=new byte[(int)targetLength];int cursor=0;
            for(int i=0;i<count;i++)
            {
                int kind=reader.ReadByte();long offset=kind==0?reader.ReadInt64():0;int length=reader.ReadInt32();
                if((kind!=0&&kind!=1)||length<=0||length>output.Length-cursor||(kind==0&&(offset<0||offset>source.Length-length)))throw new InvalidDataException("Game recipe copy is out of bounds.");
                if(kind==0)Buffer.BlockCopy(source,(int)offset,output,cursor,length);
                else{int remaining=length;while(remaining>0){int n=reader.Read(output,cursor+length-remaining,remaining);if(n==0)throw new EndOfStreamException();remaining-=n;}}
                cursor+=length;
            }
            if(cursor!=output.Length||reader.Read()!=-1||Hash(output)!=targetHash)throw new InvalidDataException("Prepared game verification failed.");
            return output;
        }
    }
    static byte[] Read(BinaryReader reader,int length){byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return bytes;}
}
