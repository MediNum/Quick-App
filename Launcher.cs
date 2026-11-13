using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Microsoft.Win32;

[assembly:System.Reflection.AssemblyTitle("Quick App")]
[assembly:System.Reflection.AssemblyProduct("Quick App")]
[assembly:System.Reflection.AssemblyVersion("0.1.3.0")]
[assembly:System.Reflection.AssemblyFileVersion("0.1.3.0")]

public class Entry {
    public string Id {get;set;} public string Name {get;set;} public string Path {get;set;}
    public bool Unpinned {get;set;} public string Args {get;set;} public string Icon {get;set;} public string Group {get;set;}
}
public class Library {
    public List<Entry> Apps = new List<Entry>();
    public List<string> Folders = new List<string>();
    public int Size = 104;
    public int Gap = 10;
    public bool Dark;
    public bool HomeInitialized;
}
public class Launcher : Window {
    static readonly string Data = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppShelf", "Data");
    static readonly string Config = System.IO.Path.Combine(Data,"library.json");
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    Library library;
    bool compact;
    int modalDepth;
    Rect expandedBounds;
    WindowState expandedState;
    System.Windows.Point compactOrigin;
    Rect? snappedWorkArea;
    bool snapAnimating;
    Matrix snapToDevice;
    System.Windows.Point snapDeviceOrigin;
    IntPtr snapHandle;
    System.Drawing.Rectangle? lastSnapFrame;
    readonly Stopwatch snapClock=new Stopwatch();
    Rect snapStart;
    Rect snapTarget;
    System.Windows.Point? dragPointer;
    System.Windows.Point dragWindow;

