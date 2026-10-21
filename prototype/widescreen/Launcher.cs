using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Isolated client launcher and read-only renderer diagnostics. The companion
// has a separately guarded, opt-in Tab-map scale writer in GameTools.cs.
internal static class Program
{
    internal static readonly string Root = LocateRoot();
    internal static string InstallRoot
    {
        get {
            string folder=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),parent=Path.GetDirectoryName(folder);
            if(parent==null||!String.Equals(Path.GetFileName(parent),"versions",StringComparison.OrdinalIgnoreCase))return null;
            string root=Path.GetDirectoryName(parent);
            return File.Exists(Path.Combine(root,"installation.json"))&&File.Exists(Path.Combine(root,"DA-Library.exe"))?root:null;
        }
    }
    static string LocateRoot()
    {
        string folder=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        // Versioned candidates can be built while an earlier edition is open.
        string parent=Path.GetDirectoryName(folder);
        if(Path.GetFileName(parent)=="staged"&&Path.GetFileName(Path.GetDirectoryName(parent))=="build")folder=parent;
        // Candidate builds stay out of the launcher's folder. They use the
        // same local assets, then the tested EXE can replace the main build.
        if(Path.GetFileName(folder)=="staged"&&Path.GetFileName(Path.GetDirectoryName(folder))=="build")
        {
            string project=Path.GetDirectoryName(Path.GetDirectoryName(folder));
            if(File.Exists(Path.Combine(project,"build-launcher.ps1"))&&Directory.Exists(Path.Combine(project,"theme")))return project;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }
    internal static readonly string Game = Path.Combine(Root, "runtime", "Darkages.exe");
    internal static readonly string Manifest = Path.Combine(Root, "build", "manifest.json");
    internal static readonly string Report = Path.Combine(Root, "build", "runtime-validation.json");
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static FileStream installationLease;

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if(HostUpdater.RecoverBeforeLaunch())return 0;
            if(args.Length>0&&args[0]=="--updater-ui-checks"){
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Directory.CreateDirectory(Path.Combine(Root,"build"));
                using(var owner=new Form())using(var updater=new HostUpdater(owner,message=>{},()=>{})){updater.LastStatus="Updates are ready. Library and ads use separate signed releases.";updater.Show(Path.Combine(Root,"build","updater-ui-check.png"));}
                return 0;
            }
            if(InstallRoot!=null){
                try{installationLease=new FileStream(Path.Combine(InstallRoot,"maintenance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);}
                catch(IOException){MessageBox.Show("An installation update is running, or this edition is already open. Try again after it finishes.","Foam DA");return 1;}
                AppDomain.CurrentDomain.ProcessExit+=delegate{if(installationLease!=null)installationLease.Dispose();};
            }
            if(args.Length>0&&args[0]=="--client-install-checks"){GameInstallationChecks.Run(args.Length>1?args[1]:GameInstallation.DefaultFolder);return 0;}
            if(args.Length>0&&args[0]=="--library")
            {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new GameHostForm(false,true));return 0;}
            if (args.Length > 0 && args[0] == "--open-host")
            {
                LaunchInteractive(Application.ExecutablePath, " --host");
                return 0;
            }
            if(args.Length>0&&args[0]=="--open-preview")
            {LaunchInteractive(Process.GetCurrentProcess().MainModule.FileName," --host-preview");return 0;}
            if(args.Length>0&&args[0]=="--host-preview")
            {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new GameHostForm(false));return 0;}
            if (args.Length > 0 && args[0] == "--host-checks")
            {
                HostChecks.Run();
                return 0;
            }
            if(args.Length>0&&args[0]=="--minimap-repaint-checks"){Application.EnableVisualStyles();MinimapRepaintChecks.Run();return 0;}
            if(args.Length>0&&args[0]=="--macro-scheduling-checks"){Application.EnableVisualStyles();MacroSchedulingChecks.Run();return 0;}
            if(args.Length>0&&args[0]=="--feature-probe"){HostFeatureChecks.Probe();return 0;}
            if(args.Length>0&&args[0]=="--login-probe"){HostFeatureChecks.ProbeLogin();return 0;}
            if(args.Length>0&&args[0]=="--character-preview"){HostFeatureChecks.PreviewCharacters();return 0;}
            if(args.Length>0&&args[0]=="--open-character-preview")
            {LaunchInteractive(Process.GetCurrentProcess().MainModule.FileName," --character-preview");return 0;}
            if(args.Length>0&&args[0]=="--open-login-probe")
            {LaunchInteractive(Process.GetCurrentProcess().MainModule.FileName," --login-probe");return 0;}
            if(args.Length>0&&args[0]=="--open-feature-probe")
            {LaunchInteractive(Process.GetCurrentProcess().MainModule.FileName," --feature-probe");return 0;}
            if(args.Length>0&&args[0]=="--map-preview")
            {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);HostFeatureChecks.ShowMap();return 0;}
            if(args.Length>0&&args[0]=="--open-map-preview")
            {LaunchInteractive(Process.GetCurrentProcess().MainModule.FileName," --map-preview");return 0;}
            if(args.Length>0&&args[0]=="--zoom-probe")
            {
                WorldZoomChecks.Probe();return 0;
            }
            if(args.Length>0&&(args[0]=="--layer-probe"||args[0]=="--map-probe"))
            {
                var candidates=GameSessions.Running();
                if(candidates.Count==0)throw new InvalidOperationException("No isolated client.");
                for(int i=1;i<candidates.Count;i++)candidates[i].Dispose();
                using(var process=candidates[0])
                {
                    if(process==null)throw new InvalidOperationException("No isolated client.");
                    using(var capture=new GameFrame(process))
                    {
                        capture.FogLayersRequested=true;capture.ExtraWorldRequested=args[0]=="--map-probe";capture.StandardMiniMapRequested=args[0]=="--map-probe";var watch=Stopwatch.StartNew();
                        int minimum=int.MaxValue,maximum=0,changes=0,last=-1,invalid=0,previousFrame=0;
                        int seconds=args.Length>1?Math.Max(2,Math.Min(30,int.Parse(args[1]))):2;
                        while(watch.ElapsedMilliseconds<seconds*1000){
                            capture.Update();
                            if(capture.Frames!=previousFrame){previousFrame=capture.Frames;
                                int count=0;foreach(byte p in capture.ProtectedPixels)if(p!=0)count++;
                                minimum=Math.Min(minimum,count);maximum=Math.Max(maximum,count);
                                if(last>=0&&last!=count)changes++;last=count;if(!capture.LayersReady)invalid++;
                            }System.Threading.Thread.Sleep(15);
                        }
                        File.WriteAllText(Path.Combine(Root,"build",args[0]=="--map-probe"?"map-probe.json":"layer-probe.json"),Json.Serialize(new {ready=capture.LayersReady,protected_min=minimum,protected_max=maximum,mask_changes=changes,invalid_frames=invalid,layer_misses=capture.LayerMisses,layer_failure=capture.LayerFailure,frames=capture.Frames,captured=capture.CapturedFrames,max_capture_gap_ms=capture.MaxCaptureGapMilliseconds,capture_error=capture.ReadStatus,scene_ready=capture.Scene.Ready,direction=capture.Scene.Direction,origin=new[]{capture.Scene.Origin.X,capture.Scene.Origin.Y},player=new[]{capture.Scene.Player.X,capture.Scene.Player.Y},markers=capture.Scene.Markers.Length,tab_layer=capture.TabLayer.Visible}));
                    }
                }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--tool-probe")
            {
                using(var process=FindTest())
                {
                    if(process==null)throw new InvalidOperationException("No test client is running.");
                    using(var capture=new GameFrame(process))
                    {
                        var watch=Stopwatch.StartNew();
                        while(watch.ElapsedMilliseconds<3000){capture.Update();System.Threading.Thread.Sleep(10);}
                        using(var tools=new GameTools(capture))
                        {
                            var context=tools.Context();var abilities=tools.Abilities();tools.PollMap();
                            int skills=0,spells=0;foreach(var ability in abilities){if(ability.Spell)spells++;else skills++;}
                            File.WriteAllText(Path.Combine(Root,"build","tool-probe.json"),Json.Serialize(new {
                                  status="read_only",frames=capture.Frames,skipped=capture.SkippedFrames,captured=capture.CapturedFrames,max_capture_gap_ms=capture.MaxCaptureGapMilliseconds,
                                skills=skills,spells=spells,alive=context.Alive,compact=context.Compact,chat=context.Chat,dialog=context.Dialog,transfer=context.Transfer,
                                tab_map_visible=tools.MapVisible,tab_map_scale=tools.MapScale}));
                        }
                    }
                }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--probe")
            {
                SaveProbe();
                return 0;
            }
            if (args.Length > 0 && args[0] == "--launch")
            {
                Launch();
                return 0;
            }
            if (args.Length > 0 && args[0] == "--apply-update")
            {
                ApplyPreparedUpdate();
                return 0;
            }
            if (args.Length > 0 && args[0] == "--apply-client-update")
            {
                ApplyPreparedClientUpdate();
                return 0;
            }
            if (args.Length > 0 && args[0] == "--apply-display-update")
            {
                ApplyPreparedClientUpdate(true);
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool openHost=(args.Length > 0 && args[0] == "--host") ||
                args.Length==0;
            Application.Run(openHost ? (Form)new GameHostForm() : new LauncherForm());
            return 0;
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, Json.Serialize(new { status = "error", message = error.Message }));
            if (args.Length == 0) MessageBox.Show(error.Message, "DA widescreen prototype");
            return 1;
        }
    }

    internal static string HashFile(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    internal static void Verify()
    {
        var manifest = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Manifest));
        if (HashFile(Game) != (string)manifest["test_client_sha256"] ||
            HashFile(Path.Combine(Root, "runtime", "setoa.dat")) != (string)manifest["test_setoa_sha256"])
            throw new InvalidOperationException("The test client or layout archive changed. Rebuild before launching.");
    }

    internal static void VerifyHostedGame(string game)
    {
        if(!GameSessions.IsGamePath(game))throw new InvalidOperationException("Only isolated Foam DA games can be hosted.");
        if(String.Equals(game,Game,StringComparison.OrdinalIgnoreCase)){Verify();return;}
        string folder=Path.GetDirectoryName(game);
        var manifest=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"host-manifest.json")));
        if(HashFile(game)!=(string)manifest["test_client_sha256"] || HashFile(Path.Combine(folder,"setoa.dat"))!=(string)manifest["test_setoa_sha256"])
            throw new InvalidOperationException("The session's game or layout changed.");
    }

    internal static Process FindTest()
    {
        foreach (var process in Process.GetProcessesByName("Darkages"))
        {
            try
            {
                if (String.Equals(process.MainModule.FileName, Game, StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            process.Dispose();
        }
        return null;
    }

    internal static void Launch()
    {
        Application.EnableVisualStyles();
        if(!GameInstallation.Choose(null))return;
        string game=GameSessions.Prepare("960x600");
        uint id = LaunchInteractive(game, "");
        File.WriteAllText(Path.Combine(Root, "build", "last-launch.json"),
            Json.Serialize(new { process_id = id, launched_utc = DateTime.UtcNow.ToString("o"), path = game }));
    }

    internal static uint LaunchInteractive(string application, string arguments)
    {
        // Use the user's ordinary interactive desktop, including when this tool
        // is started by a development shell on a separate desktop.
        var start = new StartupInfo();
        start.Size = (uint)Marshal.SizeOf(typeof(StartupInfo));
        start.Desktop = "winsta0\\default";
        start.Flags = 1; // STARTF_USESHOWWINDOW
        start.ShowWindow = 1; // SW_SHOWNORMAL
        ProcessInfo info;
        if (!CreateProcess(application, new StringBuilder("\"" + application + "\"" + arguments), IntPtr.Zero, IntPtr.Zero,
            false, 0, IntPtr.Zero, Path.GetDirectoryName(application), ref start, out info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { return info.ProcessId; }
        finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
    }

    internal static void ApplyProfile(string name)
    {
        if (name != "960x600" && name != "1088x640" && name != "1152x680")
            throw new InvalidOperationException("Unknown host profile.");
        Verify();
        string folder = Path.Combine(Root, "build", "profiles", name);
        var selected = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        selected["base_test_client_sha256"] = HashFile(Game);
        selected["base_test_setoa_sha256"] = HashFile(Path.Combine(Root, "runtime", "setoa.dat"));
        string update = Path.Combine(Root, "build", "display-update");
        Directory.CreateDirectory(update);
        File.Copy(Path.Combine(folder, "Darkages.exe"), Path.Combine(update, "Darkages.exe"), true);
        File.Copy(Path.Combine(folder, "setoa.dat"), Path.Combine(update, "setoa.dat"), true);
        File.WriteAllText(Path.Combine(update, "manifest.json"), Json.Serialize(selected));
        ApplyPreparedClientUpdate(true);
    }

    internal static void ApplyPreparedUpdate()
    {
        Verify();
        using (var running = FindTest())
            if (running != null) throw new InvalidOperationException("Close the test game before applying the UI update.");
        string update = Path.Combine(Root, "build", "update");
        string pendingManifest = File.ReadAllText(Path.Combine(update, "manifest.json"));
        var pending = Json.Deserialize<Dictionary<string, object>>(pendingManifest);
        if (HashFile(Game) != (string)pending["test_client_sha256"] ||
            HashFile(Path.Combine(update, "setoa.dat")) != (string)pending["test_setoa_sha256"])
            throw new InvalidOperationException("The prepared update does not match this test build.");
        string archive = Path.Combine(Root, "runtime", "setoa.dat");
        if (HashFile(archive) == (string)pending["test_setoa_sha256"]) return;
        string backupName = "before-ui-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        string backup = Path.Combine(Root, "build", backupName + ".setoa.dat");
        string backupManifest = Path.Combine(Root, "build", backupName + ".manifest.json");
        // A live mapped image cannot be opened for writing. Hold the exclusive
        // handle through the update so a simultaneous launch cannot race it.
        using (var guard = File.Open(Game, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            File.Copy(archive, backup, false);
            File.Copy(Manifest, backupManifest, false);
            try
            {
                string nextArchive = archive + ".next";
                File.Copy(Path.Combine(update, "setoa.dat"), nextArchive, true);
                File.Replace(nextArchive, archive, null);
                File.WriteAllText(Manifest + ".next", pendingManifest);
                File.Replace(Manifest + ".next", Manifest, null);
            }
            catch
            {
                File.Copy(backup, archive, true);
                File.Copy(backupManifest, Manifest, true);
                throw;
            }
        }
    }

    internal static void ApplyPreparedClientUpdate(bool includeArchive = false)
    {
        Verify();
        using (var running = FindTest())
            if (running != null) throw new InvalidOperationException("Close the test game before applying the client update.");
        string update = Path.Combine(Root, "build", includeArchive ? "display-update" : "client-update");
        string pendingText = File.ReadAllText(Path.Combine(update, "manifest.json"));
        var pending = Json.Deserialize<Dictionary<string, object>>(pendingText);
        string oldManifest = File.ReadAllText(Manifest);
        var current = Json.Deserialize<Dictionary<string, object>>(oldManifest);
        string preparedGame = Path.Combine(update, "Darkages.exe");
        string archive = Path.Combine(Root, "runtime", "setoa.dat");
        string preparedArchive = Path.Combine(update, "setoa.dat");
        if (HashFile(preparedGame) != (string)pending["test_client_sha256"] ||
            HashFile(includeArchive ? preparedArchive : archive) != (string)pending["test_setoa_sha256"] ||
            (string)current["source_client_sha256"] != (string)pending["source_client_sha256"] ||
            (string)current["source_setoa_sha256"] != (string)pending["source_setoa_sha256"] ||
            (!includeArchive && Json.Serialize(current["logical_size"]) != Json.Serialize(pending["logical_size"])))
            throw new InvalidOperationException("The prepared client update does not match this test build.");
        if (HashFile(Game) == (string)pending["test_client_sha256"] &&
            HashFile(archive) == (string)pending["test_setoa_sha256"]) return;
        if (includeArchive && (HashFile(Game) != (string)pending["base_test_client_sha256"] ||
            HashFile(archive) != (string)pending["base_test_setoa_sha256"]))
            throw new InvalidOperationException("The prepared display update targets a different starting build.");
        byte[] replacement = File.ReadAllBytes(preparedGame);
        string backup = Path.Combine(Root, "build", "before-client-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        // Keep the file unlaunchable throughout replacement. Windows rejects
        // this writable exclusive open if a process already maps the image.
        using (var guard = File.Open(Game, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            if (guard.Length != replacement.Length) throw new InvalidOperationException("Unexpected executable length.");
            byte[] original = new byte[replacement.Length];
            int offset = 0;
            while (offset < original.Length)
            {
                int read = guard.Read(original, offset, original.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            File.WriteAllBytes(backup + ".exe", original);
            File.WriteAllText(backup + ".manifest.json", oldManifest);
            if (includeArchive) File.Copy(archive, backup + ".setoa.dat", false);
            try
            {
                if (includeArchive)
                {
                    File.Copy(preparedArchive, archive + ".next", true);
                    File.Replace(archive + ".next", archive, null);
                }
                guard.Position = 0;
                guard.Write(replacement, 0, replacement.Length);
                guard.Flush(true);
                File.WriteAllText(Manifest + ".next", pendingText);
                File.Replace(Manifest + ".next", Manifest, null);
            }
            catch
            {
                guard.Position = 0;
                guard.Write(original, 0, original.Length);
                guard.Flush(true);
                if (includeArchive) File.Copy(backup + ".setoa.dat", archive, true);
                File.WriteAllText(Manifest, oldManifest);
                throw;
            }
        }
    }

    internal static Dictionary<string, object> SaveProbe()
    {
        Verify();
        using (var process = FindTest())
        {
            if (process == null) throw new InvalidOperationException("The isolated test client is not running or is inaccessible.");
            var report = Probe(process);
            File.WriteAllText(Report, Json.Serialize(report));
            return report;
        }
    }

    internal static Dictionary<string, object> Probe(Process process)
    {
        var report = new Dictionary<string, object>();
        report["time_utc"] = DateTime.UtcNow.ToString("o");
        report["process_id"] = process.Id;
        report["test_image_verified"] = true;
        report["inspection"] = "Read-only render structures; no credentials, packets or entity state collected";
        var handle = OpenProcess(0x0410, false, process.Id); // QUERY_INFORMATION | VM_READ
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            uint image = unchecked((uint)process.MainModule.BaseAddress.ToInt32());
            report["image_base"] = "0x" + image.ToString("x8");
            uint video = ReadU32(handle, image + 0x2da3a4);
            uint root = ReadU32(handle, image + 0x33d94c);
            uint mapInterface = ReadU32(handle, image + 0x33d960);
            uint events = ReadU32(handle, image + 0x2d9260);
            if (events != 0)
            {
                int[] pointer = ReadInts(handle, events + 0x10, 2);
                report["event_pointer_xy"] = new int[] { pointer[1], pointer[0] };
            }
            if (video != 0)
            {
                // Process.MainWindowHandle enumerates the caller's desktop. Use
                // the actual HWND retained by VideoSystem across desktop boundaries.
                IntPtr window = new IntPtr(unchecked((int)ReadU32(handle, video + 0x3c)));
                report["window_handle"] = "0x" + window.ToInt32().ToString("x8");
                uint windowProcess;
                uint windowThread = GetWindowThreadProcessId(window, out windowProcess);
                if (windowThread != 0) report["window_visible"] = IsWindowVisible(window);
                else report["window_access"] = "Unavailable from the diagnostic process desktop";
                NativeRect bounds;
                if (GetWindowRect(window, out bounds)) report["window_rectangle"] = new int[] { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom };
                if (GetClientRect(window, out bounds)) report["window_client_size"] = new int[] { bounds.Right, bounds.Bottom };
                report["window_desktop"] = DesktopName(GetThreadDesktop(windowThread));
                report["probe_desktop"] = DesktopName(GetThreadDesktop(GetCurrentThreadId()));
                report["video_logical_width"] = ReadU32(handle, video + 0x1c);
                report["video_logical_height"] = ReadU32(handle, video + 0x20);
                uint canvas = ReadU32(handle, video + 0x24);
                if (canvas != 0) report["main_canvas_bounds_raw"] = ReadInts(handle, canvas + 0x5c, 4);
            }
            if (root != 0) report["root_canvas_bounds_raw"] = ReadInts(handle, root + 0x5c, 4);
            report["world_initialized"] = mapInterface != 0;
            if (mapInterface != 0)
            {
                uint table = ReadU32(handle, mapInterface);
                if (ReadU32(handle, table + 0x4c) != image + 0x1f9b20)
                    throw new InvalidOperationException("Unexpected MapInterface vtable. World probe stopped.");
                uint world = mapInterface - 0x2e8;
                report["world_content_bounds_raw"] = ReadInts(handle, world + 0x2d0, 4);
                report["world_canvas_bounds_raw"] = ReadInts(handle, world + 0x5c, 4);
                uint renderer = ReadU32(handle, world + 0x20c);
                if (renderer != 0)
                {
                    uint ground = ReadU32(handle, renderer + 0xc);
                    if (ground != 0)
                    {
                        uint groundCanvas = ReadU32(handle, ground + 0x54);
                        if (groundCanvas != 0) report["ground_canvas_bounds_raw"] = ReadInts(handle, groundCanvas + 0x5c, 4);
                    }
                }
            }
            report["status"] = "render_structures_read";
            report["visual_gameplay_validation"] = "pending";
        }
        finally { CloseHandle(handle); }
        return report;
    }

    static byte[] Read(IntPtr handle, uint address, int length)
    {
        if (address < 0x10000 || length < 1 || length > 64)
            throw new InvalidOperationException("Invalid render-memory read");
        byte[] bytes = new byte[length];
        IntPtr count;
        if (!ReadProcessMemory(handle, new IntPtr(unchecked((int)address)), bytes, new IntPtr(length), out count) || count.ToInt32() != length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return bytes;
    }
    static uint ReadU32(IntPtr handle, uint address) { return BitConverter.ToUInt32(Read(handle, address, 4), 0); }
    static int[] ReadInts(IntPtr handle, uint address, int count)
    {
        byte[] bytes = Read(handle, address, count * 4);
        int[] values = new int[count];
        for (int i = 0; i < count; i++) values[i] = BitConverter.ToInt32(bytes, i * 4);
        return values;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] data, IntPtr length, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo
    {
        public uint Size;
        public string Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint bytes, out uint required);
    static string DesktopName(IntPtr desktop)
    {
        var name = new StringBuilder(260);
        uint required;
        return GetUserObjectInformation(desktop, 2, name, 520, out required) ? name.ToString() : "unavailable";
    }
}

internal sealed class LauncherForm : Form
{
    readonly TextBox details = new TextBox();
    internal LauncherForm()
    {
        Text = "Foam DA";
        Width = 740;
        Height = 470;
        StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        var launch = new Button { Text = "Launch test game", AutoSize = true };
        var inspect = new Button { Text = "Refresh diagnostics", AutoSize = true };
        var host = new Button { Text = "Open game + library host", AutoSize = true };
        host.Click += delegate { Run(delegate { Program.LaunchInteractive(Path.Combine(Program.Root, "Foam-DA.exe"), " --host"); }); };
        var library = new Button { Text = "Open Library", AutoSize = true };
        library.Click += delegate { Run(delegate { Program.LaunchInteractive(Path.Combine(Program.Root, "Foam-DA.exe"), " --library"); }); };
        var update = new Button { Text = "Apply prepared display update", AutoSize = true };
        launch.Click += delegate { Run(delegate { Program.Launch(); details.Text = "Test game launched. Sign in manually when ready, then refresh diagnostics.\r\n\r\nYour installed game is unchanged."; }); };
        inspect.Click += delegate { Run(delegate { details.Text = Program.Json.Serialize(Program.SaveProbe()).Replace(",", ",\r\n"); }); };
        update.Click += delegate { Run(delegate { Program.ApplyPreparedClientUpdate(true); details.Text = "Display update applied. Launch the test game to use the expanded view."; }); };
        panel.Controls.Add(launch);
        panel.Controls.Add(host);
        panel.Controls.Add(library);
        panel.Controls.Add(inspect);
        panel.Controls.Add(update);
        details.Multiline = true;
        details.ReadOnly = true;
        details.ScrollBars = ScrollBars.Vertical;
        details.Dock = DockStyle.Fill;
        details.Font = new System.Drawing.Font("Consolas", 10);
        details.Text = "Experimental native expanded view\r\n\r\nOriginal-size HUD beneath an expanded map. The taller build moves the full bottom HUD downward and keeps the side border at the right edge.\r\n\r\nThis is a separate game copy. Sign in yourself to test the world view.\r\nThe server still determines which live objects are available.\r\n\r\nRefresh diagnostics to record canvas dimensions and the game pointer position.\r\nNo account credentials or packets are collected.\r\n\r\nSource and build manifest: prototype\\widescreen";
        Controls.Add(details);
        Controls.Add(panel);
    }
    void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { details.Text = error.Message; }
    }
}
