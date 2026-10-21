using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class GameInstallationChecks
{
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    internal static void Run(string installed)
    {
        string folder=Path.Combine(Program.Root,"build","client-install-check-"+Guid.NewGuid().ToString("N"));
        string presenceFixture=Path.Combine(Path.GetTempPath(),"foam-install-presence-"+Guid.NewGuid().ToString("N"));
        int checks=0;var catalog=GameInstallation.Catalog();
        string exe=Program.HashFile(Path.Combine(installed,"Darkages.exe")),archive=Program.HashFile(Path.Combine(installed,"setoa.dat"));
        try
        {
            Check(GameInstallation.Validate(installed)==Path.GetFullPath(installed),"Supported local installation rejected");checks++;
            // Exercise folder selection with the real version-checked EXE/UI,
            // but only presence placeholders for the other required files.
            // This fixture is never launched as a game.
            Directory.CreateDirectory(presenceFixture);
            File.Copy(Path.Combine(installed,"Darkages.exe"),Path.Combine(presenceFixture,"Darkages.exe"));
            File.Copy(Path.Combine(installed,"setoa.dat"),Path.Combine(presenceFixture,"setoa.dat"));
            foreach(string name in catalog.assets.Where(name=>Path.GetExtension(name).Equals(".dat",StringComparison.OrdinalIgnoreCase)||
                new[]{"Mss32.dll","binkw32.dll","mServer.tbl"}.Contains(name,StringComparer.OrdinalIgnoreCase)))
                File.WriteAllBytes(Path.Combine(presenceFixture,name),new byte[0]);
            Check(GameInstallation.Validate(presenceFixture,catalog)==presenceFixture,"Compatible install without loading pictures and legacy helpers rejected");checks++;
            Check(!GameInstallation.AvailableAssets(presenceFixture,catalog).Any(file=>Path.GetExtension(file)==".bmp"),"Absent optional assets must not be copied or counted");checks++;
            long essentialBytes=new FileInfo(Path.Combine(presenceFixture,"Darkages.exe")).Length+new FileInfo(Path.Combine(presenceFixture,"setoa.dat")).Length+512;
            string emptySession=Path.Combine(presenceFixture,"unused-session-estimate");
            Check(GameSessions.AdditionalBytes(emptySession,presenceFixture,presenceFixture)==essentialBytes,"New-tab disk estimate must tolerate absent optional assets");checks++;
            string optional=Path.Combine(presenceFixture,"lod001.bmp");File.WriteAllBytes(optional,new byte[]{1,2,3});
            Check(GameInstallation.AvailableAssets(presenceFixture,catalog).Contains(optional),"Present optional loading art must remain available");checks++;
            Check(GameSessions.AdditionalBytes(emptySession,presenceFixture,presenceFixture)==essentialBytes+3,"New-tab disk estimate must include present optional art");checks++;
            File.Delete(optional);
            foreach(string name in new[]{"binkw32.dll","seo.dat"})
            {
                string absent=Path.Combine(presenceFixture,name);File.Delete(absent);
                string error=null;try{GameInstallation.Validate(presenceFixture,catalog);}catch(IOException e){error=e.Message;}
                Check(error!=null&&error.Contains(name)&&!error.Contains("lod001.bmp"),"Missing essential file must be named without reporting optional art");checks++;
                File.WriteAllBytes(absent,new byte[0]);
            }
            File.Delete(Path.Combine(presenceFixture,"Darkages.exe"));
            bool missingExe=false;try{GameInstallation.Validate(presenceFixture,catalog);}catch(IOException){missingExe=true;}
            Check(missingExe,"Missing executable must still be rejected");checks++;
            foreach(string profile in new[]{"960x600","1088x640","1152x680"})
            {
                string output=GameInstallation.PrepareProfile(profile,installed,Path.Combine(folder,profile));
                Check(Program.HashFile(Path.Combine(output,"Darkages.exe"))==catalog.profiles[profile].test_client_sha256,"Executable reconstruction differs from the validated viewport");checks++;
                Check(Program.HashFile(Path.Combine(output,"setoa.dat"))==catalog.profiles[profile].test_setoa_sha256,"UI reconstruction differs from the validated viewport");checks++;
                Check(!File.Exists(Path.Combine(output,"seo.dat"))&&!Directory.Exists(Path.Combine(output,"maps")),"Preparing a viewport must not copy the whole game");checks++;
                GameInstallation.PrepareProfile(profile,installed,output);
                Check(!Directory.GetFiles(output,"*.tmp").Any(),"Cached preparation left an incomplete output");checks++;
            }
            bool denied=false;try{GameInstallation.Validate(folder);}catch(IOException){denied=true;}
            Check(denied,"Missing executable must be rejected");checks++;
            byte[] source=File.ReadAllBytes(Path.Combine(installed,"Darkages.exe")),patch=File.ReadAllBytes(Path.Combine(GameInstallation.Recipes,"1088x640","client.patch.gz"));
            source[0]^=1;denied=false;try{ClientDelta.Apply(source,patch,catalog.source_client_sha256,catalog.profiles["1088x640"].test_client_sha256);}catch(InvalidDataException){denied=true;}
            Check(denied,"An unsupported source must not be patched");checks++;
            source[0]^=1;denied=false;try{ClientDelta.Apply(source,patch,catalog.source_client_sha256,new string('0',64));}catch(InvalidDataException){denied=true;}
            Check(denied,"A mismatched output recipe must be rejected");checks++;
            denied=false;try{ClientDelta.Apply(source,patch.Take(30).ToArray(),catalog.source_client_sha256,catalog.profiles["1088x640"].test_client_sha256);}catch(IOException){denied=true;}
            Check(denied,"A truncated recipe must be rejected");checks++;
            Check(Program.HashFile(Path.Combine(installed,"Darkages.exe"))==exe&&Program.HashFile(Path.Combine(installed,"setoa.dat"))==archive,"The original game installation changed");checks++;
            Check(Directory.GetFiles(GameInstallation.Recipes,"*",SearchOption.AllDirectories).All(p=>Path.GetExtension(p)==".gz"||Path.GetFileName(p)=="catalog.json"),"Distribution recipes contain a complete executable or archive");checks++;
            File.WriteAllText(Path.Combine(Program.Root,"build","client-install-checks.json"),Program.Json.Serialize(new{status="passed",checks,profiles=3,original_game_unchanged=true,whole_game_copied=false}));
        }
        finally
        {
            string build=Path.GetFullPath(Path.Combine(Program.Root,"build"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(folder).StartsWith(build,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid test cleanup path.");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
            if(Path.GetDirectoryName(presenceFixture)!=Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)||
                !Path.GetFileName(presenceFixture).StartsWith("foam-install-presence-",StringComparison.Ordinal))throw new InvalidOperationException("Invalid presence fixture cleanup path.");
            if(Directory.Exists(presenceFixture))Directory.Delete(presenceFixture,true);
        }
    }
}