    readonly WrapPanel grid = new WrapPanel();
    readonly TextBox query = new TextBox { Width=370, Margin=new Thickness(5), Padding=new Thickness(12), FontSize=17 };
    readonly ComboBox groups = new ComboBox { Width=140, Margin=new Thickness(5) };
    readonly TextBlock status = new TextBlock { Margin=new Thickness(5,10,5,0) };
    readonly MenuItem scan = new MenuItem { Header="重新扫描" };
    readonly Dictionary<string,ImageSource> icons = new Dictionary<string,ImageSource>(StringComparer.OrdinalIgnoreCase);
    static readonly string[] ShortcutRoots = {
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
    };
    static readonly string Notes = "0.1.3 · 设置菜单\n\n• 设置入口移至标题栏左上角。\n• 重新扫描、布局设置和版本更新统一收进设置菜单。\n• 修复右上角窗口控制按钮不显示的问题。\n• 点击窗口外部时自动收缩，再次激活时恢复。\n• 收缩时左右居中、顶边固定，收缩后可按住右键拖动。\n• 拖到屏幕边缘附近松手时，窗口会带着磁吸动画收缩并贴边。\n• 优化磁吸动画，减少窗口重复重绘。\n• 收缩状态下点击应用图标可直接启动。";
    [STAThread] public static void Main(string[] args) {
        if(args.Contains("--self-test")) {
            string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try {
                string exe = System.IO.Path.Combine(temp,"Test App.exe");
                File.Copy(typeof(Launcher).Assembly.Location,exe);
                var found = Discover(new[]{temp},false).Single();
                var state = new Library();
                state.Apps.Add(found);
                var serializer = new JavaScriptSerializer();
                var restored = serializer.Deserialize<Library>(serializer.Serialize(state));
                if(found.Path!=exe || found.Name!="Test App" || restored.Apps.Single().Id!=found.Id)
                    throw new InvalidOperationException("Scan or library round trip failed.");
                var ui = new Launcher(restored);
                ui.Render();
                var header=(DockPanel)((DockPanel)ui.Content).Children[0];
                var titleBar=(StackPanel)header.Children[1];
                var captionButtons=((StackPanel)header.Children[0]).Children.Cast<Button>().ToArray();
                header.Measure(new System.Windows.Size(620,32));
                header.Arrange(new Rect(0,0,620,32));
                if(!captionButtons.Select(System.Windows.Automation.AutomationProperties.GetName)
                        .SequenceEqual(new[]{"最小化","最大化","关闭"}) ||
                    captionButtons.Any(button=>button.ActualWidth!=46 || button.ActualHeight!=32 ||
                        !System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(button) ||
                        ((System.Windows.Shapes.Path)button.Content).Data.IsEmpty()) ||
                    captionButtons[2].TranslatePoint(new System.Windows.Point(captionButtons[2].ActualWidth,0),header).X!=620)
                    throw new InvalidOperationException("Caption buttons did not lay out at the right edge.");
                var settings=(MenuItem)titleBar.Children.OfType<Menu>().Single().Items[0];
                var commands=settings.Items.Cast<MenuItem>().Select(item=>(string)item.Header);
                if(!Equals(settings.Header,"设置") ||
                    !commands.SequenceEqual(new[]{"重新扫描","布局设置","版本更新"}) ||
                    !System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(titleBar.Children.OfType<Menu>().Single()))
                    throw new InvalidOperationException("Title bar settings menu is incomplete.");
                var frame=(DockPanel)((DockPanel)ui.Content).Children[1];
                var searchRow=frame.Children.OfType<StackPanel>().Single();
                var scroll=frame.Children.OfType<ScrollViewer>().Single();
                var chrome=System.Windows.Shell.WindowChrome.GetWindowChrome(ui);
                ui.Left=40; ui.Top=50;
                ui.SetCompact(true,chrome,header,frame,searchRow,scroll);
                ui.Render();
                var compactTile=(Button)ui.grid.Children[0];
                compactTile.ApplyTemplate();
                compactTile.Measure(new System.Windows.Size(120,120));
                compactTile.Arrange(new Rect(0,0,120,120));
                if(ui.grid.Children.Count!=1 || !ui.IsApplicationTile(compactTile.Content) || ui.IsApplicationTile(ui.query))
                    throw new InvalidOperationException("Compact app tile click routing failed.");
                if(header.Visibility!=Visibility.Collapsed || ui.status.Visibility!=Visibility.Collapsed ||
                    searchRow.Visibility!=Visibility.Visible || scroll.Visibility!=Visibility.Visible ||
                    ui.Width>=990 || ui.Height>=420 || chrome.CaptionHeight!=0 ||
                    ui.Left!=40+(990-ui.Width)/2 || ui.Top!=50)
                    throw new InvalidOperationException("Compact window did not shrink.");
                bool handled=false;
                if(ui.KeepCompactOnMouseActivate(IntPtr.Zero,0x21,IntPtr.Zero,new IntPtr(0x02040001),ref handled)!=new IntPtr(3) || !handled)
                    throw new InvalidOperationException("Right click activates the compact window.");
                ui.Left+=30; ui.Top+=20;
                ui.SetCompact(false,chrome,header,frame,searchRow,scroll);
                if(header.Visibility!=Visibility.Visible || ui.status.Visibility!=Visibility.Visible ||
                    ui.Left!=70 || ui.Top!=70 || ui.Width!=990 || ui.Height!=650 || chrome.CaptionHeight!=32)
                    throw new InvalidOperationException("Window did not restore.");
                var snapFrom=new Rect(100,200,990,650);
                var snapTo=new Rect(0,250,620,230);
                var firstStep=InterpolateSnap(snapFrom,snapTo,0.25);
                var middleStep=InterpolateSnap(snapFrom,snapTo,0.5);
                if(InterpolateSnap(snapFrom,snapTo,0)!=snapFrom || InterpolateSnap(snapFrom,snapTo,1)!=snapTo ||
                    firstStep.X>=snapFrom.X || middleStep.X>=firstStep.X ||
                    snapFrom.X-firstStep.X>=firstStep.X-middleStep.X ||
                    middleStep.Width>=snapFrom.Width || middleStep.Height<=snapTo.Height)
                    throw new InvalidOperationException("Magnetic snap animation bounds are incorrect.");
                var dpiFrame=SnapDeviceBounds(new Rect(-100,20,620,230),new Rect(-140,10,990,650),
                    new System.Windows.Point(-175,13),new Matrix(1.25,0,0,1.25,0,0));
                if(dpiFrame!=new System.Drawing.Rectangle(-125,26,775,288))
                    throw new InvalidOperationException("Snap frame DPI conversion failed.");
                var workArea=new Rect(-1280,0,1280,720);
                if(NearbyEdge(-1260,-650,-1280,0)!=-1 || NearbyEdge(-640,-10,-1280,0)!=1 ||
                    NearbyEdge(100,680,0,720)!=0 ||
                    FitToWorkArea(new Rect(-650,470,620,190),workArea,1,1)!=new System.Windows.Point(-620,530) ||
                    FitToWorkArea(new Rect(-1320,-30,620,190),workArea,-1,-1)!=new System.Windows.Point(-1280,0))
                    throw new InvalidOperationException("Work area snapping failed.");
                var sample=Enumerable.Range(0,8).Select(i=>new Entry { Id="App"+i+"|", Name="App"+i, Unpinned=true }).ToList();
                Recommend(sample,new Entry[0]);
                if(sample.Count(x=>!x.Unpinned)!=5) throw new InvalidOperationException("Unexpected recommendation count.");
                var legacy=new Library();
                legacy.Apps.AddRange(Enumerable.Range(0,155).Select(i=>new Entry {
                    Id="Legacy"+i+"|", Name="Legacy "+i, Group="新发现", Path=exe
                }));
                legacy.Apps[154].Icon="retained.png";
                InitializeHome(legacy,false,new Entry[0]);
                if(legacy.Apps.Count!=155 || legacy.Apps.Count(x=>!x.Unpinned)!=5 || legacy.Apps[154].Icon!="retained.png")
                    throw new InvalidOperationException("Legacy homepage migration failed.");
                var migratedUi=new Launcher(legacy);
                if(migratedUi.grid.Children.Count!=6 || !Equals(((Button)migratedUi.grid.Children[5]).Content,"＋"))
                    throw new InvalidOperationException("Migrated homepage must render five apps and the add tile.");
                var menu=((Button)migratedUi.grid.Children[0]).ContextMenu;
                if(!menu.Items.Cast<MenuItem>().Any(x=>Equals(x.Header,"更换图标")) ||
                   !menu.Items.Cast<MenuItem>().Any(x=>Equals(x.Header,"从首页删除")) ||
                   !menu.Items.Cast<MenuItem>().Any(x=>Equals(x.Header,"从应用库删除")))
                    throw new InvalidOperationException("Homepage context menu actions missing.");
                legacy.Apps[0].Unpinned=true;
                migratedUi.Pin(legacy.Apps[154]);
                var reloaded=serializer.Deserialize<Library>(serializer.Serialize(legacy));
                if(InitializeHome(reloaded,false,new Entry[0]) || !reloaded.Apps[0].Unpinned || reloaded.Apps.Last().Unpinned)
                    throw new InvalidOperationException("User changes did not survive restart.");
                var first=new Library();
                InitializeHome(first,true,SystemRootApps());
                if(first.Apps.Count(x=>!x.Unpinned)!=5) throw new InvalidOperationException("Fresh homepage recommendation count.");
                if(WebAddress("a & b")!="https://www.bing.com/search?q=a%20%26%20b") throw new InvalidOperationException("Search query was not encoded.");
                if(ui.grid.Children.Count!=2 || !((Button)ui.grid.Children[1]).Content.Equals("＋")) throw new InvalidOperationException("Tile was not rendered.");
            } finally { Directory.Delete(temp,true); }
            return;
        }
        bool owns;
        using(var instance = new System.Threading.Mutex(true,@"Local\AppShelf.0A5F3BC3",out owns)) {
            if(!owns) { MessageBox.Show("启动器已打开。","Quick App"); return; }
            new Application().Run(new Launcher());
        }
    }
    static Button Button(string label) { return new Button { Content=label, Padding=new Thickness(12,6,12,6), Margin=new Thickness(5) }; }
    void Action(Panel p,string label,Action action) {
        var b=Button(label); b.Click+=(s,e)=>action(); p.Children.Add(b);
    }
    public Launcher(Library initial=null) {
        Title="Quick App"; Width=990; Height=650; MinWidth=620; MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        using(var brand=System.Drawing.Icon.ExtractAssociatedIcon(typeof(Launcher).Assembly.Location))
            if(brand!=null) Icon=Imaging.CreateBitmapSourceFromHIcon(brand.Handle,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(32,32));
        library=initial ?? new Library();

        bool hasConfig=File.Exists(Config);
        if(initial==null && hasConfig) {
            try { library=json.Deserialize<Library>(File.ReadAllText(Config)) ?? new Library(); }
            catch(Exception ex) { MessageBox.Show("无法读取应用列表。原文件仍保留。\n"+ex.Message); canSave=false; }
        }
        if(initial==null && canSave && InitializeHome(library,!hasConfig,SystemRootApps())) Save();
        var chrome=new System.Windows.Shell.WindowChrome {
            CaptionHeight=32, GlassFrameThickness=new Thickness(0), UseAeroCaptionButtons=false
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this,chrome);
        var shell=new DockPanel();
        Content=shell;
        var header=new DockPanel { Height=32, Background=Brush("#F8FAFD") };
        var windowButtons=new StackPanel { Orientation=Orientation.Horizontal };
        DockPanel.SetDock(windowButtons,Dock.Right);
        header.Children.Add(windowButtons);
        var minimize=CaptionButton("最小化","M 0,5 H 10");
        minimize.Click+=(sender,e)=>SystemCommands.MinimizeWindow(this);
        windowButtons.Children.Add(minimize);
        var maximize=CaptionButton("最大化","M 0,0 H 10 V 10 H 0 Z");
        maximize.Click+=(sender,e)=>{
            if(WindowState==WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        };
        StateChanged+=(sender,e)=>{
            bool maximized=WindowState==WindowState.Maximized;
            string label=maximized?"还原":"最大化";
            ((System.Windows.Shapes.Path)maximize.Content).Data=Geometry.Parse(
                maximized?"M 0,3 H 7 V 10 H 0 Z M 3,3 V 0 H 10 V 7 H 7":"M 0,0 H 10 V 10 H 0 Z");
            maximize.ToolTip=label;
            System.Windows.Automation.AutomationProperties.SetName(maximize,label);
        };
        windowButtons.Children.Add(maximize);
        var close=CaptionButton("关闭","M 0,0 L 10,10 M 10,0 L 0,10",true);
        close.Click+=(sender,e)=>SystemCommands.CloseWindow(this);
        windowButtons.Children.Add(close);
        var titleBar=new StackPanel { Orientation=Orientation.Horizontal };
        header.Children.Add(titleBar);
        DockPanel.SetDock(header,Dock.Top);
        shell.Children.Add(header);
        titleBar.Children.Add(new System.Windows.Controls.Image {
            Source=Icon, Width=16, Height=16, Margin=new Thickness(10,0,7,0), VerticalAlignment=VerticalAlignment.Center
        });
        titleBar.Children.Add(new TextBlock {
            Text=Title, Foreground=Brush("#172338"), VerticalAlignment=VerticalAlignment.Center
        });
        var menu=new Menu { Background=System.Windows.Media.Brushes.Transparent, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(10,0,0,0) };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(menu,true);
        var settingsMenu=new MenuItem { Header="设置", Padding=new Thickness(10,4,10,4) };
        settingsMenu.Items.Add(scan);
        scan.Click+=async(sender,e)=>await Scan();
        var layout=new MenuItem { Header="布局设置" };
        layout.Click+=(sender,e)=>Settings();
        settingsMenu.Items.Add(layout);
        var updates=new MenuItem { Header="版本更新" };
        updates.Click+=(sender,e)=>KeepExpanded(()=>MessageBox.Show(this,Notes,"版本更新"));
        settingsMenu.Items.Add(updates);
        menu.Items.Add(settingsMenu);
        titleBar.Children.Add(menu);
        groups.Width=108;
        groups.Margin=new Thickness(6,0,0,0);
        groups.VerticalAlignment=VerticalAlignment.Center;
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(groups,true);
        titleBar.Children.Add(groups);
        var frame=new DockPanel { Margin=new Thickness(22) };
        shell.Children.Add(frame);
        DockPanel.SetDock(status,Dock.Bottom); frame.Children.Add(status);
        var searchRow=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Center, Margin=new Thickness(0,44,0,20) };
        DockPanel.SetDock(searchRow,Dock.Top); frame.Children.Add(searchRow);
        query.ToolTip="输入应用名称或网页关键词"; AutomationPropertiesForSearch();
        searchRow.Children.Add(query);
        Action(searchRow,"网页搜索",SearchWeb);
        query.KeyDown+=(sender,e)=>{if(e.Key==Key.Enter){SearchWeb(); e.Handled=true;}};
        grid.HorizontalAlignment=HorizontalAlignment.Center;
        var scroll=new ScrollViewer { Content=grid, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Margin=new Thickness(0,14,0,0), AllowDrop=true };
        scroll.DragOver+=(s,e)=>{ if(e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Copy;e.Handled=true;} };
        scroll.Drop+=(s,e)=>{ var files=e.Data.GetData(DataFormats.FileDrop) as string[]; if(files!=null) AddFiles(files); };
        frame.Children.Add(scroll);
        query.TextChanged+=(s,e)=>Render();
        groups.SelectionChanged+=(s,e)=>Render();
        UpdateGroups();
        if(initial==null) {
            Deactivated+=(sender,e)=>{
                if(IsLoaded && modalDepth==0 && WindowState!=WindowState.Minimized)
                    SetCompact(true,chrome,header,frame,searchRow,scroll);
            };
            Activated+=(sender,e)=>SetCompact(false,chrome,header,frame,searchRow,scroll);

            Closed+=(sender,e)=>StopSnap(false);
            Action snap=()=>{
                if(!IsLoaded || WindowState!=WindowState.Normal)return;
                Rect workArea=GetWorkArea();
                var bounds=new Rect(Left,Top,Width,Height);
                int horizontal=NearbyEdge(bounds.Left,bounds.Right,workArea.Left,workArea.Right);
                int vertical=NearbyEdge(bounds.Top,bounds.Bottom,workArea.Top,workArea.Bottom);
                snappedWorkArea=null;
                if(horizontal==0 && vertical==0)return;
                var compactBounds=SetCompact(true,chrome,header,frame,searchRow,scroll,true);
                var position=FitToWorkArea(compactBounds,workArea,horizontal,vertical);
                snappedWorkArea=workArea;
                AnimateSnap(bounds,new Rect(position.X,position.Y,compactBounds.Width,compactBounds.Height));
            };
            SourceInitialized+=(sender,e)=>{
                var source=(HwndSource)PresentationSource.FromVisual(this);
                source.AddHook(KeepCompactOnMouseActivate);
                bool moving=false;
                source.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>{
                    const int Moving=0x0216, ExitSizeMove=0x0232;
                    if(message==Moving) moving=true;
                    if(message==ExitSizeMove && moving) {
                        moving=false;
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,snap);
                    }
                    return IntPtr.Zero;
                });
            };
            PreviewMouseLeftButtonDown+=(sender,e)=>{
                if(!compact || !IsActive || IsApplicationTile(e.OriginalSource))return;
                SetCompact(false,chrome,header,frame,searchRow,scroll);
                e.Handled=true;
            };
            PreviewMouseRightButtonDown+=(sender,e)=>{
                if(!compact)return;
                StopSnap(true);
                if(Mouse.Capture(this)) {
                    dragPointer=PointToScreen(e.GetPosition(this));
                    dragWindow=new System.Windows.Point(Left,Top);
                }
                e.Handled=true;
            };
            PreviewMouseMove+=(sender,e)=>{
                if(!dragPointer.HasValue || !IsMouseCaptured)return;
                if(e.RightButton!=MouseButtonState.Pressed) {
                    ReleaseMouseCapture();
                    return;
                }
                var source=PresentationSource.FromVisual(this);
                var delta=source.CompositionTarget.TransformFromDevice.Transform(
                    PointToScreen(e.GetPosition(this))-dragPointer.Value);
                Left=dragWindow.X+delta.X;
                Top=dragWindow.Y+delta.Y;
                e.Handled=true;
            };
            PreviewMouseRightButtonUp+=(sender,e)=>{
                if(!compact)return;
                bool moved=dragPointer.HasValue && (Left!=dragWindow.X || Top!=dragWindow.Y);
                if(IsMouseCaptured) ReleaseMouseCapture();
                if(moved) snap();
                e.Handled=true;
            };
            LostMouseCapture+=(sender,e)=>{if(!IsMouseCaptured)dragPointer=null;};
            ContextMenuOpening+=(sender,e)=>{if(compact)e.Handled=true;};
        }
        Loaded+=async(s,e)=>await Scan();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);

    void AnimateSnap(Rect start,Rect target) {
        StopSnap(false);
        if(start==target || !SystemParameters.ClientAreaAnimation) {
            ApplyBounds(target);
            return;
        }
        var source=(HwndSource)PresentationSource.FromVisual(this);
        snapHandle=source.Handle;
        snapToDevice=source.CompositionTarget.TransformToDevice;
        snapDeviceOrigin=PointToScreen(new System.Windows.Point());
        snapStart=start;
        snapTarget=target;
        lastSnapFrame=null;
        snapAnimating=true;
        snapClock.Restart();
        CompositionTarget.Rendering+=AdvanceSnap;
    }
    void StopSnap(bool finish) {
        if(!snapAnimating)return;
        snapAnimating=false;
        CompositionTarget.Rendering-=AdvanceSnap;
        snapClock.Stop();
        if(finish) ApplyBounds(snapTarget);
    }
    void AdvanceSnap(object sender,EventArgs e) {
        double progress=snapClock.Elapsed.TotalMilliseconds/210;
        if(progress>=1) {
            StopSnap(true);
            return;
        }
        var frame=SnapDeviceBounds(InterpolateSnap(snapStart,snapTarget,progress),snapStart,snapDeviceOrigin,snapToDevice);
        if(lastSnapFrame==frame)return;
        lastSnapFrame=frame;
        const uint NoZOrder=0x0004, NoActivate=0x0010, NoOwnerZOrder=0x0200;
        if(!SetWindowPos(snapHandle,IntPtr.Zero,frame.X,frame.Y,frame.Width,frame.Height,NoZOrder|NoActivate|NoOwnerZOrder))
            StopSnap(true);
    }
    static System.Drawing.Rectangle SnapDeviceBounds(Rect bounds,Rect start,System.Windows.Point origin,Matrix transform) {
        var position=origin+transform.Transform(bounds.Location-start.Location);
        var size=transform.Transform(new Vector(bounds.Width,bounds.Height));
        return new System.Drawing.Rectangle((int)Math.Round(position.X),(int)Math.Round(position.Y),
            (int)Math.Round(size.X),(int)Math.Round(size.Y));
    }
    static Rect InterpolateSnap(Rect start,Rect target,double progress) {
        // Accelerate toward the edge, then ease into the final position.
        double weight=progress*progress*(3-2*progress);
        return new Rect(
            start.X+(target.X-start.X)*weight,
            start.Y+(target.Y-start.Y)*weight,
            start.Width+(target.Width-start.Width)*weight,
            start.Height+(target.Height-start.Height)*weight);
    }
    void ApplyBounds(Rect bounds) {
        Width=bounds.Width;
        Height=bounds.Height;
        Left=bounds.Left;
        Top=bounds.Top;
    }
    Rect GetWorkArea() {
        var source=(HwndSource)PresentationSource.FromVisual(this);
        var screen=System.Windows.Forms.Screen.FromHandle(source.Handle).WorkingArea;
        var origin=PointToScreen(new System.Windows.Point());
        var transform=source.CompositionTarget.TransformFromDevice;
        var offset=transform.Transform(new Vector(screen.Left-origin.X,screen.Top-origin.Y));
        var size=transform.Transform(new Vector(screen.Width,screen.Height));
        return new Rect(Left+offset.X,Top+offset.Y,size.X,size.Y);
    }
    static int NearbyEdge(double start,double end,double workStart,double workEnd) {
        const double distance=24;
        double startGap=Math.Abs(start-workStart);
        double endGap=Math.Abs(end-workEnd);
        if(Math.Min(startGap,endGap)>distance)return 0;
        return startGap<=endGap?-1:1;
    }
    static System.Windows.Point FitToWorkArea(Rect bounds,Rect workArea,int horizontal=0,int vertical=0) {
        double right=Math.Max(workArea.Left,workArea.Right-bounds.Width);
        double bottom=Math.Max(workArea.Top,workArea.Bottom-bounds.Height);
        double x=horizontal<0?workArea.Left:horizontal>0?right:Math.Max(workArea.Left,Math.Min(right,bounds.Left));
        double y=vertical<0?workArea.Top:vertical>0?bottom:Math.Max(workArea.Top,Math.Min(bottom,bounds.Top));
        return new System.Windows.Point(x,y);
    }
    bool IsApplicationTile(object source) {
        var visual=source as Visual;
        if(visual==null)return false;
        for(DependencyObject element=visual;element!=null && element!=grid;element=VisualTreeHelper.GetParent(element)) {
            var tile=element as Button;
            if(tile!=null && tile.Tag is Entry)return true;
        }
        return false;
    }
    IntPtr KeepCompactOnMouseActivate(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
        const int MouseActivate=0x0021, LeftButtonDown=0x0201, RightButtonDown=0x0204, NoActivate=3;
        if(!compact || message!=MouseActivate)return IntPtr.Zero;
        long button=(lParam.ToInt64()>>16)&0xffff;
        if(button!=RightButtonDown) {
            if(button!=LeftButtonDown)return IntPtr.Zero;
            var cursor=System.Windows.Forms.Cursor.Position;
            var hit=InputHitTest(PointFromScreen(new System.Windows.Point(cursor.X,cursor.Y)));
            if(!IsApplicationTile(hit))return IntPtr.Zero;
        }
        handled=true;
        return new IntPtr(NoActivate);
    }
    Rect SetCompact(bool value,System.Windows.Shell.WindowChrome chrome,DockPanel header,DockPanel frame,
        StackPanel searchRow,ScrollViewer scroll,bool deferBounds=false) {
        if(compact==value)return new Rect(Left,Top,Width,Height);
        compact=value;
        Render();
        header.Visibility=status.Visibility=value?Visibility.Collapsed:Visibility.Visible;
        chrome.CaptionHeight=value?0:32;
        frame.Margin=value?new Thickness(18,16,18,16):new Thickness(22);
        searchRow.Margin=value?new Thickness(0,0,0,16):new Thickness(0,44,0,20);
        scroll.Margin=value?new Thickness(0):new Thickness(0,14,0,0);
        if(value) {
            expandedState=WindowState;
            var anchor=new Rect(Left,Top,Width,Height);
            if(expandedState==WindowState.Maximized) {
                var source=PresentationSource.FromVisual(this);
                var origin=source.CompositionTarget.TransformFromDevice.Transform(PointToScreen(new System.Windows.Point()));
                anchor=new Rect(origin.X,origin.Y,ActualWidth,ActualHeight);
            }
            expandedBounds=expandedState==WindowState.Maximized
                ? RestoreBounds : new Rect(Left,Top,Width,Height);
            WindowState=WindowState.Normal;
            MinHeight=0;
            double tileWidth=library.Size+library.Gap;
            double tileHeight=library.Size+12+library.Gap;
            int count=Math.Max(1,grid.Children.Count);
            double availableWidth=anchor.Width;
            double compactWidth=Math.Min(availableWidth,Math.Max(MinWidth,count*tileWidth+58));
            searchRow.Measure(new System.Windows.Size(compactWidth-36,double.PositiveInfinity));
            int columns=Math.Max(1,(int)((compactWidth-58)/tileWidth));
            int rows=Math.Min(2,(int)Math.Ceiling((double)count/columns));
            double compactHeight=Math.Min(anchor.Height,Math.Ceiling(searchRow.DesiredSize.Height+rows*tileHeight+40));
            var bounds=new Rect(anchor.Left+(anchor.Width-compactWidth)/2,anchor.Top,compactWidth,compactHeight);
            compactOrigin=bounds.Location;
            if(!deferBounds) ApplyBounds(bounds);
            return bounds;
        } else {
            StopSnap(true);
            var offset=new System.Windows.Point(Left,Top)-compactOrigin;
            if(IsMouseCaptured) ReleaseMouseCapture();
            MinHeight=420;
            if(!double.IsNaN(expandedBounds.Left)) Left=expandedBounds.Left+offset.X;
            if(!double.IsNaN(expandedBounds.Top)) Top=expandedBounds.Top+offset.Y;
            Width=expandedBounds.Width;
            Height=expandedBounds.Height;
            if(snappedWorkArea.HasValue) {
                var position=FitToWorkArea(new Rect(Left,Top,Width,Height),snappedWorkArea.Value);
                Left=position.X;
                Top=position.Y;
                snappedWorkArea=null;
            }
            WindowState=expandedState;
        }
        return Rect.Empty;
    }
    T KeepExpanded<T>(Func<T> action) {
        modalDepth++;
        try { return action(); }
        finally { modalDepth--; }
    }
    static Button CaptionButton(string label,string geometry,bool isClose=false) {
        var glyph=new System.Windows.Shapes.Path {
            Width=11, Height=11, Stretch=Stretch.None, Data=Geometry.Parse(geometry),
            Stroke=Brush("#172338"), StrokeThickness=1, SnapsToDevicePixels=true
        };
        var button=new Button {
            Width=46, Height=32, Padding=new Thickness(0), Content=glyph, ToolTip=label,
            BorderThickness=new Thickness(0), Background=System.Windows.Media.Brushes.Transparent
        };
        var template=new ControlTemplate(typeof(Button));
        var background=new FrameworkElementFactory(typeof(Border));
        background.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);
        background.AppendChild(content);
        template.VisualTree=background;
        var hover=new Trigger { Property=UIElement.IsMouseOverProperty, Value=true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty,Brush(isClose?"#F9CAD0":"#E0E8F4")));
        template.Triggers.Add(hover);
        button.Template=template;
        System.Windows.Automation.AutomationProperties.SetName(button,label);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button,true);
        return button;
    }
    void AutomationPropertiesForSearch() { System.Windows.Automation.AutomationProperties.SetName(query,"搜索应用"); }
    bool canSave=true;
    void Save() {
        if(!canSave) { status.Text="配置文件读取失败，本次修改仅保留在内存中。"; return; }
        try {
            Directory.CreateDirectory(Data);
            string next=Config+".tmp";
            File.WriteAllText(next,json.Serialize(library));
            if(File.Exists(Config)) File.Replace(next,Config,null); else File.Move(next,Config);
        } catch(Exception ex) { KeepExpanded(()=>MessageBox.Show(this,ex.Message,"保存失败")); }
    }
    static bool ValidApp(string path) {
        string ext=System.IO.Path.GetExtension(path);
        return string.Equals(ext,".exe",StringComparison.OrdinalIgnoreCase) || string.Equals(ext,".lnk",StringComparison.OrdinalIgnoreCase);
    }
    static Entry ReadEntry(string file,object shell=null) {
        string id=file;
        if(shell!=null && string.Equals(System.IO.Path.GetExtension(file),".lnk",StringComparison.OrdinalIgnoreCase)) {
            object link=null;
            try {
                link=((dynamic)shell).CreateShortcut(file);
                string target=((dynamic)link).TargetPath;
                string args=((dynamic)link).Arguments;
                if(!string.IsNullOrWhiteSpace(target) && File.Exists(target)) id=target+"|"+args;
            } catch(System.Runtime.InteropServices.COMException) {}
            finally { if(link!=null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); }
        }
        if(id==file && string.Equals(System.IO.Path.GetExtension(file),".exe",StringComparison.OrdinalIgnoreCase)) id=file+"|";
        return new Entry { Id=id, Name=System.IO.Path.GetFileNameWithoutExtension(file), Path=file, Group="新发现" };
    }
    static List<Entry> Discover(IEnumerable<string> roots,bool shortcuts) {
        var found=new List<Entry>();
        object shell=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            var folders=new Queue<string>(roots.Where(Directory.Exists));
            while(folders.Count>0) {
                string folder=folders.Dequeue();
                try {
                    foreach(string file in Directory.GetFiles(folder,shortcuts?"*.lnk":"*")) {
                        string name=System.IO.Path.GetFileNameWithoutExtension(file);
                        if(ValidApp(file) && !name.StartsWith("uninstall",StringComparison.OrdinalIgnoreCase) && !name.StartsWith("卸载"))
                            found.Add(ReadEntry(file,shell));
                    }
                    foreach(string child in Directory.GetDirectories(folder))
                        if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0) folders.Enqueue(child);
                } catch(UnauthorizedAccessException) {} catch(IOException) {}
            }
        } finally { if(shell!=null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        return found;
    }
    async Task Scan() {
        if(!scan.IsEnabled)return;
        scan.IsEnabled=false; status.Text="正在扫描应用…";
        var extra=library.Folders.ToArray();
        try {
            var found=await Task.Run(()=>Discover(ShortcutRoots,true).Concat(Discover(extra,false)).OrderBy(x=>x.Name).ToList());
            var stale=library.Apps.Where(x=>x.Unpinned && DriveReady(x.Path) && !File.Exists(x.Path)).ToList();
            foreach(var app in stale) { DeleteIconFile(app.Icon); library.Apps.Remove(app); }
            var known=new HashSet<string>(library.Apps.Select(x=>x.Id),StringComparer.OrdinalIgnoreCase);
            var paths=new HashSet<string>(library.Apps.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);
            string ownPath=typeof(Launcher).Assembly.Location;
            var fresh=found.Where(x=>!Same(x.Id,ownPath+"|") && !Same(x.Path,ownPath) && known.Add(x.Id) && paths.Add(x.Path)).ToList();
            foreach(var app in fresh) app.Unpinned=true;
            library.Apps.AddRange(fresh);
            status.Text="扫描完成 · 新增 "+fresh.Count+" 项"+(stale.Count>0?" · 清理失效 "+stale.Count+" 项":"")+" · 应用库共 "+library.Apps.Count+" 项";
            if(fresh.Count>0 || stale.Count>0) Save();
            UpdateGroups();
        } catch(Exception ex) { status.Text="扫描失败 · "+ex.Message; }
        finally { scan.IsEnabled=true; if(CatalogChanged!=null) CatalogChanged(); }
    }
    Action CatalogChanged;
    static bool DriveReady(string path) {
        string root=System.IO.Path.GetPathRoot(path);
        if(string.IsNullOrEmpty(root))return true;
        try { return new DriveInfo(root).IsReady; } catch(ArgumentException) { return true; }
    }
    void DeleteIconFile(string icon) {
        if(string.IsNullOrEmpty(icon))return;
        string folder=System.IO.Path.Combine(Data,"Icons");
        if(!Same(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(icon)),folder))return;
        if(library.Apps.Any(x=>Same(x.Icon,icon)))return;
        try { File.Delete(icon); } catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    static string WebAddress(string term) { return "https://www.bing.com/search?q="+Uri.EscapeDataString(term); }
    void SearchWeb() {
        string term=query.Text.Trim();
        if(term.Length==0) { query.Focus(); return; }
        try { Process.Start(new ProcessStartInfo(WebAddress(term)) { UseShellExecute=true }); }
        catch(Exception ex) { KeepExpanded(()=>MessageBox.Show(this,ex.Message,"网页搜索失败")); }
    }
    void Pin(Entry app) {
        if(!app.Unpinned)return;
        app.Unpinned=false;
        if(SelectedGroup!="全部") app.Group=SelectedGroup;
        library.Apps.Remove(app);
        library.Apps.Add(app);
    }
    void ShowCatalog() {
        var body=new DockPanel { Margin=new Thickness(18) };
        var dialog=new Window { Title="所有应用", Owner=this, Width=560, Height=520, MinHeight=360,
            WindowStartupLocation=WindowStartupLocation.CenterOwner, ShowInTaskbar=false, Content=body };
        var filters=new StackPanel();
        DockPanel.SetDock(filters,Dock.Top); body.Children.Add(filters);
        var filter=Field(filters,"搜索所有应用","");
        var caption=new TextBlock { Margin=new Thickness(0,8,0,8) };
        filters.Children.Add(caption);
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,8,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom); body.Children.Add(actions);
        var list=new ListBox { SelectionMode=SelectionMode.Extended };
        body.Children.Add(list);
        Action refresh=()=>{
            var selected=new HashSet<Entry>(list.SelectedItems.Cast<ListBoxItem>().Select(item=>(Entry)item.Tag));
            list.Items.Clear();
            string term=filter.Text.Trim();
            foreach(var app in library.Apps.Where(x=>x.Name.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(x=>x.Name)) {
                var row=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(3) };
                row.Children.Add(new System.Windows.Controls.Image { Source=GetIcon(app), Width=24, Height=24, Margin=new Thickness(0,0,10,0) });
                row.Children.Add(new TextBlock { Text=app.Name+(app.Unpinned?"":"（已在首页）"), VerticalAlignment=VerticalAlignment.Center });
                var item=new ListBoxItem { Content=row, Tag=app, ToolTip=app.Path, IsEnabled=app.Unpinned };
                list.Items.Add(item);
                if(app.Unpinned && selected.Contains(app)) item.IsSelected=true;
            }
            caption.Text=!scan.IsEnabled?"正在扫描应用…":list.Items.Count+" 个应用 · 按住 Ctrl 可多选";
        };
        Action(actions,"浏览文件…",()=>{PickApps(dialog); refresh();});
        Action(actions,"添加到首页",()=>{
            var selected=list.SelectedItems.Cast<ListBoxItem>().Select(item=>(Entry)item.Tag).ToList();
            if(selected.Count==0) { list.Focus(); return; }
            foreach(var app in selected) Pin(app);
            query.Clear(); Save(); UpdateGroups(); dialog.Close();
        });
        Action(actions,"从应用库删除",()=>{
            var selected=list.SelectedItems.Cast<ListBoxItem>().Select(item=>(Entry)item.Tag).Where(x=>x.Unpinned).ToList();
            if(selected.Count==0) { list.Focus(); return; }
            string message=selected.Count==1
                ?"把「"+selected[0].Name+"」从应用库删除？只移除记录，不删除文件本身。"
                :"把选中的 "+selected.Count+" 项从应用库删除？只移除记录，不删除文件本身。";
            if(MessageBox.Show(dialog,message,"从应用库删除",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            foreach(var app in selected) { DeleteIconFile(app.Icon); library.Apps.Remove(app); }
            Save(); UpdateGroups(); refresh();
        });
        filter.TextChanged+=(sender,e)=>refresh();
        CatalogChanged=refresh;
        try { refresh(); KeepExpanded(()=>dialog.ShowDialog()); }
        finally { CatalogChanged=null; }
    }
    static ControlTemplate RoundedTile() {
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(19));
        border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree=border;
        var hover=new Trigger { Property=UIElement.IsMouseOverProperty, Value=true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty,0.82));
        template.Triggers.Add(hover);
        return template;
    }
    static IEnumerable<Entry> SystemRootApps() {
        string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string system=Environment.GetFolderPath(Environment.SpecialFolder.System);
        var candidates=new[] {
            new { Name="文件资源管理器", Path=System.IO.Path.Combine(windows,"explorer.exe") },
            new { Name="记事本", Path=System.IO.Path.Combine(system,"notepad.exe") },
            new { Name="计算器", Path=System.IO.Path.Combine(system,"calc.exe") },
            new { Name="画图", Path=System.IO.Path.Combine(system,"mspaint.exe") },
            new { Name="截图工具", Path=System.IO.Path.Combine(system,"SnippingTool.exe") },
            new { Name="任务管理器", Path=System.IO.Path.Combine(system,"Taskmgr.exe") },
            new { Name="控制面板", Path=System.IO.Path.Combine(system,"control.exe") }
        };
        foreach(var candidate in candidates.Where(x=>File.Exists(x.Path))) {
            var app=ReadEntry(candidate.Path);
            app.Name=candidate.Name;
            app.Unpinned=true;
            yield return app;
        }
    }
    static bool InitializeHome(Library state,bool newHome,IEnumerable<Entry> fallback) {
        if(state.HomeInitialized)return false;
        // Older versions saved automatically discovered shortcuts as if they were pinned.
        bool legacyBulkHome=!state.Apps.Any(x=>Same(x.Group,"推荐"))
            && state.Apps.Count(x=>!x.Unpinned && Same(x.Group,"新发现"))>5;
        if(newHome || legacyBulkHome) {
            foreach(var app in state.Apps) app.Unpinned=true;
            Recommend(state.Apps,fallback);
        }
        state.HomeInitialized=true;
        return true;
    }
    static void Recommend(List<Entry> apps,IEnumerable<Entry> fallback) {
        var preferred=new[]{"msedge.exe","chrome.exe","explorer.exe","notepad.exe","calc.exe","mspaint.exe","snippingtool.exe","taskmgr.exe","control.exe"};
        var known=new HashSet<string>(apps.Select(x=>x.Id),StringComparer.OrdinalIgnoreCase);
        var candidates=apps.Concat(fallback.Where(x=>known.Add(x.Id))).ToList();
        var selected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picks=candidates.OrderBy(x=>{
            string exe=System.IO.Path.GetFileName(x.Id.Split('|')[0]);
            int rank=Array.FindIndex(preferred,name=>Same(name,exe));
            return rank<0?preferred.Length:rank;
        }).Where(x=>selected.Add(x.Id.Split('|')[0])).Take(5).ToList();
        foreach(var app in picks) {
            if(!apps.Contains(app)) apps.Add(app);
            app.Unpinned=false;
            if(string.IsNullOrEmpty(app.Group)||Same(app.Group,"新发现")) app.Group="推荐";
        }
    }
    string ImportIcon(string path) {
        string ext=System.IO.Path.GetExtension(path);
        if(!Same(ext,".png")&&!Same(ext,".ico"))throw new IOException("请选择 PNG 或 ICO 图标。");
        using(var stream=File.OpenRead(path)) BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
        string folder=System.IO.Path.Combine(Data,"Icons"); Directory.CreateDirectory(folder);
        string copy=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+ext);
        File.Copy(path,copy);
        return copy;
    }
    void ChangeIcon(Entry app) {
        var pick=new OpenFileDialog { Filter="图标|*.png;*.ico" };
        if(KeepExpanded(()=>pick.ShowDialog(this))!=true)return;
        try { app.Icon=ImportIcon(pick.FileName); Save(); Render(); }
        catch(Exception ex) { KeepExpanded(()=>MessageBox.Show(this,ex.Message,"图标无法读取")); }
    }
    void RemoveFromLibrary(Entry app) {
        string message=app.Unpinned
            ?"把「"+app.Name+"」从应用库删除？只移除记录，不删除文件本身。"
            :"「"+app.Name+"」在首页上，删除后会一并从首页移除。\n只移除记录，不删除文件本身。确定删除？";
        if(KeepExpanded(()=>MessageBox.Show(this,message,"从应用库删除",MessageBoxButton.YesNo))!=MessageBoxResult.Yes)return;
        DeleteIconFile(app.Icon);
        library.Apps.Remove(app);
        Save(); UpdateGroups();
    }
    void PickApps(Window owner) {
        var dialog=new OpenFileDialog { Multiselect=true, Filter="应用或快捷方式|*.exe;*.lnk" };
        if(KeepExpanded(()=>dialog.ShowDialog(owner))==true) AddFiles(dialog.FileNames);
    }
    void AddFiles(IEnumerable<string> files) {
        object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try {
            foreach(var file in files.Where(x=>File.Exists(x)&&ValidApp(x))) {
                var app=ReadEntry(file,shell);
                var existing=library.Apps.FirstOrDefault(x=>Same(x.Path,file)||Same(x.Id,app.Id));
                if(existing!=null) { Pin(existing); continue; }
                app.Group=SelectedGroup=="全部"?"手动添加":SelectedGroup;
                library.Apps.Add(app);
            }
        } finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        Save(); UpdateGroups();
    }
    static bool Same(string a,string b) { return string.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
    string SelectedGroup { get { return groups.SelectedItem as string ?? "全部"; } }
    void UpdateGroups() {
        string selected=SelectedGroup;
        var choices=new[]{"全部"}.Concat(library.Apps.Where(x=>!x.Unpinned).Select(x=>x.Group).Where(x=>!string.IsNullOrEmpty(x) && x!="全部").Distinct().OrderBy(x=>x)).ToList();
        groups.ItemsSource=choices;
        groups.SelectedItem=choices.Contains(selected)?selected:"全部";
        Render();
    }
    static SolidColorBrush Brush(string color) { return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)); }
    void Render() {
        Background=new LinearGradientBrush(Brush(library.Dark?"#142036":"#79B6D8").Color,Brush(library.Dark?"#263851":"#DCEEF8").Color,70);
        Foreground=Brush(library.Dark?"#E8EDF8":"#192740");
        status.Foreground=Foreground;
        grid.Children.Clear();
        string term=query.Text.Trim();
        var visible=library.Apps.Where(x=>term.Length>0
            ? x.Name.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0
            : !x.Unpinned && (SelectedGroup=="全部" || x.Group==SelectedGroup));
        foreach(var app in visible) grid.Children.Add(Tile(app));
        if(compact)return;
        var add=new Button { Content="＋", FontSize=36, Width=library.Size, Height=library.Size+12,
            Margin=new Thickness(library.Gap/2.0), Background=Brush(library.Dark?"#35435A":"#C4E0EF"),
            Foreground=Foreground, BorderBrush=Brush(library.Dark?"#526580":"#A6CDE3"), ToolTip="添加应用" };
        add.Template=RoundedTile();
        add.Click+=(sender,e)=>ShowCatalog();
        grid.Children.Add(add);
    }
    UIElement Tile(Entry app) {
        var tile=new Button { Tag=app, Width=library.Size, Height=library.Size+12, Margin=new Thickness(library.Gap/2.0), Background=Brush(library.Dark?"#252D3D":"#FFFFFF"), Foreground=Foreground, BorderBrush=Brush(library.Dark?"#3B465C":"#D9E2F0"), ToolTip=app.Name+"\n"+app.Path, AllowDrop=true };
        tile.Template=RoundedTile();
        var stack=new StackPanel { Width=library.Size-14 };
        var image=new System.Windows.Controls.Image { Width=36, Height=36, Source=GetIcon(app), Margin=new Thickness(0,3,0,7) };
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock { Text=app.Name, TextAlignment=TextAlignment.Center, TextWrapping=TextWrapping.Wrap, MaxHeight=38 });
        tile.Content=stack;
        tile.Click+=(s,e)=>Launch(app);
        var menu=new ContextMenu();
        var edit=new MenuItem {Header="编辑应用"}; edit.Click+=(s,e)=>Edit(app); menu.Items.Add(edit);
        var changeIcon=new MenuItem { Header="更换图标" }; changeIcon.Click+=(sender,e)=>ChangeIcon(app); menu.Items.Add(changeIcon);
        var hide=new MenuItem {Header="从首页删除",IsEnabled=!app.Unpinned}; hide.Click+=(s,e)=>{app.Unpinned=true; Save(); UpdateGroups();}; menu.Items.Add(hide);
        var remove=new MenuItem {Header="从应用库删除"}; remove.Click+=(s,e)=>RemoveFromLibrary(app); menu.Items.Add(remove);
        tile.ContextMenu=menu;
        System.Windows.Point origin=new System.Windows.Point();
        tile.PreviewMouseLeftButtonDown+=(s,e)=>origin=e.GetPosition(this);
        tile.PreviewMouseMove+=(s,e)=>{
            if(compact || app.Unpinned || query.Text.Trim().Length>0 || e.LeftButton!=MouseButtonState.Pressed || !tile.IsMouseCaptured)return;
            var point=e.GetPosition(this);
            if(Math.Abs(point.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            tile.ReleaseMouseCapture(); KeepExpanded(()=>DragDrop.DoDragDrop(tile,new DataObject("AppShelf.Entry",app),DragDropEffects.Move)); e.Handled=true;
        };
        tile.DragOver+=(s,e)=>{if(e.Data.GetDataPresent("AppShelf.Entry")){e.Effects=DragDropEffects.Move;e.Handled=true;}};
        tile.Drop+=(s,e)=>{
            var dropped=e.Data.GetData(DataFormats.FileDrop) as string[];
            if(dropped!=null && dropped.Length==1) {
                string ext=System.IO.Path.GetExtension(dropped[0]);
                if(Same(ext,".png") || Same(ext,".ico")) {
                    e.Handled=true;
                    try {
                        app.Icon=ImportIcon(dropped[0]); Save(); Render();
                    } catch(Exception ex) { KeepExpanded(()=>MessageBox.Show(this,ex.Message,"图标无法读取")); }
                    return;
                }
            }
            var from=e.Data.GetData("AppShelf.Entry") as Entry;
            if(from==null||from==app)return;
            int a=library.Apps.IndexOf(from), b=library.Apps.IndexOf(app);
            if(a<0||b<0)return;
            library.Apps[a]=app; library.Apps[b]=from;
            Save(); Render(); e.Handled=true;
        };
        return tile;
    }
    ImageSource GetIcon(Entry app) {
        string path=string.IsNullOrEmpty(app.Icon)?app.Path:app.Icon;
        ImageSource image;
        if(icons.TryGetValue(path,out image))return image;
        try {
            if(Same(System.IO.Path.GetExtension(path),".png")||Same(System.IO.Path.GetExtension(path),".ico")) {
                using(var stream=File.OpenRead(path)) image=BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames.OrderByDescending(x=>x.PixelWidth).First();
            } else {
                using(var icon=System.Drawing.Icon.ExtractAssociatedIcon(path)) image=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(36,36));
            }
            image.Freeze();
        } catch { image=null; }
        if(image==null && !Same(path,app.Path)) return GetIcon(new Entry{Path=app.Path});
        if(image==null) {
            var geometry=Geometry.Parse("M2,2 L30,2 30,30 2,30 Z M8,8 L24,8 24,11 8,11 Z");
            image=new DrawingImage(new GeometryDrawing(Brush("#5278B9"),null,geometry)); image.Freeze();
        }
        icons[path]=image;
        return image;
    }
    void Launch(Entry app) {
        try {
            if(!File.Exists(app.Path))throw new FileNotFoundException("找不到启动文件，请在编辑中修改路径。");
            var start=new ProcessStartInfo(app.Path,app.Args??"") { UseShellExecute=true };
            if(Same(System.IO.Path.GetExtension(app.Path),".exe")) start.WorkingDirectory=System.IO.Path.GetDirectoryName(app.Path);
            Process.Start(start);
        } catch(Exception ex) { KeepExpanded(()=>MessageBox.Show(this,ex.Message,"启动失败")); }
    }
    Window Dialog(string title,StackPanel body) {
        return new Window { Title=title, Owner=this, WindowStartupLocation=WindowStartupLocation.CenterOwner, Width=490, SizeToContent=SizeToContent.Height, ResizeMode=ResizeMode.NoResize, Content=body, ShowInTaskbar=false };
    }
    static TextBox Field(StackPanel body,string label,string value) {
        body.Children.Add(new TextBlock { Text=label, Margin=new Thickness(0,7,0,3) });
        var box=new TextBox { Text=value??"", Padding=new Thickness(5) }; body.Children.Add(box); return box;
    }
    void Edit(Entry app) {
        var body=new StackPanel { Margin=new Thickness(18) };
        var dialog=Dialog("编辑应用",body);
        var name=Field(body,"名称",app.Name);
        var path=Field(body,"启动文件（.exe 或 .lnk）",app.Path);
        var args=Field(body,"启动参数",app.Args);
        var group=Field(body,"分组（输入新名称可创建分组）",app.Group);
        var icon=Field(body,"自定义图标（可拖入 PNG 或 ICO，留空恢复默认）",app.Icon);
        icon.AllowDrop=true;
        icon.PreviewDragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        icon.PreviewDrop+=(s,e)=>{var files=e.Data.GetData(DataFormats.FileDrop) as string[]; if(files!=null && files.Length>0)icon.Text=files[0];e.Handled=true;};
        Action(body,"浏览图标",()=>{var pick=new OpenFileDialog { Filter="图标|*.png;*.ico" }; if(pick.ShowDialog(dialog)==true)icon.Text=pick.FileName;});
        Action(body,"保存",()=>{
            string file=path.Text.Trim().Trim('"'), selectedIcon=icon.Text.Trim().Trim('"');
            if(string.IsNullOrWhiteSpace(name.Text)||!File.Exists(file)||!ValidApp(file)) { MessageBox.Show(dialog,"请填写名称并选择有效的启动文件。");return; }
            if(library.Apps.Any(x=>x!=app&&Same(x.Path,file))) { MessageBox.Show(dialog,"该启动文件已在列表中。");return; }
            if(selectedIcon.Length>0 && !Same(selectedIcon,app.Icon)) {
                try {
                    string ext=System.IO.Path.GetExtension(selectedIcon);
                    if(!Same(ext,".png")&&!Same(ext,".ico"))throw new IOException("请选择 PNG 或 ICO 图标。");
                    using(var stream=File.OpenRead(selectedIcon)) BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
                    string folder=System.IO.Path.Combine(Data,"Icons"); Directory.CreateDirectory(folder);
                    string copy=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+ext);
                    File.Copy(selectedIcon,copy); selectedIcon=copy;
                } catch(Exception ex) { MessageBox.Show(dialog,ex.Message,"图标无法读取");return; }
            }
            app.Name=name.Text.Trim(); app.Path=file; app.Args=args.Text; app.Group=group.Text.Trim(); app.Icon=selectedIcon;
            Save(); UpdateGroups(); dialog.Close();
        });
        KeepExpanded(()=>dialog.ShowDialog());
    }
    void Settings() {
        var body=new StackPanel { Margin=new Thickness(18) };
        var dialog=Dialog("布局设置",body);
        var size=Field(body,"图标卡片宽度（80–180）",library.Size.ToString());
        var gap=Field(body,"卡片间距（0–32）",library.Gap.ToString());
        var dark=new CheckBox { Content="深色主题", IsChecked=library.Dark, Margin=new Thickness(0,12,0,5) }; body.Children.Add(dark);
        var dirs=Field(body,"额外扫描目录（每行一个，扫描子目录）",string.Join(Environment.NewLine,library.Folders));
        dirs.AcceptsReturn=true; dirs.Height=82; dirs.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        Action(body,"保存设置",()=>{
            int width, spacing;
            if(!int.TryParse(size.Text,out width)||width<80||width>180||!int.TryParse(gap.Text,out spacing)||spacing<0||spacing>32) {MessageBox.Show(dialog,"请输入有效的宽度和间距。");return;}
            var paths=dirs.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().Trim('"')).Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(paths.Any(x=>!Directory.Exists(x))) {MessageBox.Show(dialog,"有扫描目录不存在或无法访问。");return;}
            library.Size=width; library.Gap=spacing; library.Dark=dark.IsChecked==true; library.Folders=paths; Save(); Render(); dialog.Close();
        });
        KeepExpanded(()=>dialog.ShowDialog());
    }
}