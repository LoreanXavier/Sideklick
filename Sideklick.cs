using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program {
 delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
 static HookProc callback=Mouse;
 static IntPtr hook;

 // Button slots: 1 = side button 1 (Mouse 4), 2 = side button 2 (Mouse 5), 3 = wheel click.
 // Modes: 0 = normal, 1 = disabled, 2 = send F13/F14/F15.
 static int[] modes=new int[4];
 static bool paused;
 static bool[] held=new bool[4], swallowed=new bool[4];
 static volatile bool windowActive, rehookRequested;
 static volatile int pressed;

 static EventWaitHandle reopen, versionMarker;
 static Form form;
 static NotifyIcon tray;
 static Icon windowIcon, trayIcon, trayPausedIcon;
 static Bitmap logoImage;
 static Panel[] rows=new Panel[4];
 static RadioButton[,] radios=new RadioButton[4,3];
 static DateTime[] flashUntil=new DateTime[4];
 static Button pauseButton;
 static Label statusLabel;
 static CheckBox startupBox;
 static ToolStripMenuItem[,] menuModes=new ToolStripMenuItem[4,3];
 static ToolStripMenuItem menuPause, menuStartup;
 static bool syncing, hintShown;

 static readonly bool tr=(Environment.GetEnvironmentVariable("SIDEKLICK_LANG")??CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)=="tr";
 static string T(string en,string trk){return tr?trk:en;}

 // Settings live in %APPDATA% so they survive no matter where the exe is kept (Downloads, a zip, a moved folder).
 static readonly string dataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Sideklick");
 static readonly string settingsPath=Path.Combine(dataDir,"settings.txt");
 static string legacyPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.txt");
 const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";

 static readonly Color Ink=Color.FromArgb(24,35,51), Muted=Color.FromArgb(104,114,130), Line=Color.FromArgb(222,227,235),
  Back=Color.FromArgb(244,246,250), Accent=Color.FromArgb(38,103,224), Good=Color.FromArgb(30,150,95), Warn=Color.FromArgb(214,128,20);

 [StructLayout(LayoutKind.Sequential)] struct Data {public int x,y; public uint mouseData,flags,time; public UIntPtr extra;}
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
 [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
 [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
 [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);

 [STAThread] static int Main(string[] args) {
  bool created;
  using(var mutex=new Mutex(true,"Local\\Sideklick",out created))
  using(reopen=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\Sideklick.Open")) {
   if(!created){
    bool selftestRun=args.Length>0&&args[0]=="/selftest";
    if(selftestRun||!OlderVersionRunning()){reopen.Set();return 0;}
    if(!ReplaceOlderVersion(mutex))return 0;
   }
   // Lets later launches tell this version apart from the original Sideklick, which never creates this event.
   versionMarker=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\Sideklick.Tray");
   hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
   if(hook==IntPtr.Zero){MessageBox.Show("Could not start mouse hook: "+Marshal.GetLastWin32Error(),"Sideklick");return 1;}
   if(args.Length>0&&args[0]=="/selftest"){UnhookWindowsHookEx(hook);return 0;}
   bool quiet=args.Length>0&&args[0]=="--quiet";

   Application.EnableVisualStyles();
   bool firstRun=!File.Exists(settingsPath);
   Load();
   // Opened straight from a zip, the exe lives in a temp folder that Windows deletes; don't point startup there.
   bool temporary=Application.ExecutablePath.StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase);
   if(firstRun){Save();if(!temporary)SetStartup(true);}   // new installs and upgrades start with Windows by default
   else if(StartupEnabled()&&!temporary)SetStartup(true);  // refresh the path in case the exe was moved

   LoadIcons();
   BuildTray();
   PowerModeChangedEventHandler power=delegate(object s,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Resume)rehookRequested=true;};
   SessionSwitchEventHandler session=delegate(object s,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionUnlock)rehookRequested=true;};
   SystemEvents.PowerModeChanged+=power;
   SystemEvents.SessionSwitch+=session;
   try {
    using(var timer=new System.Windows.Forms.Timer{Interval=100}) {
     timer.Tick+=delegate{Tick();};
     timer.Start();
     if(!quiet)ShowWindow();
     if(temporary)MessageBox.Show(T("Sideklick is running from a temporary folder (probably straight from the zip), so it can't start with Windows.\n\nExtract Sideklick.exe to a folder you keep (for example Documents) and open it from there.",
      "Sideklick geçici bir klasörden çalışıyor (muhtemelen doğrudan zip'in içinden), bu yüzden Windows ile başlayamaz.\n\nSideklick.exe'yi kalıcı bir klasöre (ör. Belgeler) çıkar ve oradan aç."),"Sideklick",MessageBoxButtons.OK,MessageBoxIcon.Warning);
     Application.Run();
    }
   } finally {
    SystemEvents.PowerModeChanged-=power;
    SystemEvents.SessionSwitch-=session;
    UnhookWindowsHookEx(hook);Release();
    tray.Visible=false;tray.Dispose();
   }
  } return 0;
 }

 // ---------- replacing the original Sideklick ----------
 static bool OlderVersionRunning(){
  try{using(EventWaitHandle.OpenExisting("Local\\Sideklick.Tray")){}return false;}
  catch(WaitHandleCannotBeOpenedException){return true;}
  catch{return false;}
 }
 static bool ReplaceOlderVersion(Mutex mutex){
  Application.EnableVisualStyles();
  var me=Process.GetCurrentProcess();
  var old=new List<Process>();
  foreach(var name in new[]{"Sideklick",Path.GetFileNameWithoutExtension(Application.ExecutablePath)})
   foreach(var p in Process.GetProcessesByName(name))
    if(p.Id!=me.Id&&p.SessionId==me.SessionId&&!old.Exists(o=>o.Id==p.Id))old.Add(p);
  if(old.Count==0){
   MessageBox.Show(T("An older Sideklick is already running. Open it, click Exit, then open this one again.",
    "Sideklick'in eski bir sürümü zaten çalışıyor. Onu açıp Çıkış'a bas, sonra bunu tekrar aç."),"Sideklick",MessageBoxButtons.OK,MessageBoxIcon.Information);
   return false;
  }
  var answer=MessageBox.Show(T("An older version of Sideklick is running.\n\nReplace it with this version? Your button settings carry over.",
   "Sideklick'in eski bir sürümü çalışıyor.\n\nBu sürümle değiştirilsin mi? Tuş ayarların aynen aktarılır."),"Sideklick",MessageBoxButtons.YesNo,MessageBoxIcon.Question);
  if(answer!=DialogResult.Yes)return false;
  foreach(var p in old){
   // The original keeps settings.txt next to its exe; remember it so Load() can carry the choices over.
   try{var file=Path.Combine(Path.GetDirectoryName(p.MainModule.FileName),"settings.txt");if(File.Exists(file))legacyPath=file;}catch{}
   try{p.Kill();p.WaitForExit(3000);}catch{}
  }
  try{
   if(!mutex.WaitOne(3000)){
    MessageBox.Show(T("Could not close the older Sideklick. Close it yourself, then open this one again.",
     "Eski Sideklick kapatılamadı. Onu kendin kapat, sonra bunu tekrar aç."),"Sideklick");
    return false;
   }
  } catch(AbandonedMutexException){}  // the old process was killed while holding it; we own it now
  return true;
 }

 static void Tick(){
  if(reopen.WaitOne(0))ShowWindow();
  if(rehookRequested){
   // Windows can silently drop low-level hooks after sleep; reinstall to be safe.
   rehookRequested=false;
   var fresh=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
   if(fresh!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=fresh;}
  }
  int p=pressed;
  if(p!=0){pressed=0;flashUntil[p]=DateTime.Now.AddMilliseconds(1200);if(rows[p]!=null)rows[p].Invalidate();}
  for(int b=1;b<=3;b++)if(flashUntil[b]!=DateTime.MinValue&&DateTime.Now>flashUntil[b]){flashUntil[b]=DateTime.MinValue;if(rows[b]!=null)rows[b].Invalidate();}
 }

 // ---------- settings ----------
 static void Load(){
  foreach(var path in new[]{settingsPath,legacyPath}){
   try {
    if(!File.Exists(path))continue;
    var lines=File.ReadAllLines(path);
    for(int i=1;i<=3&&i<=lines.Length;i++){int v;if(int.TryParse(lines[i-1].Trim(),out v)&&v>=0&&v<=2)modes[i]=v;}
    // The pause flag is not restored: after a restart Sideklick always starts active, so it never looks "forgotten".
    return;
   } catch(IOException){} catch(UnauthorizedAccessException){}
  }
 }
 static void Save(){
  try {
   Directory.CreateDirectory(dataDir);
   File.WriteAllLines(settingsPath,new[]{modes[1].ToString(),modes[2].ToString(),modes[3].ToString(),paused?"1":"0"});
  } catch(Exception ex){MessageBox.Show(T("Could not save settings: ","Ayarlar kaydedilemedi: ")+ex.Message,"Sideklick");}
 }
 static bool StartupEnabled(){
  try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){return key!=null&&key.GetValue("Sideklick")!=null;}}catch{return false;}
 }
 static void SetStartup(bool on){
  try {
   using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){
    string value="\""+Application.ExecutablePath+"\" --quiet";
    if(on){if(!value.Equals(key.GetValue("Sideklick") as string))key.SetValue("Sideklick",value);}
    else key.DeleteValue("Sideklick",false);
   }
  } catch(Exception ex){MessageBox.Show(T("Could not update startup: ","Başlangıç ayarı değiştirilemedi: ")+ex.Message,"Sideklick");}
  Sync();
 }
 static void SetMode(int b,int m){if(modes[b]==m)return;Release();modes[b]=m;Save();Sync();}
 static void SetPaused(bool p){if(paused==p)return;Release();paused=p;Save();Sync();}

 // ---------- names ----------
 static string Name(int b){return b==3?T("Wheel click","Tekerlek tıklaması"):T("Side button ","Yan tuş ")+b;}
 static string Detail(int b){
  if(b==1)return T("Mouse 4 · usually Back","Mouse 4 · genelde Geri");
  if(b==2)return T("Mouse 5 · usually Forward","Mouse 5 · genelde İleri");
  return T("Mouse 3 · middle button","Mouse 3 · orta tuş");
 }
 static string ModeName(int b,int m){return m==0?T("Normal","Normal"):m==1?T("Block","Engelle"):"F"+(12+b);}
 static string ModeMenuName(int b,int m){
  return m==0?T("Normal (Back/Forward)","Normal (Geri/İleri)"):m==1?T("Block - keep Discord PTT","Engelle - Discord PTT aynen kalır"):T("Act as ","Şuna dönüştür: ")+"F"+(12+b);
 }

 // ---------- tray ----------
 static void LoadIcons(){
  Icon source=null;
  try{var s=typeof(Program).Assembly.GetManifestResourceStream("Sideklick.ico");if(s!=null)source=new Icon(s);}catch{}
  if(source==null)source=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
  windowIcon=source;
  // The .ico frame is PNG-compressed, which Icon.ToBitmap can't read on .NET 4, so draw from the PNG instead.
  try{var s=typeof(Program).Assembly.GetManifestResourceStream("icon.png");if(s!=null)logoImage=new Bitmap(s);}catch{}
  if(logoImage==null)logoImage=source.ToBitmap();
  var size=SystemInformation.SmallIconSize;
  trayIcon=MakeIcon(logoImage,size,false);
  trayPausedIcon=MakeIcon(logoImage,size,true);
 }
 static Icon MakeIcon(Bitmap src,Size size,bool gray){
  using(var bmp=new Bitmap(size.Width,size.Height))
  using(var g=Graphics.FromImage(bmp)){
   g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
   var attr=new ImageAttributes();
   if(gray)attr.SetColorMatrix(new ColorMatrix(new[]{
    new[]{.3f,.3f,.3f,0,0},new[]{.59f,.59f,.59f,0,0},new[]{.11f,.11f,.11f,0,0},new[]{0,0,0,.55f,0},new[]{0,0,0,0,1f}}));
   g.DrawImage(src,new Rectangle(0,0,size.Width,size.Height),0,0,src.Width,src.Height,GraphicsUnit.Pixel,attr);
   return Icon.FromHandle(bmp.GetHicon());
  }
 }
 static void BuildTray(){
  var menu=new ContextMenuStrip();
  var open=new ToolStripMenuItem(T("Open Sideklick","Sideklick'i aç"));
  open.Font=new Font(open.Font,FontStyle.Bold);
  open.Click+=delegate{ShowWindow();};
  menu.Items.Add(open);
  menu.Items.Add(new ToolStripSeparator());
  for(int b=1;b<=3;b++){
   var sub=new ToolStripMenuItem(Name(b));
   for(int m=0;m<3;m++){
    int bb=b,mm=m;
    var item=new ToolStripMenuItem(ModeMenuName(b,m));
    item.Click+=delegate{SetMode(bb,mm);};
    menuModes[b,m]=item;sub.DropDownItems.Add(item);
   }
   menu.Items.Add(sub);
  }
  menu.Items.Add(new ToolStripSeparator());
  menuPause=new ToolStripMenuItem(T("Pause","Duraklat"));
  menuPause.Click+=delegate{SetPaused(!paused);};
  menuStartup=new ToolStripMenuItem(T("Start with Windows","Windows ile başlat"));
  menuStartup.Click+=delegate{SetStartup(!StartupEnabled());};
  menu.Items.Add(menuPause);menu.Items.Add(menuStartup);
  menu.Items.Add(new ToolStripSeparator());
  var exit=new ToolStripMenuItem(T("Exit","Çıkış"));
  exit.Click+=delegate{Application.Exit();};
  menu.Items.Add(exit);
  menu.Opening+=delegate{Sync();};

  tray=new NotifyIcon{ContextMenuStrip=menu,Visible=true};
  tray.MouseClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left)ShowWindow();};
  Sync();
 }

 // Pushes the current state into the tray menu and the window.
 static void Sync(){
  if(tray==null)return;
  syncing=true;
  try {
   bool startup=StartupEnabled();
   tray.Icon=paused?trayPausedIcon:trayIcon;
   tray.Text=paused?T("Sideklick - paused","Sideklick - duraklatıldı"):T("Sideklick - active","Sideklick - aktif");
   menuPause.Text=paused?T("Resume","Devam et"):T("Pause","Duraklat");
   menuStartup.Checked=startup;
   for(int b=1;b<=3;b++)for(int m=0;m<3;m++)menuModes[b,m].Checked=modes[b]==m;
   if(form==null||form.IsDisposed)return;
   for(int b=1;b<=3;b++)for(int m=0;m<3;m++){var r=radios[b,m];r.Checked=modes[b]==m;StyleSegment(r);}
   startupBox.Checked=startup;
   statusLabel.Text=paused?T("● Paused - buttons behave normally","● Duraklatıldı - tuşlar normal çalışıyor"):T("● Active","● Aktif");
   statusLabel.ForeColor=paused?Warn:Good;
   pauseButton.Text=paused?T("Resume","Devam et"):T("Pause","Duraklat");
  } finally {syncing=false;}
 }

 // ---------- window ----------
 static Button FlatButton(string text,int x,int y,int w,int h){
  var b=new Button{Text=text,Left=x,Top=y,Width=w,Height=h,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Ink,Cursor=Cursors.Hand,TabStop=true};
  b.FlatAppearance.BorderColor=Line;
  return b;
 }
 static void StyleSegment(RadioButton r){
  r.BackColor=r.Checked?Accent:Color.White;
  r.ForeColor=r.Checked?Color.White:Ink;
  r.FlatAppearance.BorderColor=r.Checked?Accent:Line;
 }
 static void ShowWindow(){
  if(form!=null&&!form.IsDisposed){
   if(form.WindowState==FormWindowState.Minimized)form.WindowState=FormWindowState.Normal;
   form.Show();form.Activate();Sync();return;
  }
  const int W=560, pad=24;
  form=new Form{Text="Sideklick",ClientSize=new Size(W,560),BackColor=Back,Font=new Font("Segoe UI",10f),
   StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedSingle,MaximizeBox=false,Icon=windowIcon,KeyPreview=true};

  var logo=new PictureBox{Left=pad,Top=22,Width=44,Height=44,SizeMode=PictureBoxSizeMode.Zoom,Image=logoImage};
  var title=new Label{Text="Sideklick",Left=pad+54,Top=18,AutoSize=true,Font=new Font("Segoe UI Semibold",18f),ForeColor=Ink};
  statusLabel=new Label{Left=pad+56,Top=50,Width=300,Height=20,Font=new Font("Segoe UI",9f)};
  pauseButton=FlatButton("",W-pad-110,26,110,34);
  pauseButton.Click+=delegate{SetPaused(!paused);};

  var intro=new Label{Text=T("Choose what each button does. Changes apply and save instantly.\nPress a mouse button while this window is open to find its row.",
   "Her tuşun ne yapacağını seç. Değişiklikler anında uygulanır ve kaydedilir.\nPencere açıkken bir fare tuşuna basarsan satırı yanıp söner."),
   Left=pad,Top=84,Width=W-2*pad,Height=42,ForeColor=Muted,Font=new Font("Segoe UI",9.5f)};
  form.Controls.AddRange(new Control[]{logo,title,statusLabel,pauseButton,intro});

  int y=138;
  for(int b=1;b<=3;b++){
   int bb=b;
   var row=new Panel{Left=pad,Top=y,Width=W-2*pad,Height=72,BackColor=Color.White};
   row.Paint+=delegate(object s,PaintEventArgs e){
    bool flash=flashUntil[bb]!=DateTime.MinValue;
    var rect=new Rectangle(0,0,row.Width-1,row.Height-1);
    if(flash)using(var fill=new SolidBrush(Color.FromArgb(232,240,255)))e.Graphics.FillRectangle(fill,rect);
    using(var pen=new Pen(flash?Accent:Line,flash?2f:1f))e.Graphics.DrawRectangle(pen,flash?new Rectangle(1,1,row.Width-2,row.Height-2):rect);
   };
   var name=new Label{Text=Name(b),Left=16,Top=14,AutoSize=true,Font=new Font("Segoe UI Semibold",11f),ForeColor=Ink,BackColor=Color.Transparent};
   var detail=new Label{Text=Detail(b),Left=16,Top=38,AutoSize=true,Font=new Font("Segoe UI",9f),ForeColor=Muted,BackColor=Color.Transparent};
   row.Controls.Add(name);row.Controls.Add(detail);
   int segW=86,x=row.Width-16-3*segW;
   for(int m=0;m<3;m++){
    int mm=m;
    var r=new RadioButton{Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter,
     Text=ModeName(b,m),Left=x+m*segW-(m>0?m:0),Top=18,Width=segW,Height=36,Cursor=Cursors.Hand,Font=new Font("Segoe UI",9.5f)};
    r.FlatAppearance.CheckedBackColor=Accent;
    r.FlatAppearance.MouseOverBackColor=Color.FromArgb(238,243,252);
    r.CheckedChanged+=delegate{StyleSegment(r);if(!syncing&&r.Checked)SetMode(bb,mm);};
    radios[b,m]=r;row.Controls.Add(r);
   }
   rows[b]=row;form.Controls.Add(row);
   y+=84;
  }

  var help=new Label{Text=T("Block: Back/Forward stop working, your Discord push-to-talk bind keeps working.\nF13 / F14 / F15: the button acts as that key. Rebind push-to-talk in Discord.\nPause turns everything off until you resume.",
   "Engelle: Geri/İleri çalışmaz, Discord bas-konuş tuşun aynen çalışmaya devam eder.\nF13 / F14 / F15: Tuş o klavye tuşu gibi davranır. Discord'da bas-konuş'u yeniden ata.\nDuraklat: Devam et diyene kadar her şeyi kapatır."),
   Left=pad,Top=y+2,Width=W-2*pad,Height=62,ForeColor=Muted,Font=new Font("Segoe UI",9f)};
  y+=76;
  var sep=new Label{Left=pad,Top=y,Width=W-2*pad,Height=1,BackColor=Line};
  y+=16;
  startupBox=new CheckBox{Text=T("Start with Windows","Windows açılınca otomatik başlat"),Left=pad,Top=y+4,Width=320,Height=26,ForeColor=Ink};
  startupBox.CheckedChanged+=delegate{if(!syncing)SetStartup(startupBox.Checked);};
  var exit=FlatButton(T("Exit","Çıkış"),W-pad-110,y,110,34);
  exit.Click+=delegate{Application.Exit();};
  var trayHint=new Label{Text=T("Closing this window keeps Sideklick running in the tray (next to the clock).",
   "Pencereyi kapatınca Sideklick saatin yanındaki tepside çalışmaya devam eder."),
   Left=pad,Top=y+40,Width=W-2*pad,Height=20,ForeColor=Muted,Font=new Font("Segoe UI",8.5f)};
  form.Controls.AddRange(new Control[]{help,sep,startupBox,exit,trayHint});
  form.ClientSize=new Size(W,y+72);

  form.Activated+=delegate{windowActive=true;};
  form.Deactivate+=delegate{windowActive=false;};
  form.VisibleChanged+=delegate{if(!form.Visible)windowActive=false;};
  form.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape)form.Close();};
  form.FormClosing+=delegate(object s,FormClosingEventArgs e){
   if(e.CloseReason!=CloseReason.UserClosing)return;
   e.Cancel=true;windowActive=false;form.Hide();
   if(!hintShown){hintShown=true;tray.ShowBalloonTip(3000,"Sideklick",T("Still running in the tray. Click the icon to change settings.","Tepside çalışmaya devam ediyor. Ayarlar için simgeye tıkla."),ToolTipIcon.Info);}
  };
  Sync();
  form.Show();form.Activate();
 }

 // ---------- mouse hook ----------
 // Runs on the UI thread; keep it fast and only touch flags/arrays.
 static IntPtr Mouse(int n,IntPtr w,IntPtr l){
  if(n>=0){
   int msg=w.ToInt32(),b=0;bool down=msg==0x20B||msg==0x207;
   if(msg==0x20B||msg==0x20C){var d=(Data)Marshal.PtrToStructure(l,typeof(Data));int side=(int)(d.mouseData>>16);if(side==1||side==2)b=side;}
   else if(msg==0x207||msg==0x208)b=3;
   if(b>0){
    if(down){
     if(windowActive){pressed=b;swallowed[b]=true;return new IntPtr(1);}
     if(paused||modes[b]==0)return CallNextHookEx(hook,n,w,l);
     if(modes[b]==2&&!held[b]){keybd_event((byte)(0x7B+b),0,0,UIntPtr.Zero);held[b]=true;}
     swallowed[b]=true;return new IntPtr(1);
    }
    // Always swallow the release of a press we swallowed, even if the mode changed in between.
    if(swallowed[b]){
     swallowed[b]=false;
     if(held[b]){keybd_event((byte)(0x7B+b),0,2,UIntPtr.Zero);held[b]=false;}
     return new IntPtr(1);
    }
   }
  }
  return CallNextHookEx(hook,n,w,l);
 }
 static void Release(){for(int i=1;i<=3;i++)if(held[i]){keybd_event((byte)(0x7B+i),0,2,UIntPtr.Zero);held[i]=false;}}
}
