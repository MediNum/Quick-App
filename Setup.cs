using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Quick App Setup")]
[assembly: AssemblyProduct("Quick App")]
[assembly: AssemblyVersion("0.1.3.0")]
[assembly: AssemblyFileVersion("0.1.3.0")]

static class Setup {
    const string Version="0.1.3";
    const string Key=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AppShelf";
    static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AppShelf","Program");
    static readonly string Shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Quick App.lnk");

    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length==1 && args[0]=="--self-test") return SelfTest();
            if(args.Length==2 && args[0]=="--finish-uninstall") {
                try { using(var original=Process.GetProcessById(int.Parse(args[1]))) original.WaitForExit(10000); }
                catch(ArgumentException) {}
                Uninstall();
                return 0;
            }
            if(args.Length==1 && args[0]=="--uninstall") {
                if(MessageBox.Show("卸载 Quick App？你的布局和图标会保留。","Quick App 卸载",MessageBoxButtons.YesNo)!=DialogResult.Yes) return 0;
                string running=Path.Combine(Root,"AppShelf.exe");
                if(File.Exists(running) && FileLocked(running)) { MessageBox.Show("Quick App 正在运行，请先退出后再卸载。","Quick App 卸载"); return 0; }
                string temp=Path.Combine(Path.GetTempPath(),"AppShelf-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");
                File.Copy(Assembly.GetExecutingAssembly().Location,temp);
                Process.Start(new ProcessStartInfo(temp,"--finish-uninstall "+Process.GetCurrentProcess().Id){UseShellExecute=false});
                return 0;
            }
            if(MessageBox.Show("为当前 Windows 用户安装 Quick App "+Version+"？","Quick App 安装",MessageBoxButtons.OKCancel)!=DialogResult.OK) return 0;
            string installedExe=Path.Combine(Root,"AppShelf.exe");
            if(File.Exists(installedExe) && FileLocked(installedExe)) { MessageBox.Show("Quick App 正在运行，请先退出后再安装。","Quick App 安装"); return 1; }
            string exe=InstallFiles(Root,Shortcut);
            RemoveOldShortcut(Shortcut,exe,"快应用.lnk");
            RemoveOldShortcut(Shortcut,exe,"应用架.lnk");
            string installedSetup=Path.Combine(Root,"Uninstall.exe");
            using(var key=Registry.CurrentUser.CreateSubKey(Key)) {
                key.SetValue("DisplayName","Quick App");
                key.SetValue("DisplayVersion",Version);
                key.SetValue("DisplayIcon",exe);
                key.SetValue("InstallLocation",Root);
                key.SetValue("UninstallString","\""+installedSetup+"\" --uninstall");
                key.SetValue("NoModify",1);
                key.SetValue("NoRepair",1);
            }
            if(MessageBox.Show("安装完成。现在打开 Quick App？","Quick App 安装",MessageBoxButtons.YesNo)==DialogResult.Yes)
                Process.Start(new ProcessStartInfo(exe){UseShellExecute=true});
            return 0;
        } catch(Exception ex) { if(args.Length==1 && args[0]=="--self-test") return 1; MessageBox.Show(ex.Message,"Quick App 安装程序"); return 1; }
    }
    static string InstallFiles(string folder,string shortcut) {
        Directory.CreateDirectory(folder);
        string exe=Path.Combine(folder,"AppShelf.exe");
        ReplaceFile(exe,stream=>{
            using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("AppShelf.exe")) payload.CopyTo(stream);
        });
        string uninstaller=Path.Combine(folder,"Uninstall.exe");
        string source=Assembly.GetExecutingAssembly().Location;
        if(!string.Equals(uninstaller,source,StringComparison.OrdinalIgnoreCase))
            ReplaceFile(uninstaller,stream=>{using(var setup=File.OpenRead(source)) setup.CopyTo(stream);});
        CreateShortcut(shortcut,exe);
        return exe;
    }
    static void ReplaceFile(string destination,Action<Stream> write) {
        string staged=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=File.Create(staged)) write(stream);
            if(File.Exists(destination)) File.Replace(staged,destination,null); else File.Move(staged,destination);
        } finally { File.Delete(staged); }
    }
    static void RemoveOldShortcut(string shortcut,string target,string oldName) {
        string old=Path.Combine(Path.GetDirectoryName(shortcut),oldName);
        if(!File.Exists(old))return;
        object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object link=null;
        try {
            link=((dynamic)shell).CreateShortcut(old);
            if(string.Equals((string)((dynamic)link).TargetPath,target,StringComparison.OrdinalIgnoreCase)) File.Delete(old);
        } finally {
            if(link!=null) Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
    }
    static void CreateShortcut(string path,string exe) {
        object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object link=null;
        try {
            link=((dynamic)shell).CreateShortcut(path);
            ((dynamic)link).TargetPath=exe;
            ((dynamic)link).WorkingDirectory=Path.GetDirectoryName(exe);
            ((dynamic)link).Save();
        } finally {
            if(link!=null) Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
    }
    static bool FileLocked(string path) {
        try { using(File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) return false; }
        catch(IOException) { return true; }
        catch(UnauthorizedAccessException) { return true; }
    }
    static void Uninstall() {
        string exe=Path.Combine(Root,"AppShelf.exe");
        if(File.Exists(exe) && FileLocked(exe)) { MessageBox.Show("Quick App 正在运行，请先退出后再卸载。","Quick App 卸载"); return; }
        File.Delete(exe);
        File.Delete(Path.Combine(Root,"Uninstall.exe"));
        File.Delete(Shortcut);
        Registry.CurrentUser.DeleteSubKeyTree(Key,false);
        if(Directory.Exists(Root) && Directory.GetFileSystemEntries(Root).Length==0) Directory.Delete(Root);
        MessageBox.Show("Quick App 已卸载。","Quick App 卸载");
    }
    static int SelfTest() {
        string temp=Path.Combine(Path.GetTempPath(),"AppShelf-Test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            string folder=Path.Combine(temp,"Program");
            string shortcut=Path.Combine(temp,"Quick App.lnk");
            string exe=InstallFiles(folder,shortcut);
            InstallFiles(folder,shortcut);
            string old=Path.Combine(temp,"快应用.lnk");
            string unrelated=Path.Combine(temp,"应用架.lnk");
            CreateShortcut(old,exe);
            CreateShortcut(unrelated,Assembly.GetExecutingAssembly().Location);
            RemoveOldShortcut(shortcut,exe,"快应用.lnk");
            RemoveOldShortcut(shortcut,exe,"应用架.lnk");
            if(File.Exists(old) || !File.Exists(unrelated)) return 1;
            if(!File.Exists(shortcut) || !File.Exists(Path.Combine(folder,"Uninstall.exe"))) return 1;
            using(var test=Process.Start(new ProcessStartInfo(exe,"--self-test"){UseShellExecute=false})) {
                if(!test.WaitForExit(20000)) {test.Kill(); test.WaitForExit(); return 2;}
                return test.ExitCode;
            }
        } finally { Directory.Delete(temp,true); }
    }
}